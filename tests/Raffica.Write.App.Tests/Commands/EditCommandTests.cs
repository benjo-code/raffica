using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Raffica.Core.Viewing;
using Raffica.Shell.Frame;
using Raffica.Write.Commands;
using Raffica.Write.Editing;
using Raffica.Write.Model;
using Raffica.Write.Platform.MacOS;
using Raffica.Write.Viewing;
using Raffica.WriteApp.Tests.Editing;
using Raffica.WriteApp.Tests.Platform;
using Raffica.WriteApp.Tests.Viewing;
using Xunit;

namespace Raffica.WriteApp.Tests.Commands;

/// <summary>Undo, redo, cut, copy, paste and select all as commands of Write (Task 17 of milestone 4.3a): the catalog, the rules, the clipboard and the macOS menu.</summary>
public sealed class EditCommandTests : IDisposable
{
    private static readonly string[] s_editIds = [WriteCommandIds.Undo, WriteCommandIds.Redo, WriteCommandIds.Cut, WriteCommandIds.Copy, WriteCommandIds.Paste, WriteCommandIds.SelectAll];
    private static readonly string[] s_editGroups = ["GroupUndo", "GroupClipboard"];
    private static readonly string[] s_editIcons = ["Undo2", "Redo2", "Scissors", "Copy", "ClipboardPaste", "TextSelect"];
    private static readonly TimeSpan GateTimeout = TimeSpan.FromSeconds(60);
    private static readonly RawInputModifiers SelectAllKey = OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;
    private static readonly XName Bold = XName.Get("b", "http://schemas.openxmlformats.org/wordprocessingml/2006/main");
    private readonly string _folder = Directory.CreateTempSubdirectory("raffica-write-editcmd-").FullName;

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

    // A clipboard that holds one content object, can be told to fail and can be made to wait (an asynchronous read that has not finished).
    private sealed class FakeClipboard : IEditClipboard
    {
        public ClipboardContent? Content { get; set; }

        public bool FailRead { get; set; }

        public bool FailWrite { get; set; }

        public bool Throws { get; set; }

        public int Writes { get; private set; }

        public TaskCompletionSource? Gate { get; set; }

        public async Task<ClipboardContent?> ReadAsync()
        {
            if (Gate is { } gate)
            {
                await gate.Task;
            }

            if (Throws)
            {
                throw new InvalidOperationException("the clipboard is broken");
            }

            return FailRead ? null : Content;
        }

        public Task<bool> WriteAsync(ClipboardContent content)
        {
            if (Throws)
            {
                throw new InvalidOperationException("the clipboard is broken");
            }

            if (FailWrite)
            {
                return Task.FromResult(false);
            }

            Content = content;
            Writes++;
            return Task.FromResult(true);
        }
    }

