using Avalonia;
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

/// <summary>A table of contents in the page view: the caret rests only at its edges (a click goes to the nearer edge, a key steps over the field) and an edit that is refused says why.</summary>
public sealed class PageViewTocTests : IDisposable
{
    private static readonly TimeSpan GateTimeout = TimeSpan.FromSeconds(60);
    private readonly string _folder = Directory.CreateTempSubdirectory("raffica-write-toc-view-").FullName;

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

    private const string Begin = "<w:r><w:fldChar w:fldCharType=\"begin\"/></w:r><w:r><w:instrText xml:space=\"preserve\"> TOC \\o \"1-3\" \\h </w:instrText></w:r><w:r><w:fldChar w:fldCharType=\"separate\"/></w:r>";
    private const string End = "<w:r><w:fldChar w:fldCharType=\"end\"/></w:r>";

    // Before, three entries (the begin in the first, the end in the last), After: blocks 1 to 3 are the field.
    private async Task<ViewHarness> OpenAsync()
    {
        var path = StyledDocx.Write(
            _folder,
            Guid.NewGuid().ToString("N") + ".docx",
            StyledDocx.StylesWithHeading2,
            "<w:r><w:t>Before</w:t></w:r>",
            Begin + "<w:r><w:t>Entry one</w:t></w:r>",
            "<w:r><w:t>Entry two</w:t></w:r>",
            "<w:r><w:t>Entry three</w:t></w:r>" + End,
            "<w:r><w:t>After</w:t></w:r>");
        var harness = new ViewHarness(path, 900, 1000);
        harness.View.Runner.LeaseWaitTimeout = GateTimeout;
        harness.View.Runner.Start();
        await ViewHarness.WaitAsync(() => harness.View.Runner.FullModelReady || harness.View.Runner.IsFaulted, 30000);
        Assert.True(harness.View.Runner.FullModelReady);
        var layout = harness.View.Runner.PageLayout;
        await ViewHarness.WaitAsync(() => layout.IsComplete, 30000);
        return harness;
    }

    private static Paragraph P(ViewHarness h, int index) => (Paragraph)h.View.Tab.Docx.Model.Body.Blocks[index];

    private static string Text(Paragraph paragraph) => ParagraphProjection.Build(paragraph, ProjectionHost.Plain).Text;

    private static void Stroke(ViewHarness h, PhysicalKey key)
    {
        h.Window.KeyPressQwerty(key, RawInputModifiers.None);
        h.Window.KeyReleaseQwerty(key, RawInputModifiers.None);
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

    [AvaloniaFact]
    public async Task ARightArrowFromTheStartEdgeStepsOverTheWholeField()
    {
        using var h = await OpenAsync();
        var session = Place(h, 1, 0);

        Stroke(h, PhysicalKey.ArrowRight);

        var caret = session.Caret!.Value;
        Assert.Same(P(h, 3), caret.Paragraph);
        Assert.Equal(Text(P(h, 3)).Length, caret.Offset);

        // And back: one press from the end edge goes to the start edge.
        Stroke(h, PhysicalKey.ArrowLeft);
        Assert.Same(P(h, 1), session.Caret!.Value.Paragraph);
        Assert.Equal(0, session.Caret!.Value.Offset);
    }

    [AvaloniaFact]
    public async Task AClickInTheMiddleOfTheFieldPutsTheCaretAtTheNearerEdge()
    {
        using var h = await OpenAsync();
        var view = h.View;
        var inside = new TextPosition(StoryRef.Body, P(h, 2), 3);
        var bounds = view.CaretBounds(inside);
        Assert.NotNull(bounds);
        var point = new Point(bounds!.Value.X + 1, bounds.Value.Y + (bounds.Value.Height / 2));

        h.Window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
        h.Window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
        ViewHarness.Pump();

        // The middle entry is as near to one edge as to the other: the end.
        var caret = view.Caret!.Value;
        Assert.Same(P(h, 3), caret.Paragraph);
        Assert.Equal(Text(P(h, 3)).Length, caret.Offset);
        Assert.True(view.Selection!.Value.IsEmpty);
    }

    [AvaloniaFact]
    public async Task TypingAtTheEdgeOfTheFieldIsRefusedWithTheReasonAndTypingBeforeItIsNot()
    {
        using var h = await OpenAsync();
        var notes = new List<string>();
        h.View.StructureNote += notes.Add;
        var session = Place(h, 3, Text(P(h, 3)).Length);
        var revision = h.View.Tab.Docx.Model.Edits.Revision;

        h.Window.KeyTextInput("x");
        ViewHarness.Pump();

        Assert.Equal([WriteStrings.NoteTocProtected], notes);
        Assert.Equal(revision, h.View.Tab.Docx.Model.Edits.Revision);
        Assert.Equal("Entry three", Text(P(h, 3)));
        Assert.Contains("table of contents", WriteStrings.NoteTocProtected, StringComparison.Ordinal);

        notes.Clear();
        Place(h, 4, 0);
        h.Window.KeyTextInput("x");
        ViewHarness.Pump();
        Assert.Empty(notes);
        Assert.Equal("xAfter", Text(P(h, 4)));
        Assert.True(session.CanEdit);
    }
}
