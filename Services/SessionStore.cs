using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace LaterVideo.Services;

/// <summary>
/// 需要持久化的会话数据。
/// </summary>
public sealed class SessionState
{
    /// <summary>数据结构版本。</summary>
    public int Version { get; set; } = 1;

    /// <summary>上次退出时是否为网格视图。</summary>
    public bool IsGridView { get; set; }

    /// <summary>上次列表中的文件完整路径。</summary>
    public List<string> Paths { get; set; } = new();
}

/// <summary>
/// 列表持久化：把文件清单保存到 %LOCALAPPDATA%\LaterVideo\session.json，重启后自动恢复。
/// 只记录路径，不复制、不修改任何用户文件。
/// </summary>
public static class SessionStore
{
    private const int MaxSavedPaths = 20000;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    /// <summary>数据文件所在目录。</summary>
    public static string StorageDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LaterVideo");

    /// <summary>数据文件完整路径。</summary>
    public static string StorageFilePath { get; } = Path.Combine(StorageDirectory, "session.json");

    /// <summary>读取上次保存的清单；文件不存在或损坏时返回空状态。</summary>
    public static SessionState Load()
    {
        try
        {
            if (!File.Exists(StorageFilePath))
            {
                return new SessionState();
            }

            var json = File.ReadAllText(StorageFilePath, Encoding.UTF8);
            var state = JsonSerializer.Deserialize<SessionState>(json, SerializerOptions);
            if (state is null)
            {
                return new SessionState();
            }

            state.Paths ??= new List<string>();
            if (state.Paths.Count > MaxSavedPaths)
            {
                state.Paths = state.Paths.GetRange(0, MaxSavedPaths);
            }

            return state;
        }
        catch
        {
            // 读取失败（文件损坏、无权限等）时按空列表处理，不影响应用启动
            return new SessionState();
        }
    }

    /// <summary>保存清单；失败时静默忽略，不影响主流程。</summary>
    public static bool Save(SessionState state)
    {
        try
        {
            if (state.Paths.Count > MaxSavedPaths)
            {
                state.Paths = state.Paths.GetRange(0, MaxSavedPaths);
            }

            Directory.CreateDirectory(StorageDirectory);
            var json = JsonSerializer.Serialize(state, SerializerOptions);
            File.WriteAllText(StorageFilePath, json, new UTF8Encoding(false));
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>清空持久化清单。</summary>
    public static void Clear()
    {
        try
        {
            if (File.Exists(StorageFilePath))
            {
                File.Delete(StorageFilePath);
            }
        }
        catch
        {
            // 忽略
        }
    }
}
