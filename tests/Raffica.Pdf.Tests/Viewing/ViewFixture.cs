using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Raffica.Core.Pdf;
using Raffica.Core.Viewing;
using Raffica.Pdf.Documents;
using Raffica.Pdf.Viewing;
using Raffica.TestSupport;
using Xunit;

namespace Raffica.Pdf.Tests.Viewing;

/// <summary>An opened document in a headless window with a <see cref="PageView"/> on it; everything is disposed with the fixture.</summary>
internal sealed class ViewFixture : IDisposable
{
    private static readonly PdfEngine Engine = new();

    private ViewFixture(PdfTab tab, PageView view, Window window)
    {
        Tab = tab;
        View = view;
        Window = window;
    }

    public PdfTab Tab { get; }

    private static string PathOf(string file) => Path.IsPathRooted(file) ? file : TestData.Pdf(file);

    public PageView View { get; }

    public Window Window { get; }

    public ViewerSession Session => Tab.Session;

    /// <summary>Opens a test file; <paramref name="show"/> puts the view into a window of the given size, otherwise it stays off screen.</summary>
    public static async Task<ViewFixture> OpenAsync(string file, double width = 800, double height = 600, bool show = true, Action<ViewerSession>? prepare = null, Action<PageView>? configure = null)
    {
        var session = new ViewerSession(Engine);
        await session.OpenAsync(PathOf(file));
        Assert.Equal(ViewerState.Ready, session.State);
        prepare?.Invoke(session);
        var tab = new PdfTab(session, PathOf(file));
        var view = new PageView { Tab = tab };
        configure?.Invoke(view);
        var window = new Window { Width = width, Height = height };
        if (show)
        {
            window.Content = view;
            window.Show();
        }
        else
        {
            view.Measure(new Size(width, height));
            view.Arrange(new Rect(0, 0, width, height));
        }

        var fixture = new ViewFixture(tab, view, window);
        Pump();
        return fixture;
    }

    public static void Pump()
    {
        for (var i = 0; i < 5; i++)
        {
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>Waits until the scheduler has nothing left to render and the tiles that arrived were taken over by the view.</summary>
    public async Task SettleAsync()
    {
        for (var i = 0; i < 400 && Session.Scheduler is { PendingCount: > 0 }; i++)
        {
            await Task.Delay(25);
            Pump();
        }

        Assert.Equal(0, Session.Scheduler!.PendingCount);
        await Task.Delay(25);
        Pump();
    }

    public static async Task WaitAsync(Func<bool> condition)
    {
        for (var i = 0; i < 400 && !condition(); i++)
        {
            await Task.Delay(25);
            Pump();
        }

        Assert.True(condition());
    }

    /// <summary>Paints the view into a bitmap (one drawing pass) and returns the BGRA pixels with their row width.</summary>
    public (byte[] Pixels, int Stride) Capture()
    {
        var size = new PixelSize((int)View.Bounds.Width, (int)View.Bounds.Height);
        using var frame = new RenderTargetBitmap(size, new Vector(96, 96));
        frame.Render(View);
        var stride = size.Width * 4;
        var pixels = new byte[stride * size.Height];
        var buffer = Marshal.AllocHGlobal(pixels.Length);
        try
        {
            frame.CopyPixels(new Avalonia.PixelRect(size), buffer, pixels.Length, stride);
            Marshal.Copy(buffer, pixels, 0, pixels.Length);
            Raffica.Pdf.Tests.Viewing.PixelOrder.ToBgra(pixels, frame.Format);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return (pixels, stride);
    }

    public void Dispose()
    {
        Window.Close();
        View.Tab = null;
        Tab.Dispose();
        Pump();
    }
}
