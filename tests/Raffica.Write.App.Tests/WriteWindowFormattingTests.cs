using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Raffica.Shell.Controls;
using Raffica.Shell.Frame;
using Raffica.Write;
using Raffica.Write.Commands;
using Raffica.Write.Documents;
using Raffica.Write.Editing;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using Raffica.Write.Viewing;
using Raffica.WriteApp.Tests.Commands;
using Xunit;

namespace Raffica.WriteApp.Tests;

/// <summary>The Home groups, the pickers and the display of the formatting in the window (Task 11 of milestone 4.3b).</summary>
public sealed class WriteWindowFormattingTests
{
    private static readonly System.Xml.Linq.XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static CommandDefinition Command(WriteWindow window, string id) =>
        window.Ribbon.Definition!.Tabs.SelectMany(t => t.Groups).SelectMany(g => g.Commands).Single(c => c.Id == id);

    private static async Task<(WriteWindow Window, WriteTab Tab, EditSession Session)> OpenAsync(WindowFixture fixture, string? styles, params string[] paragraphs)
    {
        var path = StyledDocx.Write(fixture.Folder, "doc.docx", styles, paragraphs);
        var window = fixture.Show();
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

    private static void Select(EditSession session, WriteTab tab, int paragraph, int from, int to) =>
        session.SetSelection(new TextRange(new TextPosition(StoryRef.Body, P(tab, paragraph), from), new TextPosition(StoryRef.Body, P(tab, paragraph), to)));

    // The display waits for typing to pause; the tests run the waiting refresh now.
    private static void Settle(WriteWindow window)
    {
        window.FormatRefresh?.Flush();
        WindowFixture.Pump();
    }

    private static SelectBox PickerBox(WriteWindow window, string name) =>
        window.GetVisualDescendants().OfType<SelectBox>().Single(b => b.Classes.Contains("ribbon-picker") && Avalonia.Automation.AutomationProperties.GetName(b) == name);

    [AvaloniaFact]
    public async Task TheRibbonHasTheThreeNewGroupsWithTheirFieldCodes()
    {
        using var fixture = new WindowFixture();
        var (window, _, _) = await OpenAsync(fixture, StyledDocx.Styles, "<w:r><w:t>text</w:t></w:r>");

        var home = window.Ribbon.Definition!.Tabs.Single(t => t.Letter == WriteRibbon.HomeLetter);
        var groups = home.Groups.ToDictionary(g => g.Digit);
        Assert.Equal(WriteStrings.GroupFont, groups["5"].Label);
        Assert.Equal(WriteStrings.GroupParagraph, groups["6"].Label);
        Assert.Equal(WriteStrings.GroupStyles, groups["7"].Label);

        // The codes are the group digit plus the position; no code begins another one of the tab (Validate runs when the ribbon is built).
        Assert.Equal(["51", "52", "53", "54", "55", "56", "57", "58", "59", "5A", "5B", "5C", "5D", "5E", "5F"], groups["5"].Commands.Select(c => c.FieldCode));
        Assert.Equal(["71", "72", "73"], groups["7"].Commands.Select(c => c.FieldCode));
        Assert.Equal(13, groups["6"].Commands.Count);

        // The value commands are pickers (the size steps are buttons); the rest are buttons.
        string[] pickers = [WriteCommandIds.Font, WriteCommandIds.Size, WriteCommandIds.Color, WriteCommandIds.Highlight, WriteCommandIds.LineSpacing, WriteCommandIds.SpaceBefore, WriteCommandIds.SpaceAfter, WriteCommandIds.ParagraphStyle, WriteCommandIds.CharacterStyle];
        Assert.All(home.Groups.SelectMany(g => g.Commands), c => Assert.Equal(pickers.Contains(c.Id), c.Picker is not null));
        Assert.True(Command(window, WriteCommandIds.Size).Picker!.Editable);
        Assert.Equal(4, Command(window, WriteCommandIds.Size).Picker!.MaxLength);
        Assert.NotNull(Command(window, WriteCommandIds.SizeUp).Run);
        Assert.NotNull(Command(window, WriteCommandIds.SizeDown).Run);
    }

    [AvaloniaFact]
    public async Task ThePickerShowsMixedForAMixedSelection()
    {
        using var fixture = new WindowFixture();
        var (window, tab, session) = await OpenAsync(
            fixture,
            StyledDocx.Styles,
            "<w:r><w:rPr><w:b/><w:sz w:val=\"28\"/></w:rPr><w:t>big</w:t></w:r><w:r><w:t>small</w:t></w:r>");

        // The whole paragraph: two sizes, one run bold.
        Select(session, tab, 0, 0, TextLength(P(tab, 0)));
        Settle(window);
        var size = Command(window, WriteCommandIds.Size).Picker!;
        Assert.True(size.IsMixed!());
        Assert.False(Command(window, WriteCommandIds.Bold).IsChecked);
        Assert.Contains("mixed", PickerBox(window, WriteStrings.CmdFormatSize).Classes);

        // Inside the first run: one size, bold on.
        Select(session, tab, 0, 0, 3);
        Settle(window);
        Assert.False(size.IsMixed!());
        Assert.Equal("14", size.SelectedId!());
        Assert.True(Command(window, WriteCommandIds.Bold).IsChecked);
        Assert.DoesNotContain("mixed", PickerBox(window, WriteStrings.CmdFormatSize).Classes);
    }

    [AvaloniaFact]
    public async Task ChoosingAStyleInThePickerAppliesItInOneUndoStep()
    {
        using var fixture = new WindowFixture();
        var (window, tab, session) = await OpenAsync(fixture, StyledDocx.Styles, "<w:r><w:t>heading text</w:t></w:r>");
        Select(session, tab, 0, 2, 2);
        Settle(window);
        var picker = Command(window, WriteCommandIds.ParagraphStyle).Picker!;
        Assert.Equal("Normal", picker.SelectedId!());
        Assert.Contains(picker.Items(), i => i.Id == "Heading1");

        picker.Chosen("Heading1");
        WindowFixture.Pump();

        Assert.Equal("Heading1", P(tab, 0).Properties.StyleId);
        Assert.Equal(1, session.Editor.History.UndoCount);
        Settle(window);
        Assert.Equal("Heading1", picker.SelectedId!());
        Assert.True(Command(window, WriteCommandIds.KeepNext).IsChecked);

        session.Editor.History.Undo();
        Assert.Null(P(tab, 0).Properties.StyleId);
    }

    [AvaloniaFact]
    public async Task TheToggleCommandsShowTheEffectiveStateAndThePendingFormat()
    {
        using var fixture = new WindowFixture();
        var (window, tab, session) = await OpenAsync(
            fixture,
            StyledDocx.Styles,
            "<w:pPr><w:pStyle w:val=\"Heading1\"/><w:jc w:val=\"center\"/></w:pPr><w:r><w:t>title</w:t></w:r>",
            string.Empty);

        // The heading style gives bold, the paragraph centres: the buttons show the effective state, not only direct formatting.
        Select(session, tab, 0, 2, 2);
        Settle(window);
        Assert.True(Command(window, WriteCommandIds.Bold).IsChecked);
        Assert.False(Command(window, WriteCommandIds.Italic).IsChecked);
        Assert.True(Command(window, WriteCommandIds.AlignCenter).IsChecked);
        Assert.False(Command(window, WriteCommandIds.AlignLeft).IsChecked);

        // A pending-only command at a caret raises no edit and no selection change; the display still follows it.
        Select(session, tab, 1, 0, 0);
        Settle(window);
        Assert.False(Command(window, WriteCommandIds.Italic).IsChecked);
        Assert.True(window.RunCommand(WriteCommandIds.Italic));
        Settle(window);
        Assert.True(Command(window, WriteCommandIds.Italic).IsChecked);
        Assert.True(window.RunCommand(WriteCommandIds.Italic));
        Settle(window);
        Assert.False(Command(window, WriteCommandIds.Italic).IsChecked);

        // A pending size shows in the size picker.
        Assert.True(window.RunCommand(WriteCommandIds.Size, 20));
        Settle(window);
        Assert.Equal("20", Command(window, WriteCommandIds.Size).Picker!.SelectedId!());
    }

    [AvaloniaFact]
    public async Task TheValueCommandsRunThroughTheirPickersAndRefuseABadPayload()
    {
        using var fixture = new WindowFixture();
        var (window, tab, session) = await OpenAsync(fixture, StyledDocx.Styles, "<w:r><w:t>abcdef</w:t></w:r>");
        Select(session, tab, 0, 0, 6);
        Settle(window);

        Command(window, WriteCommandIds.Size).Picker!.Chosen("14");
        Command(window, WriteCommandIds.Font).Picker!.Chosen("Carlito");
        Command(window, WriteCommandIds.Color).Picker!.Chosen("FF0000");
        Command(window, WriteCommandIds.Highlight).Picker!.Chosen("yellow");
        Command(window, WriteCommandIds.SpaceAfter).Picker!.Chosen("12");
        Command(window, WriteCommandIds.LineSpacing).Picker!.Chosen("auto:1.5");
        var run = P(tab, 0).Inlines.OfType<Run>().Single();
        Assert.Equal("28", run.Properties.Get(W + "sz")!.Attribute(W + "val")!.Value);
        Assert.Equal("Carlito", run.Properties.Get(W + "rFonts")!.Attribute(W + "ascii")!.Value);
        Assert.Equal("FF0000", run.Properties.Get(W + "color")!.Attribute(W + "val")!.Value);
        Assert.Equal("yellow", run.Properties.Get(W + "highlight")!.Attribute(W + "val")!.Value);
        Assert.Equal("240", P(tab, 0).Properties.Get(W + "spacing")!.Attribute(W + "after")!.Value);
        Assert.Equal("360", P(tab, 0).Properties.Get(W + "spacing")!.Attribute(W + "line")!.Value);

        // The size steps: from 14 pt the next size of the list (16), then back down (14, 12).
        Settle(window);
        Command(window, WriteCommandIds.SizeUp).Run!();
        Assert.Equal("32", P(tab, 0).Inlines.OfType<Run>().Single().Properties.Get(W + "sz")!.Attribute(W + "val")!.Value);
        Settle(window);
        Command(window, WriteCommandIds.SizeDown).Run!();
        Settle(window);
        Command(window, WriteCommandIds.SizeDown).Run!();
        Assert.Equal("24", P(tab, 0).Inlines.OfType<Run>().Single().Properties.Get(W + "sz")!.Attribute(W + "val")!.Value);

        // A bad payload changes nothing and is reported.
        var revision = tab.Docx.Model.Edits.Revision;
        Assert.False(window.RunCommand(WriteCommandIds.Size, "big"));
        Assert.False(window.RunCommand(WriteCommandIds.Size, 5000));
        Assert.False(window.RunCommand(WriteCommandIds.Color, "red"));
        Assert.False(window.RunCommand(WriteCommandIds.ParagraphStyle, "NoSuchStyle"));
        Assert.False(window.RunCommand(WriteCommandIds.Bold, 3));
        Assert.Equal(revision, tab.Docx.Model.Edits.Revision);

        // Typing a size in the size box commits it (comma or point).
        Command(window, WriteCommandIds.Size).Picker!.Chosen("10,5");
        Assert.Equal("21", P(tab, 0).Inlines.OfType<Run>().Single().Properties.Get(W + "sz")!.Attribute(W + "val")!.Value);
        Command(window, WriteCommandIds.Size).Picker!.Chosen("abc");
        Assert.Equal("21", P(tab, 0).Inlines.OfType<Run>().Single().Properties.Get(W + "sz")!.Attribute(W + "val")!.Value);
    }

    [AvaloniaFact]
    public async Task NoStylesPartDisablesTheStylePickersWithAReason()
    {
        using var fixture = new WindowFixture();
        var (window, tab, session) = await OpenAsync(fixture, null, "<w:r><w:t>plain</w:t></w:r>");
        Select(session, tab, 0, 1, 1);
        Settle(window);
        window.Ribbon.RefreshCommandStates();

        Assert.Empty(StylePickerSource.Paragraph(tab.Docx.Model));
        foreach (var id in new[] { WriteCommandIds.ParagraphStyle, WriteCommandIds.CharacterStyle })
        {
            var command = Command(window, id);
            Assert.False(command.IsEnabled);
            Assert.Equal(WriteStrings.ReasonNoStyles, command.CurrentDisabledReason);
        }
    }

    [AvaloniaFact]
    public async Task TheCommandSearchOpensAPicker()
    {
        using var fixture = new WindowFixture();
        var (window, tab, session) = await OpenAsync(fixture, StyledDocx.Styles, "<w:r><w:t>text</w:t></w:r>");
        Select(session, tab, 0, 1, 1);
        Settle(window);

        var font = Command(window, WriteCommandIds.Font);
        Assert.Null(font.Run);
        Assert.False(PickerBox(window, WriteStrings.CmdFormatFont).IsDropDownOpen);
        window.ChooseFromSearch(font);
        WindowFixture.Pump();

        Assert.True(PickerBox(window, WriteStrings.CmdFormatFont).IsDropDownOpen);
        Assert.True(window.Pickers.ListReads >= 1);
    }

    [AvaloniaFact]
    public async Task EveryColourOfTheListsIsReadBackFromTheDocument()
    {
        using var fixture = new WindowFixture();
        var (window, tab, session) = await OpenAsync(fixture, StyledDocx.Styles, "<w:r><w:t>colours</w:t></w:r>");
        Select(session, tab, 0, 0, 7);

        foreach (var color in ColorChoices.TextColors)
        {
            Assert.True(window.RunCommand(WriteCommandIds.Color, color.Id), color.Id);
            Settle(window);
            Assert.Equal(color.Id, Command(window, WriteCommandIds.Color).Picker!.SelectedId!());
        }

        foreach (var highlight in ColorChoices.Highlights)
        {
            Assert.True(window.RunCommand(WriteCommandIds.Highlight, highlight.Id), highlight.Id);
            Settle(window);
            Assert.Equal(highlight.Id, Command(window, WriteCommandIds.Highlight).Picker!.SelectedId!());
        }

        Assert.True(window.RunCommand(WriteCommandIds.Highlight, ColorChoices.NoHighlight));
        Settle(window);
        Assert.Equal(ColorChoices.NoHighlight, Command(window, WriteCommandIds.Highlight).Picker!.SelectedId!());
    }

    [Fact]
    public void EveryColourAndSpacingNameHasBothLanguages()
    {
        var german = System.Globalization.CultureInfo.GetCultureInfo("de");
        var english = System.Globalization.CultureInfo.GetCultureInfo("en");
        string[] keys = [.. ColorChoices.TextColors.Select(c => c.NameKey), .. ColorChoices.Highlights.Select(c => c.NameKey), "HlNone", "LineSingle", "LineDouble", "LineAtLeastFormat", "LineExactFormat", "PointsFormat", "StyleNone"];
        Assert.All(keys, key =>
        {
            Assert.NotEqual(key, WriteStrings.GetIn(key, english));
            Assert.False(string.IsNullOrWhiteSpace(WriteStrings.GetIn(key, german)), key);
        });
        Assert.Equal("Dunkelrot", WriteStrings.GetIn("ColorDarkRed", german));
    }

    [Fact]
    public void TheSizeStepsFollowWordsList()
    {
        Assert.Equal(12f, FormatPickers.StepSize(11f, up: true));
        Assert.Equal(10.5f, FormatPickers.StepSize(11f, up: false));
        Assert.Equal(11f, FormatPickers.StepSize(10.5f, up: true));
        Assert.Equal(8f, FormatPickers.StepSize(9f, up: false));
        Assert.Equal(72f, FormatPickers.StepSize(48f, up: true));
        Assert.Equal(48f, FormatPickers.StepSize(72f, up: false));

        // Beyond the list: 10 pt steps up, never above 1638; below it 1 pt steps, never below 1.
        Assert.Equal(80f, FormatPickers.StepSize(72f, up: true));
        Assert.Equal(1638f, FormatPickers.StepSize(1635f, up: true));
        Assert.Equal(7f, FormatPickers.StepSize(8f, up: false));
        Assert.Equal(1f, FormatPickers.StepSize(1f, up: false));
        Assert.Equal(1f, FormatPickers.StepSize(1.5f, up: false));
    }

    [AvaloniaFact]
    public async Task SelectAllOfTheThesisIsReadBoundedAndOnce()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        var tab = Assert.IsType<Opened>(await window.Flow.OpenAsync(WindowFixture.Thesis)).Tab;
        tab.Runner.LeaseWaitTimeout = TimeSpan.FromSeconds(60);
        tab.Runner.Start();
        await WindowFixture.WaitAsync(() => tab.Runner.FullModelReady || tab.Runner.IsFaulted);
        Assert.True(tab.Runner.FullModelReady);
        var session = tab.EnsureEditor();
        _ = session.Selection;
        var blocks = tab.Docx.Model.Body.Blocks;
        var first = blocks.OfType<Paragraph>().First();
        var last = blocks.OfType<Paragraph>().Last();
        session.SetSelection(new TextRange(new TextPosition(StoryRef.Body, first, 0), new TextPosition(StoryRef.Body, last, TextLength(last))));
        Assert.True(blocks.Count > TextEditor.MaxFactBlocks);

        var watch = Stopwatch.StartNew();
        var format = EditCommandState.FormatOf(tab);
        var firstRead = watch.ElapsedMilliseconds;
        watch.Restart();
        for (var i = 0; i < 50; i++)
        {
            Assert.Same(format, EditCommandState.FormatOf(tab));
            window.Ribbon.RefreshCommandStates();
        }

        // One read of the model, which is bounded (the start of the selection); everything after it is a lookup.
        Assert.NotNull(format);
        Assert.True(format.Truncated);

        // The bounded read is never shown as the whole selection: every value is mixed, no switch is pressed, the pickers say "Mixed".
        Assert.True(format.Bold.Mixed && format.Size.Mixed && format.Font.Mixed && format.ParagraphStyle.Mixed && format.Alignment.Mixed);
        Assert.False(Command(window, WriteCommandIds.Bold).IsChecked);
        Assert.False(Command(window, WriteCommandIds.AlignLeft).IsChecked);
        Assert.True(Command(window, WriteCommandIds.Size).Picker!.IsMixed!());
        Assert.Equal(1, session.FormatReads);
        Assert.True(firstRead < 500, $"the first read took {firstRead} ms");
        Assert.True(watch.ElapsedMilliseconds < 2000, $"50 refreshes took {watch.ElapsedMilliseconds} ms");

        // A 150-block selection is read whole, once.
        var some = blocks.OfType<Paragraph>().Take(150).ToList();
        session.SetSelection(new TextRange(new TextPosition(StoryRef.Body, some[0], 0), new TextPosition(StoryRef.Body, some[^1], TextLength(some[^1]))));
        var part = EditCommandState.FormatOf(tab);
        Assert.False(part!.Truncated);
        Assert.Equal(2, session.FormatReads);
        _ = EditCommandState.FormatOf(tab);
        Assert.Equal(2, session.FormatReads);
    }

