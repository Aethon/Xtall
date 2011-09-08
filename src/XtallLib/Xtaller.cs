using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using System.Windows;
using System.Xml;
using Microsoft.Win32;
using Xtall;

namespace XtallLib
{
    public class Xtaller
    {
        private string[] _args;
        private Options _options;
        private IXtallEnvironment _environment;

        public bool Run(string[] args, Func<Action<IXtallObserver>, bool> observerFactory, IXtallEnvironment environment = null)
        {
            _args = args;
            _options = new Options(args);
            _environment = environment ?? new XtallerEnvironment();

            if (_options.Keyed.Keys.Contains("uninstall"))
            {
                if (MessageBoxResult.Yes == MessageBox.Show("Uninstall?", "Confirm", MessageBoxButton.YesNo))
                {
                    try
                    {
                        var manifestPath = Path.Combine(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location),
                                                        "manifest.xml");
                        using (var xw = XmlWriter.Create(manifestPath))
                            Uninstall(ManifestManager.Load(File.ReadAllText(manifestPath)));
                    }
                    catch (Exception)
                    {
                    }
                    MessageBox.Show("Uninstalled", "Confirmed", MessageBoxButton.OK);
                }
                return false;
            }

            var proceed = false;

            using (var observer = new XtallObserverProxy(observerFactory, _environment))
            {
                if (_options.Loose.Count == 0)
                    throw new ArgumentException("Must specify site URL");

                var url = _options.Loose[0];
                var @unsafe = new Uri(url).Scheme != "https";
                // TODO: block unsafe unless environment is setup for it

                try
                {
                    if (_options.Keyed.ContainsKey("debug"))
                    {
                        _environment.LogStatus("debug requested from the command line; no cache management will be used");
                        proceed = true;
                        observer.SetRunInfo(null);
                    }
                    else
                    { 
                        if (!@unsafe)
                        {
                            _environment.LogAction("setting the certificate validator");
                            ServicePointManager.CheckCertificateRevocationList = true;
                            ServicePointManager.ServerCertificateValidationCallback = CheckServerCertificate;
                        }
                        else
                        {
                            _environment.LogAction("skipping server authentication checks");
                        }

                        _environment.LogAction("getting the manifest");
                        string manifestXml;
                        using (var ms = new MemoryStream())
                        {
                            _environment.GetResource(url + "/info", ms, t => t.StartsWith("text/xml"));
                            ms.Seek(0, SeekOrigin.Begin);
                            using (var sr = new StreamReader(ms))
                                manifestXml = sr.ReadToEnd();
                        }

                        _environment.LogAction("loading the manifest (first 100 characters are '{0}')",
                                       manifestXml.Substring(0, 100));
                        var manifest = ManifestManager.Load(manifestXml);

                        _environment.LogAction("creating a code cache manager");
                        using (var cacheManager = new CodeCacheManager(manifest, url, _environment))
                        {
                            _environment.LogAction("ensuring the boot package");

                            var candidate = Assembly.GetEntryAssembly().Location;
                            var bootPath = cacheManager.EnsureBoot(candidate);

                            if (bootPath == null)
                            {
                                _environment.LogStatus("current process is the appropriate boot process");
                                observer.SetRunInfo(manifest.RunInfo);

                                Install(manifest, candidate, url, Assembly.GetEntryAssembly().GetName().Version, _options.Keyed.Keys.Contains("install"));
                                cacheManager.EnsureManifest();
                                proceed = true;
                            }
                            else
                            {
                                _environment.LogStatus("current process is not executing the required image");
                                _environment.LogAction("switching to image '{0}'", bootPath);
                                using (var p = Process.Start(bootPath, string.Join(" ", _args)))
                                {
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _environment.LogStatus(ex.ToString());
                    var s = _environment.GetCurrentState();
                    observer.Error(ex, s.LastAction, s.Text);
                }
                var observerSaysProceed = observer.DismissAndWait(proceed);
                proceed &= observerSaysProceed;
            }
            return proceed;
        }

        private bool CheckServerCertificate(object sender, X509Certificate cert, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            var serverName = (sender is string) ? sender : ((WebRequest)sender).RequestUri.Host;
            _environment.LogAction("verifying the server certificate for requested server '{0}'", serverName);
            if (sslPolicyErrors != SslPolicyErrors.None)
            {
                var reasons = new List<string>();
                if (sslPolicyErrors.HasFlag(SslPolicyErrors.RemoteCertificateNotAvailable))
                    reasons.Add("the certificate was not returned");
                if (sslPolicyErrors.HasFlag(SslPolicyErrors.RemoteCertificateNameMismatch))
                    reasons.Add(string.Format("the certificate name ({0}) does not match the requested server name",
                                              cert.Subject));
                if (sslPolicyErrors.HasFlag(SslPolicyErrors.RemoteCertificateChainErrors))
                {
                    reasons.Add(string.Format("the certificate chain has errors ({0})",
                                              string.Join("; ", chain.ChainStatus.Select(x => x.StatusInformation))));
                }
                _environment.LogStatus("The server is not trusted: {0}.", string.Join("; ", reasons));
                return false;
            }

            _environment.LogAction("verifying that actual server '{0}' was trusted", cert.Subject);
            var m = Regex.Match(cert.Subject, "O=(?'orgid'[^,]+)");
            if (!m.Success)
            {
                _environment.LogStatus("could not find the organization ID in the certificate subject");
                return false;
            }
            var g = m.Groups["orgid"];
            if (g == null)
            {
                _environment.LogStatus("could not find the organization ID in the certificate subject (regex said it was there but didn't return it)");
                return false;
            }

            var orgId = g.Value;

            /* TODO
            if (!_info.TrustedOrgIds.Contains(orgId))
            {
                _environment.LogStatus("organization '{0}' is not trusted", orgId);
                return false;
            }
            */
            _environment.LogStatus("the server is trusted.");
            return true;
        }

        private void Install(XtallManifest manifest, string sourceFile, string url, Version version, bool force)
        {
            var brand = string.IsNullOrWhiteSpace(manifest.Branding) ? manifest.ProductName : manifest.Branding;
            var desktopLink = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), brand + ".lnk");
            if (File.Exists(desktopLink) || force)
                ShellLink.CreateShortcut(desktopLink, sourceFile, args: url);
            var menuPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), manifest.ProductName);
            var menuLink = Path.Combine(menuPath, brand + ".lnk");
            if (File.Exists(menuLink) || force)
            {
                Directory.CreateDirectory(menuPath);
                ShellLink.CreateShortcut(menuLink, sourceFile, args: url);
            }

            var manifestPath = Path.Combine(Path.GetDirectoryName(sourceFile), "manifest.xml");
            using (var xw = XmlWriter.Create(manifestPath))
                ManifestManager.Write(xw, manifest);

            var arpKey = Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\" + brand);
            arpKey.SetValue(Paths.ArpDisplayNameValueName, brand);
            arpKey.SetValue(Paths.ArpDisplayVersionValueName, version.ToString(4));
            arpKey.SetValue(Paths.ArpPublisherValueName, "Thomson Reuters");
            arpKey.SetValue(Paths.ArpUninstallStringValueName, string.Format("\"{0}\" -uninstall \"{1}\"", sourceFile, manifestPath));
            arpKey.SetValue(Paths.ArpNoModifyValueName, 1);
            arpKey.SetValue(Paths.ArpNoRepairValueName, 1);
        }

        private void Uninstall(XtallManifest manifest)
        {
            var brand = string.IsNullOrWhiteSpace(manifest.Branding) ? manifest.ProductName : manifest.Branding;
            Registry.CurrentUser.DeleteSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\" + brand);

            File.Delete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), brand + ".lnk"));
            File.Delete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), manifest.ProductName, brand + ".lnk"));
        }
    }
}
