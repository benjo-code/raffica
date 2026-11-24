using Raffica.Shell.Frame;
using Raffica.Write;
using Raffica.Write.Commands;
using Xunit;

namespace Raffica.WriteApp.Tests.Commands;

/// <summary>The Insert tab (milestone 4.5a, Task 3): its key tip, its groups, its field codes and the width of the Home band, which it must not touch.</summary>
public sealed class ObjectRibbonTests
{
    [Fact]
    public void TheInsertTabHasTheKeyTipIAndTheNotesGroup()
    {
        var insert = WriteCommandCatalog.Tabs.Single(t => t.Letter == WriteRibbon.InsertLetter);
        Assert.Equal("I", insert.Letter);
        Assert.Equal("TabInsert", insert.NameKey);
        Assert.Equal(["GroupNotes"], insert.Groups.Select(g => g.LabelKey).ToArray());
        Assert.Equal(["note.footnote", "note.endnote", "note.back"], insert.Groups[0].Commands.Select(c => c.Id).ToArray());

        // The table and picture groups of the later tasks come before the notes: the digit of the notes is the last of three.
        Assert.Equal("3", insert.Groups[0].Digit);

        // The tabs are Home, Insert, Layout, View in this order, with unique key tips; the field codes of the tab are unique and none begins another.
        Assert.Equal(["H", "I", "P", "V"], WriteCommandCatalog.Tabs.Select(t => t.Letter).ToArray());
        var codes = insert.Groups.SelectMany(g => g.Commands.Select(c => c.FieldCode)).ToList();
        Assert.Equal(codes.Count, codes.Distinct().Count());
        Assert.All(codes, a => Assert.DoesNotContain(codes, b => a != b && b.StartsWith(a, StringComparison.Ordinal)));
        Assert.All(insert.Groups.SelectMany(g => g.Commands), c => Assert.StartsWith("3", c.FieldCode, StringComparison.Ordinal));
        WriteRibbon.Create(new Dictionary<string, Action>());
    }

    [Fact]
    public void TheHomeBandHintIsUnchanged()
    {
        Assert.Equal(2500, WriteRibbon.HomeFullWidthAtLeast);
        var definition = WriteRibbon.Create(new Dictionary<string, Action>());
        var tabs = definition.Tabs.ToDictionary(t => t.Letter, t => t.FullWidthAtLeast);
        Assert.Equal(2500, tabs[WriteRibbon.HomeLetter]);
        Assert.Equal(0, tabs[WriteRibbon.InsertLetter]);
        Assert.Equal(0, tabs[WriteRibbon.LayoutLetter]);
        Assert.Equal(0, tabs[WriteRibbon.ViewLetter]);
    }
}
