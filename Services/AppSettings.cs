using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace LaterVideo.Services;

/// <summary>外观主题。</summary>
public enum AppTheme
{
    /// <summary>跟随系统。</summary>
    System,

    /// <summary>浅色。</summary>
    Light,

    /// <summary>深色。</summary>
    Dark
}

/// <summary>默认视图模式。</summary>
public enum DefaultViewMode
{
    /// <summary>列表视图。</summary>
    List,

    /// <summary>缩略图网格视图。</summary>
    Grid
}

/// <summary>缩略图（网格卡片）尺寸档位。</summary>
public enum ThumbnailSizeOption
{
    /// <summary>小。</summary>
    Small,

    /// <summary>中。</summary>
    Medium,

    /// <summary>大。</summary>
    Large
}

/// <summary>网格卡片尺寸与缩略图解码宽度的映射结果。</summary>
public readonly record struct GridTileMetrics(double ItemWidth, double ItemHeight, int DecodeWidth);

/// <summary>
/// 应用设置。与列表会话数据（session.json）放在同一个配置目录
/// %LOCALAPPDATA%\LaterVideo\settings.json。
/// </summary>
public sealed class AppSettings
{
    /// <summary>数据结构版本。</summary>
    public int Version { get; set; } = 1;

    /// <summary>主题：System / Light / Dark。</summary>
    public string Theme { get; set; } = nameof(AppTheme.System);

    /// <summary>是否记住上次的文件清单。</summary>
    public bool RememberSession { get; set; } = true;

    /// <summary>是否记住上次的视图模式（关闭时改用 DefaultView）。</summary>
    public bool RememberViewMode { get; set; } = true;

    /// <summary>默认视图：List / Grid。</summary>
    public string DefaultView { get; set; } = nameof(DefaultViewMode.List);

    /// <summary>缩略图尺寸：Small / Medium / Large。</summary>
    public string ThumbnailSize { get; set; } = nameof(ThumbnailSizeOption.Medium);

    /// <summary>左侧功能侧边栏是否处于折叠（仅图标）状态。</summary>
    public bool IsSidebarCollapsed { get; set; }

    /// <summary>主题的强类型视图。</summary>
    public AppTheme ThemeValue => Enum.TryParse<AppTheme>(Theme, true, out var value) ? value : AppTheme.System;

    /// <summary>默认视图的强类型视图。</summary>
    public DefaultViewMode DefaultViewValue => Enum.TryParse<DefaultViewMode>(DefaultView, true, out var value)
        ? value
        : DefaultViewMode.List;

    /// <summary>缩略图尺寸的强类型视图。</summary>
    public ThumbnailSizeOption ThumbnailSizeValue => Enum.TryParse<ThumbnailSizeOption>(ThumbnailSize, true, out var value)
        ? value
        : ThumbnailSizeOption.Medium;

    /// <summary>当前缩略图档位对应的网格卡片尺寸。</summary>
    public GridTileMetrics GridMetrics => GetMetrics(ThumbnailSizeValue);

    /// <summary>按档位取网格卡片尺寸与缩略图解码宽度。</summary>
    public static GridTileMetrics GetMetrics(ThumbnailSizeOption size) => size switch
    {
        ThumbnailSizeOption.Small => new GridTileMetrics(120, 146, 120),
        ThumbnailSizeOption.Large => new GridTileMetrics(190, 220, 200),
        _ => new GridTileMetrics(152, 180, 152)
    };
}

/// <summary>
/// 设置持久化：读写 %LOCALAPPDATA%\LaterVideo\settings.json。
/// 只保存应用自身的偏好设置，不触碰任何用户文件。
/// </summary>
public static class AppSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    /// <summary>配置目录（与 session.json 相同）。</summary>
    public static string StorageDirectory => SessionStore.StorageDirectory;

    /// <summary>设置文件完整路径。</summary>
    public static string SettingsFilePath { get; } = Path.Combine(SessionStore.StorageDirectory, "settings.json");

    /// <summary>读取设置；文件不存在或损坏时返回默认设置。</summary>
    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsFilePath))
            {
                return new AppSettings();
            }

            var json = File.ReadAllText(SettingsFilePath, Encoding.UTF8);
            return JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions) ?? new AppSettings();
        }
        catch
        {
            // 文件损坏、无权限时按默认设置处理，不影响应用启动
            return new AppSettings();
        }
    }

    /// <summary>保存设置；失败时返回 false，不影响主流程。</summary>
    public static bool Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(StorageDirectory);
            var json = JsonSerializer.Serialize(settings, SerializerOptions);
            File.WriteAllText(SettingsFilePath, json, new UTF8Encoding(false));
            return true;
        }
        catch
        {
            return false;
        }
    }
}
