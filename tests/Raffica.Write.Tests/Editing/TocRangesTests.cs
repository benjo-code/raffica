using Raffica.Write.Editing;
using Raffica.Write.Model;
using Raffica.Write.Tests.Support;
using Xunit;
using static Raffica.Write.Tests.Support.TocDocument;

namespace Raffica.Write.Tests.Editing;

/// <summary>Finding the tables of contents of a list of blocks, and keeping the answer between edits only while it is still true.</summary>
public sealed class TocRangesTests
{
    private static Paragraph Orphan()
    {
        var paragraph = new Paragraph();
        paragraph.Inlines.Add(Mark(FieldCharKind.Begin));
        paragraph.Inlines.Add(Code(" SEQ Figure "));
        paragraph.Inlines.Add(Text("never closed"));
        return paragraph;
    }

    private static List<(int First, int Last)> Found(IList<Block> blocks) => (TocRanges.In(blocks) ?? []).Select(r => (r.First, r.Last)).ToList();

    [Fact]
    public void ATocWithLinksAndPageFieldsInEveryEntryIsOneField()
    {
        var document = Document([Plain("a"), .. Toc(nested: true, "One", "Two", "Three"), Plain("b")]);

        Assert.Equal([(1, 3)], Found(document.Body.Blocks));
    }

    [Fact]
    public void ATocInAContentControlIsFoundInThatList()
    {
        var control = new ContentControlBlock();
        control.Content.Add(Plain("Contents"));
        foreach (var entry in Toc(nested: false, "One", "Two"))
        {
            control.Content.Add(entry);
        }

        var document = Document(Plain("a"), control, Plain("b"));
        var editor = new TextEditor(document, (_, _) => ProjectionHost.Plain);

        Assert.Null(TocRanges.In(document.Body.Blocks));
        Assert.Equal([(1, 2)], Found(control.Content));
        var inside = (Paragraph)control.Content[2];
        var result = editor.InsertText(new TextRange(new TextPosition(StoryRef.Body, inside, 1), new TextPosition(StoryRef.Body, inside, 1)), "x");
        Assert.Empty(result.Changes);
        Assert.Equal(FieldRefusalKind.InsideToc, editor.LastFieldRefusal?.Kind);
        Assert.True(FieldView.IsInTableOfContents(control.Content, 2));
        Assert.False(FieldView.IsInTableOfContents(control.Content, 0));
    }

    [Fact]
    public void TwoTocsAreTwoRanges()
    {
        var document = Document([.. Toc(nested: false, "a", "b"), Plain("x"), .. Toc(nested: true, "c", "d", "e")]);

        Assert.Equal([(0, 1), (3, 5)], Found(document.Body.Blocks));
    }

    [Fact]
    public void ATocThatIsNeverClosedIsNoRange()
    {
        var paragraphs = Toc(nested: false, "a", "b", "c");
        paragraphs[2].Inlines.RemoveAt(paragraphs[2].Inlines.Count - 1);
        var document = Document([.. paragraphs, Plain("x")]);

        Assert.Empty(Found(document.Body.Blocks));
    }

    [Fact]
    public void AnOrphanBeginDoesNotDisableTheProtectionOfLaterTocs()
    {
        var document = Document([Plain("a"), Orphan(), .. Toc(nested: false, "c", "d"), Plain("x"), Orphan(), .. Toc(nested: true, "e", "f", "g"), Plain("y")]);

        Assert.Equal([(2, 3), (6, 8)], Found(document.Body.Blocks));
    }

    [Fact]
    public void AnEndWithoutABeginIsIgnored()
    {
        var stray = new Paragraph();
        stray.Inlines.Add(Mark(FieldCharKind.End));
        var document = Document([stray, .. Toc(nested: false, "a", "b")]);

        Assert.Equal([(1, 2)], Found(document.Body.Blocks));
    }

    [Fact]
    public void TypingInADocumentWithoutAFieldBuildsTheAnswerOnce()
    {
        var document = Document(Plain("one"), Plain("two"), Plain("three"));
        var editor = new TextEditor(document, (_, _) => ProjectionHost.Plain);
        var first = (Paragraph)document.Body.Blocks[0];
        var epoch = document.Edits.FieldMarkEpoch;

        for (var i = 0; i < 20; i++)
        {
            var at = new TextPosition(StoryRef.Body, first, i);
            Assert.NotEmpty(editor.InsertText(new TextRange(at, at), "x").Changes);
            Assert.Equal(epoch, document.Edits.FieldMarkEpoch);
        }

        Assert.Null(editor.TocRangesOf(document.Body.Blocks));
        Assert.Equal(epoch, document.Edits.FieldMarkEpoch);
    }

