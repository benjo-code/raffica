using Raffica.Shell.Frame;
using Raffica.Write.Layout;

namespace Raffica.Write.Commands;

/// <summary>
/// The list of the font picker: the shipped families first (as the names a document uses: Calibri and Cambria), then the installed families, each name once (case ignored), in alphabetical
/// order. Names only: asking the catalog for the installed families reads no font file and makes no face (<see cref="FontCatalog.LoadedFaces"/> stays as it was). The list is read each time the
/// picker opens, never at startup.
/// </summary>
internal static class FontPickerSource
{
    /// <summary>The families in picker order; the id of an item is the family name (what the font command writes).</summary>
    public static IReadOnlyList<PickerItem> Families(FontCatalog fonts)
    {
        ArgumentNullException.ThrowIfNull(fonts);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var items = new List<PickerItem>();
        foreach (var name in FontCatalog.ShippedFamilies)
        {
            if (seen.Add(name))
            {
                items.Add(new PickerItem(name, name));
            }
        }

        foreach (var name in fonts.InstalledFamilies().Select(n => n.Trim()).Where(n => n.Length > 0 && !n.StartsWith('.') && n.Length <= 128).Order(StringComparer.CurrentCultureIgnoreCase))
        {
            if (seen.Add(name))
            {
                items.Add(new PickerItem(name, name));
            }
        }

        return items;
    }
}
