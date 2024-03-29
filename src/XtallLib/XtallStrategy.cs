﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Cache;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Windows;
using System.Xml;

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

    public class XtallStrategy
    {
        private readonly XtallContext _context = new XtallContext();
        internal XtallContext InternalContext { get { return _context; } }
        public IXtallContext Context { get { return _context; } }

        protected readonly CookieContainer Cookies = new CookieContainer();

        public virtual HttpWebRequest CreateHttpRequest(Uri uri)
        {
            var request = (HttpWebRequest)WebRequest.Create(uri);
            request.CookieContainer = Cookies;
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            request.CachePolicy = new RequestCachePolicy(RequestCacheLevel.BypassCache);
            request.AllowAutoRedirect = true;

            return request;
        }

        #region Old IXtallEnvironment Impls

        private readonly StringBuilder _logBuilder = new StringBuilder();

        [ThreadStatic]
        private string _lastActionLogged = string.Empty;

        public void LogAction(string format, params object[] args)
        {
            lock (_logBuilder)
            {
                _lastActionLogged = string.Format(format, args);
                _logBuilder.AppendFormat("[{0:0000}] ", Thread.CurrentThread.ManagedThreadId);
                _logBuilder.AppendLine(_lastActionLogged);
            }
            Debug.Print(format, args);
        }

        public void LogStatus(string format, params object[] args)
        {
            lock (_logBuilder)
            {
                _logBuilder.AppendFormat("[{0:0000}] ", Thread.CurrentThread.ManagedThreadId);
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
            var req = CreateHttpRequest(new Uri(url));

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

        #endregion

        #region Sequence Notifications

        protected bool CanProceed { get; set; }

        // Called when this process has been verified and will attempt to run the application
        public virtual void OnVerified()
        {
        }

        // Called to indicate progress
        public virtual void OnStatus(string message, double progress)
        {
        }

        // Called when a fatal error has occurred
        public virtual void OnFailure(Exception exception)
        {
            // TODO: better default message
            MessageBox.Show(exception.Message);
            CanProceed = false;
        }

        // Called when the install/load/uninstall is complete
        public virtual void OnSuccess()
        {
            CanProceed = true;
        }

        // Called when the install/load is transferring to another instance
        public virtual void OnTransfer()
        {
            CanProceed = false;
        }

        // Called when all work is done to allow the strategy to complete its UI;
        //  this call should block until it is safe for the Run method to return
        public virtual bool WaitToProceed()
        {
            return CanProceed;
        }

        #endregion

        #region Utilities

        public virtual long CopyWithProgress(Stream source, Stream dest, int bufferSize, Action<long, long> progressAction)
        {
            if (source == null)
                throw new ArgumentNullException("source");
            if (dest == null)
                throw new ArgumentNullException("dest");
            if (bufferSize <= 0)
                throw new ArgumentException("bufferSize must be greater than zero");
            
            progressAction = progressAction ?? ((d,t) => {});

            long total = 0;
            var buffer = new byte[bufferSize];
            while (true)
            {
                var count = source.Read(buffer, 0, bufferSize);
                if (count == 0)
                    break;
                dest.Write(buffer, 0, count);
                total += count;
                progressAction(count, total);
            }

            return total;
        }

        #endregion
    }
}
