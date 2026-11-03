using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Raffica.Shell.Controls;
using Xunit;

namespace Raffica.Shell.Tests.Icons;

public sealed class IconTests
{
    public static TheoryData<string> Names =>
    [
        "Check", "ChevronDown", "ChevronRight", "X", "Search", "Plus", "Minus", "Info", "TriangleAlert",
        "OctagonAlert", "Bold", "Italic", "Underline", "Undo", "Redo", "AlignLeft", "AlignCenter", "AlignRight",
    ];

    [AvaloniaTheory]
    [MemberData(nameof(Names))]
    public void EveryIconKeyParsesToANonEmptyGeometry(string name)
    {
        Assert.True(Application.Current!.TryGetResource("Raffica.Icon." + name, Application.Current!.ActualThemeVariant, out var value));

        var geometry = Assert.IsAssignableFrom<Geometry>(value);
        Assert.False(geometry.Bounds.Width <= 0 && geometry.Bounds.Height <= 0);
        Assert.InRange(geometry.Bounds.Right, 0, 24.001);
        Assert.InRange(geometry.Bounds.Bottom, 0, 24.001);
    }

    [Fact]
    public void TheStrokeKeepsOnePointFiveScreenPixels()
    {
        Assert.Equal(2.25, StrokeIcon.StrokeThicknessFor(16));
        Assert.Equal(1.8, StrokeIcon.StrokeThicknessFor(20));
    }

    [AvaloniaTheory]
    [InlineData(16)]
    [InlineData(20)]
    public void TheTwoGridSizesAreAccepted(int size)
    {
        var icon = new StrokeIcon { Size = size };

        icon.Measure(Size.Infinity);

        Assert.Equal(new Size(size, size), icon.DesiredSize);
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(18)]
    [InlineData(24)]
    public void ASizeOffTheGridThrows(int size)
    {
        var icon = new StrokeIcon();

        Assert.Throws<ArgumentOutOfRangeException>(() => icon.Size = size);
    }
}
