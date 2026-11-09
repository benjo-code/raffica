using System.Globalization;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Raffica.Core.Annotations;
using Raffica.Core.Ocr;
using Raffica.Core.Pages;
using Raffica.Core.Pdf;
using Raffica.Core.Printing;
using Raffica.Core.Redaction;
using Raffica.Core.Saving;
using Raffica.Core.Signatures;
using Raffica.Core.Text;
using Raffica.Core.Updates;
using Raffica.Pdf.Dialogs;
using Raffica.Pdf.Documents;
using Raffica.Pdf.Ocr;
using Raffica.Pdf.Organizing;
using Raffica.Pdf.Printing;
using Raffica.Pdf.Signing;
using Raffica.Shell.Controls;
using Raffica.Shell.Frame;
using Xunit;

namespace Raffica.Pdf.Tests.Dialogs;

/// <summary>
/// Every card has its footer in one order: the primary action first (the default, Enter), then the secondary actions, the cancel last (Esc). A
/// card with a single way out has one button that is both. The walk covers every card factory; a factory that is not listed fails
/// <see cref="EveryCardFactoryIsListed"/>.
/// </summary>
public sealed class CardFooterTests : IDisposable
{
    private static readonly PdfPageSize A4 = new(595, 842);

    public void Dispose() => PdfStrings.Culture = CultureInfo.GetCultureInfo("en");

    private static LanguageList ListOf(params string[] names) =>
        new([.. names.Select(n => new TessdataLanguage(n, TessdataOrigin.Shipped, 16, n + ".traineddata"))], "en", stored: null);

    private static string Root() => Directory.CreateTempSubdirectory("raffica-footer-").FullName;

