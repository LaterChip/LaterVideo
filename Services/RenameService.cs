using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using LaterVideo.Models;

namespace LaterVideo.Services;

/// <summary>
/// 单个文件的重命名计划项。
/// </summary>
public sealed class RenamePlanItem
{
    /// <summary>原文件条目。</summary>
    public FileItem Item { get; set; } = null!;

    /// <summary>原完整路径。</summary>
    public string SourcePath { get; set; } = string.Empty;

    /// <summary>新文件名（不含目录）。</summary>
    public string NewName { get; set; } = string.Empty;

    /// <summary>新的完整路径（与原文件同目录，仅文件名变化）。</summary>
    public string TargetPath { get; set; } = string.Empty;

    /// <summary>跳过原因；为 null 表示该条会被重命名。</summary>
    public string? SkipReason { get; set; }

    /// <summary>是否会被重命名。</summary>
    public bool WillRename => SkipReason is null;
}

/// <summary>
/// 批量重命名计划。
/// </summary>
public sealed class RenamePlan
{
    public List<RenamePlanItem> Items { get; } = new();

    /// <summary>将受影响的文件数量。</summary>
    public int RenameCount => Items.Count(i => i.WillRename);

    /// <summary>被跳过的数量。</summary>
    public int SkipCount => Items.Count(i => !i.WillRename);

    /// <summary>被跳过的条目。</summary>
    public IEnumerable<RenamePlanItem> Skipped => Items.Where(i => !i.WillRename);
}

/// <summary>
/// 重命名失败项。
/// </summary>
public sealed class RenameFailure
{
    public FileItem Item { get; set; } = null!;

    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// 重命名执行结果。
/// </summary>
public sealed class RenameOutcome
{
    public List<(FileItem Source, string NewPath, string NewName)> Renamed { get; } = new();

    public List<RenameFailure> Failed { get; } = new();

    public int SkippedCount { get; set; }
}

/// <summary>
/// 批量重命名：统一前缀 + 递增序号，仅在原目录内改名，不移动文件位置。
/// </summary>
public static class RenameService
{
    private const int MinDigits = 1;
    private const int MaxDigits = 6;

    /// <summary>按「前缀 + 递增序号（+ 原扩展名）」生成新文件名。</summary>
    public static string BuildName(string prefix, int index, int digits, string extension)
    {
        var width = Math.Clamp(digits, MinDigits, MaxDigits);
        var sequence = Math.Max(index, 0).ToString("D" + width.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        return prefix + sequence + extension;
    }

    /// <summary>清理前缀中的非法文件名字符。</summary>
    public static string SanitizePrefix(string? prefix)
    {
        if (string.IsNullOrEmpty(prefix))
        {
            return string.Empty;
        }

        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(prefix.Length);
        foreach (var ch in prefix)
        {
            builder.Append(Array.IndexOf(invalid, ch) >= 0 ? '_' : ch);
        }

        return builder.ToString().Trim();
    }

    /// <summary>
    /// 生成重命名计划。已存在同名文件、与批次内其它目标重名或名称未变化的条目会被标记为跳过，
    /// 保证不会覆盖任何已有文件。
    /// </summary>
    public static RenamePlan BuildPlan(IReadOnlyList<FileItem> orderedItems, string prefix, int startIndex, int digits, bool keepExtension)
    {
        var plan = new RenamePlan();
        var safePrefix = SanitizePrefix(prefix);
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var index = Math.Max(startIndex, 0);
        foreach (var item in orderedItems)
        {
            var extension = keepExtension ? item.Extension : string.Empty;
            var newName = BuildName(safePrefix, index, digits, extension);
            index++;

            var targetPath = Path.Combine(item.DirectoryPath, newName);
            string? skipReason = null;

            if (string.Equals(targetPath, item.FullPath, StringComparison.OrdinalIgnoreCase))
            {
                skipReason = "名称未发生变化";
            }
            else if (!claimed.Add(targetPath))
            {
                skipReason = "与本次生成的其他名称重复";
            }
            else if (File.Exists(targetPath))
            {
                skipReason = "目标名称已存在";
            }

            plan.Items.Add(new RenamePlanItem
            {
                Item = item,
                SourcePath = item.FullPath,
                NewName = newName,
                TargetPath = targetPath,
                SkipReason = skipReason
            });
        }

        return plan;
    }

    /// <summary>执行重命名计划，逐条独立处理，单条失败不影响其他文件。</summary>
    public static RenameOutcome Execute(RenamePlan plan)
    {
        var outcome = new RenameOutcome { SkippedCount = plan.SkipCount };

        foreach (var item in plan.Items)
        {
            if (!item.WillRename)
            {
                continue;
            }

            try
            {
                File.Move(item.SourcePath, item.TargetPath);
                outcome.Renamed.Add((item.Item, item.TargetPath, item.NewName));
            }
            catch (Exception ex)
            {
                outcome.Failed.Add(new RenameFailure { Item = item.Item, Reason = ex.Message });
            }
        }

        return outcome;
    }
}
