using System.IO.Compression;
using System.Text;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Raffica.Write.Commands;
using Raffica.Write.Documents;
using Raffica.Write.Editing;
using Raffica.Write.Model;
using Raffica.Write.Viewing;
using Raffica.WriteApp.Tests.Viewing;
using Xunit;

namespace Raffica.WriteApp.Tests.Editing;

/// <summary>The formatting keys of the page view (Task 9 of milestone 4.3b): Ctrl (Cmd on macOS) with B, I, U, E, L, R and J.</summary>
public sealed class FormattingKeysTests : IDisposable
{
    private const string FirstText = "Body text of the first paragraph.";
    private static readonly TimeSpan GateTimeout = TimeSpan.FromSeconds(60);
    private static readonly RawInputModifiers Command = OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;
    private static readonly System.Xml.Linq.XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private readonly string _folder = Directory.CreateTempSubdirectory("raffica-write-formatting-keys-").FullName;

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

    // The shortcuts macOS reserves, copied by value from the PDF app's ReservedShortcuts (Write never references the PDF app).
    private static readonly (Key Key, KeyModifiers Modifiers)[] s_reservedOnMac =
    [
        (Key.H, KeyModifiers.Meta), (Key.H, KeyModifiers.Alt | KeyModifiers.Meta), (Key.M, KeyModifiers.Meta), (Key.Q, KeyModifiers.Meta), (Key.Q, KeyModifiers.Shift | KeyModifiers.Meta),
        (Key.Tab, KeyModifiers.Meta), (Key.Space, KeyModifiers.Meta), (Key.OemTilde, KeyModifiers.Meta), (Key.F, KeyModifiers.Control | KeyModifiers.Meta),
        (Key.D3, KeyModifiers.Shift | KeyModifiers.Meta), (Key.D4, KeyModifiers.Shift | KeyModifiers.Meta), (Key.D5, KeyModifiers.Shift | KeyModifiers.Meta),
    ];

    private static readonly (Key Key, string Id)[] s_keys =
    [
        (Key.B, WriteCommandIds.Bold), (Key.I, WriteCommandIds.Italic), (Key.U, WriteCommandIds.Underline), (Key.E, WriteCommandIds.AlignCenter),
        (Key.L, WriteCommandIds.AlignLeft), (Key.R, WriteCommandIds.AlignRight), (Key.J, WriteCommandIds.Justify),
    ];

    private static async Task<ViewHarness> OpenWholeAsync(string path)
    {
        var harness = new ViewHarness(path, 900, 1400);
        harness.View.Runner.LeaseWaitTimeout = GateTimeout;
        harness.View.Runner.Start();
        await ViewHarness.WaitAsync(() => harness.View.Runner.FullModelReady || harness.View.Runner.IsFaulted, 30000);
        Assert.True(harness.View.Runner.FullModelReady);
        var layout = harness.View.Runner.PageLayout;
        await ViewHarness.WaitAsync(() => layout.IsComplete || layout.LaidOutPages >= 2, 30000);
        return harness;
    }

    private static void Stroke(ViewHarness h, PhysicalKey key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        h.Window.KeyPressQwerty(key, modifiers);
        h.Window.KeyReleaseQwerty(key, modifiers);
        ViewHarness.Pump();
    }

    private static EditSession Place(ViewHarness h, Paragraph paragraph, int offset, int? to = null, StoryRef? story = null)
    {
        var session = h.View.Tab.EnsureEditor();
        _ = session.Selection; // binds the editor (a selection set before the first use is replaced by the start of the document)
        var start = new TextPosition(story ?? StoryRef.Body, paragraph, offset);
        var end = to.HasValue ? start with { Offset = to.Value } : start;
        session.SetSelection(new TextRange(start, end));
        h.View.Focus();
        return session;
    }

    private static Paragraph P(ViewHarness h, int index) => (Paragraph)h.View.Tab.Docx.Model.Body.Blocks[index];

    private static IEnumerable<Run> RunsOf(Paragraph paragraph) => paragraph.Inlines.OfType<Run>();

    private static string TextOf(Run run) => string.Concat(run.Content.OfType<TextContent>().Select(t => t.Text));