    private static string[] AllIds() =>
        [.. typeof(WriteCommandIds).GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!)];

    private string WriteDocument(params string[] paragraphs)
    {
        var path = Path.Combine(_folder, Guid.NewGuid().ToString("N") + ".docx");
        var body = string.Concat(paragraphs.Select(p => p.StartsWith("<w:p>", StringComparison.Ordinal) ? p : $"<w:p>{p}</w:p>"));
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
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
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

    private static async Task<ViewHarness> OpenWholeAsync(string path, double width = 900, double height = 1400)
    {
        var harness = new ViewHarness(path, width, height);
        harness.View.Runner.LeaseWaitTimeout = GateTimeout;
        harness.View.Runner.Start();
        await ViewHarness.WaitAsync(() => harness.View.Runner.FullModelReady || harness.View.Runner.IsFaulted, 30000);
        Assert.True(harness.View.Runner.FullModelReady);
        return harness;
    }

    private static Paragraph P(ViewHarness h, int index) => (Paragraph)h.View.Tab.Docx.Model.Body.Blocks[index];

    private static string TextOf(ViewHarness h, Paragraph paragraph) =>
        ParagraphProjection.Build(paragraph, h.View.Runner.PageLayout.ProjectionHost(StoryRef.Body, paragraph)).Text;

    private static EditSession Select(ViewHarness h, Paragraph from, int fromOffset, Paragraph to, int toOffset)
    {
        var session = h.View.Tab.EnsureEditor();
        _ = session.Selection; // binds the editor
        session.SetSelection(new TextRange(new TextPosition(StoryRef.Body, from, fromOffset), new TextPosition(StoryRef.Body, to, toOffset)));
        h.View.Focus();
        return session;
    }

    private static EditSession Place(ViewHarness h, Paragraph paragraph, int offset, int? to = null) => Select(h, paragraph, offset, paragraph, to ?? offset);

    private static string RunText(Run run) => string.Concat(run.Content.OfType<TextContent>().Select(t => t.Text));

    [AvaloniaFact]
    public void EveryEditCommandHasAnIconAndBothStrings()
    {
        foreach (var id in s_editIds)
        {
            var command = WriteCommandCatalog.Commands.Single(c => c.Id == id);
            Assert.Equal(WriteRibbon.HomeLetter, command.Tab.Letter);
            Assert.Contains(command.Group.LabelKey, s_editGroups);
            Assert.True(
                Application.Current!.TryGetResource("Raffica.Icon." + command.Icon, Application.Current!.ActualThemeVariant, out var value),
                id + " " + command.Icon);
            Assert.IsAssignableFrom<Geometry>(value);
            Assert.NotNull(command.DescriptionKey);
            foreach (var language in new[] { "en", "de" })
            {
                var culture = System.Globalization.CultureInfo.GetCultureInfo(language);
                Assert.False(string.IsNullOrWhiteSpace(WriteStrings.GetIn(command.NameKey, culture)), id);
                Assert.False(string.IsNullOrWhiteSpace(WriteStrings.GetIn(command.DescriptionKey!, culture)), id);
                Assert.False(string.IsNullOrWhiteSpace(WriteStrings.GetIn(command.Group.LabelKey, culture)), id);
            }
        }

        // The six icons are the ones of the brief and no two commands of Write share one.
        Assert.Equal(
            s_editIcons,
            s_editIds.Select(id => WriteCommandCatalog.Commands.Single(c => c.Id == id).Icon).ToArray());
        Assert.Equal(WriteCommandCatalog.Commands.Count, WriteCommandCatalog.Commands.Select(c => c.Icon).Distinct().Count());

        // Field codes: unique inside the tab, and the group digit leads (the frame's scheme).
        var codes = WriteCommandCatalog.Tabs.Single(t => t.Letter == WriteRibbon.HomeLetter).Groups.SelectMany(g => g.Commands.Select(c => c.FieldCode)).ToList();
        Assert.Equal(codes.Count, codes.Distinct().Count());
        foreach (var group in WriteCommandCatalog.Tabs.SelectMany(t => t.Groups))
        {
            Assert.All(group.Commands, c => Assert.StartsWith(group.Digit, c.FieldCode, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void WriteCommandRulesGivesAReasonForEveryDisabledState()
    {
        var blocks = new EditBlockedReason?[] { null, EditBlockedReason.StillOpening, EditBlockedReason.Saving, EditBlockedReason.Faulted };
        var seenReasons = new HashSet<string>();
        foreach (var hasDocument in new[] { false, true })
        {
            foreach (var blocked in blocks)
            {
                foreach (var selection in new[] { false, true })
                {
                    foreach (var undo in new[] { false, true })
                    {
                        foreach (var redo in new[] { false, true })
                        {
                            var state = new EditCommandState(blocked, selection, undo, redo);
                            var rules = new WriteCommandRules(() => hasDocument, () => 1.0, () => true, () => state);
                            foreach (var id in AllIds())
                            {
                                var label = $"{id}: document {hasDocument}, {state}";
                                var reason = rules.ReasonFor(id);
                                if (!hasDocument)
                                {
                                    Assert.Null(reason);
                                }
                                else if (rules.Allows(id))
                                {
                                    Assert.True(reason is null, label);
                                }
                                else
                                {
                                    Assert.False(string.IsNullOrWhiteSpace(reason), label);
                                    seenReasons.Add(reason!);
                                }
                            }
                        }
                    }
                }
            }
        }

        // Every kind of reason of the edit commands was met at least once.
        Assert.True(seenReasons.Count >= 6, string.Join(" | ", seenReasons));

        // The rules in detail: a caret is no selection; undo and redo need a step; a save queues paste, undo and redo but not the commands that read the selection now;
        // an empty clipboard is no disabled state (paste is a no-op then).
        var idle = new WriteCommandRules(() => true, () => 1.0, () => true, () => new EditCommandState(null, false, false, false));
        Assert.True(idle.Allows(WriteCommandIds.Paste));
        Assert.True(idle.Allows(WriteCommandIds.SelectAll));
        Assert.False(idle.Allows(WriteCommandIds.Cut) || idle.Allows(WriteCommandIds.Copy) || idle.Allows(WriteCommandIds.Undo) || idle.Allows(WriteCommandIds.Redo));
        Assert.Equal("Nothing is selected.", idle.ReasonFor(WriteCommandIds.Copy));
        Assert.Equal("There is nothing to undo.", idle.ReasonFor(WriteCommandIds.Undo));
        Assert.Equal("There is nothing to redo.", idle.ReasonFor(WriteCommandIds.Redo));
        var saving = new WriteCommandRules(() => true, () => 1.0, () => true, () => new EditCommandState(EditBlockedReason.Saving, true, true, true));
        Assert.True(saving.Allows(WriteCommandIds.Paste) && saving.Allows(WriteCommandIds.Undo) && saving.Allows(WriteCommandIds.Redo));
        Assert.False(saving.Allows(WriteCommandIds.Cut) || saving.Allows(WriteCommandIds.Copy) || saving.Allows(WriteCommandIds.SelectAll));
        Assert.Equal("A save is running.", saving.ReasonFor(WriteCommandIds.Cut));
        var opening = new WriteCommandRules(() => true, () => 1.0, () => true, () => new EditCommandState(EditBlockedReason.StillOpening, true, true, true));
        Assert.All(s_editIds, id => Assert.False(opening.Allows(id), id));
        Assert.Equal("The document is still opening.", opening.ReasonFor(WriteCommandIds.Paste));
        var faulted = new WriteCommandRules(() => true, () => 1.0, () => true, () => new EditCommandState(EditBlockedReason.Faulted, true, true, true));
        Assert.All(s_editIds, id => Assert.False(faulted.Allows(id), id));
        Assert.Equal("Editing is not available for this document because of an error.", faulted.ReasonFor(WriteCommandIds.Paste));
    }

    [Fact]
    public void TheCatalogKeepsTheEditKeysOutOfTheWindowAndTheShortcutsAreRight()
    {
        // The page view owns these keys (a text box has Ctrl+C itself): the window's catalog lookup never returns them.
        Assert.Null(WriteCommandCatalog.Find(Key.C, KeyModifiers.Control, KeyModifiers.Control, macOs: false));
        Assert.Null(WriteCommandCatalog.Find(Key.Z, KeyModifiers.Meta, KeyModifiers.Meta, macOs: true));

        string Text(string id, bool mac) => WriteCommandCatalog.Commands.Single(c => c.Id == id).ShortcutFor(mac)!.Text(mac);
        Assert.Equal("Ctrl+Z", Text(WriteCommandIds.Undo, false));
        Assert.Equal("Ctrl+Y", Text(WriteCommandIds.Redo, false));
        Assert.Equal("Meta+Shift+Z", Text(WriteCommandIds.Redo, true));
        Assert.Equal("Ctrl+X", Text(WriteCommandIds.Cut, false));
        Assert.Equal("Meta+C", Text(WriteCommandIds.Copy, true));
        Assert.Equal("Ctrl+V", Text(WriteCommandIds.Paste, false));
        Assert.Equal("Meta+A", Text(WriteCommandIds.SelectAll, true));

        // The shortcut matcher knows the required Shift.
        var redo = WriteCommandCatalog.Commands.Single(c => c.Id == WriteCommandIds.Redo).ShortcutFor(true)!;
        Assert.True(redo.Matches(Key.Z, KeyModifiers.Meta | KeyModifiers.Shift, KeyModifiers.Meta));
        Assert.False(redo.Matches(Key.Z, KeyModifiers.Meta, KeyModifiers.Meta));

        // The ids of the keys of Task 16 are the commands.
        Assert.Equal(
            [EditingKeys.UndoCommand, EditingKeys.RedoCommand, EditingKeys.CutCommand, EditingKeys.CopyCommand, EditingKeys.PasteCommand],
            [WriteCommandIds.Undo, WriteCommandIds.Redo, WriteCommandIds.Cut, WriteCommandIds.Copy, WriteCommandIds.Paste]);
    }

    [AvaloniaFact]
    public async Task CopyThenPasteInsideWriteKeepsFormatting()
    {
        using var h = await OpenWholeAsync(WriteDocument("<w:r><w:rPr><w:b/></w:rPr><w:t>Bold</w:t></w:r><w:r><w:t xml:space=\"preserve\"> plain tail</w:t></w:r>", "<w:r><w:t>Target</w:t></w:r>"));
        var clipboard = new FakeClipboard();
        h.View.EditClipboard = clipboard;
        Place(h, P(h, 0), 0, 4);

        Assert.True(await h.View.RunEditCommandAsync(WriteCommandIds.Copy));
        Assert.Equal("Bold", clipboard.Content!.PlainText);
        Assert.NotNull(clipboard.Content.Fragment);
        Assert.NotNull(clipboard.Content.SourceId);
        Assert.Equal("document", clipboard.Content.SourcePart);
        Assert.Equal("Bold plain tail", TextOf(h, P(h, 0)));

        Place(h, P(h, 1), 6);
        Assert.True(await h.View.RunEditCommandAsync(WriteCommandIds.Paste));

        Assert.Equal("TargetBold", TextOf(h, P(h, 1)));
        var pasted = P(h, 1).Inlines.OfType<Run>().Single(r => RunText(r) == "Bold");
        Assert.True(pasted.Properties.GetFlag(Bold));
        Assert.False(P(h, 1).Inlines.OfType<Run>().First(r => RunText(r) == "Target").Properties.GetFlag(Bold));
        Assert.Equal(10, h.View.Caret!.Value.Offset);
        Assert.True(h.View.Tab.Editor!.CanUndo);
    }

    [AvaloniaFact]
    public async Task CopyPlacesPlainTextOnTheSystemClipboard()
    {
        using var h = await OpenWholeAsync(WriteDocument("<w:r><w:rPr><w:b/></w:rPr><w:t>Bold</w:t></w:r><w:r><w:t xml:space=\"preserve\"> plain tail</w:t></w:r>", "<w:r><w:t>Second</w:t></w:r>"));
        var topLevel = TopLevel.GetTopLevel(h.View)!;
        Assert.NotNull(topLevel.Clipboard);
        var clipboard = new AvaloniaEditClipboard(() => topLevel.Clipboard);
        await topLevel.Clipboard!.ClearAsync();

        // One data object with the plain text, the fragment and the source; text only readers (other programs) see the text.
        var content = new ClipboardContent("Bold plain", [1, 2, 3], "abc123", "header:rId7");
        Assert.True(await clipboard.WriteAsync(content));
        Assert.Equal("Bold plain", await Avalonia.Input.Platform.ClipboardExtensions.TryGetTextAsync(topLevel.Clipboard));

        var read = await clipboard.ReadAsync();
        Assert.Equal("Bold plain", read!.PlainText);
        Assert.Equal([1, 2, 3], read.Fragment);
        Assert.Equal("abc123", read.SourceId);
        Assert.Equal("header:rId7", read.SourcePart);

        // The command writes the same through the real clipboard class.
        h.View.EditClipboard = clipboard;
        Place(h, P(h, 0), 0, 4);
        Assert.True(await h.View.RunEditCommandAsync(WriteCommandIds.Copy));
        Assert.Equal("Bold", await Avalonia.Input.Platform.ClipboardExtensions.TryGetTextAsync(topLevel.Clipboard));
        var back = await clipboard.ReadAsync();
        Assert.Equal("Bold", back!.PlainText);
        Assert.NotNull(back.Fragment);
        Assert.NotNull(back.SourceId);

        // Plain text from another program has no fragment and no source.
        await Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(topLevel.Clipboard, "from outside");
        var outside = await clipboard.ReadAsync();
        Assert.Equal("from outside", outside!.PlainText);
        Assert.Null(outside.Fragment);
        Assert.Null(outside.SourceId);

        // An empty clipboard reads as nothing.
        await topLevel.Clipboard.ClearAsync();
        Assert.Null(await clipboard.ReadAsync());
    }

    [AvaloniaFact]
    public async Task PasteFromOutsideIsPlainText()
    {
        using var h = await OpenWholeAsync(WriteDocument("<w:r><w:t>Start</w:t></w:r>", "<w:r><w:t>End</w:t></w:r>"));
        var clipboard = new FakeClipboard { Content = new ClipboardContent("one\ttwo\nthree\r\nfour", null) };
        h.View.EditClipboard = clipboard;
        var count = h.View.Tab.Docx.Model.Body.Blocks.Count;
        Place(h, P(h, 0), 5);

        Assert.True(await h.View.RunEditCommandAsync(WriteCommandIds.Paste));

        Assert.Equal(count + 2, h.View.Tab.Docx.Model.Body.Blocks.Count);
        Assert.Equal("Startone\ttwo", TextOf(h, P(h, 0)));
        Assert.Equal("three", TextOf(h, P(h, 1)));
        Assert.Equal("four", TextOf(h, P(h, 2)));
        Assert.Contains(P(h, 0).Inlines.OfType<Run>().SelectMany(r => r.Content), c => c is TabContent);
    }

    [AvaloniaFact]
    public async Task AnEmptyClipboardOrAFailedReadPastesNothing()
    {
        using var h = await OpenWholeAsync(WriteDocument("<w:r><w:t>Start</w:t></w:r>"));
        var clipboard = new FakeClipboard();
        h.View.EditClipboard = clipboard;
        var session = Place(h, P(h, 0), 5);
        var revision = h.View.Tab.Docx.Model.Edits.Revision;

        Assert.True(await h.View.RunEditCommandAsync(WriteCommandIds.Paste));
        clipboard.Content = new ClipboardContent("x", null);
        clipboard.FailRead = true;
        Assert.True(await h.View.RunEditCommandAsync(WriteCommandIds.Paste));
        clipboard.FailRead = false;
        clipboard.Throws = true;
        Assert.True(await h.View.RunEditCommandAsync(WriteCommandIds.Paste));

        Assert.Equal(revision, h.View.Tab.Docx.Model.Edits.Revision);
        Assert.False(session.CanUndo);
        Assert.Equal("Start", TextOf(h, P(h, 0)));
        Assert.Null(h.View.Runner.Fault);
    }

    [AvaloniaFact]
    public async Task AClipboardThatThrowsOrFailsNeverCrashesOrChangesTheModelOnCopy()
    {
        using var h = await OpenWholeAsync(WriteDocument("<w:r><w:t>Some text here</w:t></w:r>"));
        var clipboard = new FakeClipboard { Throws = true };
        h.View.EditClipboard = clipboard;
        Place(h, P(h, 0), 0, 4);
        var revision = h.View.Tab.Docx.Model.Edits.Revision;

        await h.View.RunEditCommandAsync(WriteCommandIds.Copy);
        clipboard.Throws = false;
        clipboard.FailWrite = true;
        await h.View.RunEditCommandAsync(WriteCommandIds.Copy);

        Assert.Equal(revision, h.View.Tab.Docx.Model.Edits.Revision);
        Assert.Equal(0, clipboard.Writes);
        Assert.Equal("Some text here", TextOf(h, P(h, 0)));
    }

    [AvaloniaFact]
    public async Task CutRemovesTheTextAndFillsTheClipboardAndUndoBringsItBack()
    {
        using var h = await OpenWholeAsync(WriteDocument("<w:r><w:t>Hello brave world</w:t></w:r>"));
        var clipboard = new FakeClipboard { Content = new ClipboardContent("old", null) };
        h.View.EditClipboard = clipboard;
        var session = Place(h, P(h, 0), 6, 12);

        Assert.True(await h.View.RunEditCommandAsync(WriteCommandIds.Cut));
        Assert.Equal("Hello world", TextOf(h, P(h, 0)));
        Assert.Equal("brave ", clipboard.Content!.PlainText);
        Assert.Equal(6, h.View.Caret!.Value.Offset);

        Assert.True(await h.View.RunEditCommandAsync(WriteCommandIds.Undo));
        Assert.Equal("Hello brave world", TextOf(h, P(h, 0)));
        Assert.True(session.CanRedo);
        Assert.Equal("brave ", clipboard.Content.PlainText);
    }

    [AvaloniaFact]
    public async Task CutWithARefusedDeleteLeavesTheClipboardAndTheDocumentAlone()
    {
        // The first paragraph ends a section: a cut over it is refused by the editor (the delete would remove the section break).
        var first = "<w:p><w:pPr><w:sectPr><w:pgSz w:w=\"12240\" w:h=\"15840\"/></w:sectPr></w:pPr><w:r><w:t>Section one</w:t></w:r></w:p>";
        using var h = await OpenWholeAsync(WriteDocument(first, "<w:r><w:t>Section two</w:t></w:r>"));
        var clipboard = new FakeClipboard { Content = new ClipboardContent("keep me", null) };
        h.View.EditClipboard = clipboard;
        var session = Select(h, P(h, 0), 3, P(h, 1), 4);
        var revision = h.View.Tab.Docx.Model.Edits.Revision;

        await h.View.RunEditCommandAsync(WriteCommandIds.Cut);

        Assert.Equal("keep me", clipboard.Content!.PlainText);
        Assert.Equal(0, clipboard.Writes);
        Assert.Equal(revision, h.View.Tab.Docx.Model.Edits.Revision);
        Assert.False(session.CanUndo);
        Assert.Equal("Section one", TextOf(h, P(h, 0)));
    }

    [AvaloniaFact]
    public async Task ACutWhoseClipboardWriteFailsIsTakenBackWhenNothingElseHappened()
    {
        using var h = await OpenWholeAsync(WriteDocument("<w:r><w:t>Hello brave world</w:t></w:r>"));
        var clipboard = new FakeClipboard { FailWrite = true, Content = new ClipboardContent("old", null) };
        h.View.EditClipboard = clipboard;
        var session = Place(h, P(h, 0), 6, 12);
        var revision = h.View.Tab.Docx.Model.Edits.Revision;

        await h.View.RunEditCommandAsync(WriteCommandIds.Cut);

        // The text must not vanish into a clipboard that did not take it.
        Assert.Equal("Hello brave world", TextOf(h, P(h, 0)));
        Assert.Equal("old", clipboard.Content!.PlainText);
        Assert.Equal(revision, h.View.Tab.Docx.Model.Edits.Revision);
        Assert.True(session.CanRedo);
    }

    [AvaloniaFact]
    public async Task UndoRedoCommandsRestoreTextAndTheCaretAsAnEmptySelection()
    {
        using var h = await OpenWholeAsync(WriteDocument("<w:r><w:t>Hello world</w:t></w:r>"));
        var session = Place(h, P(h, 0), 5);
        h.Window.KeyTextInput("!!");
        ViewHarness.Pump();
        Assert.Equal("Hello!! world", TextOf(h, P(h, 0)));
        Assert.Equal(7, h.View.Caret!.Value.Offset);

        Assert.True(await h.View.RunEditCommandAsync(WriteCommandIds.Undo));
        Assert.Equal("Hello world", TextOf(h, P(h, 0)));
        Assert.Equal(5, h.View.Caret!.Value.Offset);
        Assert.True(h.View.Selection!.Value.IsEmpty);
        Assert.False(session.CanUndo);
        Assert.True(session.CanRedo);

        Assert.True(await h.View.RunEditCommandAsync(WriteCommandIds.Redo));
        Assert.Equal("Hello!! world", TextOf(h, P(h, 0)));
        Assert.Equal(7, h.View.Caret!.Value.Offset);
        Assert.True(session.CanUndo);
        Assert.False(session.CanRedo);

        // Nothing to redo: a no-op that changes nothing.
        var revision = h.View.Tab.Docx.Model.Edits.Revision;
        await h.View.RunEditCommandAsync(WriteCommandIds.Redo);
        Assert.Equal(revision, h.View.Tab.Docx.Model.Edits.Revision);
    }

    [AvaloniaFact]
    public async Task SelectAllSelectsTheWholeStoryByTheKeyAndByTheCommand()
    {
        using var h = await OpenWholeAsync(WriteDocument("<w:r><w:t>One</w:t></w:r>", "<w:r><w:t>Two</w:t></w:r>", "<w:r><w:t>Three</w:t></w:r>"));
        Place(h, P(h, 1), 1);

        Assert.True(await h.View.RunEditCommandAsync(WriteCommandIds.SelectAll));
        var byCommand = h.View.Selection!.Value;
        Assert.Same(P(h, 0), byCommand.Start.Paragraph);
        Assert.Equal(0, byCommand.Start.Offset);
        Assert.Same(P(h, 2), byCommand.End.Paragraph);
        Assert.Equal(5, byCommand.End.Offset);

        Place(h, P(h, 1), 1);
        h.Window.KeyPressQwerty(PhysicalKey.A, SelectAllKey);
        h.Window.KeyReleaseQwerty(PhysicalKey.A, SelectAllKey);
        ViewHarness.Pump();
        Assert.Equal(byCommand, h.View.Selection!.Value);
    }

    [AvaloniaFact]
    public async Task AFragmentFromAnotherSourceIsForeignAndOneThatCannotBeReadFallsBackToText()
    {
        var styled = "<w:p><w:pPr><w:pStyle w:val=\"Fancy\"/></w:pPr><w:r><w:t>Styled one</w:t></w:r></w:p>";
        using var h = await OpenWholeAsync(WriteDocument(styled, "<w:r><w:t>Styled two</w:t></w:r>", "<w:r><w:t>Target</w:t></w:r>"));
        var clipboard = new FakeClipboard();
        h.View.EditClipboard = clipboard;
        Select(h, P(h, 0), 0, P(h, 1), 10);
        await h.View.RunEditCommandAsync(WriteCommandIds.Copy);
        var copied = clipboard.Content!;
        Assert.NotNull(copied.Fragment);
        Assert.Equal(2, copied.PlainText.Split('\n').Length);

        // Same editor and story: the paragraph style stays.
        var count = h.View.Tab.Docx.Model.Body.Blocks.Count;
        Place(h, P(h, 2), 0);
        await h.View.RunEditCommandAsync(WriteCommandIds.Paste);
        Assert.Equal(count + 1, h.View.Tab.Docx.Model.Body.Blocks.Count);
        Assert.Equal("Fancy", P(h, 2).Properties.StyleId);
        await h.View.RunEditCommandAsync(WriteCommandIds.Undo);
        Assert.Equal(count, h.View.Tab.Docx.Model.Body.Blocks.Count);
        Assert.Null(P(h, 2).Properties.StyleId);

        // Another source (another process or window): the style is not in this document, so it goes.
        clipboard.Content = copied with { SourceId = "another-editor" };
        Place(h, P(h, 2), 0);
        await h.View.RunEditCommandAsync(WriteCommandIds.Paste);
        Assert.Equal(count + 1, h.View.Tab.Docx.Model.Body.Blocks.Count);
        Assert.Null(P(h, 2).Properties.StyleId);
        await h.View.RunEditCommandAsync(WriteCommandIds.Undo);

        // A fragment that cannot be read is treated as absent: the plain text is pasted in the same data object.
        clipboard.Content = new ClipboardContent("fallback text", [0xFF, 0x00, 0x13, 0x37], copied.SourceId, copied.SourcePart);
        Place(h, P(h, 2), 0);
        await h.View.RunEditCommandAsync(WriteCommandIds.Paste);
        Assert.Equal("fallback textTarget", TextOf(h, P(h, 2)));
    }

    [AvaloniaFact]
    public async Task AnAsyncPasteIsDroppedWhenTheTabIsClosedMeanwhile()
    {
        using var h = await OpenWholeAsync(WriteDocument("<w:r><w:t>Start</w:t></w:r>"));
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var clipboard = new FakeClipboard { Gate = gate, Content = new ClipboardContent("pasted", null) };
        h.View.EditClipboard = clipboard;
        Place(h, P(h, 0), 5);
        var model = h.View.Tab.Docx.Model;

        var paste = h.View.RunEditCommandAsync(WriteCommandIds.Paste);
        Assert.False(paste.IsCompleted);
        h.Tab.Dispose();
        ViewHarness.Pump();
        gate.SetResult();
        await paste.WaitAsync(TimeSpan.FromSeconds(20));

        // Dropped without a trace: no exception, nothing inserted.
        Assert.True(paste.IsCompletedSuccessfully);
        Assert.Equal(0, model.Edits.Revision);
        Assert.Equal("Start", string.Concat(((Paragraph)model.Body.Blocks[0]).Inlines.OfType<Run>().Select(RunText)));
    }

    [AvaloniaFact]
    public async Task AnAsyncPasteIsDroppedWhenTheSessionFaultedMeanwhileAndGoesToTheSelectionThatIsThereAfterwards()
    {
        using var h = await OpenWholeAsync(WriteDocument("<w:r><w:t>Start</w:t></w:r>", "<w:r><w:t>Other</w:t></w:r>"));
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var clipboard = new FakeClipboard { Gate = gate, Content = new ClipboardContent("X", null) };
        h.View.EditClipboard = clipboard;
        Place(h, P(h, 0), 5);

        // The caret moves while the clipboard is read: the paste lands where the caret is when the text arrives.
        var paste = h.View.RunEditCommandAsync(WriteCommandIds.Paste);
        Place(h, P(h, 1), 0);
        gate.SetResult();
        await paste.WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Equal("Start", TextOf(h, P(h, 0)));
        Assert.Equal("XOther", TextOf(h, P(h, 1)));

        // The layout fails while the clipboard is read: the paste is refused, the session stays faulted.
        var gate2 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        clipboard.Gate = gate2;
        var second = h.View.RunEditCommandAsync(WriteCommandIds.Paste);
        h.View.Runner.FailEdit(new InvalidOperationException("layout failed"));
        var revision = h.View.Tab.Docx.Model.Edits.Revision;
        gate2.SetResult();
        await second.WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Equal(revision, h.View.Tab.Docx.Model.Edits.Revision);
        Assert.Equal(EditBlockedReason.Faulted, h.View.Tab.Editor!.BlockedReason);
    }

    [AvaloniaFact]
    public async Task PasteUndoAndRedoDuringASaveAreQueuedAndReplayed()
    {
        using var h = await OpenWholeAsync(WriteDocument("<w:r><w:t>Start</w:t></w:r>"));
        var clipboard = new FakeClipboard { Content = new ClipboardContent("++", null) };
        h.View.EditClipboard = clipboard;
        var session = Place(h, P(h, 0), 5);

        var save = session.BeginSave();
        await h.View.RunEditCommandAsync(WriteCommandIds.Paste);
        Assert.Equal(1, session.PendingCount);
        Assert.Equal("Start", TextOf(h, P(h, 0)));
        save.Dispose();
        ViewHarness.Pump();
        Assert.Equal(0, session.PendingCount);
        Assert.Equal("Start++", TextOf(h, P(h, 0)));

        // Undo during a save is queued too.
        var second = session.BeginSave();
        await h.View.RunEditCommandAsync(WriteCommandIds.Undo);
        Assert.Equal("Start++", TextOf(h, P(h, 0)));
        second.Dispose();
        ViewHarness.Pump();
        Assert.Equal("Start", TextOf(h, P(h, 0)));
    }

    [AvaloniaFact]
    public async Task CopyOfAVeryLargeSelectionStillPutsTextOnTheClipboardWithinABound()
    {
        using var h = await OpenWholeAsync(Raffica.WriteApp.Tests.WindowFixture.Thesis, 900, 800);
        var clipboard = new FakeClipboard();
        h.View.EditClipboard = clipboard;
        h.View.Focus();
        h.View.Tab.EnsureEditor();
        Assert.True(await h.View.RunEditCommandAsync(WriteCommandIds.SelectAll));
        Assert.False(h.View.Selection!.Value.IsEmpty);

        var watch = Stopwatch.StartNew();
        var copy = h.View.RunEditCommandAsync(WriteCommandIds.Copy);
        await copy.WaitAsync(TimeSpan.FromSeconds(30));
        watch.Stop();

        Assert.True(copy.IsCompletedSuccessfully);
        Assert.NotNull(clipboard.Content);
        Assert.True(clipboard.Content.PlainText.Length > 100_000, "plain text length " + clipboard.Content.PlainText.Length);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), "copying the whole thesis took " + watch.Elapsed);
    }

    // A codec that changes the model (a revision is raised) and then throws: the stand-in for an editor operation that failed half way.
    private sealed class MutatingCodec(WordDocument model) : IFragmentCodec
    {
        public byte[] Write(IReadOnlyList<Paragraph> paragraphs) => [1];

        public IReadOnlyList<Paragraph> Read(ReadOnlySpan<byte> bytes)
        {
            model.Body.Blocks.Add(new Paragraph());
            model.Edits.Raise();
            throw new InvalidOperationException("failed after a change");
        }
    }

    // The editor's own refusal before it changed anything (a stale position): the StalePositionException is the one failure that is only a refusal.
    private sealed class RefusingCodec : IFragmentCodec
    {
        public byte[] Write(IReadOnlyList<Paragraph> paragraphs) => [1];

        public IReadOnlyList<Paragraph> Read(ReadOnlySpan<byte> bytes) => throw new StalePositionException("refused before any change");
    }

    // Changes the model and fails before the revision is raised: model, history and layout now disagree and nothing records it.
    private sealed class SilentMutatingCodec(WordDocument model) : IFragmentCodec
    {
        public byte[] Write(IReadOnlyList<Paragraph> paragraphs) => [1];

        public IReadOnlyList<Paragraph> Read(ReadOnlySpan<byte> bytes)
        {
            model.Body.Blocks.Add(new Paragraph());
            throw new InvalidOperationException("failed between the change and the revision");
        }
    }

    [AvaloniaFact]
    public async Task AFailureAfterAPartialChangeFaultsTheSessionAndOneBeforeAnyChangeOnlyRefuses()
    {
        using var h = await OpenWholeAsync(WriteDocument("<w:r><w:t>Start</w:t></w:r>"));
        var refused = new List<EditBlockedReason>();
        h.View.InputRefused += refused.Add;
        var clipboard = new FakeClipboard { Content = new ClipboardContent("x", [1, 2, 3], "x", "document") };
        h.View.EditClipboard = clipboard;
        var session = Place(h, P(h, 0), 5);
        var model = h.View.Tab.Docx.Model;

        // Before any change: refused, the session lives on.
        h.View.ApplyOperation(ed =>
        {
            ed.FragmentCodec = new RefusingCodec();
            return null;
        });
        await h.View.RunEditCommandAsync(WriteCommandIds.Paste);
        Assert.Null(session.BlockedReason);
        Assert.Null(h.View.Runner.Fault);
        Assert.Equal(0, model.Edits.Revision);

        // After a change (the operation failed half way): the session is faulted, visibly, through the same helper the keys use.
        h.View.ApplyOperation(ed =>
        {
            ed.FragmentCodec = new MutatingCodec(model);
            return null;
        });
        await h.View.RunEditCommandAsync(WriteCommandIds.Paste);
        Assert.Equal(EditBlockedReason.Faulted, session.BlockedReason);
        Assert.NotNull(h.View.Runner.Fault);
        Assert.Contains(EditBlockedReason.Faulted, refused);
    }

    [AvaloniaFact]
    public async Task AFailureBetweenTheChangeAndTheRevisionFaultsTheSession()
    {
        using var h = await OpenWholeAsync(WriteDocument("<w:r><w:t>Start</w:t></w:r>"));
        var refused = new List<EditBlockedReason>();
        h.View.InputRefused += refused.Add;
        h.View.EditClipboard = new FakeClipboard { Content = new ClipboardContent("x", [1, 2, 3], "x", "document") };
        var session = Place(h, P(h, 0), 5);
        var model = h.View.Tab.Docx.Model;
        h.View.ApplyOperation(ed =>
        {
            ed.FragmentCodec = new SilentMutatingCodec(model);
            return null;
        });

        await h.View.RunEditCommandAsync(WriteCommandIds.Paste);

        // The revision did not move, yet the model has a block it did not have: the session is faulted, not left to type on.
        Assert.Equal(0, model.Edits.Revision);
        Assert.Equal(2, model.Body.Blocks.Count);
        Assert.Equal(EditBlockedReason.Faulted, session.BlockedReason);
        Assert.NotNull(h.View.Runner.Fault);
        Assert.Contains(EditBlockedReason.Faulted, refused);
    }

    [AvaloniaFact]
    public async Task InputDroppedAtTheEndOfASaveReachesTheHintPathOfTheView()
    {
        using var h = await OpenWholeAsync(WriteDocument("<w:r><w:t>Start</w:t></w:r>"));
        var refused = new List<EditBlockedReason>();
        h.View.InputRefused += refused.Add;
        var session = Place(h, P(h, 0), 5);
        h.View.ApplyOperation(_ => null); // the view listens to the session from its first operation on
        var save = session.BeginSave();
        Assert.Null(session.Apply(ed => ed.InsertText(session.Selection!.Value, "a"), 0));
        h.View.Runner.FailEdit(new InvalidOperationException("an edit failed during the save"));

        save.Dispose();

        Assert.Equal([EditBlockedReason.Faulted], refused);
    }

    // Changes the model and then throws the given exception before any revision is raised (the argument exceptions of the framework included).
    private sealed class MutatingThenThrowingCodec(WordDocument model, Exception exception) : IFragmentCodec
    {
        public byte[] Write(IReadOnlyList<Paragraph> paragraphs) => [1];

        public IReadOnlyList<Paragraph> Read(ReadOnlySpan<byte> bytes)
        {
            model.Body.Blocks.Add(new Paragraph());
            throw exception;
        }
    }

    public static TheoryData<string> ArgumentFailures() => new() { "range", "null", "plain" };

    private static ArgumentException ArgumentFailure(string kind) => kind switch
    {
        "range" => new ArgumentOutOfRangeException(null, "after a change"),
        "null" => new ArgumentNullException(null, "after a change"),
        _ => new ArgumentException("after a change"),
    };

    [AvaloniaTheory]
    [MemberData(nameof(ArgumentFailures))]
    public async Task AnArgumentExceptionAfterAChangeFaultsTheSessionOnTheCommandPath(string kind)
    {
        using var h = await OpenWholeAsync(WriteDocument("<w:r><w:t>Start</w:t></w:r>"));
        h.View.EditClipboard = new FakeClipboard { Content = new ClipboardContent("x", [1, 2, 3], "x", "document") };
        var session = Place(h, P(h, 0), 5);
        var model = h.View.Tab.Docx.Model;
        h.View.ApplyOperation(ed =>
        {
            ed.FragmentCodec = new MutatingThenThrowingCodec(model, ArgumentFailure(kind));
            return null;
        });

        await h.View.RunEditCommandAsync(WriteCommandIds.Paste);

        Assert.Equal(0, model.Edits.Revision);
        Assert.Equal(2, model.Body.Blocks.Count);
        Assert.Equal(EditBlockedReason.Faulted, session.BlockedReason);
    }

    [AvaloniaTheory]
    [MemberData(nameof(ArgumentFailures))]
    public async Task AnArgumentExceptionAfterAChangeFaultsTheSessionOnTheKeyPath(string kind)
    {
        using var h = await OpenWholeAsync(WriteDocument("<w:r><w:t>Start</w:t></w:r>"));
        var session = Place(h, P(h, 0), 5);
        var model = h.View.Tab.Docx.Model;

        h.View.ApplyOperation(_ =>
        {
            model.Body.Blocks.Add(new Paragraph());
            throw ArgumentFailure(kind);
        });

        Assert.Equal(2, model.Body.Blocks.Count);
        Assert.Equal(EditBlockedReason.Faulted, session.BlockedReason);
        Assert.NotNull(h.View.Runner.Fault);
    }

    [AvaloniaFact]
    public async Task TheStalePositionRefusalIsAPlainRefusalWithAnUnmovedModelOnTheKeyPath()
    {
        using var h = await OpenWholeAsync(WriteDocument("<w:r><w:t>Start</w:t></w:r>"));
        var refused = new List<EditBlockedReason>();
        h.View.InputRefused += refused.Add;
        var session = Place(h, P(h, 0), 5);
        var model = h.View.Tab.Docx.Model;
        var stranger = new Paragraph();

        // A position in a paragraph that is not in the document: the editor refuses before it changes anything.
        h.View.ApplyOperation(ed => ed.InsertText(new TextRange(new TextPosition(StoryRef.Body, stranger, 0), new TextPosition(StoryRef.Body, stranger, 0)), "x"));

        Assert.Null(session.BlockedReason);
        Assert.Empty(refused);
        Assert.Equal(0, model.Edits.Revision);
        Assert.Single(model.Body.Blocks);
    }

    [AvaloniaFact]
    public async Task AnExceptionThatIsNotTheEditorsOwnRefusalFaultsEvenBeforeAnyChange()
    {
        using var h = await OpenWholeAsync(WriteDocument("<w:r><w:t>Start</w:t></w:r>"));
        var session = Place(h, P(h, 0), 5);

        h.View.ApplyOperation(_ => throw new InvalidOperationException("a bug in an operation"));

        Assert.Equal(EditBlockedReason.Faulted, session.BlockedReason);
    }

    [AvaloniaFact]
    public async Task TheKeyPathAndTheCommandPathShareTheFaultRule()
    {
        using var h = await OpenWholeAsync(WriteDocument("<w:r><w:t>Start</w:t></w:r>"));
        var session = Place(h, P(h, 0), 5);
        var model = h.View.Tab.Docx.Model;

        // An operation of a key that changes the model and then throws.
        h.View.ApplyOperation(ed =>
        {
            ed.InsertText(session.Selection!.Value, "Y");
            throw new InvalidOperationException("failed after a change");
        });

        Assert.Equal(EditBlockedReason.Faulted, session.BlockedReason);
        Assert.NotNull(h.View.Runner.Fault);
        Assert.NotEqual(0, model.Edits.Revision);

        // And the next key is refused without touching the model.
        var revision = model.Edits.Revision;
        h.Window.KeyTextInput("Z");
        ViewHarness.Pump();
        Assert.Equal(revision, model.Edits.Revision);
    }

    [AvaloniaFact]
    public void TheRibbonHasTheEditCommandsWithShortcutsReasonsAndDescriptions()
    {
        var home = WriteCommandCatalog.Tabs.Single(t => t.Letter == WriteRibbon.HomeLetter);
        Assert.Equal(["GroupFile", "GroupUndo", "GroupClipboard", "GroupFont", "GroupParagraph", "GroupStyles", "GroupLists", "GroupOutline", "GroupSearch"], home.Groups.Select(g => g.LabelKey).ToArray());
        var rules = new WriteCommandRules(() => true, () => 1.0, () => true, () => new EditCommandState(null, false, false, false));
        var actions = s_editIds.ToDictionary(id => id, _ => (Action)(() => { }));
        var definition = WriteRibbon.Create(actions, rules);
        var commands = definition.Tabs.Single(t => t.Letter == WriteRibbon.HomeLetter).Groups.SelectMany(g => g.Commands).ToDictionary(c => c.Id);
        foreach (var id in s_editIds)
        {
            Assert.False(string.IsNullOrWhiteSpace(commands[id].Description), id);
        }

        Assert.False(commands[WriteCommandIds.Copy].IsEnabled);
        Assert.Equal("Nothing is selected.", commands[WriteCommandIds.Copy].CurrentDisabledReason);
        Assert.True(commands[WriteCommandIds.Paste].IsEnabled);
        Assert.Equal(OperatingSystem.IsMacOS() ? "Meta+V" : "Ctrl+V", commands[WriteCommandIds.Paste].Shortcut);
    }

    [AvaloniaFact]
    public void MacEditMenuHasUndoCutCopyPasteSelectAll()
    {
        var window = new WriteMacMenuTests.FakeWindow { HasTab = true };
        window.Allowed.UnionWith(s_editIds);
        var menu = new WriteMacMenu(() => window, () => Task.CompletedTask, WriteMenuPart.Window);
        menu.Refresh();

        Assert.Equal(["File", "Edit", "Format", "Layout", "View", "Window"], menu.Menu.Items.OfType<NativeMenuItem>().Select(i => i.Header!).ToArray());
        var edit = menu.Menu.Items.OfType<NativeMenuItem>().Single(i => i.Header == "Edit").Menu!;
        var items = edit.Items.OfType<NativeMenuItem>().Where(i => i is not NativeMenuItemSeparator).ToList();
        Assert.Equal(["Undo", "Redo", "Cut", "Copy", "Paste", "Select all"], items.Select(i => i.Header!).ToArray());
        Assert.Equal(
            [KeyGesture.Parse("Meta+Z"), KeyGesture.Parse("Meta+Shift+Z"), KeyGesture.Parse("Meta+X"), KeyGesture.Parse("Meta+C"), KeyGesture.Parse("Meta+V"), KeyGesture.Parse("Meta+A")],
            items.Select(i => i.Gesture!).ToArray());
        Assert.Equal(s_editIds, new[] { WriteCommandIds.Undo, WriteCommandIds.Redo, WriteCommandIds.Cut, WriteCommandIds.Copy, WriteCommandIds.Paste, WriteCommandIds.SelectAll });

        // The items run the commands of the catalog by id on the window, and what the rules do not allow is disabled.
        foreach (var item in items)
        {
            Assert.True(item.Command!.CanExecute(null));
            item.Command.Execute(null);
        }

        Assert.Equal(s_editIds.Select(id => "run " + id).ToArray(), window.Calls.ToArray());
        window.Allowed.Remove(WriteCommandIds.Cut);
        window.Raise();
        Assert.False(items[2].Command!.CanExecute(null));
        window.DialogOpen = true;
        Assert.False(items[0].Command!.CanExecute(null));
    }

    [Fact]
    public void EditShortcutsAreNotReserved()
    {
        var reserved = EditingKeysTests.s_reservedOnMac;
        var shortcuts = new List<(WriteShortcut Shortcut, string Id)>();
        foreach (var id in s_editIds)
        {
            foreach (var mac in new[] { false, true })
            {
                var shortcut = WriteCommandCatalog.Commands.Single(c => c.Id == id).ShortcutFor(mac)!;
                Assert.Equal(WriteShortcutBase.Command, shortcut.Base);
                if (mac)
                {
                    var modifiers = KeyModifiers.Meta | (shortcut.Shift ? KeyModifiers.Shift : KeyModifiers.None);
                    foreach (var key in shortcut.Keys)
                    {
                        Assert.DoesNotContain(reserved, r => r.Key == key && r.Modifiers == modifiers);
                    }

                    shortcuts.Add((shortcut, id));
                }
            }
        }

        // The audit sees something, and the six are distinct on the Mac.
        Assert.Equal(6, shortcuts.Select(s => s.Shortcut.Text(true)).Distinct().Count());
        Assert.NotEmpty(reserved);
    }

    [AvaloniaFact]
    public async Task TheMenuLeavesTheEditKeysToATextBoxThatHasTheFocus()
    {
        using var fixture = new Raffica.WriteApp.Tests.WindowFixture();
        var window = fixture.Show();
        var tab = Assert.IsType<Raffica.Write.Documents.Opened>(await window.Flow.OpenAsync(Raffica.WriteApp.Tests.WindowFixture.Poi)).Tab;
        Raffica.WriteApp.Tests.WindowFixture.Pump();
        tab.Runner.Start();
        await ViewHarness.WaitAsync(() => tab.Runner.FullModelReady, 30000);
        var view = window.ViewOf(tab)!;
        var session = tab.EnsureEditor();
        var first = session.Selection!.Value.Start;
        session.SetSelection(new TextRange(first, first with { Offset = 3 }));
        view.Focus();
        window.Ribbon.RefreshCommandStates();
        var menuWindow = (IWriteMenuWindow)window;
        Assert.True(menuWindow.CanRun(WriteCommandIds.Copy));
        Assert.True(menuWindow.CanRun(WriteCommandIds.Cut));

        // The command search has the focus: Cmd+C is the box's own, so the menu item steps aside (a disabled key equivalent goes on to the focused control).
        window.TitleSearch.FocusAndSelect();
        ViewHarness.Pump();
        Assert.False(menuWindow.CanRun(WriteCommandIds.Copy));
        Assert.False(menuWindow.CanRun(WriteCommandIds.Paste));
        Assert.True(menuWindow.CanRun(WriteCommandIds.ZoomIn));
    }

    private static async Task<(Raffica.Write.WriteWindow Window, Raffica.Write.Documents.WriteTab Tab, PageView View)> OpenWindowAsync(Raffica.WriteApp.Tests.WindowFixture fixture)
    {
        var window = fixture.Show();
        var tab = Assert.IsType<Raffica.Write.Documents.Opened>(await window.Flow.OpenAsync(Raffica.WriteApp.Tests.WindowFixture.Poi)).Tab;
        Raffica.WriteApp.Tests.WindowFixture.Pump();
        tab.Runner.Start();
        await ViewHarness.WaitAsync(() => tab.Runner.FullModelReady, 30000);
        var session = tab.EnsureEditor();
        _ = session.Selection;
        var view = window.ViewOf(tab)!;
        view.Focus();
        ViewHarness.Pump();
        return (window, tab, view);
    }

    private static RibbonButton ButtonOf(Raffica.Write.WriteWindow window, string id) =>
        window.GetVisualDescendants().OfType<RibbonButton>().First(b => b.Definition.Id == id);

    [AvaloniaFact]
    public async Task ARibbonEditCommandReturnsTheFocusToThePageView()
    {
        using var fixture = new Raffica.WriteApp.Tests.WindowFixture();
        var (window, tab, view) = await OpenWindowAsync(fixture);
        var session = tab.EnsureEditor();
        window.KeyTextInput("Z");
        ViewHarness.Pump();
        Assert.True(session.CanUndo);
        var first = session.Selection!.Value.Start with { Offset = 0 };
        session.SetSelection(new TextRange(first, first with { Offset = 3 }));
        view.EditClipboard = new FakeClipboard();

        // A click on a ribbon button gives the button the focus, then runs the command of the definition, as the ribbon does.
        foreach (var id in new[] { WriteCommandIds.Copy, WriteCommandIds.SelectAll, WriteCommandIds.Undo })
        {
            window.Ribbon.RefreshCommandStates();
            var button = ButtonOf(window, id);
            Assert.True(button.Focus(), id + " cannot take the focus in this test");
            Assert.True(button.IsFocused);
            button.Definition.Run!();
            ViewHarness.Pump();
            Assert.True(view.IsFocused, id + " left the focus on the ribbon");
        }

        // Typing goes to the page, not to a button.
        var before = tab.Docx.Model.Edits.Revision;
        window.KeyTextInput("Q");
        ViewHarness.Pump();
        Assert.NotEqual(before, tab.Docx.Model.Edits.Revision);
    }

    [AvaloniaFact]
    public async Task TheRibbonStateOfCutAndCopyFollowsTheSelectionAfterAnEditCollapsedIt()
    {
        using var fixture = new Raffica.WriteApp.Tests.WindowFixture();
        var (window, _, _) = await OpenWindowAsync(fixture);
        var copy = ButtonOf(window, WriteCommandIds.Copy);
        var cut = ButtonOf(window, WriteCommandIds.Cut);
        Assert.False(copy.IsEnabled);

        // Select with the keys, type over the selection (it collapses), then select everything.
        for (var i = 0; i < 3; i++)
        {
            window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.Shift);
            window.KeyReleaseQwerty(PhysicalKey.ArrowRight, RawInputModifiers.Shift);
        }

        ViewHarness.Pump();
        Assert.True(copy.IsEnabled && cut.IsEnabled);
        window.KeyTextInput("X");
        ViewHarness.Pump();
        Assert.False(copy.IsEnabled || cut.IsEnabled);

        window.KeyPressQwerty(PhysicalKey.A, SelectAllKey);
        window.KeyReleaseQwerty(PhysicalKey.A, SelectAllKey);
        ViewHarness.Pump();
        Assert.True(copy.IsEnabled && cut.IsEnabled, "the ribbon still says nothing is selected");
        Assert.Null(copy.Definition.CurrentDisabledReason);
        Assert.Null(cut.Definition.CurrentDisabledReason);
    }

    [AvaloniaFact]
    public async Task AnEditFailureShowsTheEditFailureWordingAndNoOpenErrorCard()
    {
        using var fixture = new Raffica.WriteApp.Tests.WindowFixture();
        var (window, tab, view) = await OpenWindowAsync(fixture);
        var model = tab.Docx.Model;

        // An operation that changed the model and then failed.
        view.ApplyOperation(ed =>
        {
            ed.InsertText(tab.Editor!.Selection!.Value, "Y");
            throw new InvalidOperationException("failed after a change");
        });
        ViewHarness.Pump();
        await Task.Delay(100);
        ViewHarness.Pump();

        Assert.Equal(EditBlockedReason.Faulted, tab.Editor!.BlockedReason);
        Assert.False(window.Host.IsOpen, "the card of a file that could not be read came up for an edit failure");
        Assert.Equal(WriteStrings.EditFailed, window.Status.Hint);
        Assert.DoesNotContain("has not been changed", WriteStrings.EditFailed, StringComparison.Ordinal);
        Assert.Contains("since the last save may be lost", WriteStrings.EditFailed, StringComparison.Ordinal);
        Assert.NotEqual(0, model.Edits.Revision);
    }

    [AvaloniaFact]
    public async Task ALayoutFailureDuringAnEditAlsoShowsTheEditFailureWordingAndNoCard()
    {
        using var fixture = new Raffica.WriteApp.Tests.WindowFixture();
        var (window, tab, _) = await OpenWindowAsync(fixture);

        tab.Runner.FailEdit(new InvalidOperationException("layout failed"));
        ViewHarness.Pump();
        await Task.Delay(100);
        ViewHarness.Pump();

        Assert.False(window.Host.IsOpen);
        Assert.Equal(WriteStrings.EditFailed, window.Status.Hint);
    }

    [AvaloniaFact]
    public async Task PasteOfNothingUsableChangesNothingAndKeepsTheSelection()
    {
        using var h = await OpenWholeAsync(WriteDocument("<w:r><w:t>Keep this text</w:t></w:r>"));
        var clipboard = new FakeClipboard();
        h.View.EditClipboard = clipboard;
        var session = Place(h, P(h, 0), 0, 4);
        var revision = h.View.Tab.Docx.Model.Edits.Revision;

        // Empty text and no fragment; empty text and an unreadable fragment: nothing is pasted and the selection is not deleted.
        clipboard.Content = new ClipboardContent(string.Empty, null);
        await h.View.RunEditCommandAsync(WriteCommandIds.Paste);
        clipboard.Content = new ClipboardContent(string.Empty, [0xFF, 0x00, 0x13, 0x37], "x", "document");
        await h.View.RunEditCommandAsync(WriteCommandIds.Paste);

        Assert.Equal(revision, h.View.Tab.Docx.Model.Edits.Revision);
        Assert.Equal("Keep this text", TextOf(h, P(h, 0)));
        Assert.False(session.CanUndo);
        Assert.False(h.View.Selection!.Value.IsEmpty);

        // A readable fragment with no text still pastes.
        Select(h, P(h, 0), 0, P(h, 0), 4);
        await h.View.RunEditCommandAsync(WriteCommandIds.Copy);
        var copied = clipboard.Content!;
        clipboard.Content = copied with { PlainText = string.Empty };
        Place(h, P(h, 0), 14);
        await h.View.RunEditCommandAsync(WriteCommandIds.Paste);
        Assert.Equal("Keep this textKeep", TextOf(h, P(h, 0)));
    }
}
