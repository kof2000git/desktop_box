namespace DesktopBox.Models;

public class BoxItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BoxId { get; set; }
    public ItemType Type { get; set; }
    public string TargetPath { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? IconCachePath { get; set; }
    public int Order { get; set; }
}
