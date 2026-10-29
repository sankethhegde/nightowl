// Draws the nightOwl icon (two owl eyes on a night-indigo tile) and writes assets/nightowl.ico + ui/owl.png.
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

var root = args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "NightOwl");
var sizes = new[] { 16, 20, 24, 32, 40, 48, 64, 256 };
var pngs = sizes.Select(Draw).ToList();

using (var fs = File.Create(Path.Combine(root, "assets", "nightowl.ico")))
using (var w = new BinaryWriter(fs))
{
    w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)sizes.Length);
    int offset = 6 + 16 * sizes.Length;
    for (int i = 0; i < sizes.Length; i++)
    {
        byte b = (byte)(sizes[i] >= 256 ? 0 : sizes[i]);
        w.Write(b); w.Write(b); w.Write((byte)0); w.Write((byte)0);
        w.Write((ushort)1); w.Write((ushort)32);
        w.Write((uint)pngs[i].Length); w.Write((uint)offset);
        offset += pngs[i].Length;
    }
    foreach (var p in pngs) w.Write(p);
}
File.WriteAllBytes(Path.Combine(root, "ui", "owl.png"), Draw(128));
Console.WriteLine("icon written to " + Path.GetFullPath(root));

static byte[] Draw(int s)
{
    using var bmp = new Bitmap(s, s);
    using (var g = Graphics.FromImage(bmp))
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);
        float r = Math.Max(2, s * 0.22f), d = r * 2;
        using var path = new GraphicsPath();
        path.AddArc(0, 0, d, d, 180, 90);
        path.AddArc(s - d - 1, 0, d, d, 270, 90);
        path.AddArc(s - d - 1, s - d - 1, d, d, 0, 90);
        path.AddArc(0, s - d - 1, d, d, 90, 90);
        path.CloseFigure();
        using var bg = new LinearGradientBrush(new Point(0, 0), new Point(s, s), Color.FromArgb(49, 46, 129), Color.FromArgb(15, 23, 42));
        g.FillPath(bg, path);

        using var tuft = new SolidBrush(Color.FromArgb(99, 102, 241));
        g.FillPolygon(tuft, new[] { P(0.16f, 0.38f), P(0.22f, 0.12f), P(0.40f, 0.30f) });
        g.FillPolygon(tuft, new[] { P(0.84f, 0.38f), P(0.78f, 0.12f), P(0.60f, 0.30f) });

        float eye = s * 0.34f, pupil = eye * 0.48f;
        using var amber = new SolidBrush(Color.FromArgb(251, 191, 36));
        using var dark = new SolidBrush(Color.FromArgb(15, 23, 42));
        foreach (var cx in new[] { s * 0.31f, s * 0.69f })
        {
            g.FillEllipse(amber, cx - eye / 2, s * 0.5f - eye / 2, eye, eye);
            g.FillEllipse(dark, cx - pupil / 2, s * 0.5f - pupil / 2, pupil, pupil);
        }
        using var beak = new SolidBrush(Color.FromArgb(245, 158, 11));
        g.FillPolygon(beak, new[] { P(0.44f, 0.70f), P(0.56f, 0.70f), P(0.50f, 0.84f) });

        PointF P(float x, float y) => new(s * x, s * y);
    }
    using var ms = new MemoryStream();
    bmp.Save(ms, ImageFormat.Png);
    return ms.ToArray();
}
