using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Raffica.Write.Commands;
using Raffica.Write.Documents;
using Raffica.Write.Docx;
using Raffica.Write.Panes;
using Raffica.Write.Settings;
using Raffica.Write.Viewing;
using Raffica.WriteApp.Tests.Viewing;
using Xunit;

namespace Raffica.WriteApp.Tests.Panes;

/// <summary>The outline: the headings read from the model, the pane, its keys and the navigation to a heading (also one the layout has not reached yet).</summary>
public sealed class OutlinePaneTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private const string StylesXml = """
        <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
          <w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/></w:style>
          <w:style w:type="paragraph" w:styleId="Heading1"><w:name w:val="heading 1"/><w:basedOn w:val="Normal"/></w:style>
          <w:style w:type="paragraph" w:styleId="Chapter"><w:name w:val="Chapter Title"/><w:basedOn w:val="Normal"/><w:pPr><w:outlineLvl w:val="0"/></w:pPr></w:style>
          <w:style w:type="paragraph" w:styleId="SubChapter"><w:name w:val="Sub"/><w:basedOn w:val="Chapter"/><w:pPr><w:outlineLvl w:val="1"/></w:pPr></w:style>
          <w:style w:type="paragraph" w:styleId="Inherits"><w:name w:val="Inherits"/><w:basedOn w:val="SubChapter"/></w:style>
        </w:styles>
        """;

    private static string P(string text, string? style = null, string? pPr = null) =>
        $"<w:p><w:pPr>{(style is null ? string.Empty : $"<w:pStyle w:val=\"{style}\"/>")}{pPr}</w:pPr><w:r><w:t>{text}</w:t></w:r></w:p>";

    // A small document with the given body (and the styles above).
    private static string WriteDocx(string folder, string name, string body)
    {
        var path = Path.Combine(folder, name);
        using var stream = File.Create(path);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
        Entry(zip, "[Content_Types].xml", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>
              <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
              <Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/>
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
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
            </Relationships>
            """);
        Entry(zip, "word/styles.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" + StylesXml);
        Entry(zip, "word/document.xml", $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body>{body}</w:body></w:document>
            """);
        return path;
    }

    private static void Entry(ZipArchive zip, string name, string text)
    {
        var entry = zip.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(text.TrimStart());
    }

    private static IReadOnlyList<OutlineEntry> ReadOutline(string path)
    {
        using var docx = DocxFile.Open(path);
        return OutlineSource.Read(docx.Model);
    }

    private static string TempFolder() => Directory.CreateTempSubdirectory("raffica-outline-").FullName;

    [Fact]
    public void ThesisOutlineHasThirtyChaptersAndTheirSections()
    {
        var entries = ReadOutline(WindowFixture.Thesis);

        // A second, independent pass over the XML of the file: the outline level of a paragraph (its own or its style's, through "basedOn") of the top-level paragraphs.
        using var zip = ZipFile.OpenRead(WindowFixture.Thesis);
        var styles = XDocument.Load(zip.GetEntry("word/styles.xml")!.Open()).Root!.Elements(W + "style").ToDictionary(s => (string)s.Attribute(W + "styleId")!);
        int? LevelOf(string? id, int guard = 0)
        {
            if (id is null || guard > 20 || !styles.TryGetValue(id, out var style))
            {
                return null;
            }

            return (int?)style.Element(W + "pPr")?.Element(W + "outlineLvl")?.Attribute(W + "val") ?? LevelOf((string?)style.Element(W + "basedOn")?.Attribute(W + "val"), guard + 1);
        }

        var expected = new List<(int Level, int Block)>();
        var body = XDocument.Load(zip.GetEntry("word/document.xml")!.Open()).Root!.Element(W + "body")!.Elements().ToList();
        for (var i = 0; i < body.Count; i++)
        {
            if (body[i].Name != W + "p")
            {
                continue;
            }

            var props = body[i].Element(W + "pPr");
            var level = (int?)props?.Element(W + "outlineLvl")?.Attribute(W + "val") ?? LevelOf((string?)props?.Element(W + "pStyle")?.Attribute(W + "val"));
            var text = string.Concat(body[i].Descendants(W + "t").Select(t => t.Value));
            if (level is >= 0 and <= 8 && text.Trim().Length > 0)
            {
                expected.Add((level.Value + 1, i));
            }
        }

        Assert.Equal(expected, entries.Select(e => (e.Level, e.BodyBlockIndex)).ToList());
        Assert.Equal(entries.Count, expected.Count);
        var chapters = entries.Count(e => e.Level == 1);
        Assert.Equal(expected.Count(e => e.Level == 1), chapters);

        // The seeded thesis has 32 level-1 headings (30 numbered chapters plus the two that are not: the notes of the front and back matter); every entry has text.
        Assert.Equal(32, chapters);
        Assert.Equal(32 + 90 + 180, entries.Count);
        Assert.All(entries, e => Assert.False(string.IsNullOrWhiteSpace(e.Text)));
        Assert.Equal(entries.OrderBy(e => e.BodyBlockIndex).ToList(), entries);
    }

    [Fact]
    public void LevelsFollowOutlineLevelNotStyleNames()
    {
        var folder = TempFolder();
        try
        {
            var path = WriteDocx(
                folder,
                "levels.docx",
                P("Named but not outlined", "Heading1")
                + P("By style", "Chapter")
                + P("By inheritance", "Inherits")
                + P("Direct deep level", null, "<w:outlineLvl w:val=\"2\"/>")
                + P("Direct body text level", "Chapter", "<w:outlineLvl w:val=\"9\"/>")
                + P("Plain"));
            var entries = ReadOutline(path);
            Assert.Equal(
                [new OutlineEntry(1, "By style", 1), new OutlineEntry(2, "By inheritance", 2), new OutlineEntry(3, "Direct deep level", 3)],
                entries);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void EmptyHeadingsAreSkippedAndTheTextIsWhatThePageShows()
    {
        var folder = TempFolder();
        try
        {
            const string Heading = "<w:pPr><w:pStyle w:val=\"Chapter\"/></w:pPr>";
            var body =
                $"<w:p>{Heading}</w:p>"
                + $"<w:p>{Heading}<w:r><w:t xml:space=\"preserve\">   </w:t></w:r></w:p>"
                + $"<w:p>{Heading}<w:del w:id=\"1\" w:author=\"a\"><w:r><w:delText>gone</w:delText></w:r></w:del></w:p>"
                + $"<w:p>{Heading}<w:r><w:t xml:space=\"preserve\">  Kept   words </w:t></w:r><w:ins w:id=\"2\" w:author=\"a\"><w:r><w:t xml:space=\"preserve\"> inserted</w:t></w:r></w:ins>"
                + "<w:del w:id=\"3\" w:author=\"a\"><w:r><w:delText>deleted</w:delText></w:r></w:del></w:p>"
                + $"<w:p>{Heading}<w:r><w:t xml:space=\"preserve\">Chapter </w:t></w:r><w:r><w:fldChar w:fldCharType=\"begin\"/></w:r><w:r><w:instrText> SEQ c </w:instrText></w:r>"
                + "<w:r><w:fldChar w:fldCharType=\"separate\"/></w:r><w:r><w:t>7</w:t></w:r><w:r><w:fldChar w:fldCharType=\"end\"/></w:r></w:p>"
                + $"<w:p>{Heading}<w:r><w:fldChar w:fldCharType=\"begin\"/></w:r><w:r><w:instrText> TOC </w:instrText></w:r><w:r><w:fldChar w:fldCharType=\"separate\"/></w:r><w:r><w:fldChar w:fldCharType=\"end\"/></w:r></w:p>"
                + "<w:tbl><w:tr><w:tc><w:p><w:pPr><w:pStyle w:val=\"Chapter\"/></w:pPr><w:r><w:t>In a table</w:t></w:r></w:p></w:tc></w:tr></w:tbl>"
                + $"<w:sdt><w:sdtContent><w:p>{Heading}<w:r><w:t>In a control</w:t></w:r></w:p></w:sdtContent></w:sdt>";
            var entries = ReadOutline(WriteDocx(folder, "empty.docx", body));

            Assert.Equal(
                [new OutlineEntry(1, "Kept words inserted", 3), new OutlineEntry(1, "Chapter 7", 4), new OutlineEntry(1, "In a control", 7)],
                entries);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task PaneIsClosedAndUnbuiltUntilAskedFor()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        Assert.Null(window.OutlineHost);
        var tab = Assert.IsType<Opened>(await window.Flow.OpenAsync(WindowFixture.Thesis)).Tab;
        WindowFixture.Pump();

        // A document is open and the outline was never asked for: no slot pane, no outline pane, nothing in the frame's left slot.
        Assert.Null(window.OutlineHost);
        Assert.Null(window.OutlineOf(tab));
        Assert.Null(window.Frame.LeftPane);
        Assert.False(window.IsOutlineOpen);

        Assert.True(window.RunCommand(WriteCommandIds.Outline));
        var pane = window.OutlineOf(tab);
        Assert.NotNull(pane);
        Assert.NotNull(window.OutlineHost);
        Assert.Same(window.OutlineHost, window.Frame.LeftPane);
        Assert.True(window.IsOutlineOpen);
        Assert.Equal(240, window.OutlineHost!.Width);
        Assert.Same(pane, window.OutlineHost.Content);
        Assert.Equal("Outline", window.OutlineHost.Title);

        // Toggled off: the slot closes and the pane stays for the tab (the outline is read once); on again: the same pane.
        Assert.True(window.RunCommand(WriteCommandIds.Outline));
        Assert.False(window.IsOutlineOpen);
        Assert.Null(window.OutlineHost.Content);
        Assert.True(window.RunCommand(WriteCommandIds.Outline));
        Assert.Same(pane, window.OutlineOf(tab));
        await pane!.LoadTask;
        Assert.Equal(1, pane.ReadCount);
    }

    [AvaloniaFact]
    public async Task TheOutlineCommandIsEnabledWithADocumentAndIsASwitch()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        Assert.False(window.RunCommand(WriteCommandIds.Outline));
        var tab = Assert.IsType<Opened>(await window.Flow.OpenAsync(WindowFixture.Poi)).Tab;
        WindowFixture.Pump();
        var command = window.Ribbon.Definition!.Tabs.SelectMany(t => t.Groups).SelectMany(g => g.Commands).Single(c => c.Id == WriteCommandIds.Outline);
        Assert.True(command.IsEnabled);
        Assert.False(command.IsChecked);
        command.Run!();
        Assert.True(command.IsChecked);
        Assert.True(window.IsOutlineOpen);
        command.Run!();
        Assert.False(command.IsChecked);

        // The pane follows the tab: the second tab gets a pane of its own and a closed tab's pane is disposed.
        command.Run!();
        var first = window.OutlineOf(tab)!;
        var second = Assert.IsType<Opened>(await window.Flow.OpenAsync(WindowFixture.Thesis)).Tab;
        WindowFixture.Pump();
        Assert.NotSame(first, window.OutlineOf(second));
        Assert.Same(window.OutlineOf(second), window.OutlineHost!.Content);
        window.Workspace.Close(second);
        WindowFixture.Pump();
        Assert.Null(window.OutlineOf(second));
        Assert.Same(first, window.OutlineHost.Content);
        window.Workspace.Close(tab);
        WindowFixture.Pump();
        Assert.True(first.IsDisposed);
        Assert.Null(window.OutlineHost.Content);
        Assert.Null(window.Frame.LeftPane);
    }

    [AvaloniaFact]
    public async Task OutlineOfADocumentWithoutHeadingsSaysSo()
    {
        using var fixture = new WindowFixture();
        var path = WriteDocx(fixture.Folder, "plain.docx", P("Just text") + P("More text"));
        var window = fixture.Show();
        var tab = Assert.IsType<Opened>(await window.Flow.OpenAsync(path)).Tab;
        WindowFixture.Pump();
        window.RunCommand(WriteCommandIds.Outline);
        var pane = window.OutlineOf(tab)!;
        await ViewHarness.WaitAsync(() => pane.LoadTask.IsCompleted);
        Assert.Empty(pane.Entries);
        Assert.False(pane.List.IsVisible);
        Assert.True(pane.Hint.IsVisible);
        Assert.Equal("This document has no headings.", pane.Hint.Text);
        Assert.Equal("This document has no headings.", AutomationProperties.GetName(pane.Hint));
    }

    [AvaloniaFact]
    public async Task ThePaneShowsReadingUntilTheWholeModelIsThereAndReadsOnce()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        using var gate = new ManualResetEventSlim(false);
        var tab = Assert.IsType<Opened>(await window.Flow.OpenAsync(WindowFixture.Thesis)).Tab;
        tab.Runner.BeforeFullOpen = () => gate.Wait(TimeSpan.FromSeconds(30));
        WindowFixture.Pump();

        window.RunCommand(WriteCommandIds.Outline);
        var pane = window.OutlineOf(tab)!;
        Assert.False(tab.Runner.FullModelReady);
        Assert.Equal(0, pane.ReadCount);
        Assert.True(pane.Hint.IsVisible);
        Assert.Equal("Reading the headings…", pane.Hint.Text);
        Assert.False(pane.List.IsVisible);

        gate.Set();
        await ViewHarness.WaitAsync(() => pane.LoadTask.IsCompleted, 30000);
        Assert.Equal(1, pane.ReadCount);
        Assert.True(pane.List.IsVisible);
        Assert.False(pane.Hint.IsVisible);
        Assert.Equal(ReadOutline(WindowFixture.Thesis), pane.Entries);
    }

    [AvaloniaFact]
    public async Task KeyboardNavigation()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        var tab = Assert.IsType<Opened>(await window.Flow.OpenAsync(WindowFixture.Thesis)).Tab;
        WindowFixture.Pump();
        window.RunCommand(WriteCommandIds.Outline);
        var pane = window.OutlineOf(tab)!;
        await ViewHarness.WaitAsync(() => pane.LoadTask.IsCompleted, 30000);
        WindowFixture.Pump();
        var chosen = new List<int>();
        pane.Navigate += chosen.Add;
        var entries = pane.Entries;

        // The command put the focus in the list; the keys are the list's own and Enter chooses.
        pane.FocusContent();
        WindowFixture.Pump();
        Assert.Equal(0, pane.List.SelectedIndex);
        window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
        WindowFixture.Pump();
        Assert.Equal(1, pane.List.SelectedIndex);
        window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
        window.KeyPressQwerty(PhysicalKey.ArrowUp, RawInputModifiers.None);
        WindowFixture.Pump();
        Assert.Equal(1, pane.List.SelectedIndex);
        window.KeyPressQwerty(PhysicalKey.End, RawInputModifiers.None);
        WindowFixture.Pump();
        Assert.Equal(entries.Count - 1, pane.List.SelectedIndex);
        window.KeyPressQwerty(PhysicalKey.Home, RawInputModifiers.None);
        WindowFixture.Pump();
        Assert.Equal(0, pane.List.SelectedIndex);
        window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        WindowFixture.Pump();
        Assert.Equal([entries[1].BodyBlockIndex], chosen);
        Assert.Same(window.OutlineHost!.Content, pane);

        // Names for assistive technology: the list, and every realised row says its text and level.
        Assert.Equal("Outline", AutomationProperties.GetName(pane.List));
        var row = Assert.IsAssignableFrom<Control>(pane.List.ContainerFromIndex(0));
        Assert.Equal($"{entries[0].Text}, heading level {entries[0].Level}", AutomationProperties.GetName(row));
    }

    [AvaloniaFact]
    public async Task NavigateToALateHeadingWaitsForTheLayout()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        var tab = Assert.IsType<Opened>(await window.Flow.OpenAsync(WindowFixture.Thesis)).Tab;
        var view = window.ViewOf(tab)!;
        var chapters = ReadOutline(WindowFixture.Thesis).Where(e => e.Level == 1).ToList();
        var target = chapters[20].BodyBlockIndex;

        var ticks = 0;
        var probe = new DispatcherTimer(TimeSpan.FromMilliseconds(10), DispatcherPriority.Normal, (_, _) => ticks++);
        probe.Start();
        try
        {
            var clock = Stopwatch.StartNew();
            window.NavigateTo(tab, target);
            clock.Stop();

            // The call returned at once with the wish pending and the status bar saying what it waits for: the layout is not done and the UI thread did not wait for it.
            Assert.True(clock.ElapsedMilliseconds < 300, $"NavigateTo took {clock.ElapsedMilliseconds} ms");
            Assert.False(tab.Runner.PageLayout.IsComplete);
            Assert.True(view.IsWaitingForAnchor);
            Assert.True(window.IsNavigationWaiting);
            Assert.Equal("Laying out the pages up to the heading…", window.Status.Hint);

            var before = ticks;
            await ViewHarness.WaitAsync(() => !view.IsWaitingForAnchor, 60000);
            Assert.True(ticks > before, "the dispatcher kept running while the navigation waited");
        }
        finally
        {
            probe.Stop();
        }

        Assert.False(window.IsNavigationWaiting);
        Assert.Null(window.Status.Hint);

        // The heading is at the top of the view (a few device-independent pixels of breathing room above it).
        var heading = view.Scroller!.OffsetOf(new ViewAnchor(target, 0));
        Assert.NotNull(heading);
        var gap = heading!.Value - view.OffsetY;
        Assert.InRange(gap, 7, 9);

        // The last chapter: the view ends at the end of the document (the offset is clamped there).
        window.NavigateTo(tab, chapters[^1].BodyBlockIndex);
        await ViewHarness.WaitAsync(() => !window.IsNavigationWaiting, 60000);
        Assert.True(view.OffsetY > 0);
    }

    [AvaloniaFact]
    public async Task ClosingTheTabWhileANavigationWaitsCancelsItWithoutAnError()
    {
        var unobserved = new List<Exception>();
        void OnUnobserved(object? sender, UnobservedTaskExceptionEventArgs e) => unobserved.Add(e.Exception);
        TaskScheduler.UnobservedTaskException += OnUnobserved;
        try
        {
            using var fixture = new WindowFixture();
            var window = fixture.Show();
            var tab = Assert.IsType<Opened>(await window.Flow.OpenAsync(WindowFixture.Thesis)).Tab;
            var view = window.ViewOf(tab)!;
            var last = ReadOutline(WindowFixture.Thesis).Last(e => e.Level == 1).BodyBlockIndex;
            window.NavigateTo(tab, last);
            Assert.True(window.IsNavigationWaiting);

            window.Workspace.Close(tab);
            await ViewHarness.WaitAsync(() => !window.IsNavigationWaiting);
            Assert.Null(window.Status.Hint);
            var offset = view.OffsetY;
            await Task.Delay(300);
            WindowFixture.Pump();
            Assert.Equal(offset, view.OffsetY);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            WindowFixture.Pump();
            Assert.Empty(unobserved);
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= OnUnobserved;
        }
    }

    [AvaloniaFact]
    public async Task AUserScrollEndsTheWaitAndAnotherChoiceReplacesIt()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        var tab = Assert.IsType<Opened>(await window.Flow.OpenAsync(WindowFixture.Thesis)).Tab;
        var view = window.ViewOf(tab)!;
        var chapters = ReadOutline(WindowFixture.Thesis).Where(e => e.Level == 1).ToList();

        window.NavigateTo(tab, chapters[^1].BodyBlockIndex);
        Assert.True(window.IsNavigationWaiting);
        window.NavigateTo(tab, chapters[25].BodyBlockIndex);
        Assert.True(window.IsNavigationWaiting);
        view.ScrollByWheel(-1);
        WindowFixture.Pump();
        Assert.False(view.IsWaitingForAnchor);
        Assert.False(window.IsNavigationWaiting);
        Assert.Null(window.Status.Hint);
    }

    [AvaloniaFact]
    public async Task NavigationToAHeadingOnPageOneNeedsNoWait()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        var path = WriteDocx(fixture.Folder, "first.docx", P("Title", "Chapter") + string.Concat(Enumerable.Range(0, 400).Select(i => P("Line " + i))));
        var tab = Assert.IsType<Opened>(await window.Flow.OpenAsync(path)).Tab;
        var view = window.ViewOf(tab)!;
        window.NavigateTo(tab, 0);
        Assert.False(view.IsWaitingForAnchor);
        Assert.False(window.IsNavigationWaiting);
        Assert.Null(window.Status.Hint);

        // The heading is at the top of the view (the margin above it is scrolled away).
        Assert.InRange(view.Scroller!.OffsetOf(new ViewAnchor(0, 0))!.Value - view.OffsetY, 7, 9);
    }

    [AvaloniaFact]
    public async Task TheWritingViewNavigatesToo()
    {
        using var fixture = new WindowFixture();
        var window = fixture.ShowWith(WriteSettings.Default with { DefaultView = WriteView.Writing });
        var tab = Assert.IsType<Opened>(await window.Flow.OpenAsync(WindowFixture.Thesis)).Tab;
        var view = window.ViewOf(tab)!;
        Assert.Equal(WriteView.Writing, view.Mode);
        var target = ReadOutline(WindowFixture.Thesis).Where(e => e.Level == 1).ElementAt(5).BodyBlockIndex;

        window.NavigateTo(tab, target);
        await ViewHarness.WaitAsync(() => !view.IsWaitingForAnchor && !window.IsNavigationWaiting, 60000);
        Assert.Null(window.Status.Hint);
        var heading = view.Scroller!.OffsetOf(new ViewAnchor(target, 0));
        Assert.NotNull(heading);
        Assert.True(view.OffsetY > 0);
        Assert.InRange(heading!.Value - view.OffsetY, 11, 13);
    }

    [AvaloniaFact]
    public async Task AClickChoosesAHeadingAndTheViewGoesThere()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        var tab = Assert.IsType<Opened>(await window.Flow.OpenAsync(WindowFixture.Thesis)).Tab;
        var view = window.ViewOf(tab)!;
        window.RunCommand(WriteCommandIds.Outline);
        var pane = window.OutlineOf(tab)!;
        await ViewHarness.WaitAsync(() => pane.LoadTask.IsCompleted, 30000);
        await ViewHarness.WaitAsync(() => view.IsLayoutComplete, 60000);
        WindowFixture.Pump();

        var row = pane.Entries.Select((e, i) => (e, i)).First(x => x.e.Level == 1 && x.i > 40).i;
        pane.Choose(row);
        WindowFixture.Pump();
        var heading = view.Scroller!.OffsetOf(new ViewAnchor(pane.Entries[row].BodyBlockIndex, 0));
        Assert.InRange(heading!.Value - view.OffsetY, 7, 9);
        Assert.False(window.IsNavigationWaiting);
    }
}
