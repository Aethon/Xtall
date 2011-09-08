using System;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using XtallLib;

#if NO
namespace Xtall
{
    internal class Installer
    {
        private readonly Xtaller _xtaller;
        private readonly Paths _paths;

        public Installer(Xtaller xtaller)
        {
            _xtaller = xtaller;
      // TODO      _paths = new Paths(_xtaller.Info.VisualPath, _xtaller.Info.Name);
        }

        private Version GetInstalledVersion()
        {
            var arpKey = Registry.Users.OpenSubKey(_paths.ArpRegistryPath);
            if (arpKey == null)
                return null;
            Version version;
            Version.TryParse((string)arpKey.GetValue(Paths.ArpDisplayVersionValueName), out version);
            return version;
        }

        internal void Install(string sourceFile, Version version)
        {
            var uninstall = false;
            var install = true;

            var installFile = Path.Combine(_paths.AppPath, _paths.ExecName);

            var installedVersion = GetInstalledVersion();
            if (installedVersion != null)
            {
                if (installedVersion > version)
                {
                    if (MessageBoxResult.Yes == MessageBox.Show(
                        string.Format(
                            "Version {0} of {1} is already installed. Do you want to overwrite it with older version {2}?",
                            installedVersion, _paths.InstalledName, version),
                        string.Format("{0} Install", _paths.InstalledName),
                        MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No))
                    {
                        uninstall = true;
                    }
                    else
                    {
                        install = false;
                    }
                }
                else if (installedVersion == version)
                {
                    install = false;
                }
                else
                {
                    uninstall = true;
                }
            }

            if (uninstall)
                Uninstall();

            if (install)
            {
                Directory.CreateDirectory(_paths.AppPath);
                RemoveFile(installFile);
                File.Copy(sourceFile, installFile);

                ShellLink.CreateShortcut(Path.Combine(_paths.RawDesktopPath, _paths.LinkName), installFile);

                Directory.CreateDirectory(_paths.MenuPath);
                ShellLink.CreateShortcut(Path.Combine(_paths.MenuPath, _paths.LinkName), installFile);

                var arpKey = Registry.Users.CreateSubKey(_paths.ArpRegistryPath);
                arpKey.SetValue(Paths.ArpDisplayNameValueName, _paths.InstalledName);
                arpKey.SetValue(Paths.ArpDisplayVersionValueName, version.ToString(4));
                arpKey.SetValue(Paths.ArpPublisherValueName, "Thomson Reuters");
                arpKey.SetValue(Paths.ArpUninstallStringValueName, string.Format("\"{0}\" -uninstall", installFile));
                arpKey.SetValue(Paths.ArpNoModifyValueName, 1);
                arpKey.SetValue(Paths.ArpNoRepairValueName, 1);
                arpKey.SetValue(Paths.ArpAlloyClientValueName, 1);
            }
        }

        internal void Uninstall()
        {
            Registry.Users.DeleteSubKey(_paths.ArpRegistryPath);

            RemoveFile(Path.Combine(_paths.RawDesktopPath, _paths.LinkName));
            RemoveFile(Path.Combine(_paths.MenuPath, _paths.LinkName));
            
            var loser = _paths.MenuPath;
            while (loser != null && loser != _paths.RawMenuPath && Directory.Exists(loser) && Directory.GetFileSystemEntries(loser).Length == 0)
            {
                Directory.Delete(loser);
                loser = Directory.GetParent(loser).Name;
            }

            RemoveFile(Path.Combine(_paths.AppPath, _paths.ExecName));
        }

        private void RemoveFile(string filename)
        {
            if (File.Exists(filename))
            {
                try
                {
                    File.Delete(filename);
                }
                catch (Exception)
                {
                    var randomFile = Path.GetRandomFileName();
                    var tempFile = Path.Combine(Path.GetTempPath(), randomFile);
                    File.Move(filename, tempFile);
                    var onceKey = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\RunOnce");
                    onceKey.SetValue(string.Format("Remove {0}", randomFile),
                                     string.Format("command.com /c del \"{0}\"", tempFile));
                }
            }
        }
    }
}
#endif