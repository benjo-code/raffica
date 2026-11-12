namespace Raffica.Pdf.Commands;

/// <summary>Whether the window offers the Alt key tips (field codes). macOS does not: Option types characters and the menu bar replaces the codes.</summary>
internal static class KeyTips
{
    /// <summary>True on every system except macOS.</summary>
    public static bool Offered(bool macOs) => !macOs;
}
