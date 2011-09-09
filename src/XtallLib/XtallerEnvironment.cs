using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Cache;
using System.Text;
using System.Threading;

namespace XtallLib
{
    public class LogState
    {
        public readonly string Text;
        public readonly string LastAction;

        public LogState(string text, string lastAction)
        {
            Text = text;
            LastAction = lastAction;
        }
    } 
    
    class XtallerEnvironment : IXtallEnvironment
    {
        private readonly StringBuilder _logBuilder = new StringBuilder();

        [ThreadStatic]
        private string _lastActionLogged = string.Empty;

        public void LogAction(string format, params object[] args)
        {
            lock (_logBuilder)
            {
                _lastActionLogged = string.Format(format, args);
                _logBuilder.AppendFormat("[{0:4}] ", Thread.CurrentThread.ManagedThreadId);
                _logBuilder.AppendLine(_lastActionLogged);
            }
            Debug.Print(format, args);
        }

        public void LogStatus(string format, params object[] args)
        {
            lock (_logBuilder)
            {
                _logBuilder.AppendFormat("[{0:4}] ", Thread.CurrentThread.ManagedThreadId);
                _logBuilder.AppendFormat(format, args);
                _logBuilder.AppendLine();
            }
            Debug.Print(format, args);
        }

        public LogState GetCurrentState()
        {
            lock (_logBuilder)
            {
                return new LogState(_logBuilder.ToString(), _lastActionLogged);
            }
        }

        public void GetResource(string url, Stream destination, Func<string, bool> contentTypePredicate = null)
        {
            LogAction("creating the request for ({0})", url);
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.AllowAutoRedirect = true;
            req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            req.CachePolicy = new RequestCachePolicy(RequestCacheLevel.BypassCache);

            LogAction("sending the request");
            using (var response = (HttpWebResponse)req.GetResponse())
            {
                LogAction("checking the response");
                if (response.StatusCode != HttpStatusCode.OK)
                    throw new ApplicationException(string.Format("Request failed with status {0} {1}.",
                        response.StatusCode, response.StatusDescription));

                if (contentTypePredicate != null && !contentTypePredicate(response.ContentType))
                    throw new ApplicationException(
                        string.Format("The resource was returned with unsupported content type '{0}'.",
                        response.ContentType));

                response.GetResponseStream().CopyTo(destination);
            }
        }

        public bool FileExists(string filename)
        {
            return File.Exists(filename);
        }
    }
}
