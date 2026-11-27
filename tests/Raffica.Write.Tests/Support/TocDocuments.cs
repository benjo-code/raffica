using System.Xml.Linq;
using Raffica.Write.Docx;
using Raffica.Write.Editing;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using Xunit;

namespace Raffica.Write.Tests.Support;

/// <summary>New documents of Write with numbered headings, and readings of the entries of their tables of contents (milestone 4.5b: the shape of an entry).</summary>
internal static class TocDocuments
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    /// <summary>A blank A4 document with one paragraph per line (a line of a style given that style), its headings numbered with the level texts given.</summary>
    public static (DocxFile File, TextEditor Editor) Numbered(IReadOnlyList<(string? Style, string Text)> lines, string[]? levels = null, string? format = null, int start = 1, string? suffix = null)
    {
        var file = DocxFile.Open(BlankDocument.Create(PaperSize.A4));
        var editor = new TextEditor(file.Model, (_, _) => ProjectionHost.Plain, format: new LayoutEffectiveFormat(file.Model));
        var first = file.Model.Body.Blocks.OfType<Paragraph>().First();
        Assert.NotNull(editor.InsertText(Caret(new TextPosition(StoryRef.Body, first, 0)), string.Join("\n", lines.Select(l => l.Text))));
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].Style is { } style)
            {
                editor.ApplyParagraphStyle(Caret(new TextPosition(StoryRef.Body, (Paragraph)file.Model.Body.Blocks[i], 0)), style);
            }
        }

        var heading = file.Model.Body.Blocks.OfType<Paragraph>().First(p => p.Properties.StyleId == "Heading1");
        Assert.NotNull(editor.SetHeadingNumbering(true, new TextPosition(StoryRef.Body, heading, 0)));
        foreach (var level in file.Model.Numbering!.Abstracts.Where(a => a.Levels.Any(l => l.Level == 0 && l.Text?.Contains("%1", StringComparison.Ordinal) == true)).SelectMany(a => a.Levels))
        {
            if (levels is not null && level.Level < levels.Length)
            {
                level.Text = levels[level.Level];
            }

            if (level.Level < 3)
            {
                level.Start = start;
                level.Format = format ?? level.Format;
                if (suffix is not null)
                {
                    level.KeptChildren.Add(new KeptChild(OrderedElement.NewPosition, new UnknownContent(new XElement(W + "suff", new XAttribute(W + "val", suffix)))));
                }
            }
        }

        // A level text that was changed by hand has to reach the labels.
        file.Model.Edits.Raise();
        return (file, editor);
    }

    /// <summary>Gives the first level of the numbered headings another level text and start.</summary>
    public static void Renumber(WordDocument document, string text, int start)
    {
        foreach (var level in document.Numbering!.Abstracts.SelectMany(a => a.Levels).Where(l => l.Level == 0 && l.Text is { Length: > 0 }))
        {
            level.Text = text;
            level.Start = start;
        }

        document.Edits.Raise();
    }

    public static TextRange Caret(TextPosition at) => new(at, at);

    /// <summary>The paragraphs of the entries of the table of contents number <paramref name="index"/> of the body (a paragraph that holds only the end mark is not one).</summary>
    public static List<Paragraph> Entries(WordDocument document, int index = 0)
    {
        var site = TocRefresh.Find(document.Body.Blocks)[index];
        var last = TocRefresh.Analyze(site)?.TailParagraph == true ? site.Last - 1 : site.Last;
        return [.. Enumerable.Range(site.First, last - site.First + 1).Select(i => (Paragraph)site.Container[i])];
    }

    /// <summary>The stored text of an entry, a tab shown as <c>|</c> (instruction text left out).</summary>
    public static string Line(Paragraph paragraph) =>
        string.Concat(ModelWalk.Inlines(paragraph.Inlines).OfType<Run>().SelectMany(r => r.Content).Select(c => c switch { TextContent t => t.Text, TabContent => "|", _ => string.Empty }));

    /// <summary>The direct tab stops of the paragraph in the order they are written: the kind, the position and the leader.</summary>
    public static List<(string Kind, int Position, string? Leader)> Tabs(Paragraph paragraph) =>
        [.. (paragraph.Properties.Get(W + "tabs")?.Elements(W + "tab") ?? []).Select(t => ((string)t.Attribute(W + "val")!, (int)t.Attribute(W + "pos")!, (string?)t.Attribute(W + "leader")))];

    /// <summary>The tab stops as one string, for example <c>left:440 right:9072/dot</c>.</summary>
    public static string TabText(Paragraph paragraph) =>
        string.Join(' ', Tabs(paragraph).Select(t => $"{t.Kind}:{t.Position}{(t.Leader is null ? string.Empty : "/" + t.Leader)}"));

    public static string Instruction(WordDocument document, int index = 0) => TocRefresh.Find(document.Body.Blocks)[index].Instruction.Trim();
}
