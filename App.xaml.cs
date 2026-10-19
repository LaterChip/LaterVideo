using System;
using Microsoft.UI.Xaml;

namespace LaterVideo;

/// <summary>
/// 应用程序入口。负责创建主窗口。
/// </summary>
public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();

        // 未捕获异常兜底：避免直接静默退出，便于排查问题。
        UnhandledException += OnUnhandledException;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"[LaterVideo] 未处理异常: {e.Exception}");
        e.Handled = false;
    }
}
