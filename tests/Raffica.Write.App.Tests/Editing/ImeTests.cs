using System.IO.Compression;
using System.Text;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.TextInput;
using Raffica.Write.Editing;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using Raffica.Write.Settings;
using Raffica.Write.Viewing;
using Raffica.WriteApp.Tests.Viewing;
using Xunit;

namespace Raffica.WriteApp.Tests.Editing;

/// <summary>The input method client and Write's own preedit (Task 18 of milestone 4.3a), driven the way a platform drives it: the request on focus, <c>SetPreeditText</c>, the commit as text input, the cursor rectangle read.</summary>
public sealed class ImeTests : IDisposable
{
    private const string FirstText = "Body text of the first paragraph.";
    private static readonly TimeSpan GateTimeout = TimeSpan.FromSeconds(60);
    private readonly string _folder = Directory.CreateTempSubdirectory("raffica-write-ime-").FullName;

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

    private static async Task<ViewHarness> OpenWholeAsync(string path, double width = 900, double height = 700)
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

    // What the platform does when the view takes the focus: it asks the control for its client.
    private static TextInputMethodClient Request(ViewHarness h)
    {
        var args = new TextInputMethodClientRequestedEventArgs { RoutedEvent = InputElement.TextInputMethodClientRequestedEvent };
        h.View.RaiseEvent(args);
        return args.Client ?? throw new InvalidOperationException("the view gave no client");
    }

    private static void Commit(ViewHarness h, string text)
    {
        h.Window.KeyTextInput(text);
        ViewHarness.Pump();
    }

    private static void Stroke(ViewHarness h, PhysicalKey key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        h.Window.KeyPressQwerty(key, modifiers);
        h.Window.KeyReleaseQwerty(key, modifiers);
        ViewHarness.Pump();
    }

    private static EditSession Place(ViewHarness h, Paragraph paragraph, int offset, int? to = null)
    {
        var session = h.View.Tab.EnsureEditor();
        _ = session.Selection;
        var start = new TextPosition(StoryRef.Body, paragraph, offset);
        var end = to.HasValue ? start with { Offset = to.Value } : start;
        session.SetSelection(new TextRange(start, end));
        h.View.Focus();
        return session;
    }

    private static Paragraph P(ViewHarness h, int index) => (Paragraph)h.View.Tab.Docx.Model.Body.Blocks[index];

    private static string TextOf(ViewHarness h, Paragraph paragraph) =>
        ParagraphProjection.Build(paragraph, h.View.Runner.PageLayout.ProjectionHost(StoryRef.Body, paragraph)).Text;

