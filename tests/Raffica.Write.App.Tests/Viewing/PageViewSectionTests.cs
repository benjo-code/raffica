using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Raffica.Write.Commands;
using Raffica.Write.Documents;
using Raffica.Write.Editing;
using Raffica.Write.Model;
using Raffica.WriteApp.Tests.Commands;
using Xunit;

namespace Raffica.WriteApp.Tests.Viewing;

/// <summary>The keys that meet a section break in the page view (milestone 4.4b, Task 11): the editor refuses to delete or merge a paragraph that ends a section and the view says where the break can be taken away.</summary>
public sealed class PageViewSectionTests : IDisposable
{
    private static readonly TimeSpan GateTimeout = TimeSpan.FromSeconds(60);
    private readonly string _folder = Directory.CreateTempSubdirectory("raffica-write-section-keys-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // The system cleans the temp folder.
        }
    }

    private const string Break = "<w:pPr><w:sectPr><w:type w:val=\"nextPage\"/></w:sectPr></w:pPr>";

    private async Task<ViewHarness> OpenAsync(params string[] paragraphs)
    {
        var path = StyledDocx.Write(_folder, Guid.NewGuid().ToString("N") + ".docx", StyledDocx.Styles, paragraphs);
        var harness = new ViewHarness(path, 900, 1400);
        harness.View.Runner.LeaseWaitTimeout = GateTimeout;
        harness.View.Runner.Start();
        await ViewHarness.WaitAsync(() => harness.View.Runner.FullModelReady || harness.View.Runner.IsFaulted, 30000);
        Assert.True(harness.View.Runner.FullModelReady);
        return harness;
    }

    private static void Stroke(ViewHarness h, PhysicalKey key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        h.Window.KeyPressQwerty(key, modifiers);
        h.Window.KeyReleaseQwerty(key, modifiers);
        ViewHarness.Pump();
    }

    private static EditSession Place(ViewHarness h, int paragraph, int offset)
    {
        var session = h.View.Tab.EnsureEditor();
        _ = session.Selection;
        var start = new TextPosition(StoryRef.Body, P(h, paragraph), offset);
        session.SetSelection(new TextRange(start, start));
        h.View.Focus();
        return session;
    }

    private static Paragraph P(ViewHarness h, int index) => (Paragraph)h.View.Tab.Docx.Model.Body.Blocks[index];

    private static string Text(Paragraph paragraph) => ParagraphProjection.Build(paragraph, ProjectionHost.Plain).Text;

    [AvaloniaFact]
    public async Task BackspaceIntoASectionBreakShowsAReasonThatPointsToTheMarker()
    {
        using var h = await OpenAsync(Break + "<w:r><w:t>one</w:t></w:r>", "<w:r><w:t>two</w:t></w:r>");
        var notes = new List<string>();
        h.View.StructureNote += notes.Add;
        Place(h, 1, 0);
        var revision = h.View.Tab.Docx.Model.Edits.Revision;

        Stroke(h, PhysicalKey.Backspace);

        // Nothing was merged or removed, and the words say where the break can be taken away.
        Assert.Equal([WriteStrings.NoteSectionBreakKey], notes);
        Assert.Contains("marker", WriteStrings.NoteSectionBreakKey, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(WriteStrings.Get("CmdRemoveSection"), WriteStrings.NoteSectionBreakKey, StringComparison.Ordinal);
        Assert.Equal(revision, h.View.Tab.Docx.Model.Edits.Revision);
        Assert.NotNull(P(h, 0).SectionBreak);
        Assert.Equal("one", Text(P(h, 0)));
        Assert.Equal("two", Text(P(h, 1)));
    }

    [AvaloniaFact]
    public async Task DeleteAtTheEndOfTheBreakParagraphAndARangeOverItShowTheSameReason()
    {
        using var h = await OpenAsync(Break + "<w:r><w:t>one</w:t></w:r>", "<w:r><w:t>two</w:t></w:r>");
        var notes = new List<string>();
        h.View.StructureNote += notes.Add;

        // Delete at the end of the paragraph that ends the section would merge the next paragraph into it.
        Place(h, 0, 3);
        Stroke(h, PhysicalKey.Delete);
        Assert.Equal([WriteStrings.NoteSectionBreakKey], notes);
        Assert.Equal(2, h.View.Tab.Docx.Model.Body.Blocks.Count);

        // A selection from the end of the first paragraph into the second removes the same break.
        var session = Place(h, 0, 1);
        session.SetSelection(new TextRange(new TextPosition(StoryRef.Body, P(h, 0), 1), new TextPosition(StoryRef.Body, P(h, 1), 1)));
        notes.Clear();
        Stroke(h, PhysicalKey.Delete);
        Assert.Equal([WriteStrings.NoteSectionBreakKey], notes);
        Assert.NotNull(P(h, 0).SectionBreak);
        Assert.Equal("one", Text(P(h, 0)));
        Assert.Equal("two", Text(P(h, 1)));
    }

    [AvaloniaFact]
    public async Task OrdinaryDeletesNextToABreakAndWithoutOneSayNothing()
    {
        using var h = await OpenAsync(Break + "<w:r><w:t>one</w:t></w:r>", "<w:r><w:t>two</w:t></w:r>", "<w:r><w:t>three</w:t></w:r>");
        var notes = new List<string>();
        h.View.StructureNote += notes.Add;

        // A letter is deleted before the caret in the paragraph after the break, and at the end of the last paragraph there is nothing to delete.
        Place(h, 1, 2);
        Stroke(h, PhysicalKey.Backspace);
        Assert.Equal("to", Text(P(h, 1)));

        // Two paragraphs with no break between them merge as before.
        Place(h, 2, 0);
        Stroke(h, PhysicalKey.Backspace);
        Assert.Equal("tothree", Text(P(h, 1)));
        Place(h, 1, Text(P(h, 1)).Length);
        Stroke(h, PhysicalKey.Delete);
        Assert.Empty(notes);
        Assert.NotNull(P(h, 0).SectionBreak);
    }

    [AvaloniaFact]
    public void ThePlaceOfACardCommandIsNotDescribedForAPrefixAndNothingIsMade()
    {
        // The runner is not started, so only the first blocks are open: the facts say why, the session is not made and the document is untouched.
        var path = StyledDocx.Write(_folder, "long.docx", StyledDocx.Styles, [.. Enumerable.Range(0, 200).Select(i => $"<w:r><w:t>p{i}</w:t></w:r>")]);
        using var h = new ViewHarness(path, 900, 1400);
        Assert.True(h.View.Tab.Docx.IsPrefix);
        var revision = h.View.Tab.Docx.Model.Edits.Revision;

        Assert.Null(h.View.SectionFactsAtCaret(out var note));
        Assert.Equal(WriteStrings.ReasonSectionPrefix, note);
        Assert.Null(h.View.CaretSectionSetup(out var setupNote));
        Assert.Equal(WriteStrings.ReasonSectionPrefix, setupNote);
        Assert.Null(h.View.Tab.Editor);
        Assert.Equal(revision, h.View.Tab.Docx.Model.Edits.Revision);
    }
}
