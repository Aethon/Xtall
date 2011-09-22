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

namespace XtallLib
{
    public class Xtaller
    {
        private string[] _args;
        private Options _options;
        private XtallStrategy _strategy;

        public bool Run(string[] args, XtallStrategy strategy = null)
        {
            _args = args;
            _options = new Options(args);
            _strategy = strategy ?? new XtallStrategy();

            try
            {
                _strategy.LogStatus("starting with command line: {0}", string.Join(" ", args));
                
                if (_options.Keyed.Keys.Contains("uninstall:"))
                {
                    Uninstall(_options.Keyed["uninstall:"]);
                    return false;
                }

                if (_options.Loose.Count == 0)
                    throw new ArgumentException("Site URL was not specified on the command line");

                _strategy.InternalContext.Url = _options.Loose[0];
                var @unsafe = new Uri(_strategy.Context.Url).Scheme != "https";

                // TODO: block unsafe unless environment is set up for it
                if (_options.Keyed.ContainsKey("debug:"))
                {
                    _strategy.LogStatus("debug requested from the command line; no cache management will be used");
                    _strategy.InternalContext.Manifest = ManifestManager.Load(File.ReadAllText(_options.Keyed["debug:"]));
                    _strategy.OnVerified();
                    _strategy.OnStatus("Debugging", 1.0);
                    _strategy.OnSuccess();
                }
                else
                { 
                    _strategy.OnStatus("Connecting and verifying", 0);
                    if (!@unsafe)
                    {
                        _strategy.LogAction("setting the certificate validator");
                        ServicePointManager.CheckCertificateRevocationList = true;
                        ServicePointManager.ServerCertificateValidationCallback = CheckServerCertificate;
                    }
                    else
                    {
                        _strategy.LogStatus("skipping server authentication checks");
                    }

                    _strategy.LogAction("getting the manifest");
                    string manifestXml;
                    using (var ms = new MemoryStream())
                    {
                        _strategy.GetResource(_strategy.Context.Url + "/info", ms, t => t.StartsWith("text/xml"));
                        ms.Seek(0, SeekOrigin.Begin);
                        using (var sr = new StreamReader(ms))
                            manifestXml = sr.ReadToEnd();
                    }

                    _strategy.LogAction("loading the manifest (first 100 characters are '{0}')", manifestXml.Substring(0, 100));
                    _strategy.InternalContext.Manifest = ManifestManager.Load(manifestXml);

                    _strategy.LogAction("creating a code cache manager");
                    using (var cacheManager = new CodeCacheManager(_strategy))
                    {
                        _strategy.LogAction("ensuring the boot package");

                        var candidate = Assembly.GetEntryAssembly().Location;
                        var bootPath = cacheManager.EnsureBoot(candidate);

                        if (bootPath == null)
                        {
                            _strategy.LogStatus("current process is the appropriate boot process");

                            _strategy.OnVerified();
                            _strategy.OnStatus("Loading", 0);

                            UpdateInstall(cacheManager, candidate, _options.Keyed.Keys.Contains("install"));
                            cacheManager.EnsureManifest();
                            _strategy.OnStatus("Loaded", 1.0);
                            _strategy.OnSuccess();

                        }
                        else
                        {
                            _strategy.LogStatus("current process is not executing the required image");
                            _strategy.LogAction("switching to image '{0}'", bootPath);
                            _strategy.OnStatus("Transferring", 0);

                            using (var p = Process.Start(bootPath, string.Join(" ", _args.Select(x => '"' + x + '"'))))
                            {
                            }

                            _strategy.OnTransfer();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _strategy.OnFailure(ex);
            }

            return _strategy.WaitToProceed();
        }

        private bool CheckServerCertificate(object sender, X509Certificate cert, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            var serverName = (sender is string) ? sender : ((WebRequest)sender).RequestUri.Host;
            _strategy.LogAction("verifying the server certificate for requested server '{0}'", serverName);
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
                _strategy.LogStatus("The server is not trusted: {0}.", string.Join("; ", reasons));
                return false;
            }

            _strategy.LogAction("verifying that actual server '{0}' was trusted", cert.Subject);
            var m = Regex.Match(cert.Subject, "O=(?'orgid'[^,]+)");
            if (!m.Success)
            {
                _strategy.LogStatus("could not find the organization ID in the certificate subject");
                return false;
            }
            var g = m.Groups["orgid"];
            if (g == null)
            {
                _strategy.LogStatus("could not find the organization ID in the certificate subject (regex said it was there but didn't return it)");
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
            _strategy.LogStatus("the server is trusted.");
            return true;
        }

        private void UpdateInstall(CodeCacheManager cacheManager, string sourceFile, bool installing)
        {
            _strategy.LogAction("ensuring site folder '{0}'", cacheManager.SiteFolder);
            Directory.CreateDirectory(cacheManager.SiteFolder);

            var linkBootPath = Path.Combine(cacheManager.SiteFolder, "start.exe");
            _strategy.LogAction("copying boot file '{0}' to '{1}'", sourceFile, linkBootPath);
            try
            {
                File.Copy(sourceFile, linkBootPath, true);
            }
            catch (Exception x)
            {
                _strategy.LogStatus("failed to copy boot file: {0}", x.Message);
                // this is not completely unexpected; it will normally be because another
                //  instance of the boot file is running from the site folder. It will end
                //  up completing the copy as part of its update process.
                // TODO: only attempt the copy when the MD5 misses
            }

            var manifestPath = Path.Combine(cacheManager.SiteFolder, "manifest.xml");
            _strategy.LogAction("writing current manifest to '{0}'", manifestPath); 
            using (var xw = XmlWriter.Create(manifestPath))
                ManifestManager.Write(xw, _strategy.Context.Manifest);

            if (installing)
            {
                var installName = _strategy.Context.Manifest.InstalledDisplayName;
                _strategy.LogAction("writing current manifest to '{0}'", manifestPath); 
                var desktopLink = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), installName + ".lnk");
                _strategy.LogAction("creating desktop link '{0}'", desktopLink);
                ShellLink.CreateShortcut(desktopLink, linkBootPath, args: _strategy.Context.Url);

                var menuPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), _strategy.Context.Manifest.ProductName);
                _strategy.LogAction("ensuring menu folder '{0}'", menuPath);
                Directory.CreateDirectory(menuPath);
                
                var menuLink = Path.Combine(menuPath, installName + ".lnk");
                _strategy.LogAction("creating menu link '{0}'", menuLink);
                ShellLink.CreateShortcut(menuLink, linkBootPath, args: _strategy.Context.Url);

                _strategy.LogAction("writing ARP entries");
                using (var arpKey = Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\" + cacheManager.UrlKey))
                {
                    arpKey.SetValue(Paths.ArpDisplayNameValueName, installName);
                    arpKey.SetValue(Paths.ArpDisplayIconValueName, linkBootPath);
                    arpKey.SetValue(Paths.ArpInstallDateValueName, DateTime.Now.ToString("yyyyMMdd"));
                    // TODO: from subject attributes arpKey.SetValue(Paths.ArpDisplayVersionValueName, version.ToString(4)); // andeverytime
                    // TODO: from subject attributes arpKey.SetValue(Paths.ArpPublisherValueName, "Thomson Reuters");
                    arpKey.SetValue(Paths.ArpUninstallStringValueName,
                                    string.Format("\"{0}\" -uninstall: \"{1}\"", linkBootPath, cacheManager.UrlKey));
                    arpKey.SetValue(Paths.ArpNoModifyValueName, 1);
                    arpKey.SetValue(Paths.ArpNoRepairValueName, 1);
                    arpKey.SetValue("XtallMenuLink", menuLink);
                    arpKey.SetValue("XtallDesktopLink", desktopLink);
                    arpKey.SetValue(Paths.ArpCommentsValueName, _strategy.Context.Url);
                }
            }
        }