    internal static IEnumerable<(string Key, Func<Dialog> Make)> Entries()
    {
        var empty = new RedactionAnalysis([], [], true, [], [], true, 0, 0);
        var raster = new RasterPageInfo(0, 150, 1240, 1754, false, 4_000_000);
        Assert.True(ExternalLink.TryParse("https://example.org/a", out var link));
        var noLoss = new PageEditReport([]);
        var refused = new PageEditReport([new PageLoss(PageLossKind.EncryptionNotPreserved, "source:1", null, Blocks: true)]);
        var summary = new PlanSummary(12, 10, 3, 1, 0, 0);
        var info = new DocumentInfo("report.pdf", @"C:\Docs", 12, "1.7", 3_145_728, "Title", "Author", new DateTimeOffset(2024, 3, 5, 10, 0, 0, TimeSpan.Zero));
        var sig = new SavedSignature("a", "Mine", SavedSignatureKind.Image, 2, DrawColor.Black, null);

        yield return ("AboutCard.Create", () => AboutCard.Create("1.0", null, "notices", () => { }, () => { }));
        yield return ("ApplyCard.Create", () => ApplyCard.Create("a.pdf", summary, noLoss, _ => null, ApplyTarget.Choose, _ => { }));
        yield return ("ApplyCard.Create(only save as)", () => ApplyCard.Create("a.pdf", summary, noLoss, _ => null, ApplyTarget.SaveAs, _ => { }));
        yield return ("ApplyCard.Create(refused)", () => ApplyCard.Create("a.pdf", summary, refused, _ => "other.pdf", ApplyTarget.Choose, _ => { }));
        yield return ("ChangedOriginalCard.Create", () => ChangedOriginalCard.Create("a.pdf", _ => { }));
        yield return ("CrashCard.Create", () => CrashCard.Create(() => { }, () => { }));
        yield return ("DefaultAppCard.Create", () => DefaultAppCard.Create(_ => { }));
        yield return ("DocumentInfoCard.Create", () => DocumentInfoCard.Create(info, () => { }));
        yield return ("ErrorCard.Create", () => ErrorCard.Create("a.pdf", PdfOpenError.NotAPdf, () => { }));
        yield return ("ExportImagesCard.Create", () => ExportImagesCard.Create("a.pdf", [A4, A4], () => Task.FromResult<string?>(null), _ => { }));
        yield return ("ExtractCard.Create", () => ExtractCard.Create("a.pdf", 6, 1, "a", _ => { }));
        yield return ("ExtractCard.CreateConfirm", () => ExtractCard.CreateConfirm("1-2", ["Loss."], _ => { }));
        yield return ("GoToPageCard.Create", () => GoToPageCard.Create(10, 1, _ => { }));
        yield return ("HelpCard.Create", () => HelpCard.Create([new HelpEntry("File", "Open", "Ctrl+O")], () => { }));
        yield return ("HelpCard.Create(about)", () => HelpCard.Create([new HelpEntry("File", "Open", "Ctrl+O")], () => { }, () => { }));
        yield return ("InsertFileCard.Create", () => InsertFileCard.Create("b.pdf", 6, null, _ => { }));
        yield return ("LanguageFilesCard.Create", () => LanguageFilesCard.Create([new TessdataLanguage("eng", TessdataOrigin.Shipped, 1, "e")], _ => { }));
        yield return ("LanguageFilesCard.CreateReplace", () => LanguageFilesCard.CreateReplace("fra", _ => { }));
        yield return ("LinkCard.Create", () => LinkCard.Create(link!, () => { }, () => { }));
        yield return ("NoticeCard.Create", () => NoticeCard.Create("Title", "Message", () => { }));
        yield return ("OcrKeepPagesCard.CreateKeep", () => OcrKeepPagesCard.CreateKeep(3, "a.pdf", _ => { }));
        yield return ("OcrReportCard.Create", () => OcrReportCard.Create(new OcrReportText("Title", "Summary", ["Line"]), "a.pdf", () => { }));
        yield return ("OpenFailuresCard.Create", () => OpenFailuresCard.Create(["a.pdf", "b.pdf"], () => { }));
        yield return ("PartsResultCard.Create", () => PartsResultCard.Create(new PartsReport("Title", "Status", null, ["a.pdf"], @"C:\out", CanOpen: true), _ => { }));
        yield return ("PartsResultCard.Create(no open)", () => PartsResultCard.Create(new PartsReport("Title", "Status", null, ["a.pdf"], @"C:\out", CanOpen: false), _ => { }));
        yield return ("PasswordCard.Create", () => PasswordCard.Create("a.pdf", incorrect: false, _ => { }));
        yield return ("PrintCard.Create", () => PrintCard.Create(new PrintCardInput(10, 3, "plan.pdf", [new("Office", null, true)], new PrintSettings(), _ => Task.FromResult(false)), _ => { }));
        yield return ("PrintMessageCard.Create", () => PrintMessageCard.Create("Text", () => { }));
        yield return ("RecognizeCard.Create", () => RecognizeCard.Create("scan.pdf", 6, ListOf("eng"), false, _ => { }));
        yield return ("RedactApplyCard.Create", () => RedactApplyCard.Create("a.pdf", RedactionPlan.Empty, empty, [], ApplyTarget.Choose, _ => { }));
        yield return ("RedactApplyCard.Create(rescue)", () => RedactApplyCard.Create("a.pdf", RedactionPlan.Empty, empty, [raster], ApplyTarget.Choose, _ => { }));
        yield return ("RedactOriginalCard.Create", () => RedactOriginalCard.Create("a.pdf", _ => { }));
        yield return ("RedactRefusedCard.Create", () => RedactRefusedCard.Create("a.pdf", "Title", "Intro", ["Reason"], () => { }));
        yield return ("RedactResultCard.Create", () => RedactResultCard.Create("out.pdf", empty, new RedactionCheck([], []), () => { }));
        yield return ("RequiredFieldsCard.Create", () => RequiredFieldsCard.Create(["Name"], _ => { }));
        yield return ("SaveFailedCard.Create", () => SaveFailedCard.Create("a.pdf", SaveFailureReason.DiskFull, () => { }));
        yield return ("SaveFirstCard.Create", () => SaveFirstCard.Create("a.pdf", _ => { }));
        yield return ("SignedFileCard.Create", () => SignedFileCard.Create("a.pdf", SignatureAdvice.WouldInvalidate, false, _ => { }));
        yield return ("SignedFileCard.Create(note)", () => SignedFileCard.Create("a.pdf", SignatureAdvice.AppendedOnly, true, _ => { }));
        yield return ("SplitCard.Create", () => SplitCard.Create("a.pdf", 6, false, (mode, every, ranges) => SplitFlow.PlanFor(6, mode, every, ranges, "a", []), () => Task.FromResult<string?>(null), _ => { }));
        yield return ("SplitCard.CreateConfirm", () => SplitCard.CreateConfirm(3, ["Loss."], _ => { }));
        foreach (var kind in Enum.GetValues<UnsavedKind>())
        {
            yield return ($"UnsavedChangesCard.Create({kind})", () => UnsavedChangesCard.Create("a.pdf", _ => { }, kind));
        }

        yield return ("UpdateCard.Create(install)", () => UpdateCard.Create("0.2.0", "0.1.0", canInstall: true, _ => { }));
        yield return ("UpdateCard.Create(manual)", () => UpdateCard.Create("0.2.0", "0.1.0", canInstall: false, _ => { }));
        yield return ("UpdateProgressCard.Dialog", () => new UpdateProgressCard(() => { }, _ => { }).Dialog);
        yield return ("NewSignatureCard.Create", () => NewSignatureCard.Create(new SignatureLibrary(Root), () => Task.FromResult<Stream?>(null), _ => { }));
        yield return ("SignaturePickerCard.Create", () => SignaturePickerCard.Create([sig], new SignatureLibrary(Root), _ => { }));
        yield return ("SettingsDialog.Create", () => SettingsDialog.Create(new ShellSettings(), _ => { }, () => { }));
    }

