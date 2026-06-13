namespace DesktopBox.Models;

public class Box
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "新盒子";
    public double X { get; set; } = 100;
    public double Y { get; set; } = 100;
    public double Width { get; set; } = 240;
    public double Height { get; set; } = 200;
    public string? AccentColor { get; set; }
    public int Order { get; set; }
    public List<BoxItem> Items { get; set; } = new();
}
