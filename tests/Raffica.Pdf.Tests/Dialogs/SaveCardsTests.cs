using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Raffica.Core.Saving;
using Raffica.Pdf.Dialogs;
using Raffica.Pdf.Documents;
using Raffica.Pdf.Tests.Text;
using Raffica.Shell.Controls;
using Xunit;

namespace Raffica.Pdf.Tests.Dialogs;

public sealed class SaveCardsTests
{
    private static readonly SaveFailureReason[] AllReasons = Enum.GetValues<SaveFailureReason>();

    private static readonly string[] CardKeys =
    [
        "SignedWarnTitle", "SignedWarnText", "SignedNoteTitle", "SignedNoteText", "SignedSaveAnyway",
        "ChangedTitle", "ChangedMessageFormat", "ChangedOverwrite", "SaveFailedTitle",
        "UnsavedLockedTitle", "UnsavedLockedMessageFormat", "UnsavedDiscard",
    ];

    private sealed class Shown(Window window, Dialog card, ManualTime time) : IDisposable
    {
        public Dialog Card { get; } = card;

        public ManualTime Time { get; } = time;

        public IReadOnlyList<string> ButtonNames =>
            Card.GetVisualDescendants().OfType<Button>().Select(b => b.Name!).Where(n => n.StartsWith("PART_", StringComparison.Ordinal) && n != "PART_Close").ToList();

        public IReadOnlyList<string?> Texts => Card.GetVisualDescendants().OfType<TextBlock>().Select(x => x.Text).ToList();

        public Button Button(string name) => Card.GetVisualDescendants().OfType<Button>().Single(b => b.Name == name);