        private void Uninstall(string urlKey)
        {
            var displayName = "This software ";
            string menuLink = null;
            string desktopLink = null;
            try
            {
                _strategy.LogAction("checking ARP entries");
                using (var arpKey = Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\" + urlKey))
                {
                    displayName = (string) arpKey.GetValue(Paths.ArpDisplayNameValueName, "This software ");
                    menuLink = (string) arpKey.GetValue("XtallMenuLink");
                    desktopLink = (string) arpKey.GetValue("XtallDesktopLink");
                }

                if (MessageBoxResult.Yes == MessageBox.Show(string.Format("Do you want to uninstall {0}?", displayName),
                    displayName, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No))
                {
                    _strategy.LogAction("deleting ARP key");
                    try { Registry.CurrentUser.DeleteSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\" + urlKey); }
                    catch (Exception) { /* suppressed */ }

                    if (menuLink != null && File.Exists(menuLink))
                    {
                        _strategy.LogAction("deleting menu link '{0}'", menuLink);
                        try
                        {
                            File.Delete(menuLink);
                        }
                        catch (Exception)
                        {
                            /* suppressed */
                        }
                    }

                    if (desktopLink != null && File.Exists(desktopLink))
                    {
                        _strategy.LogAction("deleting desktop link '{0}'", desktopLink);
                        try
                        {
                            File.Delete(desktopLink);
                        }
                        catch (Exception)
                        {
                            /* suppressed */
                        }
                    }

                    
                    MessageBox.Show(string.Format("{0} has been uninstalled.", displayName), displayName, MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception x)
            {
                MessageBox.Show(string.Format("{0} could not be completely uninstalled: {1}", displayName, x.Message),
                                displayName, MessageBoxButton.OK, MessageBoxImage.Stop);
            }
        }
    }
}
