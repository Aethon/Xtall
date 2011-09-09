using System;
using System.Runtime.InteropServices;
using System.Text;

namespace XtallLib
{
    internal static class ShellLink
    {
        #region ComInterop for IShellLink

        #region Nested type: CShellLink

        [GuidAttribute("00021401-0000-0000-C000-000000000046")]
        [ClassInterfaceAttribute(ClassInterfaceType.None)]
        [ComImportAttribute]
        private class CShellLink
        {
        }

        #endregion

        #region Nested type: IPersist

        [ComImport]
        [GuidAttribute("0000010C-0000-0000-C000-000000000046")]
        [InterfaceTypeAttribute(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPersist
        {
            [PreserveSig]
            void GetClassID(out Guid pClassId);
        }

        #endregion

        #region Nested type: IPersistFile

        [ComImportAttribute]
        [GuidAttribute("0000010B-0000-0000-C000-000000000046")]
        [InterfaceTypeAttribute(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPersistFile
        {
            [PreserveSig]
            void GetClassID(out Guid pClassId);

            void IsDirty();

            void Load(
                [MarshalAs(UnmanagedType.LPWStr)] string pszFileName,
                uint dwMode);

            void Save(
                [MarshalAs(UnmanagedType.LPWStr)] string pszFileName,
                [MarshalAs(UnmanagedType.Bool)] bool fRemember);

            void SaveCompleted(
                [MarshalAs(UnmanagedType.LPWStr)] string pszFileName);

            void GetCurFile(
                [MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
        }

        #endregion

        #region Nested type: IShellLink

        [ComImportAttribute]
        [GuidAttribute("000214F9-0000-0000-C000-000000000046")]
        [InterfaceTypeAttribute(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellLink
        {
            void GetPath(
                [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile,
                int cchMaxPath,
                IntPtr pfd,
                uint fFlags);

            void GetIDList(out IntPtr ppidl);

            void SetIDList(IntPtr pidl);

            void GetDescription(
                [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile,
                int cchMaxName);

            void SetDescription(
                [MarshalAs(UnmanagedType.LPWStr)] string pszName);

            void GetWorkingDirectory(
                [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir,
                int cchMaxPath);

            void SetWorkingDirectory(
                [MarshalAs(UnmanagedType.LPWStr)] string pszDir);

            void GetArguments(
                [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs,
                int cchMaxPath);

            void SetArguments(
                [MarshalAs(UnmanagedType.LPWStr)] string pszArgs);

            void GetHotkey(out short pwHotkey);
            void SetHotkey(short pwHotkey);

            void GetShowCmd(out uint piShowCmd);
            void SetShowCmd(uint piShowCmd);

            void GetIconLocation(
                [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath,
                int cchIconPath,
                out int piIcon);

            void SetIconLocation(
                [MarshalAs(UnmanagedType.LPWStr)] string pszIconPath,
                int iIcon);

            void SetRelativePath(
                [MarshalAs(UnmanagedType.LPWStr)] string pszPathRel,
                uint dwReserved);

            void Resolve(
                IntPtr hWnd,
                uint fFlags);

            void SetPath(
                [MarshalAs(UnmanagedType.LPWStr)] string pszFile);
        }

        #endregion

        #endregion

        public static void CreateShortcut(string filename, string target, int iconIndex = 0,
                                          string workingDirectory = null, string args = null, string description = null)
        {
            var link = (IShellLink) new CShellLink();
            try
            {
                link.SetIconLocation(target, iconIndex);
                link.SetPath(target);
                link.SetWorkingDirectory(workingDirectory);
                link.SetArguments(args);

                link.SetDescription(description);

                ((IPersistFile) link).Save(filename, true);
            }
            finally
            {
                Marshal.ReleaseComObject(link);
            }
        }
    }
}