        public void Click(string name) => Button(name).RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));

        public void Escape()
        {
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Pump();
        }

        public void Dispose() => window.Close();
    }

    private static void Pump()
    {
        for (var i = 0; i < 5; i++)
        {
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static Shown Show(Func<ManualTime, Dialog> make)
    {
        PdfStrings.Culture = CultureInfo.GetCultureInfo("en");
        var time = new ManualTime();
        var card = make(time);
        var host = new DialogHost { Content = new Border() };
        var window = new Window { Content = host, Width = 700, Height = 400 };
        window.Show();
        host.Show(card);
        Pump();
        return new Shown(window, card, time);
    }

    [AvaloniaFact]
    public void SignedWarningDefaultsToCancel()
    {
        SignedChoice? result = null;
        using var s = Show(t => SignedFileCard.Create("a.pdf", SignatureAdvice.WouldInvalidate, c => result = c, t));

        Assert.Equal(PdfStrings.SignedWarnTitle, s.Card.Title);
        Assert.Contains(PdfStrings.SignedWarnText, s.Texts);
        Assert.Equal(["PART_Cancel", "PART_SaveAs", "PART_SaveAnyway"], s.ButtonNames);
        Assert.True(s.Button("PART_Cancel").IsFocused);
        Assert.False(s.Button("PART_SaveAnyway").IsFocused);

        s.Click("PART_SaveAs");
        Assert.Equal(SignedChoice.SaveAs, result);
    }

    [AvaloniaFact]
    public void LevelThreeIsOnlyANote()
    {
        SignedChoice? result = null;
        using var s = Show(t => SignedFileCard.Create("a.pdf", SignatureAdvice.AppendedOnly, c => result = c, t));

        Assert.Equal(PdfStrings.SignedNoteTitle, s.Card.Title);
        Assert.Contains(PdfStrings.SignedNoteText, s.Texts);
        Assert.Equal(["PART_Ok"], s.ButtonNames);
        Assert.True(s.Button("PART_Ok").IsFocused);

        s.Click("PART_Ok");
        Assert.Equal(SignedChoice.Continue, result);
    }

    [Fact]
    public void ACardForAnUnsignedFileIsRefused() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => SignedFileCard.Create("a.pdf", SignatureAdvice.None, _ => { }));

    [AvaloniaFact]
    public void DangerousChoicesAreDisabledFor400ms()
    {
        var signed = new List<SignedChoice>();
        using (var s = Show(t => SignedFileCard.Create("a.pdf", SignatureAdvice.WouldInvalidate, signed.Add, t)))
        {
            Assert.False(s.Button("PART_SaveAnyway").IsEnabled);
            s.Click("PART_SaveAnyway");
            Assert.Empty(signed);
            s.Time.Advance(399);
            s.Click("PART_SaveAnyway");
            Assert.Empty(signed);
            s.Time.Advance(1);
            s.Click("PART_SaveAnyway");
            Assert.Equal([SignedChoice.SaveAnyway], signed);
        }

        var changed = new List<ChangedChoice>();
        using (var s = Show(t => ChangedOriginalCard.Create("a.pdf", changed.Add, t)))
        {
            Assert.False(s.Button("PART_Overwrite").IsEnabled);
            s.Click("PART_Overwrite");
            Assert.Empty(changed);
            s.Time.Advance(400);
            s.Click("PART_Overwrite");
            Assert.Equal([ChangedChoice.Overwrite], changed);
        }

        var locked = new List<CloseChoice>();
        using (var s = Show(t => UnsavedChangesCard.Create("a.pdf", locked.Add, locked: true, time: t)))
        {
            Assert.False(s.Button("PART_Discard").IsEnabled);
            s.Click("PART_Discard");
            Assert.Empty(locked);
            s.Time.Advance(400);
            s.Click("PART_Discard");
            Assert.Equal([CloseChoice.DontSave], locked);
        }

        using (var s = Show(t => UnsavedChangesCard.Create("a.pdf", _ => { }, time: t)))
        {
            Assert.False(s.Button("PART_DontSave").IsEnabled);
            Assert.True(s.Button("PART_Save").IsEnabled);
            Assert.True(s.Button("PART_Save").IsFocused);
        }
    }

    [AvaloniaFact]
    public void TheChangedFileCardDefaultsToSaveAs()
    {
        var changed = new List<ChangedChoice>();
        using var s = Show(t => ChangedOriginalCard.Create("a.pdf", changed.Add, t));
        Assert.Equal(PdfStrings.ChangedTitle, s.Card.Title);
        Assert.True(s.Button("PART_SaveAs").IsFocused);
        Assert.False(s.Button("PART_Overwrite").IsFocused);
        s.Click("PART_SaveAs");
        Assert.Equal([ChangedChoice.SaveAs], changed);
    }

    [AvaloniaFact]
    public void EscCancelsEveryCard()
    {
        var signed = new List<SignedChoice>();
        foreach (var advice in new[] { SignatureAdvice.WouldInvalidate, SignatureAdvice.AppendedOnly })
        {
            using var s = Show(t => SignedFileCard.Create("a.pdf", advice, signed.Add, t));
            s.Time.Advance(1000);
            s.Escape();
        }

        Assert.Equal([SignedChoice.Cancel, SignedChoice.Cancel], signed);

        var changed = new List<ChangedChoice>();
        using (var s = Show(t => ChangedOriginalCard.Create("a.pdf", changed.Add, t)))
        {
            s.Time.Advance(1000);
            s.Escape();
        }

        Assert.Equal([ChangedChoice.Cancel], changed);

        var failed = 0;
        using (var s = Show(_ => SaveFailedCard.Create("a.pdf", SaveFailureReason.DiskFull, () => failed++)))
        {
            s.Escape();
        }

        Assert.Equal(1, failed);

        var closes = new List<CloseChoice>();
        foreach (var locked in new[] { false, true })
        {
            using var s = Show(t => UnsavedChangesCard.Create("a.pdf", closes.Add, locked, t));
            s.Time.Advance(1000);
            s.Escape();
        }

        Assert.Equal([CloseChoice.Cancel, CloseChoice.Cancel], closes);
    }

    [AvaloniaFact]
    public void TheFailureCardShowsTheFileTheReasonAndOneButton()
    {
        var done = 0;
        using var s = Show(_ => SaveFailedCard.Create("a.pdf", SaveFailureReason.NotWritable, () => done++));
        Assert.Contains("a.pdf", s.Texts);
        Assert.Contains(PdfStrings.SaveFailure(SaveFailureReason.NotWritable), s.Texts);
        Assert.Equal(["PART_Ok"], s.ButtonNames);
        Assert.True(s.Button("PART_Ok").IsFocused);
        s.Click("PART_Ok");
        Assert.Equal(1, done);
    }

    [AvaloniaFact]
    public void UnsavedChangesCardForAnUnsavableTabHasOnlyCloseAndCancel()
    {
        var closes = new List<CloseChoice>();
        using var s = Show(t => UnsavedChangesCard.Create("a.pdf", closes.Add, locked: true, time: t));
        Assert.Equal(PdfStrings.UnsavedLockedTitle, s.Card.Title);
        Assert.Equal(["PART_Cancel", "PART_Discard"], s.ButtonNames);
        Assert.Equal(PdfStrings.UnsavedDiscard, s.Button("PART_Discard").Content);
        Assert.True(s.Button("PART_Cancel").IsFocused);
        s.Click("PART_Cancel");
        Assert.Equal([CloseChoice.Cancel], closes);
    }

    [Fact]
    public void EveryFailureReasonHasAnEnglishAndAGermanText()
    {
        Assert.NotEmpty(AllReasons);
        foreach (var culture in new[] { "en", "de" })
        {
            PdfStrings.Culture = CultureInfo.GetCultureInfo(culture);
            var texts = AllReasons.Select(PdfStrings.SaveFailure).ToList();
            Assert.All(texts, t => Assert.False(string.IsNullOrWhiteSpace(t)));
            Assert.Equal(texts.Count, texts.Distinct().Count());
            Assert.All(AllReasons, r => Assert.DoesNotContain(r.ToString(), PdfStrings.SaveFailure(r), StringComparison.Ordinal));
        }

        PdfStrings.Culture = CultureInfo.GetCultureInfo("en");
        Assert.Throws<ArgumentOutOfRangeException>(() => PdfStrings.SaveFailure((SaveFailureReason)(-1)));
    }

    [Fact]
    public void FailureTextsSayTheOriginalIsUnchanged()
    {
        foreach (var (culture, marker) in new[] { ("en", "unchanged"), ("de", "unverändert") })
        {
            PdfStrings.Culture = CultureInfo.GetCultureInfo(culture);
            Assert.All(AllReasons, r => Assert.Contains(marker, PdfStrings.SaveFailure(r), StringComparison.Ordinal));
        }

        PdfStrings.Culture = CultureInfo.GetCultureInfo("en");
    }

    [Fact]
    public void NoUiTextCallsAnythingLegallyBinding()
    {
        // Whole words only: the honest warning says "invalid" / "ungültig", which contain "valid" / "gültig" as a substring and must pass;
        // a claim that something is "valid" / "gültig" or "legally binding" / "rechtsverbindlich" must not.
        var banned = new Regex(@"\b(valid|gültig|legally binding|rechtsverbindlich)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        foreach (var culture in new[] { "en", "de" })
        {
            var info = CultureInfo.GetCultureInfo(culture);
            var texts = CardKeys.Select(k => PdfStrings.GetIn(k, info)).ToList();
            PdfStrings.Culture = info;
            texts.AddRange(AllReasons.Select(PdfStrings.SaveFailure));
            Assert.All(texts, t => Assert.False(banned.IsMatch(t), t));
        }

        PdfStrings.Culture = CultureInfo.GetCultureInfo("en");

        // The pattern itself flags the claims and lets the warning through.
        Assert.Matches(banned, "The signature is valid.");
        Assert.Matches(banned, "Die Signatur ist gültig.");
        Assert.DoesNotMatch(banned, "it as invalid");
        Assert.DoesNotMatch(banned, "als ungültig");
    }
}
