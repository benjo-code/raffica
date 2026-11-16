using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Raffica.Write;
using Raffica.Write.Documents;
using Raffica.Write.Editing;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using Raffica.Write.Viewing;
using Raffica.WriteApp.Tests.Viewing;
using Xunit;
using DocumentLayout = Raffica.Write.Layout.DocumentLayout;

namespace Raffica.WriteApp.Tests.Editing;

/// <summary>Typing, the caret keys and the selection keys of the page view (Task 16 of milestone 4.3a).</summary>
public sealed class TypingTests : IDisposable
{
    private const string FirstText = "Body text of the first paragraph.";
    private static readonly TimeSpan GateTimeout = TimeSpan.FromSeconds(60);
    private readonly string _folder = Directory.CreateTempSubdirectory("raffica-write-typing-").FullName;

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

    private static async Task<ViewHarness> OpenWholeAsync(string path, double width = 900, double height = 1400)
    {
        var harness = new ViewHarness(path, width, height);
        harness.View.Runner.LeaseWaitTimeout = GateTimeout;
        harness.View.Runner.Start();
        await ViewHarness.WaitAsync(() => harness.View.Runner.FullModelReady || harness.View.Runner.IsFaulted, 30000);
        Assert.True(harness.View.Runner.FullModelReady);
        var layout = harness.View.Runner.PageLayout;
        await ViewHarness.WaitAsync(() => layout.IsComplete || layout.LaidOutPages >= 2, 30000);
        return harness;
    }

    private static void Type(ViewHarness h, string text)
    {
        h.Window.KeyTextInput(text);
        ViewHarness.Pump();
    }

    // The command key and the word key of the system: Ctrl and Ctrl on Windows and Linux, ⌘ and Option on macOS.
    private static readonly RawInputModifiers Command = OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;
    private static readonly RawInputModifiers WordKey = OperatingSystem.IsMacOS() ? RawInputModifiers.Alt : RawInputModifiers.Control;

    // The line ends are Home and End, ⌘← and ⌘→ on macOS (where the plain keys go to the document ends); the document ends are Ctrl+Home and Ctrl+End, the plain keys on macOS.
    private static void LineStart(ViewHarness h) => Stroke(h, OperatingSystem.IsMacOS() ? PhysicalKey.ArrowLeft : PhysicalKey.Home, OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.None);

    private static void LineEnd(ViewHarness h) => Stroke(h, OperatingSystem.IsMacOS() ? PhysicalKey.ArrowRight : PhysicalKey.End, OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.None);

    private static void DocumentStart(ViewHarness h) => Stroke(h, PhysicalKey.Home, OperatingSystem.IsMacOS() ? RawInputModifiers.None : RawInputModifiers.Control);

    private static void DocumentEnd(ViewHarness h) => Stroke(h, PhysicalKey.End, OperatingSystem.IsMacOS() ? RawInputModifiers.None : RawInputModifiers.Control);

    // A key press and its release (a single stroke).
    private static void Stroke(ViewHarness h, PhysicalKey key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        h.Window.KeyPressQwerty(key, modifiers);
        h.Window.KeyReleaseQwerty(key, modifiers);
        ViewHarness.Pump();
    }

    private static EditSession Place(ViewHarness h, Paragraph paragraph, int offset, int? to = null)
    {
        var session = h.View.Tab.EnsureEditor();
        _ = session.Selection; // binds the editor (a selection set before the first use is replaced by the start of the document)
        var start = new TextPosition(StoryRef.Body, paragraph, offset);
        var end = to.HasValue ? start with { Offset = to.Value } : start;
        session.SetSelection(new TextRange(start, end));
        h.View.Focus();
        return session;
    }

    private static string TextOf(PageView view, Paragraph paragraph, StoryRef? story = null)
    {
        var layout = view.Tab.Editor is { } ? view.Runner.PageLayout : view.Runner.PageLayout;
        return ParagraphProjection.Build(paragraph, layout.ProjectionHost(story ?? StoryRef.Body, paragraph)).Text;
    }

    private static Paragraph P(ViewHarness h, int index) => (Paragraph)h.View.Tab.Docx.Model.Body.Blocks[index];

