using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Raffica.Write;
using Raffica.Write.Commands;
using Raffica.Write.Documents;
using Raffica.Write.Editing;
using Raffica.Write.Model;
using Raffica.Write.Panes;
using Raffica.WriteApp.Tests.Commands;
using Xunit;

namespace Raffica.WriteApp.Tests.Panes;

/// <summary>
/// The formatting pane (Task 12 of milestone 4.3b): the two style lines and the override rows of the selection, the removal of exactly one element per row (the acceptance criterion of the
/// milestone, through the pane and over the whole serialized document), the two buttons, the refresh that waits for typing to pause, the bounded read, the wording in both languages and what
/// the pane never does (exist before it is opened, show text of the document).
/// </summary>
public sealed class FormattingPaneTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    // Overrides on paragraphs (partly: not every paragraph has them), on runs, and on the mark of the empty paragraph.
    private static readonly string[] s_many =
    [
        "<w:pPr><w:pStyle w:val=\"Heading1\"/><w:jc w:val=\"center\"/><w:spacing w:after=\"240\"/><w:ind w:left=\"720\"/></w:pPr>"
            + "<w:r><w:rPr><w:b/><w:rFonts w:ascii=\"Carlito\" w:hAnsi=\"Carlito\"/><w:color w:val=\"FF0000\"/></w:rPr><w:t>first </w:t></w:r><w:r><w:rPr><w:b/><w:i/></w:rPr><w:t>second</w:t></w:r>",
        "<w:pPr><w:jc w:val=\"center\"/><w:rPr><w:b/><w:i/></w:rPr></w:pPr>",
        "<w:pPr><w:spacing w:after=\"240\"/></w:pPr><w:r><w:rPr><w:b/></w:rPr><w:t>third</w:t></w:r>",
    ];

    private static async Task<(WriteWindow Window, WriteTab Tab, EditSession Session)> OpenAsync(WindowFixture fixture, string? styles, string name, params string[] paragraphs)
    {
        var path = StyledDocx.Write(fixture.Folder, name, styles, paragraphs);
        var window = fixture.Show();
        return await OpenIn(window, path);
    }

    private static async Task<(WriteWindow Window, WriteTab Tab, EditSession Session)> OpenIn(WriteWindow window, string path)
    {
        var tab = Assert.IsType<Opened>(await window.Flow.OpenAsync(path)).Tab;
        tab.Runner.LeaseWaitTimeout = TimeSpan.FromSeconds(60);
        tab.Runner.Start();
        await WindowFixture.WaitAsync(() => tab.Runner.FullModelReady || tab.Runner.IsFaulted);
        Assert.True(tab.Runner.FullModelReady);
        var session = tab.EnsureEditor();
        _ = session.Selection;
        return (window, tab, session);
    }

    private static Paragraph P(WriteTab tab, int index) => (Paragraph)tab.Docx.Model.Body.Blocks[index];

    private static int TextLength(Paragraph paragraph) => ParagraphProjection.Build(paragraph, ProjectionHost.Plain).Text.Length;

    private static void Select(EditSession session, WriteTab tab, int firstParagraph, int from, int lastParagraph, int to) =>
        session.SetSelection(new TextRange(new TextPosition(StoryRef.Body, P(tab, firstParagraph), from), new TextPosition(StoryRef.Body, P(tab, lastParagraph), to)));

    private static void SelectAll(EditSession session, WriteTab tab, int last) => Select(session, tab, 0, 0, last, TextLength(P(tab, last)));

    // Opens the pane of the active tab (the command of the View tab) and shows what the selection says now.
    private static FormattingPane Open(WriteWindow window, WriteTab tab)
    {
        if (!window.IsFormattingOpen)
        {
            Assert.True(window.RunCommand(WriteCommandIds.FormattingPane));
        }

        WindowFixture.Pump();
        var pane = window.FormattingOf(tab)!;
        Settle(window, pane);
        return pane;
    }

    // The pane waits for typing to pause; the tests run the waiting refresh now and let the layout of the list happen.
    private static void Settle(WriteWindow window, FormattingPane pane)
    {
        pane.Refresh.Flush();
        WindowFixture.Pump();
        window.UpdateLayout();
        WindowFixture.Pump();
    }

    private static XDocument SavedDocument(WriteTab tab, string folder, string name)
    {
        var path = Path.Combine(folder, name);
        tab.Docx.SaveAsync(path).GetAwaiter().GetResult();
        using var zip = ZipFile.OpenRead(path);
        using var stream = zip.GetEntry("word/document.xml")!.Open();
        return XDocument.Load(stream);
    }

    // The document before, minus the element: from every paragraph's properties (scope paragraph), every run's properties (scope run) or the mark of every paragraph without text.
    private static XDocument Minus(XDocument before, OverrideScope scope, XName element)
    {
        var copy = new XDocument(before);
        foreach (var paragraph in copy.Descendants(W + "p").ToList())
        {
            switch (scope)
            {
                case OverrideScope.Paragraph:
                    paragraph.Element(W + "pPr")?.Elements(element).Remove();
                    break;
                case OverrideScope.Run:
                    foreach (var run in paragraph.Elements(W + "r"))
                    {
                        run.Element(W + "rPr")?.Elements(element).Remove();
                    }

                    break;
                default:
                    if (!paragraph.Descendants(W + "t").Any())
                    {
                        paragraph.Element(W + "pPr")?.Element(W + "rPr")?.Elements(element).Remove();
                    }

                    break;
            }
        }

        return copy;
    }

    private static void Press(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [AvaloniaFact]
    public async Task ThePaneShowsStyleLinesAndRows()
    {
        using var fixture = new WindowFixture();
        var (window, tab, session) = await OpenAsync(fixture, StyledDocx.Styles, "doc.docx", s_many);
        Select(session, tab, 0, 0, 0, 5);
        var pane = Open(window, tab);

        // The style lines say what the file calls the styles; a paragraph without a character style shows "(none)".
        Assert.Null(pane.HintText);
        Assert.Equal("heading 1", pane.ParagraphStyleText);
        Assert.Equal(WriteStrings.StyleNone, pane.CharacterStyleText);

        // Paragraph rows first, then run rows, each with the words of the app and the value in points.
        var words = pane.Rows.Select(r => r.Wording).ToList();
        Assert.Contains(WriteStrings.GetIn("FmtRowPJcCenter", CultureInfo.GetCultureInfo("en")), words);
        Assert.Contains("Space after 12 pt", words);
        Assert.Contains("Left indent 36 pt", words);
        Assert.Contains("Font Carlito", words);
        Assert.Contains("Bold", words);
        Assert.Contains("Color #FF0000", words);
        Assert.True(words.IndexOf("Space after 12 pt") < words.IndexOf("Font Carlito"), "paragraph rows come before run rows");
        Assert.All(pane.Rows, r => Assert.Null(r.State));

        // Nothing of the text of the document is in the pane.
        var shown = string.Join('\n', pane.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
        Assert.DoesNotContain("first", shown, StringComparison.Ordinal);
        Assert.DoesNotContain("second", shown, StringComparison.Ordinal);

        // Every row has a remove button with a name of its own, and the list has a name.
        for (var i = 0; i < pane.Rows.Count; i++)
        {
            Assert.Equal(WriteStrings.Fill(WriteStrings.FormattingRemoveFormat, pane.Rows[i].Wording), Avalonia.Automation.AutomationProperties.GetName(pane.RemoveButtonOf(i)));
        }
    }

    [AvaloniaFact]
    public async Task RemovingARowRemovesExactlyThatOverrideInTheDocument()
    {
        using var fixture = new WindowFixture();
        var (window, tab, session) = await OpenAsync(fixture, StyledDocx.Styles, "doc.docx", s_many);
        SelectAll(session, tab, 2);
        var pane = Open(window, tab);
        var before = SavedDocument(tab, fixture.Folder, "before.docx");
        var count = pane.Rows.Count;
        Assert.True(count >= 9, "the document has paragraph, run and mark rows: " + count);
        Assert.Contains(pane.Rows, r => r.Row.Scope == OverrideScope.ParagraphMark);

        for (var i = 0; i < count; i++)
        {
            var row = pane.Rows[i].Row;
            var undoSteps = session.Editor.History.UndoCount;
            Press(pane.RemoveButtonOf(i));
            WindowFixture.Pump();

            // One undo step, and the whole document equals the document before minus that element (every part is serialized by the writer, never by the code under test).
            Assert.Equal(undoSteps + 1, session.Editor.History.UndoCount);
            var after = SavedDocument(tab, fixture.Folder, "after.docx");
            Assert.True(XNode.DeepEquals(Minus(before, row.Scope, row.Element).Root, after.Root), $"row {i} {row.Scope} {row.Element.LocalName}");
            Assert.DoesNotContain(pane.Rows, r => r.Row.Scope == row.Scope && r.Row.Element == row.Element);

            // Undo brings the document back, and the pane lists the row again.
            Assert.True(window.RunCommand(WriteCommandIds.Undo));
            WindowFixture.Pump();
            SelectAll(session, tab, 2);
            Settle(window, pane);
            Assert.True(XNode.DeepEquals(before.Root, SavedDocument(tab, fixture.Folder, "undone.docx").Root));
            Assert.Equal(count, pane.Rows.Count);
        }
    }

    [AvaloniaFact]
    public async Task PartlyAndMixedRowsShowTheirState()
    {
        using var fixture = new WindowFixture();
        var (window, tab, session) = await OpenAsync(
            fixture,
            StyledDocx.Styles,
            "doc.docx",
            "<w:r><w:rPr><w:b/><w:color w:val=\"FF0000\"/></w:rPr><w:t>one</w:t></w:r><w:r><w:rPr><w:color w:val=\"0000FF\"/></w:rPr><w:t>two</w:t></w:r>");
        Select(session, tab, 0, 0, 0, 6);
        var pane = Open(window, tab);

        var bold = pane.Rows.Single(r => r.Row.Element == W + "b");
        Assert.Equal(OverridePresence.Partly, bold.Row.Presence);
        Assert.Equal(WriteStrings.FormattingPartly, bold.State);

        // Two different colors: one row that says the values differ, and does not repeat the first value as if it were the answer.
        var color = pane.Rows.Single(r => r.Row.Element == W + "color");
        Assert.Equal(OverridePresence.Mixed, color.Row.Presence);
        Assert.Equal(WriteStrings.FormattingMixedValues, color.State);
        Assert.DoesNotContain("FF0000", color.Wording, StringComparison.Ordinal);
        Assert.Equal(WriteStrings.Fill(WriteStrings.FormattingRowNameFormat, color.Wording, color.State), Avalonia.Automation.AutomationProperties.GetName(pane.RemoveButtonOf(pane.Rows.ToList().IndexOf(color)).Parent!));

        // Removing a "partly" row removes it from every run that has it.
        Press(pane.RemoveButtonOf(pane.Rows.ToList().IndexOf(bold)));
        WindowFixture.Pump();
        Assert.DoesNotContain(pane.Rows, r => r.Row.Element == W + "b");
        Assert.All(P(tab, 0).Inlines.OfType<Run>(), run => Assert.Null(run.Properties.Get(W + "b")));
    }

    [AvaloniaFact]
    public async Task TheButtonsRunTheCommandsAsOneUndoStep()
    {
        using var fixture = new WindowFixture();
        var (window, tab, session) = await OpenAsync(fixture, StyledDocx.Styles, "doc.docx", s_many);
        Select(session, tab, 0, 0, 0, 5);
        var pane = Open(window, tab);
        Assert.True(pane.UpdateButton.IsEnabled);
        Assert.True(pane.ClearButton.IsEnabled);
        Assert.Null(pane.UpdateReasonText);

        // Update style to match: the style takes the paragraph's overrides, one history step.
        var steps = session.Editor.History.UndoCount;
        Press(pane.UpdateButton);
        WindowFixture.Pump();
        Assert.Equal(steps + 1, session.Editor.History.UndoCount);
        Assert.NotNull(tab.Docx.Model.Styles!.Find("Heading1")!.ParagraphProperties.Get(W + "jc"));
        Assert.Null(P(tab, 0).Properties.Get(W + "jc"));

        // Clear direct formatting: every row goes, one history step.
        Select(session, tab, 0, 0, 2, TextLength(P(tab, 2)));
        Settle(window, pane);
        Assert.NotEmpty(pane.Rows);
        steps = session.Editor.History.UndoCount;
        Press(pane.ClearButton);
        WindowFixture.Pump();
        Settle(window, pane);
        Assert.Equal(steps + 1, session.Editor.History.UndoCount);
        Assert.Empty(pane.Rows);
        Assert.False(pane.ClearButton.IsEnabled);
        Assert.Equal(WriteStrings.ReasonNoFormattingToClear, pane.ClearReasonText);
    }

    [AvaloniaFact]
    public async Task ThePaneFollowsTheCaretAndEdits()
    {
        using var fixture = new WindowFixture();
        var (window, tab, session) = await OpenAsync(fixture, StyledDocx.Styles, "doc.docx", s_many);
        Select(session, tab, 0, 0, 0, 5);
        var pane = Open(window, tab);
        Assert.Contains(pane.Rows, r => r.Row.Element == W + "rFonts");
        var refreshes = pane.RefreshCount;

        // Many selection changes ask for one refresh: nothing is read until typing and selecting pause.
        for (var i = 0; i < 6; i++)
        {
            Select(session, tab, 2, 0, 2, i % 3);
        }

        Assert.Equal(refreshes, pane.RefreshCount);
        Assert.True(pane.Refresh.IsPending);
        Assert.Contains(pane.Rows, r => r.Row.Element == W + "rFonts");
        var reads = session.InspectorReads;
        Settle(window, pane);
        Assert.Equal(refreshes + 1, pane.RefreshCount);
        Assert.Equal(reads + 1, session.InspectorReads);
        Assert.DoesNotContain(pane.Rows, r => r.Row.Element == W + "rFonts");
        Assert.False(pane.Refresh.IsPending);

        // An edit (here a command) shows in the pane after the pause.
        Select(session, tab, 2, 0, 2, 5);
        Settle(window, pane);
        Assert.DoesNotContain(pane.Rows, r => r.Row.Element == W + "i");
        Assert.True(window.RunCommand(WriteCommandIds.Italic));
        Assert.True(pane.Refresh.IsPending);
        Settle(window, pane);
        Assert.Contains(pane.Rows, r => r.Row.Element == W + "i");

        // The same selection and revision are read once (the answer is remembered).
        reads = session.InspectorReads;
        pane.Refresh.Request();
        Settle(window, pane);
        Assert.Equal(reads, session.InspectorReads);
    }

    [AvaloniaFact]
    public async Task TheSharedStyleNoteShowsOncePerTab()
    {
        using var fixture = new WindowFixture();
        var (window, first, session) = await OpenAsync(fixture, StyledDocx.Styles, "first.docx", s_many);
        Select(session, first, 0, 0, 0, 5);
        var pane = Open(window, first);
        Assert.Equal(WriteStrings.FormattingSharedNote, pane.NoteText);

        // Closing the pane ends the note for this tab: it does not come back when the pane is opened again.
        Assert.True(window.RunCommand(WriteCommandIds.FormattingPane));
        WindowFixture.Pump();
        Assert.False(window.IsFormattingOpen);
        Assert.Null(pane.NoteText);
        pane = Open(window, first);
        Assert.Same(pane, window.FormattingOf(first));
        Assert.Null(pane.NoteText);

        // Another tab has its own pane and its own note.
        var (_, second, secondSession) = await OpenIn(window, StyledDocx.Write(fixture.Folder, "second.docx", StyledDocx.Styles, s_many));
        WindowFixture.Pump();
        Select(secondSession, second, 0, 0, 0, 5);
        var other = window.FormattingOf(second)!;
        Assert.NotSame(pane, other);
        Settle(window, other);
        Assert.Equal(WriteStrings.FormattingSharedNote, other.NoteText);

        // Update style to match ends it as well.
        Press(other.UpdateButton);
        WindowFixture.Pump();
        Assert.Null(other.NoteText);
    }

    [AvaloniaFact]
    public async Task AFaultedSessionDisablesTheButtonsWithAReason()
    {
        using var fixture = new WindowFixture();
        var (window, tab, session) = await OpenAsync(fixture, StyledDocx.Styles, "doc.docx", s_many);
        Select(session, tab, 0, 0, 0, 5);
        var pane = Open(window, tab);
        Assert.True(pane.ClearButton.IsEnabled);

        tab.Runner.FailEdit(new InvalidOperationException("layout failed"));
        pane.Refresh.Request();
        Settle(window, pane);

        Assert.Equal(WriteStrings.EditUnavailable, pane.HintText);
        Assert.False(pane.UpdateButton.IsEnabled);
        Assert.False(pane.ClearButton.IsEnabled);
        Assert.Equal(WriteStrings.EditUnavailable, pane.UpdateReasonText);
        Assert.Equal(WriteStrings.EditUnavailable, pane.ClearReasonText);
        Assert.DoesNotContain(pane.GetVisualDescendants().OfType<Button>(), b => b.Classes.Contains("fmt-remove") && b.IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public async Task TheButtonsOfAnUnstyledSelectionSayWhy()
    {
        using var fixture = new WindowFixture();
        var (window, tab, session) = await OpenAsync(fixture, StyledDocx.Styles, "doc.docx", "<w:pPr><w:pStyle w:val=\"Missing\"/></w:pPr><w:r><w:rPr><w:b/></w:rPr><w:t>text</w:t></w:r>", "<w:r><w:t>plain</w:t></w:r>");
        Select(session, tab, 0, 0, 0, 2);
        var pane = Open(window, tab);

        // An id the file does not define is shown as its id, and Update style says why it cannot run.
        Assert.Equal(WriteStrings.Fill(WriteStrings.FormattingStyleUndefinedFormat, "Missing"), pane.ParagraphStyleText);
        Assert.False(pane.UpdateButton.IsEnabled);
        Assert.Equal(WriteStrings.ReasonStyleNotDefined, pane.UpdateReasonText);
        Assert.Null(pane.NoteText);

        // Two paragraphs of different styles: mixed, with its reason.
        Select(session, tab, 0, 0, 1, 2);
        Settle(window, pane);
        Assert.Equal(WriteStrings.FormattingStyleMixed, pane.ParagraphStyleText);
        Assert.Equal(WriteStrings.ReasonStyleMixed, pane.UpdateReasonText);
    }

    [AvaloniaFact]
    public async Task F6ReachesThePane()
    {
        using var fixture = new WindowFixture();
        var (window, tab, session) = await OpenAsync(fixture, StyledDocx.Styles, "doc.docx", s_many);
        Select(session, tab, 0, 0, 0, 5);
        var pane = Open(window, tab);
        window.ActiveView!.Focus();
        WindowFixture.Pump();
        Assert.True(window.FormattingHost!.IsOpen);
        Assert.True(window.IsFormattingOpen);
        Assert.Same(pane, window.FormattingHost.Content);
        Assert.Equal(WriteStrings.CmdFormatting, window.FormattingHost.Title);

        // The ring is the document and the pane: F6 reaches the pane (a first press may land on the document pane the focus is in already).
        for (var i = 0; i < 3 && !window.FormattingHost.IsPaneFocused; i++)
        {
            window.KeyPressQwerty(PhysicalKey.F6, RawInputModifiers.None);
            WindowFixture.Pump();
        }

        Assert.True(window.FormattingHost.IsPaneFocused);
        Assert.True(window.FormattingHost.IsKeyboardFocusWithin);
    }

    [AvaloniaFact]
    public async Task AfterARemovalTheFocusStaysInThePane()
    {
        using var fixture = new WindowFixture();
        var (window, tab, session) = await OpenAsync(fixture, StyledDocx.Styles, "doc.docx", s_many);
        Select(session, tab, 0, 0, 0, 5);
        var pane = Open(window, tab);
        var button = pane.RemoveButtonOf(0);
        button.Focus();
        WindowFixture.Pump();
        Assert.True(button.IsFocused);

        Press(button);
        WindowFixture.Pump();
        WindowFixture.Pump();
        Assert.True(window.FormattingHost!.IsKeyboardFocusWithin, "the focus stays in the pane");
    }

    [AvaloniaFact]
    public async Task ASelectionOfTheWholeThesisIsReadOnceAndBounded()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        var tab = Assert.IsType<Opened>(await window.Flow.OpenAsync(WindowFixture.Thesis)).Tab;
        tab.Runner.LeaseWaitTimeout = TimeSpan.FromSeconds(60);
        tab.Runner.Start();
        await WindowFixture.WaitAsync(() => tab.Runner.FullModelReady || tab.Runner.IsFaulted);
        var session = tab.EnsureEditor();
        _ = session.Selection;
        var blocks = tab.Docx.Model.Body.Blocks;
        var last = blocks.OfType<Paragraph>().Last();
        session.SetSelection(new TextRange(
            new TextPosition(StoryRef.Body, blocks.OfType<Paragraph>().First(), 0),
            new TextPosition(StoryRef.Body, last, ParagraphProjection.Build(last, ProjectionHost.Plain).Text.Length)));

        var watch = Stopwatch.StartNew();
        var pane = Open(window, tab);
        watch.Stop();

        // One read that did not walk the document: the pane says the selection is too large, and the buttons follow the permissive facts of the rules.
        Assert.Equal(WriteStrings.FormattingTooLarge, pane.HintText);
        Assert.Equal(1, session.InspectorReads);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), watch.Elapsed.ToString());
        Assert.Empty(pane.Rows);
        Assert.True(pane.ClearButton.IsEnabled);
        Assert.True(pane.UpdateButton.IsEnabled);

        // Asking again for the same selection and revision costs nothing.
        pane.Refresh.Request();
        Settle(window, pane);
        Assert.Equal(1, session.InspectorReads);
    }

    [Fact]
    public void EveryKnownElementHasWordingInBothLanguages()
    {
        var english = CultureInfo.GetCultureInfo("en");
        var german = CultureInfo.GetCultureInfo("de");
        var kept = new HashSet<XName>
        {
            W + "pStyle", W + "numPr", W + "sectPr", W + "rPr", W + "pPrChange", W + "rStyle", W + "rPrChange",
        };

        // Every key the wording can use is in both resource files and filled.
        Assert.NotEmpty(FormattingRowWording.KnownKeys);
        foreach (var culture in new[] { english, german })
        {
            Assert.All(FormattingRowWording.KnownKeys, key => Assert.False(string.IsNullOrWhiteSpace(WriteStrings.GetIn("FmtRow" + key, culture)), key));
        }

        var previous = WriteStrings.Culture;
        try
        {
            foreach (var culture in new[] { english, german })
            {
                WriteStrings.Culture = culture;
                foreach (var (scope, kind) in new[] { (OverrideScope.Paragraph, Raffica.Write.Model.PropertyKind.Paragraph), (OverrideScope.Run, Raffica.Write.Model.PropertyKind.Run) })
                {
                    var elements = Raffica.Write.Model.SchemaOrder.NamesOf(kind).Where(name => !kept.Contains(name)).ToList();
                    Assert.True(elements.Count > 20, "the schema lists the elements of " + kind);
                    foreach (var element in elements)
                    {
                        // A row of every scope that can list the element, with and without a value.
                        Assert.True(FormattingRowWording.IsKnown(scope, element), $"{scope} {element.LocalName} has no wording");
                        foreach (var value in new string?[] { null, "0", "val=1", "x" })
                        {
                            var text = FormattingRowWording.Describe(new OverrideRow(scope, element, OverridePresence.All, value));
                            Assert.False(string.IsNullOrWhiteSpace(text), element.LocalName);
                            Assert.NotEqual(element.LocalName, text);
                            Assert.DoesNotContain("{", text, StringComparison.Ordinal);
                        }

                        if (scope == OverrideScope.Run)
                        {
                            // The mark of a paragraph without text carries run properties, worded as the runs' with the mark in front.
                            var mark = FormattingRowWording.Describe(new OverrideRow(OverrideScope.ParagraphMark, element, OverridePresence.All, null));
                            Assert.Contains(FormattingRowWording.Describe(new OverrideRow(scope, element, OverridePresence.All, null)), mark, StringComparison.Ordinal);
                        }
                    }
                }
            }
        }
        finally
        {
            WriteStrings.Culture = previous;
        }
    }

    [Fact]
    public void AnElementWriteHasNoWordingForIsNamedByItsLocalNameOnly()
    {
        // An element of another namespace, one the table does not know and a known name in a foreign namespace: the local name, never a value or a guess.
        Assert.Equal("blink", FormattingRowWording.Describe(new OverrideRow(OverrideScope.Run, XName.Get("blink", "urn:example"), OverridePresence.All, "secret text")));
        Assert.Equal("b", FormattingRowWording.Describe(new OverrideRow(OverrideScope.Run, XName.Get("b", "urn:example"), OverridePresence.All, null)));
        Assert.Equal("wibble", FormattingRowWording.Describe(new OverrideRow(OverrideScope.Paragraph, W + "wibble", OverridePresence.Partly, "x")));
        Assert.False(FormattingRowWording.IsKnown(OverrideScope.Paragraph, W + "wibble"));

        // A long or odd local name is cut and cleaned.
        var text = FormattingRowWording.Describe(new OverrideRow(OverrideScope.Run, XName.Get(new string('x', 200), "urn:example"), OverridePresence.All, null));
        Assert.True(text.Length <= FormattingRowWording.MaxValueLength + 1);
    }

    [Fact]
    public void RowsSayTheirValuesInPointsInTheLanguageOfTheUi()
    {
        var previous = WriteStrings.Culture;
        try
        {
            WriteStrings.Culture = CultureInfo.GetCultureInfo("en");
            string Say(OverrideScope scope, string element, string? value, OverridePresence presence = OverridePresence.All) =>
                FormattingRowWording.Describe(new OverrideRow(scope, W + element, presence, value));

            Assert.Equal("Space after 12 pt", Say(OverrideScope.Paragraph, "spacing", "after=240"));
            Assert.Equal("Space before 6 pt; Space after 12 pt; Line spacing 1.5", Say(OverrideScope.Paragraph, "spacing", "before=120 after=240 line=360 lineRule=auto"));
            Assert.Equal("Line spacing exactly 18 pt", Say(OverrideScope.Paragraph, "spacing", "line=360 lineRule=exact"));
            Assert.Equal("Font Carlito", Say(OverrideScope.Run, "rFonts", "ascii=Carlito hAnsi=Carlito"));
            Assert.Equal("Font Times New Roman", Say(OverrideScope.Run, "rFonts", "ascii=Times New Roman hAnsi=Times New Roman"));
            Assert.Equal("Size 14 pt", Say(OverrideScope.Run, "sz", "28"));
            Assert.Equal("Size 12.5 pt", Say(OverrideScope.Run, "sz", "25"));
            Assert.Equal("Bold", Say(OverrideScope.Run, "b", null));
            Assert.Equal("Bold: off", Say(OverrideScope.Run, "b", "0"));
            Assert.Equal("Paragraph mark: Bold", Say(OverrideScope.ParagraphMark, "b", null));
            Assert.Equal("Font", Say(OverrideScope.Run, "rFonts", "ascii=Carlito", OverridePresence.Mixed));
            Assert.Equal("Highlight Yellow", Say(OverrideScope.Run, "highlight", "yellow"));
            Assert.Equal("Underline double", Say(OverrideScope.Run, "u", "double"));
            Assert.Equal("Outline level 2", Say(OverrideScope.Paragraph, "outlineLvl", "1"));

            WriteStrings.Culture = CultureInfo.GetCultureInfo("de");
            Assert.Equal("Abstand nach 12 pt", Say(OverrideScope.Paragraph, "spacing", "after=240"));
            Assert.Equal("Größe 12,5 pt", Say(OverrideScope.Run, "sz", "25"));
            Assert.Equal("Fett", Say(OverrideScope.Run, "b", null));
            Assert.Equal("Absatzmarke: Fett", Say(OverrideScope.ParagraphMark, "b", null));
        }
        finally
        {
            WriteStrings.Culture = previous;
        }
    }
}
