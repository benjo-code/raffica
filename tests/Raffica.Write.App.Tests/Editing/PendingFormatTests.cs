using Raffica.Write.Editing;
using Xunit;

namespace Raffica.WriteApp.Tests.Editing;

/// <summary>The pending format tells the ribbon about a change (a pending-only command raises no edit and no selection change), and only about a real one.</summary>
public sealed class PendingFormatTests
{
    [Fact]
    public void ChangedIsRaisedOnlyForARealChange()
    {
        var pending = new PendingFormat();
        var raised = 0;
        pending.Changed += () => raised++;

        pending.Clear();
        Assert.Equal(0, raised);

        pending.Set(FormatChanges.Toggle(CharacterToggle.Bold, on: true));
        Assert.Equal(1, raised);

        // The same change again (an equal element) is no change.
        pending.Set(FormatChanges.Toggle(CharacterToggle.Bold, on: true));
        Assert.Equal(1, raised);

        pending.Set(FormatChanges.Merge(pending.Changes, FormatChanges.Size(14)));
        Assert.Equal(2, raised);

        pending.Set(FormatChanges.Toggle(CharacterToggle.Bold, on: false));
        Assert.Equal(3, raised);

        pending.Clear();
        Assert.Equal(4, raised);
        Assert.Empty(pending.Changes);
        pending.Clear();
        Assert.Equal(4, raised);
    }
}
