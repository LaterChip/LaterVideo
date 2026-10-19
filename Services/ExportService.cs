using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using LaterVideo.Models;

namespace LaterVideo.Services;

/// <summary>
/// 清单导出：把当前列表转换为 CSV / TXT 文本。
/// </summary>
public static class ExportService
{
    private static readonly char[] CsvSpecialChars = { ',', '"', '\r', '\n' };

    /// <summary>生成 CSV（逗号分隔，含表头，字段按 RFC 4180 规则转义）。</summary>
    public static string BuildCsv(IReadOnlyList<FileItem> items)
    {
        var builder = new StringBuilder();
        builder.Append("序号,文件名,类型,扩展名,大小(字节),大小,修改时间,完整路径\r\n");

        var index = 1;
        foreach (var item in items)
        {
            builder.Append(index++.ToString(CultureInfo.InvariantCulture)).Append(',');
            builder.Append(EscapeCsv(item.Name)).Append(',');
            builder.Append(EscapeCsv(item.CategoryName)).Append(',');
            builder.Append(EscapeCsv(item.Extension)).Append(',');
            builder.Append(item.SizeBytes.ToString(CultureInfo.InvariantCulture)).Append(',');
            builder.Append(EscapeCsv(item.SizeText)).Append(',');
            builder.Append(EscapeCsv(item.ModifiedText)).Append(',');
            builder.Append(EscapeCsv(item.FullPath)).Append("\r\n");
        }

        return builder.ToString();
    }

    /// <summary>生成 TXT（含概览、分类统计与逐条明细）。</summary>
    public static string BuildTxt(IReadOnlyList<FileItem> items)
    {
        var builder = new StringBuilder();

        long totalBytes = 0;
        foreach (var item in items)
        {
            totalBytes += item.SizeBytes;
        }

        builder.AppendLine("LaterVideo 文件清单");
        builder.AppendLine("导出时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        builder.AppendLine("文件数量：" + items.Count.ToString(CultureInfo.InvariantCulture));
        builder.AppendLine("总大小：" + FileTypeHelper.FormatSize(totalBytes));
        builder.AppendLine("数据文件：" + SessionStore.StorageFilePath);
        builder.AppendLine();

        builder.AppendLine("分类统计：");
        foreach (var category in Enum.GetValues<FileCategory>())
        {
            var count = 0;
            foreach (var item in items)
            {
                if (item.Category == category)
                {
                    count++;
                }
            }

            if (count > 0)
            {
                builder.AppendLine("  " + FileTypeHelper.GetDisplayName(category) + "：" + count.ToString(CultureInfo.InvariantCulture));
            }
        }

        builder.AppendLine();
        builder.AppendLine(new string('=', 56));
        builder.AppendLine();

        var index = 1;
        foreach (var item in items)
        {
            builder.AppendLine("[" + index++.ToString(CultureInfo.InvariantCulture) + "] " + item.Name);
            builder.AppendLine("      类型：" + item.CategoryName);
            builder.AppendLine("      大小：" + item.SizeText + "（" + item.SizeBytes.ToString(CultureInfo.InvariantCulture) + " 字节）");
            builder.AppendLine("      修改时间：" + item.ModifiedText);
            builder.AppendLine("      路径：" + item.FullPath);
            builder.AppendLine();
        }

        return builder.ToString();
    }

    /// <summary>CSV 字段转义：包含逗号、引号或换行时用双引号包裹，内部引号双写。</summary>
    private static string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if (value.IndexOfAny(CsvSpecialChars) < 0)
        {
            return value;
        }

        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
