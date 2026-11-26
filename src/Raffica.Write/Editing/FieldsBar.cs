using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Raffica.Write.Editing;

/// <summary>
/// The line that says how many fields of the document use options Write does not update (they keep Word's last result): offered once per document session, whatever the answer was. The model decides whether and what to say
/// (<see cref="Offer"/>); <see cref="Build"/> makes the bar for the frame's notification slot.
/// </summary>
internal sealed class FieldsBar
{
    /// <summary>Whether the bar was offered (and so will not be offered again in this session of the document).</summary>
    public bool WasOffered { get; private set; }

    /// <summary>Whether the user dismissed the bar.</summary>
    public bool IsDismissed { get; private set; }

    /// <summary>The count the bar was offered with.</summary>
    public int Count { get; private set; }

    /// <summary>The sentence of the bar; null before it was offered.</summary>
    public string? Text { get; private set; }

    /// <summary>True while the bar is on the screen: offered and not dismissed.</summary>
    public bool IsShown => Text is not null && !IsDismissed;

    /// <summary>
    /// The sentence for <paramref name="count"/> fields, the first time it is called with a count above 0; null afterwards and for 0 (the document session says it once). The count is the fields of the whole body that a full refresh found, plus the
    /// ones in notes, headers, footers and comments.
    /// </summary>
    public string? Offer(int count)
    {
        if (WasOffered || count <= 0)
        {
            return null;
        }

        WasOffered = true;
        Count = count;
        Text = count == 1 ? WriteStrings.FieldsBarOne : WriteStrings.Fill(WriteStrings.FieldsBarManyFormat, count);
        return Text;
    }

    /// <summary>Ends the bar for this document session.</summary>
    public void Dismiss() => IsDismissed = true;

    /// <summary>Makes the bar: the sentence and a button that dismisses it (<paramref name="dismissed"/> runs after the click). Call it only while <see cref="IsShown"/>.</summary>
    public Control Build(Action dismissed)
    {
        ArgumentNullException.ThrowIfNull(dismissed);
        var text = Text ?? throw new InvalidOperationException("The bar was not offered.");
        var label = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        label.Bind(TextBlock.ForegroundProperty, label.GetResourceObservable("Raffica.Brush.Text.Primary"));
        var button = new Button { Content = WriteStrings.FieldsBarDismiss, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        button.Click += (_, _) =>
        {
            Dismiss();
            dismissed();
        };
        var row = new DockPanel { LastChildFill = true, Margin = new Thickness(16, 6, 16, 6) };
        DockPanel.SetDock(button, Dock.Right);
        row.Children.Add(button);
        row.Children.Add(label);
        var bar = new Border { Child = row };
        bar.Bind(Border.BackgroundProperty, bar.GetResourceObservable("Raffica.Brush.Surface.Band"));
        return bar;
    }
}
