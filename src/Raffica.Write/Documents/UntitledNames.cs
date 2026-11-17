namespace Raffica.Write.Documents;

/// <summary>The title of a new document: "Untitled", then "Untitled 2" and so on, the smallest number no untitled tab of the window has (German: "Unbenannt", "Unbenannt 2").</summary>
internal static class UntitledNames
{
    /// <summary>The title for the next untitled tab among <paramref name="tabs"/>; tabs that belong to a file are ignored.</summary>
    public static string Next(IEnumerable<WriteTab> tabs)
    {
        ArgumentNullException.ThrowIfNull(tabs);
        var used = tabs.Where(t => t.IsUntitled).Select(t => t.Title).ToHashSet(StringComparer.Ordinal);
        for (var number = 1; ; number++)
        {
            var title = NameOf(number);
            if (!used.Contains(title))
            {
                return title;
            }
        }
    }

    private static string NameOf(int number) => number == 1 ? WriteStrings.UntitledName : WriteStrings.Fill(WriteStrings.UntitledNumberedFormat, number);
}
