using Raffica.Write.Editing;
using Raffica.Write.Model;
using Raffica.Write.Tests.Support;
using Xunit;
using static Raffica.Write.Tests.Support.TocDocument;

namespace Raffica.Write.Tests.Editing;

/// <summary>Every way into a table of contents is refused with its reason and leaves the document as it was (compared as a full dump, not as text); a whole field still moves and goes as one.</summary>
public sealed class TocGuardTests
{
    private static readonly byte[] s_png = TestPictures.Png(4, 4);

    // Intro, Heading, three entries (indices 2 to 4), Middle, After.
    private sealed record Fixture(WordDocument Document, TextEditor Editor, Paragraph Intro, Paragraph One, Paragraph Two, Paragraph Three, Paragraph After, int FirstIndex, int LastIndex);

    private static Fixture Make()
    {
        var entries = Toc(nested: true, "One", "Two", "Three");
        var intro = Plain("Intro");
        var after = Plain("After");
        var document = Document([intro, Plain("Heading"), .. entries, Plain("Middle"), after]);
        var editor = new TextEditor(document, (_, _) => ProjectionHost.Plain);
        return new Fixture(document, editor, intro, entries[0], entries[1], entries[2], after, 2, 4);
    }

    private static TextPosition At(Paragraph paragraph, int offset) => new(StoryRef.Body, paragraph, offset);

    private static TextRange Caret(Paragraph paragraph, int offset) => new(At(paragraph, offset), At(paragraph, offset));

    private static TextRange Sel(Paragraph first, int start, Paragraph second, int end) => new(At(first, start), At(second, end));

    private static void AssertRefused(Fixture f, string dump, int revision, object? result, FieldRefusalKind? kind = null)
    {
        Assert.True(result is null || result is EditResult { Changes.Count: 0, Selected: null }, "the edit changed something");
        Assert.NotNull(f.Editor.LastFieldRefusal);
        if (kind is { } expected)
        {
            Assert.Equal(expected, f.Editor.LastFieldRefusal!.Kind);
        }

        Assert.Equal(dump, Dump(f.Document));
        Assert.Equal(revision, f.Document.Edits.Revision);
        Assert.False(f.Editor.History.CanUndo);
    }

    [Fact]
    public void PicturesAreRefusedInsideAndOverAPartOfAToc()
    {
        var f = Make();
        var dump = Dump(f.Document);
        var revision = f.Document.Edits.Revision;

        AssertRefused(f, dump, revision, f.Editor.InsertPicture(At(f.Two, 1), s_png, null), FieldRefusalKind.InsideToc);
        AssertRefused(f, dump, revision, f.Editor.InsertPicture(At(f.One, 0), s_png, null), FieldRefusalKind.InsideToc);
        AssertRefused(f, dump, revision, f.Editor.PastePicture(Caret(f.Two, 1), s_png), FieldRefusalKind.InsideToc);

        // Over a selection from the middle of the field to the paragraph after it: the end of the field would be deleted.
        AssertRefused(f, dump, revision, f.Editor.PastePicture(Sel(f.Two, 1, f.After, 2), s_png), FieldRefusalKind.PartOfToc);
        AssertRefused(f, dump, revision, f.Editor.PastePicture(Sel(f.Intro, 2, f.Three, 2), s_png), FieldRefusalKind.PartOfToc);
        Assert.Empty(f.Document.Media.Entries);
    }

