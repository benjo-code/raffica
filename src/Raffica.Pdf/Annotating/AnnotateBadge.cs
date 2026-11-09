using Avalonia.Controls;
using Avalonia.Layout;

namespace Raffica.Pdf.Annotating;

/// <summary>
/// What hangs over the top middle of the page area while the annotate mode is on: the sign that names the tool (<see cref="ModeIndicator"/>,
/// no pointer input) and under it the <see cref="StyleStrip"/> with the colour and width choice. One per tab; it is the
/// <c>Badge</c> of the page view, so the view knows that key and pointer events from inside it are not the tool's.
/// </summary>
internal sealed class AnnotateBadge : StackPanel, IDisposable
{
    public AnnotateBadge(ToolState tools, SelectController select)
    {
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(select);
        Orientation = Orientation.Vertical;
        Spacing = 4;
        Indicator = new ModeIndicator(tools) { HorizontalAlignment = HorizontalAlignment.Center };
        Strip = new StyleStrip(tools, select);
        Children.Add(Indicator);
        Children.Add(Strip);
    }

    /// <summary>The sign of the mode.</summary>
    public ModeIndicator Indicator { get; }

    /// <summary>The colour and width choice.</summary>
    public StyleStrip Strip { get; }

    public void Dispose()
    {
        Strip.Dispose();
        Indicator.Dispose();
    }
}
