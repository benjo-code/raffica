using System.Reflection;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Raffica.Core.Viewing;
using Raffica.Pdf.Panes;
using Raffica.Pdf.Tests.Panes;
using Xunit;

namespace Raffica.Pdf.Tests.Viewing;

/// <summary>
/// The events of the scheduler and the session arrive on pool threads, possibly after the headless session of a test has ended (each test has its own
/// dispatcher; the end of one clears the static UI dispatcher until the next test makes its own). A handler that posts through the static
/// <c>Dispatcher.UIThread</c> then makes a new dispatcher on the pool thread, which is published before its platform part is set: a second pool thread
/// that posts at that moment throws a NullReferenceException in <c>Dispatcher.RequestProcessing</c> and takes the test host down. The handlers must post
/// to the dispatcher their owner was made on.
/// </summary>
public sealed class TeardownRaceTests
{
    private static readonly FieldInfo UiThreadField =
        typeof(Dispatcher).GetField("s_uiThread", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("Avalonia changed the static UI dispatcher: update this test.");

    [AvaloniaFact]
    public async Task EventsOnPoolThreadsNeverTouchTheStaticDispatcherWhenNoneIsLive()
    {
        using var f = await PaneFixture<PagesPane>.OpenAsync("pages-marked.pdf", (tab, view) => new PagesPane(tab, view));
        await f.FirstSharpAsync();
        await ViewFixture.WaitAsync(() => f.Pane.LastPlan.Count > 0);
        var thumbnails = f.Session.EnsureThumbnails()!.Scheduler;
        var pages = f.Session.Scheduler!;
        await ViewFixture.WaitAsync(() => thumbnails.PendingCount == 0 && pages.PendingCount == 0);
        ViewFixture.Pump();

        var live = UiThreadField.GetValue(null);
        Assert.NotNull(live);
        object? published;
        Exception? error = null;
        UiThreadField.SetValue(null, null);
        try
        {
            // The same calls the schedulers and the session make from their pool threads (a tile arrived, pages were invalidated, the state changed).
            var key = new TileKey(0, TileKey.PreviewScaleKey, 0, 0);
            try
            {
                await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
                {
                    Raise(thumbnails, "TileReady", key);
                    Raise(pages, "TileReady", key);
                    Raise(f.Session, "PagesInvalidated", (IReadOnlySet<int>)new HashSet<int> { 0 });
                    Raise(f.Session, "Changed");
                })));
            }
            catch (Exception ex)
            {
                error = ex;
            }

            published = UiThreadField.GetValue(null);
        }
        finally
        {
            UiThreadField.SetValue(null, live);
        }

        Assert.Null(error);
        Assert.Null(published);
        ViewFixture.Pump();
    }

    // Invokes the field-like event on the calling (pool) thread, as its owner does.
    private static void Raise(object owner, string eventName, params object[] args)
    {
        var field = owner.GetType().GetField(eventName, BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"{owner.GetType().Name}.{eventName} is not a field-like event.");
        ((Delegate?)field.GetValue(owner))?.DynamicInvoke(args);
    }
}