    [Fact]
    public void ANoteATableABookmarkAndASectionBreakAreRefusedInsideAToc()
    {
        var f = Make();
        var dump = Dump(f.Document);
        var revision = f.Document.Edits.Revision;

        AssertRefused(f, dump, revision, f.Editor.InsertNote(At(f.Two, 1), NoteType.Footnote), FieldRefusalKind.InsideToc);
        AssertRefused(f, dump, revision, f.Editor.InsertNote(At(f.Three, 5), NoteType.Endnote), FieldRefusalKind.InsideToc);
        AssertRefused(f, dump, revision, f.Editor.InsertTable(At(f.Two, 1), 2, 2), FieldRefusalKind.InsideToc);
        AssertRefused(f, dump, revision, f.Editor.AddBookmark(Sel(f.Two, 0, f.Two, 2), "Mine"), FieldRefusalKind.PartOfToc);
        AssertRefused(f, dump, revision, f.Editor.AddBookmark(Sel(f.Intro, 1, f.Two, 1), "Mine"), FieldRefusalKind.PartOfToc);
        AssertRefused(f, dump, revision, f.Editor.AddBookmark(Caret(f.Two, 1), "Mine"), FieldRefusalKind.InsideToc);
        AssertRefused(f, dump, revision, f.Editor.InsertSectionBreak(At(f.Two, 1), SectionBreakKind.NextPage), FieldRefusalKind.InsideToc);
        AssertRefused(f, dump, revision, f.Editor.InsertSectionBreak(At(f.Three, 1), SectionBreakKind.NextPage), FieldRefusalKind.InsideToc);
        AssertRefused(f, dump, revision, f.Editor.NumberPagesFrom(At(f.Two, 1), new PageNumbering(1, PageNumberFormat.Arabic, string.Empty)), FieldRefusalKind.InsideToc);
        Assert.Null(f.Document.Footnotes);
        Assert.Null(f.Document.Endnotes);
    }

    [Fact]
    public void ListsAndOutlineLevelsAreRefusedOverAToc()
    {
        var f = Make();
        var dump = Dump(f.Document);
        var revision = f.Document.Edits.Revision;

        AssertRefused(f, dump, revision, f.Editor.ToggleList(Sel(f.One, 0, f.Three, 3), ListKind.Bullet), FieldRefusalKind.PartOfToc);
        AssertRefused(f, dump, revision, f.Editor.ChangeListLevel(Sel(f.Two, 0, f.Two, 1), 1), FieldRefusalKind.PartOfToc);
        AssertRefused(f, dump, revision, f.Editor.RestartNumbering(At(f.Two, 1)), FieldRefusalKind.InsideToc);
        AssertRefused(f, dump, revision, f.Editor.ContinueNumbering(At(f.Two, 1)), FieldRefusalKind.InsideToc);
        AssertRefused(f, dump, revision, f.Editor.ShiftOutlineLevels(1, 3, 1), FieldRefusalKind.PartOfToc);
    }

    [Fact]
    public void AListCommandOverMoreThanTheTocLeavesTheTocAloneAndIsRefusedWhenThereIsNothingElse()
    {
        var f = Make();
        var tocDump = DumpOf(f, 2, 4);

        // The paragraphs outside the field get the list; the field keeps its paragraphs as they were (its entries are written by the refresh).
        var result = f.Editor.ToggleList(Sel(f.Intro, 0, f.Three, 3), ListKind.Numbered);
        Assert.NotEmpty(result.Changes);
        Assert.Null(f.Editor.LastFieldRefusal);
        Assert.NotNull(f.Intro.Properties.NumberingId);
        Assert.Equal(tocDump, DumpOf(f, 2, 4));
        Assert.NotNull(f.Editor.History.Undo());
        Assert.Null(((Paragraph)f.Document.Body.Blocks[0]).Properties.NumberingId);
    }

    private static string DumpOf(Fixture f, int first, int last)
    {
        var all = Dump(f.Document).Split((char)10);
        return string.Join((char)10, all.Skip(first).Take(last - first + 1));
    }

