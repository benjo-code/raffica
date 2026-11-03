using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Raffica.Shell.Controls;
using Raffica.Shell.Localization;
using Xunit;

namespace Raffica.Shell.Tests.Controls;

public sealed class DialogTests
{
    private sealed record Fixture(Window Window, DialogHost Host, Dialog Dialog, Button Opener, Button Behind, Button Body, Button Cancel, Button Ok, Counter BehindClicks, Counter CancelRequests);

    private sealed class Counter
    {
        public int Count { get; set; }
    }

    private static Fixture Create(ThemeMode mode, bool open = true)
    {
        var behindClicks = new Counter();
        var cancelRequests = new Counter();
        var opener = new Button { Content = "Open" };
        var behind = new Button { Content = "Behind" };
        behind.Click += (_, _) => behindClicks.Count++;
        var host = new DialogHost { Content = new StackPanel { Margin = new Thickness(20), Spacing = 8, Children = { opener, behind } } };

        var body = new Button { Content = "Body", HorizontalAlignment = HorizontalAlignment.Left };
        var cancel = new Button { Content = "Cancel" };
        cancel.Classes.Add("boxed");
        var ok = new Button { Content = "OK" };
        ok.Classes.Add("primary");
        var dialog = new Dialog
        {
            Title = "Settings",
            Content = body,
            Footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { cancel, ok } },
        };
        dialog.CancelRequested += (_, _) => cancelRequests.Count++;

        var window = ControlHarness.Show(host, mode);
        window.Width = 800;
        window.Height = 600;
        ControlHarness.Settle(window);
        opener.Focus(NavigationMethod.Tab);
        ControlHarness.Settle(window);
        if (open)
        {
            host.Show(dialog);
            ControlHarness.Settle(window);
        }