    // The text of the runs that are bold, joined.
    private static string BoldText(Paragraph paragraph) => string.Concat(RunsOf(paragraph).Where(r => r.Properties.Get(W + "b") is not null).Select(TextOf));

    private string Document(string[] paragraphs, string header = "Header text")
    {
        var path = Path.Combine(_folder, "keys.docx");
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
                </Relationships>
                """);
            Entry(zip, "word/document.xml", $"""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <w:body>
                    {body}
                    <w:sectPr>
                      <w:headerReference w:type="default" r:id="rId1"/>
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
        }

        return path;
    }

    private static void Entry(ZipArchive zip, string name, string text)
    {
        var entry = zip.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(text.TrimStart());
    }

    private string TwoParagraphs() => Document([$"<w:r><w:t>{FirstText}</w:t></w:r>", "<w:r><w:t>Second paragraph of the body.</w:t></w:r>"]);

    [AvaloniaFact]
    public async Task CtrlBoldTogglesTheSelection()
    {
        using var h = await OpenWholeAsync(TwoParagraphs());
        var session = Place(h, P(h, 0), 0, 4);

        Stroke(h, PhysicalKey.B, Command);
        Assert.Equal("Body", BoldText(P(h, 0)));
        Assert.Equal(1, session.Editor.History.UndoCount);

        // All of it is on now: the same key turns it off, and no element stays behind.
        Stroke(h, PhysicalKey.B, Command);
        Assert.Equal(string.Empty, BoldText(P(h, 0)));
        Assert.Equal(2, session.Editor.History.UndoCount);

        // A key of the paragraph group is a paragraph change on the paragraph of the selection.
        Stroke(h, PhysicalKey.E, Command);
        Assert.Equal("center", P(h, 0).Properties.Get(W + "jc")?.Attribute(W + "val")?.Value);
        Assert.Null(P(h, 1).Properties.Get(W + "jc"));
    }

    [AvaloniaFact]
    public async Task CtrlBoldAtACaretSetsThePendingFormat()
    {
        using var h = await OpenWholeAsync(TwoParagraphs());
        var session = Place(h, P(h, 0), 4);

        Stroke(h, PhysicalKey.B, Command);

        // The document is as it was: no step, no revision; the next typed text takes the format.
        Assert.Equal(0, session.Editor.History.UndoCount);
        Assert.Equal(FirstText, string.Concat(RunsOf(P(h, 0)).Select(TextOf)));
        Assert.Equal(string.Empty, BoldText(P(h, 0)));
        Assert.True(FormatChanges.IsOn(session.Pending.Changes, CharacterToggle.Bold));

        h.Window.KeyTextInput("X");
        ViewHarness.Pump();
        Assert.Equal("X", BoldText(P(h, 0)));

        // A second press at the caret turns the pending bold off again; a caret move forgets what is pending.
        Place(h, P(h, 0), 2);
        Stroke(h, PhysicalKey.B, Command);
        Stroke(h, PhysicalKey.B, Command);
        Assert.False(FormatChanges.IsOn(session.Pending.Changes, CharacterToggle.Bold));
        Stroke(h, PhysicalKey.ArrowRight);
        Assert.Empty(session.Pending.Changes);
    }

    [AvaloniaFact]
    public async Task AltGrAndCtrlAltAreTextNotShortcuts()
    {
        foreach (var (key, _) in s_keys)
        {
            // AltGr arrives as Ctrl and Alt together on Windows; Alt alone, Ctrl with Shift and the wrong command key are no shortcut either.
            Assert.Null(EditingKeys.FormatCommandFor(key, KeyModifiers.Control | KeyModifiers.Alt, macOs: false));
            Assert.Null(EditingKeys.FormatCommandFor(key, KeyModifiers.Alt, macOs: false));
            Assert.Null(EditingKeys.FormatCommandFor(key, KeyModifiers.Control | KeyModifiers.Shift, macOs: false));
            Assert.Null(EditingKeys.FormatCommandFor(key, KeyModifiers.Meta, macOs: false));
            Assert.Null(EditingKeys.FormatCommandFor(key, KeyModifiers.Meta | KeyModifiers.Alt, macOs: true));
            Assert.Null(EditingKeys.FormatCommandFor(key, KeyModifiers.Alt, macOs: true));
        }

        Assert.True(EditingKeys.IsTextInput(KeyModifiers.Control | KeyModifiers.Alt, "b", macOs: false));
        Assert.True(EditingKeys.IsTextInput(Key.B, KeyModifiers.Control | KeyModifiers.Alt, "b", macOs: false));

        // In the view: AltGr+B changes nothing and the key is not claimed (the text input follows).
        using var h = await OpenWholeAsync(TwoParagraphs());
        var session = Place(h, P(h, 0), 0, 4);
        var handled = new List<bool>();
        h.View.AddHandler(InputElement.KeyDownEvent, (_, e) => handled.Add(e.Handled), Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
        Stroke(h, PhysicalKey.B, RawInputModifiers.Control | RawInputModifiers.Alt);
        Assert.Equal([false], handled);
        Assert.Equal(0, session.Editor.History.UndoCount);
        Assert.Empty(session.Pending.Changes);
    }

    [Fact]
    public void NoFormattingKeyIsInTheReservedOrExistingKeys()
    {
        var formatting = WriteCommandCatalog.Commands.Where(c => WriteCommandIds.IsFormatting(c.Id) && c.ShortcutFor(false) is not null).ToList();
        Assert.Equal(s_keys.Select(k => k.Id).Order(StringComparer.Ordinal), formatting.Select(c => c.Id).Order(StringComparer.Ordinal));
        Assert.Equal(s_keys.Select(k => k.Key).Order(), formatting.Select(c => c.ShortcutFor(false)!.Key).Order());

        foreach (var macOs in new[] { false, true })
        {
            var command = macOs ? KeyModifiers.Meta : KeyModifiers.Control;
            foreach (var candidate in formatting)
            {
                var shortcut = candidate.ShortcutFor(macOs)!;

                // Not one of the shortcuts macOS reserves (the list copied above), tested with the shortcut's own matcher on every chord of the reserved list.
                Assert.DoesNotContain(s_reservedOnMac, r => shortcut.Matches(r.Key, r.Modifiers, command));

                // Not the key of any other command of the catalog (the same key, the same base and Shift).
                foreach (var other in WriteCommandCatalog.Commands.Where(o => o.Id != candidate.Id && o.ShortcutFor(macOs) is not null))
                {
                    var otherShortcut = other.ShortcutFor(macOs)!;
                    Assert.False(otherShortcut.Keys.Contains(shortcut.Key) && otherShortcut.Base == shortcut.Base && otherShortcut.Shift == shortcut.Shift, $"{candidate.Id} clashes with {other.Id} (macOS {macOs})");
                }

                // Not a key the editor itself owns (caret, delete, clipboard, undo ...), with any set of modifiers.
                for (var bits = 0; bits < 16; bits++)
                {
                    var modifiers = (KeyModifiers)bits;
                    if (EditingKeys.FormatCommandFor(shortcut.Key, modifiers, macOs) is not null)
                    {
                        Assert.Null(EditingKeys.For(shortcut.Key, modifiers, macOs));
                    }
                }

                // The window never matches it itself (it is the view's key), and the command search stays Alt+Q (Cmd+K on macOS).
                Assert.Null(WriteCommandCatalog.Find(shortcut.Key, command, command, macOs));
            }
        }
    }

    [Fact]
    public void MacUsesTheCommandModifier()
    {
        foreach (var (key, id) in s_keys)
        {
            Assert.Equal(id, EditingKeys.FormatCommandFor(key, KeyModifiers.Control, macOs: false));
            Assert.Equal(id, EditingKeys.FormatCommandFor(key, KeyModifiers.Meta, macOs: true));
            Assert.Null(EditingKeys.FormatCommandFor(key, KeyModifiers.Control, macOs: true));
            Assert.Null(EditingKeys.FormatCommandFor(key, KeyModifiers.Meta, macOs: false));
            Assert.Null(EditingKeys.FormatCommandFor(key, KeyModifiers.None, macOs: false));
        }

        // The shortcut text the ribbon and the menu show.
        Assert.Equal("Ctrl+B", WriteCommandCatalog.Commands.Single(c => c.Id == WriteCommandIds.Bold).ShortcutFor(false)!.Text(false));
        Assert.Equal("Meta+B", WriteCommandCatalog.Commands.Single(c => c.Id == WriteCommandIds.Bold).ShortcutFor(true)!.Text(true));
    }

    [AvaloniaFact]
    public async Task AFormattingKeyInTheOutlineListBelongsToTheList()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        Assert.IsType<Opened>(await window.Flow.OpenAsync(WindowFixture.Thesis));
        var view = window.ActiveView!;
        await WindowFixture.WaitAsync(() => view.IsEffectivelyVisible && view.PageCount > 0);
        Assert.True(window.RunCommand(WriteCommandIds.Outline));
        WindowFixture.Pump();
        var outline = window.OutlineOf(view.Tab)!;
        await WindowFixture.WaitAsync(() => outline.LoadTask.IsCompleted);
        WindowFixture.Pump();
        await WindowFixture.WaitAsync(() => view.Runner.FullModelReady);
        view.Focus();
        WindowFixture.Pump();

        // F6 puts the focus in the pane; Ctrl+B there is the list's key: nothing is formatted and no editing session is even made.
        window.KeyPressQwerty(PhysicalKey.F6, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.F6, RawInputModifiers.None);
        WindowFixture.Pump();
        Assert.True(window.OutlineHost!.IsPaneFocused);
        var revision = view.Tab.Docx.Model.Edits.Revision;

        window.KeyPressQwerty(PhysicalKey.B, Command);
        window.KeyReleaseQwerty(PhysicalKey.B, Command);
        WindowFixture.Pump();

        Assert.Equal(revision, view.Tab.Docx.Model.Edits.Revision);
        Assert.Null(view.Tab.Editor);
        Assert.True(window.OutlineHost.IsPaneFocused);
    }

    [AvaloniaFact]
    public async Task KeyRepeatDoesNotRepeatAToggle()
    {
        using var h = await OpenWholeAsync(TwoParagraphs());
        var session = Place(h, P(h, 0), 0, 4);

        // Held: three key downs without a release are one toggle.
        for (var i = 0; i < 3; i++)
        {
            h.Window.KeyPressQwerty(PhysicalKey.B, Command);
        }

        ViewHarness.Pump();
        Assert.Equal("Body", BoldText(P(h, 0)));
        Assert.Equal(1, session.Editor.History.UndoCount);

        // The next press, after the release, is a new toggle; the handler of the window (its rules) is asked once per press.
        h.Window.KeyReleaseQwerty(PhysicalKey.B, Command);
        var asked = new List<string>();
        h.View.FormatKeyHandler = id =>
        {
            asked.Add(id);
            return h.View.RunFormatCommand(id);
        };
        for (var i = 0; i < 3; i++)
        {
            h.Window.KeyPressQwerty(PhysicalKey.B, Command);
        }

        h.Window.KeyReleaseQwerty(PhysicalKey.B, Command);
        ViewHarness.Pump();
        Assert.Equal([WriteCommandIds.Bold], asked);
        Assert.Equal(string.Empty, BoldText(P(h, 0)));
    }

    [AvaloniaFact]
    public async Task FormattingInAHeaderRunsThroughTheSameSessionPath()
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
        var session = view.Tab.Editor!;
        var changed = 0;
        session.Changed += () => changed++;
        var steps = session.Editor.History.UndoCount;

        Stroke(h, PhysicalKey.B, Command);

        // One session operation: one history step and one Changed, the same as typing; the header paragraph is bold and the body is untouched.
        Assert.Equal(1, changed);
        Assert.Equal(steps + 1, session.Editor.History.UndoCount);
        var selection = view.Selection!.Value;
        Assert.Equal(StoryKind.HeaderFooter, selection.End.Story.Kind);
        Assert.Equal("Header text", BoldText(selection.End.Paragraph));
        Assert.Equal(string.Empty, BoldText(P(h, 0)));

        Assert.True(h.View.RunEditCommand(WriteCommandIds.Undo));
        Assert.Equal(steps, session.Editor.History.UndoCount);
        Assert.Equal(string.Empty, BoldText(view.Selection!.Value.End.Paragraph));
    }
}
