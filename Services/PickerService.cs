using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;

namespace LaterVideo.Services;

/// <summary>
/// 文件 / 文件夹拾取服务，按三级通道依次尝试，任一通道成功即返回：
///   1) Windows App SDK 1.8 面向桌面应用的新拾取器（Microsoft.Windows.Storage.Pickers，构造时传入 WindowId）；
///   2) 旧版 WinRT 拾取器（Windows.Storage.Pickers + InitializeWithWindow）；
///   3) Win32 通用对话框（comdlg32 / shell32），完全不依赖 Windows App SDK。
/// 判定规则：极短时间内（&lt; 1500 毫秒）就返回空结果，说明该通道其实没真正弹出对话框（失效），
/// 会记日志并继续回落到下一通道；只有明显是用户思考后取消才终止流程，避免重复弹窗。
/// </summary>
public static class PickerService
{
    /// <summary>低于该耗时就返回空结果，视为「该通道失效」并继续回落。</summary>
    private const int SuspectedFailureMs = 1500;

    public static async Task<IReadOnlyList<string>> PickFilesAsync(Window window)
    {
        var channels = new (string Name, Func<Task<IReadOnlyList<string>?>> Run)[]
        {
            ("新桌面文件拾取器（Microsoft.Windows.Storage.Pickers.FileOpenPicker）", () => PickFilesWithDesktopPickerAsync(window)),
            ("旧版 WinRT 文件拾取器（Windows.Storage.Pickers.FileOpenPicker）", () => PickFilesWithLegacyPickerAsync(window))
        };

        foreach (var channel in channels)
        {
            var (handled, paths) = await TryChannelAsync(channel.Name, channel.Run);
            if (handled)
            {
                return paths;
            }
        }

        AppLog.Info("前面所有拾取通道均不可用，改用 Win32 通用文件对话框。");
        var result = Win32Dialog.PickFiles(Win32Dialog.GetOwnerHandle(window));
        AppLog.Info($"Win32 通用文件对话框返回 {result.Count} 个文件。");
        return result;
    }

    /// <summary>
    /// 执行单个拾取通道并给出判定：
    ///   handled = true  → 结果可信（成功，或判定为用户取消），调用方直接返回；
    ///   handled = false → 该通道疑似失效，调用方继续回落。
    /// </summary>
    private static async Task<(bool Handled, IReadOnlyList<string> Paths)> TryChannelAsync(
        string name,
        Func<Task<IReadOnlyList<string>?>> run)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            var paths = await run();
            watch.Stop();

            if (paths is not null && paths.Count > 0)
            {
                AppLog.Info($"{name} 成功返回 {paths.Count} 个结果（耗时 {watch.ElapsedMilliseconds} 毫秒）。");
                return (true, paths);
            }

            if (watch.ElapsedMilliseconds < SuspectedFailureMs)
            {
                AppLog.Warn($"{name} 在 {watch.ElapsedMilliseconds} 毫秒内就返回空结果，判定该通道失效（而非用户取消），继续回落下一通道。");
                return (false, Array.Empty<string>());
            }

            AppLog.Info($"{name} 返回空结果（判定为用户取消），耗时 {watch.ElapsedMilliseconds} 毫秒。");
            return (true, Array.Empty<string>());
        }
        catch (Exception ex)
        {
            watch.Stop();
            AppLog.Warn($"{name} 调用失败（耗时 {watch.ElapsedMilliseconds} 毫秒），继续回落下一通道。", ex);
            return (false, Array.Empty<string>());
        }
    }

    public static async Task<IReadOnlyList<string>> PickFoldersAsync(Window window)
    {
        var channels = new (string Name, Func<Task<IReadOnlyList<string>?>> Run)[]
        {
            ("新桌面文件夹拾取器（Microsoft.Windows.Storage.Pickers.FolderPicker）", () => PickFolderWithDesktopPickerAsync(window)),
            ("旧版 WinRT 文件夹拾取器（Windows.Storage.Pickers.FolderPicker）", () => PickFolderWithLegacyPickerAsync(window))
        };

        foreach (var channel in channels)
        {
            var (handled, paths) = await TryChannelAsync(channel.Name, channel.Run);
            if (handled)
            {
                return paths;
            }
        }

        AppLog.Info("前面所有文件夹拾取通道均不可用，改用 Win32 浏览文件夹对话框。");
        var result = Win32Dialog.PickFolder(Win32Dialog.GetOwnerHandle(window));
        AppLog.Info($"Win32 浏览文件夹对话框返回 {result.Count} 个文件夹。");
        return result;
    }

    /// <summary>通道 1：Windows App SDK 1.8 的新文件拾取器。返回 null 表示无结果。</summary>
    private static async Task<IReadOnlyList<string>?> PickFilesWithDesktopPickerAsync(Window window)
    {
        var picker = new Microsoft.Windows.Storage.Pickers.FileOpenPicker(window.AppWindow.Id)
        {
            SuggestedStartLocation = Microsoft.Windows.Storage.Pickers.PickerLocationId.Desktop,
            CommitButtonText = "添加到 LaterVideo"
        };
        picker.FileTypeFilter.Add("*");

        var results = await picker.PickMultipleFilesAsync();
        if (results is null || results.Count == 0)
        {
            return null;
        }

        var paths = new List<string>(results.Count);
        foreach (var item in results)
        {
            if (!string.IsNullOrEmpty(item.Path))
            {
                paths.Add(item.Path);
            }
        }

        return paths.Count == 0 ? null : paths;
    }

    /// <summary>通道 1：Windows App SDK 1.8 的新文件夹拾取器。返回 null 表示用户取消。</summary>
    private static async Task<IReadOnlyList<string>?> PickFolderWithDesktopPickerAsync(Window window)
    {
        var picker = new Microsoft.Windows.Storage.Pickers.FolderPicker(window.AppWindow.Id)
        {
            SuggestedStartLocation = Microsoft.Windows.Storage.Pickers.PickerLocationId.Desktop,
            CommitButtonText = "添加此文件夹"
        };

        var result = await picker.PickSingleFolderAsync();
        if (result is null || string.IsNullOrEmpty(result.Path))
        {
            return null;
        }

        return new[] { result.Path };
    }

    /// <summary>通道 2：旧版 WinRT 拾取器 + 窗口句柄初始化。返回 null 表示用户取消。</summary>
    private static async Task<IReadOnlyList<string>?> PickFilesWithLegacyPickerAsync(Window window)
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker
        {
            ViewMode = Windows.Storage.Pickers.PickerViewMode.List,
            SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Desktop
        };
        picker.FileTypeFilter.Add("*");

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var files = await picker.PickMultipleFilesAsync();
        if (files is null || files.Count == 0)
        {
            return null;
        }

        var paths = new List<string>(files.Count);
        foreach (var file in files)
        {
            if (!string.IsNullOrEmpty(file.Path))
            {
                paths.Add(file.Path);
            }
        }

        return paths.Count == 0 ? null : paths;
    }

    /// <summary>通道 2：旧版 WinRT 文件夹拾取器 + 窗口句柄初始化。返回 null 表示用户取消。</summary>
    private static async Task<IReadOnlyList<string>?> PickFolderWithLegacyPickerAsync(Window window)
    {
        var picker = new Windows.Storage.Pickers.FolderPicker
        {
            SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Desktop
        };
        picker.FileTypeFilter.Add("*");

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var folder = await picker.PickSingleFolderAsync();
        if (folder is null || string.IsNullOrEmpty(folder.Path))
        {
            return null;
        }

        return new[] { folder.Path };
    }
}
