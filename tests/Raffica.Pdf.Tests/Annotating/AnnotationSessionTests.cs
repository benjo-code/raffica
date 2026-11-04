using System.Collections.Concurrent;
using Avalonia.Headless.XUnit;
using Raffica.Core.Annotations;
using Raffica.Core.Text;
using Raffica.Pdf.Annotating;
using Raffica.Pdf.Tests.Viewing;
using Raffica.TestSupport;
using Xunit;

namespace Raffica.Pdf.Tests.Annotating;

/// <summary>A context that only queues what is posted to it; the test runs the queue by hand and so decides when the "UI thread" acts.</summary>
internal sealed class QueueContext : SynchronizationContext
{
    private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _queue = new();

    public int PostedFromThread { get; private set; } = -1;

    public int Count => _queue.Count;

    public override void Post(SendOrPostCallback d, object? state)
    {
        PostedFromThread = Environment.CurrentManagedThreadId;
        _queue.Enqueue((d, state));
    }

    public int Run()
    {
        var count = 0;
        while (_queue.TryDequeue(out var item))
        {
            item.Callback(item.State);
            count++;
        }

        return count;
    }
}

/// <summary>A copy of a test file in a temp folder (saving replaces the file); the folder goes with the object.</summary>
internal sealed class TempCopy : IDisposable
{
    public TempCopy(string file)
    {
        Folder = Directory.CreateTempSubdirectory("raffica-annotating-").FullName;
        Path = System.IO.Path.Combine(Folder, file);
        File.Copy(TestData.Pdf(file), Path);
    }

    public string Folder { get; }

    public string Path { get; }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Folder, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>A temp copy of a test file opened as a tab.</summary>
internal sealed class AnnotatedFile : IDisposable
{
    private readonly TempCopy _copy;

    private AnnotatedFile(TempCopy copy, ViewFixture fixture)
    {
        _copy = copy;
        Fixture = fixture;
    }

    public ViewFixture Fixture { get; }

    public Raffica.Pdf.Documents.PdfTab Tab => Fixture.Tab;

    public static async Task<AnnotatedFile> OpenAsync(string file = "text-rich.pdf")
    {
        var copy = new TempCopy(file);
        return new AnnotatedFile(copy, await ViewFixture.OpenAsync(copy.Path, show: false));
    }

    public static Task<PdfAnnotation?> HighlightAsync(AnnotationSession session) =>
        session.Editor.ApplyMarkupAsync(0, AnnotationKind.Highlight, [new PageRect(10, 10, 100, 30)], new AnnotationColor(255, 255, 0), null, null);

    public void Dispose()
    {
        Fixture.Dispose();
        _copy.Dispose();
    }
}

public sealed class AnnotationSessionTests
{
    [AvaloniaFact]
    public async Task ChangeMarksTheTabDirtyAndTheDotAppears()
    {
        using var file = await AnnotatedFile.OpenAsync();
        var tab = file.Tab;
        Assert.False(tab.IsDirty);

        Assert.NotNull(await AnnotatedFile.HighlightAsync(tab.Annotations));
        await tab.Annotations.FlushAsync();
        ViewFixture.Pump();

        Assert.True(tab.IsDirty);
        Assert.True(tab.Tab.IsDirty);
        Assert.True(tab.Annotations.CanUndo);
    }

    [AvaloniaFact]
    public async Task ChangedFromAnotherThreadIsMarshalledToTheUiContext()
    {
        using var file = await AnnotatedFile.OpenAsync();
        var ui = new QueueContext();
        using var session = new AnnotationSession(file.Tab, ui);
        var states = 0;
        session.StateChanged += () => states++;

        var apply = Task.Run(() => AnnotatedFile.HighlightAsync(session), TestContext.Current.CancellationToken);
        await apply;
        for (var i = 0; i < 200 && ui.Count == 0; i++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        // The event arrived on a pool thread, and neither the tab nor a handler was touched yet.
        Assert.True(ui.Count > 0);
        Assert.NotEqual(Environment.CurrentManagedThreadId, ui.PostedFromThread);
        Assert.False(file.Tab.IsDirty);
        Assert.Equal(0, states);

        Assert.True(ui.Run() > 0);
        Assert.True(file.Tab.IsDirty);
        Assert.True(states > 0);
    }

    [AvaloniaFact]
    public async Task BusyEditorCountsAsDirty()
    {
        using var file = await AnnotatedFile.OpenAsync();
        var session = file.Tab.Annotations;
        Assert.False(session.IsDirty);

        // The task is started and not awaited: the editor is busy from the call on.
        var apply = AnnotatedFile.HighlightAsync(session);
        Assert.True(session.Editor.IsBusy);
        Assert.True(session.IsDirty);

        await apply;
        await session.FlushAsync();
        Assert.True(session.IsDirty);
        Assert.False(session.Editor.IsBusy);
    }

    [AvaloniaFact]
    public async Task PendingDraftCountsAsDirtyUntilDisposed()
    {
        using var file = await AnnotatedFile.OpenAsync();
        var session = file.Tab.Annotations;
        Assert.False(session.IsDirty);

        var draft = session.BeginPending();
        Assert.True(session.IsDirty);
        Assert.True(file.Tab.IsDirty);

        draft.Dispose();
        draft.Dispose();
        Assert.False(session.IsDirty);
        Assert.False(file.Tab.IsDirty);
    }

    [AvaloniaFact]
    public async Task UndoToTheSavedVersionIsStillDirty()
    {
        using var file = await AnnotatedFile.OpenAsync();
        var session = file.Tab.Annotations;
        await AnnotatedFile.HighlightAsync(session);
        await session.FlushAsync();

        await session.UndoAsync();
        ViewFixture.Pump();
        Assert.True(session.IsDirty);
        Assert.False(session.CanUndo);
        Assert.True(session.CanRedo);

        await session.RedoAsync();
        ViewFixture.Pump();
        Assert.True(session.IsDirty);
        Assert.True(file.Tab.IsDirty);
    }

    [AvaloniaFact]
    public async Task FlushWaitsForAnInFlightStroke()
    {
        using var file = await AnnotatedFile.OpenAsync();
        var session = file.Tab.Annotations;
        var stroke = session.BeginPending();

        var flush = session.FlushAsync();
        await Task.Delay(50, TestContext.Current.CancellationToken);
        ViewFixture.Pump();
        Assert.False(flush.IsCompleted);

        await AnnotatedFile.HighlightAsync(session);
        stroke.Dispose();
        await flush;

        Assert.True(session.Editor.CanUndo);
        Assert.False(session.Editor.IsBusy);
    }

    [AvaloniaFact]
    public async Task TheSessionIsLazyAndGoesWithTheTab()
    {
        using var file = await AnnotatedFile.OpenAsync();
        var session = file.Tab.Annotations;
        Assert.Same(session, file.Tab.Annotations);

        file.Tab.Dispose();
        // A change that arrives after the tab is gone is ignored.
        file.Tab.Annotations.Dispose();
    }
}