    private static List<Button> VisibleButtons(Dialog dialog) =>
        dialog.Footer is Control footer
            ? [.. footer.GetLogicalDescendants().OfType<Button>().Where(b => b.IsVisible)]
            : [];

    private static IEnumerable<string> Problems(string key, List<Button> buttons)
    {
        if (buttons.Count == 0)
        {
            yield return $"{key}: no footer button";
            yield break;
        }

        var defaults = buttons.Where(b => b.IsDefault).ToList();
        var cancels = buttons.Where(b => b.IsCancel).ToList();
        if (defaults.Count != 1)
        {
            yield return $"{key}: expected one default button, found {defaults.Count}";
            yield break;
        }

        if (cancels.Count > 1)
        {
            yield return $"{key}: more than one cancel button";
            yield break;
        }

        if (cancels.Count == 1 && !ReferenceEquals(buttons[^1], cancels[0]))
        {
            yield return $"{key}: the cancel button is not last";
        }

        if ((cancels.Count == 0 || !ReferenceEquals(cancels[0], defaults[0])) && !ReferenceEquals(buttons[0], defaults[0]))
        {
            yield return $"{key}: the default button is not first";
        }
    }

    [AvaloniaTheory]
    [InlineData("en")]
    [InlineData("de")]
    public void EveryCardFooterHasThePrimaryFirstAndTheCancelLast(string language)
    {
        PdfStrings.Culture = CultureInfo.GetCultureInfo(language);
        var problems = new List<string>();
        foreach (var (key, make) in Entries())
        {
            // Shown in a window of its own, as in the app: radio groups and timers belong to the window the card is in.
            var host = new DialogHost { Content = new Border() };
            var window = new Window { Content = host, Width = 800, Height = 700 };
            window.Show();
            var dialog = make();
            host.Show(dialog);
            Dispatcher.UIThread.RunJobs();
            problems.AddRange(Problems(key, VisibleButtons(dialog)));
            window.Close();
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [AvaloniaFact]
    public void TheUpdateProgressCardKeepsTheOrderInEveryState()
    {
        var card = new UpdateProgressCard(() => { }, _ => { });
        var problems = new List<string>();
        problems.AddRange(Problems("progress, running", VisibleButtons(card.Dialog)));
        card.Apply(new UpdateState(UpdateStage.Failed, null, null, 0, null), null, canRetry: true, offersDownloadPage: true);
        problems.AddRange(Problems("progress, failed with retry and page", VisibleButtons(card.Dialog)));
        card.Apply(new UpdateState(UpdateStage.Failed, null, null, 0, null), null, canRetry: false, offersDownloadPage: false);
        problems.AddRange(Problems("progress, failed without retry", VisibleButtons(card.Dialog)));
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [AvaloniaFact]
    public void EveryCardFactoryIsListed()
    {
        var listed = Entries().Select(e => e.Key.Split('(')[0]).ToHashSet(StringComparer.Ordinal);
        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (var assembly in new[] { typeof(HelpCard).Assembly, typeof(SettingsDialog).Assembly })
        {
            foreach (var type in assembly.GetTypes().Where(t => !t.IsNested))
            {
                foreach (var method in type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (method.ReturnType == typeof(Dialog))
                    {
                        found.Add($"{type.Name}.{method.Name}");
                    }
                }

                if (type.GetProperty("Dialog", BindingFlags.Instance | BindingFlags.Public)?.PropertyType == typeof(Dialog))
                {
                    found.Add($"{type.Name}.Dialog");
                }
            }
        }

        var missing = found.Except(listed).Order().ToList();
        Assert.True(missing.Count == 0, "Card factories without a footer test: " + string.Join(", ", missing));
        var stale = listed.Except(found).Order().ToList();
        Assert.True(stale.Count == 0, "Listed factories that do not exist: " + string.Join(", ", stale));
    }

    private static (Window Window, DialogHost Host) Open(Dialog dialog)
    {
        var host = new DialogHost { Content = new Border() };
        var window = new Window { Content = host, Width = 800, Height = 700 };
        window.Show();
        host.Show(dialog);
        Dispatcher.UIThread.RunJobs();
        return (window, host);
    }

    [AvaloniaFact]
    public void EnterAnywhereInTheCardPressesTheDefaultButton()
    {
        var closed = 0;
        var card = HelpCard.Create([new HelpEntry("File", "Open", "Ctrl+O")], () => closed++);
        var (window, _) = Open(card);

        // The focus is on the list (not on a button): Enter reaches the default button of the footer, Close.
        var list = card.GetVisualDescendants().OfType<ScrollViewer>().First(v => v.Name == "PART_List");
        list.Focus();
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, closed);
        window.Close();
    }

    // A key that starts in the card and is not taken by a control (a check box and every button take Enter themselves).
    private static void PressEnterIn(Dialog card)
    {
        card.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter, PhysicalKey = PhysicalKey.Enter, KeyModifiers = KeyModifiers.None });
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void EnterDoesNotReachAButtonThatIsLockedWithItsGroup()
    {
        var decisions = new List<RedactApplyDecision>();
        var empty = new RedactionAnalysis([], [], true, [], [], true, 0, 0);
        var raster = new RasterPageInfo(0, 150, 1240, 1754, false, 4_000_000);
        var card = RedactApplyCard.Create("a.pdf", RedactionPlan.Empty, empty, [raster], ApplyTarget.Choose, decisions.Add);
        var (window, _) = Open(card);

        // The rescue page is not ticked, so the ways out are locked: Enter inside the card must not apply.
        PressEnterIn(card);
        Assert.Empty(decisions);

        card.GetVisualDescendants().OfType<CheckBox>().First().IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        PressEnterIn(card);
        Assert.Equal(ApplyChoice.SaveAs, Assert.Single(decisions).Choice);
        window.Close();
    }
}
