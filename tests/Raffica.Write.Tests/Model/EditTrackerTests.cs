using Raffica.Write.Model;
using Xunit;

namespace Raffica.Write.Tests.Model;

public sealed class EditTrackerTests
{
    private static BlockChange Change(IList<Block> container, int first, int removed, int added, StoryRef? story = null) =>
        new(story ?? StoryRef.Body, container, first, removed, added, []);

    private static List<Block> Body() => [new Paragraph(), new Paragraph(), new Paragraph(), new Paragraph()];

    [Fact]
    public void ARaiseThatWasAttributedIsCompleteAndNamesTheBlocksOfItsRange()
    {
        var body = Body();
        var edits = new DocumentEdits();
        var tracker = edits.Track(body);

        edits.Raise();
        edits.Attribute([Change(body, 1, 1, 2)]);
        var taken = tracker.Take();

        Assert.True(taken.Complete);
        Assert.False(taken.NestedChanged);
        Assert.Equal(2, taken.Dirty.Count);
        Assert.Contains(body[1], taken.Dirty);
        Assert.Contains(body[2], taken.Dirty);
    }

    [Fact]
    public void ARaiseWithoutItsAttributionMakesTheAnswerIncomplete()
    {
        var edits = new DocumentEdits();
        var tracker = edits.Track(Body());

        edits.Raise();

        Assert.False(tracker.Take().Complete);
    }

    [Fact]
    public void ARestoreNeedsItsAttributionToo()
    {
        var body = Body();
        var edits = new DocumentEdits();
        edits.Raise();
        var tracker = edits.Track(body);

        edits.Restore(0);
        Assert.False(tracker.Take().Complete);

        edits.Restore(1);
        edits.Attribute([Change(body, 0, 1, 1)]);
        Assert.True(tracker.Take().Complete);
    }

    [Fact]
    public void TakeStartsOver()
    {
        var body = Body();
        var edits = new DocumentEdits();
        var tracker = edits.Track(body);
        edits.Raise();
        edits.Attribute([Change(body, 0, 1, 1)]);
        tracker.Take();

        var second = tracker.Take();

        Assert.True(second.Complete);
        Assert.Empty(second.Dirty);
    }

    [Fact]
    public void AListInsideABlockIsANestedChangeAndAnotherStoryIsNoChangeOfTheBody()
    {
        var body = Body();
        var cell = new List<Block> { new Paragraph() };
        var note = new List<Block> { new Paragraph() };
        var edits = new DocumentEdits();
        var tracker = edits.Track(body);

        edits.Raise();
        edits.Attribute([Change(note, 0, 1, 1, new StoryRef(StoryKind.Note, "1"))]);
        var other = tracker.Take();
        Assert.True(other.Complete);
        Assert.False(other.NestedChanged);
        Assert.Empty(other.Dirty);

        edits.Raise();
        edits.Attribute([Change(cell, 0, 1, 1)]);
        var nested = tracker.Take();
        Assert.True(nested.NestedChanged);
        Assert.Empty(nested.Dirty);
    }

    [Fact]
    public void ARangeBeyondTheBodyIsClamped()
    {
        var body = Body();
        var edits = new DocumentEdits();
        var tracker = edits.Track(body);

        edits.Raise();
        edits.Attribute([Change(body, 3, 1, 5)]);

        Assert.Single(tracker.Take().Dirty);
    }

    [Fact]
    public void ATrackerIsHeldWeakly()
    {
        var edits = new DocumentEdits();
        WeakReference Make() => new(edits.Track(Body()));
        var weak = Make();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        edits.Raise();

        Assert.False(weak.IsAlive);
    }
}
