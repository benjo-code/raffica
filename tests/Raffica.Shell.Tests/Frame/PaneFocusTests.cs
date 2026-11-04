using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Raffica.Shell.Controls;
using Raffica.Shell.Frame;
using Raffica.Shell.Localization;
using Raffica.Shell.Tests.Controls;
using Xunit;

namespace Raffica.Shell.Tests.Frame;

public sealed class PaneFocusTests
{
    private sealed record Fixture(
        Window Window, DialogHost Host, Pane Navigation, Pane Document, Pane Comments, Button NavButton, StatusBar Status, PaneFocus Focus);

    private static Fixture Create()
    {
        ShellTheme.Apply(Application.Current!, ThemeMode.Light, AppInk.Write, forceHighContrast: false);
        var navButton = new Button { Content = "Nav" };
        var navigation = new Pane { Title = ShellStrings.PaneNavigation, Content = navButton, Width = 200 };
        var document = new Pane { PaneName = ShellStrings.PaneDocument, IsDocument = true, Content = new Button { Content = "Doc" } };
        var comments = new Pane { Title = ShellStrings.PaneComments, Content = new Button { Content = "Comment" }, Width = 200 };
        var status = new StatusBar();
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), RowDefinitions = new RowDefinitions("*,Auto") };
        Grid.SetColumn(document, 1);
        Grid.SetColumn(comments, 2);
        Grid.SetRow(status, 1);
        Grid.SetColumnSpan(status, 3);
        grid.Children.Add(navigation);
        grid.Children.Add(document);
        grid.Children.Add(comments);
        grid.Children.Add(status);
        var host = new DialogHost { Content = grid };
        var window = new Window { Width = 900, Height = 500, Content = host };
        window.Show();
        ControlHarness.Settle(window);
        var focus = new PaneFocus(window, [navigation, document, comments], status);
        return new Fixture(window, host, navigation, document, comments, navButton, status, focus);
    }

    private static void Press(Window window, PhysicalKey key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyPressQwerty(key, modifiers);
        ControlHarness.Settle(window);
    }

    private static bool FocusIsInside(Fixture f, Pane pane)
    {
        var focused = TopLevel.GetTopLevel(f.Window)!.FocusManager!.GetFocusedElement() as Visual;
        return focused is not null && pane.IsVisualAncestorOf(focused);
    }

    [AvaloniaFact]
    public void F6MovesNavigationDocumentComments()
    {
        var f = Create();
        Press(f.Window, PhysicalKey.F6);
        Assert.True(FocusIsInside(f, f.Navigation));
        Press(f.Window, PhysicalKey.F6);
        Assert.True(FocusIsInside(f, f.Document));
        Press(f.Window, PhysicalKey.F6);
        Assert.True(FocusIsInside(f, f.Comments));
        Press(f.Window, PhysicalKey.F6);
        Assert.True(FocusIsInside(f, f.Navigation));
        Assert.True(f.Focus.IsNavigating);
    }

    [AvaloniaFact]
    public void ShiftF6GoesBackwards()
    {
        var f = Create();
        Press(f.Window, PhysicalKey.F6, RawInputModifiers.Shift);
        Assert.True(FocusIsInside(f, f.Comments));
        Press(f.Window, PhysicalKey.F6, RawInputModifiers.Shift);
        Assert.True(FocusIsInside(f, f.Document));
        Press(f.Window, PhysicalKey.F6, RawInputModifiers.Shift);
        Assert.True(FocusIsInside(f, f.Navigation));
    }

    [AvaloniaFact]
    public void FocusedPaneHasTheInkBorderAndPrimaryTitle()
    {
        var f = Create();
        Press(f.Window, PhysicalKey.F6);

        var frame = ControlHarness.Part<Border>(f.Navigation, "PART_Frame");
        var title = ControlHarness.Part<TextBlock>(f.Navigation, "PART_Title");
        Assert.True(f.Navigation.IsPaneFocused);
        Assert.Equal(new Thickness(1), frame.BorderThickness);
        ControlHarness.AssertToken("Raffica.Brush.Ink", frame.BorderBrush);
        ControlHarness.AssertToken("Raffica.Brush.Text.Primary", title.Foreground);

        var otherTitle = ControlHarness.Part<TextBlock>(f.Comments, "PART_Title");
        Assert.False(f.Comments.IsPaneFocused);
        ControlHarness.AssertToken("Raffica.Brush.Text.Secondary", otherTitle.Foreground);
        Assert.NotEqual(ControlHarness.Token("Raffica.Brush.Ink"), ControlHarness.ColorOf(ControlHarness.Part<Border>(f.Comments, "PART_Frame").BorderBrush));

        Press(f.Window, PhysicalKey.F6);
        Assert.False(f.Navigation.IsPaneFocused);
        ControlHarness.AssertToken("Raffica.Brush.Text.Secondary", title.Foreground);
    }

    [AvaloniaFact]
    public void StatusShowsFocusOnlyWhileNavigating()
    {
        var previous = ShellStrings.Culture;
        try
        {
            ShellStrings.Culture = CultureInfo.GetCultureInfo("de");
            var f = Create();
            Assert.Null(f.Status.FocusPane);

            Press(f.Window, PhysicalKey.F6);
            Press(f.Window, PhysicalKey.F6);
            Press(f.Window, PhysicalKey.F6);
            Assert.Equal("Fokus: Kommentare", f.Status.FocusPane);
            Assert.True(ControlHarness.Part<Control>(f.Status, "PART_FocusText").IsVisible);

            f.Focus.End();
            ControlHarness.Settle(f.Window);
            Assert.Null(f.Status.FocusPane);
            Assert.False(ControlHarness.Part<Control>(f.Status, "PART_FocusText").IsVisible);
        }
        finally
        {
            ShellStrings.Culture = previous;
        }
    }

    [AvaloniaFact]
    public void PointerPressEndsNavigation()
    {
        var f = Create();
        Press(f.Window, PhysicalKey.F6);
        Assert.True(f.Focus.IsNavigating);

        f.Window.MouseDown(new Point(450, 250), MouseButton.Left);
        ControlHarness.Settle(f.Window);

        Assert.False(f.Focus.IsNavigating);
        Assert.False(f.Navigation.IsPaneFocused);
        Assert.Null(f.Status.FocusPane);
    }

    [AvaloniaFact]
    public void EscEndsNavigation()
    {
        var f = Create();
        Press(f.Window, PhysicalKey.F6);
        Press(f.Window, PhysicalKey.F6);
        Assert.True(f.Document.IsPaneFocused);

        Press(f.Window, PhysicalKey.Escape);

        Assert.False(f.Focus.IsNavigating);
        Assert.False(f.Document.IsPaneFocused);
        Assert.Null(f.Status.FocusPane);
    }

    [AvaloniaFact]
    public void ClosingTheCommentsPaneWhileFocusedMovesToTheDocument()
    {
        var f = Create();
        Press(f.Window, PhysicalKey.F6, RawInputModifiers.Shift);
        Assert.True(f.Comments.IsPaneFocused);

        f.Comments.IsOpen = false;
        ControlHarness.Settle(f.Window);

        Assert.False(f.Comments.IsPaneFocused);
        Assert.True(f.Document.IsPaneFocused);
        Assert.True(FocusIsInside(f, f.Document));
        Assert.Equal(ShellStrings.FocusPane(ShellStrings.PaneDocument), f.Status.FocusPane);
        Assert.True(f.Focus.IsNavigating);
    }

    [AvaloniaFact]
    public void ClosingAPaneThatDoesNotHaveTheFocusChangesNothing()
    {
        var f = Create();
        Press(f.Window, PhysicalKey.F6);
        f.Comments.IsOpen = false;
        ControlHarness.Settle(f.Window);
        Assert.True(f.Navigation.IsPaneFocused);
        Assert.True(FocusIsInside(f, f.Navigation));
    }

    [AvaloniaFact]
    public void F6DoesNothingWhileADialogIsOpen()
    {
        var f = Create();
        f.Host.Show(new Dialog { Title = "Dialog", Content = new Button { Content = "OK" } });
        ControlHarness.Settle(f.Window);

        Press(f.Window, PhysicalKey.F6);

        Assert.False(f.Focus.IsNavigating);
        Assert.False(f.Navigation.IsPaneFocused);
        Assert.Null(f.Status.FocusPane);
        Assert.False(f.Focus.Move(false));
        Assert.False(FocusIsInside(f, f.Navigation));
        Assert.False(FocusIsInside(f, f.Document));
    }

    [AvaloniaFact]
    public void MoveFocusesTheFirstFocusableDescendantAndReportsSuccess()
    {
        var f = Create();
        Assert.True(f.Focus.Move(false));
        Assert.Same(f.NavButton, TopLevel.GetTopLevel(f.Window)!.FocusManager!.GetFocusedElement());
    }

    [AvaloniaFact]
    public void FocusMovesIntoThatPaneAndMarksIt()
    {
        var f = Create();

        Assert.True(f.Focus.Focus(f.Comments));

        Assert.True(FocusIsInside(f, f.Comments));
        Assert.True(f.Comments.IsPaneFocused);
        Assert.Equal("Focus: Comments", f.Status.FocusPane);
        Assert.True(f.Focus.Focus(f.Navigation));
        Assert.False(f.Comments.IsPaneFocused);
        Assert.True(f.Navigation.IsPaneFocused);

        f.Host.Show(new Dialog { Title = "Dialog", Content = new Button { Content = "OK" } });
        ControlHarness.Settle(f.Window);
        Assert.False(f.Focus.Focus(f.Comments));
    }
}