    [AvaloniaFact]
    public async Task ASelectionOverTheBoundShowsMixedAndOneAtTheBoundShowsRealValues()
    {
        using var fixture = new WindowFixture();

        // 250 paragraphs: the first 150 bold at 14 pt, the rest plain. The first block is bold, as a heading is.
        var bold = "<w:r><w:rPr><w:b/><w:sz w:val=\"28\"/></w:rPr><w:t>b</w:t></w:r>";
        var plain = "<w:r><w:t>p</w:t></w:r>";
        var (window, tab, session) = await OpenAsync(fixture, StyledDocx.Styles, [.. Enumerable.Repeat(bold, 150), .. Enumerable.Repeat(plain, 100)]);
        var last = tab.Docx.Model.Body.Blocks.Count - 1;

        // 150 blocks: read whole, real values (bold, 14 pt, one style).
        session.SetSelection(new TextRange(new TextPosition(StoryRef.Body, P(tab, 0), 0), new TextPosition(StoryRef.Body, P(tab, 149), 1)));
        Settle(window);
        Assert.False(EditCommandState.FormatOf(tab)!.Truncated);
        Assert.True(Command(window, WriteCommandIds.Bold).IsChecked);
        Assert.False(Command(window, WriteCommandIds.Size).Picker!.IsMixed!());
        Assert.Equal("14", Command(window, WriteCommandIds.Size).Picker!.SelectedId!());

        // Exactly the bound (200 blocks, the last 50 plain): read whole, so really mixed.
        session.SetSelection(new TextRange(new TextPosition(StoryRef.Body, P(tab, 0), 0), new TextPosition(StoryRef.Body, P(tab, 199), 1)));
        Assert.False(EditCommandState.FormatOf(tab)!.Truncated);

        // Select all: over the bound; the bold first block must not show as the whole selection.
        session.SetSelection(new TextRange(new TextPosition(StoryRef.Body, P(tab, 0), 0), new TextPosition(StoryRef.Body, P(tab, last), 1)));
        Settle(window);
        Assert.True(EditCommandState.FormatOf(tab)!.Truncated);
        Assert.False(Command(window, WriteCommandIds.Bold).IsChecked);
        foreach (var id in new[] { WriteCommandIds.Font, WriteCommandIds.Size, WriteCommandIds.Color, WriteCommandIds.ParagraphStyle, WriteCommandIds.LineSpacing, WriteCommandIds.SpaceAfter })
        {
            Assert.True(Command(window, id).Picker!.IsMixed!(), id);
        }

        Assert.Contains("mixed", PickerBox(window, WriteStrings.CmdFormatSize).Classes);

        // Grow font from a mixed read starts from the default size (11 -> 12), not from the first block's 14 pt.
        Command(window, WriteCommandIds.SizeUp).Run!();
        Assert.Equal("24", P(tab, 0).Inlines.OfType<Run>().Single().Properties.Get(W + "sz")!.Attribute(W + "val")!.Value);
    }

