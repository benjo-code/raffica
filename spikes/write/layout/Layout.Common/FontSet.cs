using HarfBuzzSharp;
using SkiaSharp;

namespace Layout.Common;

/// <summary>One font file: its Skia typeface, its HarfBuzz face and font, and per-em metrics. No system fonts are ever used.</summary>
public sealed class FontEntry : IDisposable
{
    public string Family { get; }
    public bool Bold { get; }
    public bool Italic { get; }
    public string Path { get; }
    public byte[] Data { get; }
    public SKTypeface Typeface { get; }
    public Blob Blob { get; }
    public Face Face { get; }
    public HarfBuzzSharp.Font HbFont { get; }
    /// <summary>Ascent, descent (positive) and line gap per em; the gap is added to the descent by the layouters.</summary>
    public float Ascent { get; }
    public float Descent { get; }
    public int UnitsPerEm { get; }

    internal FontEntry(string family, bool bold, bool italic, string path)
    {
        Family = family; Bold = bold; Italic = italic; Path = path;
        Data = File.ReadAllBytes(path);
        Typeface = SKTypeface.FromData(SKData.CreateCopy(Data)) ?? throw new InvalidDataException("Skia cannot load " + path);
        Blob = Blob.FromFile(path) ?? throw new InvalidDataException(path);
        Face = new Face(Blob, 0);
        HbFont = new HarfBuzzSharp.Font(Face);
        UnitsPerEm = Face.UnitsPerEm;
        HbFont.TryGetHorizontalFontExtents(out var ext);
        Ascent = ext.Ascender / (float)UnitsPerEm;
        Descent = (-ext.Descender + ext.LineGap) / (float)UnitsPerEm;
    }

    /// <summary>True when the font has a glyph (not .notdef) for the code point.</summary>
    public bool HasGlyph(int codePoint) => HbFont.TryGetGlyph(codePoint, out var g) && g != 0;

    public void Dispose()
    {
        HbFont.Dispose(); Face.Dispose(); Blob.Dispose(); Typeface.Dispose();
    }
}

/// <summary>The eight font files of the spikes: Carlito from the shell's assets and Caladea from spikes/write/fonts. A missing file throws.</summary>
public sealed class FontSet : IDisposable
{
    readonly Dictionary<(string, bool, bool), FontEntry> _fonts = new();

    public IEnumerable<FontEntry> All => _fonts.Values;

    public static FontSet LoadSpikeFonts(string repoRoot)
    {
        var set = new FontSet();
        string[] styles = { "Regular", "Bold", "Italic", "BoldItalic" };
        foreach (var family in new[] { "Carlito", "Caladea" })
            foreach (var style in styles)
            {
                string dir = family == "Carlito"
                    ? System.IO.Path.Combine(repoRoot, "src", "Raffica.Shell", "Assets", "Fonts")
                    : System.IO.Path.Combine(repoRoot, "spikes", "write", "fonts");
                string path = System.IO.Path.Combine(dir, $"{family}-{style}.ttf");
                if (!File.Exists(path)) throw new FileNotFoundException("spike font missing (no fallback is used)", path);
                set._fonts[(family, style.StartsWith("Bold"), style.EndsWith("Italic"))] = new FontEntry(family, style.StartsWith("Bold"), style.EndsWith("Italic"), path);
            }
        return set;
    }

    public FontEntry Get(string family, bool bold, bool italic) =>
        _fonts.TryGetValue((family, bold, italic), out var f) ? f : throw new KeyNotFoundException($"font {family} bold={bold} italic={italic} is not in the spike set");

    public void Dispose()
    {
        foreach (var f in _fonts.Values) f.Dispose();
        _fonts.Clear();
    }

    /// <summary>The repository root: the folder with Raffica.slnx above the base directory or the current directory.</summary>
    public static string FindRepoRoot()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var dir = new DirectoryInfo(start);
            while (dir is not null)
            {
                if (File.Exists(System.IO.Path.Combine(dir.FullName, "Raffica.slnx"))) return dir.FullName;
                dir = dir.Parent;
            }
        }
        throw new DirectoryNotFoundException("repository root (Raffica.slnx) not found");
    }
}
