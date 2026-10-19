using System;
using System.Collections.Generic;
using System.Globalization;
using LaterVideo.Models;

namespace LaterVideo.Services;

/// <summary>
/// 文件类型识别与格式化工具。
/// </summary>
public static class FileTypeHelper
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".jpe", ".png", ".bmp", ".gif", ".webp", ".tif", ".tiff",
        ".svg", ".ico", ".heic", ".heif", ".avif", ".jfif"
    };

    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".wav", ".flac", ".aac", ".ogg", ".oga", ".m4a", ".wma", ".aiff", ".aif",
        ".opus", ".mid", ".midi", ".amr", ".ape", ".ac3", ".cda"
    };

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".m4v", ".avi", ".mkv", ".mov", ".wmv", ".flv", ".webm", ".mpg", ".mpeg",
        ".mpe", ".ts", ".m2ts", ".mts", ".3gp", ".3g2", ".rmvb", ".rm", ".vob", ".asf", ".ogv"
    };

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".markdown", ".log", ".rtf", ".srt", ".vtt", ".ass", ".tex",
        ".text", ".nfo", ".jsonl"
    };

    private static readonly HashSet<string> ConfigExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".json", ".yaml", ".yml", ".xml", ".ini", ".cfg", ".conf", ".config", ".toml",
        ".properties", ".env", ".props", ".targets", ".resx", ".editorconfig", ".gitignore",
        ".gitattributes", ".reg", ".plist", ".xaml", ".csproj", ".sln", ".bat", ".cmd",
        ".ps1", ".sh", ".sql", ".cs", ".py", ".js", ".ts", ".css", ".html", ".htm"
    };

    private static readonly HashSet<string> DocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".doc", ".docx", ".docm", ".dot", ".dotx", ".xls", ".xlsx", ".xlsm", ".xlsb",
        ".ppt", ".pptx", ".pptm", ".pps", ".ppsx", ".csv", ".tsv", ".wps", ".et", ".dps",
        ".odt", ".ods", ".odp", ".epub", ".mobi", ".one", ".msg", ".eml"
    };

    /// <summary>根据扩展名判断文件分类。</summary>
    public static FileCategory GetCategory(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return FileCategory.Other;
        }

        var ext = extension.Trim();
        if (!ext.StartsWith('.'))
        {
            ext = "." + ext;
        }

        if (ImageExtensions.Contains(ext)) return FileCategory.Image;
        if (AudioExtensions.Contains(ext)) return FileCategory.Audio;
        if (VideoExtensions.Contains(ext)) return FileCategory.Video;
        if (TextExtensions.Contains(ext)) return FileCategory.Text;
        if (ConfigExtensions.Contains(ext)) return FileCategory.Config;
        if (DocumentExtensions.Contains(ext)) return FileCategory.Document;
        return FileCategory.Other;
    }

    /// <summary>分类的中文显示名。</summary>
    public static string GetDisplayName(FileCategory category) => category switch
    {
        FileCategory.Image => "图片",
        FileCategory.Audio => "音乐/音频",
        FileCategory.Video => "视频",
        FileCategory.Text => "文本",
        FileCategory.Config => "配置文件",
        FileCategory.Document => "文档",
        _ => "其他"
    };

    /// <summary>分类对应的图标字形（Segoe Fluent Icons）。</summary>
    public static string GetGlyph(FileCategory category) => category switch
    {
        FileCategory.Image => "\uEB9F",
        FileCategory.Audio => "\uE8D6",
        FileCategory.Video => "\uE714",
        FileCategory.Text => "\uE8A5",
        FileCategory.Config => "\uE713",
        FileCategory.Document => "\uE8A5",
        _ => "\uE7C3"
    };

    /// <summary>把字节数格式化为易读文本。</summary>
    public static string FormatSize(long bytes)
    {
        if (bytes < 0) return "0 B";
        if (bytes < 1024) return bytes.ToString(CultureInfo.InvariantCulture) + " B";

        string[] units = { "KB", "MB", "GB", "TB", "PB" };
        double value = bytes;
        int index = -1;

        do
        {
            value /= 1024d;
            index++;
        }
        while (value >= 1024d && index < units.Length - 1);

        return value.ToString(value >= 100 ? "0" : "0.##", CultureInfo.InvariantCulture) + " " + units[index];
    }
}
