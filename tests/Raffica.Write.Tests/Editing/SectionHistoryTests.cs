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

    [Fact]
    public void HistoryBytesCountEverySnapshot()
    {
        static long Bytes(int sections)
        {
            var document = Three();
            var history = new EditHistory(document, new FakeTime(), HistoryLimits.Default);
            var chosen = SectionList.Of(document).Select(s => s.Properties).Take(sections).ToList();
            Do(document, history, () => Widen(document, chosen, "5000"));
            return history.UndoBytes;
        }

        var one = Bytes(1);
        var three = Bytes(3);
        Assert.True(one > 0);
        Assert.True(three > one * 2, $"{three} against {one}");
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
