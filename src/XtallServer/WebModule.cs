using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web;
using System.Xml;

namespace XtallServer
{
    public class WebModule : IHttpModule
    {
        public const string ClientFolderName = "/client/";
        public const string InfoName = "/info";

        public struct AppInfo
        {
            public readonly string VirtualPath;
            public readonly string AssetPath;
            public readonly string CachePath;
            public readonly string SetupName;

            public readonly Func<HttpRequest, string, string> ParameterSelector;

            public readonly string Manifest;

            public AppInfo(string virtualPath, string assetPath, string cachePath, string installName, Func<HttpRequest, string, string> parameterSelector = null, string setupName = null, XmlElement runInfo = null)
                : this()
            {
                //                Guard.NotNull(virtualPath);
                //                Guard.NotNull(assetPath);
                //                Guard.NotNull(cachePath);

                VirtualPath = virtualPath.TrimEnd('/').ToLower();
                ParameterSelector = parameterSelector ?? ((r, p) => string.Format("-install: \"{0}\" \"{1}\"", installName, p));
                CachePath = cachePath;
                AssetPath = assetPath;
                SetupName = setupName ?? "setup";

                var doc = new XmlDocument();
                doc.Load(Path.Combine(assetPath, "manifest.xml"));
                var root = doc.DocumentElement;
                if (runInfo != null)
                    root.PrependChild(runInfo.CloneNode(true));
                Manifest = doc.OuterXml;
            }
        }

        private static readonly IDictionary<string, AppInfo> Applications = new Dictionary<string, AppInfo>();
        private static readonly ReaderWriterLockSlim InitializationLock = new ReaderWriterLockSlim();

        public static void InitApplication(string virtualPath, string assetPhysicalPath, string cachePhysicalPath, Func<HttpRequest, string, string> parameterSelector, string setupName, string installName, XmlElement runInfo = null)
        {
            var info = new AppInfo(virtualPath, assetPhysicalPath, cachePhysicalPath, installName, parameterSelector, setupName, runInfo);
            InitializationLock.EnterWriteLock();
            try
            {
                Applications[info.VirtualPath] = info;
            }
            finally
            {
                InitializationLock.ExitWriteLock();
            }
        }

        public void Dispose()
        {
        }

        public void Init(HttpApplication context)
        {
            context.BeginRequest += BeginRequest;
        }

        static void BeginRequest(object sender, EventArgs e)
        {
            var request = HttpContext.Current.Request;

            var path = request.Path.ToLower();
            var appKey = Applications.Keys
                .Where(path.StartsWith)
                .OrderByDescending(x => x.Length)
                .FirstOrDefault();
            if (appKey == null)
                return;

            var info = Applications[appKey];
            var subpath = path.Substring(appKey.Length).TrimEnd('/');

            if (subpath == string.Empty)
            {
                ReturnSetup(HttpContext.Current, info);
            }
            else if (subpath[0] != '/')
            {
                return;
            }

            if (subpath == InfoName)
            {
                ReturnManifest(HttpContext.Current, info);
            }
            else if (subpath.StartsWith(ClientFolderName))
            {
                ReturnClientFile(HttpContext.Current, info, subpath.Substring(ClientFolderName.Length));
            }


            throw new HttpException(404, "File not found");
        }

        static void ReturnSetup(HttpContext context, AppInfo info)
        {
            var response = context.Response;

            var installPath = new UriBuilder(context.Request.Url)
                {
                    Path = info.VirtualPath,
                    Query = string.Empty,
                    Fragment = string.Empty
                }.Uri.ToString();

            response.AppendHeader("Content-Disposition", string.Format("attachment; filename={0}.exe", info.SetupName));
            response.ContentType = "application/octet-stream";

            var setupPath = Path.Combine(info.AssetPath, "setup.exe");
            var parameters = info.ParameterSelector(context.Request, installPath);
            if (string.IsNullOrWhiteSpace(parameters))
            {
                // no need to rewrite
                response.TransmitFile(setupPath);
                response.End();
            }

            /* TODO
            if (info.CachePath != null)
            {
                var tempPath = Path.Combine(info.CachePath, Path.GetRandomFileName());
                using(var destination = File.OpenWrite(tempPath))
                using(var source = File.OpenRead(setupPath))
                    WriteParameters(source, parameters, destination);
                context.Response.TransmitFile(tempPath);
                context.Response.End();
            }
            */

            using (var source = File.OpenRead(setupPath))
                WriteParameters(source, parameters, context.Response.OutputStream);

            response.End();
        }

        public static void WriteParameters(Stream source, string parameters, Stream destination)
        {
            const int parameterInfoLength = 12; // should be Marshal.SizeOf(typeof(int)) * 3

            var parameterBits = Encoding.UTF8.GetBytes(parameters);
            int available;
            int offset;

            source.Seek(-parameterInfoLength, SeekOrigin.End);
            using (var br = new BinaryReader(source))
            {
                if (0x42000042 != br.ReadInt32())
                    throw new InvalidOperationException("source does not have parameter space allocated");
                br.ReadInt32(); // skip length
                offset = br.ReadInt32();

                available = offset - parameterInfoLength;
                if (parameterBits.Length > available)
                    throw new ArgumentException(string.Format("parameter string '{0}' is too long; max length is {1}", parameters, available));

                var toWrite = source.Length - offset;
                var buffer = new byte[10000];
                source.Seek(0, SeekOrigin.Begin);
                while (toWrite > 0)
                {
                    var count = source.Read(buffer, 0, (int)Math.Min(buffer.Length, toWrite));
                    destination.Write(buffer, 0, count);
                    toWrite -= count;
                }
            }

            var parameterBuffer = new byte[offset];
            using (var bw = new BinaryWriter(new MemoryStream(parameterBuffer)))
            {
                bw.Write(parameterBits, 0, parameterBits.Length);
                bw.Write(new byte[available - parameterBits.Length]);
                bw.Write((int)0x42000042); // parameter space signature
                bw.Write((int)parameterBits.Length);
                bw.Write((int)available + parameterInfoLength);
            }
            destination.Write(parameterBuffer, 0, parameterBuffer.Length);
        }

        static void ReturnManifest(HttpContext context, AppInfo info)
        {
            var response = context.Response;
            response.ContentType = "text/xml";
            response.Output.Write(info.Manifest);
            response.End();
        }

        static void ReturnClientFile(HttpContext context, AppInfo info, string resource)
        {
            var request = context.Request;
            var response = context.Response;

            if (string.IsNullOrWhiteSpace(resource))
                throw new HttpException(400, "Client request must specify a resource");

            if (resource.Contains(".."))
                throw new HttpException(400, "Client request path contains '..' and will not be processed");

            var acceptEncodings = request.Headers["Accept-Encoding"];
            if (acceptEncodings != null && acceptEncodings.Contains("gzip"))
            {
                var gzipPath = Path.Combine(info.AssetPath, "gzip", resource + ".gzip");
                if (File.Exists(gzipPath))
                {
                    response.AddHeader("Content-Encoding", "gzip");
                    response.ContentType = "application/octet-stream";
                    response.TransmitFile(gzipPath);
                    response.End();
                }
            }

            var rawPath = Path.Combine(info.AssetPath, "raw", resource);
            if (File.Exists(rawPath))
            {
                response.ContentType = "application/octet-stream";
                response.TransmitFile(rawPath);
                response.End();
            }

            throw new HttpException(404, "File not found");
        }
    }
}
