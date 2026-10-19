using System;
using System.IO;
using System.Text;

namespace LaterVideo.Services;

/// <summary>
/// 极简文件日志。所有需要事后诊断的事件（尤其是文件拾取链路）都会写入
/// %LOCALAPPDATA%\LaterVideo\log.txt。日志自身出错时静默忽略，绝不影响主流程。
/// </summary>
public static class AppLog
{
    private static readonly object Gate = new();

    /// <summary>日志文件完整路径：%LOCALAPPDATA%\LaterVideo\log.txt</summary>
    public static string LogFilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LaterVideo",
        "log.txt");

    public static void Info(string message) => Write("INFO", message, null);

    public static void Warn(string message, Exception? ex = null) => Write("WARN", message, ex);

    public static void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

    private static void Write(string level, string message, Exception? ex)
    {
        try
        {
            var directory = Path.GetDirectoryName(LogFilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var now = DateTime.Now;
            var stamp = now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var builder = new StringBuilder();
            builder.Append('[').Append(stamp).Append("] [").Append(level).Append("] ").Append(message);
            if (ex is not null)
            {
                builder.AppendLine();
                builder.Append(ex);
            }

            builder.AppendLine();

            lock (Gate)
            {
                File.AppendAllText(LogFilePath, builder.ToString(), Encoding.UTF8);
            }
        }
        catch
        {
            // 写日志失败不能影响功能
        }
    }
}
