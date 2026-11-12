using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Raffica.Core.Saving;

/// <summary>The operating systems whose path rules differ (the test seam of <see cref="PathRules"/>).</summary>
internal enum PathSystem
{
    Windows,
    Linux,
    MacOS,
}

/// <summary>
/// The one rule for comparing paths and file names: Windows ignores the case, Linux keeps it, macOS ignores the case and
/// treats a precomposed and a decomposed spelling of the same name (NFC and NFD) as one name, as its volumes do.
/// </summary>
public static class PathRules
{
    private static readonly PathSystem s_current = OperatingSystem.IsWindows() ? PathSystem.Windows
        : OperatingSystem.IsMacOS() ? PathSystem.MacOS
        : PathSystem.Linux;

    /// <summary>True on Windows and macOS, where the case of a path does not tell two files apart.</summary>
    public static bool IsCaseInsensitive => s_current != PathSystem.Linux;

    /// <summary>The comparer for dictionaries and sets of paths; it follows the same rule as <see cref="AreEqual(string?, string?)"/>.</summary>
    public static StringComparer Comparer { get; } = new PathComparer(s_current);

    /// <summary>Whether two paths or names are the same on this system; two <see langword="null"/> values are equal.</summary>
    public static bool AreEqual(string? a, string? b) => AreEqual(a, b, s_current);

    internal static bool AreEqual(string? a, string? b, PathSystem system)
    {
        if (a is null || b is null)
        {
            return a is null && b is null;
        }

        switch (system)
        {
            case PathSystem.Windows:
                return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
            case PathSystem.Linux:
                return string.Equals(a, b, StringComparison.Ordinal);
            default:
                return string.Equals(a, b, StringComparison.Ordinal)
                    || string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string Normalize(string value) => Ascii(value) ? value : value.Normalize(NormalizationForm.FormC);

    private static bool Ascii(string value)
    {
        foreach (var c in value)
        {
            if (c > 0x7F)
            {
                return false;
            }
        }

        return true;
    }

    private sealed class PathComparer(PathSystem system) : StringComparer
    {
        public override int Compare(string? x, string? y) => system switch
        {
            PathSystem.Windows => string.Compare(x, y, StringComparison.OrdinalIgnoreCase),
            PathSystem.Linux => string.CompareOrdinal(x, y),
            _ => x is null || y is null
                ? string.CompareOrdinal(x, y)
                : string.Compare(Normalize(x), Normalize(y), StringComparison.OrdinalIgnoreCase),
        };

        public override bool Equals(string? x, string? y) => AreEqual(x, y, system);

        public override int GetHashCode([DisallowNull] string obj)
        {
            ArgumentNullException.ThrowIfNull(obj);
            return system switch
            {
                PathSystem.Windows => StringComparer.OrdinalIgnoreCase.GetHashCode(obj),
                PathSystem.Linux => StringComparer.Ordinal.GetHashCode(obj),
                _ => StringComparer.OrdinalIgnoreCase.GetHashCode(Normalize(obj)),
            };
        }
    }
}
