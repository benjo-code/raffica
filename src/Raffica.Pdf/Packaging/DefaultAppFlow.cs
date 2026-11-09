using Raffica.Core.Distribution;
using Raffica.Core.Settings;
using Raffica.Pdf.Dialogs;

namespace Raffica.Pdf.Packaging;

/// <summary>
/// The offer to make Raffica the default app for PDF files, and the command that does it on request. The window makes the flow on first
/// use (after the first sharp page, or when the user asks), so nothing of it exists at startup. The service does its work on the pool; the flow only
/// shows the cards. Every wait has a time limit and a token. A machine that cannot set the default app (no <c>xdg-mime</c>, a locked registry, no
/// settings page) ends in one worded note and changes nothing; the offer is made once per run, so no result brings the card back in a loop.
/// </summary>
internal sealed class DefaultAppFlow
{
    /// <summary>How long the system is given to answer the question whether Raffica is the default.</summary>
    public static readonly TimeSpan StatusTimeout = TimeSpan.FromSeconds(10);

    /// <summary>How long the system is given to set the default (or to open its settings page).</summary>
    public static readonly TimeSpan MakeTimeout = TimeSpan.FromSeconds(30);

    private readonly IDefaultAppService _service;
    private readonly Func<AppSettings> _read;
    private readonly Action<DefaultAppPromptSetting> _write;
    private readonly DialogQueue _cards;
    private readonly TimeProvider? _time;
    private readonly Action<DateOnly>? _snooze;
    private bool _offered;

    /// <param name="service">Reads and sets the default app of this system.</param>
    /// <param name="read">The viewer settings in force.</param>
    /// <param name="write">Stores the answer to "Don't ask again".</param>
    /// <param name="cards">Where the cards are shown.</param>
    /// <param name="time">The clock of the card's arming delay and of the snooze (tests); null: the system clock.</param>
    /// <param name="snooze">Stores the day until which "Not now" keeps the card away (<see cref="SnoozeDays"/> days from today); null: "Not now" stores nothing.</param>
    public DefaultAppFlow(IDefaultAppService service, Func<AppSettings> read, Action<DefaultAppPromptSetting> write, DialogQueue cards, TimeProvider? time = null, Action<DateOnly>? snooze = null)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(write);
        ArgumentNullException.ThrowIfNull(cards);
        _service = service;
        _read = read;
        _write = write;
        _cards = cards;
        _time = time;
        _snooze = snooze;
    }

    /// <summary>How many days "Not now" keeps the card away.</summary>
    public const int SnoozeDays = 30;

    /// <summary>
    /// Whether "Not now" still holds on the day of <paramref name="time"/>: the card stays away through the stored day and returns on the first start after it.
    /// A day further away than <see cref="SnoozeDays"/> can only come from a clock that was wrong when it was stored: it counts as expired.
    /// </summary>
    internal static bool IsSnoozed(AppSettings settings, TimeProvider? time)
    {
        var today = Today(time);
        return settings.DefaultAppPromptSnoozedUntil is { } until && today <= until && until <= today.AddDays(SnoozeDays);
    }

    private static DateOnly Today(TimeProvider? time) => DateOnly.FromDateTime((time ?? TimeProvider.System).GetLocalNow().DateTime);

    /// <summary>Whether a question or a change of the default app is under way.</summary>
    public bool IsBusy { get; private set; }

    /// <summary>
    /// Asks once per run, and only when the setting is <see cref="DefaultAppPromptSetting.Ask"/>, "Not now" is not remembered for today (<see cref="IsSnoozed"/>), the system says another app is the default
    /// (<see cref="DefaultAppStatus.NotDefault"/>; an unknown status asks nothing) and nothing else of this flow runs.
    /// </summary>
    public async Task OfferAtStartAsync(CancellationToken cancellationToken)
    {
        if (_offered || IsBusy || _read().DefaultAppPrompt != DefaultAppPromptSetting.Ask || IsSnoozed(_read(), _time) || !_service.IsSupported)
        {
            return;
        }

        // Set before anything is asked: whatever the answer or a failure, the run does not ask a second time.
        _offered = true;
        IsBusy = true;
        try
        {
            if (await StatusAsync(cancellationToken) != DefaultAppStatus.NotDefault || _read().DefaultAppPrompt != DefaultAppPromptSetting.Ask)
            {
                return;
            }

            var choice = await _cards.ShowAsync<DefaultAppChoice>(done => DefaultAppCard.Create(done, _time));
            cancellationToken.ThrowIfCancellationRequested();
            switch (choice)
            {
                case DefaultAppChoice.SetDefault:
                    await MakeAndReportAsync(cancellationToken);
                    break;
                case DefaultAppChoice.DontAskAgain:
                    _write(DefaultAppPromptSetting.Never);
                    break;
                case DefaultAppChoice.NotNow:
                    _snooze?.Invoke(Today(_time).AddDays(SnoozeDays));
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            // The window closes: nothing more to show.
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>The command "Make Raffica the default app for PDFs": no question first, the result is worded in a note. It does not touch the setting.</summary>
    public async Task MakeDefaultFromCommandAsync(CancellationToken cancellationToken)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await MakeAndReportAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // The window closes: nothing more to show.
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<DefaultAppStatus> StatusAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _service.GetStatusAsync(cancellationToken).WaitAsync(StatusTimeout, cancellationToken);
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return DefaultAppStatus.Unknown;
        }
    }

    private async Task MakeAndReportAsync(CancellationToken cancellationToken)
    {
        var result = DefaultAppResult.Failed;
        if (!_service.IsSupported)
        {
            result = DefaultAppResult.NotAvailable;
        }
        else
        {
            try
            {
                result = await _service.MakeDefaultAsync(cancellationToken).WaitAsync(MakeTimeout, cancellationToken);
            }
            catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                // A service that throws or hangs ends like any other failure: one note, nothing changed.
            }
        }

        var text = NoteFor(result);
        await _cards.ShowAsync<bool>(done => NoticeCard.Create(PdfStrings.DefaultAppResultTitle, text, () => done(true)));
    }

    /// <summary>The wording of a result in the current UI language.</summary>
    internal static string NoteFor(DefaultAppResult result) => result switch
    {
        DefaultAppResult.Done => PdfStrings.DefaultAppDone,
        DefaultAppResult.OpenedSystemSettings => PdfStrings.DefaultAppOpenedSettings,
        DefaultAppResult.NotAvailable => PdfStrings.DefaultAppNotAvailable,
        DefaultAppResult.Failed => PdfStrings.DefaultAppFailed,
        _ => throw new ArgumentOutOfRangeException(nameof(result), result, "This result has no text."),
    };
}