    [Fact]
    public void AnEditThatTouchesAFieldMarkOrShiftsBlocksChangesTheEpoch()
    {
        var entries = Toc(nested: false, "a", "b");
        var document = Document(Plain("one"), entries[0], entries[1], Plain("two"));
        var editor = new TextEditor(document, (_, _) => ProjectionHost.Plain);
        var epoch = document.Edits.FieldMarkEpoch;

        // Enter in a plain paragraph shifts the blocks after it: the range of the field moves.
        var one = (Paragraph)document.Body.Blocks[0];
        Assert.NotEmpty(editor.SplitParagraph(new TextRange(new TextPosition(StoryRef.Body, one, 1), new TextPosition(StoryRef.Body, one, 1))).Changes);
        Assert.NotEqual(epoch, document.Edits.FieldMarkEpoch);
        Assert.Equal([(2, 3)], (editor.TocRangesOf(document.Body.Blocks) ?? []).Select(r => (r.First, r.Last)).ToList());

        // Undo and redo ask again.
        Assert.NotNull(editor.History.Undo());
        Assert.Equal([(1, 2)], (editor.TocRangesOf(document.Body.Blocks) ?? []).Select(r => (r.First, r.Last)).ToList());
        Assert.NotNull(editor.History.Redo());
        Assert.Equal([(2, 3)], (editor.TocRangesOf(document.Body.Blocks) ?? []).Select(r => (r.First, r.Last)).ToList());
    }

    [Fact]
    public void TheKeptAnswerEqualsAFreshSearchAfterRandomEditsUndoAndRedo()
    {
        var random = new Random(4711);
        for (var round = 0; round < 12; round++)
        {
            var blocks = new List<Block> { Plain("start") };
            for (var i = 0; i < 3; i++)
            {
                blocks.AddRange(Toc(nested: random.Next(2) == 0, "e" + i, "f" + i, "g" + i));
                blocks.Add(Plain("between " + i));
            }

            blocks.Insert(random.Next(blocks.Count), Orphan());
            var document = Document([.. blocks]);
            var editor = new TextEditor(document, (_, _) => ProjectionHost.Plain);
            for (var step = 0; step < 60; step++)
            {
                Step(editor, document, random);
                AssertSame(editor, document, $"round {round} step {step}");
            }

            while (editor.History.CanUndo)
            {
                Assert.NotNull(editor.History.Undo());
                AssertSame(editor, document, $"round {round} undo");
            }

            while (editor.History.CanRedo)
            {
                Assert.NotNull(editor.History.Redo());
                AssertSame(editor, document, $"round {round} redo");
            }
        }
    }

    private static void AssertSame(TextEditor editor, WordDocument document, string label)
    {
        var kept = editor.TocRangesOf(document.Body.Blocks) ?? [];
        var fresh = TocRanges.In(document.Body.Blocks) ?? [];
        Assert.True(kept.SequenceEqual(fresh), $"{label}: the kept answer differs from a fresh search");
    }

    private static void Step(TextEditor editor, WordDocument document, Random random)
    {
        var paragraphs = document.Body.Blocks.OfType<Paragraph>().ToList();
        var paragraph = paragraphs[random.Next(paragraphs.Count)];
        var length = ParagraphProjection.Build(paragraph, ProjectionHost.Plain).Text.Length;
        var at = new TextPosition(StoryRef.Body, paragraph, random.Next(length + 1));
        var other = paragraphs[random.Next(paragraphs.Count)];
        var otherAt = new TextPosition(StoryRef.Body, other, random.Next(ParagraphProjection.Build(other, ProjectionHost.Plain).Text.Length + 1));
        switch (random.Next(8))
        {
            case 0:
                editor.InsertText(new TextRange(at, at), "x");
                break;
            case 1:
                editor.SplitParagraph(new TextRange(at, at));
                break;
            case 2:
                editor.Delete(new TextRange(at, otherAt));
                break;
            case 3:
                editor.Backspace(at, DeleteUnit.Cluster);
                break;
            case 4:
                editor.DeleteForward(at, DeleteUnit.Cluster);
                break;
            case 5:
                var count = document.Body.Blocks.Count;
                editor.MoveBlocks(random.Next(count), 1 + random.Next(2), random.Next(count + 1));
                break;
            case 6:
                if (editor.History.CanUndo)
                {
                    editor.History.Undo();
                }

                break;
            default:
                if (editor.History.CanRedo)
                {
                    editor.History.Redo();
                }

                break;
        }
    }
}
