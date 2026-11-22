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

/// <summary>The keys of the lists and of the outline in the page view (Task 11 of milestone 4.4a): Tab and Shift+Tab, Enter and Backspace in a list, Alt+Shift with the arrows.</summary>
public sealed class PageViewStructureTests : IDisposable
{
    private static readonly TimeSpan GateTimeout = TimeSpan.FromSeconds(60);
    private readonly string _folder = Directory.CreateTempSubdirectory("raffica-write-structure-keys-").FullName;

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

    private const string ListItem = "<w:pPr><w:numPr><w:ilvl w:val=\"0\"/><w:numId w:val=\"1\"/></w:numPr></w:pPr>";

    private async Task<ViewHarness> OpenAsync(params string[] paragraphs)
    {
        var path = StyledDocx.WriteWithNumbering(_folder, Guid.NewGuid().ToString("N") + ".docx", StyledDocx.StylesWithHeading2, StyledDocx.BulletNumbering, paragraphs);
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

    private static int Level(Paragraph paragraph) => paragraph.Properties.NumberingLevel ?? 0;

    private static int NumId(Paragraph paragraph) => paragraph.Properties.NumberingId ?? 0;

    [AvaloniaFact]
    public async Task TabAtTheStartOfAListParagraphChangesTheLevel()
    {
        using var h = await OpenAsync("<w:r><w:t>plain</w:t></w:r>", ListItem + "<w:r><w:t>Item</w:t></w:r>");
        var session = Place(h, 1, 0);

        Stroke(h, PhysicalKey.Tab);

        Assert.Equal(1, Level(P(h, 1)));
        Assert.Equal("Item", Text(P(h, 1)));
        Assert.Equal(1, session.Editor.History.UndoCount);

        // The key repeats at the start (a second press goes one level deeper); undo puts the level back in one step each.
        Stroke(h, PhysicalKey.Tab);
        Assert.Equal(2, Level(P(h, 1)));
        session.Editor.History.Undo();
        Assert.Equal(1, Level(P(h, 1)));
    }

    [AvaloniaFact]
    public async Task TabInTheMiddleOfTheTextInsertsATab()
    {
        using var h = await OpenAsync(ListItem + "<w:r><w:t>Item</w:t></w:r>", "<w:r><w:t>plain</w:t></w:r>");
        Place(h, 0, 2);
        Stroke(h, PhysicalKey.Tab);
        Assert.Equal("It\tem", Text(P(h, 0)));
        Assert.Equal(0, Level(P(h, 0)));

        // Outside a list the key is a tab as it was.
        Place(h, 1, 0);
        Stroke(h, PhysicalKey.Tab);
        Assert.Equal("\tplain", Text(P(h, 1)));
        Assert.Equal(0, NumId(P(h, 1)));
    }

    [AvaloniaFact]
    public async Task TabOverASelectionOfListParagraphsMovesThemAll()
    {
        using var h = await OpenAsync(ListItem + "<w:r><w:t>one</w:t></w:r>", ListItem + "<w:r><w:t>two</w:t></w:r>");
        var session = h.View.Tab.EnsureEditor();
        _ = session.Selection;
        session.SetSelection(new TextRange(new TextPosition(StoryRef.Body, P(h, 0), 1), new TextPosition(StoryRef.Body, P(h, 1), 2)));
        h.View.Focus();

        Stroke(h, PhysicalKey.Tab);

        Assert.Equal([1, 1], [Level(P(h, 0)), Level(P(h, 1))]);
        Assert.Equal(["one", "two"], [Text(P(h, 0)), Text(P(h, 1))]);
    }

    [AvaloniaFact]
    public async Task ShiftTabAtTheStartOfALevel0ItemKeepsTheLevel()
    {
        using var h = await OpenAsync(ListItem + "<w:r><w:t>Item</w:t></w:r>");
        var session = Place(h, 0, 0);
        var revision = h.View.Tab.Docx.Model.Edits.Revision;

        Stroke(h, PhysicalKey.Tab, RawInputModifiers.Shift);

        Assert.Equal(0, Level(P(h, 0)));
        Assert.Equal("Item", Text(P(h, 0)));
        Assert.Equal(revision, h.View.Tab.Docx.Model.Edits.Revision);
        Assert.Equal(0, session.Editor.History.UndoCount);

        // At level 1 it goes back to level 0.
        Stroke(h, PhysicalKey.Tab);
        Assert.Equal(1, Level(P(h, 0)));
        Stroke(h, PhysicalKey.Tab, RawInputModifiers.Shift);
        Assert.Equal(0, Level(P(h, 0)));
    }

    [AvaloniaFact]
    public async Task EnterOnAnEmptyListItemEndsTheList()
    {
        using var h = await OpenAsync(ListItem + "<w:r><w:t>Item</w:t></w:r>", ListItem);
        Place(h, 1, 0);

        Stroke(h, PhysicalKey.Enter);

        Assert.Equal(2, h.View.Tab.Docx.Model.Body.Blocks.OfType<Paragraph>().Count());
        Assert.Equal(0, NumId(P(h, 1)));
        Assert.Equal(1, NumId(P(h, 0)));
    }

    [AvaloniaFact]
    public async Task BackspaceAtTheStartOfAListItemRemovesTheList()
    {
        using var h = await OpenAsync(ListItem + "<w:r><w:t>Item</w:t></w:r>");
        Place(h, 0, 0);

        Stroke(h, PhysicalKey.Backspace);

        Assert.Equal(0, NumId(P(h, 0)));
        Assert.Equal("Item", Text(P(h, 0)));
    }

    [AvaloniaFact]
    public async Task AltShiftArrowsRunTheOutlineCommandsWithTheCaretInAHeading()
    {
        using var h = await OpenAsync(
            "<w:pPr><w:pStyle w:val=\"Heading1\"/></w:pPr><w:r><w:t>A</w:t></w:r>",
            "<w:r><w:t>under a</w:t></w:r>",
            "<w:pPr><w:pStyle w:val=\"Heading1\"/></w:pPr><w:r><w:t>B</w:t></w:r>",
            "<w:r><w:t>under b</w:t></w:r>");
        // These are the keys of Windows and Linux whatever system runs the test (the Mac has none, see the test below).
        h.View.MacOsKeys = false;
        var session = Place(h, 0, 1);
        var alt = RawInputModifiers.Alt | RawInputModifiers.Shift;

        // Down moves the chapter past the next heading (its subtree is the heading and the paragraph under it).
        Stroke(h, PhysicalKey.ArrowDown, alt);
        Assert.Equal(["B", "under b", "A", "under a"], h.View.Tab.Docx.Model.Body.Blocks.OfType<Paragraph>().Select(Text).ToArray());
        Assert.Equal(1, session.Editor.History.UndoCount);
        Assert.Same(P(h, 2), session.Caret!.Value.Paragraph);
        Assert.Equal(0, session.Caret!.Value.Offset);

        // Up moves it back.
        Stroke(h, PhysicalKey.ArrowUp, alt);
        Assert.Equal(["A", "under a", "B", "under b"], h.View.Tab.Docx.Model.Body.Blocks.OfType<Paragraph>().Select(Text).ToArray());

        // Right demotes the heading, Left promotes it again; the caret stays where it was.
        Stroke(h, PhysicalKey.ArrowRight, alt);
        Assert.Equal("Heading2", P(h, 0).Properties.StyleId);
        Stroke(h, PhysicalKey.ArrowLeft, alt);
        Assert.Equal("Heading1", P(h, 0).Properties.StyleId);

        // A caret in a paragraph that follows a heading acts on that heading (the nearest one at or before the caret).
        Place(h, 1, 3);
        Stroke(h, PhysicalKey.ArrowRight, alt);
        Assert.Equal("Heading2", P(h, 0).Properties.StyleId);
    }

    [AvaloniaFact]
    public async Task OnTheMacAltShiftArrowsRunNoOutlineCommand()
    {
        // The Option and Shift arrows extend the selection on the Mac, so the outline commands have no key there: the chord changes no heading, moves no chapter and records no step.
        using var h = await OpenAsync(
            "<w:pPr><w:pStyle w:val=\"Heading1\"/></w:pPr><w:r><w:t>A</w:t></w:r>",
            "<w:r><w:t>under a</w:t></w:r>",
            "<w:pPr><w:pStyle w:val=\"Heading1\"/></w:pPr><w:r><w:t>B</w:t></w:r>",
            "<w:r><w:t>under b</w:t></w:r>");
        h.View.MacOsKeys = true;
        var session = Place(h, 0, 1);
        var alt = RawInputModifiers.Alt | RawInputModifiers.Shift;
        var revision = h.View.Tab.Docx.Model.Edits.Revision;

        foreach (var key in new[] { PhysicalKey.ArrowDown, PhysicalKey.ArrowUp, PhysicalKey.ArrowRight, PhysicalKey.ArrowLeft })
        {
            Stroke(h, key, alt);
        }

        Assert.Equal(["A", "under a", "B", "under b"], h.View.Tab.Docx.Model.Body.Blocks.OfType<Paragraph>().Select(Text).ToArray());
        Assert.Equal(new string?[] { "Heading1", null, "Heading1", null }, h.View.Tab.Docx.Model.Body.Blocks.OfType<Paragraph>().Select(p => p.Properties.StyleId).ToArray());
        Assert.Equal(revision, h.View.Tab.Docx.Model.Edits.Revision);
        Assert.Equal(0, session.Editor.History.UndoCount);
    }

    [AvaloniaFact]
    public async Task AltShiftArrowsDoNothingBeforeTheFirstHeading()
    {
        using var h = await OpenAsync("<w:r><w:t>intro</w:t></w:r>", "<w:pPr><w:pStyle w:val=\"Heading1\"/></w:pPr><w:r><w:t>A</w:t></w:r>");
        var session = Place(h, 0, 0);
        var revision = h.View.Tab.Docx.Model.Edits.Revision;

        Stroke(h, PhysicalKey.ArrowDown, RawInputModifiers.Alt | RawInputModifiers.Shift);

        Assert.Equal(revision, h.View.Tab.Docx.Model.Edits.Revision);
        Assert.Equal(0, session.Editor.History.UndoCount);
    }

    [AvaloniaFact]
    public async Task AltGrIsTextNotACommand()
    {
        // AltGr arrives as Ctrl and Alt together: with Shift and an arrow it is no outline key, and the keys never take Ctrl (nor, on Windows and Linux, any macOS chord).
        foreach (var key in new[] { Key.Up, Key.Down, Key.Left, Key.Right })
        {
            Assert.Null(EditingKeys.StructureCommandFor(key, KeyModifiers.Control | KeyModifiers.Alt, macOs: false));
            Assert.Null(EditingKeys.StructureCommandFor(key, KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift, macOs: false));
            Assert.Null(EditingKeys.StructureCommandFor(key, KeyModifiers.Alt, macOs: false));
            Assert.Null(EditingKeys.StructureCommandFor(key, KeyModifiers.Shift, macOs: false));
            Assert.Null(EditingKeys.StructureCommandFor(key, KeyModifiers.Alt | KeyModifiers.Shift, macOs: true));
        }

        Assert.Equal(WriteCommandIds.MoveUp, EditingKeys.StructureCommandFor(Key.Up, KeyModifiers.Alt | KeyModifiers.Shift, macOs: false));
        Assert.Equal(WriteCommandIds.MoveDown, EditingKeys.StructureCommandFor(Key.Down, KeyModifiers.Alt | KeyModifiers.Shift, macOs: false));
        Assert.Equal(WriteCommandIds.Promote, EditingKeys.StructureCommandFor(Key.Left, KeyModifiers.Alt | KeyModifiers.Shift, macOs: false));
        Assert.Equal(WriteCommandIds.Demote, EditingKeys.StructureCommandFor(Key.Right, KeyModifiers.Alt | KeyModifiers.Shift, macOs: false));

        using var h = await OpenAsync(
            "<w:pPr><w:pStyle w:val=\"Heading1\"/></w:pPr><w:r><w:t>A</w:t></w:r>",
            "<w:pPr><w:pStyle w:val=\"Heading1\"/></w:pPr><w:r><w:t>B</w:t></w:r>");
        var session = Place(h, 0, 0);
        var revision = h.View.Tab.Docx.Model.Edits.Revision;
        Stroke(h, PhysicalKey.ArrowDown, RawInputModifiers.Control | RawInputModifiers.Alt | RawInputModifiers.Shift);
        Assert.Equal(revision, h.View.Tab.Docx.Model.Edits.Revision);
        Assert.Equal(0, session.Editor.History.UndoCount);
    }
}
