#:property PublishAot=false

// PLACEHOLDER app icon of Raffica PDF. Raffica has no brand mark yet; this draws a plain rounded square in the accent colour of the PDF app
// with a geometric "R", so the installer, the .desktop file and the packages have an icon to ship. The owner replaces the result with the
// real mark (Docs/design/icons/README.md says how). Deterministic, no packages: a small software rasterizer writes PNG and ICO itself.
//   dotnet run tools/make-placeholder-icon.cs [-- --root <repo>]
// Writes src/Raffica.Pdf/Assets/raffica-pdf.ico (16..256, PNG entries) and Docs/design/icons/linux/hicolor/<n>x<n>/apps/raffica-pdf.png.

using System.Buffers.Binary;
using System.IO.Compression;

var root = ".";
for (var i = 0; i < args.Length; i++)
{
    if (args[i] == "--root" && i + 1 < args.Length)
    {
        root = args[++i];
    }
    else
    {
        Console.Error.WriteLine("Usage: dotnet run tools/make-placeholder-icon.cs [-- --root <repo>]");
        return 2;
    }
}

int[] pngSizes = [16, 24, 32, 48, 64, 128, 256, 512];
int[] icoSizes = [16, 24, 32, 48, 64, 128, 256];
var pngs = pngSizes.ToDictionary(s => s, s => Png.Encode(s, Mark.Render(s)));

foreach (var size in pngSizes)
{
    var dir = Path.Combine(root, "Docs", "design", "icons", "linux", "hicolor", $"{size}x{size}", "apps");
    Directory.CreateDirectory(dir);
    File.WriteAllBytes(Path.Combine(dir, "raffica-pdf.png"), pngs[size]);
}

var assets = Path.Combine(root, "src", "Raffica.Pdf", "Assets");
Directory.CreateDirectory(assets);
File.WriteAllBytes(Path.Combine(assets, "raffica-pdf.ico"), Ico.Encode(icoSizes.Select(s => (s, pngs[s])).ToList()));
Console.WriteLine($"Wrote {pngSizes.Length} PNG files and raffica-pdf.ico (placeholder mark).");
return 0;

// The mark in unit coordinates (0..1): a rounded square in the PDF accent and a white "R" drawn as thick round-capped strokes.
static class Mark
{
    // The accent of the PDF app (Raffica.Color.Ink.Pdf, light theme).
    private static readonly (double R, double G, double B) Plum = (0x5E / 255.0, 0x1A / 255.0, 0x3E / 255.0);

    private static readonly List<((double X, double Y) A, (double X, double Y) B)> Strokes = BuildStrokes();

    private const double StrokeHalf = 0.055;

    private static List<((double X, double Y) A, (double X, double Y) B)> BuildStrokes()
    {
        var s = new List<((double X, double Y) A, (double X, double Y) B)>
        {
            ((0.36, 0.26), (0.36, 0.76)),
            ((0.36, 0.26), (0.52, 0.26)),
            ((0.36, 0.54), (0.52, 0.54)),
            ((0.50, 0.54), (0.66, 0.76)),
        };

        // The bowl: a half circle from the top bar to the middle bar, as short segments.
        const int steps = 24;
        (double X, double Y) previous = (0.52, 0.26);
        for (var i = 1; i <= steps; i++)
        {
            var angle = -Math.PI / 2 + Math.PI * i / steps;
            var point = (0.52 + 0.14 * Math.Cos(angle), 0.40 + 0.14 * Math.Sin(angle));
            s.Add((previous, point));
            previous = point;
        }

        return s;
    }

    private static double SegmentDistance((double X, double Y) p, (double X, double Y) a, (double X, double Y) b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / (dx * dx + dy * dy), 0, 1);
        return Math.Sqrt(Math.Pow(p.X - (a.X + t * dx), 2) + Math.Pow(p.Y - (a.Y + t * dy), 2));
    }

    // Signed distance (unit coordinates) to a rounded square of the given half size and corner radius around the centre.
    private static double RoundedSquare((double X, double Y) p, double half, double radius)
    {
        var qx = Math.Abs(p.X - 0.5) - (half - radius);
        var qy = Math.Abs(p.Y - 0.5) - (half - radius);
        return Math.Sqrt(Math.Pow(Math.Max(qx, 0), 2) + Math.Pow(Math.Max(qy, 0), 2)) + Math.Min(Math.Max(qx, qy), 0) - radius;
    }

    // Straight (non-premultiplied) RGBA, size x size.
    public static byte[] Render(int size)
    {
        var pixels = new byte[size * size * 4];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var p = ((x + 0.5) / size, (y + 0.5) / size);
                var coverage = Math.Clamp(0.5 - RoundedSquare(p, 0.46, 0.20) * size, 0, 1);
                var glyph = Strokes.Min(st => SegmentDistance(p, st.A, st.B)) - StrokeHalf;
                var white = Math.Clamp(0.5 - glyph * size, 0, 1);
                var i = (y * size + x) * 4;
                pixels[i] = (byte)Math.Round(255 * (Plum.R + (1 - Plum.R) * white));
                pixels[i + 1] = (byte)Math.Round(255 * (Plum.G + (1 - Plum.G) * white));
                pixels[i + 2] = (byte)Math.Round(255 * (Plum.B + (1 - Plum.B) * white));
                pixels[i + 3] = (byte)Math.Round(255 * coverage);
            }
        }

        return pixels;
    }
}

static class Png
{
    public static byte[] Encode(int size, byte[] rgba)
    {
        using var output = new MemoryStream();
        output.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0), size);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), size);
        header[8] = 8; // bit depth
        header[9] = 6; // RGBA
        Chunk(output, "IHDR", header);

        var raw = new byte[(size * 4 + 1) * size];
        for (var y = 0; y < size; y++)
        {
            Array.Copy(rgba, y * size * 4, raw, y * (size * 4 + 1) + 1, size * 4); // filter byte 0 = none
        }

        using var compressed = new MemoryStream();
        using (var z = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            z.Write(raw);
        }

        Chunk(output, "IDAT", compressed.ToArray());
        Chunk(output, "IEND", []);
        return output.ToArray();
    }

    private static void Chunk(Stream output, string type, byte[] data)
    {
        var length = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        output.Write(length);
        var body = new byte[4 + data.Length];
        System.Text.Encoding.ASCII.GetBytes(type, body);
        data.CopyTo(body, 4);
        output.Write(body);
        var crc = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc(body));
        output.Write(crc);
    }

    private static uint Crc(byte[] bytes)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in bytes)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++)
            {
                crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
            }
        }

        return ~crc;
    }
}

static class Ico
{
    // An .ico whose entries are PNG images (Windows Vista and later); 256 is written as 0 in the directory.
    public static byte[] Encode(List<(int Size, byte[] Png)> images)
    {
        using var output = new MemoryStream();
        using var w = new BinaryWriter(output);
        w.Write((ushort)0);
        w.Write((ushort)1);
        w.Write((ushort)images.Count);
        var offset = 6 + 16 * images.Count;
        foreach (var (size, png) in images)
        {
            w.Write((byte)(size >= 256 ? 0 : size));
            w.Write((byte)(size >= 256 ? 0 : size));
            w.Write((byte)0);
            w.Write((byte)0);
            w.Write((ushort)1);
            w.Write((ushort)32);
            w.Write(png.Length);
            w.Write(offset);
            offset += png.Length;
        }

        foreach (var (_, png) in images)
        {
            w.Write(png);
        }

        w.Flush();
        return output.ToArray();
    }
}
