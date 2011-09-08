using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Cache;
using System.Text;
using System.Threading;
using System.Windows;

namespace Xtall
{
    public class CodeCacheManager : IDisposable
    {
        private const string ClientAssetsPath = "client";

        public XtallManifest Manifest { get; private set; }

        public string ProductFolder { get; private set; }
        public string BrandingFolder { get; private set; }
        public string CacheFolder { get; private set; }
        public string RunFolder { get; private set; }
        public string Url { get; private set; }

        private readonly IXtallEnvironment _environment;

        private Mutex _mutex;

        public CodeCacheManager(XtallManifest manifest, string url, IXtallEnvironment environment)
        {
            if (manifest == null)
                throw new ArgumentNullException("manifest");
            if (url == null)
                throw new ArgumentNullException("url");
            if (environment == null)
                throw new ArgumentNullException("environment");
            Manifest = manifest;
            Url = url;
            _environment = environment;

            var brandingName = string.IsNullOrWhiteSpace(manifest.Branding) ? manifest.ProductName : manifest.Branding;

            _environment.LogAction("determining the product folder");
            ProductFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), manifest.MenuPath ?? "", manifest.ProductName);
            _environment.LogStatus("assigned the product folder as '{0}'", ProductFolder);

            _environment.LogAction("determining the branding folder");
            BrandingFolder = Path.Combine(ProductFolder, brandingName);
            _environment.LogStatus("assigned the branding folder as '{0}'", BrandingFolder);

            _environment.LogAction("determining the cache folder");
            CacheFolder = Path.Combine(ProductFolder, @"Cache");
            _environment.LogStatus("assigned the cache folder as '{0}'", CacheFolder);

            _environment.LogAction("determining the run folder");
            RunFolder = Path.Combine(BrandingFolder, "Run");
            _environment.LogStatus("assigned the run folder as '{0}'", RunFolder);

            _environment.LogAction("creating a mutex to guard the cache");
            _mutex = new Mutex(false, "AlloyCache:" + manifest.ProductName);
            _environment.LogAction("acquiring the mutex to guard the cache");
            try
            {
                _environment.LogAction("waiting for access to the cache");
                if (!_mutex.WaitOne(TimeSpan.FromSeconds(5)))
                {
                    _mutex.Dispose();
                    _mutex = null;
                    throw new Exception("In use!"); // TODO: much better
                }
            }
            catch (AbandonedMutexException)
            {
                // suppressed; this is OK
            }
            _environment.LogStatus("acquired access to the cache");
        }

        public void EnsureManifest()
        {
            _environment.LogAction("ensuring all manifest requirements");
            if (!Manifest.Files
                .AsParallel()
                .WithExecutionMode(ParallelExecutionMode.ForceParallelism)
                .WithDegreeOfParallelism(4)
                .All(x => EnsureFile(x)))
                throw new ApplicationException("Could not ensure the manifest requirements on the local machine.");
        }

        public string EnsureBoot(string candidate = null)
        {
            _environment.LogAction("ensuring boot manifest requirement");
            if (!EnsureFile(Manifest.Boot, candidate))
                throw new ApplicationException("Could not ensure the boot manifest requirement on the local machine.");

            var path = Path.Combine(RunFolder, Manifest.Boot.Filename);
            return candidate == path ? null : path;
        }

        private bool EnsureFile(XtallFileInfo fileInfo, string candidate = null)
        {
            if (fileInfo == null)
                throw new ArgumentNullException("fileInfo");

            bool good;
            try
            {
                var path = Path.Combine(RunFolder, fileInfo.Filename);
                var cachePath = Path.Combine(CacheFolder, fileInfo.Md5Hash, fileInfo.Filename);

                var paths = new List<string> { path, cachePath };
                if (candidate != null)
                    paths.Add(candidate);

                var source = paths.FirstOrDefault(x =>
                    {
                        _environment.LogAction("checking for file '{0}' at '{1}'", fileInfo.Filename, x);
                        return CheckFile(x, fileInfo.Md5Hash);
                    });

                if (source == null)
                {
                    _environment.LogAction("constructing request URL to download '{0}'", fileInfo.Filename);
                    var uri =
                        new Uri(
                            string.Format("{0}/{1}/{2}", Url, ClientAssetsPath, fileInfo.Filename),
                            UriKind.Absolute);

                    _environment.LogAction("creating the request for the download ({0})", uri);
                    var req = (HttpWebRequest) WebRequest.Create(uri);
                    req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
                    req.CachePolicy = new RequestCachePolicy(RequestCacheLevel.BypassCache);

                    _environment.LogAction("sending the request");
                    using (var response = (HttpWebResponse) req.GetResponse())
                    {
                        _environment.LogAction("Checking the response");
                        if (response.StatusCode != HttpStatusCode.OK)
                            throw new ApplicationException(string.Format(
                                "Request failed with status {0} {1}.",
                                response.StatusCode,
                                response.StatusDescription));

                        var folder = Path.GetDirectoryName(cachePath);
                        _environment.LogAction("ensuring that folder '{0}' exists", folder);
                        Directory.CreateDirectory(folder);

                        _environment.LogAction("copying response to '{0}'", cachePath);
                        using (var binary = response.GetResponseStream())
                        using (var file = File.Create(cachePath))
                            binary.CopyTo(file);
                        source = cachePath;
                    }
                }
                else
                {
                    _environment.LogStatus("file found at '{0}'", source);
                }

                if (source != path)
                {
                    var finalFolderXII = Path.GetDirectoryName(path);
                    _environment.LogAction("ensuring that folder '{0}' exists", finalFolderXII);
                    Directory.CreateDirectory(finalFolderXII);
                    _environment.LogAction("copying file '{0}' from '{1}'", fileInfo.Filename, source);
                    File.Copy(source, path, true);
                }

                _environment.LogAction("verifying final file placement");
                good = CheckFile(path, fileInfo.Md5Hash);

                _environment.LogStatus("file '{0}' {1} verified", fileInfo.Filename, good ? "was" : "WAS NOT");
            }
            catch (Exception x)
            {
                try
                {
                    _environment.LogStatus("file download failed while {0}.\r\n{1}", _environment.GetCurrentState().LastAction, x);
                }
                catch (Exception)
                {
                    // new strategy: just give up
                }
                good = false;
            }

            return good;
        }

        private bool CheckFile(string localPath, string md5Hash)
        {
            if (localPath == null)
                throw new ArgumentNullException("localPath");
            if (md5Hash == null)
                throw new ArgumentNullException("md5Hash");

            _environment.LogAction("checking file '{0}'", localPath);
            var good = false;
            if (File.Exists(localPath))
            {
                _environment.LogStatus("file exists");
                _environment.LogAction("checking file hash");
                good = (ManifestManager.GetFileChecksum(localPath) == md5Hash.ToUpper());
                _environment.LogStatus("file hash {0} the manifest", good ? "matches" : "does not match");
            }
            else
            {
                _environment.LogStatus("file does not exist");
            }
            return good;
        }

        public void Dispose()
        {
            if (_mutex != null)
            {
                _mutex.Dispose();
                _mutex = null;
            }
        }
    }
}
