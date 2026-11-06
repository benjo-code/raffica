using Raffica.Pdf.Documents;

namespace Raffica.Pdf.Redacting;

/// <summary>The Redacting session of a tab: made on first use and disposed with the tab. Groundwork of M2.5; the behavior arrives with its task.</summary>
internal sealed class RedactSession(PdfTab tab, SynchronizationContext ui) : IDisposable
{
    /// <summary>The tab the session belongs to.</summary>
    public PdfTab Tab { get; } = tab;

    /// <summary>The UI context the session reports on.</summary>
    public SynchronizationContext Ui { get; } = ui;

    /// <summary>Whether the mode is on.</summary>
    public bool IsActive { get; private set; }

    public void Dispose()
    {
    }
}
