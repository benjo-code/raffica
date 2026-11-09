using Raffica.Core;
using Raffica.Core.Distribution;
using Raffica.Core.Settings;
using Raffica.Core.Updates;
using Raffica.Pdf.Dialogs;
using Raffica.Pdf.Packaging;

namespace Raffica.Pdf;

/// <summary>
/// The default app offer and command (M2.6). Nothing of it exists before it is used: the flow and its service are made by the first offer
/// (after the first sharp page of a tab, only while the setting asks and only where the app gave the window a service) or by the first command.
/// </summary>
internal sealed partial class PdfWindow
{
    private DefaultAppFlow? _defaultAppFlow;
    private CancellationTokenSource? _defaultAppCancel;
    private bool _defaultAppOfferStarted;

    /// <summary>
    /// What makes the default app service of the system. The app sets it (<see cref="PlatformDefaultAppServices.Create"/>, not in a measurement run);
    /// while it is null the window offers nothing and the command does nothing, so a test window never touches the system. Called on first use only.
    /// </summary>
    internal Func<IDefaultAppService>? DefaultAppServices { get; set; }

    /// <summary>Whether the flow exists (it is made by the first offer or command; for tests).</summary>
    internal bool DefaultAppFlowCreated => _defaultAppFlow is not null;

    private DefaultAppFlow? DefaultAppFlow => _defaultAppFlow ??= DefaultAppServices is { } make
        ? new DefaultAppFlow(make(), () => Settings.App, ChooseDefaultAppPrompt, Dialogs, snooze: SnoozeDefaultAppPrompt)
        : null;

    private CancellationToken DefaultAppToken => (_defaultAppCancel ??= new CancellationTokenSource()).Token;

    private void ChooseDefaultAppPrompt(DefaultAppPromptSetting prompt) => Settings.Update(Settings.App with { DefaultAppPrompt = prompt });

    private void SnoozeDefaultAppPrompt(DateOnly until) => Settings.Update(Settings.App with { DefaultAppPromptSnoozedUntil = until });

    /// <summary>
    /// After the first sharp page of a tab: offers once per run to make Raffica the default app. Nothing is made while the setting is "never", in a
    /// measurement run, or without a service; the flow itself asks the system and shows the card only when another app is the default.
    /// </summary>
    internal async Task OfferDefaultAppAsync()
    {
        if (_defaultAppOfferStarted || _options.IsMeasuring || DefaultAppServices is null || Settings.App.DefaultAppPrompt != DefaultAppPromptSetting.Ask || DefaultAppFlow.IsSnoozed(Settings.App, null))
        {
            return;
        }

        _defaultAppOfferStarted = true;
        if (DefaultAppFlow is { } flow)
        {
            await flow.OfferAtStartAsync(DefaultAppToken);
        }
    }

    /// <summary>
    /// Where the About card gets the notices text. It is read when the card opens and never before (a test replaces it to see that);
    /// the default reads the text embedded in the assembly.
    /// </summary>
    internal Func<string> NoticesSource { get; set; } = ThirdPartyNotices.Read;

    /// <summary>The About card (the palette, the help card): the notices are read now, when it opens, and the card returns when it is closed.</summary>
    internal Task ShowAboutAsync()
    {
        var notices = NoticesSource();
        return Dialogs.ShowAsync<bool>(done => AboutCard.Create(
            AppInfo.Version, UpdateTrust.TestBuildNote(PdfStrings.Culture), notices, () => done(true),
            // The link closes the card first; the confirmation card then waits in the queue behind it for a moment and shows.
            () => _ = GuardAsync(() => OpenAddressAfterConfirmationAsync(AboutCard.ProjectAddress))));
    }

    /// <summary>The command "Make Raffica the default app for PDFs" (the palette, the settings dialog).</summary>
    internal void MakeDefaultApp()
    {
        if (_options.IsMeasuring || DefaultAppFlow is not { } flow)
        {
            return;
        }

        _ = GuardAsync(() => flow.MakeDefaultFromCommandAsync(DefaultAppToken));
    }
}
