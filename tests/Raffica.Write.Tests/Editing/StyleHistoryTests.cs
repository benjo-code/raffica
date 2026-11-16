using System.Xml.Linq;
using Raffica.Write.Editing;
using Raffica.Write.Model;
using Xunit;

namespace Raffica.Write.Tests.Editing;

public sealed class StyleHistoryTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private sealed class FakeTime : TimeProvider
    {
        private long _ticks;

        public override long TimestampFrequency => 1000;

        public override long GetTimestamp() => _ticks;

        public void Advance(int milliseconds) => _ticks += milliseconds;
    }

    private static Run R(string text)
    {
        var run = new Run();
        run.Content.Add(new TextContent(text));
        return run;
    }

    private static Style S(string id) => new() { Id = id, Type = "paragraph", Name = id };

    private static (WordDocument Document, EditHistory History, Paragraph Paragraph) Setup(HistoryLimits? limits = null)
    {
        var story = new Story(new OoxmlRoot(W + "document", []));
        var paragraph = new Paragraph();
        paragraph.Properties.SetFlag(W + "keepNext", true);
        paragraph.Inlines.Add(R("abc"));
        story.Blocks.Add(paragraph);
        var document = new WordDocument(story) { Styles = new StyleSheet(new OoxmlRoot(W + "styles", [])) };
        document.Styles.Styles.Add(S("A"));
        document.Styles.Styles.Add(S("B"));
        return (document, new EditHistory(document, new FakeTime(), limits ?? HistoryLimits.Default), paragraph);
    }

    // What UpdateStyleToMatch does, by hand: the style gains a jc, the paragraph loses its keepNext, and the step is recorded with the snapshots.
    private static void Step(WordDocument document, EditHistory history, string styleId = "A")
    {
        var sheet = document.Styles!;
        var original = sheet.Find(styleId)!;
        var paragraph = (Paragraph)document.Body.Blocks[0];
        var revision = history.BeginOperation();
        var old = new Block[] { paragraph.Clone() };
        var updated = original.Clone();
        updated.ParagraphProperties.Set(W + "jc", new XElement(W + "jc", new XAttribute(W + "val", "center")));
        paragraph.Properties.SetFlag(W + "keepNext", false);
        sheet.Replace(original, updated);
        document.Edits.Raise();
        var result = new EditResult(
            new TextPosition(StoryRef.Body, paragraph, 1),
            [new BlockChange(StoryRef.Body, document.Body.Blocks, 0, 1, 1, old)],
            Style: new StyleChange(original.Clone(), updated.Clone()));
        history.Record(result, new EditHistory.Point(0, 1), revision, null);
    }

    private static string? Jc(WordDocument document, string id) => (string?)document.Styles!.Find(id)!.ParagraphProperties.Get(W + "jc")?.Attribute(W + "val");

    [Fact]
    public void UndoRestoresTheStyleAndTheParagraphTogether()
    {
        var (document, history, _) = Setup();
        Step(document, history);
        var undone = history.Undo();
        Assert.NotNull(undone);
        Assert.NotNull(undone.Style);
        Assert.Null(Jc(document, "A"));
        Assert.NotNull(((Paragraph)document.Body.Blocks[0]).Properties.Get(W + "keepNext"));
        Assert.Equal(["A", "B"], document.Styles!.Styles.Select(s => s.Id));
    }

    [Fact]
    public void RedoRepeatsBoth()
    {
        var (document, history, _) = Setup();
        Step(document, history);
        history.Undo();
        var redone = history.Redo();
        Assert.NotNull(redone?.Style);
        Assert.Equal("center", Jc(document, "A"));
        Assert.Null(((Paragraph)document.Body.Blocks[0]).Properties.Get(W + "keepNext"));
    }

    private static string TextOf(WordDocument document) => ParagraphProjection.Build((Paragraph)document.Body.Blocks[0], ProjectionHost.Plain).Text;

    private static void Type(TextEditor editor, WordDocument document, string text)
    {
        var paragraph = (Paragraph)document.Body.Blocks[0];
        var at = new TextPosition(StoryRef.Body, paragraph, ParagraphProjection.Build(paragraph, ProjectionHost.Plain).Text.Length);
        editor.InsertText(new TextRange(at, at), text);
    }

    private static (WordDocument Document, TextEditor Editor) TypingSetup()
    {
        var story = new Story(new OoxmlRoot(W + "document", []));
        var paragraph = new Paragraph();
        paragraph.Properties.StyleId = "A";
        paragraph.Properties.Set(W + "jc", new XElement(W + "jc", new XAttribute(W + "val", "center")));
        paragraph.Inlines.Add(R("abc"));
        story.Blocks.Add(paragraph);
        var document = new WordDocument(story) { Styles = new StyleSheet(new OoxmlRoot(W + "styles", [])) };
        document.Styles.Styles.Add(S("A"));
        var editor = new TextEditor(document, (_, _) => ProjectionHost.Plain, new FakeTime());
        Type(editor, document, "x");
        var paragraphAfter = (Paragraph)document.Body.Blocks[0];
        var whole = new TextRange(new TextPosition(StoryRef.Body, paragraphAfter, 0), new TextPosition(StoryRef.Body, paragraphAfter, 4));
        Assert.Equal(1, editor.History.UndoCount);
        Assert.NotNull(editor.UpdateStyleToMatch(whole));
        Assert.Equal(2, editor.History.UndoCount);
        return (document, editor);
    }

    [Fact]
    public void AStyleStepEndsTypingCoalescing()
    {
        // Typing right after the style step (same word, same instant) is a step of its own: undoing it leaves the style step and the "x".
        var (document, editor) = TypingSetup();
        Type(editor, document, "y");
        Assert.Equal(3, editor.History.UndoCount);
        editor.History.Undo();
        Assert.Equal("abcx", TextOf(document));
        Assert.Equal("center", Jc(document, "A"));
    }

    [Fact]
    public void TypingAfterUndoingAStyleStepDoesNotJoinTheEarlierTypingStep()
    {
        // Undoing the style step restores the revision and the caret the "x" step ended at, so only a stale open step could let the next typing join it.
        var (document, editor) = TypingSetup();
        editor.History.Undo();
        Assert.Null(Jc(document, "A"));
        Type(editor, document, "z");
        Assert.Equal(2, editor.History.UndoCount);
        editor.History.Undo();
        Assert.Equal("abcx", TextOf(document));
        Assert.Equal(1, editor.History.UndoCount);
    }

    [Fact]
    public void UndoToTheSavedRevisionIsCleanAgain()
    {
        var (document, history, _) = Setup();
        document.Edits.MarkSaved();
        Step(document, history);
        Assert.True(document.Edits.IsDirty);
        history.Undo();
        Assert.False(document.Edits.IsDirty);
        history.Redo();
        Assert.True(document.Edits.IsDirty);
    }

    [Fact]
    public void ADocumentChangedBehindTheHistoryClearsIt()
    {
        var (document, history, _) = Setup();
        Step(document, history);
        document.Edits.Raise();
        Assert.False(history.CanUndo);
        Assert.Null(history.Undo());
        Assert.Equal("center", Jc(document, "A"));
    }

    [Fact]
    public void ANewEditAfterUndoDropsTheRedo()
    {
        var (document, history, _) = Setup();
        Step(document, history);
        history.Undo();
        Assert.True(history.CanRedo);
        Step(document, history, "B");
        Assert.False(history.CanRedo);
        Assert.Equal(1, history.UndoCount);
        history.Undo();
        Assert.Null(Jc(document, "B"));
    }

    [Fact]
    public void AStyleStepWithoutBlockChangesIsRecordedAndUndone()
    {
        var (document, history, paragraph) = Setup();
        var sheet = document.Styles!;
        var original = sheet.Find("A")!;
        var revision = history.BeginOperation();
        var updated = original.Clone();
        updated.ParagraphProperties.Set(W + "jc", new XElement(W + "jc", new XAttribute(W + "val", "right")));
        sheet.Replace(original, updated);
        document.Edits.Raise();
        history.Record(new EditResult(new TextPosition(StoryRef.Body, paragraph, 0), [], Style: new StyleChange(original.Clone(), updated.Clone())), new EditHistory.Point(0, 0), revision, null);
        Assert.Equal(1, history.UndoCount);
        var undone = history.Undo();
        Assert.NotNull(undone);
        Assert.Empty(undone.Changes);
        Assert.Null(Jc(document, "A"));
    }

    [Fact]
    public void TheStyleCountsTowardsTheByteLimit()
    {
        var (document, history, _) = Setup();
        Step(document, history);
        var withStyle = history.UndoBytes;

        var (plainDocument, plainHistory, _) = Setup();
        var paragraph = (Paragraph)plainDocument.Body.Blocks[0];
        var revision = plainHistory.BeginOperation();
        var old = new Block[] { paragraph.Clone() };
        paragraph.Properties.SetFlag(W + "keepNext", false);
        plainDocument.Edits.Raise();
        plainHistory.Record(
            new EditResult(new TextPosition(StoryRef.Body, paragraph, 1), [new BlockChange(StoryRef.Body, plainDocument.Body.Blocks, 0, 1, 1, old)]),
            new EditHistory.Point(0, 1),
            revision,
            null);
        Assert.True(withStyle > plainHistory.UndoBytes + 100);

        var (limited, limitedHistory, _) = Setup(new HistoryLimits(100, withStyle + 1));
        Step(limited, limitedHistory, "A");
        Step(limited, limitedHistory, "B");
        Assert.Equal(1, limitedHistory.UndoCount);
    }
}