    [Fact]
    public void AMoveNeverSeparatesOrEntersAToc()
    {
        var f = Make();
        var dump = Dump(f.Document);
        var revision = f.Document.Edits.Revision;

        // Part of the field moves: refused. A block lands inside the field: refused.
        AssertRefused(f, dump, revision, f.Editor.MoveBlocks(3, 1, 0), FieldRefusalKind.PartOfToc);
        AssertRefused(f, dump, revision, f.Editor.MoveBlocks(2, 2, 6), FieldRefusalKind.PartOfToc);
        AssertRefused(f, dump, revision, f.Editor.MoveBlocks(1, 3, 6), FieldRefusalKind.PartOfToc);
        AssertRefused(f, dump, revision, f.Editor.MoveBlocks(0, 1, 3), FieldRefusalKind.PartOfToc);
        AssertRefused(f, dump, revision, f.Editor.MoveBlocks(6, 1, 4), FieldRefusalKind.PartOfToc);

        // The whole field moves, alone or with its neighbours, and a block lands before or after it.
        Assert.NotNull(f.Editor.MoveBlocks(2, 3, 6));
        Assert.Null(f.Editor.LastFieldRefusal);
        Assert.Equal(["Intro", "Heading", "Middle", "One1", "Two2", "Three3", "After"], f.Document.Body.Blocks.OfType<Paragraph>().Select(p => ParagraphProjection.Build(p, ProjectionHost.Plain).Text).ToArray());
        Assert.Equal(3, f.Editor.TocRangesOf(f.Document.Body.Blocks)![0].First);
        Assert.NotNull(f.Editor.MoveBlocks(0, 1, 6));
    }

    [Fact]
    public void ARangeCoveringTheWholeTocDeletesItAndUndoRestoresTheModelExactly()
    {
        var f = Make();
        var dump = Dump(f.Document);

        var result = f.Editor.Delete(Sel(f.One, 0, f.Three, 6));
        Assert.Single(result.Changes);
        Assert.DoesNotContain("f:", Dump(f.Document));
        Assert.NotNull(f.Editor.History.Undo());
        Assert.Equal(dump, Dump(f.Document));
        Assert.NotNull(f.Editor.History.Redo());
        Assert.DoesNotContain("f:", Dump(f.Document));
        Assert.NotNull(f.Editor.History.Undo());
        Assert.Equal(dump, Dump(f.Document));

        // Replacing the whole field by typing is the same delete and an insert: one step.
        var blocks = f.Document.Body.Blocks;
        var replaced = f.Editor.InsertText(Sel((Paragraph)blocks[2], 0, (Paragraph)blocks[4], 6), "x");
        Assert.Single(replaced.Changes);
        Assert.NotNull(f.Editor.History.Undo());
        Assert.Equal(dump, Dump(f.Document));
    }

    [Fact]
    public void FormattingAndStylesAreNotRefusedOverAToc()
    {
        var f = Make();

        var styled = f.Editor.ApplyParagraphStyle(Sel(f.One, 0, f.Three, 3), "Heading2");
        Assert.NotEmpty(styled.Changes);
        Assert.Null(f.Editor.LastFieldRefusal);
    }

    [Fact]
    public void ACaretStepsOverTheFieldInTheDirectionOfTheMove()
    {
        var f = Make();
        var end = new TextPosition(StoryRef.Body, f.Three, 5 + 1);

        // From the start edge a step forward lands inside: it goes to the end edge; from the end edge a step back goes to the start edge.
        Assert.Equal(end, f.Editor.SnapCaret(At(f.One, 1), from: At(f.One, 0)));
        Assert.Equal(At(f.One, 0), f.Editor.SnapCaret(At(f.Three, 2), from: end));
        Assert.Equal(end, f.Editor.SnapCaret(At(f.Two, 0), from: At(f.One, 0)));
        Assert.Equal(At(f.One, 0), f.Editor.SnapCaret(At(f.Two, 0), from: At(f.After, 0)));

        // A click has no direction: the nearer edge.
        Assert.Equal(At(f.One, 0), f.Editor.SnapCaret(At(f.One, 2)));
        Assert.Equal(end, f.Editor.SnapCaret(At(f.Three, 1)));
        Assert.Equal(At(f.After, 1), f.Editor.SnapCaret(At(f.After, 1)));
    }
}
