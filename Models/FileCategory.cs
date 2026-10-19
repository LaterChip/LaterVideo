namespace LaterVideo.Models;

/// <summary>
/// 文件分类。
/// </summary>
public enum FileCategory
{
    /// <summary>图片</summary>
    Image,

    /// <summary>音乐 / 音频</summary>
    Audio,

    /// <summary>视频</summary>
    Video,

    /// <summary>纯文本</summary>
    Text,

    /// <summary>配置文件</summary>
    Config,

    /// <summary>文档（Office / PDF）</summary>
    Document,

    /// <summary>其他</summary>
    Other
}
