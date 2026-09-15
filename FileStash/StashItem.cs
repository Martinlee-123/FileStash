namespace FileStash;

/// <summary>
/// 暂存栈中的一条文件项。
/// </summary>
public class StashItem
{
    /// <summary>文件完整路径</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>文件名（含扩展名）</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 是否由拖入的文字/图片临时生成的本地文件。
    /// 为 true 时，拖出（取出）后应把该临时文件一并删除，避免残留。
    /// </summary>
    public bool IsTemporary { get; set; }
}