        return new Fixture(window, host, dialog, opener, behind, body, cancel, ok, behindClicks, cancelRequests);
    }

    private static void Press(Window window, PhysicalKey key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyPressQwerty(key, modifiers);
        window.KeyReleaseQwerty(key, modifiers);
        ControlHarness.Settle(window);
    }

    private static Point Centre(Window window, Control control) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;

    private static void Click(Window window, Control control)
    {
        var at = Centre(window, control);
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        ControlHarness.Settle(window);
    }

    private static bool IsInside(Dialog dialog, IInputElement? element) =>
        element is Visual visual && (ReferenceEquals(visual, dialog) || dialog.IsVisualAncestorOf(visual));

    private static IInputElement? Focused(Window window) => window.FocusManager!.GetFocusedElement();

    [AvaloniaTheory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    public void DialogHostShowsTheScrimAndMovesFocusIntoTheDialog(ThemeMode mode)
    {
        var f = Create(mode, open: false);
        var scrim = ControlHarness.Part<Border>(f.Host, "PART_Scrim");
        Assert.False(f.Host.IsOpen);
        Assert.False(scrim.IsVisible);

        f.Host.Show(f.Dialog);
        ControlHarness.Settle(f.Window);

        Assert.True(f.Host.IsOpen);
        Assert.True(scrim.IsVisible);
        ControlHarness.AssertToken("Raffica.Brush.Scrim", scrim.Background, "scrim");
        Assert.Equal(f.Window.Bounds.Size, scrim.Bounds.Size);
        Assert.True(IsInside(f.Dialog, Focused(f.Window)), "focus moved into the dialog");
        Assert.Same(f.Body, Focused(f.Window));
    }

    [AvaloniaTheory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    public void TabKeyNeverLeavesTheDialog(ThemeMode mode)
    {
        var f = Create(mode);
        var visited = new HashSet<IInputElement>();

        for (var i = 0; i < 10; i++)
        {
            Press(f.Window, PhysicalKey.Tab);
            var focused = Focused(f.Window);
            Assert.True(IsInside(f.Dialog, focused), $"Tab #{i + 1} left the dialog");
            visited.Add(focused!);
        }

        // The close button, the body button and the two footer buttons all take part in the cycle.
        Assert.Contains(f.Body, visited);
        Assert.Contains(f.Cancel, visited);
        Assert.Contains(f.Ok, visited);
        Assert.Equal(4, visited.Count);

        for (var i = 0; i < 10; i++)
        {
            Press(f.Window, PhysicalKey.Tab, RawInputModifiers.Shift);
            Assert.True(IsInside(f.Dialog, Focused(f.Window)), $"Shift+Tab #{i + 1} left the dialog");
        }

        // From the first element Shift+Tab wraps to the last one, and Tab from the last wraps to the first.
        var close = ControlHarness.Part<Button>(f.Dialog, "PART_Close");
        close.Focus(NavigationMethod.Tab);
        Press(f.Window, PhysicalKey.Tab, RawInputModifiers.Shift);
        Assert.Same(f.Ok, Focused(f.Window));
        Press(f.Window, PhysicalKey.Tab);
        Assert.Same(close, Focused(f.Window));
    }

    [AvaloniaTheory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    public void EscapeRaisesCancelRequested(ThemeMode mode)
    {
        var f = Create(mode);

        Press(f.Window, PhysicalKey.Escape);

        Assert.Equal(1, f.CancelRequests.Count);
        // The dialog does not close itself: the app decides.
        Assert.True(f.Host.IsOpen);

        // The close button in the header asks for the same.
        Click(f.Window, ControlHarness.Part<Button>(f.Dialog, "PART_Close"));
        Assert.Equal(2, f.CancelRequests.Count);
    }

    [AvaloniaTheory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    public void ContentBehindTheDialogIsNotReachable(ThemeMode mode)
    {
        var f = Create(mode);
        var at = Centre(f.Window, f.Behind);

        // A click lands on the scrim, not on the covered button.
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        var hit = f.Window.InputHitTest(at) as Visual;
        Assert.NotNull(hit);
        Assert.False(ReferenceEquals(hit, f.Behind) || f.Behind.IsVisualAncestorOf(hit));
        Click(f.Window, f.Behind);
        Assert.Equal(0, f.BehindClicks.Count);

        // A key sent while the covered button has the focus does nothing either.
        f.Behind.Focus(NavigationMethod.Tab);
        Press(f.Window, PhysicalKey.Space);
        Press(f.Window, PhysicalKey.Enter);
        Assert.Equal(0, f.BehindClicks.Count);
        Assert.True(f.Host.IsOpen);
    }

    [AvaloniaTheory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    public void FocusReturnsToTheOpenerOnClose(ThemeMode mode)
    {
        var f = Create(mode);
        Assert.NotSame(f.Opener, Focused(f.Window));

        f.Host.Close();
        ControlHarness.Settle(f.Window);

        Assert.False(f.Host.IsOpen);
        Assert.False(ControlHarness.Part<Border>(f.Host, "PART_Scrim").IsVisible);
        Assert.Same(f.Opener, Focused(f.Window));

        // The content is reachable again.
        Click(f.Window, f.Behind);
        Assert.Equal(1, f.BehindClicks.Count);
    }

    [AvaloniaTheory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    public void DialogMetricsMatchTheSpec(ThemeMode mode)
    {
        var f = Create(mode);

        Assert.Equal(520, f.Dialog.Bounds.Width);
        var frame = ControlHarness.Part<Border>(f.Dialog, "PART_Frame");
        Assert.Equal(new Thickness(1), frame.BorderThickness);
        Assert.Equal(new CornerRadius(3), frame.CornerRadius);
        ControlHarness.AssertToken("Raffica.Brush.Border.Control", frame.BorderBrush, "frame edge");
        ControlHarness.AssertToken("Raffica.Brush.Surface.Page", frame.Background, "frame fill");
        Assert.False(ControlHarness.HasShadow(f.Dialog));

        var header = ControlHarness.Part<Border>(f.Dialog, "PART_Header");
        Assert.Equal(44, header.Bounds.Height);
        var footer = ControlHarness.Part<Border>(f.Dialog, "PART_FooterBar");
        Assert.Equal(52, footer.Bounds.Height);
        ControlHarness.AssertToken("Raffica.Brush.Surface.Band", footer.Background, "footer fill");
        Assert.Equal(new Thickness(16), f.Dialog.Padding);

        var title = ControlHarness.Part<TextBlock>(f.Dialog, "PART_Title");
        Assert.Equal("Settings", title.Text);
        Assert.Equal(15, title.FontSize);
        Assert.Equal(FontWeight.SemiBold, title.FontWeight);
        ControlHarness.AssertToken("Raffica.Brush.Text.Primary", title.Foreground, "title");

        var close = ControlHarness.Part<Button>(f.Dialog, "PART_Close");
        Assert.Equal(ShellStrings.Close, AutomationProperties.GetName(close));

        foreach (var button in new[] { f.Cancel, f.Ok })
        {
            Assert.Equal(28, button.Bounds.Height);
            Assert.True(button.Bounds.Width >= 96, $"{button.Content} is {button.Bounds.Width} px wide");
        }

        // The buttons sit on the right, 16 px from the edge of the footer bar (inside the 1 px frame).
        var right = f.Ok.TranslatePoint(new Point(f.Ok.Bounds.Width, 0), footer)!.Value.X;
        Assert.Equal(footer.Bounds.Width - 16, right, 1);
    }

    [AvaloniaFact]
    public void TheHostHoldsOneDialogAtATime()
    {
        var f = Create(ThemeMode.Light);

        Assert.Throws<InvalidOperationException>(() => f.Host.Show(new Dialog()));
        f.Host.Close();
        f.Host.Close();
        Assert.False(f.Host.IsOpen);
    }
}
