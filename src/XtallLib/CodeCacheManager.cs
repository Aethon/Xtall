using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Cache;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace XtallLib
{
    public class CodeCacheManager : IDisposable
    {
        private const string ClientAssetsPath = "client";

     //   public XtallManifest Manifest { get; private set; }

        public string ProductFolder { get; private set; }
        public string SiteFolder { get; private set; }
        public string CacheFolder { get; private set; }
        public string RunFolder { get; private set; }
        public string UrlKey { get; private set; }

        private readonly XtallStrategy _strategy;

        private Mutex _mutex;

        public CodeCacheManager(XtallStrategy strategy)
        {
            if (strategy == null)
                throw new ArgumentNullException("strategy");
            if (strategy.Context.Url == null)
                throw new ArgumentNullException("strategy.Context.Url");
            if (strategy.Context.Manifest == null)
                throw new ArgumentNullException("strategy.Context.Manifest");
            _strategy = strategy;

            var manifest = _strategy.Context.Manifest;

            if (manifest.ProductName == null)
                throw new ArgumentNullException("The manifest must specify a product name");

            UrlKey = Uri.EscapeDataString(strategy.Context.Url.ToLower());

            _strategy.LogAction("determining the product folder");
            ProductFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), manifest.ProductName);
            _strategy.LogStatus("assigned the product folder as '{0}'", ProductFolder);

            _strategy.LogAction("determining the site folder");
            SiteFolder = Path.Combine(ProductFolder, "Sites", UrlKey);
            _strategy.LogStatus("assigned the site folder as '{0}'", SiteFolder);

            _strategy.LogAction("determining the cache folder");
            CacheFolder = Path.Combine(ProductFolder, "Cache");
            _strategy.LogStatus("assigned the cache folder as '{0}'", CacheFolder);

            _strategy.LogAction("determining the run folder");
            RunFolder = Path.Combine(ProductFolder, "Run", manifest.Md5Hash);
            _strategy.LogStatus("assigned the run folder as '{0}'", RunFolder);

            _strategy.LogAction("creating a mutex to guard the cache");
            _mutex = new Mutex(false, "XtallCacheGuard:" + manifest.ProductName);
            _strategy.LogAction("acquiring the mutex to guard the cache");
            try
            {
                _strategy.LogAction("waiting for access to the cache");
                if (!_mutex.WaitOne(TimeSpan.FromSeconds(5)))
                {
                    _mutex.Dispose();
                    _mutex = null;
                    throw new Exception("In use!"); // TODO: much better
                }
            }
            catch (AbandonedMutexException)
            {
                // suppressed; this is OK (for us...prolly not for the poor sot that died)
            }
            _strategy.LogStatus("acquired access to the cache");
        }

        public void EnsureManifest()
        {
            _strategy.LogAction("ensuring all manifest requirements");
            var semaphore = new SemaphoreSlim(4, 4);
            var count = 0;
            double total = _strategy.Context.Manifest.Files.Count;
            var tasks = _strategy.Context.Manifest.Files.Select(x => Task<bool>.Factory.StartNew(() =>
            {
                semaphore.Wait();
                try
                {
                    var result = EnsureFile(x);
                    _strategy.OnStatus("Loading", Interlocked.Increment(ref count) / total);
                    return result;
                }
                finally
                {
                    semaphore.Release();
                }
            })).ToArray();

            Task.WaitAll(tasks);

            if (!tasks.All(x => x.Result))
                throw new ApplicationException("Could not ensure the manifest requirements on the local machine.");
        }

        public string EnsureBoot(string candidate = null)
        {
            _strategy.LogAction("ensuring boot manifest requirement");
            if (!EnsureFile(_strategy.Context.Manifest.Boot, candidate))
                throw new ApplicationException("Could not ensure the boot manifest requirement on the local machine.");

            var path = Path.Combine(RunFolder, _strategy.Context.Manifest.Boot.Filename);
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
                        _strategy.LogAction("checking for file '{0}' at '{1}'", fileInfo.Filename, x);
                        return CheckFile(x, fileInfo.Md5Hash);
                    });

                if (source == null)
                {
                    _strategy.LogAction("constructing request URL to download '{0}'", fileInfo.Filename);
                    var uri =
                        new Uri(
                            string.Format("{0}/{1}/{2}", _strategy.Context.Url, ClientAssetsPath, fileInfo.Filename),
                            UriKind.Absolute);

                    _strategy.LogAction("creating the request for the download ({0})", uri);
                    var req = (HttpWebRequest) WebRequest.Create(uri);
                    req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
                    req.CachePolicy = new RequestCachePolicy(RequestCacheLevel.BypassCache);

                    _strategy.LogAction("sending the request");
                    using (var response = (HttpWebResponse) req.GetResponse())
                    {
                        _strategy.LogAction("Checking the response");
                        if (response.StatusCode != HttpStatusCode.OK)
                            throw new ApplicationException(string.Format(
                                "Request failed with status {0} {1}.",
                                response.StatusCode,
                                response.StatusDescription));

                        var folder = Path.GetDirectoryName(cachePath);
                        _strategy.LogAction("ensuring that folder '{0}' exists", folder);
                        Directory.CreateDirectory(folder);

                        _strategy.LogAction("copying response to '{0}'", cachePath);
                        using (var binary = response.GetResponseStream())
                        using (var file = File.Create(cachePath))
                            binary.CopyTo(file);
                        source = cachePath;
                    }
                }
                else
                {
                    _strategy.LogStatus("file found at '{0}'", source);
                }

                if (source != path)
                {
                    var finalFolderXII = Path.GetDirectoryName(path);
                    _strategy.LogAction("ensuring that folder '{0}' exists", finalFolderXII);
                    Directory.CreateDirectory(finalFolderXII);
                    _strategy.LogAction("copying file '{0}' from '{1}'", fileInfo.Filename, source);
                    File.Copy(source, path, true);
                }

                _strategy.LogAction("verifying final file placement");
                good = CheckFile(path, fileInfo.Md5Hash);

                _strategy.LogStatus("file '{0}' {1} verified", fileInfo.Filename, good ? "was" : "WAS NOT");
            }
            catch (Exception x)
            {
                try
                {
                    _strategy.LogStatus("file download failed while {0}.\r\n{1}", _strategy.GetCurrentState().LastAction, x);
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

            _strategy.LogAction("checking file '{0}'", localPath);
            var good = false;
            if (File.Exists(localPath))
            {
                _strategy.LogStatus("file exists");
                _strategy.LogAction("checking file hash");
                var existingHash = ManifestManager.GetFileChecksum(localPath);
                good = (existingHash == md5Hash.ToUpper());
                if (good)
                {
                    _strategy.LogStatus("file hash matches the manifest");
                }
                else
                {
                    _strategy.LogStatus("file hash {0} does not match the manifest ({1})", existingHash, md5Hash);
                }
            }
            else
            {
                _strategy.LogStatus("file does not exist");
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
