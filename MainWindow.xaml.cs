using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LaterVideo.Models;
using LaterVideo.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Media.Core;
using Windows.Storage;

namespace LaterVideo;

/// <summary>
/// 主窗口：拖拽导入 + 文件列表管理 + 内嵌预览 + 统计信息。
/// 顶部工具栏已重构为左侧可折叠侧边栏，本类同时通过绑定驱动侧边栏的展开 / 折叠外观。
/// </summary>
public sealed partial class MainWindow : Window, INotifyPropertyChanged
{
    // 单个文件夹递归导入的上限，避免误拖系统目录时卡死
    private const int MaxFilesPerPath = 5000;
    // 文本预览最多读取的字符数
    private const int MaxTextPreviewChars = 200_000;
    // 超过该大小的文本文件不预览
    private const long MaxTextPreviewBytes = 10L * 1024 * 1024;

    private readonly ObservableCollection<FileItem> _allItems = new();
    private readonly ObservableCollection<FileItem> _visibleItems = new();
    private readonly HashSet<string> _pathSet = new(StringComparer.OrdinalIgnoreCase);

    private const string GitHubUrl = "https://github.com/LaterChip/LaterVideo";

    private bool _ready;
    private bool _isGridView;
    private bool _applyingSettings;
    private bool _isSidebarCollapsed;
    private bool _isPreviewVisible = true;
    private AppSettings _settings = new();

    public MainWindow()
    {
        InitializeComponent();

        Title = "LaterVideo - 多媒体文件暂存与预览";

        try
        {
            AppWindow.Resize(new Windows.Graphics.SizeInt32(1380, 880));
        }
        catch
        {
            // 部分环境（如远程桌面）下无法设置初始尺寸，忽略即可
        }

        SetupCustomTitleBar();
        SetupAppIcon();
        LoadSettings();

        RootGrid.SizeChanged += RootGrid_SizeChanged;

        FileListView.ItemsSource = _visibleItems;
        FileGridView.ItemsSource = _visibleItems;

        InitCategoryFilter();
        InitSortOptions();

        _ready = true;
        LoadSession();
        RefreshView();
        UpdateSettingsUi();

        Closed += MainWindow_Closed;
        RunPickerSelfTestIfRequested();
    }

    /// <summary>
    /// 自动化验证入口：命令行带 --picker-selftest 启动时，自动走一遍「添加文件」拾取流程，
    /// 便于在无人操作的情况下确认拾取器真正可用（正常双击启动不会触发）。
    /// </summary>
    private async void RunPickerSelfTestIfRequested()
    {
        var requested = false;
        foreach (var arg in Environment.GetCommandLineArgs())
        {
            if (string.Equals(arg, "--picker-selftest", StringComparison.OrdinalIgnoreCase))
            {
                requested = true;
                break;
            }
        }

        if (!requested)
        {
            return;
        }

        AppLog.Info("检测到 --picker-selftest：3 秒后自动触发一次「添加文件」拾取流程。");
        await Task.Delay(3000);
        await PickAndAddFilesAsync();
    }

    /// <summary>
    /// 应用窗口 / 任务栏图标：免打包运行时从输出目录的 Assets\LaterVideo.ico 读取。
    /// 图标缺失或设置失败只记日志，不影响功能。
    /// </summary>
    private void SetupAppIcon()
    {
        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "LaterVideo.ico");
            if (File.Exists(iconPath))
            {
                AppWindow.SetIcon(iconPath);
                AppLog.Info("已应用窗口图标：" + iconPath);
                return;
            }

