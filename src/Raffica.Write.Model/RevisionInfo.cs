using System.Globalization;

namespace Raffica.Write.Model;

/// <summary>The id, author and date every revision element carries. The date is kept as the text read, so a save writes it back unchanged; <see cref="Date"/> is the parsed view.</summary>
public sealed class RevisionInfo
{
    public RevisionInfo(int id, string? author, string? dateText)
    {
        Id = id;
        Author = author;
        DateText = dateText;
    }

    public int Id { get; set; }

    public string? Author { get; set; }

    public string? DateText { get; set; }

    /// <summary>The date parsed as ISO 8601 (a missing zone counts as UTC), or null when absent or unreadable.</summary>
    public DateTimeOffset? Date => Parse(DateText);

    public static DateTimeOffset? Parse(string? text) =>
        DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date) ? date : null;
}
