using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Raffica.Shell.Controls;
using Raffica.Shell.Localization;
using Xunit;

namespace Raffica.Shell.Tests.Controls;

public sealed class NotificationBarTests
{
    private static (Window Window, StackPanel Stack, NotificationBar Bar) Show(ThemeMode mode, NotificationSeverity severity, string? leadIn = "Lead.", bool withAction = true)
    {
        var bar = new NotificationBar { Severity = severity, LeadIn = leadIn, Message = "The message of the bar." };
        if (withAction)
        {
            var action = new Button { Content = "Act" };
            action.Classes.Add("boxed");
            bar.Actions.Add(action);
        }

        var below = new Button { Content = "Below" };
        var stack = new StackPanel { Margin = new Thickness(10), Children = { bar, below } };
        var window = ControlHarness.Show(stack, mode);
        return (window, stack, bar);
    }

    private static object? Resource(string key)
    {
        Assert.True(Application.Current!.TryGetResource(key, Application.Current.ActualThemeVariant, out var value), key);
        return value;
    }

    [AvaloniaTheory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    public void TheBarIs40PxHighWithTheSurfaceEdgeAndRadius(ThemeMode mode)
    {
        var (_, _, bar) = Show(mode, NotificationSeverity.Info);

        Assert.Equal(40, bar.Bounds.Height);
        var frame = ControlHarness.Part<Border>(bar, "PART_Frame");
        Assert.Equal(40, frame.Bounds.Height);
        Assert.Equal(new Thickness(1), frame.BorderThickness);
        Assert.Equal(new CornerRadius(3), frame.CornerRadius);
        ControlHarness.AssertToken("Raffica.Brush.Surface.Page", frame.Background, "bar fill");
        ControlHarness.AssertToken("Raffica.Brush.Border.Control", frame.BorderBrush, "bar edge");
        Assert.False(ControlHarness.HasShadow(bar));
    }

    [AvaloniaTheory]
    [InlineData(ThemeMode.Light, NotificationSeverity.Info, "Raffica.Icon.Info", "Raffica.Brush.Text.Secondary")]
    [InlineData(ThemeMode.Dark, NotificationSeverity.Info, "Raffica.Icon.Info", "Raffica.Brush.Text.Secondary")]
    [InlineData(ThemeMode.Light, NotificationSeverity.Warning, "Raffica.Icon.TriangleAlert", "Raffica.Brush.Warning.Icon")]
    [InlineData(ThemeMode.Dark, NotificationSeverity.Warning, "Raffica.Icon.TriangleAlert", "Raffica.Brush.Warning.Icon")]
    [InlineData(ThemeMode.Light, NotificationSeverity.Error, "Raffica.Icon.OctagonAlert", "Raffica.Brush.Text.Primary")]
    [InlineData(ThemeMode.Dark, NotificationSeverity.Error, "Raffica.Icon.OctagonAlert", "Raffica.Brush.Text.Primary")]
    public void TheSeverityDecidesIconAndBrush(ThemeMode mode, NotificationSeverity severity, string iconKey, string brushKey)
    {
        var (_, _, bar) = Show(mode, severity);
        var icon = ControlHarness.Part<StrokeIcon>(bar, "PART_Icon");

        Assert.Same(Resource(iconKey), icon.Data);
        Assert.Equal(16, icon.Size);
        ControlHarness.AssertToken(brushKey, icon.EffectiveStroke, "severity icon");
    }

    [AvaloniaTheory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    public void TheSeverityCanChangeAtRuntime(ThemeMode mode)
    {
        var (window, _, bar) = Show(mode, NotificationSeverity.Info);
        var icon = ControlHarness.Part<StrokeIcon>(bar, "PART_Icon");

        bar.Severity = NotificationSeverity.Warning;
        ControlHarness.Settle(window);

        Assert.Same(Resource("Raffica.Icon.TriangleAlert"), icon.Data);
        ControlHarness.AssertToken("Raffica.Brush.Warning.Icon", icon.EffectiveStroke, "warning icon");
    }

    [AvaloniaTheory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    public void TheErrorBarUsesNoDangerBrushAnywhere(ThemeMode mode)
    {
        var (_, _, bar) = Show(mode, NotificationSeverity.Error);
        var danger = ControlHarness.Token("Raffica.Brush.Danger");

        var brushes = new List<(string What, IBrush? Brush)>();
        foreach (var visual in ControlHarness.Descendants(bar).Prepend(bar))
        {
            switch (visual)
            {
                case Border border:
                    brushes.Add(("border background", border.Background));
                    brushes.Add(("border edge", border.BorderBrush));
                    break;
                case ContentPresenter presenter:
                    brushes.Add(("presenter background", presenter.Background));
                    brushes.Add(("presenter edge", presenter.BorderBrush));
                    brushes.Add(("presenter text", presenter.Foreground));
                    break;
                case TextBlock text:
                    brushes.Add(("text", text.Foreground));
                    foreach (var run in text.Inlines!.OfType<Run>())
                    {
                        brushes.Add(("run", run.Foreground));
                    }

                    break;
                case StrokeIcon icon:
                    brushes.Add(("icon", icon.EffectiveStroke));
                    break;
                case TemplatedControl control:
                    brushes.Add((control.GetType().Name + " foreground", control.Foreground));
                    brushes.Add((control.GetType().Name + " background", control.Background));
                    brushes.Add((control.GetType().Name + " edge", control.BorderBrush));
                    break;
            }
        }

        Assert.NotEmpty(brushes);
        foreach (var (what, brush) in brushes)
        {
            if (brush is ISolidColorBrush solid)
            {
                Assert.True(solid.Color != danger, $"{what} uses the danger colour");
            }
        }

        var errorIcon = ControlHarness.Part<StrokeIcon>(bar, "PART_Icon");
        ControlHarness.AssertToken("Raffica.Brush.Text.Primary", errorIcon.EffectiveStroke, "error icon");
    }

    [AvaloniaFact]
    public void AThirdActionThrows()
    {
        var bar = new NotificationBar();
        bar.Actions.Add(new Button());
        bar.Actions.Add(new Button());

        Assert.Throws<InvalidOperationException>(() => bar.Actions.Add(new Button()));
        Assert.Equal(2, bar.Actions.Count);
    }

    [AvaloniaTheory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    public void TheActionsAreShownInTheBar(ThemeMode mode)
    {
        var (_, _, bar) = Show(mode, NotificationSeverity.Info);
        var panel = ControlHarness.Part<Panel>(bar, "PART_Actions");

        Assert.Single(panel.Children);
        Assert.Same(bar.Actions[0], panel.Children[0]);
        Assert.True(bar.Actions[0].Bounds.Width > 0);
    }

    [AvaloniaTheory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    public void ClosingRaisesClosedAndRemovesTheBarFromTheLayout(ThemeMode mode)
    {
        var (window, stack, bar) = Show(mode, NotificationSeverity.Warning);
        var below = stack.Children[1];
        var top = below.Bounds.Top;
        var closed = 0;
        bar.Closed += (_, _) => closed++;

        var close = ControlHarness.Part<Button>(bar, "PART_Close");
        Assert.True(close.IsVisible);
        var at = close.TranslatePoint(new Point(close.Bounds.Width / 2, close.Bounds.Height / 2), window)!.Value;
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        ControlHarness.Settle(window);

        Assert.Equal(1, closed);
        Assert.False(bar.IsVisible);
        Assert.Equal(top - 40, below.Bounds.Top);

        bar.Close();
        Assert.Equal(1, closed);
    }

    [AvaloniaFact]
    public void IsClosableHidesTheCloseButton()
    {
        var (window, _, bar) = Show(ThemeMode.Light, NotificationSeverity.Info);
        var close = ControlHarness.Part<Button>(bar, "PART_Close");
        Assert.True(bar.IsClosable);
        Assert.True(close.IsVisible);

        bar.IsClosable = false;
        ControlHarness.Settle(window);

        Assert.False(close.IsVisible);
    }

    [AvaloniaTheory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    public void TheLeadInIsBoldAndTheMessageIsNot(ThemeMode mode)
    {
        var (_, _, bar) = Show(mode, NotificationSeverity.Warning, leadIn: "Font missing.");
        var runs = ControlHarness.Part<TextBlock>(bar, "PART_Text").Inlines!.OfType<Run>().ToList();

        Assert.Equal(2, runs.Count);
        Assert.Equal("Font missing. ", runs[0].Text);
        Assert.Equal(FontWeight.SemiBold, runs[0].FontWeight);
        Assert.Equal("The message of the bar.", runs[1].Text);
        Assert.NotEqual(FontWeight.SemiBold, runs[1].FontWeight);

        var (_, _, plain) = Show(mode, NotificationSeverity.Info, leadIn: null);
        var plainRuns = ControlHarness.Part<TextBlock>(plain, "PART_Text").Inlines!.OfType<Run>().ToList();
        Assert.Single(plainRuns);
        Assert.Equal("The message of the bar.", plainRuns[0].Text);
    }

    [AvaloniaTheory]
    [InlineData(NotificationSeverity.Info)]
    [InlineData(NotificationSeverity.Warning)]
    [InlineData(NotificationSeverity.Error)]
    public void TheCloseButtonAndTheIconHaveAutomationNames(NotificationSeverity severity)
    {
        var (_, _, bar) = Show(ThemeMode.Light, severity);

        Assert.Equal(ShellStrings.Close, AutomationProperties.GetName(ControlHarness.Part<Button>(bar, "PART_Close")));
        var expected = severity switch
        {
            NotificationSeverity.Info => ShellStrings.NotificationInfo,
            NotificationSeverity.Warning => ShellStrings.NotificationWarning,
            _ => ShellStrings.NotificationError,
        };
        Assert.Equal(expected, AutomationProperties.GetName(ControlHarness.Part<StrokeIcon>(bar, "PART_Icon")));
    }
}
