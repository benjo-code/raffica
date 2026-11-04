using System.Globalization;

namespace Raffica.Shell.Frame;

/// <summary>One entry of the recent list of the start view: the file name, its folder and when it was last changed.</summary>
public sealed record RecentFile(string Name, string Folder, DateTimeOffset Modified)
{
    /// <summary>The short date in the given UI culture (German <c>03.11.2026</c>), taken from <see cref="Modified"/> in its own offset.</summary>
    public string DateText(CultureInfo ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        return Modified.ToString("d", ui);
    }
}
