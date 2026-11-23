using System.Xml.Linq;
using Raffica.Write.Editing;
using Raffica.Write.Model;
using Raffica.Write.Tests.Layout;
using Xunit;

namespace Raffica.Write.Tests.Editing;

/// <summary>The history step of a section change: the content of every swapped section and the flag of left and right pages come back together, with the blocks of the step when it has any.</summary>
public sealed class SectionHistoryTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private sealed class FakeTime : TimeProvider
    {
        public override long TimestampFrequency => 1000;

        public override long GetTimestamp() => 0;
    }

    // Three sections: "a" and "b" make the first (b carries its break), "c" and "d" the second (d carries), "e" is the last with the body's own sectPr.
    private static WordDocument Three()
    {
        var body = PageTestKit.P("a") + PageTestKit.PEnd("b", PageTestKit.Sect()) + PageTestKit.P("c") + PageTestKit.PEnd("d", PageTestKit.Sect()) + PageTestKit.P("e") + PageTestKit.Sect();
        var document = LayoutModel.Document(body);
        document.Settings = new DocumentSettings(new OoxmlRoot(W + "settings", []));
        return document;
    }

    private static string? Width(SectionProperties properties) => (string?)properties.Get(W + "pgSz")?.Attribute(W + "w");

    // What a page-setup command does by hand: the width of every section in the list changes, the step is recorded with the clones.
    private static SectionChange Widen(WordDocument document, IEnumerable<SectionProperties> sections, string width, bool? flagAfter = null)
    {
        var swaps = new List<SectionSwap>();
        foreach (var live in sections)
        {
            var old = live.Clone();
            live.Get(W + "pgSz")!.SetAttributeValue(W + "w", width);
            swaps.Add(new SectionSwap(live, old, live.Clone()));
        }

        bool? before = null;
        if (flagAfter is { } flag)
        {
            before = document.Settings!.EvenAndOddHeaders;
            document.Settings.EvenAndOddHeaders = flag;
        }

        return new SectionChange(swaps, before, flagAfter);
    }

    private static void Do(WordDocument document, EditHistory history, Func<SectionChange> change)
    {
        var revision = history.BeginOperation();
        var made = change();
        document.Edits.Raise();
        var result = new EditResult(new TextPosition(StoryRef.Body, (Paragraph)document.Body.Blocks[0], 0), [], Sections: made);
        history.Record(result, new EditHistory.Point(0, 0), revision, null);
    }

    [Fact]
    public void UndoRestoresTheContentOfEverySwappedSection()
    {
        var document = Three();
        var history = new EditHistory(document, new FakeTime(), HistoryLimits.Default);
        var all = SectionList.Of(document).Select(s => s.Properties).ToList();
        Do(document, history, () => Widen(document, all, "5000"));
        Assert.All(all, s => Assert.Equal("5000", Width(s)));

        var undone = history.Undo();

        Assert.NotNull(undone?.Sections);
        Assert.All(all, s => Assert.Equal("12240", Width(s)));
        foreach (var pair in all.Zip(SectionList.Of(document)))
        {
            Assert.Same(pair.First, pair.Second.Properties);
        }
    }

    [Fact]
    public void RedoSwapsAgain()
    {
        var document = Three();
        var history = new EditHistory(document, new FakeTime(), HistoryLimits.Default);
        var all = SectionList.Of(document).Select(s => s.Properties).ToList();
        Do(document, history, () => Widen(document, all, "5000"));
        history.Undo();

        var redone = history.Redo();

        Assert.NotNull(redone?.Sections);
        Assert.All(all, s => Assert.Equal("5000", Width(s)));
        history.Undo();
        Assert.All(all, s => Assert.Equal("12240", Width(s)));
    }

    [Fact]
    public void ABlockChangeAndASectionChangeAreOneStep()
    {
        var document = Three();
        var history = new EditHistory(document, new FakeTime(), HistoryLimits.Default);
        var carrier = (Paragraph)document.Body.Blocks[1];
        var revision = history.BeginOperation();
        var old = new Block[] { carrier.Clone() };
        var change = Widen(document, [carrier.SectionBreak!], "5000");
        carrier.Inlines.Clear();
        document.Edits.Raise();
        var result = new EditResult(new TextPosition(StoryRef.Body, carrier, 0), [new BlockChange(StoryRef.Body, document.Body.Blocks, 1, 1, 1, old)], Sections: change);
        history.Record(result, new EditHistory.Point(1, 0), revision, null);
        Assert.Equal(1, history.UndoCount);

        var undone = history.Undo();

        Assert.NotNull(undone?.Sections);
        var restored = (Paragraph)document.Body.Blocks[1];
        Assert.Equal("12240", Width(restored.SectionBreak!));
        Assert.Single(restored.Inlines);
        history.Redo();
        var again = (Paragraph)document.Body.Blocks[1];
        Assert.Equal("5000", Width(again.SectionBreak!));
        Assert.Empty(again.Inlines);
    }

    [Fact]
    public void TheStepNeverCoalescesWithTyping()
    {
        var document = Three();
        var editor = new TextEditor(document, (_, _) => ProjectionHost.Plain, new FakeTime());

        void Type(string text)
        {
            var p = (Paragraph)document.Body.Blocks[0];
            var at = new TextPosition(StoryRef.Body, p, ParagraphProjection.Build(p, ProjectionHost.Plain).Text.Length);
            editor.InsertText(new TextRange(at, at), text);
        }

        Type("x");
        Type("y");
        Assert.Equal(1, editor.History.UndoCount);
        var final = SectionList.Of(document)[^1].Properties;
        Do(document, editor.History, () => Widen(document, [final], "5000"));
        Assert.Equal(2, editor.History.UndoCount);
        Type("z");

        Assert.Equal(3, editor.History.UndoCount);
        editor.History.Undo();
        Assert.Equal("5000", Width(final));
        editor.History.Undo();
        Assert.Equal("12240", Width(final));
    }

    [Fact]
    public void TheFlagOfLeftAndRightPagesComesBack()
    {
        var document = Three();
        var history = new EditHistory(document, new FakeTime(), HistoryLimits.Default);
        var final = SectionList.Of(document)[^1].Properties;
        Do(document, history, () => Widen(document, [final], "5000", flagAfter: true));
        Assert.True(document.Settings!.EvenAndOddHeaders);

        history.Undo();
        Assert.False(document.Settings.EvenAndOddHeaders);
        history.Redo();
        Assert.True(document.Settings.EvenAndOddHeaders);
    }

    private static string TextOf(WordDocument document, int block) => ParagraphProjection.Build((Paragraph)document.Body.Blocks[block], ProjectionHost.Plain).Text;

    private static void TypeInCarrier(TextEditor editor, WordDocument document, string text)
    {
        var carrier = (Paragraph)document.Body.Blocks[1];
        var at = new TextPosition(StoryRef.Body, carrier, ParagraphProjection.Build(carrier, ProjectionHost.Plain).Text.Length);
        Assert.NotNull(editor.InsertText(new TextRange(at, at), text));
    }

    // The section object of the first section: the same object at every point of a walk, because a block undo or redo of its carrier paragraph keeps it.
    private static void AssertFirstSection(WordDocument document, SectionProperties live, string width, string text)
    {
        var first = SectionList.Of(document)[0];
        Assert.Same(live, first.Properties);
        Assert.Equal(width, Width(live));
        Assert.Equal(text, TextOf(document, 1));
    }

    [Fact]
    public void ASectionStepThenTypingInItsCarrierWalksUndoAndRedoWithoutClearingTheHistory()
    {
        var document = Three();
        var editor = new TextEditor(document, (_, _) => ProjectionHost.Plain, new FakeTime());
        var live = SectionList.Of(document)[0].Properties;
        Do(document, editor.History, () => Widen(document, [live], "5000"));
        TypeInCarrier(editor, document, "x");
        Assert.Equal(2, editor.History.UndoCount);
        AssertFirstSection(document, live, "5000", "bx");

        Assert.NotNull(editor.History.Undo());
        AssertFirstSection(document, live, "5000", "b");
        Assert.NotNull(editor.History.Undo());
        AssertFirstSection(document, live, "12240", "b");
        Assert.False(editor.History.CanUndo);

        Assert.NotNull(editor.History.Redo());
        AssertFirstSection(document, live, "5000", "b");
        Assert.NotNull(editor.History.Redo());
        AssertFirstSection(document, live, "5000", "bx");
        Assert.Equal(2, editor.History.UndoCount);
        Assert.False(editor.History.CanRedo);
    }

    [Fact]
    public void TypingInTheCarrierThenASectionStepWalksUndoAndRedoWithoutClearingTheHistory()
    {
        var document = Three();
        var editor = new TextEditor(document, (_, _) => ProjectionHost.Plain, new FakeTime());
        var live = SectionList.Of(document)[0].Properties;
        TypeInCarrier(editor, document, "x");
        Do(document, editor.History, () => Widen(document, [live], "5000"));
        Assert.Equal(2, editor.History.UndoCount);

        Assert.NotNull(editor.History.Undo());
        AssertFirstSection(document, live, "12240", "bx");
        Assert.NotNull(editor.History.Undo());
        AssertFirstSection(document, live, "12240", "b");
        Assert.NotNull(editor.History.Redo());
        AssertFirstSection(document, live, "12240", "bx");
        Assert.NotNull(editor.History.Redo());
        AssertFirstSection(document, live, "5000", "bx");
        Assert.Equal(2, editor.History.UndoCount);
    }

    [Fact]
    public void ABlockStepWithASwapOfAnotherCarrierKeepsBothThroughUndoAndRedo()
    {
        // Step 1 swaps the second section; step 2 swaps the first section together with its carrier paragraph. The undo of step 2 replaces the first carrier by a clone;
        // step 1 must still find the second section afterwards, and nothing is skipped.
        var document = Three();
        var history = new EditHistory(document, new FakeTime(), HistoryLimits.Default);
        var sections = SectionList.Of(document);
        var (first, second) = (sections[0].Properties, sections[1].Properties);
        Do(document, history, () => Widen(document, [second], "6000"));
        var carrier = (Paragraph)document.Body.Blocks[1];
        var revision = history.BeginOperation();
        var old = new Block[] { carrier.Clone() };
        var change = Widen(document, [first], "5000");
        carrier.Inlines.Clear();
        document.Edits.Raise();
        history.Record(new EditResult(new TextPosition(StoryRef.Body, carrier, 0), [new BlockChange(StoryRef.Body, document.Body.Blocks, 1, 1, 1, old)], Sections: change), new EditHistory.Point(1, 0), revision, null);

        for (var round = 0; round < 2; round++)
        {
            Assert.NotNull(history.Undo());
            Assert.Equal("12240", Width(SectionList.Of(document)[0].Properties));
            Assert.Equal("6000", Width(SectionList.Of(document)[1].Properties));
            Assert.NotNull(history.Undo());
            Assert.Equal("12240", Width(SectionList.Of(document)[1].Properties));
            Assert.NotNull(history.Redo());
            Assert.NotNull(history.Redo());
            Assert.Equal("5000", Width(SectionList.Of(document)[0].Properties));
            Assert.Equal("6000", Width(SectionList.Of(document)[1].Properties));
            Assert.Same(first, SectionList.Of(document)[0].Properties);
            Assert.Same(second, SectionList.Of(document)[1].Properties);
        }
    }

    [Fact]
    public void AStepWhoseSectionIsGoneIsRefusedWholeAndTheHistoryClears()
    {
        // A block step that removes a section break detaches the object an earlier section step names (the undo of the removal restores a break the swap cannot pair):
        // undoing the section step is then refused before anything changes.
        var document = Three();
        var history = new EditHistory(document, new FakeTime(), HistoryLimits.Default);
        var live = SectionList.Of(document)[0].Properties;
        var last = SectionList.Of(document)[^1].Properties;
        Do(document, history, () => Widen(document, [live, last], "5000"));
        var carrier = (Paragraph)document.Body.Blocks[1];
        var revision = history.BeginOperation();
        var snapshot = carrier.Clone();
        var plain = new Paragraph();
        foreach (var inline in carrier.Inlines)
        {
            plain.Inlines.Add(inline.Clone());
        }

        document.Body.Blocks[1] = plain;
        document.Edits.Raise();
        history.Record(new EditResult(new TextPosition(StoryRef.Body, plain, 0), [new BlockChange(StoryRef.Body, document.Body.Blocks, 1, 1, 1, [snapshot])]), new EditHistory.Point(1, 0), revision, null);

        Assert.NotNull(history.Undo());
        Assert.Equal("5000", Width(SectionList.Of(document)[0].Properties));
        Assert.Equal("5000", Width(last));

        Assert.Null(history.Undo());
        Assert.False(history.CanUndo);
        Assert.False(history.CanRedo);
        Assert.Equal("5000", Width(SectionList.Of(document)[0].Properties));
        Assert.Equal("5000", Width(last));
    }

    [Fact]
    public void ARedoThatNeedsTheSettingsPartAndFindsNoneChangesNothing()
    {
        var document = Three();
        var history = new EditHistory(document, new FakeTime(), HistoryLimits.Default);
        var final = SectionList.Of(document)[^1].Properties;
        Do(document, history, () => Widen(document, [final], "5000", flagAfter: true));
        Assert.NotNull(history.Undo());
        Assert.Equal("12240", Width(final));
        document.Settings = null;

        Assert.Null(history.Redo());

        Assert.Equal("12240", Width(final));
        Assert.Null(document.Settings);
        Assert.False(history.CanRedo);
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void HistoryBytesAreTheExactSumOfTheSnapshots()
    {
        var document = Three();
        var history = new EditHistory(document, new FakeTime(), HistoryLimits.Default);
        var sections = SectionList.Of(document).Select(s => s.Properties).ToList();
        sections[1].HeaderFooterReferences.Add(new HeaderFooterReference(HeaderFooterKind.Header, HeaderFooterType.Default, "rId9"));
        SectionChange? made = null;
        Do(document, history, () => made = Widen(document, sections, "5000"));

        long expected = 0;
        foreach (var swap in made!.Swaps)
        {
            expected += EditHistory.SizeOf(swap.Old) + EditHistory.SizeOf(swap.New);
        }

        Assert.Equal(expected, history.UndoBytes);
        Assert.Equal(expected, EditHistory.SizeOf(made));

        // A header or footer reference counts 64.
        var bare = sections[1].Clone();
        bare.HeaderFooterReferences.Clear();
        Assert.Equal(64, EditHistory.SizeOf(sections[1]) - EditHistory.SizeOf(bare));
        Assert.True(EditHistory.SizeOf(bare) > 0);
    }

    [Fact]
    public void AnEditBehindTheHistorysBackClearsIt()
    {
        var document = Three();
        var history = new EditHistory(document, new FakeTime(), HistoryLimits.Default);
        var final = SectionList.Of(document)[^1].Properties;
        Do(document, history, () => Widen(document, [final], "5000"));
        Assert.True(history.CanUndo);

        document.Edits.Raise();

        Assert.False(history.CanUndo);
        Assert.Null(history.Undo());
        Assert.Equal("5000", Width(final));
    }
}
