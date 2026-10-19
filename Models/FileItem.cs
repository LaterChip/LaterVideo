using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using LaterVideo.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace LaterVideo.Models;

/// <summary>
/// 列表中的一个文件条目。基础数据在创建时一次性读取，缩略图按需懒加载。
/// </summary>
public sealed class FileItem : INotifyPropertyChanged
{
    // 缩略图解码宽度上限，控制内存占用；实际取值可由设置页调整
    private static int _thumbnailDecodeWidth = 128;

    private ImageSource? _thumbnail;
    private bool _thumbnailRequested;

    public FileItem(string fullPath)
    {
        var info = new FileInfo(fullPath);

        FullPath = info.FullName;
        Name = info.Name;
        Extension = info.Extension;
        DirectoryPath = info.DirectoryName ?? string.Empty;
        SizeBytes = info.Length;
        ModifiedTime = info.LastWriteTime;
        CreatedTime = info.CreationTime;
        Category = FileTypeHelper.GetCategory(info.Extension);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>缩略图解码宽度（像素）。由设置页的缩略图尺寸档位决定，仅影响之后解码的缩略图。</summary>
    public static int ThumbnailDecodeWidth
    {
        get => _thumbnailDecodeWidth;
        set => _thumbnailDecodeWidth = Math.Clamp(value, 64, 512);
    }

    /// <summary>文件完整路径。</summary>
    public string FullPath { get; }

    /// <summary>文件名（含扩展名）。</summary>
    public string Name { get; }

    /// <summary>扩展名（含点号）。</summary>
    public string Extension { get; }

    /// <summary>所在目录。</summary>
    public string DirectoryPath { get; }

    /// <summary>分类。</summary>
    public FileCategory Category { get; }

    /// <summary>字节数。</summary>
    public long SizeBytes { get; }

    /// <summary>最后修改时间。</summary>
    public DateTime ModifiedTime { get; }

    /// <summary>创建时间。</summary>
    public DateTime CreatedTime { get; }

    /// <summary>分类显示名。</summary>
    public string CategoryName => FileTypeHelper.GetDisplayName(Category);

    /// <summary>分类图标字形。</summary>
    public string Glyph => FileTypeHelper.GetGlyph(Category);

    /// <summary>易读的文件大小。</summary>
    public string SizeText => FileTypeHelper.FormatSize(SizeBytes);

    /// <summary>易读的修改时间。</summary>
    public string ModifiedText => ModifiedTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    /// <summary>是否可内嵌预览的图片。</summary>
    public bool IsImage => Category == FileCategory.Image;

    /// <summary>是否可内嵌预览的文本 / 配置文件。</summary>
    public bool IsTextLike => Category == FileCategory.Text || Category == FileCategory.Config;

    /// <summary>是否可用媒体播放器预览。</summary>
    public bool IsMedia => Category == FileCategory.Audio || Category == FileCategory.Video;

    /// <summary>是否支持生成缩略图（目前仅图片类型）。</summary>
    public bool CanHaveThumbnail => Category == FileCategory.Image;

    /// <summary>缩略图（懒加载，未生成时为 null）。</summary>
    public ImageSource? Thumbnail
    {
        get => _thumbnail;
        private set
        {
            if (ReferenceEquals(_thumbnail, value))
            {
                return;
            }

            _thumbnail = value;
            OnPropertyChanged(nameof(Thumbnail));
            OnPropertyChanged(nameof(IconVisibility));
            OnPropertyChanged(nameof(ImageVisibility));
        }
    }

    /// <summary>网格视图中分类图标的可见性（无缩略图时显示）。</summary>
    public Visibility IconVisibility => _thumbnail is null ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>网格视图中缩略图的可见性。</summary>
    public Visibility ImageVisibility => _thumbnail is null ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>
    /// 按需生成缩略图。重复调用只会真正执行一次；失败时静默保留分类图标。
    /// </summary>
    public void EnsureThumbnail()
    {
        if (_thumbnailRequested || !CanHaveThumbnail)
        {
            return;
        }

        _thumbnailRequested = true;

        try
        {
            Thumbnail = new BitmapImage(new Uri(FullPath)) { DecodePixelWidth = ThumbnailDecodeWidth };
        }
        catch
        {
            // 路径含特殊字符等情况下无法生成缩略图，保留分类图标即可
        }
    }

    private void OnPropertyChanged(string propertyName)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
