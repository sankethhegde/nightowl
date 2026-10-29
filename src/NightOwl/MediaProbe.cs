using System.Text;

namespace NightOwl;

/// <summary>
/// Cheap video codec sniffing: looks for the codec tags MP4 (sample entries) and Matroska/WebM
/// (CodecID strings) carry near the start or end of the file. Enough to warn about HEVC, which
/// neither WebView2 nor Media Foundation can decode without Microsoft's HEVC Video Extensions.
/// </summary>
public static class MediaProbe
{
    static readonly (string tag, string codec)[] Tags =
    {
        ("hvc1", "hevc"), ("hev1", "hevc"), ("V_MPEGH/ISO/HEVC", "hevc"),
        ("avc1", "h264"), ("avc3", "h264"), ("V_MPEG4/ISO/AVC", "h264"),
        ("av01", "av1"), ("V_AV1", "av1"),
        ("vp09", "vp9"), ("V_VP9", "vp9"), ("V_VP8", "vp8"),
    };

    public static string VideoCodec(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            const int window = 4 << 20;
            var found = Scan(fs, 0, window);
            if (found == "" && fs.Length > window) found = Scan(fs, Math.Max(window, fs.Length - window), window);
            return found;
        }
        catch { return ""; }
    }

    static string Scan(FileStream fs, long offset, int length)
    {
        fs.Position = offset;
        var buf = new byte[(int)Math.Min(length, fs.Length - offset)];
        int read = fs.ReadAtLeast(buf, buf.Length, throwOnEndOfStream: false);
        var span = buf.AsSpan(0, read);
        foreach (var (tag, codec) in Tags)
            if (span.IndexOf(Encoding.ASCII.GetBytes(tag)) >= 0) return codec;
        return "";
    }

    public static string Describe(string codec) => codec switch
    {
        "hevc" => "HEVC (H.265)",
        "h264" => "H.264",
        "av1" => "AV1",
        "vp9" => "VP9",
        "vp8" => "VP8",
        _ => "",
    };
}
