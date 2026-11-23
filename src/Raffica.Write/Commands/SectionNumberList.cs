using System.Text;

namespace Raffica.Write.Commands;

/// <summary>
/// Says which sections a line speaks of, in the UI language: counted from 1, a run of three or more as "3 to 5", the rest as a list ("3, 4 and 6"). Used by the status line of a shared header or footer
/// and by the Number pages card.
/// </summary>
internal static class SectionNumberList
{
    /// <summary>The words for <paramref name="sections"/> (counted from 1, ascending; duplicates are ignored); empty for none.</summary>
    public static string Describe(IEnumerable<int> sections)
    {
        ArgumentNullException.ThrowIfNull(sections);
        var parts = new List<string>();
        var numbers = sections.Distinct().Order().ToList();
        for (var i = 0; i < numbers.Count;)
        {
            var end = i;
            while (end + 1 < numbers.Count && numbers[end + 1] == numbers[end] + 1)
            {
                end++;
            }

            if (end - i >= 2)
            {
                parts.Add(WriteStrings.Fill(WriteStrings.SectionListRangeFormat, numbers[i], numbers[end]));
                i = end + 1;
            }
            else
            {
                parts.Add(Convert.ToString(numbers[i], WriteStrings.Culture)!);
                i++;
            }
        }

        if (parts.Count <= 1)
        {
            return parts.Count == 0 ? string.Empty : parts[0];
        }

        var head = new StringBuilder();
        for (var i = 0; i < parts.Count - 1; i++)
        {
            head.Append(i == 0 ? string.Empty : ", ").Append(parts[i]);
        }

        return WriteStrings.Fill(WriteStrings.SectionListAndFormat, head.ToString(), parts[^1]);
    }
}
