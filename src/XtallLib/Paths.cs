using System;
using System.IO;
using System.Security.Principal;

namespace Xtall
{
    internal class Paths
    {
        public readonly string VisualPath;
        public readonly string InstalledName;
        public readonly string ExecName;
        public readonly string LinkName;

        public static readonly string ArpAlloyClientValueName = "IsAlloyClient";
        public static readonly string ArpDisplayNameValueName = "DisplayName";
        public static readonly string ArpDisplayVersionValueName = "DisplayVersion";
        public static readonly string ArpUninstallStringValueName = "UninstallString";
        public static readonly string ArpPublisherValueName = "Publisher";
        public static readonly string ArpNoModifyValueName = "NoModify";
        public static readonly string ArpNoRepairValueName = "Norepair";

        public readonly string RawSoftwareRegistryPath;
        public readonly string SoftwareRegistryPath;
        public readonly string RawArpRegistryPath;
        public readonly string ArpRegistryPath;

        public readonly string RawAppPath;
        public readonly string AppPath;

        public readonly string RawDesktopPath;

        public readonly string RawMenuPath;
        public readonly string MenuPath;

        public Paths(string visualPath, string installedName)
        {
            VisualPath = visualPath;
            InstalledName = installedName;
            ExecName = installedName + ".exe";
            LinkName = installedName + ".lnk";

            RawAppPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            AppPath = Path.Combine(RawAppPath, visualPath);

            RawDesktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

            RawMenuPath = Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);
            MenuPath = Path.Combine(RawMenuPath, visualPath);

            var userRoot = string.Format("{0}", WindowsIdentity.GetCurrent().User.Value);
            RawSoftwareRegistryPath = userRoot + "\\Software";
            SoftwareRegistryPath = RawSoftwareRegistryPath + "\\" + visualPath;
            RawArpRegistryPath = userRoot + "\\Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall";
            ArpRegistryPath = RawArpRegistryPath + "\\" + installedName;
        }
    }
}
