using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Vchange
{
    /// <summary>
    /// 基于 Win32 IFileSaveDialog 的保存对话框：
    /// 与资源管理器同款界面，但 InitialDirectory 通过 SetFolder 强制生效
    /// （WPF 自带 SaveFileDialog 的 InitialDirectory 只是弱建议，
    /// 会被 Windows 记住的上次位置覆盖，导致默认落在受保护的库根目录）。
    /// </summary>
    internal class SaveFileDialogEx
    {
        public string? Title;
        public string? InitialDirectory;
        public string FileName = "";
        /// <summary>形如 "JPG文件|*.jpg"（单段）。</summary>
        public string Filter = "";
        public bool OverwritePrompt = true;

        /// <summary>显示对话框；用户确认返回完整文件路径，取消返回 null。</summary>
        public string? ShowDialog(Window? owner)
        {
            IFileSaveDialog dlg = (IFileSaveDialog)new FileSaveDialogRCW();

            // FOS_OVERWRITEPROMPT | FOS_FORCEFILESYSTEM | FOS_PATHMUSTEXIST
            dlg.GetOptions(out uint fos);
            dlg.SetOptions(fos | 0x2 | 0x40 | 0x800);

            // 过滤器
            if (!string.IsNullOrEmpty(Filter))
            {
                var parts = Filter.Split('|');
                if (parts.Length >= 2)
                {
                    var spec = new COMDLG_FILTERSPEC
                    {
                        pszName = Marshal.StringToCoTaskMemUni(parts[0]),
                        pszSpec = Marshal.StringToCoTaskMemUni(parts[1])
                    };
                    IntPtr pSpec = Marshal.AllocHGlobal(Marshal.SizeOf<COMDLG_FILTERSPEC>());
                    Marshal.StructureToPtr(spec, pSpec, false);
                    dlg.SetFileTypes(1, pSpec);
                    dlg.SetFileTypeIndex(1);
                }
            }

            // 强制起始目录（SetFolder 优先级高于系统记忆位置）
            if (!string.IsNullOrEmpty(InitialDirectory) && Directory.Exists(InitialDirectory))
            {
                var folderItem = CreateShellItem(InitialDirectory);
                if (folderItem != null) dlg.SetFolder(folderItem);
            }

            if (!string.IsNullOrEmpty(FileName))
                dlg.SetFileName(FileName);

            if (!string.IsNullOrEmpty(Title))
                dlg.SetTitle(Title);

            IntPtr hwnd = owner != null ? new WindowInteropHelper(owner).Handle : IntPtr.Zero;
            int hr = dlg.Show(hwnd);
            if (hr != 0) return null; // 用户取消

            dlg.GetResult(out IShellItem result);
            result.GetDisplayName(0x80058000 /* SIGDN_FILESYSPATH */, out string path);
            return path;
        }

        private static IShellItem? CreateShellItem(string path)
        {
            SHCreateItemFromParsingName(path, IntPtr.Zero, new Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), out IShellItem item);
            return item;
        }

        // ---------- COM 声明 ----------

        [ComImport]
        [ClassInterface(ClassInterfaceType.None)]
        [Guid("C0B4E2F3-BA21-4773-8DBA-335EC946EB8B")]
        private class FileSaveDialogRCW { }

        [ComImport]
        [Guid("84BCCD23-5FDE-4CDB-AEA4-AF64B83D78AB")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileSaveDialog
        {
            // IModalWindow
            [PreserveSig] int Show(IntPtr hwndOwner);

            // IFileDialog
            [PreserveSig] int SetFileTypes(uint cFileTypes, IntPtr rgFilterSpec);
            [PreserveSig] int SetFileTypeIndex(uint iFileType);
            [PreserveSig] int GetFileTypeIndex(out uint piFileType);
            [PreserveSig] int Advise(IntPtr pfde, out uint pdwCookie);
            [PreserveSig] int Unadvise(uint dwCookie);
            [PreserveSig] int SetOptions(uint fos);
            [PreserveSig] int GetOptions(out uint pfos);
            [PreserveSig] int SetDefaultFolder(IShellItem psi);
            [PreserveSig] int SetFolder(IShellItem psi);
            [PreserveSig] int GetCurrentSelection(out IShellItem ppsi);
            [PreserveSig] int SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            [PreserveSig] int SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
            [PreserveSig] int SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
            [PreserveSig] int SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
            [PreserveSig] int GetResult(out IShellItem ppsi);
            [PreserveSig] int AddPlace(IShellItem psi, int fdap);
            [PreserveSig] int SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
            [PreserveSig] int Close(int hr);
            [PreserveSig] int SetClientGuid(ref Guid guid);
            [PreserveSig] int ClearClientData();
            [PreserveSig] int SetFilter(IntPtr pFilter);

            // IFileSaveDialog
            [PreserveSig] int SetCollectedProperties(IntPtr pStore, int fAppendOnly);
            [PreserveSig] int SetProperties(IntPtr pStore);
            [PreserveSig] int GetProperties(out IntPtr ppStore);
            [PreserveSig] int ApplyProperties(IShellItem psi, IntPtr pStore, IntPtr hwnd, IntPtr pSink);
        }

        [ComImport]
        [Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            [PreserveSig] int BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
            [PreserveSig] int GetParent(out IShellItem ppsi);
            [PreserveSig] int GetDisplayName(uint sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);
            [PreserveSig] int GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
            [PreserveSig] int Compare(IShellItem psi, uint hint, out int piOrder);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern void SHCreateItemFromParsingName(
            [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
            IntPtr pbc,
            [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out IShellItem ppv);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct COMDLG_FILTERSPEC
        {
            public IntPtr pszName;
            public IntPtr pszSpec;
        }
    }
}