    [AvaloniaFact]
    public async Task ALateSessionEventAfterTheWindowClosedStartsNoTimer()
    {
        using var fixture = new WindowFixture();
        var (window, tab, session) = await OpenAsync(fixture, StyledDocx.Styles, "<w:r><w:t>abc</w:t></w:r>");
        Assert.Null(window.FormatRefresh);

        window.Close();
        WindowFixture.Pump();
        session.SetSelection(new TextRange(new TextPosition(StoryRef.Body, P(tab, 0), 1), new TextPosition(StoryRef.Body, P(tab, 0), 2)));
        WindowFixture.Pump();

        Assert.Null(window.FormatRefresh);
    }

    [AvaloniaFact]
    public async Task TheRefreshWaitsForTypingToPauseAndTypingDoesNotReadTheFormat()
    {
        using var fixture = new WindowFixture();
        var (window, tab, session) = await OpenAsync(fixture, StyledDocx.Styles, "<w:r><w:t>abc</w:t></w:r>");
        Select(session, tab, 0, 3, 3);
        Assert.True(window.FormatRefresh!.IsPending);
        var reads = session.FormatReads;

        // Typing restarts the delay; it reads nothing (the display is read when the typing pauses).
        for (var i = 0; i < 20; i++)
        {
            session.Apply(ed => ed.InsertText(session.Selection!.Value, "x"), 0, "typing", typing: true);
        }

        Assert.Equal(reads, session.FormatReads);
        Assert.True(window.FormatRefresh.IsPending);
        Settle(window);
        Assert.False(window.FormatRefresh.IsPending);
        Assert.True(session.FormatReads <= reads + 1);
    }
}
