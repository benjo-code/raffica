// Copied from src/Raffica.Pdf/Platform/Windows/PrintDialogNative.cs: PrintPageRange, PrintDlgEx and the PrintDlgExW call. Left out: the mapping to
// PrintSettings and DEVMODE (duplex, orientation, printer name), which the spike does not need, and the Raffica.Pdf strings. Spike code.
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace PrintSpike.Interop;

[StructLayout(LayoutKind.Sequential)]
internal struct PrintPageRange { public uint FromPage; public uint ToPage; }

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct PrintDlgEx
{
    public uint StructSize;
    public nint Owner;
    public nint DevMode;
    public nint DevNames;
    public nint DeviceContext;
    public uint Flags;
    public uint Flags2;
    public uint ExclusionFlags;
    public uint PageRangeCount;
    public uint MaxPageRanges;
    public PrintPageRange* PageRanges;
    public uint MinPage;
    public uint MaxPage;
    public uint Copies;
    public nint Instance;
    public char* PrintTemplateName;
    public nint Callback;
    public uint PropertyPageCount;
    public nint* PropertyPages;
    public uint StartPage;
    public uint ResultAction;
}

/// <summary>What the dialog returned: the device context of the printer (the caller deletes it) and the first and last page of the range.</summary>
internal sealed record DialogResult(nint DeviceContext, int FromPage, int ToPage, int Copies);

[SupportedOSPlatform("windows")]
internal static unsafe partial class PrintDialogNative
{
    const uint ReturnDc = 0x00000100, NoSelection = 0x00000004, ResultPrint = 1, StartPageGeneral = 0xFFFFFFFF;
    const int MaxRanges = 32;

    public static DialogResult? Show(nint owner, int pageCount)
    {
        if (owner == 0) throw new InvalidOperationException("the print dialog needs a window handle");
        var ranges = (PrintPageRange*)NativeMemory.AllocZeroed((nuint)(MaxRanges * sizeof(PrintPageRange)));
        var dialog = new PrintDlgEx
        {
            StructSize = (uint)sizeof(PrintDlgEx), Owner = owner, Flags = ReturnDc | NoSelection,
            PageRangeCount = 1, MaxPageRanges = MaxRanges, PageRanges = ranges, MinPage = 1, MaxPage = (uint)pageCount, Copies = 1,
            StartPage = StartPageGeneral,
        };
        ranges[0] = new PrintPageRange { FromPage = 1, ToPage = (uint)pageCount };
        try
        {
            int result = PrintDlgExW(&dialog);
            if (result != 0) throw new InvalidOperationException("PrintDlgEx failed: " + result);
            if (dialog.ResultAction != ResultPrint || dialog.DeviceContext == 0)
            {
                if (dialog.DeviceContext != 0) Gdi.DeleteDC(dialog.DeviceContext);
                return null;
            }
            int from = 1, to = pageCount;
            if (dialog.PageRangeCount > 0 && ranges[0].FromPage > 0) { from = (int)ranges[0].FromPage; to = (int)ranges[0].ToPage; }
            return new DialogResult(dialog.DeviceContext, from, Math.Min(to, pageCount), (int)Math.Max(1, dialog.Copies));
        }
        finally
        {
            NativeMemory.Free(ranges);
            if (dialog.DevMode != 0) GlobalMemory.GlobalFree(dialog.DevMode);
            if (dialog.DevNames != 0) GlobalMemory.GlobalFree(dialog.DevNames);
        }
    }

    [LibraryImport("comdlg32.dll")]
    private static partial int PrintDlgExW(PrintDlgEx* dialog);
}
