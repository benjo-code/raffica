using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace PrintSpike.Interop;

// The drawing side of GDI that Write needs and Raffica PDF does not (PDFium draws there): fonts, glyph output, pens, bitmaps.
// New code of the spike; the print dialog and the job calls come from Gdi.cs.

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct LogFont
{
    public int Height, Width, Escapement, Orientation, Weight;
    public byte Italic, Underline, StrikeOut, CharSetId, OutPrecision, ClipPrecision, Quality, PitchAndFamily;
    public fixed char FaceName[32];
}

[StructLayout(LayoutKind.Sequential)]
internal struct Rect { public int Left, Top, Right, Bottom; }

[StructLayout(LayoutKind.Sequential)]
internal struct BitmapInfoHeader
{
    public uint Size; public int Width, Height; public ushort Planes, BitCount; public uint Compression, SizeImage;
    public int XPelsPerMeter, YPelsPerMeter; public uint ClrUsed, ClrImportant;
}

[SupportedOSPlatform("windows")]
internal static unsafe partial class GdiDrawing
{
    public const uint EtoGlyphIndex = 0x0010;
    public const uint TaBaseline = 24;
    public const int Transparent = 1;
    public const int NullBrush = 5;
    public const int PhysicalOffsetX = 112, PhysicalOffsetY = 113;

    [LibraryImport("gdi32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint CreateFontIndirectW(LogFont* font);

    [LibraryImport("gdi32.dll")]
    public static partial nint SelectObject(nint dc, nint obj);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeleteObject(nint obj);

    [LibraryImport("gdi32.dll")]
    public static partial uint SetTextAlign(nint dc, uint mode);

    [LibraryImport("gdi32.dll")]
    public static partial int SetBkMode(nint dc, int mode);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ExtTextOutW(nint dc, int x, int y, uint options, Rect* rect, char* text, uint count, int* dx);

    [LibraryImport("gdi32.dll")]
    public static partial nint CreatePen(int style, int width, uint color);

    [LibraryImport("gdi32.dll")]
    public static partial nint CreateSolidBrush(uint color);

    [LibraryImport("gdi32.dll")]
    public static partial nint GetStockObject(int index);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool Rectangle(nint dc, int left, int top, int right, int bottom);

    [LibraryImport("user32.dll")]
    public static partial int FillRect(nint dc, Rect* rect, nint brush);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool MoveToEx(nint dc, int x, int y, nint previous);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool LineTo(nint dc, int x, int y);

    [LibraryImport("gdi32.dll")]
    public static partial int StretchDIBits(nint dc, int xDest, int yDest, int wDest, int hDest, int xSrc, int ySrc, int wSrc, int hSrc, byte* bits, BitmapInfoHeader* info, uint usage, uint rop);

    [LibraryImport("gdi32.dll")]
    public static partial nint AddFontMemResourceEx(byte* font, uint size, nint reserved, uint* count);
}
