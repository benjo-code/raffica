using System.Collections.Concurrent;

namespace Raffica.Write.Layout;

/// <summary>
/// Turns the font name of a document into a face. Carlito (for Calibri) and Caladea (for Cambria) ship in the assembly; a
/// name that is neither shipped nor an alias is looked up among the installed fonts, and failing that falls back to a shipped
/// default (Caladea for serif-like names, Carlito for everything else). A face is made on first use and cached.
/// </summary>
public sealed class FontCatalog
{
    private const string ResourcePrefix = "Raffica.Write.Layout.Fonts.";
    private const string Sans = "Carlito";
    private const string Serif = "Caladea";

    private static readonly string[] s_serifNames =
    [
        "times new roman", "times", "georgia", "garamond", "palatino", "palatino linotype", "book antiqua", "bookman old style",
        "century schoolbook", "century", "constantia", "baskerville", "didot", "minion pro", "cambria math", "sitka text", "rockwell",
    ];

    private readonly ISystemFonts _system;
    private readonly ConcurrentDictionary<(string Family, bool Bold, bool Italic), Lazy<FontFace>> _shipped = new();
    private readonly ConcurrentDictionary<(string Name, bool Bold, bool Italic), FontFace> _resolved = new();
    private int _loaded;

    private FontCatalog(ISystemFonts system) => _system = system;

    /// <summary>The number of shipped faces loaded so far (a catalog loads none before the first <see cref="Resolve"/>).</summary>
    public int LoadedFaces => Volatile.Read(ref _loaded);

    /// <summary>Makes a catalog of the shipped fonts. <paramref name="system"/> serves installed fonts; null means the operating system's.</summary>
    public static FontCatalog CreateShipped(ISystemFonts? system = null) => new(system ?? new SkiaSystemFonts());

    /// <summary>The families that ship in the assembly, as the names a document uses (Calibri and Cambria stand for the shipped metric-compatible faces Carlito and Caladea).</summary>
    public static IReadOnlyList<string> ShippedFamilies { get; } = ["Calibri", "Cambria"];

    /// <summary>
    /// The names of the installed font families, in the order the system gives them; names only (no face is made, <see cref="LoadedFaces"/> does not rise). Called when a font list opens,
    /// never at startup; a failing font manager answers none.
    /// </summary>
    public IReadOnlyList<string> InstalledFamilies()
    {
        try
        {
            return [.. _system.Families()];
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return [];
        }
    }

    /// <summary>The face for a requested font. Never null and never throws; the caller keeps the requested name.</summary>
    public FontFace Resolve(string family, bool bold, bool italic)
    {
        var name = (family ?? string.Empty).Trim();
        var key = (name.ToUpperInvariant(), bold, italic);
        if (_resolved.TryGetValue(key, out var known))
        {
            return known;
        }

        return _resolved.GetOrAdd(key, Find(name, bold, italic));
    }

    private FontFace Find(string name, bool bold, bool italic)
    {
        var shipped = ShippedFamilyFor(name);
        if (shipped is not null)
        {
            return Shipped(shipped, bold, italic);
        }

        if (name.Length > 0)
        {
            try
            {
                var installed = _system.Match(name, bold, italic);
                if (installed is not null)
                {
                    return installed;
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // A failing font manager is the same as a missing font.
            }
        }

        return Shipped(LooksLikeSerif(name) ? Serif : Sans, bold, italic);
    }

    private static string? ShippedFamilyFor(string name)
    {
        if (name.Equals("Carlito", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Calibri", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Calibri Light", StringComparison.OrdinalIgnoreCase))
        {
            return Sans;
        }

        if (name.Equals("Caladea", StringComparison.OrdinalIgnoreCase) || name.Equals("Cambria", StringComparison.OrdinalIgnoreCase))
        {
            return Serif;
        }

        return null;
    }

    private static bool LooksLikeSerif(string name)
    {
        foreach (var serif in s_serifNames)
        {
            if (name.Equals(serif, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return name.Contains("serif", StringComparison.OrdinalIgnoreCase) && !name.Contains("sans", StringComparison.OrdinalIgnoreCase);
    }

    private FontFace Shipped(string family, bool bold, bool italic) =>
        _shipped.GetOrAdd((family, bold, italic), k => new Lazy<FontFace>(() => Load(k.Family, k.Bold, k.Italic))).Value;

    private unsafe FontFace Load(string family, bool bold, bool italic)
    {
        var style = (bold, italic) switch
        {
            (false, false) => "Regular",
            (true, false) => "Bold",
            (false, true) => "Italic",
            _ => "BoldItalic",
        };
        var resource = ResourcePrefix + family + "-" + style + ".ttf";
        using var stream = typeof(FontCatalog).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException("The font resource is missing: " + resource);
        FontFace face;
        if (stream is UnmanagedMemoryStream memory)
        {
            face = FontFace.FromStaticMemory(family, bold, italic, (IntPtr)memory.PositionPointer, checked((int)memory.Length));
        }
        else
        {
            var bytes = new byte[stream.Length];
            stream.ReadExactly(bytes);
            face = FontFace.FromBytes(family, bold, italic, bytes);
        }

        Interlocked.Increment(ref _loaded);
        return face;
    }
}