    // A document of body paragraphs given as the XML of their runs, with a header and a footer.
    private string WriteDocument(string[] paragraphs, string header = "Header text", string footer = "Footer text")
    {
        var path = Path.Combine(_folder, "typing.docx");
        var body = string.Concat(paragraphs.Select(p => $"<w:p>{p}</w:p>"));
        using (var stream = File.Create(path))
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            Entry(zip, "[Content_Types].xml", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
                  <Override PartName="/word/header1.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml"/>
                  <Override PartName="/word/footer1.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.footer+xml"/>
                </Types>
                """);
            Entry(zip, "_rels/.rels", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
                </Relationships>
                """);
            Entry(zip, "word/_rels/document.xml.rels", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/header" Target="header1.xml"/>
                  <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/footer" Target="footer1.xml"/>
                </Relationships>
                """);
            Entry(zip, "word/document.xml", $"""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <w:body>
                    {body}
                    <w:sectPr>
                      <w:headerReference w:type="default" r:id="rId1"/>
                      <w:footerReference w:type="default" r:id="rId2"/>
                      <w:pgSz w:w="12240" w:h="15840"/>
                      <w:pgMar w:top="1440" w:right="1440" w:bottom="1440" w:left="1440" w:header="720" w:footer="720" w:gutter="0"/>
                    </w:sectPr>
                  </w:body>
                </w:document>
                """);
            Entry(zip, "word/header1.xml", $"""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:hdr xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:p><w:r><w:t>{header}</w:t></w:r></w:p></w:hdr>
                """);
            Entry(zip, "word/footer1.xml", $"""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:ftr xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:p><w:r><w:t>{footer}</w:t></w:r></w:p></w:ftr>
                """);
        }

        return path;
    }

    private static void Entry(ZipArchive zip, string name, string text)
    {
        var entry = zip.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(text.TrimStart());
    }

    // More than 64 blocks: the open reads a prefix first (a smaller document is whole at once).
    private string ManyParagraphs() => WriteDocument([$"<w:r><w:t>{FirstText}</w:t></w:r>", .. Enumerable.Range(1, 90).Select(i => $"<w:r><w:t>Paragraph {i}</w:t></w:r>")]);

    private string TwoParagraphs() => WriteDocument([$"<w:r><w:t>{FirstText}</w:t></w:r>", "<w:r><w:t>Second paragraph of the body.</w:t></w:r>"]);

    // The entries are the inner XML of a paragraph each; this one closes its paragraph, writes a block content control with two paragraphs and opens the next one.
    private string WithContentControl() => WriteDocument(
    [
        "<w:r><w:t>Before the control.</w:t></w:r></w:p><w:sdt><w:sdtPr><w:alias w:val=\"Box\"/></w:sdtPr><w:sdtContent><w:p><w:r><w:t>Inside one.</w:t></w:r></w:p><w:p><w:r><w:t>Inside two.</w:t></w:r></w:p></w:sdtContent></w:sdt><w:p><w:r><w:t>After the control.</w:t></w:r>",
    ]);

    [AvaloniaFact]
    public async Task TypingEnterAndBackspaceInAParagraphOfAContentControlWorkThroughTheKeyPath()
    {
        using var h = await OpenWholeAsync(WithContentControl());
        var control = Assert.IsType<ContentControlBlock>(h.View.Tab.Docx.Model.Body.Blocks[1]);
        var session = Place(h, (Paragraph)control.Content[0], 6);

        Type(h, "X");
        Assert.True(session.CanEdit);
        Assert.Equal("InsideX one.", TextOf(h.View, (Paragraph)control.Content[0]));
        Assert.Contains("InsideX one.", h.View.Runner.PageLayout.Page(0).Text, StringComparison.Ordinal);

        Stroke(h, PhysicalKey.Enter);
        Assert.True(session.CanEdit);
        Assert.Equal(3, control.Content.Count);
        Assert.Same(control, h.View.Tab.Docx.Model.Body.Blocks[1]);

        // The caret is at the start of the new paragraph: Backspace merges it back inside the control.
        Stroke(h, PhysicalKey.Backspace);
        Assert.True(session.CanEdit);
        Assert.Equal(2, control.Content.Count);
        Assert.Equal("InsideX one.", TextOf(h.View, (Paragraph)control.Content[0]));

        // Undo walks back through the clones of the paragraphs inside the control.
        Assert.True(session.CanUndo);
        while (session.CanUndo)
        {
            h.View.RunEditCommand(Raffica.Write.Commands.WriteCommandIds.Undo);
            ViewHarness.Pump();
        }

        Assert.Equal(["Inside one.", "Inside two."], control.Content.OfType<Paragraph>().Select(p => TextOf(h.View, p)).ToArray());
    }

    [AvaloniaFact]
    public async Task TypingInsertsAtTheCaretAndAdvancesIt()
    {
        using var h = await OpenWholeAsync(TwoParagraphs());
        var first = P(h, 0);
        var session = Place(h, first, 4);

        Type(h, "X");
        Type(h, "Y");

        var now = P(h, 0);
        Assert.Equal("BodyXY text of the first paragraph.", TextOf(h.View, now));
        Assert.Equal(6, h.View.Caret!.Value.Offset);
        Assert.Same(now, h.View.Caret.Value.Paragraph);
        // The caret is drawn where it is, and the page shows the typed text.
        Assert.NotNull(h.View.CaretBarBounds);
        Assert.Contains("BodyXY text of the first paragraph.", h.View.Runner.PageLayout.Page(0).Text, StringComparison.Ordinal);
        Assert.True(session.CanUndo);
    }

    [AvaloniaFact]
    public async Task TypingWithNoClickStartsAtTheStartOfTheDocument()
    {
        using var h = await OpenWholeAsync(TwoParagraphs());
        h.View.Focus();
        Assert.Null(h.View.Caret);

        Type(h, "A");

        Assert.Equal("A" + FirstText, TextOf(h.View, P(h, 0)));
        Assert.Equal(1, h.View.Caret!.Value.Offset);
    }

    [AvaloniaFact]
    public async Task EnterBackspaceDelete()
    {
        using var h = await OpenWholeAsync(TwoParagraphs());
        var blocks = h.View.Tab.Docx.Model.Body.Blocks;
        var count = blocks.Count;
        Place(h, P(h, 0), 4);

        Stroke(h, PhysicalKey.Enter);
        Assert.Equal(count + 1, blocks.Count);
        Assert.Equal("Body", TextOf(h.View, P(h, 0)));
        Assert.Equal(" text of the first paragraph.", TextOf(h.View, P(h, 1)));
        Assert.Same(P(h, 1), h.View.Caret!.Value.Paragraph);
        Assert.Equal(0, h.View.Caret.Value.Offset);

        Stroke(h, PhysicalKey.Backspace);
        Assert.Equal(count, blocks.Count);
        Assert.Equal(FirstText, TextOf(h.View, P(h, 0)));
        Assert.Equal(4, h.View.Caret!.Value.Offset);

        Stroke(h, PhysicalKey.Delete);
        Assert.Equal("Bodytext of the first paragraph.", TextOf(h.View, P(h, 0)));
        Assert.Equal(4, h.View.Caret!.Value.Offset);

        // Ctrl+Backspace takes a word.
        Stroke(h, PhysicalKey.Backspace, WordKey);
        Assert.Equal("text of the first paragraph.", TextOf(h.View, P(h, 0)));
        Assert.Equal(0, h.View.Caret!.Value.Offset);
    }

    [AvaloniaFact]
    public async Task ArrowKeysMoveAndShiftSelects()
    {
        using var h = await OpenWholeAsync(TwoParagraphs());
        var first = P(h, 0);
        Place(h, first, 5);

        Stroke(h, PhysicalKey.ArrowRight);
        Assert.Equal(6, h.View.Caret!.Value.Offset);
        Stroke(h, PhysicalKey.ArrowLeft);
        Stroke(h, PhysicalKey.ArrowLeft);
        Assert.Equal(4, h.View.Caret!.Value.Offset);

        // Shift extends from the anchor; the moving end is the caret.
        Stroke(h, PhysicalKey.ArrowRight, RawInputModifiers.Shift);
        Stroke(h, PhysicalKey.ArrowRight, RawInputModifiers.Shift);
        var selection = h.View.Selection!.Value;
        Assert.Equal(4, selection.Start.Offset);
        Assert.Equal(6, selection.End.Offset);
        Stroke(h, PhysicalKey.ArrowLeft, RawInputModifiers.Shift);
        Assert.Equal(4, h.View.Selection!.Value.Start.Offset);
        Assert.Equal(5, h.View.Selection!.Value.End.Offset);

        // A plain Left over a selection collapses it to its start, a plain Right to its end.
        Stroke(h, PhysicalKey.ArrowRight, RawInputModifiers.Shift);
        Stroke(h, PhysicalKey.ArrowLeft);
        Assert.True(h.View.Selection!.Value.IsEmpty);
        Assert.Equal(4, h.View.Caret!.Value.Offset);
        Stroke(h, PhysicalKey.ArrowRight, RawInputModifiers.Shift);
        Stroke(h, PhysicalKey.ArrowRight, RawInputModifiers.Shift);
        Stroke(h, PhysicalKey.ArrowRight);
        Assert.Equal(6, h.View.Caret!.Value.Offset);

        // A backward selection: the anchor stays, the caret goes left of it.
        Stroke(h, PhysicalKey.ArrowLeft, RawInputModifiers.Shift);
        Stroke(h, PhysicalKey.ArrowLeft, RawInputModifiers.Shift);
        Assert.Equal(6, h.View.Selection!.Value.Start.Offset);
        Assert.Equal(4, h.View.Selection!.Value.End.Offset);

        // Words, line ends and the document ends.
        Stroke(h, PhysicalKey.Home);
        Assert.Equal(0, h.View.Caret!.Value.Offset);
        Stroke(h, PhysicalKey.ArrowRight, WordKey);
        Assert.Equal(5, h.View.Caret!.Value.Offset);
        DocumentEnd(h);
        Assert.Same(P(h, 1), h.View.Caret!.Value.Paragraph);
        Assert.Equal(TextOf(h.View, P(h, 1)).Length, h.View.Caret!.Value.Offset);
        DocumentStart(h);
        Assert.Same(first, h.View.Caret!.Value.Paragraph);
        Assert.Equal(0, h.View.Caret!.Value.Offset);

        // Down goes to the next paragraph's line, Up back.
        Stroke(h, PhysicalKey.ArrowDown);
        Assert.Same(P(h, 1), h.View.Caret!.Value.Paragraph);
        Stroke(h, PhysicalKey.ArrowUp);
        Assert.Same(first, h.View.Caret!.Value.Paragraph);
    }

    [AvaloniaFact]
    public async Task TypingReplacesTheSelection()
    {
        using var h = await OpenWholeAsync(TwoParagraphs());
        var session = Place(h, P(h, 0), 5, 9);
        Assert.Equal("text", TextOf(h.View, P(h, 0))[5..9]);

        Type(h, "Z");

        Assert.Equal("Body Z of the first paragraph.", TextOf(h.View, P(h, 0)));
        Assert.True(h.View.Selection!.Value.IsEmpty);
        Assert.Equal(6, h.View.Caret!.Value.Offset);
        Assert.Equal(1, session.Editor.History.UndoCount);

        // Enter over a selection replaces it with a paragraph break; Backspace over one deletes it, one step each.
        Place(h, P(h, 0), 0, 4);
        Stroke(h, PhysicalKey.Enter);
        Assert.Equal("", TextOf(h.View, P(h, 0)));
        Assert.Equal(" Z of the first paragraph.", TextOf(h.View, P(h, 1)));
        Assert.Equal(2, session.Editor.History.UndoCount);

        Place(h, P(h, 1), 1, 3);
        Stroke(h, PhysicalKey.Backspace);
        Assert.Equal(" of the first paragraph.", TextOf(h.View, P(h, 1)));
        Assert.Equal(3, session.Editor.History.UndoCount);

        // A backward selection is replaced as well.
        var q = P(h, 1);
        var anchor = new TextPosition(StoryRef.Body, q, 7);
        session.SetSelection(new TextRange(anchor, anchor with { Offset = 3 }));
        Type(h, "-");
        Assert.Equal(" of- first paragraph.", TextOf(h.View, P(h, 1)));
    }

    [AvaloniaFact]
    public async Task ASurrogatePairIsInsertedAsIsAndDeletedAsOne()
    {
        using var h = await OpenWholeAsync(TwoParagraphs());
        Place(h, P(h, 0), 4);

        Type(h, "😀");
        Assert.Equal(6, h.View.Caret!.Value.Offset);
        Assert.Equal("Body😀 text of the first paragraph.", TextOf(h.View, P(h, 0)));

        Stroke(h, PhysicalKey.Backspace);
        Assert.Equal(FirstText, TextOf(h.View, P(h, 0)));
        Assert.Equal(4, h.View.Caret!.Value.Offset);

        // Control characters never become text.
        Type(h, "\r");
        Type(h, "\u0001");
        Assert.Equal(FirstText, TextOf(h.View, P(h, 0)));
    }

    [AvaloniaFact]
    public async Task TabInsertsATabAndShiftTabDoesNothing()
    {
        using var h = await OpenWholeAsync(TwoParagraphs());
        var session = Place(h, P(h, 0), 4);

        Stroke(h, PhysicalKey.Tab);
        Assert.Equal("Body\t text of the first paragraph.", TextOf(h.View, P(h, 0)));
        Assert.Equal(5, h.View.Caret!.Value.Offset);
        var steps = session.Editor.History.UndoCount;

        Stroke(h, PhysicalKey.Tab, RawInputModifiers.Shift);
        Assert.Equal("Body\t text of the first paragraph.", TextOf(h.View, P(h, 0)));
        Assert.Equal(5, h.View.Caret!.Value.Offset);
        Assert.Equal(steps, session.Editor.History.UndoCount);
    }

    [AvaloniaFact]
    public async Task KeysAreRepeatSafe()
    {
        using var h = await OpenWholeAsync(WriteDocument(["<w:r><w:t>abcdefgh</w:t></w:r>"]));
        Place(h, P(h, 0), 8);

        // Held Backspace: key down events without a release in between; each one deletes one cluster, none is dropped or doubled.
        h.Window.KeyPressQwerty(PhysicalKey.Backspace, RawInputModifiers.None);
        h.Window.KeyPressQwerty(PhysicalKey.Backspace, RawInputModifiers.None);
        h.Window.KeyPressQwerty(PhysicalKey.Backspace, RawInputModifiers.None);
        ViewHarness.Pump();
        Assert.Equal("abcde", TextOf(h.View, P(h, 0)));
        h.Window.KeyReleaseQwerty(PhysicalKey.Backspace, RawInputModifiers.None);
        Stroke(h, PhysicalKey.Backspace);
        Assert.Equal("abcd", TextOf(h.View, P(h, 0)));

        // A held arrow repeats.
        Place(h, P(h, 0), 4);
        h.Window.KeyPressQwerty(PhysicalKey.ArrowLeft, RawInputModifiers.None);
        h.Window.KeyPressQwerty(PhysicalKey.ArrowLeft, RawInputModifiers.None);
        h.Window.KeyReleaseQwerty(PhysicalKey.ArrowLeft, RawInputModifiers.None);
        Assert.Equal(2, h.View.Caret!.Value.Offset);

        // A one-shot command key acts once per press, however long it is held.
        var ran = new List<string>();
        h.View.EditCommandHandler = id =>
        {
            ran.Add(id);
            return true;
        };
        for (var i = 0; i < 3; i++)
        {
            h.Window.KeyPressQwerty(PhysicalKey.Z, Command);
        }

        Assert.Equal([EditingKeys.UndoCommand], ran);
        h.Window.KeyReleaseQwerty(PhysicalKey.Z, Command);
        Stroke(h, PhysicalKey.Z, Command | RawInputModifiers.Shift);
        Assert.Equal([EditingKeys.UndoCommand, EditingKeys.RedoCommand], ran);
        Stroke(h, PhysicalKey.V, Command);
        Stroke(h, PhysicalKey.C, Command);
        Stroke(h, PhysicalKey.X, Command);
        Assert.Equal(
            [EditingKeys.UndoCommand, EditingKeys.RedoCommand, EditingKeys.PasteCommand, EditingKeys.CopyCommand, EditingKeys.CutCommand],
            ran);
    }

    [AvaloniaFact]
    public async Task AFieldResultIsSelectedFirstAndDeletedByTheNextBackspace()
    {
        const string Field = "<w:r><w:t>ab</w:t></w:r><w:fldSimple w:instr=\" PAGE \"><w:r><w:t>7</w:t></w:r></w:fldSimple><w:r><w:t>cd</w:t></w:r>";
        using var h = await OpenWholeAsync(WriteDocument([Field]));
        var paragraph = P(h, 0);
        var before = TextOf(h.View, paragraph);
        var at = before.IndexOf('c');
        Assert.True(at > 2, before);
        Place(h, paragraph, at);

        // The first Backspace selects the field result and changes nothing.
        Stroke(h, PhysicalKey.Backspace);
        Assert.Equal(before, TextOf(h.View, P(h, 0)));
        var selection = h.View.Selection!.Value;
        Assert.False(selection.IsEmpty);
        Assert.Equal(at, selection.End.Offset);
        Assert.Equal(0, h.View.Tab.Editor!.Editor.History.UndoCount);

        // The second deletes the selection (one step).
        Stroke(h, PhysicalKey.Backspace);
        Assert.Equal("abcd", TextOf(h.View, P(h, 0)));
        Assert.True(h.View.Selection!.Value.IsEmpty);
        Assert.Equal(1, h.View.Tab.Editor.Editor.History.UndoCount);

        // Held down over a field: it selects once, then deletes, then goes on with the text; the held key never reselects in a loop.
        const string Again = "<w:r><w:t>abc</w:t></w:r><w:fldSimple w:instr=\" PAGE \"><w:r><w:t>7</w:t></w:r></w:fldSimple><w:r><w:t>d</w:t></w:r>";
        using var g = await OpenWholeAsync(WriteDocument([Again]));
        var start = TextOf(g.View, P(g, 0));
        Place(g, P(g, 0), start.IndexOf('d'));
        for (var i = 0; i < 3; i++)
        {
            g.Window.KeyPressQwerty(PhysicalKey.Backspace, RawInputModifiers.None);
        }

        g.Window.KeyReleaseQwerty(PhysicalKey.Backspace, RawInputModifiers.None);
        ViewHarness.Pump();
        Assert.Equal("abd", TextOf(g.View, P(g, 0)));
    }

    [AvaloniaFact]
    public async Task SelectAllSelectsTheStoryOfTheCaret()
    {
        using var h = await OpenWholeAsync(TwoParagraphs());
        var view = h.View;
        Place(h, P(h, 0), 3);

        Stroke(h, PhysicalKey.A, Command);
        var selection = view.Selection!.Value;
        Assert.Same(P(h, 0), selection.Start.Paragraph);
        Assert.Equal(0, selection.Start.Offset);
        Assert.Same(P(h, 1), selection.End.Paragraph);
        Assert.Equal(TextOf(view, P(h, 1)).Length, selection.End.Offset);

        // Typing replaces everything; the document is one paragraph with the new text (one step).
        Type(h, "Q");
        Assert.Equal("Q", TextOf(view, P(h, 0)));
        Assert.Equal(1, view.Tab.Editor!.Editor.History.UndoCount);
    }

    [AvaloniaFact]
    public async Task SelectAllInAHeaderSelectsOnlyTheHeader()
    {
        using var h = await OpenWholeAsync(TwoParagraphs());
        var view = h.View;
        Place(h, P(h, 0), 3);
        var rect = view.PageBounds(0)!.Value;
        var scale = view.Zoom * 96 / 72;
        var inHeader = new Point(rect.X + (500 * scale), rect.Y + (41 * scale));
        for (var i = 0; i < 2; i++)
        {
            h.Window.MouseDown(inHeader, MouseButton.Left);
            h.Window.MouseUp(inHeader, MouseButton.Left);
        }

        ViewHarness.Pump();
        Assert.Equal(StoryKind.HeaderFooter, view.Caret!.Value.Story.Kind);

        Stroke(h, PhysicalKey.A, Command);
        var selection = view.Selection!.Value;
        Assert.Equal(StoryKind.HeaderFooter, selection.Start.Story.Kind);
        Assert.Equal(selection.Start.Story, selection.End.Story);
        Assert.Equal(0, selection.Start.Offset);
        Assert.Equal("Header text".Length, TextOf(view, selection.End.Paragraph, selection.End.Story).Length >= 11 ? selection.End.Offset : -1);
        Assert.Equal(FirstText, TextOf(view, P(h, 0)));
    }

    [AvaloniaFact]
    public async Task HomeAndEndAndTheVerticalKeysReadTheAffinity()
    {
        var word = string.Concat(Enumerable.Repeat("abcdefgh-", 60));
        using var h = await OpenWholeAsync(WriteDocument([$"<w:r><w:t>{word}</w:t></w:r>", "<w:r><w:t>next</w:t></w:r>"]));
        var view = h.View;
        var paragraph = P(h, 0);
        Place(h, paragraph, 5);

        // End goes to the end of the first line of the wrapped word; the offset is also the start of the second line, so the caret stays upstream.
        LineEnd(h);
        var lineEnd = view.Caret!.Value.Offset;
        Assert.InRange(lineEnd, 10, 539);
        Assert.Equal(CaretAffinity.Upstream, view.Affinity);
        var bar = view.CaretBarBounds!.Value;
        var firstLineBar = view.CaretBounds(new TextPosition(StoryRef.Body, paragraph, 5))!.Value;
        Assert.Equal(firstLineBar.Y, bar.Y, 1);

        // Home from there goes to the start of the first line (not to the offset itself, which is the start of the second line).
        LineStart(h);
        Assert.Equal(0, view.Caret!.Value.Offset);
        Assert.Equal(CaretAffinity.Downstream, view.Affinity);

        // Up and Down from an upstream caret start at its (first) line: Down lands on the second line.
        LineEnd(h);
        Assert.Equal(CaretAffinity.Upstream, view.Affinity);
        Stroke(h, PhysicalKey.ArrowDown);
        var down = view.Caret!.Value.Offset;
        Assert.True(down > lineEnd, $"{down} {lineEnd}");
        Assert.Equal(CaretAffinity.Downstream, view.Affinity);

        // Right resets the affinity, as typing does; an explicit reset never lets it revive for the same position.
        LineEnd(h);
        Assert.Equal(CaretAffinity.Upstream, view.Affinity);
        Stroke(h, PhysicalKey.ArrowRight);
        Assert.Equal(CaretAffinity.Downstream, view.Affinity);
        Place(h, paragraph, 5);
        LineEnd(h);
        Assert.Equal(CaretAffinity.Upstream, view.Affinity);
        view.ResetAffinity();
        Assert.Equal(CaretAffinity.Downstream, view.Affinity);
        Type(h, "x");
        Assert.Equal(CaretAffinity.Downstream, view.Affinity);
    }

    [AvaloniaFact]
    public async Task VerticalMovesKeepTheirColumn()
    {
        var long1 = "<w:r><w:t>aaaaaaaaaaaaaaaaaaaaaaaa</w:t></w:r>";
        var short2 = "<w:r><w:t>bb</w:t></w:r>";
        var long3 = "<w:r><w:t>cccccccccccccccccccccccc</w:t></w:r>";
        using var h = await OpenWholeAsync(WriteDocument([long1, short2, long3]));
        var view = h.View;
        Place(h, P(h, 0), 20);

        Stroke(h, PhysicalKey.ArrowDown);
        Assert.Same(P(h, 1), view.Caret!.Value.Paragraph);
        Assert.Equal(2, view.Caret.Value.Offset);
        Stroke(h, PhysicalKey.ArrowDown);
        Assert.Same(P(h, 2), view.Caret!.Value.Paragraph);
        // The column of the first move is kept over the short line.
        Assert.InRange(view.Caret.Value.Offset, 18, 22);
    }

    [AvaloniaFact]
    public async Task WithoutACaretTheCaretKeysStillScroll()
    {
        using var h = await OpenWholeAsync(WindowFixture.Thesis, 900, 600);
        var view = h.View;
        h.View.Focus();
        Assert.Null(view.Tab.Editor);
        Assert.Equal(0, view.OffsetY);

        Stroke(h, PhysicalKey.PageDown);

        Assert.True(view.OffsetY > 0);
        Assert.Null(view.Tab.Editor);
        Assert.Null(view.Caret);
    }

    [AvaloniaFact]
    public async Task ThePageKeysMoveTheCaretAndKeepItInView()
    {
        using var h = await OpenWholeAsync(WindowFixture.Thesis, 900, 600);
        var view = h.View;
        var layout = view.Runner.PageLayout;

        // Two laid-out pages can hold no long paragraph on a slow machine: wait for the whole layout so the search below is deterministic.
        await ViewHarness.WaitAsync(() => layout.IsComplete, 60000);
        var paragraph = (Paragraph)view.Tab.Docx.Model.Body.Blocks.First(b => b is Paragraph p && TextOf(view, p).Length > 80
            && view.CaretBounds(new TextPosition(StoryRef.Body, p, 3)) is not null);
        Place(h, paragraph, 3);
        view.EnsureCaretVisible();
        ViewHarness.Pump();
        var start = view.CaretBarBounds!.Value;

        Stroke(h, PhysicalKey.PageDown);

        var after = view.CaretBarBounds!.Value;
        Assert.NotEqual(paragraph, view.Caret!.Value.Paragraph);
        Assert.True(after.Top >= 0 && after.Bottom <= view.Viewport.Height, $"{after} in {view.Viewport}");
        Assert.NotEqual(start, after);
        Assert.NotNull(layout);
    }

    [AvaloniaFact]
    public async Task KeysWhileTheModelIsFaultedConsumeNothingAndSayWhy()
    {
        var path = ManyParagraphs();
        var harness = new ViewHarness(path, 900, 1000);
        using var h = harness;
        h.View.Runner.BeforeFullOpen = () => throw new InvalidOperationException("broken");
        h.View.Runner.Start();
        await ViewHarness.WaitAsync(() => h.View.Runner.IsFaulted, 30000);
        var refused = new List<EditBlockedReason>();
        h.View.InputRefused += refused.Add;
        h.View.Focus();
        var prefix = TextOf(h.View, P(h, 0));

        Type(h, "a");
        Stroke(h, PhysicalKey.Backspace);
        Stroke(h, PhysicalKey.Enter);

        Assert.Equal([EditBlockedReason.Faulted, EditBlockedReason.Faulted, EditBlockedReason.Faulted], refused);
        Assert.Equal(prefix, TextOf(h.View, P(h, 0)));
        Assert.Null(h.View.Caret);
        Assert.Null(h.View.Tab.Editor);
    }

    [AvaloniaFact]
    public async Task KeysWhileOnlyAPrefixIsOpenChangeNothing()
    {
        var path = ManyParagraphs();
        using var h = new ViewHarness(path, 900, 1000);
        var gate = new ManualResetEventSlim(false);
        h.View.Runner.BeforeFullOpen = () => gate.Wait(TimeSpan.FromSeconds(30));
        h.View.Runner.Start();
        var refused = new List<EditBlockedReason>();
        h.View.InputRefused += refused.Add;
        h.View.Focus();
        Assert.False(h.View.Runner.FullModelReady);

        Type(h, "a");
        Stroke(h, PhysicalKey.Delete);
        Stroke(h, PhysicalKey.Enter);

        Assert.Equal(3, refused.Count);
        Assert.All(refused, r => Assert.Equal(EditBlockedReason.StillOpening, r));
        Assert.Null(h.View.Tab.Editor);
        Assert.Equal(FirstText, TextOf(h.View, P(h, 0)));

        // A caret key is not refused: it scrolls as before.
        Stroke(h, PhysicalKey.ArrowDown);
        Assert.Equal(3, refused.Count);

        gate.Set();
        await ViewHarness.WaitAsync(() => h.View.Runner.FullModelReady, 30000);
        Type(h, "a");
        Assert.Equal("a" + FirstText, TextOf(h.View, P(h, 0)));
        Assert.Equal(3, refused.Count);
    }

    [AvaloniaFact]
    public async Task TypingDuringASaveIsQueuedSilentlyAndReplayedInOrder()
    {
        using var h = await OpenWholeAsync(TwoParagraphs());
        var session = Place(h, P(h, 0), 4);
        var refused = 0;
        h.View.InputRefused += _ => refused++;

        var save = session.BeginSave();
        Type(h, "a");
        Type(h, "b");
        Stroke(h, PhysicalKey.Backspace);
        Type(h, "c");
        // A caret key during a save is claimed and does nothing.
        Stroke(h, PhysicalKey.ArrowLeft);
        Assert.Equal(FirstText, TextOf(h.View, P(h, 0)));
        Assert.Equal(0, refused);

        save.Dispose();
        ViewHarness.Pump();

        Assert.Equal("Bodyac text of the first paragraph.", TextOf(h.View, P(h, 0)));
        Assert.Equal(6, h.View.Caret!.Value.Offset);
        Assert.Equal(0, refused);
    }

    [AvaloniaFact]
    public async Task AFailedLayoutUpdateDuringAKeyFaultsTheSessionAndNoExceptionEscapes()
    {
        using var h = await OpenWholeAsync(TwoParagraphs());
        var session = Place(h, P(h, 0), 4);
        var refused = new List<EditBlockedReason>();
        h.View.InputRefused += refused.Add;
        var revision = h.View.Tab.Docx.Model.Edits.Revision;

        // The layout throws while the first key brings it up to date: the catch of the key handler runs (the session was editable before the key).
        Assert.True(session.CanEdit);
        h.View.Runner.PageLayout.PageFinished = _ => throw new InvalidOperationException("seam");
        Type(h, "x");

        Assert.Equal(EditBlockedReason.Faulted, session.BlockedReason);
        Assert.Equal([EditBlockedReason.Faulted], refused);
        Assert.Null(h.View.Caret);
        Assert.True(h.View.Runner.IsFaulted);

        // The next keys are consumed without effect and say the same.
        var after = h.View.Tab.Docx.Model.Edits.Revision;
        Type(h, "y");
        Stroke(h, PhysicalKey.Backspace);
        Assert.Equal(after, h.View.Tab.Docx.Model.Edits.Revision);
        Assert.Equal([EditBlockedReason.Faulted, EditBlockedReason.Faulted, EditBlockedReason.Faulted], refused);
        Assert.True(after > revision);
    }

    [AvaloniaFact]
    public async Task TheFaultedHintShowsOncePerBurstInTheWindow()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        Assert.IsType<Raffica.Write.Documents.Opened>(await window.Flow.OpenAsync(TwoParagraphs()));
        var view = window.ActiveView!;
        await WindowFixture.WaitAsync(() => view.Runner.FullModelReady && view.IsEffectivelyVisible);
        view.Focus();
        var first = (Paragraph)view.Tab.Docx.Model.Body.Blocks[0];
        var session = view.Tab.EnsureEditor();
        _ = session.Selection;
        session.SetSelection(new TextRange(new TextPosition(StoryRef.Body, first, 4), new TextPosition(StoryRef.Body, first, 4)));
        var hints = new List<string?>();
        window.Status.PropertyChanged += (_, e) =>
        {
            if (e.Property == Raffica.Shell.Frame.StatusBar.HintProperty)
            {
                hints.Add(window.Status.Hint);
            }
        };

        view.Runner.PageLayout.PageFinished = _ => throw new InvalidOperationException("seam");
        window.KeyTextInput("x");
        window.KeyTextInput("y");
        WindowFixture.Pump();

        Assert.Equal(EditBlockedReason.Faulted, session.BlockedReason);
        Assert.Equal([WriteStrings.EditFailed], hints);
    }

    private sealed class TraceCapture : TraceListener
    {
        public List<string> Lines { get; } = [];

        public override void Write(string? message) => Lines.Add(message ?? string.Empty);

        public override void WriteLine(string? message) => Lines.Add(message ?? string.Empty);
    }

    [AvaloniaFact]
    public async Task AFailureThatIsNotTheLayoutsIsRefusedWithoutAHintAndTheSessionStaysUsable()
    {
        using var h = await OpenWholeAsync(TwoParagraphs());
        var session = Place(h, P(h, 0), 4);
        var refused = new List<EditBlockedReason>();
        h.View.InputRefused += refused.Add;
        var capture = new TraceCapture();
        Trace.Listeners.Add(capture);
        try
        {
            // A stale selection (a paragraph that is no longer in the document): the text editor throws StalePositionException before it changes anything.
            var stale = new TextPosition(StoryRef.Body, new Paragraph(), 0);
            session.SetSelection(new TextRange(stale, stale));
            var revision = h.View.Tab.Docx.Model.Edits.Revision;
#if DEBUG
            Assert.Throws<StalePositionException>(() => Type(h, "x"));
#else
            Type(h, "x");
            Stroke(h, PhysicalKey.Backspace);

            Assert.Empty(refused);
            Assert.Equal(revision, h.View.Tab.Docx.Model.Edits.Revision);
            Assert.Equal(FirstText, TextOf(h.View, P(h, 0)));
            Assert.Null(session.BlockedReason);
            // Only the type of the exception is traced, never its message.
            Assert.Contains(capture.Lines, l => l.Contains(nameof(StalePositionException), StringComparison.Ordinal));
            Assert.DoesNotContain(capture.Lines, l => l.Contains("paragraph", StringComparison.OrdinalIgnoreCase));
#endif
            // The session is usable: a real selection types.
            var ok = new TextPosition(StoryRef.Body, P(h, 0), 4);
            session.SetSelection(new TextRange(ok, ok));
            Type(h, "Z");
            Assert.Equal("BodyZ text of the first paragraph.", TextOf(h.View, P(h, 0)));
        }
        finally
        {
            Trace.Listeners.Remove(capture);
        }
    }

    [AvaloniaFact]
    public async Task TabAndShiftTabKeepMovingTheFocusWhileOnlyAPrefixIsOpenOrAfterAFault()
    {
        using var h = new ViewHarness(ManyParagraphs(), 900, 1000);
        var gate = new ManualResetEventSlim(false);
        h.View.Runner.BeforeFullOpen = () => gate.Wait(TimeSpan.FromSeconds(30));
        h.View.Runner.Start();
        var refused = new List<EditBlockedReason>();
        h.View.InputRefused += refused.Add;
        var handled = new List<bool>();
        h.View.AddHandler(InputElement.KeyDownEvent, (_, e) => handled.Add(e.Handled), Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
        h.View.Focus();
        Assert.False(h.View.Runner.FullModelReady);

        Stroke(h, PhysicalKey.Tab);
        Stroke(h, PhysicalKey.Tab, RawInputModifiers.Shift);

        // The page view did not take them (and said nothing: nothing was typed).
        Assert.Equal([false, false], handled);
        Assert.Empty(refused);

        // Enter still says why it does nothing.
        Stroke(h, PhysicalKey.Enter);
        Assert.Equal([EditBlockedReason.StillOpening], refused);

        gate.Set();
        await ViewHarness.WaitAsync(() => h.View.Runner.FullModelReady, 30000);
        handled.Clear();
        Place(h, P(h, 0), 4);
        Stroke(h, PhysicalKey.Tab);
        Stroke(h, PhysicalKey.Tab, RawInputModifiers.Shift);
        Assert.Equal([true, true], handled);

        // After a fault they move the focus again.
        h.View.Runner.FailEdit(new InvalidOperationException("seam"));
        handled.Clear();
        Stroke(h, PhysicalKey.Tab);
        Stroke(h, PhysicalKey.Tab, RawInputModifiers.Shift);
        Assert.Equal([false, false], handled);
    }

    [AvaloniaFact]
    public async Task F6LeavesThePageViewForTheOutlinePaneAndComesBack()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        Assert.IsType<Raffica.Write.Documents.Opened>(await window.Flow.OpenAsync(WindowFixture.Thesis));
        var view = window.ActiveView!;
        await WindowFixture.WaitAsync(() => view.IsEffectivelyVisible && view.PageCount > 0);
        Assert.True(window.RunCommand(Raffica.Write.Commands.WriteCommandIds.Outline));
        WindowFixture.Pump();
        var host = window.OutlineHost!;
        // The list of headings is filled in the background: F6 can focus the pane once it has something to focus.
        var outline = window.OutlineOf(view.Tab)!;
        await WindowFixture.WaitAsync(() => outline.LoadTask.IsCompleted);
        WindowFixture.Pump();
        view.Focus();
        WindowFixture.Pump();
        Assert.True(view.IsFocused);

        window.KeyPressQwerty(PhysicalKey.F6, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.F6, RawInputModifiers.None);
        WindowFixture.Pump();
        Assert.False(view.IsFocused);
        Assert.True(host.IsKeyboardFocusWithin);
        Assert.True(host.IsPaneFocused);

        window.KeyPressQwerty(PhysicalKey.F6, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.F6, RawInputModifiers.None);
        WindowFixture.Pump();
        Assert.True(view.IsFocused);
        Assert.False(host.IsKeyboardFocusWithin);

        // Shift+F6 goes the other way round.
        window.KeyPressQwerty(PhysicalKey.F6, RawInputModifiers.Shift);
        window.KeyReleaseQwerty(PhysicalKey.F6, RawInputModifiers.Shift);
        WindowFixture.Pump();
        Assert.True(host.IsKeyboardFocusWithin);
    }

    [AvaloniaFact]
    public async Task AltGrIsTextNotACommand()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        var path = TwoParagraphs();
        var opened = Assert.IsType<Raffica.Write.Documents.Opened>(await window.Flow.OpenAsync(path));
        var view = window.ActiveView!;
        await WindowFixture.WaitAsync(() => view.Runner.FullModelReady && view.IsEffectivelyVisible);
        view.Focus();
        WindowFixture.Pump();
        Assert.Same(opened.Tab, view.Tab);
        var input = window.TitleSearch.GetVisualDescendants().OfType<Avalonia.Controls.TextBox>().Single();
        var first = (Paragraph)view.Tab.Docx.Model.Body.Blocks[0];
        var session = view.Tab.EnsureEditor();
        _ = session.Selection; // binds the editor
        session.SetSelection(new TextRange(new TextPosition(StoryRef.Body, first, 4), new TextPosition(StoryRef.Body, first, 4)));

        // AltGr+Q on a German layout types "@": Windows reports Ctrl+Alt and the text "@".
        var modifiers = RawInputModifiers.Control | RawInputModifiers.Alt;
        window.KeyPress(Key.Q, modifiers, PhysicalKey.Q, "@");
        window.KeyTextInput("@");
        window.KeyRelease(Key.Q, modifiers, PhysicalKey.Q, "@");
        WindowFixture.Pump();

        Assert.False(input.IsFocused);
        Assert.Equal("Body@ text of the first paragraph.", ParagraphProjection.Build((Paragraph)view.Tab.Docx.Model.Body.Blocks[0], view.Runner.PageLayout.ProjectionHost(StoryRef.Body, (Paragraph)view.Tab.Docx.Model.Body.Blocks[0])).Text);

        // The command search opens by its own key: Alt+Q alone, ⌘K on macOS.
        var searchKey = OperatingSystem.IsMacOS() ? PhysicalKey.K : PhysicalKey.Q;
        // The shell takes the command key from the platform settings, which a headless run on macOS reports as Ctrl, so ask the same source.
        var command = Avalonia.Application.Current!.PlatformSettings!.HotkeyConfiguration.CommandModifiers;
        var searchModifiers = !OperatingSystem.IsMacOS() ? RawInputModifiers.Alt : command.HasFlag(KeyModifiers.Meta) ? RawInputModifiers.Meta : RawInputModifiers.Control;
        window.KeyPressQwerty(searchKey, searchModifiers);
        window.KeyReleaseQwerty(searchKey, searchModifiers);
        Assert.True(input.IsFocused);
    }

    [AvaloniaFact]
    public async Task TypingBeforeTheFullModelShowsTheStillOpeningHint()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        var gate = new ManualResetEventSlim(false);
        window.FirstSharpPageDrawn += tab => tab.Runner.BeforeFullOpen = () => gate.Wait(TimeSpan.FromSeconds(30));
        var path = ManyParagraphs();
        Assert.IsType<Raffica.Write.Documents.Opened>(await window.Flow.OpenAsync(path));
        var view = window.ActiveView!;
        await WindowFixture.WaitAsync(() => view.Tab.Runner.BeforeFullOpen is not null);
        Assert.False(view.Tab.Runner.FullModelReady);
        view.Focus();
        WindowFixture.Pump();
        var hints = new List<string?>();
        window.Status.PropertyChanged += (_, e) =>
        {
            if (e.Property == Raffica.Shell.Frame.StatusBar.HintProperty)
            {
                hints.Add(window.Status.Hint);
            }
        };

        // A burst of keys: the hint shows once, the document does not change, nothing is queued.
        window.KeyTextInput("a");
        window.KeyTextInput("b");
        window.KeyPressQwerty(PhysicalKey.Backspace, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Backspace, RawInputModifiers.None);
        WindowFixture.Pump();
        Assert.Equal([WriteStrings.EditStillOpening], hints);
        Assert.Equal(WriteStrings.EditStillOpening, window.Status.Hint);
        Assert.Null(view.Tab.Editor);

        // The hint goes when the open completes, and the next key types.
        gate.Set();
        await WindowFixture.WaitAsync(() => view.Tab.Runner.FullModelReady && window.Status.Hint is null);
        Assert.Equal([WriteStrings.EditStillOpening, null], hints);
        window.KeyTextInput("a");
        WindowFixture.Pump();
        var first = (Paragraph)view.Tab.Docx.Model.Body.Blocks[0];
        Assert.Equal("a" + FirstText, ParagraphProjection.Build(first, view.Runner.PageLayout.ProjectionHost(StoryRef.Body, first)).Text);
        Assert.Null(window.Status.Hint);
    }

    [Theory]
    [InlineData("EditStillOpening")]
    [InlineData("EditUnavailable")]
    public void TheHintsAreWordedInBothLanguages(string key)
    {
        var english = WriteStrings.GetIn(key, new System.Globalization.CultureInfo("en"));
        var german = WriteStrings.GetIn(key, new System.Globalization.CultureInfo("de"));
        Assert.False(string.IsNullOrWhiteSpace(english));
        Assert.False(string.IsNullOrWhiteSpace(german));
        Assert.NotEqual(english, german);
    }
}
