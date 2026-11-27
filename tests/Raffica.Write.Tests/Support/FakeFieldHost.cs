using System.Globalization;
using Raffica.Write.Editing;
using Raffica.Write.Model;

namespace Raffica.Write.Tests.Support;

/// <summary>
/// The test host of the field refresh: heading labels come from <see cref="ReferenceNumbering"/> (no layout), note marks are the 1-based order of the references of each kind in the body, and the page of a paragraph is a map
/// the test controls (later tasks). The labels are read again when the document's <see cref="DocumentEdits.Stamp"/> moves or after <see cref="Invalidate"/> (a test that changes the model without raising).
/// </summary>
internal sealed class FakeFieldHost(WordDocument document) : IFieldHost
{
    private Dictionary<Paragraph, string>? _labels;
    private EditStamp _stamp;

    /// <summary>What <see cref="IsComplete"/> answers.</summary>
    public bool Complete { get; set; } = true;

    /// <summary>The page index and text of a paragraph (by reference); a paragraph that is not in it has no page.</summary>
    public Dictionary<Paragraph, (int PageIndex, string PageText)> Pages { get; } = new(ReferenceEqualityComparer.Instance);

    public bool IsComplete => Complete;

    public void Invalidate() => _labels = null;

    public string? NumberLabel(Paragraph paragraph)
    {
        if (_labels is null || _stamp != document.Edits.Stamp)
        {
            _labels = new Dictionary<Paragraph, string>(ReferenceEqualityComparer.Instance);
            foreach (var (item, label) in ReferenceNumbering.Labels(document))
            {
                _labels[item] = label;
            }

            _stamp = document.Edits.Stamp;
        }

        return _labels.GetValueOrDefault(paragraph);
    }

    public string? ChapterNumber(Paragraph paragraph) => ReferenceNumbering.ChapterNumbers(document).Where(c => ReferenceEquals(c.Paragraph, paragraph)).Select(c => c.Numbers).FirstOrDefault();

    public (int PageIndex, string PageText)? PageOf(TextPosition position) =>
        Pages.TryGetValue(position.Paragraph, out var page) ? page : null;

    public string? NoteMarkText(int noteId, NoteType type)
    {
        var order = 0;
        foreach (var content in ModelWalk.RunContents(document.Body))
        {
            var id = (type, content) switch
            {
                (NoteType.Footnote, FootnoteReference f) => f.Id,
                (NoteType.Endnote, EndnoteReference e) => e.Id,
                _ => (int?)null,
            };
            if (id is null)
            {
                continue;
            }

            order++;
            if (id == noteId)
            {
                return order.ToString(CultureInfo.InvariantCulture);
            }
        }

        return null;
    }
}