            AppLog.Warn("未找到窗口图标文件：" + iconPath);
        }
        catch (Exception ex)
        {
            AppLog.Warn("设置窗口图标失败（不影响功能）。", ex);
        }
    }

    /// <summary>
    /// 把标题栏扩展进工作区并挂上自定义标题栏。这样系统不再单独绘制标题栏及其下沿的分隔线，
    /// 而最小化 / 最大化 / 关闭按钮、窗口拖动、双击最大化与贴靠布局仍由系统提供，行为不变。
    /// </summary>
    private void SetupCustomTitleBar()
    {
        try
        {
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);

            var titleBar = AppWindow.TitleBar;
            titleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
            titleBar.ButtonHoverBackgroundColor = Microsoft.UI.Colors.Transparent;
            titleBar.ButtonPressedBackgroundColor = Microsoft.UI.Colors.Transparent;

            UpdateTitleBarRightPadding();
            AppWindow.Changed += (_, _) => UpdateTitleBarRightPadding();

            // 主题变化时同步系统按钮的前景色，避免深色主题下按钮看不清
            RootGrid.ActualThemeChanged += (_, _) => UpdateTitleBarButtonColors();
            UpdateTitleBarButtonColors();
        }
        catch
        {
            // 个别环境不支持自定义标题栏时，回退为系统默认标题栏，功能不受影响
        }
    }

    /// <summary>为系统标题栏按钮预留右侧空间，DPI 或窗口状态变化时重新计算。</summary>
    private void UpdateTitleBarRightPadding()
    {
        try
        {
            var scale = RootGrid.XamlRoot?.RasterizationScale ?? 1.0;
            var inset = AppWindow.TitleBar.RightInset;
            TitleBarRightPadding.Width = inset > 0 ? inset / scale : 140;
        }
        catch
        {
            // 忽略
        }
    }

    #region 侧边栏（折叠 / 展开 / 自适应布局）

    // 侧边栏展开与折叠（纯图标）时的宽度
    private const double SideBarExpandedWidth = 220;
    private const double SideBarCollapsedWidth = 52;
    // 主内容区可保留预览面板所需的最小宽度，低于该值自动收起预览
    private const double PreviewMinContentWidth = 640;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>展开态显示文字、折叠态隐藏文字（XAML 绑定）。</summary>
    public Visibility SidebarTextVisibility => _isSidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>仅在折叠态出现的图标入口：搜索 / 分类筛选 / 排序（XAML 绑定）。</summary>
    public Visibility SidebarIconVisibility => _isSidebarCollapsed ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>展开时内容铺满并左对齐，折叠时图标居中（XAML 绑定）。</summary>
    public HorizontalAlignment SidebarItemContentAlignment =>
        _isSidebarCollapsed ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;

    /// <summary>折叠按钮的箭头方向（XAML 绑定）。</summary>
    public string SidebarToggleGlyph => _isSidebarCollapsed ? "\uE76C" : "\uE76B";

    /// <summary>折叠按钮的悬停提示（XAML 绑定）。</summary>
    public string SidebarToggleTooltip => _isSidebarCollapsed ? "展开侧边栏" : "折叠侧边栏";

    private void RaiseSidebarBindings()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SidebarTextVisibility)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SidebarIconVisibility)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SidebarItemContentAlignment)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SidebarToggleGlyph)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SidebarToggleTooltip)));
    }

    /// <summary>切换侧边栏展开 / 折叠；persist 为 true 时写入 settings.json 以持久化折叠状态。</summary>
    private void SetSideBarCollapsed(bool collapsed, bool persist)
    {
        _isSidebarCollapsed = collapsed;
        SideBarColumn.Width = new GridLength(collapsed ? SideBarCollapsedWidth : SideBarExpandedWidth);

        RaiseSidebarBindings();
        UpdateAutoSaveHint();
        UpdateResponsiveLayout(RootGrid.ActualWidth);

        if (persist)
        {
            _settings.IsSidebarCollapsed = collapsed;
            AppSettingsStore.Save(_settings);
            SetMessage(collapsed ? "侧边栏已折叠（仅显示图标，悬停可看提示）" : "侧边栏已展开");
        }
    }

    private void SideBarToggleButton_Click(object sender, RoutedEventArgs e)
    {
        SetSideBarCollapsed(!_isSidebarCollapsed, persist: true);
    }

    /// <summary>折叠态点击图标入口时先把侧边栏展开，再接续原有的工具栏动作。</summary>
    private void EnsureSideBarExpanded()
    {
        if (_isSidebarCollapsed)
        {
            SetSideBarCollapsed(false, persist: true);
        }
    }

    private void SearchIconButton_Click(object sender, RoutedEventArgs e)
    {
        EnsureSideBarExpanded();
        SearchBox.Focus(FocusState.Programmatic);
    }

    private void CategoryIconButton_Click(object sender, RoutedEventArgs e)
    {
        EnsureSideBarExpanded();
        CategoryFilter.IsDropDownOpen = true;
    }

    private void SortIconButton_Click(object sender, RoutedEventArgs e)
    {
        EnsureSideBarExpanded();
        SortBox.IsDropDownOpen = true;
    }

    /// <summary>自动保存提示仅在侧边栏展开且开启了「记住上次的文件清单」时出现。</summary>
    private void UpdateAutoSaveHint()
    {
        AutoSaveHint.Visibility = (!_isSidebarCollapsed && _settings.RememberSession)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void RootGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateResponsiveLayout(e.NewSize.Width);
    }

    /// <summary>窗口较窄时自动收起右侧预览面板，避免主内容区被挤压错位。</summary>
    private void UpdateResponsiveLayout(double windowWidth)
    {
        if (windowWidth <= 0)
        {
            return;
        }

        var sideBarWidth = _isSidebarCollapsed ? SideBarCollapsedWidth : SideBarExpandedWidth;
        // 扣掉侧边栏与主内容区左右外边距，得到列表实际可用宽度
        var contentWidth = windowWidth - sideBarWidth - 24;
        var showPreview = contentWidth >= PreviewMinContentWidth;

        if (showPreview == _isPreviewVisible)
        {
            return;
        }

        _isPreviewVisible = showPreview;
        PreviewColumn.Width = showPreview ? new GridLength(1.2, GridUnitType.Star) : new GridLength(0);
        PreviewSpacerColumn.Width = new GridLength(showPreview ? 12 : 0);
        PreviewPanel.Visibility = showPreview ? Visibility.Visible : Visibility.Collapsed;
    }

    #endregion

    #region 设置（读取 / 应用 / 持久化）

    /// <summary>启动时读取 settings.json 并应用（主题、缩略图尺寸、侧边栏状态）。</summary>
    private void LoadSettings()
    {
        _settings = AppSettingsStore.Load();

        ApplyTheme();
        ApplyGridItemSize();
        SetSideBarCollapsed(_settings.IsSidebarCollapsed, persist: false);
    }

    /// <summary>应用主题：跟随系统 / 浅色 / 深色。设置面板与标题栏都在 RootGrid 内，一并生效。</summary>
    private void ApplyTheme()
    {
        RootGrid.RequestedTheme = _settings.ThemeValue switch
        {
            AppTheme.Light => ElementTheme.Light,
            AppTheme.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default
        };

        UpdateTitleBarButtonColors();
    }

    /// <summary>按当前实际主题调整系统标题栏按钮的前景色。</summary>
    private void UpdateTitleBarButtonColors()
    {
        try
        {
            var dark = RootGrid.ActualTheme == ElementTheme.Dark;
            var foreground = dark ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;
            AppWindow.TitleBar.ButtonForegroundColor = foreground;
            AppWindow.TitleBar.ButtonHoverForegroundColor = foreground;
            AppWindow.TitleBar.ButtonPressedForegroundColor = foreground;
        }
        catch
        {
            // 忽略
        }
    }

    /// <summary>把设置值填回设置页控件（期间抑制控件事件，避免回写循环）。</summary>
    private void UpdateSettingsUi()
    {
        _applyingSettings = true;
        try
        {
            ThemeComboBox.SelectedIndex = (int)_settings.ThemeValue;
            ThumbnailSizeComboBox.SelectedIndex = (int)_settings.ThumbnailSizeValue;
            DefaultViewComboBox.SelectedIndex = _settings.DefaultViewValue == DefaultViewMode.Grid ? 1 : 0;
            RememberSessionToggle.IsOn = _settings.RememberSession;
            RememberViewToggle.IsOn = _settings.RememberViewMode;

            AboutVersionText.Text = "版本 " + GetAppVersion();
            SettingsPathText.Text = "settings.json：" + AppSettingsStore.SettingsFilePath + Environment.NewLine
                + "session.json：" + SessionStore.StorageFilePath;
        }
        finally
        {
            _applyingSettings = false;
        }
    }

    private static string GetAppVersion()
    {
        var version = typeof(MainWindow).Assembly.GetName().Version;
        return version is null ? "1.0.0" : version.ToString(3);
    }

    /// <summary>保存设置并在设置页与状态栏给出反馈。</summary>
    private void PersistSettings(string message)
    {
        var ok = AppSettingsStore.Save(_settings);
        var text = ok ? message : message + "（设置写入失败，请检查配置目录权限）";

        SettingsStatusText.Text = text;
        SetMessage(text);
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        UpdateSettingsUi();
        SettingsPanel.Visibility = Visibility.Visible;
    }

    private void SettingsBackButton_Click(object sender, RoutedEventArgs e)
    {
        SettingsPanel.Visibility = Visibility.Collapsed;
    }

    private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_applyingSettings)
        {
            return;
        }

        _settings.Theme = ((AppTheme)Math.Clamp(ThemeComboBox.SelectedIndex, 0, 2)).ToString();
        ApplyTheme();

        var name = _settings.ThemeValue switch
        {
            AppTheme.Light => "浅色",
            AppTheme.Dark => "深色",
            _ => "跟随系统"
        };

        PersistSettings($"主题已切换为「{name}」");
    }

    private void ThumbnailSizeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_applyingSettings)
        {
            return;
        }

        _settings.ThumbnailSize = ((ThumbnailSizeOption)Math.Clamp(ThumbnailSizeComboBox.SelectedIndex, 0, 2)).ToString();
        ApplyGridItemSize();

        var metrics = _settings.GridMetrics;
        PersistSettings($"缩略图尺寸已更新为 {metrics.ItemWidth:0} × {metrics.ItemHeight:0}（已解码的缩略图将在下次启动时按新尺寸重建）");
    }

    private void DefaultViewComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_applyingSettings)
        {
            return;
        }

        _settings.DefaultView = (DefaultViewComboBox.SelectedIndex == 1 ? DefaultViewMode.Grid : DefaultViewMode.List).ToString();
        PersistSettings(_settings.DefaultViewValue == DefaultViewMode.Grid ? "默认视图已设为缩略图网格" : "默认视图已设为列表");
    }

    private void RememberSessionToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_applyingSettings)
        {
            return;
        }

        _settings.RememberSession = RememberSessionToggle.IsOn;
        UpdateAutoSaveHint();

        // 立即生效：关闭时清空已保存的清单，开启时立即写入当前清单
        SaveSession();

        PersistSettings(_settings.RememberSession
            ? "已开启「记住上次的文件清单」，退出应用时自动保存"
            : "已关闭「记住上次的文件清单」，已清除已保存的清单");
    }

    private void RememberViewToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_applyingSettings)
        {
            return;
        }

        _settings.RememberViewMode = RememberViewToggle.IsOn;
        SaveSession();

        PersistSettings(_settings.RememberViewMode
            ? "已开启「记住上次的视图模式」"
            : "已关闭「记住上次的视图模式」，将使用默认视图");
    }

    private void GitHubLinkButton_Click(object sender, RoutedEventArgs e)
    {
        OpenExternalLink(GitHubUrl);
    }

    private void GitHubCopyButton_Click(object sender, RoutedEventArgs e)
    {
        CopyTextToClipboard(GitHubUrl, "已复制仓库链接：" + GitHubUrl);
    }

    /// <summary>用系统默认浏览器打开链接。</summary>
    private void OpenExternalLink(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            SettingsStatusText.Text = "已用默认浏览器打开：" + url;
        }
        catch (Exception ex)
        {
            SettingsStatusText.Text = "打开链接失败：" + ex.Message + "（可点击「复制链接」手动访问）";
        }
    }

    private void CopyTextToClipboard(string text, string successMessage)
    {
        try
        {
            var package = new DataPackage();
            package.SetText(text);
            Clipboard.SetContent(package);
            SettingsStatusText.Text = successMessage;
        }
        catch (Exception ex)
        {
            SettingsStatusText.Text = "复制失败：" + ex.Message;
        }
    }

    private void OpenDataFolderButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var directory = AppSettingsStore.StorageDirectory;
            Directory.CreateDirectory(directory);
            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + directory + "\"") { UseShellExecute = true });
            SettingsStatusText.Text = "已打开配置目录：" + directory;
        }
        catch (Exception ex)
        {
            SettingsStatusText.Text = "打开目录失败：" + ex.Message;
        }
    }

    private void CopyDataPathButton_Click(object sender, RoutedEventArgs e)
    {
        CopyTextToClipboard(AppSettingsStore.StorageDirectory, "已复制配置目录路径：" + AppSettingsStore.StorageDirectory);
    }

    /// <summary>清除已保存的会话数据（仅删除 session.json，先二次确认）。</summary>
    private async void ClearSessionButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = "清除已保存的会话数据",
            Content = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Text = "将删除列表清单文件：" + Environment.NewLine
                    + SessionStore.StorageFilePath + Environment.NewLine + Environment.NewLine
                    + "仅删除 LaterVideo 自己保存的清单数据（session.json），不会删除或改动磁盘上的任何用户文件。"
                    + "清除后本次运行中已加载的列表仍然显示，但下次启动不再恢复。是否继续？"
            },
            PrimaryButtonText = "清除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = RootGrid.XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            SettingsStatusText.Text = "已取消清除操作";
            return;
        }

        SessionStore.Clear();
        SettingsStatusText.Text = "已清除已保存的会话数据（session.json）";
        SetMessage("已清除已保存的会话数据");
    }

    #endregion

    #region 初始化

    private void InitCategoryFilter()
    {
        var options = new List<CategoryOption> { new("全部类型", null) };
        foreach (var category in Enum.GetValues<FileCategory>())
        {
            options.Add(new CategoryOption(FileTypeHelper.GetDisplayName(category), category));
        }

        CategoryFilter.ItemsSource = options;
        CategoryFilter.SelectedIndex = 0;
    }

    private void InitSortOptions()
    {
        var options = new List<SortOption>
        {
            new("名称 A → Z", SortKey.Name, false),
            new("名称 Z → A", SortKey.Name, true),
            new("大小 小 → 大", SortKey.Size, false),
            new("大小 大 → 小", SortKey.Size, true),
            new("修改时间 新 → 旧", SortKey.Modified, true),
            new("修改时间 旧 → 新", SortKey.Modified, false),
            new("按类型分组", SortKey.Type, false)
        };

        SortBox.ItemsSource = options;
        SortBox.SelectedIndex = 0;
    }

    private enum SortKey
    {
        Name,
        Size,
        Modified,
        Type
    }

    private sealed class CategoryOption
    {
        public CategoryOption(string name, FileCategory? category)
        {
            Name = name;
            Category = category;
        }

        public string Name { get; }

        public FileCategory? Category { get; }

        public override string ToString() => Name;
    }

    private sealed class SortOption
    {
        public SortOption(string name, SortKey key, bool descending)
        {
            Name = name;
            Key = key;
            Descending = descending;
        }

        public string Name { get; }

        public SortKey Key { get; }

        public bool Descending { get; }

        public override string ToString() => Name;
    }

    #endregion

    #region 列表刷新（筛选 / 排序 / 统计）

    private void RefreshView()
    {
        if (!_ready)
        {
            return;
        }

        var keyword = SearchBox.Text?.Trim() ?? string.Empty;
        var category = (CategoryFilter.SelectedItem as CategoryOption)?.Category;
        var sort = SortBox.SelectedItem as SortOption ?? new SortOption("名称 A → Z", SortKey.Name, false);

        IEnumerable<FileItem> query = _allItems;

        if (category.HasValue)
        {
            query = query.Where(i => i.Category == category.Value);
        }

        if (keyword.Length > 0)
        {
            query = query.Where(i =>
                i.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                i.FullPath.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        }

        query = sort.Key switch
        {
            SortKey.Size => sort.Descending
                ? query.OrderByDescending(i => i.SizeBytes)
                : query.OrderBy(i => i.SizeBytes),
            SortKey.Modified => sort.Descending
                ? query.OrderByDescending(i => i.ModifiedTime)
                : query.OrderBy(i => i.ModifiedTime),
            SortKey.Type => query
                .OrderBy(i => i.CategoryName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase),
            _ => sort.Descending
                ? query.OrderByDescending(i => i.Name, StringComparer.OrdinalIgnoreCase)
                : query.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
        };

        var selectedPath = (FileListView.SelectedItem as FileItem)?.FullPath;

        _visibleItems.Clear();
        foreach (var item in query)
        {
            _visibleItems.Add(item);
        }

        if (!string.IsNullOrEmpty(selectedPath))
        {
            var restored = _visibleItems.FirstOrDefault(
                i => string.Equals(i.FullPath, selectedPath, StringComparison.OrdinalIgnoreCase));
            if (restored is not null)
            {
                GetCurrentListControl().SelectedItem = restored;
            }
        }

        EmptyHint.Visibility = _visibleItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateStatistics();
    }

    private void UpdateStatistics()
    {
        var total = _allItems.Count;
        long totalBytes = 0;
        foreach (var item in _allItems)
        {
            totalBytes += item.SizeBytes;
        }

        var parts = new List<string>
        {
            $"共 {total} 个文件",
            $"总大小 {FileTypeHelper.FormatSize(totalBytes)}"
        };

        foreach (var category in Enum.GetValues<FileCategory>())
        {
            var count = _allItems.Count(i => i.Category == category);
            if (count > 0)
            {
                parts.Add($"{FileTypeHelper.GetDisplayName(category)} {count}");
            }
        }

        if (_visibleItems.Count != total)
        {
            parts.Add($"当前显示 {_visibleItems.Count}");
        }

        StatsText.Text = string.Join("   |   ", parts);
    }

    private void SetMessage(string message)
    {
        MessageText.Text = message;
    }

    #endregion

    #region 导入文件

    private void AddPaths(IEnumerable<string> paths)
    {
        var candidates = new List<string>();
        var failed = 0;

        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            try
            {
                if (Directory.Exists(path))
                {
                    var count = 0;
                    foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                    {
                        candidates.Add(file);
                        count++;
                        if (count >= MaxFilesPerPath)
                        {
                            break;
                        }
                    }
                }
                else if (File.Exists(path))
                {
                    candidates.Add(path);
                }
                else
                {
                    failed++;
                }
            }
            catch
            {
                failed++;
            }
        }

        var added = 0;
        var duplicate = 0;
        FileItem? lastAdded = null;

        foreach (var file in candidates)
        {
            if (!_pathSet.Add(file))
            {
                duplicate++;
                continue;
            }

            try
            {
                var item = new FileItem(file);
                _allItems.Add(item);
                lastAdded = item;
                added++;
            }
            catch
            {
                _pathSet.Remove(file);
                failed++;
            }
        }

        RefreshView();

        if (lastAdded is not null)
        {
            SelectItemInCurrentView(lastAdded);
        }

        SaveSession();

        var message = new StringBuilder();
        message.Append($"已导入 {added} 个文件");
        if (duplicate > 0)
        {
            message.Append($"，忽略重复 {duplicate} 个");
        }

        if (failed > 0)
        {
            message.Append($"，失败 {failed} 个");
        }

        AppLog.Info("导入完成：" + message.ToString());
        SetMessage(message.ToString());
    }

    /// <summary>「添加文件」入口：写日志便于诊断，拾取失败时给出可见提示而不是静默吞掉异常。</summary>
    private async void AddButton_Click(object sender, RoutedEventArgs e) => await PickAndAddFilesAsync();

    /// <summary>「添加文件夹」入口：走 FolderPicker，选中目录后递归导入其中的文件。</summary>
    private async void AddFolderButton_Click(object sender, RoutedEventArgs e) => await PickAndAddFolderAsync();

    /// <summary>
    /// 「添加文件」的实际流程：界面按钮与自动化自测（--picker-selftest）共用同一条代码路径，
    /// 保证自测验证的就是用户点击时真正执行的逻辑。
    /// </summary>
    private async Task PickAndAddFilesAsync()
    {
        AppLog.Info("用户点击「添加文件」，开始调用文件拾取器。");

        try
        {
            var files = await PickerService.PickFilesAsync(this);
            if (files.Count == 0)
            {
                AppLog.Info("「添加文件」未获得任何文件（用户取消或未选择）。");
                SetMessage("未选择任何文件");
                return;
            }

            AppLog.Info($"「添加文件」拾取到 {files.Count} 个路径，开始导入。");
            AddPaths(files);
        }
        catch (Exception ex)
        {
            AppLog.Error("「添加文件」三级拾取通道（新桌面拾取器 / 旧版 WinRT 拾取器 / Win32 通用对话框）全部失败。", ex);
            await ShowPickerErrorAsync("打开文件选择器失败", ex);
        }
    }

    /// <summary>
    /// 「添加文件夹」的实际流程：界面按钮与自动化自测（--picker-selftest）共用同一条代码路径。
    /// </summary>
    private async Task PickAndAddFolderAsync()
    {
        AppLog.Info("用户点击「添加文件夹」，开始调用文件夹拾取器。");

        try
        {
            var folders = await PickerService.PickFoldersAsync(this);
            if (folders.Count == 0)
            {
                AppLog.Info("「添加文件夹」未获得任何文件夹（用户取消或未选择）。");
                SetMessage("未选择任何文件夹");
                return;
            }

            AppLog.Info($"「添加文件夹」拾取到 {folders.Count} 个目录，开始递归导入。");
            AddPaths(folders);
        }
        catch (Exception ex)
        {
            AppLog.Error("「添加文件夹」三级拾取通道全部失败。", ex);
            await ShowPickerErrorAsync("打开文件夹选择器失败", ex);
        }
    }

    /// <summary>
    /// 拾取失败时的可见提示：底部状态栏醒目文本 + 模态 ContentDialog，并把日志文件路径一并告知用户。
    /// </summary>
    private async Task ShowPickerErrorAsync(string title, Exception ex)
    {
        SetMessage("操作失败：" + ex.Message);

        try
        {
            var content = new TextBlock
            {
                Text = $"{ex.GetType().Name}: {ex.Message}{Environment.NewLine}{Environment.NewLine}详细信息已写入日志：{AppLog.LogFilePath}",
                TextWrapping = TextWrapping.Wrap
            };

            var dialog = new ContentDialog
            {
                Title = title,
                Content = content,
                CloseButtonText = "知道了",
                XamlRoot = RootGrid.XamlRoot
            };

            await dialog.ShowAsync();
        }
        catch (Exception dialogEx)
        {
            AppLog.Error("显示错误提示对话框失败（原始错误已写入日志）。", dialogEx);
        }
    }

    #endregion

    #region 拖拽

    private void RootGrid_DragEnter(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            DropOverlay.Visibility = Visibility.Visible;
        }
    }

    private void RootGrid_DragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.IsCaptionVisible = true;
            e.DragUIOverride.Caption = "导入到 LaterVideo";
            e.DragUIOverride.IsContentVisible = true;
            DropOverlay.Visibility = Visibility.Visible;
        }
        else
        {
            e.AcceptedOperation = DataPackageOperation.None;
            DropOverlay.Visibility = Visibility.Collapsed;
        }
    }

    private void RootGrid_DragLeave(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
    }

    private async void RootGrid_Drop(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;

        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        try
        {
            var items = await e.DataView.GetStorageItemsAsync();
            var paths = new List<string>();

            foreach (var item in items)
            {
                switch (item)
                {
                    case StorageFile file when !string.IsNullOrEmpty(file.Path):
                        paths.Add(file.Path);
                        break;
                    case StorageFolder folder when !string.IsNullOrEmpty(folder.Path):
                        paths.Add(folder.Path);
                        break;
                }
            }

            if (paths.Count == 0)
            {
                SetMessage("没有识别到可导入的文件");
                return;
            }

            AddPaths(paths);
        }
        catch (Exception ex)
        {
            SetMessage("导入失败：" + ex.Message);
        }
    }

    #endregion

    #region 列表事件

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        RefreshView();
    }

    private void CategoryFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RefreshView();
    }

    private void SortBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RefreshView();
    }

    private void FileListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdatePreview(FileListView.SelectedItem as FileItem);
    }

    private void FileListView_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (FileListView.SelectedItem is FileItem item)
        {
            OpenFile(item);
        }
    }

    #endregion

    #region 文件操作

    private void OpenFile(FileItem item)
    {
        try
        {
            Process.Start(new ProcessStartInfo(item.FullPath) { UseShellExecute = true });
            SetMessage($"已用系统默认程序打开：{item.Name}");
        }
        catch (Exception ex)
        {
            SetMessage("打开文件失败：" + ex.Message);
        }
    }

    private void OpenLocation(FileItem item)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{item.FullPath}\"")
            {
                UseShellExecute = true
            });
            SetMessage("已在文件资源管理器中定位该文件");
        }
        catch (Exception ex)
        {
            SetMessage("打开所在位置失败：" + ex.Message);
        }
    }

    private void CopyPath(FileItem item)
    {
        try
        {
            var package = new DataPackage();
            package.SetText(item.FullPath);
            Clipboard.SetContent(package);
            SetMessage("已复制完整路径到剪贴板");
        }
        catch (Exception ex)
        {
            SetMessage("复制路径失败：" + ex.Message);
        }
    }

    private void RemoveSelected()
    {
        var items = FileListView.SelectedItems.OfType<FileItem>().ToList();
        if (items.Count == 0)
        {
            SetMessage("请先在左侧列表中选择要移除的文件");
            return;
        }

        foreach (var item in items)
        {
            _allItems.Remove(item);
            _pathSet.Remove(item.FullPath);
        }

        RefreshView();
        SaveSession();
        SetMessage($"已从列表移除 {items.Count} 个文件（磁盘上的原文件未被删除）");
    }

    private void ClearAll()
    {
        var count = _allItems.Count;
        if (count == 0)
        {
            SetMessage("列表已经是空的");
            return;
        }

        _allItems.Clear();
        _pathSet.Clear();
        RefreshView();
        SaveSession();
        SetMessage($"已清空列表（共 {count} 个条目，磁盘上的原文件未被删除）");
    }

    private void OpenFileButton_Click(object sender, RoutedEventArgs e)
    {
        if (FileListView.SelectedItem is FileItem item)
        {
            OpenFile(item);
        }
    }

    private void OpenLocationButton_Click(object sender, RoutedEventArgs e)
    {
        if (FileListView.SelectedItem is FileItem item)
        {
            OpenLocation(item);
        }
    }

    private void CopyPathButton_Click(object sender, RoutedEventArgs e)
    {
        if (FileListView.SelectedItem is FileItem item)
        {
            CopyPath(item);
        }
    }

    private void RemoveSelectedButton_Click(object sender, RoutedEventArgs e)
    {
        RemoveSelected();
    }

    private void RemoveButton_Click(object sender, RoutedEventArgs e)
    {
        RemoveSelected();
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        ClearAll();
    }

    #endregion

    #region 视图切换（列表 / 缩略图网格）

    private ListViewBase GetCurrentListControl() => _isGridView ? FileGridView : FileListView;

    private void ViewToggleButton_Click(object sender, RoutedEventArgs e)
    {
        SetGridView(!_isGridView);
        SaveSession();

        SetMessage(_isGridView
            ? "已切换到缩略图网格视图：图片显示缩略图，其他类型显示分类图标"
            : "已切换到列表视图");
    }

    /// <summary>按设置的缩略图档位应用网格卡片尺寸，并同步后续缩略图的解码宽度。</summary>
    private void ApplyGridItemSize()
    {
        var metrics = _settings.GridMetrics;
        FileItem.ThumbnailDecodeWidth = metrics.DecodeWidth;

        if (FileGridView.ItemsPanelRoot is ItemsWrapGrid panel)
        {
            panel.ItemWidth = metrics.ItemWidth;
            panel.ItemHeight = metrics.ItemHeight;
        }
    }

    /// <summary>网格控件首次加载后校正卡片尺寸（此时 ItemsPanelRoot 才可用）。</summary>
    private void FileGridView_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyGridItemSize();
    }

    /// <summary>切换视图模式，并尽量保留原有选中项。</summary>
    private void SetGridView(bool useGrid)
    {
        var selectedPaths = _ready ? GetSelectedPaths() : new List<string>();
        var previous = GetCurrentListControl();

        _isGridView = useGrid;

        FileGridView.Visibility = useGrid ? Visibility.Visible : Visibility.Collapsed;
        FileListView.Visibility = useGrid ? Visibility.Collapsed : Visibility.Visible;
        ListHeader.Visibility = useGrid ? Visibility.Collapsed : Visibility.Visible;

        ViewToggleText.Text = useGrid ? "切换为列表视图" : "切换为网格视图";
        ViewToggleIcon.Glyph = useGrid ? "\uE8FD" : "\uE727";

        // 网格卡片尺寸随设置档位变化（首次显示后 ItemsPanelRoot 才存在，Loaded 事件里会再校正一次）
        ApplyGridItemSize();

        // 清掉旧控件的选中状态，避免两个控件各留一份选中
        previous.SelectedItems.Clear();

        if (_ready)
        {
            RestoreSelection(selectedPaths);
        }
    }

    private List<string> GetSelectedPaths()
        => GetCurrentListControl().SelectedItems.OfType<FileItem>().Select(i => i.FullPath).ToList();

    /// <summary>在新控件中恢复选中项，并同步右侧预览面板。</summary>
    private void RestoreSelection(IReadOnlyList<string> paths)
    {
        var control = GetCurrentListControl();
        control.SelectedItems.Clear();

        if (paths.Count == 0)
        {
            UpdatePreview(null);
            return;
        }

        foreach (var item in _visibleItems)
        {
            if (paths.Any(p => string.Equals(p, item.FullPath, StringComparison.OrdinalIgnoreCase)))
            {
                control.SelectedItems.Add(item);
            }
        }

        UpdatePreview(control.SelectedItem as FileItem);
    }

    private void SelectItemInCurrentView(FileItem item)
    {
        var control = GetCurrentListControl();
        control.SelectedItem = item;
        control.ScrollIntoView(item);
    }

    private void FileGridView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdatePreview(FileGridView.SelectedItem as FileItem);
    }

    private void FileGridView_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (FileGridView.SelectedItem is FileItem item)
        {
            OpenFile(item);
        }
    }

    /// <summary>网格视图容器生成时按需解码缩略图，避免一次性加载整批图片。</summary>
    private void FileGridView_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.Item is FileItem item)
        {
            item.EnsureThumbnail();
        }
    }

    #endregion

    #region 列表持久化

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        SaveSession();
    }

    /// <summary>启动时按设置恢复上次的清单与视图模式，只恢复磁盘上仍然存在的文件。</summary>
    private void LoadSession()
    {
        var state = SessionStore.Load();

        var restored = 0;
        var missing = 0;

        if (_settings.RememberSession)
        {
            foreach (var path in state.Paths)
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    missing++;
                    continue;
                }

                if (!_pathSet.Add(path))
                {
                    continue;
                }

                try
                {
                    _allItems.Add(new FileItem(path));
                    restored++;
                }
                catch
                {
                    _pathSet.Remove(path);
                    missing++;
                }
            }
        }

        // 视图：开启「记住上次的视图模式」时沿用上次视图，否则使用设置里的默认视图
        SetGridView(_settings.RememberViewMode ? state.IsGridView : _settings.DefaultViewValue == DefaultViewMode.Grid);

        if (restored > 0 || missing > 0)
        {
            var message = new StringBuilder();
            message.Append($"已从上次的清单恢复 {restored} 个文件");
            if (missing > 0)
            {
                message.Append($"（{missing} 个路径已不存在，已忽略）");
            }

            SetMessage(message.ToString());
        }
        else if (!_settings.RememberSession && state.Paths.Count > 0)
        {
            SetMessage("已关闭「记住上次的文件清单」，本次未恢复上次的列表");
        }
    }

    /// <summary>把当前清单与视图模式写入本地 session.json（位于 %LOCALAPPDATA%\LaterVideo）。</summary>
    private void SaveSession()
    {
        if (!_ready)
        {
            return;
        }

        SessionStore.Save(new SessionState
        {
            IsGridView = _settings.RememberViewMode ? _isGridView : _settings.DefaultViewValue == DefaultViewMode.Grid,
            Paths = _settings.RememberSession ? _allItems.Select(i => i.FullPath).ToList() : new List<string>()
        });
    }

    #endregion

    #region 导出清单（CSV / TXT）

    private async void ExportCsvButton_Click(object sender, RoutedEventArgs e)
    {
        await ExportAsync(true);
    }

    private async void ExportTxtButton_Click(object sender, RoutedEventArgs e)
    {
        await ExportAsync(false);
    }

    /// <summary>导出当前列表（筛选 + 排序后的可见项）到用户选择的位置。</summary>
    private async Task ExportAsync(bool asCsv)
    {
        var items = _visibleItems.ToList();
        if (items.Count == 0)
        {
            SetMessage("当前列表为空，没有可导出的内容");
            return;
        }

        try
        {
            var picker = new Windows.Storage.Pickers.FileSavePicker
            {
                SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Desktop,
                SuggestedFileName = "LaterVideo清单_" + DateTime.Now.ToString("yyyyMMdd_HHmmss")
            };
            picker.FileTypeChoices.Add(asCsv ? "CSV 文件" : "文本文件", new List<string> { asCsv ? ".csv" : ".txt" });

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSaveFileAsync();
            if (file is null)
            {
                SetMessage("已取消导出");
                return;
            }

            var text = asCsv ? ExportService.BuildCsv(items) : ExportService.BuildTxt(items);
            await FileIO.WriteTextAsync(file, text);

            SetMessage($"已导出 {items.Count} 个文件（{(asCsv ? "CSV" : "TXT")}）：{file.Path}");
        }
        catch (Exception ex)
        {
            SetMessage("导出失败：" + ex.Message);
        }
    }

    #endregion

    #region 批量重命名（统一前缀 + 递增序号）

    private async void BatchRenameButton_Click(object sender, RoutedEventArgs e)
    {
        var targets = GetRenameTargets();
        if (targets.Count == 0)
        {
            SetMessage("列表为空，没有可重命名的文件");
            return;
        }

        var selectedCount = GetSelectedPaths().Count;

        var scopeText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Text = selectedCount > 0
                ? $"作用范围：当前选中的 {selectedCount} 个文件（按列表显示顺序编号）"
                : $"未选中任何文件，将对当前显示的 {targets.Count} 个文件重命名"
        };

        var prefixBox = new TextBox
        {
            Header = "统一前缀",
            Text = "文件_",
            PlaceholderText = "例如：假期照片_"
        };

        var startBox = new NumberBox
        {
            Header = "起始序号",
            Value = 1,
            Minimum = 0,
            Maximum = 999999,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline
        };

        var digitsBox = new NumberBox
        {
            Header = "序号位数",
            Value = 3,
            Minimum = 1,
            Maximum = 6,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline
        };

        var keepExtSwitch = new ToggleSwitch
        {
            Header = "保留原扩展名",
            IsOn = true,
            OnContent = "保留",
            OffContent = "去掉"
        };

        var previewText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
            FontSize = 12
        };

        var panel = new StackPanel { Spacing = 12, MinWidth = 420 };
        panel.Children.Add(scopeText);
        panel.Children.Add(prefixBox);
        panel.Children.Add(startBox);
        panel.Children.Add(digitsBox);
        panel.Children.Add(keepExtSwitch);
        panel.Children.Add(previewText);

        void RefreshRenamePreview()
        {
            var preview = RenameService.BuildPlan(
                targets,
                prefixBox.Text,
                ToInt(startBox.Value, 1),
                ToInt(digitsBox.Value, 3),
                keepExtSwitch.IsOn);

            var lines = new List<string>
            {
                $"受影响：{preview.RenameCount} 个文件" + (preview.SkipCount > 0 ? $"，跳过 {preview.SkipCount} 个" : string.Empty),
                string.Empty
            };

            foreach (var item in preview.Items.Take(3))
            {
                lines.Add(item.Item.Name + "  →  " + item.NewName);
            }

            if (preview.Items.Count > 3)
            {
                lines.Add("...");
            }

            var skipped = preview.Skipped.FirstOrDefault();
            if (skipped is not null)
            {
                lines.Add($"（跳过示例：{skipped.Item.Name} - {skipped.SkipReason}）");
            }

            previewText.Text = string.Join(Environment.NewLine, lines);
        }

        prefixBox.TextChanged += (_, _) => RefreshRenamePreview();
        startBox.ValueChanged += (_, _) => RefreshRenamePreview();
        digitsBox.ValueChanged += (_, _) => RefreshRenamePreview();
        keepExtSwitch.Toggled += (_, _) => RefreshRenamePreview();
        RefreshRenamePreview();

        var dialog = new ContentDialog
        {
            Title = "批量重命名",
            Content = panel,
            PrimaryButtonText = "下一步",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = RootGrid.XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            SetMessage("已取消批量重命名");
            return;
        }

        var prefix = RenameService.SanitizePrefix(prefixBox.Text);
        var startIndex = ToInt(startBox.Value, 1);
        var digits = ToInt(digitsBox.Value, 3);
        var plan = RenameService.BuildPlan(targets, prefix, startIndex, digits, keepExtSwitch.IsOn);

        if (plan.RenameCount == 0)
        {
            SetMessage("没有需要重命名的文件（全部被跳过：名称未变化、目标名称已存在或与本次生成的重名）");
            return;
        }

        // 执行前二次确认，明确显示将受影响的文件数量
        var confirmLines = new List<string>
        {
            $"即将重命名 {plan.RenameCount} 个文件" + (plan.SkipCount > 0 ? $"（另有 {plan.SkipCount} 个将被跳过）" : string.Empty),
            $"命名规则：{prefix} + 递增序号（{digits} 位，起始 {startIndex}）" + (keepExtSwitch.IsOn ? " + 原扩展名" : string.Empty),
            "仅在文件所在目录内改名，不移动位置，也不会覆盖任何已有文件。",
            string.Empty
        };

        foreach (var item in plan.Items.Where(i => i.WillRename).Take(5))
        {
            confirmLines.Add(item.Item.Name + "  →  " + item.NewName);
        }

        if (plan.RenameCount > 5)
        {
            confirmLines.Add($"...（其余 {plan.RenameCount - 5} 个）");
        }

        var confirmDialog = new ContentDialog
        {
            Title = "确认批量重命名",
            Content = new TextBlock { TextWrapping = TextWrapping.Wrap, Text = string.Join(Environment.NewLine, confirmLines) },
            PrimaryButtonText = "确认重命名",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = RootGrid.XamlRoot
        };

        if (await confirmDialog.ShowAsync() != ContentDialogResult.Primary)
        {
            SetMessage("已取消批量重命名，未改动任何文件");
            return;
        }

        var outcome = RenameService.Execute(plan);
        ApplyRenameResult(outcome);
    }

    /// <summary>重命名目标：优先取选中项（按当前显示顺序），未选中时取当前显示的全部文件。</summary>
    private List<FileItem> GetRenameTargets()
    {
        var selected = GetSelectedPaths();
        if (selected.Count == 0)
        {
            return _visibleItems.ToList();
        }

        var set = new HashSet<string>(selected, StringComparer.OrdinalIgnoreCase);
        return _visibleItems.Where(i => set.Contains(i.FullPath)).ToList();
    }

    private static int ToInt(double value, int fallback)
        => double.IsNaN(value) ? fallback : (int)Math.Round(value);

    /// <summary>把重命名结果写回列表：用新路径重建条目并保持原有顺序。</summary>
    private void ApplyRenameResult(RenameOutcome outcome)
    {
        foreach (var entry in outcome.Renamed)
        {
            var index = _allItems.IndexOf(entry.Source);
            if (index < 0)
            {
                continue;
            }

            _pathSet.Remove(entry.Source.FullPath);

            try
            {
                var replacement = new FileItem(entry.NewPath);
                _pathSet.Add(replacement.FullPath);
                _allItems[index] = replacement;
            }
            catch
            {
                _pathSet.Add(entry.Source.FullPath);
                outcome.Failed.Add(new RenameFailure
                {
                    Item = entry.Source,
                    Reason = "重命名已成功但列表刷新失败，请重新导入该文件"
                });
            }
        }

        RefreshView();
        SaveSession();

        var message = new StringBuilder();
        message.Append($"批量重命名完成：成功 {outcome.Renamed.Count} 个");
        if (outcome.SkippedCount > 0)
        {
            message.Append($"，跳过 {outcome.SkippedCount} 个");
        }

        if (outcome.Failed.Count > 0)
        {
            message.Append($"，失败 {outcome.Failed.Count} 个（{outcome.Failed[0].Item.Name}：{outcome.Failed[0].Reason}）");
        }

        SetMessage(message.ToString());
    }

    #endregion

    #region 预览

    private async void UpdatePreview(FileItem? item)
    {
        try
        {
            MediaPlayer.Source = null;
        }
        catch
        {
            // 忽略：切换预览时停止上一次播放
        }

        if (item is null)
        {
            PreviewImage.Source = null;
            PreviewImage.Visibility = Visibility.Collapsed;
            PreviewText.Visibility = Visibility.Collapsed;
            MediaPlayer.Visibility = Visibility.Collapsed;
            PreviewPlaceholder.Visibility = Visibility.Visible;
            PlaceholderText.Text = "在左侧选择一个文件即可在此预览";

            InfoName.Text = "未选择文件";
            InfoMeta.Text = "请从左侧列表选择文件";
            InfoPath.Text = string.Empty;

            OpenFileButton.IsEnabled = false;
            OpenLocationButton.IsEnabled = false;
            CopyPathButton.IsEnabled = false;
            RemoveSelectedButton.IsEnabled = false;
            return;
        }

        OpenFileButton.IsEnabled = true;
        OpenLocationButton.IsEnabled = true;
        CopyPathButton.IsEnabled = true;
        RemoveSelectedButton.IsEnabled = true;

        InfoName.Text = item.Name;
        InfoMeta.Text = $"类型：{item.CategoryName}    大小：{item.SizeText}    修改时间：{item.ModifiedText}    格式：{(string.IsNullOrEmpty(item.Extension) ? "未知" : item.Extension)}";
        InfoPath.Text = item.FullPath;

        PreviewImage.Visibility = Visibility.Collapsed;
        PreviewImage.Source = null;
        PreviewText.Visibility = Visibility.Collapsed;
        MediaPlayer.Visibility = Visibility.Collapsed;
        PreviewPlaceholder.Visibility = Visibility.Collapsed;

        switch (item.Category)
        {
            case FileCategory.Image:
                try
                {
                    PreviewImage.Source = new BitmapImage(new Uri(item.FullPath));
                    PreviewImage.Visibility = Visibility.Visible;
                }
                catch
                {
                    PlaceholderText.Text = "该图片无法预览，可点击「打开文件」用系统程序查看";
                    PreviewPlaceholder.Visibility = Visibility.Visible;
                }

                break;

            case FileCategory.Text:
            case FileCategory.Config:
                await LoadTextPreviewAsync(item);
                break;

            case FileCategory.Audio:
            case FileCategory.Video:
                try
                {
                    var file = await StorageFile.GetFileFromPathAsync(item.FullPath);
                    MediaPlayer.Source = MediaSource.CreateFromStorageFile(file);
                    MediaPlayer.Visibility = Visibility.Visible;
                }
                catch
                {
                    PlaceholderText.Text = "该媒体文件无法内嵌播放，可点击「打开文件」用系统播放器查看";
                    PreviewPlaceholder.Visibility = Visibility.Visible;
                }

                break;

            default:
                PlaceholderText.Text = "该类型暂不支持内嵌预览，可点击「打开文件」用系统默认程序查看";
                PreviewPlaceholder.Visibility = Visibility.Visible;
                break;
        }
    }

    private async Task LoadTextPreviewAsync(FileItem item)
    {
        if (item.SizeBytes > MaxTextPreviewBytes)
        {
            PreviewText.Text = $"文件较大（{item.SizeText}），已跳过文本预览。可点击「打开文件」查看完整内容。";
            PreviewText.Visibility = Visibility.Visible;
            return;
        }

        try
        {
            var text = await Task.Run(() => ReadTextHead(item.FullPath, MaxTextPreviewChars));

            if (string.IsNullOrEmpty(text))
            {
                text = "（文件内容为空）";
            }
            else if (LooksBinary(text))
            {
                text = "该文件看起来是二进制内容，无法以文本方式预览。";
            }

            PreviewText.Text = text;
            PreviewText.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            PreviewText.Text = "读取失败：" + ex.Message;
            PreviewText.Visibility = Visibility.Visible;
        }
    }

    private static string ReadTextHead(string path, int maxChars)
    {
        using var reader = new StreamReader(path, Encoding.UTF8, true);
        var buffer = new char[Math.Min(maxChars, 64 * 1024)];
        var builder = new StringBuilder();

        while (builder.Length < maxChars)
        {
            var want = Math.Min(buffer.Length, maxChars - builder.Length);
            var read = reader.Read(buffer, 0, want);
            if (read <= 0)
            {
                break;
            }

            builder.Append(buffer, 0, read);
        }

        return builder.ToString();
    }

    private static bool LooksBinary(string text)
    {
        var limit = Math.Min(text.Length, 1000);
        for (var i = 0; i < limit; i++)
        {
            if (text[i] == '\0')
            {
                return true;
            }
        }

        return false;
    }

    #endregion
}
