using System.Runtime.ExceptionServices;
using Raffica.Write.Model;

namespace Raffica.Write.Layout;

public sealed partial class DocumentLayout
{
    /// <summary>The text of the input method preedit the pages show now (control characters removed), or null when there is none.</summary>
    public string? PreeditText => _preedit.Current?.Text;

    /// <summary>The position the preedit stands at (the offset is that of the paragraph's projection), or null when there is none.</summary>
    public TextPosition? PreeditAt => _preedit.Current is { } state ? new TextPosition(state.Story, state.Paragraph, state.At) : null;

    /// <summary>
    /// Shows <paramref name="text"/> as the preedit of an input method composition at <paramref name="at"/>, or removes the preedit when the text is null or
    /// empty. The text is laid out inside the paragraph, underlined, in the format of the text around it, and the paragraph and the pages after it are laid out
    /// again as for an edit; it is in no model: the model, its revision and <see cref="Stats"/> stay as they are, and once the preedit is removed the pages are
    /// those of a layout of the model. A preedit in another paragraph is removed first (one preedit at a time). Control characters are dropped. Returns true
    /// when the pages changed, false when nothing did (no preedit to remove, the same preedit again, or a position no page of this layout shows: a paragraph
    /// of another model, a story the layout does not draw, a comment). Pages up to <paramref name="throughPage"/> are right when this returns; the
    /// caller holds the edit gate like for <see cref="ApplyEdit"/>.
    /// </summary>
    public bool SetPreedit(TextPosition at, string? text, int throughPage = 0)
    {
        var clean = CleanPreedit(text);
        lock (_gate)
        {
            _fault?.Throw();
            var current = _preedit.Current;
            if (clean.Length == 0)
            {
                if (current is null)
                {
                    return false;
                }

                _preedit.Current = null;
                Show(current.Story, current.Paragraph, throughPage);
                return true;
            }

            ArgumentNullException.ThrowIfNull(at.Paragraph);
            if (at.Story.Kind == StoryKind.Comment || !IsShown(at.Story, at.Paragraph))
            {
                return false;
            }

            var offset = Math.Max(0, at.Offset);
            if (current is not null && ReferenceEquals(current.Paragraph, at.Paragraph) && current.Story == at.Story && current.At == offset && string.Equals(current.Text, clean, StringComparison.Ordinal))
            {
                return false;
            }

            if (current is not null && (!ReferenceEquals(current.Paragraph, at.Paragraph) || current.Story != at.Story))
            {
                _preedit.Current = null;
                Show(current.Story, current.Paragraph, throughPage);
            }

            _preedit.Current = new PreeditState(at.Story, at.Paragraph, offset, clean);
            Show(at.Story, at.Paragraph, throughPage);
            return true;
        }
    }

    /// <summary>
    /// The caret inside the preedit: <paramref name="cursor"/> UTF-16 units into <see cref="PreeditText"/> (clamped), on the page and line the preedit's text
    /// has there; null when there is no preedit or no published page shows it. A position inside the preedit is no model position, so
    /// <see cref="Caret"/> cannot say where it is.
    /// </summary>
    public CaretBox? PreeditCaret(int cursor, int pageHint) => _preedit.Current is { } state ? Navigator.PreeditCaret(state, cursor, pageHint) : null;

    private static string CleanPreedit(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        foreach (var c in text)
        {
            if (char.IsControl(c))
            {
                return string.Concat(text.Where(ch => !char.IsControl(ch)));
            }
        }

        return text;
    }

    // Whether this layout can show a preedit in the paragraph: a body paragraph of the model it holds (searched through tables and block content controls), or a
    // paragraph of a header, footer or note that a published page shows.
    private bool IsShown(StoryRef story, Paragraph paragraph) =>
        story.Kind == StoryKind.Body ? Find(_engine.Document.Body.Blocks, paragraph, 0) is not null : _paragraphPages.PagesOf(paragraph) is not null;

    // The pages after the preedit changed: laid out again like an edit of the paragraph, but nothing is counted in Stats.
    private void Show(StoryRef story, Paragraph paragraph, int throughPage)
    {
        var change = story.Kind == StoryKind.Body && Find(_engine.Document.Body.Blocks, paragraph, 0) is var (container, index)
            ? new BlockChange(story, container, index, 1, 1, [paragraph])
            : new BlockChange(story, [], 0, 0, 0, []);
        try
        {
            Refill(change, Math.Max(0, throughPage));
        }
        catch (Exception e)
        {
            _fault = ExceptionDispatchInfo.Capture(e);
            throw;
        }
    }

    private static (IList<Block> Container, int Index)? Find(IList<Block> blocks, Paragraph paragraph, int depth)
    {
        for (var i = 0; i < blocks.Count; i++)
        {
            if (ReferenceEquals(blocks[i], paragraph))
            {
                return (blocks, i);
            }
        }

        if (depth > 32)
        {
            return null;
        }

        foreach (var block in blocks)
        {
            switch (block)
            {
                case ContentControlBlock control when Find(control.Content, paragraph, depth + 1) is { } inControl:
                    return inControl;
                case Table table:
                    foreach (var row in table.Rows)
                    {
                        if (FindInRow(row, paragraph, depth + 1) is { } inRow)
                        {
                            return inRow;
                        }
                    }

                    break;
            }
        }

        return null;
    }

    private static (IList<Block> Container, int Index)? FindInRow(RowItem item, Paragraph paragraph, int depth)
    {
        if (depth > 32)
        {
            return null;
        }

        switch (item)
        {
            case TableRow row:
                foreach (var inner in row.Items)
                {
                    if (FindInRow(inner, paragraph, depth + 1) is { } found)
                    {
                        return found;
                    }
                }

                return null;
            case TableCell cell:
                return Find(cell.Blocks, paragraph, depth + 1);
            default:
                return null;
        }
    }
}
