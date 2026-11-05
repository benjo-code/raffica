using Raffica.Core.Annotations;
using Raffica.Core.Settings;
using Raffica.Pdf.Annotating;
using Xunit;

namespace Raffica.Pdf.Tests.Annotating;

public sealed class ToolStateTests
{
    private sealed class Store
    {
        public AppSettings Value { get; set; } = AppSettings.Default;

        public ToolState Create() => new(() => Value, value => Value = value);
    }

    [Fact]
    public void AColorOrWidthThatIsKeptRaisesStyleChangedAndOneThatIsRefusedDoesNot()
    {
        var state = new Store().Create();
        var style = 0;
        var tool = 0;
        state.StyleChanged += () => style++;
        state.Changed += () => tool++;

        state.SetColor(AnnotateTool.Freehand, DrawPalette.ColorOf(DrawColor.Blue));
        state.SetWidth(AnnotateTool.Freehand, StrokeWidth.Thick);
        state.SetColor(AnnotateTool.Highlight, AnnotationPalette.ColorOf(MarkupColor.Green));
        Assert.Equal(3, style);

        // A colour that is not in the palette of the tool, and a width of a tool that has none, change nothing.
        state.SetColor(AnnotateTool.Freehand, new AnnotationColor(1, 2, 3));
        state.SetWidth(AnnotateTool.Highlight, StrokeWidth.Thick);
        Assert.Equal(3, style);
        Assert.Equal(0, tool);
    }

    [Fact]
    public void ChoosingTheActiveToolEndsTheMode()
    {
        var state = new Store().Create();
        var changes = 0;
        state.Changed += () => changes++;

        Assert.Equal(AnnotateTool.None, state.Tool);
        Assert.True(state.Choose(AnnotateTool.Highlight));
        Assert.Equal(AnnotateTool.Highlight, state.Tool);
        Assert.True(state.Choose(AnnotateTool.Rectangle));
        Assert.Equal(AnnotateTool.Rectangle, state.Tool);
        Assert.False(state.Choose(AnnotateTool.Rectangle));
        Assert.Equal(AnnotateTool.None, state.Tool);
        Assert.Equal(3, changes);

        state.Choose(AnnotateTool.Line);
        state.End();
        Assert.Equal(AnnotateTool.None, state.Tool);
        var before = changes;
        state.End();
        Assert.Equal(before, changes);
    }

    [Fact]
    public void ColourAndWidthAreRememberedPerTool()
    {
        var store = new Store();
        var state = store.Create();

        state.SetColor(AnnotateTool.Rectangle, DrawPalette.ColorOf(DrawColor.Blue));
        state.SetWidth(AnnotateTool.Rectangle, StrokeWidth.Thick);
        state.SetColor(AnnotateTool.Highlight, AnnotationPalette.ColorOf(MarkupColor.Green));

        Assert.Equal(new ToolStyle(DrawColor.Blue, StrokeWidth.Thick), state.StyleOf(AnnotateTool.Rectangle));
        Assert.Equal(AppSettings.DefaultToolStyle, state.StyleOf(AnnotateTool.Ellipse));
        Assert.Equal(AnnotationPalette.ColorOf(MarkupColor.Green), state.MarkupColor(AnnotateTool.Highlight));
        Assert.Equal(MarkupColor.Green, store.Value.HighlightColor);
        Assert.Equal(MarkupColor.Blue, store.Value.UnderlineColor);
        Assert.Equal(new ToolStyle(DrawColor.Blue, StrokeWidth.Thick), new ToolState(() => store.Value, _ => { }).StyleOf(AnnotateTool.Rectangle));
    }

    [Fact]
    public void MarkupDefaultsAreYellowBluePink()
    {
        var state = new Store().Create();
        Assert.Equal(new AnnotationColor(255, 235, 59), state.MarkupColor(AnnotateTool.Highlight));
        Assert.Equal(new AnnotationColor(124, 196, 250), state.MarkupColor(AnnotateTool.Underline));
        Assert.Equal(new AnnotationColor(255, 143, 199), state.MarkupColor(AnnotateTool.StrikeOut));
    }

    [Fact]
    public void DrawingDefaultIsRedMedium()
    {
        var state = new Store().Create();
        foreach (var tool in new[] { AnnotateTool.Freehand, AnnotateTool.Rectangle, AnnotateTool.Ellipse, AnnotateTool.Line, AnnotateTool.Arrow })
        {
            var style = state.StyleOf(tool);
            Assert.Equal(new AnnotationColor(220, 30, 30), DrawPalette.ColorOf(style.Color));
            Assert.Equal(2, StrokeWidths.PointsOf(style.Width));
        }
    }

    [Fact]
    public void TextBoxUsesFontSizesInPlaceOfWidths()
    {
        var state = new Store().Create();
        Assert.Equal(16, TextBoxSpec.FontSizeOf(state.StyleOf(AnnotateTool.TextBox).Width));
        state.SetWidth(AnnotateTool.TextBox, StrokeWidth.Thin);
        Assert.Equal(12, TextBoxSpec.FontSizeOf(state.StyleOf(AnnotateTool.TextBox).Width));
        state.SetWidth(AnnotateTool.TextBox, StrokeWidth.Thick);
        Assert.Equal(24, TextBoxSpec.FontSizeOf(state.StyleOf(AnnotateTool.TextBox).Width));
    }

    [Fact]
    public void BlankAuthorIsNull()
    {
        var store = new Store();
        var state = store.Create();
        Assert.Null(state.Author);
        store.Value = store.Value with { AuthorName = "   " };
        Assert.Null(state.Author);
        store.Value = store.Value with { AuthorName = "Ada" };
        Assert.Equal("Ada", state.Author);
    }
}
