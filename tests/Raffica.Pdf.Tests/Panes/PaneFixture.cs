using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Raffica.Core.Viewing;
using Raffica.Pdf.Documents;
using Raffica.Pdf.Tests.Viewing;
using Raffica.Pdf.Viewing;
using Raffica.Shell.Frame;

namespace Raffica.Pdf.Tests.Panes;

/// <summary>An opened document with its page view and a pane next to it (204 px wide) in one headless window.</summary>
internal sealed class PaneFixture<TPane> : IDisposable
    where TPane : Control
{
    private readonly ViewFixture _inner;

    private PaneFixture(ViewFixture inner, TPane pane, Grid grid)
    {
        _inner = inner;
        Pane = pane;
        Grid = grid;
    }

    public TPane Pane { get; }

    public Grid Grid { get; }

    public PageView View => _inner.View;

    public PdfTab Tab => _inner.Tab;

    public ViewerSession Session => _inner.Session;

    public Window Window => _inner.Window;

    /// <summary>Opens the file; <paramref name="make"/> builds the pane for the tab and its view.</summary>
    public static async Task<PaneFixture<TPane>> OpenAsync(string file, Func<PdfTab, PageView, TPane> make, bool showView = true)
    {
        // The view is made off screen first so that the pane exists before the view is shown (or never shown).
        var inner = await ViewFixture.OpenAsync(file, 1000, 700, show: false);
        var pane = make(inner.Tab, inner.View);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("204,*") };
        var left = new Pane { Title = "Navigation", Content = pane };
        grid.Children.Add(left);
        Grid.SetColumn(left, 0);
        inner.Window.Width = 1000;
        inner.Window.Height = 700;
        if (showView)
        {
            grid.Children.Add(inner.View);
            Grid.SetColumn(inner.View, 1);
        }

        inner.Window.Content = grid;
        inner.Window.Show();
        ViewFixture.Pump();
        return new PaneFixture<TPane>(inner, pane, grid);
    }

    /// <summary>Waits until the view has drawn its first sharp page.</summary>
    public Task FirstSharpAsync() => ViewFixture.WaitAsync(() => View.FirstSharpPageShown);

    /// <summary>Clicks the middle of a control with the left mouse button (a real press and release through the window).</summary>
    public void Click(Control target)
    {
        var point = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), Window) ?? throw new InvalidOperationException("not in the window");
        Window.MouseDown(point, MouseButton.Left);
        Window.MouseUp(point, MouseButton.Left);
        ViewFixture.Pump();
    }

    public void Dispose() => _inner.Dispose();
}
