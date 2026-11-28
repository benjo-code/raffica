using Raffica.Write.Model;
using Xunit;

namespace Raffica.Write.Tests.Model;

public sealed class DerivedVersionTests
{
    private static BlockChange Change(IList<Block> container, int first, int removed, int added) =>
        new(StoryRef.Body, container, first, removed, added, []);

    [Fact]
    public void RaiseDerivedLeavesRevisionAndDirtyAlone()
    {
        var edits = new DocumentEdits();
        edits.Raise();
        edits.MarkSaved();
        var before = edits.Stamp;

        edits.RaiseDerived();

        Assert.Equal(before.Revision, edits.Revision);
        Assert.False(edits.IsDirty);
        Assert.Equal(before.DerivedVersion + 1, edits.DerivedVersion);
        Assert.NotEqual(before, edits.Stamp);
    }

    [Fact]
    public void RestoreNeverLowersDerivedVersion()
    {
        var edits = new DocumentEdits();
        edits.Raise();
        edits.RaiseDerived();
        edits.RaiseDerived();

        edits.Restore(0);

        Assert.Equal(2, edits.DerivedVersion);
        Assert.Equal(new EditStamp(0, 2), edits.Stamp);
    }

    [Fact]
    public void AttributeDerivedMarksBlocksDirtyWithoutIncompleteness()
    {
        List<Block> body = [new Paragraph(), new Paragraph(), new Paragraph()];
        var edits = new DocumentEdits();
        var tracker = edits.Track(body);

        edits.RaiseDerived();
        edits.AttributeDerived([Change(body, 1, 1, 1)]);
        var taken = tracker.Take();

        Assert.True(taken.Complete);
        Assert.Single(taken.Dirty);
        Assert.Contains(body[1], taken.Dirty);
    }

    [Fact]
    public void AttributeDerivedAndARaiseWithoutAttributionStillReportsIncomplete()
    {
        List<Block> body = [new Paragraph(), new Paragraph()];
        var edits = new DocumentEdits();
        var tracker = edits.Track(body);

        edits.Raise();
        edits.AttributeDerived([Change(body, 0, 1, 1)]);

        Assert.False(tracker.Take().Complete);
    }
}