    private string WriteDocument(string[] paragraphs)
    {
        var path = Path.Combine(_folder, "ime.docx");
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
                </Types>
                """);
            Entry(zip, "_rels/.rels", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
                </Relationships>
                """);
            Entry(zip, "word/document.xml", $"""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:body>
                    {body}
                    <w:sectPr>
                      <w:pgSz w:w="12240" w:h="15840"/>
                      <w:pgMar w:top="1440" w:right="1440" w:bottom="1440" w:left="1440" w:header="720" w:footer="720" w:gutter="0"/>
                    </w:sectPr>
                  </w:body>
                </w:document>
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

    private string TwoParagraphs() => WriteDocument([$"<w:r><w:t>{FirstText}</w:t></w:r>", "<w:r><w:t>Second paragraph of the body.</w:t></w:r>"]);

    private string ManyParagraphs() => WriteDocument([$"<w:r><w:t>{FirstText}</w:t></w:r>", .. Enumerable.Range(1, 120).Select(i => $"<w:r><w:t>Paragraph {i} with some more words to take room</w:t></w:r>")]);

    [AvaloniaFact]
    public async Task NoImeClientBeforeTheFirstFocus()
    {
        using var h = await OpenWholeAsync(TwoParagraphs());
        Place(h, P(h, 0), 4);
        Commit(h, "x");
        Stroke(h, PhysicalKey.ArrowLeft);

        Assert.False(h.View.ImeClientCreated);
        var client = Request(h);
        Assert.True(h.View.ImeClientCreated);
        Assert.Same(client, Request(h));
        Assert.True(client.SupportsPreedit);
        Assert.True(client.SupportsSurroundingText);
        Assert.Same(h.View, client.TextViewVisual);
    }

    [AvaloniaFact]
    public async Task PreeditNeverTouchesTheModelTheHistoryOrTheRevision()
    {
        using var h = await OpenWholeAsync(TwoParagraphs());
        var session = Place(h, P(h, 0), 4);
        var client = Request(h);
        var model = h.View.Tab.Docx.Model;
        var revision = model.Edits.Revision;
        var dirty = model.Edits.IsDirty;
        var undo = session.Editor.History.UndoCount;
        var paragraph = P(h, 0);
        var edits = h.View.Runner.PageLayout.Stats.Edits;

        client.SetPreeditText("かんじ");
        client.SetPreeditText("かんじ", 2);
        client.SetPreeditText("漢字");

        Assert.True(h.View.IsComposing);
        Assert.Equal("漢字", h.View.PreeditText);
        Assert.Equal(revision, model.Edits.Revision);
        Assert.Equal(dirty, model.Edits.IsDirty);
        Assert.Equal(undo, session.Editor.History.UndoCount);
        Assert.Same(paragraph, P(h, 0));
        Assert.Equal(FirstText, TextOf(h, paragraph));
        Assert.Equal(edits, h.View.Runner.PageLayout.Stats.Edits);
        // The page shows the composition inside the paragraph.
        Assert.Contains("Body漢字 text", h.View.Runner.PageLayout.Page(0).Text, StringComparison.Ordinal);
        // The caret of the document did not move.
        Assert.Equal(4, h.View.Caret!.Value.Offset);
    }

    [AvaloniaFact]
    public async Task CommitInsertsOnceAndClearsThePreedit()
    {
        using var h = await OpenWholeAsync(TwoParagraphs());
        var session = Place(h, P(h, 0), 4);
        var client = Request(h);

        client.SetPreeditText("かん");
        client.SetPreeditText("かんじ");
        // The platform clears the preedit and then delivers the result as text.
        client.SetPreeditText(null);
        Commit(h, "漢字");

        Assert.False(h.View.IsComposing);
        Assert.Null(h.View.PreeditText);
        Assert.Null(h.View.Runner.PageLayout.PreeditText);
        Assert.Equal("Body漢字 text of the first paragraph.", TextOf(h, P(h, 0)));
        Assert.Equal(6, h.View.Caret!.Value.Offset);
        Assert.Equal(1, session.Editor.History.UndoCount);

        // Another platform delivers the result while the preedit is still shown; the text arrives once all the same.
        client.SetPreeditText("かな");
        Commit(h, "仮名");

        Assert.False(h.View.IsComposing);
        Assert.Equal("Body漢字仮名 text of the first paragraph.", TextOf(h, P(h, 0)));
        Assert.DoesNotContain("かな", h.View.Runner.PageLayout.Page(0).Text, StringComparison.Ordinal);
        Assert.Equal(8, h.View.Caret!.Value.Offset);
    }

    [AvaloniaFact]
    public async Task ASurrogatePairAndALongCommitAreInsertedWhole()
    {
        using var h = await OpenWholeAsync(TwoParagraphs());
        Place(h, P(h, 0), 4);
        var client = Request(h);

        client.SetPreeditText("𠮷");
        client.SetPreeditText(null);
        Commit(h, "𠮷野家");

        Assert.Equal("Body𠮷野家 text of the first paragraph.", TextOf(h, P(h, 0)));
        Assert.Equal(8, h.View.Caret!.Value.Offset);
    }

    [AvaloniaFact]
    public async Task CommitIsOneHistoryStepWithTypingCoalescing()
    {
        using var h = await OpenWholeAsync(TwoParagraphs());
        var session = Place(h, P(h, 0), 4);
        var client = Request(h);

        Commit(h, "a");
        Commit(h, "b");
        Assert.Equal(1, session.Editor.History.UndoCount);

        // A commit of letters directly after typing joins the typing step; the preedit before it added no step.
        client.SetPreeditText("cd");
        client.SetPreeditText(null);
        Commit(h, "cd");
        Assert.Equal(1, session.Editor.History.UndoCount);
        Assert.Equal("Bodyabcd text of the first paragraph.", TextOf(h, P(h, 0)));

        // A commit that is no letter starts a step of its own, and one undo takes back exactly that commit.
        client.SetPreeditText("、");
        client.SetPreeditText(null);
        Commit(h, "、");
        Assert.Equal(2, session.Editor.History.UndoCount);
        session.Apply(ed => ed.History.Undo(), 0);
        Assert.Equal("Bodyabcd text of the first paragraph.", TextOf(h, P(h, 0)));
    }

    [AvaloniaFact]
    public async Task ComposingOverASelectionReplacesItOnCommit()
    {
        using var h = await OpenWholeAsync(TwoParagraphs());
        var session = Place(h, P(h, 0), 5, 9);
        var client = Request(h);

        client.SetPreeditText("えん");

        // The selection stays until the commit (it is highlighted still) and the model is as it was.
        Assert.False(h.View.Selection!.Value.IsEmpty);
        Assert.Equal(FirstText, TextOf(h, P(h, 0)));

        client.SetPreeditText(null);
        Commit(h, "円");

        Assert.Equal("Body 円 of the first paragraph.", TextOf(h, P(h, 0)));
        Assert.True(h.View.Selection!.Value.IsEmpty);
        Assert.Equal(1, session.Editor.History.UndoCount);
    }

    [AvaloniaFact]
    public async Task EscCancelLeavesNoResidue()
    {
        using var h = await OpenWholeAsync(TwoParagraphs());
        var session = Place(h, P(h, 0), 4);
        var client = Request(h);
        var model = h.View.Tab.Docx.Model;
        var revision = model.Edits.Revision;
        var undo = session.Editor.History.UndoCount;
        var dirty = model.Edits.IsDirty;
        var before = h.View.Runner.PageLayout.Page(0).Text;

        client.SetPreeditText("かんじ", 1);
        Assert.Contains("かんじ", h.View.Runner.PageLayout.Page(0).Text, StringComparison.Ordinal);
        Stroke(h, PhysicalKey.Escape);

        Assert.False(h.View.IsComposing);
        Assert.Null(h.View.Runner.PageLayout.PreeditText);
        Assert.Equal(before, h.View.Runner.PageLayout.Page(0).Text);
        Assert.Equal(revision, model.Edits.Revision);
        Assert.Equal(undo, session.Editor.History.UndoCount);
        Assert.Equal(dirty, model.Edits.IsDirty);
        Assert.Equal(FirstText, TextOf(h, P(h, 0)));
        Assert.Equal(4, h.View.Caret!.Value.Offset);
        LayoutEquals(h);
    }

    // The page layout shows what a layout of the model shows, as after a composition that left nothing behind.
    private static void LayoutEquals(ViewHarness h)
    {
        var layout = h.View.Runner.PageLayout;
        var full = DocumentLayout.Create(h.View.Tab.Docx.Model, h.View.Tab.Fonts, new LayoutOptions());
        full.LayOutRestAsync(null, CancellationToken.None).GetAwaiter().GetResult();
        layout.LayOutRestAsync(null, CancellationToken.None).GetAwaiter().GetResult();
        Assert.Equal(full.LaidOutPages, layout.LaidOutPages);
        for (var i = 0; i < full.LaidOutPages; i++)
        {
            Assert.Equal(full.Page(i).Text, layout.Page(i).Text);
            Assert.Equal(full.Page(i).Items.Count, layout.Page(i).Items.Count);
        }
    }

    [AvaloniaFact]
    public async Task LostFocusClearsThePreeditWithoutWriting()
    {
        using var h = await OpenWholeAsync(TwoParagraphs());
        var session = Place(h, P(h, 0), 4);
        var client = Request(h);
        var model = h.View.Tab.Docx.Model;
        var revision = model.Edits.Revision;

        client.SetPreeditText("かんじ");
        Assert.True(h.View.IsComposing);
        h.Window.FocusManager!.Focus(null);
        ViewHarness.Pump();

        Assert.False(h.View.IsComposing);
        Assert.Null(h.View.Runner.PageLayout.PreeditText);
        Assert.Equal(revision, model.Edits.Revision);
        Assert.Equal(0, session.Editor.History.UndoCount);
        Assert.Equal(FirstText, TextOf(h, P(h, 0)));
        Assert.DoesNotContain("かんじ", h.View.Runner.PageLayout.Page(0).Text, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task KeysAreIgnoredWhileComposing()
    {
        using var h = await OpenWholeAsync(TwoParagraphs());
        var session = Place(h, P(h, 0), 4);
        var client = Request(h);
        var count = h.View.Tab.Docx.Model.Body.Blocks.Count;
        client.SetPreeditText("かんじ");

        Stroke(h, PhysicalKey.ArrowLeft);
        Stroke(h, PhysicalKey.ArrowRight, RawInputModifiers.Shift);
        Stroke(h, PhysicalKey.Backspace);
        Stroke(h, PhysicalKey.Delete);
        Stroke(h, PhysicalKey.Enter);
        Stroke(h, PhysicalKey.Tab);
        Stroke(h, PhysicalKey.Home);

        Assert.True(h.View.IsComposing);
        Assert.Equal("かんじ", h.View.PreeditText);
        Assert.Equal(4, h.View.Caret!.Value.Offset);
        Assert.True(h.View.Selection!.Value.IsEmpty);
        Assert.Equal(FirstText, TextOf(h, P(h, 0)));
        Assert.Equal(count, h.View.Tab.Docx.Model.Body.Blocks.Count);
        Assert.Equal(0, session.Editor.History.UndoCount);
    }

    [AvaloniaFact]
    public async Task ACommandKeyDuringACompositionDoesNotRunACommand()
    {
        using var h = await OpenWholeAsync(TwoParagraphs());
        Place(h, P(h, 0), 4);
        var client = Request(h);
        var ran = new List<string>();
        h.View.EditCommandHandler = id =>
        {
            ran.Add(id);
            return true;
        };
        client.SetPreeditText("かん");

        Stroke(h, PhysicalKey.A, RawInputModifiers.Control);
        Stroke(h, PhysicalKey.Z, RawInputModifiers.Control);

        Assert.Empty(ran);
        Assert.True(h.View.IsComposing);
    }

    [AvaloniaFact]
    public async Task CursorRectangleFollowsTheCaretAfterScrollAndZoom()
    {
        using var h = await OpenWholeAsync(ManyParagraphs(), 900, 500);
        Place(h, P(h, 3), 8);
        var client = Request(h);
        var raised = 0;
        client.CursorRectangleChanged += (_, _) => raised++;
        var first = client.CursorRectangle;
        Assert.True(first.Height > 0);
        Assert.Equal(h.View.CaretBarBounds!.Value, first);

        // The composition moves the cursor into the preedit.
        client.SetPreeditText("かんじ", 3);
        var composing = client.CursorRectangle;
        Assert.True(composing.X > first.X);
        Assert.Equal(h.View.PreeditCaretBounds!.Value, composing);
        Assert.True(raised > 0);

        // Scrolling moves the rectangle with the text, and says so.
        raised = 0;
        h.View.ScrollByWheel(-1);
        ViewHarness.Pump();
        var scrolled = client.CursorRectangle;
        Assert.True(scrolled.Y < composing.Y);
        Assert.Equal(h.View.PreeditCaretBounds!.Value, scrolled);
        Assert.True(raised > 0);

        // Zooming rescales it, and says so.
        raised = 0;
        h.View.SetZoom(h.View.Zoom * 2);
        ViewHarness.Pump();
        var zoomed = client.CursorRectangle;
        Assert.True(zoomed.Height > scrolled.Height * 1.5);
        Assert.Equal(h.View.PreeditCaretBounds!.Value, zoomed);
        Assert.True(raised > 0);

        // A caret move without a composition: the rectangle is the caret bar again.
        client.SetPreeditText(null);
        Assert.Equal(h.View.CaretBarBounds!.Value, client.CursorRectangle);
    }

    [AvaloniaFact]
    public async Task CursorRectangleChangesWhenTheLayoutChangesAroundIt()
    {
        using var h = await OpenWholeAsync(TwoParagraphs());
        Place(h, P(h, 0), 4);
        var client = Request(h);
        var raised = 0;
        client.CursorRectangleChanged += (_, _) => raised++;

        // A long composition reflows the paragraph; the rectangle is read from the layout as it is now.
        client.SetPreeditText(string.Concat(Enumerable.Repeat("かんじ", 60)), 180);

        Assert.True(raised > 0);
        Assert.Equal(h.View.PreeditCaretBounds!.Value, client.CursorRectangle);
        Assert.True(client.CursorRectangle.Y > h.View.CaretBarBounds!.Value.Y - 1);
    }

    [AvaloniaFact]
    public async Task PreeditCaretIsDrawnAtTheReportedCursor()
    {
        using var h = await OpenWholeAsync(TwoParagraphs());
        Place(h, P(h, 0), 4);
        var client = Request(h);
        var layout = h.View.Runner.PageLayout;

        client.SetPreeditText("かんじ", 1);
        var atOne = h.View.PreeditCaretBounds!.Value;
        client.SetPreeditText("かんじ", 3);
        var atThree = h.View.PreeditCaretBounds!.Value;
        client.SetPreeditText("かんじ");
        var atEnd = h.View.PreeditCaretBounds!.Value;

        Assert.True(atOne.X < atThree.X || atOne.X < atEnd.X);
        Assert.Equal(atThree.X, atEnd.X);
        // The bar is the caret of the layout inside the preedit, in view coordinates.
        var box = layout.PreeditCaret(3, 0)!.Value;
        var page = h.View.PageBounds(box.Page)!.Value;
        Assert.Equal(page.X + (box.X * h.View.Scroller!.Scale), atEnd.X, 1);
        // The overlay draws it (and the caret of the document is not drawn next to it).
        var drawn = h.View.OverlayRenderCount;
        var shown = h.View.CaretShown;
        h.View.TickBlink();
        Assert.NotEqual(shown, h.View.CaretShown);
        h.View.TickBlink();
        h.Capture();
        Assert.True(h.View.OverlayRenderCount > drawn);
    }

    [AvaloniaFact]
    public async Task SurroundingTextIsTheParagraph()
    {
        using var h = await OpenWholeAsync(TwoParagraphs());
        Place(h, P(h, 1), 6);
        var client = Request(h);

        Assert.Equal("Second paragraph of the body.", client.SurroundingText);
        Assert.Equal(new TextSelection(6, 6), client.Selection);

        Place(h, P(h, 1), 2, 9);
        Assert.Equal(new TextSelection(2, 9), client.Selection);

        Place(h, P(h, 0), 0);
        Assert.Equal(FirstText, client.SurroundingText);
        Assert.Equal(new TextSelection(0, 0), client.Selection);
    }

    [AvaloniaFact]
    public async Task SurroundingTextOfALongParagraphIsAWindowAroundTheCaret()
    {
        var word = "wörd ";
        var long1 = string.Concat(Enumerable.Repeat(word, 3000));
        var path = WriteDocument([$"<w:r><w:t xml:space=\"preserve\">{long1}</w:t></w:r>", "<w:r><w:t>tail</w:t></w:r>"]);
        using var h = await OpenWholeAsync(path);
        Place(h, P(h, 0), 7000);
        var client = Request(h);

        var text = client.SurroundingText;
        var selection = client.Selection;

        Assert.True(text.Length is > 0 and <= 4096 + 2);
        Assert.True(text.Length < long1.Length);
        Assert.Equal(selection.Start, selection.End);
        Assert.InRange(selection.End, 0, text.Length);
        // The window is the paragraph's own text around the caret.
        Assert.Equal(long1.Substring(7000 - selection.End, text.Length), text);
    }

    [AvaloniaFact]
    public async Task AWindowNeverSplitsASurrogatePair()
    {
        var long1 = string.Concat(Enumerable.Repeat("😀", 5000));
        var path = WriteDocument([$"<w:r><w:t>{long1}</w:t></w:r>"]);
        using var h = await OpenWholeAsync(path);
        Place(h, P(h, 0), 5000);
        var client = Request(h);

        var text = client.SurroundingText;

        Assert.False(char.IsLowSurrogate(text[0]));
        Assert.False(char.IsHighSurrogate(text[^1]));
        Assert.Equal(0, text.Length % 2);
    }

    [AvaloniaFact]
    public async Task ImeLogHasLengthsOnly()
    {
        var log = Path.Combine(_folder, "ime.log");
        using var h = await OpenWholeAsync(TwoParagraphs());
        h.View.ImeLogPath = () => log;
        var session = Place(h, P(h, 0), 4);
        var client = Request(h);
        Assert.False(File.Exists(log));

        client.SetPreeditText("秘密のかんじ");
        client.SetPreeditText("秘密の漢字");
        client.SetPreeditText(null);
        Commit(h, "秘密の漢字");
        client.SetPreeditText("ひみつ");
        Stroke(h, PhysicalKey.Escape);
        client.SetPreeditText("ほか");
        client.SetPreeditText(null);
        ViewHarness.Pump();
        await Task.Delay(PageView.CommitWindow + TimeSpan.FromMilliseconds(250));
        ViewHarness.Pump();

        var lines = File.ReadAllLines(log, Encoding.UTF8);
        Assert.Equal(["preedit len=6", "preedit len=5", "commit len=5", "preedit len=3", "cancel", "preedit len=2", "cancel"], lines);
        var all = File.ReadAllText(log, Encoding.UTF8);
        foreach (var secret in new[] { "秘", "密", "漢", "字", "ひ", "か", "ほ" })
        {
            Assert.DoesNotContain(secret, all, StringComparison.Ordinal);
        }

        Assert.Equal(1, session.Editor.History.UndoCount);
    }

    [AvaloniaFact]
    public async Task AnUnwritableLogNeverCrashes()
    {
        using var h = await OpenWholeAsync(TwoParagraphs());
        h.View.ImeLogPath = () => Path.Combine(_folder, "no-such-folder", "deeper", "x\0y.log");
        Place(h, P(h, 0), 4);
        var client = Request(h);

        client.SetPreeditText("かん");
        client.SetPreeditText(null);
        Commit(h, "感");

        Assert.Equal("Body感 text of the first paragraph.", TextOf(h, P(h, 0)));
    }

    [AvaloniaFact]
    public async Task NoLogFileWithoutTheVariable()
    {
        using var h = await OpenWholeAsync(TwoParagraphs());
        h.View.ImeLogPath = () => null;
        Place(h, P(h, 0), 4);
        var client = Request(h);

        client.SetPreeditText("かん");
        client.SetPreeditText(null);
        Commit(h, "感");

        Assert.Empty(Directory.GetFiles(_folder, "*.log"));
    }

    [AvaloniaFact]
    public async Task APreeditIsRefusedWhileOnlyAPrefixIsOpen()
    {
        var path = ManyParagraphs();
        using var h = new ViewHarness(path, 900, 700);
        Assert.False(h.View.Runner.FullModelReady);
        var refused = new List<EditBlockedReason>();
        h.View.InputRefused += refused.Add;
        var client = Request(h);

        client.SetPreeditText("かん");

        Assert.False(h.View.IsComposing);
        Assert.Null(h.View.PreeditText);
        Assert.Contains(EditBlockedReason.StillOpening, refused);
        await Task.CompletedTask;
    }

    [AvaloniaFact]
    public async Task ACommitDuringASaveIsQueuedLikeTyping()
    {
        using var h = await OpenWholeAsync(TwoParagraphs());
        var session = Place(h, P(h, 0), 4);
        var client = Request(h);

        var save = session.BeginSave();
        client.SetPreeditText("かん");
        // The preedit is not in the model, so the save does not wait for it.
        Assert.True(h.View.IsComposing);
        client.SetPreeditText(null);
        Commit(h, "感");
        Assert.Equal(FirstText, TextOf(h, P(h, 0)));
        Assert.Equal(1, session.PendingCount);

        save.Dispose();
        ViewHarness.Pump();

        Assert.Equal("Body感 text of the first paragraph.", TextOf(h, P(h, 0)));
        Assert.Equal(0, session.PendingCount);
    }

    [AvaloniaFact]
    public async Task SwitchingTheViewEndsTheComposition()
    {
        using var h = await OpenWholeAsync(TwoParagraphs());
        Place(h, P(h, 0), 4);
        var client = Request(h);
        client.SetPreeditText("かん");

        h.View.Mode = WriteView.Writing;
        ViewHarness.Pump();

        Assert.False(h.View.IsComposing);
        Assert.Null(h.View.Runner.PageLayout.PreeditText);
        Assert.Equal(FirstText, TextOf(h, P(h, 0)));
    }

    [AvaloniaFact]
    public async Task AClickDuringACompositionEndsItWithoutWritingAndPlacesTheCaret()
    {
        using var h = await OpenWholeAsync(TwoParagraphs());
        var session = Place(h, P(h, 0), 4);
        var client = Request(h);
        var model = h.View.Tab.Docx.Model;
        var revision = model.Edits.Revision;
        client.SetPreeditText("かんじ");
        var second = h.View.CaretBounds(new TextPosition(StoryRef.Body, P(h, 1), 3))!.Value;

        h.View.HandlePress(new Point(second.X, second.Y + (second.Height / 2)), 1, KeyModifiers.None, false);
        ViewHarness.Pump();

        Assert.False(h.View.IsComposing);
        Assert.Null(h.View.Runner.PageLayout.PreeditText);
        Assert.Equal(revision, model.Edits.Revision);
        Assert.Equal(0, session.Editor.History.UndoCount);
        Assert.Same(P(h, 1), h.View.Caret!.Value.Paragraph);
        Assert.Equal(FirstText, TextOf(h, P(h, 0)));
    }

    [AvaloniaFact]
    public async Task ThePlatformSettingTheSelectionMovesTheCaretOfTheParagraph()
    {
        using var h = await OpenWholeAsync(TwoParagraphs());
        Place(h, P(h, 0), 4);
        var client = Request(h);

        client.Selection = new TextSelection(2, 6);

        Assert.Equal(2, h.View.Selection!.Value.Start.Offset);
        Assert.Equal(6, h.View.Selection!.Value.End.Offset);
        Assert.Same(P(h, 0), h.View.Selection!.Value.End.Paragraph);
    }
}
