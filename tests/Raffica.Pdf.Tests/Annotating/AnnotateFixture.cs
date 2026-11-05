using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Raffica.Core.Annotations;
using Raffica.Core.Settings;
using Raffica.Core.Text;
using Raffica.Pdf.Annotating;
using Raffica.Pdf.Dialogs;
using Raffica.Pdf.Documents;
using Raffica.Pdf.Tests.Text;
using Raffica.Pdf.Tests.Viewing;
using Raffica.Pdf.Viewing;
using Raffica.Shell.Controls;
using Xunit;

namespace Raffica.Pdf.Tests.Annotating;

/// <summary>A clock that also makes timers; the test moves it by hand and a timer fires inside <see cref="Advance"/> once it is due.</summary>
internal sealed class ManualTimers : TimeProvider
{
    private readonly List<Timer> _timers = [];
    private long _milliseconds;

    public override long TimestampFrequency => 1000;

    public override long GetTimestamp() => _milliseconds;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new Timer(callback, state, _milliseconds + (long)dueTime.TotalMilliseconds);
        _timers.Add(timer);
        return timer;
    }

    public void Advance(int milliseconds)
    {
        _milliseconds += milliseconds;
        foreach (var timer in _timers.ToList())
        {
            if (!timer.Disposed && !timer.Fired && timer.Due <= _milliseconds)
            {
                timer.Fired = true;
                timer.Callback(timer.State);
            }
        }
    }

    private sealed class Timer(TimerCallback callback, object? state, long due) : ITimer
    {
        public TimerCallback Callback { get; } = callback;

        public object? State { get; } = state;

        public long Due { get; } = due;

        public bool Fired { get; set; }

        public bool Disposed { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period) => true;

        public void Dispose() => Disposed = true;

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}

/// <summary>
/// A temp copy of a test file open in a headless window with the text interaction (yielding while a tool is active) and the annotate
/// interaction on its page view, a tool model on in-memory settings and a clock with timers the test moves.
/// </summary>
internal sealed class AnnotateFixture : IDisposable
{
    private readonly TextFixture _text;
    private readonly TempCopy? _copy;
    private readonly AppSettings[] _settings;

    private AnnotateFixture(TextFixture text, TempCopy? copy, AppSettings[] settings, ToolState tools, AnnotateInteraction interaction, ManualTimers time)
    {
        _text = text;
        _copy = copy;
        _settings = settings;
        Tools = tools;
        Interaction = interaction;
        Time = time;
    }

    public ToolState Tools { get; }

    public AnnotateInteraction Interaction { get; }

    public ManualTimers Time { get; }

    public TextFixture Text => _text;

    public PageView View => _text.View;

    public PdfTab Tab => _text.Tab;

    public Window Window => _text.Window;

    public DialogHost Host => _text.Host;

    public string? Path => _copy?.Path;

    /// <summary>The settings in force (the tool model writes into them).</summary>
    public AppSettings CurrentSettings => _settings[0];

    /// <summary>Opens a copy of a test file in <c>testdata/pdfs</c>.</summary>
    public static Task<AnnotateFixture> OpenAsync(string file, double width = 800, double height = 900, double? zoom = null, AppSettings? settings = null)
    {
        var copy = new TempCopy(file);
        return OpenPathAsync(copy.Path, copy, width, height, zoom, settings);
    }

    /// <summary>Opens a file by its full path (the caller owns it unless a copy is given).</summary>
    public static async Task<AnnotateFixture> OpenPathAsync(string path, TempCopy? copy = null, double width = 800, double height = 900, double? zoom = null, AppSettings? settings = null)
    {
        var holder = new[] { settings ?? AppSettings.Default };
        var tools = new ToolState(() => holder[0], s => holder[0] = s);
        var text = await TextFixture.OpenAsync(path, width, height, zoom, () => tools.Tool != AnnotateTool.None);
        var time = new ManualTimers();
        var interaction = new AnnotateInteraction(text.Tab, text.View, tools, text.Text.Selection, new DialogQueue(text.Host), time);
        return new AnnotateFixture(text, copy, holder, tools, interaction, time);
    }

    public async Task<IReadOnlyList<PdfAnnotation>> AnnotationsAsync(int page) =>
        (await Tab.Session.Document!.GetAnnotationsAsync(page, TestContext.Current.CancellationToken)).Where(a => a.Kind is AnnotationKind.Highlight or AnnotationKind.Underline or AnnotationKind.StrikeOut).ToList();

    /// <summary>Waits until the markups that were started are made and the tab's state is current.</summary>
    public async Task SettleAsync()
    {
        ViewFixture.Pump();
        await Tab.Annotations.FlushAsync();
        ViewFixture.Pump();
    }

    public void Drag(Point from, Point to)
    {
        Window.MouseDown(from, Avalonia.Input.MouseButton.Left);
        Window.MouseMove(new Point((from.X + to.X) / 2, (from.Y + to.Y) / 2));
        Window.MouseMove(to);
        Window.MouseUp(to, Avalonia.Input.MouseButton.Left);
        ViewFixture.Pump();
    }

    public void Click(Point point, int times = 1)
    {
        for (var i = 0; i < times; i++)
        {
            Window.MouseDown(point, Avalonia.Input.MouseButton.Left);
            Window.MouseUp(point, Avalonia.Input.MouseButton.Left);
        }

        ViewFixture.Pump();
    }

    public void Dispose()
    {
        Interaction.Dispose();
        _text.Dispose();
        _copy?.Dispose();
    }
}
