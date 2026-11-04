using Avalonia.Controls;
using Raffica.Core.Printing;
using Raffica.Pdf.Dialogs;

namespace Raffica.Pdf.Printing;

/// <summary>
/// Where the printing UI appears: the Linux dialog and the messages in the dialog queue of the window (one card at a time), the job card in
/// the notification slot of the frame. The command knows nothing of the window, so tests give it their own host.
/// </summary>
internal sealed class PrintPresenter(DialogQueue dialogs, Action<Control?> setJobSlot)
{
    private PrintJobCard? _current;

    /// <summary>Shows the print dialog of the app (where the system has none) and returns what the user chose, or null when they cancelled.</summary>
    public Task<PrintSettings?> ChooseAsync(PrintCardInput input) =>
        dialogs.ShowAsync<PrintSettings?>(done => PrintCard.Create(input, done));

    /// <summary>Shows the card of a running job.</summary>
    public PrintJobCard ShowJob(string documentName, Action cancel)
    {
        var card = new PrintJobCard(documentName, cancel);
        _current = card;
        setJobSlot(card);
        return card;
    }

    /// <summary>Takes the card away (only when it is still the one shown).</summary>
    public void HideJob(PrintJobCard card)
    {
        if (ReferenceEquals(_current, card))
        {
            _current = null;
            setJobSlot(null);
        }
    }

    /// <summary>Tells the user something; it waits behind other cards and does not hold the caller back.</summary>
    public void Message(string text) =>
        _ = dialogs.ShowAsync<bool>(done => PrintMessageCard.Create(text, () => done(true)));
}
