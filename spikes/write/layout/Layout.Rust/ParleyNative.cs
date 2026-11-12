using System.Runtime.InteropServices;

namespace Layout.Rust;

[StructLayout(LayoutKind.Sequential)]
public struct PcRun { public uint Start, Len, Family, Bold, Italic; public float Size; }

[StructLayout(LayoutKind.Sequential)]
public struct PcLine { public uint Start, Len; public float Ascent, Descent, Width, YMid; }

[StructLayout(LayoutKind.Sequential)]
public struct PcRect { public float X, Y, W, H; }

[StructLayout(LayoutKind.Sequential)]
public struct PcGlyph { public uint FontId, GlyphId; public float X, Y, Size; }

/// <summary>The C interface of spikes/write/layout/parley-core (a Rust cdylib). Text and offsets are UTF-16.</summary>
internal static unsafe partial class ParleyNative
{
    const string Lib = "parley_core";

    [LibraryImport(Lib)] internal static partial nint pc_build_profile();
    [LibraryImport(Lib)] internal static partial nint pc_ctx_create();
    [LibraryImport(Lib)] internal static partial int pc_ctx_add_font(nint ctx, byte* data, nuint len, out uint fontId);
    [LibraryImport(Lib)] internal static partial nint pc_para_layout(nint ctx, char* text, nuint len, PcRun* runs, nuint runCount, float width);
    [LibraryImport(Lib)] internal static partial nuint pc_para_lines(nint para, PcLine* output, nuint cap);
    [LibraryImport(Lib)] internal static partial int pc_para_hit_test(nint para, float x, float y, out uint offset);
    [LibraryImport(Lib)] internal static partial int pc_para_caret(nint para, uint offset, out PcRect rect);
    [LibraryImport(Lib)] internal static partial nuint pc_para_glyphs(nint para, uint firstLine, uint lineCount, PcGlyph* output, nuint cap);
    [LibraryImport(Lib)] internal static partial void pc_para_free(nint para);
    [LibraryImport(Lib)] internal static partial void pc_ctx_free(nint ctx);
}
