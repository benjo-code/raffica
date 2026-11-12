using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Raffica.Pdf.Platform.Linux;

/// <summary><c>cups_option_t</c> from <c>cups/options.h</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct CupsOption
{
    public byte* Name;
    public byte* Value;
}

/// <summary><c>cups_dest_t</c> from <c>cups/dest.h</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct CupsDest
{
    public byte* Name;
    public byte* Instance;
    public int IsDefault;
    public int OptionCount;
    public CupsOption* Options;
}

/// <summary>
/// The few functions of libcups 2 that printing needs (<c>cups/cups.h</c>, <c>cups/dest.h</c>). The library is looked
/// for on first use, never at startup; <see cref="IsAvailable"/> says whether it is there. On Linux and macOS.
/// The imports use the logical name <c>cups</c>; a resolver set in the static constructor maps it to the file name
/// of the system.
/// </summary>
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macos")]
internal static unsafe partial class CupsNative
{
    private const string Library = "cups";

    private static readonly Lazy<bool> Available = new(Probe);

    static CupsNative()
    {
        NativeLibrary.SetDllImportResolver(typeof(CupsNative).Assembly, Resolve);
    }

    /// <summary>Gets whether libcups can be loaded.</summary>
    public static bool IsAvailable => Available.Value;

    /// <summary><c>cupsGetDests2</c>: lists the destinations; returns their number.</summary>
    [LibraryImport(Library)]
    public static partial int cupsGetDests2(nint http, CupsDest** dests);

    [LibraryImport(Library)]
    public static partial void cupsFreeDests(int count, CupsDest* dests);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    public static partial CupsDest* cupsGetNamedDest(nint http, string? name, string? instance);

    [LibraryImport(Library)]
    public static partial nint cupsCopyDestInfo(nint http, CupsDest* dest);

    [LibraryImport(Library)]
    public static partial void cupsFreeDestInfo(nint info);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int cupsCheckDestSupported(nint http, CupsDest* dest, nint info, string option, string? value);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int cupsAddOption(string name, string value, int count, CupsOption** options);

    [LibraryImport(Library)]
    public static partial void cupsFreeOptions(int count, CupsOption* options);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int cupsPrintFile2(nint http, string printer, string fileName, string title, int count, CupsOption* options);

    [LibraryImport(Library)]
    public static partial byte* cupsLastErrorString();

    /// <summary>Gets the file name of libcups on a system: <c>libcups.so.2</c> on Linux, <c>libcups.2.dylib</c> on macOS (found in <c>/usr/lib</c>), else <see langword="null"/>.</summary>
    /// <param name="linux">Whether the system is Linux.</param>
    /// <param name="macOs">Whether the system is macOS.</param>
    /// <returns>The file name, or <see langword="null"/> when CUPS is not used on this system.</returns>
    internal static string? LibraryNameFor(bool linux, bool macOs) =>
        linux ? "libcups.so.2" : macOs ? "libcups.2.dylib" : null;

    private static string? CurrentLibraryName() => LibraryNameFor(OperatingSystem.IsLinux(), OperatingSystem.IsMacOS());

    private static nint Resolve(string libraryName, System.Reflection.Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName != Library)
        {
            return 0;
        }

        var name = CurrentLibraryName();
        return name is not null && NativeLibrary.TryLoad(name, assembly, searchPath, out var handle) ? handle : 0;
    }

    private static bool Probe()
    {
        var name = CurrentLibraryName();
        if (name is null || !NativeLibrary.TryLoad(name, typeof(CupsNative).Assembly, null, out var handle))
        {
            return false;
        }

        NativeLibrary.Free(handle);
        return true;
    }
}
