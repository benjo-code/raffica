using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Raffica.Core.Text;
using Raffica.Pdf.Dialogs;
using Raffica.Pdf.Tests.Viewing;
using Raffica.Pdf.Text;
using Raffica.Shell.Controls;
using Xunit;

namespace Raffica.Pdf.Tests.Text;

public sealed class LinkCardTests
{
    private const string Docs = "https://example.org/docs";

    private static PageLink UriLink(string url) => new([new PageRect(0, 0, 10, 10)], new UriTarget(url));

    private static async Task<PageLink> LinkOfAsync(TextFixture f, Func<LinkTarget, bool> match) =>
        (await f.TextOfAsync(0)).Links.First(l => match(l.Target));

    private static Button Button(Dialog card, string name) => card.GetVisualDescendants().OfType<Button>().Single(b => b.Name == name);

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));

    private static Dialog Card(TextFixture f)
    {
        ViewFixture.Pump();
        return Assert.IsType<Dialog>(f.Host.ActiveDialog);
    }

    [AvaloniaFact]
    public async Task AnExternalLinkOpensOnlyAfterConfirmation()
    {
        using var f = await TextFixture.OpenAsync("text-rich.pdf");
        var link = await LinkOfAsync(f, t => t is UriTarget { Url: Docs });
        var action = LinkController.Resolve(link, f.View.PageCount);

        var task = f.Text.Links.ActivateAsync(action!);
        var card = Card(f);

        // Nothing opened yet; the card names the address in full and exactly.
        Assert.Empty(f.Launched);
        var texts = card.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains(Docs, texts);
        Assert.False(Button(card, "PART_Open").IsEnabled);

        f.Time.Advance(LinkCard.ArmingDelayMilliseconds);
        Click(Button(card, "PART_Open"));
        await task.WaitAsync(TimeSpan.FromSeconds(10));
        ViewFixture.Pump();

        var launched = Assert.Single(f.Launched);
        Assert.Equal(Docs, launched.OriginalString);
        Assert.False(f.Host.IsOpen);
    }

    [AvaloniaFact]
    public async Task CancelAndEscapeNeverOpen()
    {
        using var f = await TextFixture.OpenAsync("text-rich.pdf");
        var link = await LinkOfAsync(f, t => t is UriTarget { Url: Docs });
        var action = LinkController.Resolve(link, f.View.PageCount)!;

        var first = f.Text.Links.ActivateAsync(action);
        var card = Card(f);
        f.Time.Advance(10_000);
        Click(Button(card, "PART_Cancel"));
        await first.WaitAsync(TimeSpan.FromSeconds(10));

        var second = f.Text.Links.ActivateAsync(action);
        Card(f);
        f.Time.Advance(10_000);
        f.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        await second.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(f.Host.IsOpen);

        // Enter on the armed card acts on the button that has the focus: that is Cancel, never Open.
        var third = f.Text.Links.ActivateAsync(action);
        Card(f);
        f.Time.Advance(10_000);
        f.Window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        ViewFixture.Pump();
        await third.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Empty(f.Launched);
    }

    [AvaloniaFact]
    public async Task OpenIsIgnoredDuringTheArmingDelay()
    {
        using var f = await TextFixture.OpenAsync("text-rich.pdf");
        var link = await LinkOfAsync(f, t => t is UriTarget { Url: Docs });
        var task = f.Text.Links.ActivateAsync(LinkController.Resolve(link, f.View.PageCount)!);
        var card = Card(f);
        var open = Button(card, "PART_Open");

        Click(open);
        f.Time.Advance(LinkCard.ArmingDelayMilliseconds - 1);
        Click(open);
        Assert.Empty(f.Launched);
        Assert.True(f.Host.IsOpen);

        f.Time.Advance(1);
        Click(open);
        await task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Single(f.Launched);
    }

    [AvaloniaFact]
    public async Task ADoubleClickNeverReachesOpen()
    {
        using var f = await TextFixture.OpenAsync("text-rich.pdf");
        var text = await f.TextOfAsync(0);
        var link = text.Links.First(l => l.Target is UriTarget { Url: Docs });
        var rect = link.Rects[0];
        var point = f.View.ToView(0, (rect.Left + rect.Right) / 2, (rect.Top + rect.Bottom) / 2)!.Value;

        // The first click opens the card; the second one lands on the card's scrim and not on a second card.
        f.Window.MouseDown(point, MouseButton.Left);
        f.Window.MouseUp(point, MouseButton.Left);
        var card = Card(f);
        f.Window.MouseDown(point, MouseButton.Left);
        f.Window.MouseUp(point, MouseButton.Left);
        ViewFixture.Pump();
        Assert.Same(card, f.Host.ActiveDialog);
        Assert.Empty(f.Launched);

        // Even a double click on the Open button itself does nothing within the delay.
        var open = Button(card, "PART_Open");
        Click(open);
        f.Time.Advance(100);
        Click(open);
        Assert.Empty(f.Launched);

        Click(Button(card, "PART_Cancel"));
        ViewFixture.Pump();
        Assert.False(f.Host.IsOpen);
        Assert.Empty(f.Launched);
    }

    [AvaloniaFact]
    public async Task ALinkClickOpensTheCardOnceAndADoubleClickOnTheLinkOpensNoSecondOne()
    {
        using var f = await TextFixture.OpenAsync("text-rich.pdf");
        var text = await f.TextOfAsync(0);
        var rect = text.Links.First(l => l.Target is UriTarget { Url: Docs }).Rects[0];
        var point = f.View.ToView(0, (rect.Left + rect.Right) / 2, (rect.Top + rect.Bottom) / 2)!.Value;

        f.Window.MouseDown(point, MouseButton.Left);
        f.Window.MouseUp(point, MouseButton.Left);
        Card(f);
        Click(Button((Dialog)f.Host.ActiveDialog!, "PART_Cancel"));
        ViewFixture.Pump();
        Assert.False(f.Host.IsOpen);

        // A press that is a drag (the pointer moved off the link before the release) is no click.
        f.Window.MouseDown(point, MouseButton.Left);
        f.Window.MouseMove(new Point(point.X + 60, point.Y + 60));
        f.Window.MouseUp(new Point(point.X + 60, point.Y + 60), MouseButton.Left);
        ViewFixture.Pump();
        Assert.False(f.Host.IsOpen);
    }

    [AvaloniaTheory]
    [InlineData("javascript:alert(1)")]
    [InlineData("JaVaScRiPt:alert(1)")]
    [InlineData("file:///C:/Windows/System32/cmd.exe")]
    [InlineData("ftp://example.org/x")]
    [InlineData("data:text/html,<script>1</script>")]
    [InlineData("vbscript:msgbox(1)")]
    [InlineData("\\\\server\\share\\x.exe")]
    [InlineData("C:\\Windows\\System32\\cmd.exe")]
    [InlineData("https://example.org/a b")]
    [InlineData("https://example.org/\"quoted\"")]
    [InlineData("")]
    public void DangerousTargetsAreNotClickable(string url)
    {
        Assert.Null(LinkController.Resolve(UriLink(url), 10));
    }

    [AvaloniaFact]
    public void ATooLongAddressIsNotClickable()
    {
        Assert.Null(LinkController.Resolve(UriLink("https://example.org/" + new string('a', 5000)), 10));
        Assert.Null(LinkController.Resolve(UriLink("https://example.org/" + new string('a', ExternalLink.MaxLength)), 10));
        Assert.NotNull(LinkController.Resolve(UriLink("https://example.org/" + new string('a', 200)), 10));
    }

    [AvaloniaFact]
    public async Task DeceptiveAddressesWarnOnTheCardAndOpenExactlyAsShown()
    {
        using var f = await TextFixture.OpenAsync("text-rich.pdf");
        (string Url, ExternalLinkWarning Warning)[] cases =
        [
            ("https://xn--80ak6aa92e.com/login", ExternalLinkWarning.Punycode),
            ("https://www.example.org@evil.example/login", ExternalLinkWarning.UserInfo),
            ("https://example.org/ok", ExternalLinkWarning.None),
        ];
        foreach (var (url, warning) in cases)
        {
            var action = LinkController.Resolve(UriLink(url), 10);
            var external = Assert.IsType<OpenExternal>(action);
            Assert.Equal(warning, external.Link.Warning);

            var task = f.Text.Links.ActivateAsync(action);
            var card = Card(f);
            var shown = card.GetVisualDescendants().OfType<TextBlock>().ToList();
            Assert.Contains(url, shown.Select(t => t.Text));
            var warningText = shown.SingleOrDefault(t => t.Name == "PART_Warning");
            Assert.Equal(warning != ExternalLinkWarning.None, warningText is { IsVisible: true });
            if (warning != ExternalLinkWarning.None)
            {
                Assert.False(string.IsNullOrWhiteSpace(warningText!.Text));
            }

            f.Time.Advance(LinkCard.ArmingDelayMilliseconds);
            Click(Button(card, "PART_Open"));
            await task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(url, f.Launched[^1].OriginalString);
        }

        Assert.Equal(3, f.Launched.Count);
    }

    [AvaloniaFact]
    public async Task ThePunycodeWarningNamesTheDecodedDomainInBothLanguages()
    {
        using var f = await TextFixture.OpenAsync("text-rich.pdf");
        foreach (var language in new[] { "en", "de" })
        {
            PdfStrings.Culture = System.Globalization.CultureInfo.GetCultureInfo(language);
            var action = LinkController.Resolve(UriLink("https://xn--80ak6aa92e.com/"), 10)!;
            var task = f.Text.Links.ActivateAsync(action);
            var card = Card(f);
            var warning = card.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Warning");
            Assert.Contains(((OpenExternal)action).Link.UnicodeHost, warning.Text, StringComparison.Ordinal);
            Click(Button(card, "PART_Cancel"));
            await task.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [AvaloniaFact]
    public async Task AMailAddressIsClickable()
    {
        using var f = await TextFixture.OpenAsync("text-rich.pdf");
        var action = LinkController.Resolve(UriLink("mailto:someone@example.org?subject=Hello"), 10);
        Assert.IsType<OpenExternal>(action);
        var task = f.Text.Links.ActivateAsync(action!);
        var card = Card(f);
        f.Time.Advance(LinkCard.ArmingDelayMilliseconds);
        Click(Button(card, "PART_Open"));
        await task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("mailto:someone@example.org?subject=Hello", Assert.Single(f.Launched).OriginalString);
    }

    [AvaloniaFact]
    public async Task InternalLinkJumpsWithoutTheLauncher()
    {
        using var f = await TextFixture.OpenAsync("text-rich.pdf", 800, 500);
        var link = await LinkOfAsync(f, t => t is PageTarget);
        var action = Assert.IsType<PageJump>(LinkController.Resolve(link, f.View.PageCount));
        Assert.Equal(1, action.Page);
        Assert.Equal(250, action.Top);
        Assert.Equal(0, f.View.CurrentPage);

        await f.Text.Links.ActivateAsync(action);
        ViewFixture.Pump();

        Assert.Equal(1, f.View.CurrentPage);
        Assert.Empty(f.Launched);
        Assert.False(f.Host.IsOpen);
    }

    [AvaloniaFact]
    public void AnInternalLinkToAMissingPageIsNotClickable()
    {
        Assert.Null(LinkController.Resolve(new PageLink([new PageRect(0, 0, 1, 1)], new PageTarget(5, null)), 2));
        Assert.Null(LinkController.Resolve(new PageLink([new PageRect(0, 0, 1, 1)], new PageTarget(-1, null)), 2));
        Assert.NotNull(LinkController.Resolve(new PageLink([new PageRect(0, 0, 1, 1)], new PageTarget(1, null)), 2));
    }

    [AvaloniaFact]
    public async Task OnlyTheValidatedAddressesReachTheDefaultLauncherAndNothingIsRunWithoutAClick()
    {
        // The default launcher is the system one; here only its own gate is checked, nothing is started.
        Assert.Null(SystemLauncher.CreateStartInfo(new Uri("javascript:alert(1)")));
        Assert.Null(SystemLauncher.CreateStartInfo(new Uri("file:///C:/x.exe")));
        Assert.Null(SystemLauncher.CreateStartInfo(new Uri("ftp://example.org/")));
        Assert.Null(SystemLauncher.CreateStartInfo(new Uri("https://example.org/a b", UriKind.Absolute)));

        var info = SystemLauncher.CreateStartInfo(new Uri(Docs))!;
        if (OperatingSystem.IsWindows())
        {
            Assert.True(info.UseShellExecute);
            Assert.Equal(Docs, info.FileName);
            Assert.Empty(info.Arguments);
        }
        else
        {
            Assert.False(info.UseShellExecute);
            Assert.Equal(Docs, Assert.Single(info.ArgumentList));
            Assert.Empty(info.Arguments);
        }

        // An address with characters that a shell would read as syntax stays one argument: the shape of the address is the gate.
        Assert.Null(SystemLauncher.CreateStartInfo(new Uri("https://example.org/x\" --some-flag \"")));
        var tricky = SystemLauncher.CreateStartInfo(new Uri("https://example.org/x?a=1&b=2;c=$(id)"));
        Assert.NotNull(tricky);
        Assert.Contains("$(id)", tricky!.UseShellExecute ? tricky.FileName : tricky.ArgumentList.Single(), StringComparison.Ordinal);
        await Task.CompletedTask;
    }

    [AvaloniaFact]
    public async Task TheCursorIsAHandOverALinkAnIBeamOverTextAndTheDefaultElsewhere()
    {
        using var f = await TextFixture.OpenAsync("text-rich.pdf");
        var text = await f.TextOfAsync(0);
        var rect = text.Links.First(l => l.Target is UriTarget { Url: Docs }).Rects[0];
        var onLink = f.View.ToView(0, (rect.Left + rect.Right) / 2, (rect.Top + rect.Bottom) / 2)!.Value;
        var start = text.GetText(0, text.Chars.Count).IndexOf("Handbuch", StringComparison.Ordinal);
        var onText = f.CharPoint(text, start + 2, right: false);

        f.Window.MouseMove(onLink);
        Assert.Same(TextInteraction.HandCursor, f.View.Cursor);
        f.Window.MouseMove(onText);
        Assert.Same(TextInteraction.IBeamCursor, f.View.Cursor);
        f.Window.MouseMove(new Point(2, 2));
        Assert.Null(f.View.Cursor);
    }
}
