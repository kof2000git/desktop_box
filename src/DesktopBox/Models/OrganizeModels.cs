namespace DesktopBox.Models;

/// <summary>一次整理操作的单条移动记录(供还原使用)。</summary>
public class MoveRecord
{
    public string OriginalPath { get; set; } = "";
    public string NewPath { get; set; } = "";
    public string Category { get; set; } = "";
}

/// <summary>整理操作的完整清单,持久化到 organize.json,用于「还原整理」。</summary>
public class OrganizeManifest
{
    public DateTime Timestamp { get; set; }
    public List<MoveRecord> Moves { get; set; } = new();
    public List<Guid> BoxIds { get; set; } = new();
}

/// <summary>整理后用于在 UI 上建盒子的单条信息。</summary>
public class OrganizeEntry
{
    public string Category { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string CurrentPath { get; set; } = "";
}

/// <summary>整理/还原的结果。</summary>
public class OrganizeResult
{
    public List<OrganizeEntry> Entries { get; set; } = new();
    public OrganizeManifest Manifest { get; set; } = new();
}
