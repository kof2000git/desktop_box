using System.IO;
using System.Text.Json.Serialization;

namespace DesktopBox.Models;

public class AppConfig
{
    [JsonPropertyName("boxes")]
    public List<Box> Boxes { get; set; } = new();

    [JsonPropertyName("settings")]
    public AppSettings Settings { get; set; } = new();

    public static string DefaultPath => AppPaths.ConfigPath;
}
