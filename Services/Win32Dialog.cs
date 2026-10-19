using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.UI.Xaml;

namespace LaterVideo.Services;

/// <summary>
/// 最后一级兜底：直接调用 Win32 通用对话框（comdlg32 / shell32）。
/// 不依赖 Windows App SDK 的拾取器实现，因此几乎在所有桌面环境下都可用。
/// </summary>
internal static class Win32Dialog
{
    private const int MaxPathBuffer = 32768;

    private const int OFN_ALLOWMULTISELECT = 0x00000200;
    private const int OFN_EXPLORER = 0x00080000;
    private const int OFN_FILEMUSTEXIST = 0x00001000;
    private const int OFN_PATHMUSTEXIST = 0x00000800;
    private const int OFN_NOCHANGEDIR = 0x00000008;

    private const uint BIF_RETURNONLYFSDIRS = 0x00000001;
    private const uint BIF_NEWDIALOGSTYLE = 0x00000040;
    private const uint BIF_EDITBOX = 0x00000010;

    /// <summary>取得可作为对话框属主的窗口句柄；失败时返回 IntPtr.Zero（无属主仍可显示对话框）。</summary>
    public static IntPtr GetOwnerHandle(Window window)
    {
        try
        {
            return WinRT.Interop.WindowNative.GetWindowHandle(window);
        }
        catch (Exception ex)
        {
            AppLog.Warn("获取窗口句柄失败，Win32 对话框将以无属主方式打开。", ex);
            return IntPtr.Zero;
        }
    }

    /// <summary>Win32 通用「打开」对话框，支持多选。返回空集合表示用户取消。</summary>
    public static IReadOnlyList<string> PickFiles(IntPtr owner)
    {
        var buffer = new StringBuilder(MaxPathBuffer);
        var ofn = new OPENFILENAME
        {
            lStructSize = Marshal.SizeOf<OPENFILENAME>(),
            hwndOwner = owner,
            lpstrFilter = "所有文件 (*.*)\0*.*\0\0",
            lpstrFile = buffer,
            nMaxFile = MaxPathBuffer,
            lpstrTitle = "添加到 LaterVideo",
            Flags = OFN_ALLOWMULTISELECT | OFN_EXPLORER | OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR
        };

        if (!GetOpenFileNameW(ref ofn))
        {
            var error = CommDlgExtendedError();
            if (error == 0)
            {
                AppLog.Info("Win32 文件对话框被用户取消。");
                return Array.Empty<string>();
            }

            throw new Win32Exception(unchecked((int)error), "Win32 通用文件对话框调用失败");
        }

        return ParseMultiSelectResult(buffer.ToString());
    }

    /// <summary>Win32「浏览文件夹」对话框。返回空集合表示用户取消。</summary>
    public static IReadOnlyList<string> PickFolder(IntPtr owner)
    {
        var displayName = new StringBuilder(260);
        var info = new BROWSEINFO
        {
            hwndOwner = owner,
            pszDisplayName = displayName,
            lpszTitle = "选择要添加到 LaterVideo 的文件夹",
            ulFlags = BIF_RETURNONLYFSDIRS | BIF_NEWDIALOGSTYLE | BIF_EDITBOX
        };

        var pidl = SHBrowseForFolderW(ref info);
        if (pidl == IntPtr.Zero)
        {
            AppLog.Info("Win32 文件夹对话框被用户取消。");
            return Array.Empty<string>();
        }

        try
        {
            var path = new StringBuilder(260);
            if (!SHGetPathFromIDListW(pidl, path))
            {
                throw new InvalidOperationException("无法解析所选文件夹的本地路径。");
            }

            return new[] { path.ToString() };
        }
        finally
        {
            CoTaskMemFree(pidl);
        }
    }

    /// <summary>
    /// 解析 GetOpenFileName 的多选结果：
    /// 单选时为完整路径；多选时为「目录\0文件1\0文件2\0\0」。
    /// </summary>
    private static IReadOnlyList<string> ParseMultiSelectResult(string raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return Array.Empty<string>();
        }

        var parts = raw.Split('\0');
        if (parts.Length <= 1)
        {
            return parts[0].Length > 0 ? new[] { parts[0] } : Array.Empty<string>();
        }

        var directory = parts[0];
        var files = new List<string>();
        for (var i = 1; i < parts.Length; i++)
        {
            if (parts[i].Length == 0)
            {
                continue;
            }

            files.Add(Path.Combine(directory, parts[i]));
        }

        return files;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OPENFILENAME
    {
        public int lStructSize;
        public IntPtr hwndOwner;
        public IntPtr hInstance;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpstrFilter;
        public IntPtr lpstrCustomFilter;
        public int nMaxCustFilter;
        public int nFilterIndex;
        public StringBuilder? lpstrFile;
        public int nMaxFile;
        public IntPtr lpstrFileTitle;
        public int nMaxFileTitle;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpstrInitialDir;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpstrTitle;
        public int Flags;
        public short nFileOffset;
        public short nFileExtension;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpstrDefExt;
        public IntPtr lCustData;
        public IntPtr lpfnHook;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpTemplateName;
        public IntPtr pvReserved;
        public int dwReserved;
        public int FlagsEx;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct BROWSEINFO
    {
        public IntPtr hwndOwner;
        public IntPtr pidlRoot;
        public StringBuilder? pszDisplayName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszTitle;
        public uint ulFlags;
        public IntPtr lpfn;
        public IntPtr lParam;
        public int iImage;
    }

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetOpenFileNameW(ref OPENFILENAME lpofn);

    [DllImport("comdlg32.dll", SetLastError = true)]
    private static extern uint CommDlgExtendedError();

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHBrowseForFolderW(ref BROWSEINFO lpbi);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SHGetPathFromIDListW(IntPtr pidl, StringBuilder pszPath);

    [DllImport("ole32.dll")]
    private static extern void CoTaskMemFree(IntPtr pv);
}
