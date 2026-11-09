using System.Globalization;

namespace Raffica.Shell.Frame;

/// <summary>
/// Finds the commands of a ribbon by name for the search field. Every word of the query must start a word of the command name
/// (case-insensitive, with the comparison rules of the given culture); commands inside menus are found too. The result follows the
/// ribbon order (tab, group, position; a menu command comes before its items) and holds each command once. An empty query finds nothing.
/// </summary>
public static class CommandMatcher
{
    public static IReadOnlyList<CommandDefinition> Find(RibbonDefinition ribbon, string query, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(ribbon);
        ArgumentNullException.ThrowIfNull(culture);
        var words = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return [];
        }

        var found = new List<CommandDefinition>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tab in ribbon.Tabs)
        {
            foreach (var group in tab.Groups)
            {
                foreach (var command in group.Commands)
                {
                    Collect(command, words, culture, found, seen);
                }
            }
        }

        return found;
    }

    private static void Collect(CommandDefinition command, string[] words, CultureInfo culture, List<CommandDefinition> found, HashSet<string> seen)
    {
        if (Matches(command.Name, words, culture) && seen.Add(command.Id))
        {
            found.Add(command);
        }

        if (command.Items is { } items)
        {
            foreach (var item in items)
            {
                Collect(item, words, culture, found, seen);
            }
        }
    }

    private static bool Matches(string name, string[] words, CultureInfo culture)
    {
        var nameWords = SplitWords(name);
        foreach (var word in words)
        {
            var any = false;
            foreach (var nameWord in nameWords)
            {
                if (culture.CompareInfo.IsPrefix(nameWord, word, CompareOptions.IgnoreCase))
                {
                    any = true;
                    break;
                }
            }

            if (!any)
            {
                return false;
            }
        }

        return true;
    }

    private static List<string> SplitWords(string name)
    {
        var words = new List<string>();
        var start = -1;
        for (var i = 0; i <= name.Length; i++)
        {
            var inWord = i < name.Length && char.IsLetterOrDigit(name[i]);
            if (inWord && start < 0)
            {
                start = i;
            }
            else if (!inWord && start >= 0)
            {
                words.Add(name[start..i]);
                start = -1;
            }
        }

        return words;
    }
}
