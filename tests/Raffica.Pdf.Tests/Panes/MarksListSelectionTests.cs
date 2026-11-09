using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Raffica.Pdf.Tests.Viewing;
using Raffica.Shell;
using Raffica.Shell.Theming;
using Xunit;

namespace Raffica.Pdf.Tests.Panes;

/// <summary>The marks list is a nav-list with its own edge bar (mark-edge): the selected row has one bar, and its content sits where it did before the nav-list edge existed.</summary>
public sealed class MarksListSelectionTests
{
    [AvaloniaFact]
    public void ASelectedMarksRowHasOneBarAndTheContentDoesNotShift()
    {
        Border MakeRow(out Border edge)
        {
            edge = new Border { Width = 3, Classes = { "mark-edge" }, HorizontalAlignment = HorizontalAlignment.Left };
            return new Border { Child = new Panel { Children = { edge, new TextBlock { Text = "row", Margin = new Thickness(8, 0, 0, 0) } } }, Height = 30 };
        }

        var marks = new ListBox { Width = 200, Height = 120, Items = { MakeRow(out _), MakeRow(out _) }, SelectedIndex = 0 };
        marks.Classes.Add("nav-list");
        marks.Classes.Add("marks-list");
        var plain = new ListBox { Width = 200, Height = 120, Items = { MakeRow(out _), MakeRow(out _) }, SelectedIndex = 0 };
        plain.Classes.Add("nav-list");
        var window = new Window { Content = new StackPanel { Children = { marks, plain } } };
        window.Show();
        ViewFixture.Pump();

        var markPresenter = marks.GetVisualDescendants().OfType<ContentPresenter>().First(p => p.Name == "PART_ContentPresenter" && p.Parent is ListBoxItem);
        var plainPresenter = plain.GetVisualDescendants().OfType<ContentPresenter>().First(p => p.Name == "PART_ContentPresenter" && p.Parent is ListBoxItem);

        // One bar: the presenter has no edge in the marks list (mark-edge draws it); the plain nav-list has it.
        Assert.Equal(0, markPresenter.BorderThickness.Left);
        Assert.Equal(2, plainPresenter.BorderThickness.Left);

        // No shift: the child starts at the 6 px padding of the marks list, as before.
        var child = markPresenter.Child!;
        Assert.Equal(6, child.TranslatePoint(default, markPresenter)!.Value.X, 0.5);
        window.Close();
    }
}
