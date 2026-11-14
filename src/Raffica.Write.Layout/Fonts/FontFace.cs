using System.Runtime.InteropServices;
using HarfBuzzSharp;
using SkiaSharp;

namespace Raffica.Write.Layout;

/// <summary>
/// One font face: the Skia typeface that draws and measures, the HarfBuzz font that shapes, and the vertical metrics.
/// A face lives as long as its catalog (the process, in practice); it is never disposed while layout can use it.
/// </summary>
public sealed class FontFace : IDisposable
{
    // The font bytes are shared by Skia and HarfBuzz without a copy. They are either part of the loaded assembly (never moves,
    // never unloads) or a pinned array that this field keeps alive; the SKData and the Blob stay referenced for the same reason.
    private readonly byte[]? _pinnedBytes;
    private readonly SKData _data;
    private readonly Blob _blob;
    private readonly Face _face;

    private FontFace(string family, bool bold, bool italic, IntPtr memory, int length, byte[]? pinnedBytes, int faceIndex)
    {
        Family = family;
        Bold = bold;
        Italic = italic;
        _pinnedBytes = pinnedBytes;
        _data = SKData.Create(memory, length) ?? throw new InvalidDataException("Skia cannot hold the font data of " + family);
        Typeface = SKTypeface.FromData(_data, faceIndex) ?? throw new InvalidDataException("Skia cannot load the font " + family);
        _blob = new Blob(memory, length, MemoryMode.ReadOnly, null);
        _face = new Face(_blob, faceIndex);
        Shaper = new Font(_face);

        // Metrics at a large size, divided back, so hinting and rounding do not matter.
        const float probe = 1000f;
        using var font = new SKFont(Typeface, probe);
        font.GetFontMetrics(out var metrics);
        Ascent = -metrics.Ascent / probe;
        Descent = metrics.Descent / probe;
        LineGap = Math.Max(0f, metrics.Leading / probe);
    }

    /// <summary>The family name this face stands for (the shipped family, not the name a document asked for).</summary>
    public string Family { get; }

    public bool Bold { get; }

    public bool Italic { get; }

    public SKTypeface Typeface { get; }

    /// <summary>The HarfBuzz font, at the face's own scale (units per em). Shared: do not change its scale or variations.</summary>
    public Font Shaper { get; }

    /// <summary>Distance from the baseline to the top of a line, per em (positive).</summary>
    public float Ascent { get; }

    /// <summary>Distance from the baseline to the bottom of a line, per em (positive).</summary>
    public float Descent { get; }

    /// <summary>Extra space between two lines, per em.</summary>
    public float LineGap { get; }

    /// <summary>
    /// Releases the native objects. Nothing calls this while a catalog lives; it exists for a face that a caller made itself
    /// and must not run while any layout can still use the face.
    /// </summary>
    public void Dispose()
    {
        Shaper.Dispose();
        _face.Dispose();
        _blob.Dispose();
        Typeface.Dispose();
        _data.Dispose();
        GC.KeepAlive(_pinnedBytes);
    }

    /// <summary>Makes a face over font bytes that stay valid and unmoved for the life of the process (an embedded resource).</summary>
    internal static FontFace FromStaticMemory(string family, bool bold, bool italic, IntPtr memory, int length) =>
        new(family, bold, italic, memory, length, null, 0);

    /// <summary>Makes a face over a copy of the font bytes in a pinned array that the face keeps alive.</summary>
    internal static FontFace FromBytes(string family, bool bold, bool italic, ReadOnlySpan<byte> bytes, int faceIndex = 0)
    {
        var pinned = GC.AllocateUninitializedArray<byte>(bytes.Length, pinned: true);
        bytes.CopyTo(pinned);
        return new FontFace(family, bold, italic, Marshal.UnsafeAddrOfPinnedArrayElement(pinned, 0), pinned.Length, pinned, faceIndex);
    }

    /// <summary>Makes a face for an installed typeface (for an <see cref="ISystemFonts"/> implementation). The font bytes are read once.</summary>
    public static FontFace? FromTypeface(SKTypeface typeface, string family, bool bold, bool italic)
    {
        ArgumentNullException.ThrowIfNull(typeface);
        ArgumentNullException.ThrowIfNull(family);
        using var stream = typeface.OpenStream(out var faceIndex);
        if (stream is null || stream.Length <= 0)
        {
            return null;
        }

        var bytes = new byte[stream.Length];
        if (stream.Read(bytes, bytes.Length) != bytes.Length)
        {
            return null;
        }

        return FromBytes(family, bold, italic, bytes, faceIndex);
    }
}
