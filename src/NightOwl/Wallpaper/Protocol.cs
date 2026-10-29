namespace NightOwl.Wallpaper;

/// <summary>One wallpaper surface: a monitor, or the whole virtual screen when spanning.</summary>
public record SurfaceSpec(string Id, int X, int Y, int W, int H, string Path, string Type, string Fit,
    bool Muted, int Volume, bool Interactive);

/// <summary>Core → wallpaper process, one JSON object per stdin line.</summary>
public class WallpaperCommand
{
    /// <summary>set | state | quit</summary>
    public string Cmd { get; set; } = "";
    public List<SurfaceSpec>? Surfaces { get; set; }
    /// <summary>Surface id → play | pause | hide.</summary>
    public Dictionary<string, string>? States { get; set; }
}
