using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Raffica.Write.Documents;
using Raffica.Write.Docx;
using Xunit;

namespace Raffica.WriteApp.Tests.Viewing;

/// <summary>A full open that fails is told to the user once; the keys of the page view survive a lost focus.</summary>
public sealed class FaultAndFocusTests
{
    private static async Task<(WriteWindow Window, WriteTab Tab)> OpenThesisAsync(WindowFixture fixture, Action? beforeFullOpen = null)
    {
        var window = fixture.Show();
        var tab = Assert.IsType<Opened>(await window.Flow.OpenAsync(WindowFixture.Thesis)).Tab;

        // Before the first frame: the first drawn page starts the full open.
        tab.Runner.BeforeFullOpen = beforeFullOpen;
        WindowFixture.Pump();
        return (window, tab);
    }

    [AvaloniaTheory]
    [InlineData(false, "The file is damaged and cannot be read.")]
    [InlineData(true, "The file could not be read.")]
    public async Task AFailedFullOpenShowsOneCardAndLeavesTheTabUsable(bool unexpected, string wording)
    {
        var unobserved = new List<Exception>();
        void OnUnobserved(object? sender, UnobservedTaskExceptionEventArgs e) => unobserved.Add(e.Exception);
        TaskScheduler.UnobservedTaskException += OnUnobserved;
        try
        {
            using var fixture = new WindowFixture();
            var (window, tab) = await OpenThesisAsync(
                fixture,
                () => throw (unexpected ? new InvalidOperationException("a secret detail") : new DocxFormatException("a secret detail")));
            var view = window.ViewOf(tab)!;

            await ViewHarness.WaitAsync(() => tab.Runner.IsFaulted && window.Host.IsOpen);

            // One card with the wording of the open errors; never the text of the exception.
            var texts = WindowFixture.CardTexts(window);
            Assert.Contains("thesis.docx", texts);
            Assert.Contains(wording, texts);
            Assert.DoesNotContain(texts, t => t.Contains("secret", StringComparison.Ordinal));
            window.Host.Close();
            ViewHarness.Pump();
            await Task.Delay(100);
            ViewHarness.Pump();
            Assert.False(window.Host.IsOpen, "the card came a second time");

            // The tab stays open on what is laid out: it scrolls, and the status no longer promises more pages.
            Assert.False(tab.IsDisposed);
            Assert.True(view.IsLayoutComplete);
            Assert.Equal("Page 1 of 1", window.Status.Position);
            Assert.True(view.ScrollByWheel(-3));
            Assert.True(view.OffsetY > 0);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Assert.Empty(unobserved);
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= OnUnobserved;
        }
    }

    [AvaloniaFact]
    public async Task AFaultAfterTheTabClosedShowsNothing()
    {
        var unobserved = new List<Exception>();
        void OnUnobserved(object? sender, UnobservedTaskExceptionEventArgs e) => unobserved.Add(e.Exception);
        TaskScheduler.UnobservedTaskException += OnUnobserved;
        try
        {
            using var fixture = new WindowFixture();
            using var release = new ManualResetEventSlim();
            var (window, tab) = await OpenThesisAsync(fixture, () =>
            {
                release.Wait();
                throw new DocxFormatException("late");
            });
            var runner = tab.Runner;
            runner.Start();
            await Task.Delay(100);
            Assert.False(runner.PageTask.IsCompleted);

            window.Workspace.Close(tab);
            ViewHarness.Pump();
            release.Set();
            await runner.PageTask.WaitAsync(TimeSpan.FromSeconds(10));
            ViewHarness.Pump();
            await Task.Delay(100);
            ViewHarness.Pump();

            Assert.True(runner.IsFaulted);
            Assert.False(window.Host.IsOpen, "a card for a tab that is gone");
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Assert.Empty(unobserved);
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= OnUnobserved;
        }
    }

    [AvaloniaFact]
    public async Task OpeningADocumentGivesTheViewTheFocus()
    {
        using var fixture = new WindowFixture();
        var (window, tab) = await OpenThesisAsync(fixture);
        var view = window.ViewOf(tab)!;
        await ViewHarness.WaitAsync(() => view.IsFocused);
    }

    [AvaloniaFact]
    public async Task NavigationKeysScrollTheViewThroughTheWindowWhenTheFocusIsLost()
    {
        using var fixture = new WindowFixture();
        var (window, tab) = await OpenThesisAsync(fixture);
        var view = window.ViewOf(tab)!;
        await ViewHarness.WaitAsync(() => view.IsFocused);
        await ViewHarness.WaitAsync(() => view.PageCount >= 3 || view.Runner.PageLayout.IsComplete, 30000);

        // The focus is on a button of the ribbon (or gone, when the start tile left the tree): the keys that scroll still reach the view.
        var other = window.GetVisualDescendants().OfType<InputElement>().First(e => e.Focusable && e.IsEffectivelyEnabled && e.IsEffectivelyVisible && e is not Raffica.Write.Viewing.PageView && e is not TextBox && e is not Avalonia.Controls.Primitives.ScrollBar);
        Assert.True(other.Focus(), other.GetType().Name);
        Assert.False(view.IsFocused);

        window.KeyPressQwerty(PhysicalKey.End, RawInputModifiers.None);
        ViewHarness.Pump();
        var max = Raffica.Write.Viewing.PageScroller.MaxOffset(view.ContentHeight, view.Viewport.Height);
        Assert.True(max > 0);
        Assert.Equal(max, view.OffsetY);

        window.KeyPressQwerty(PhysicalKey.Home, RawInputModifiers.None);
        ViewHarness.Pump();
        Assert.Equal(0, view.OffsetY);

        window.KeyPressQwerty(PhysicalKey.PageDown, RawInputModifiers.None);
        ViewHarness.Pump();
        Assert.True(view.OffsetY > 0);

        // A text box keeps its keys: the command search is not scrolled behind.
        window.TitleSearch.FocusAndSelect();
        ViewHarness.Pump();
        var before = view.OffsetY;
        window.KeyPressQwerty(PhysicalKey.End, RawInputModifiers.None);
        ViewHarness.Pump();
        Assert.Equal(before, view.OffsetY);
    }
}
