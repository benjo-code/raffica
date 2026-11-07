using System.Globalization;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Raffica.Core.Updates;
using Raffica.Pdf.Dialogs;
using Raffica.Pdf.Updating;
using Xunit;

namespace Raffica.Pdf.Tests.Updating;

public sealed class UpdateProgressCardTests : IDisposable
{
    private static readonly UpdateVersion Version = new(0, 2, 0);
    private static readonly UpdateFile Setup = new(UpdateFileKinds.WindowsSetup, "RafficaPdf-0.2.0-win-x64-setup.exe", 10 * 1024 * 1024, new string('a', 64));

    public void Dispose() => PdfStrings.Culture = CultureInfo.GetCultureInfo("en");

    private static UpdateState At(UpdateStage stage, long bytes = 0) => new(stage, Version, Setup, bytes, null);

    private static T Part<T>(UpdateProgressCard card, string name) where T : Control =>
        card.Dialog.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);

    private static string[] Texts(UpdateProgressCard card, string prefix) =>
        Enumerable.Range(0, 4).Select(i => Part<TextBlock>(card, prefix + i).Text ?? string.Empty).ToArray();

    private static Window Show(UpdateProgressCard card)
    {
        var window = new Window { Content = card.Dialog };
        window.Show();
        return window;
    }

    [AvaloniaTheory]
    [InlineData("en", "Download|Check|Install|Restart", "Done", "In progress")]
    [InlineData("de", "Herunterladen|Prüfen|Installieren|Neu starten", "Fertig", "Läuft")]
    public void StepsAreNamedInTextAndCancelStopsBeforeInstall(string language, string joinedNames, string done, string current)
    {
        PdfStrings.Culture = CultureInfo.GetCultureInfo(language);
        var cancels = 0;
        var card = new UpdateProgressCard(() => cancels++, _ => { });
        var window = Show(card);
        try
        {
            Assert.Equal(joinedNames.Split('|'), Texts(card, "PART_Step"));

            // Download: the megabytes next to the bar, Cancel works.
            card.Apply(At(UpdateStage.Downloading, 5 * 1024 * 1024), null, false, false);
            Assert.Equal(0, card.CurrentStep);
            Assert.Contains(UpdateWording.Megabytes(5 * 1024 * 1024), Texts(card, "PART_Status")[0], StringComparison.Ordinal);
            card.RequestCancel();
            Assert.Equal(1, cancels);

            // Check: the download is done and the check is the current step, both in text (never colour or weight alone), and Cancel still works.
            card.Apply(At(UpdateStage.Verifying, Setup.Size), null, false, false);
            Assert.Equal(1, card.CurrentStep);
            Assert.Equal([done, current, string.Empty, string.Empty], Texts(card, "PART_Status"));
            card.RequestCancel();
            Assert.Equal(2, cancels);

            // Install and restart: no Cancel any more, the button is disabled and a request does nothing.
            foreach (var (stage, step) in new[] { (UpdateStage.Ready, 2), (UpdateStage.Installing, 2), (UpdateStage.Restarting, 3) })
            {
                card.Apply(At(stage, Setup.Size), null, false, false);
                Assert.Equal(step, card.CurrentStep);
                Assert.False(Part<Button>(card, "PART_Cancel").IsEnabled, stage.ToString());
                card.RequestCancel();
                Assert.Equal(2, cancels);
                var status = Texts(card, "PART_Status");
                Assert.All(status.Take(step), s => Assert.Equal(done, s));
                Assert.Equal(current, status[step]);
            }
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void AFailureShowsRetryOnlyWhereItCanHelpAndNeverTheRawCode()
    {
        var card = new UpdateProgressCard(() => { }, _ => { });
        var window = Show(card);
        try
        {
            card.Apply(new UpdateState(UpdateStage.Failed, Version, Setup, 0, UpdateFailure.Unreachable), null, canRetry: true, offersDownloadPage: false);
            Assert.Equal(-1, card.CurrentStep);
            Assert.True(Part<Button>(card, "PART_Retry").IsVisible);
            Assert.False(Part<Button>(card, "PART_Page").IsVisible);
            Assert.False(Part<Button>(card, "PART_Cancel").IsVisible);
            Assert.Equal(UpdateWording.ForFailure(UpdateFailure.Unreachable, PdfStrings.Culture), Part<TextBlock>(card, "PART_Failure").Text);

            card.Apply(new UpdateState(UpdateStage.Failed, Version, Setup, 0, UpdateFailure.Checksum), null, canRetry: false, offersDownloadPage: true);
            Assert.False(Part<Button>(card, "PART_Retry").IsVisible);
            Assert.True(Part<Button>(card, "PART_Page").IsVisible);
        }
        finally
        {
            window.Close();
        }
    }
}
