using Raffica.Core.Ocr;
using Raffica.Pdf.Documents;

namespace Raffica.Pdf.Ocr;

/// <summary>
/// The Ocr session of a tab: made on first use and disposed with the tab. While a text recognition runs the session is active (the mode of
/// <see cref="Commands.ModeExclusion"/>), holds the cancel source of the run and the progress it reports; the page view stays readable. It also remembers, in memory
/// only, that the scan hint was asked for this document. The members belong to the UI thread; <see cref="Changed"/> is raised there.
/// </summary>
internal sealed class OcrSession(PdfTab tab, SynchronizationContext ui) : IDisposable
{
    private CancellationTokenSource? _cancel;
    private bool _disposed;

    /// <summary>Raised on the UI thread after the mode, the progress or the message changed.</summary>
    public event Action? Changed;

    /// <summary>The tab the session belongs to.</summary>
    public PdfTab Tab { get; } = tab;

    /// <summary>The UI context the session reports on.</summary>
    public SynchronizationContext Ui { get; } = ui;

    /// <summary>Whether the mode is on (a run is in progress).</summary>
    public bool IsActive { get; private set; }

    /// <summary>How far the run is; null before the first page is decided.</summary>
    public OcrProgress? Progress { get; private set; }

    /// <summary>The sentence the badge shows and announces (the start, a step, the end); null when no run is on.</summary>
    public string? Message { get; private set; }

    /// <summary>Whether the run was asked to stop (the user's Cancel, or the close of the tab or the window).</summary>
    public bool CancelRequested => _cancel?.IsCancellationRequested == true;

    /// <summary>Starts the mode and gives the token of the run. Does nothing (null) when it is on already or the session is disposed.</summary>
    public CancellationToken? Enter(string message)
    {
        if (IsActive || _disposed)
        {
            return null;
        }

        _cancel = new CancellationTokenSource();
        IsActive = true;
        Progress = null;
        Message = message;
        Changed?.Invoke();
        return _cancel.Token;
    }

    /// <summary>Shows how far the run is; <paramref name="message"/> is what the badge says now (null: the sentence stays, so a screen reader is not told every page).</summary>
    public void Report(OcrProgress progress, string? message)
    {
        if (!IsActive)
        {
            return;
        }

        var sentence = message ?? Message;
        if (Progress == progress && sentence == Message)
        {
            return;
        }

        Progress = progress;
        Message = sentence;
        Changed?.Invoke();
    }

    /// <summary>Changes the sentence of the badge (the run is at its end).</summary>
    public void Say(string message)
    {
        if (IsActive)
        {
            Message = message;
            Changed?.Invoke();
        }
    }

    /// <summary>Asks the run to stop; the run ends with its own cancel path (the finished pages are asked about) unless the caller drops them.</summary>
    public void Cancel()
    {
        try
        {
            _cancel?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The run ended meanwhile.
        }
    }

    /// <summary>Ends the mode.</summary>
    public void Leave()
    {
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        Progress = null;
        Message = null;
        _cancel?.Dispose();
        _cancel = null;
        Changed?.Invoke();
    }

    /// <summary>Whether the scan hint was shown or answered for this document ("Not now" and "Don't ask again" both mean: not again). Held in memory only.</summary>
    public bool ScanHintHandled { get; set; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Cancel();
        _cancel?.Dispose();
        _cancel = null;
    }
}
