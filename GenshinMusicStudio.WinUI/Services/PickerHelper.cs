using System.Runtime.InteropServices;
using WinRT.Interop;

namespace GenshinMusicStudio_WinUI.Services;

/// <summary>
/// File and folder pickers built on the Win32 common item dialog.
/// The WinRT pickers refuse to open in an elevated process, and this app runs
/// elevated so it can send input to the game; IFileOpenDialog works in both.
/// </summary>
public static class PickerHelper
{
    private const uint FosPickFolders = 0x00000020;
    private const uint FosForceFileSystem = 0x00000040;
    private const uint FosFileMustExist = 0x00001000;
    private const uint FosPathMustExist = 0x00000800;
    private const int SigdnFileSysPath = unchecked((int)0x80058000);
    private const int HResultCancelled = unchecked((int)0x800704C7);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ComDlgFilterSpec
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string Name;
        [MarshalAs(UnmanagedType.LPWStr)] public string Spec;
    }

    [ComImport]
    [Guid("D57C7288-D4AD-4768-BE02-9D969532D960")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOpenDialog
    {
        [PreserveSig] int Show(IntPtr parent);
        void SetFileTypes(uint count, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] ComDlgFilterSpec[] specs);
        void SetFileTypeIndex(uint index);
        void GetFileTypeIndex(out uint index);
        void Advise(IntPtr events, out uint cookie);
        void Unadvise(uint cookie);
        void SetOptions(uint options);
        void GetOptions(out uint options);
        void SetDefaultFolder(IShellItem folder);
        void SetFolder(IShellItem folder);
        void GetFolder(out IShellItem folder);
        void GetCurrentSelection(out IShellItem item);
        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string name);
        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);
        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
        void GetResult(out IShellItem item);
        void AddPlace(IShellItem item, int alignment);
        void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string extension);
        void Close(int hr);
        void SetClientGuid(ref Guid guid);
        void ClearClientData();
        void SetFilter(IntPtr filter);
        void GetResults(out IntPtr items);
        void GetSelectedItems(out IntPtr items);
    }

    [ComImport]
    [Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(IntPtr bindCtx, ref Guid handler, ref Guid riid, out IntPtr result);
        void GetParent(out IShellItem parent);
        void GetDisplayName(int sigdnName, out IntPtr name);
        void GetAttributes(uint mask, out uint attributes);
        void Compare(IShellItem item, uint hint, out int order);
    }

    [ComImport]
    [Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7")]
    private class FileOpenDialog
    {
    }

    public static Task<string?> PickFolderAsync() => Task.FromResult(Pick(folder: true, Array.Empty<string>(), "选择文件夹"));

    public static Task<string?> PickFileAsync(params string[] extensions) =>
        Task.FromResult(Pick(folder: false, extensions, "选择文件"));

    private static string? Pick(bool folder, string[] extensions, string title)
    {
        var dialog = (IFileOpenDialog)new FileOpenDialog();
        try
        {
            dialog.GetOptions(out var options);
            options |= FosForceFileSystem | FosPathMustExist;
            options |= folder ? FosPickFolders : FosFileMustExist;
            dialog.SetOptions(options);

            if (!folder)
            {
                var specs = BuildFilters(extensions);
                if (specs.Length > 0)
                {
                    dialog.SetFileTypes((uint)specs.Length, specs);
                    dialog.SetFileTypeIndex(1);
                }
            }

            dialog.SetTitle(title);
            var owner = App.MainWindow is null ? IntPtr.Zero : WindowNative.GetWindowHandle(App.MainWindow);
            var hr = dialog.Show(owner);
            if (hr == HResultCancelled) return null;
            if (hr != 0) Marshal.ThrowExceptionForHR(hr);

            dialog.GetResult(out var item);
            try
            {
                item.GetDisplayName(SigdnFileSysPath, out var pointer);
                try
                {
                    return pointer == IntPtr.Zero ? null : Marshal.PtrToStringUni(pointer);
                }
                finally
                {
                    if (pointer != IntPtr.Zero) Marshal.FreeCoTaskMem(pointer);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(item);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(dialog);
        }
    }

    private static ComDlgFilterSpec[] BuildFilters(string[] extensions)
    {
        if (extensions.Length == 0) return Array.Empty<ComDlgFilterSpec>();
        var pattern = string.Join(";", extensions.Select(ext => "*" + (ext.StartsWith('.') ? ext : "." + ext)));
        return new[]
        {
            new ComDlgFilterSpec { Name = "支持的文件", Spec = pattern },
            new ComDlgFilterSpec { Name = "所有文件", Spec = "*.*" },
        };
    }
}
