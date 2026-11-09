using System.Globalization;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Raffica.Pdf.Dialogs;
using Raffica.Pdf.Tests.Text;
using Raffica.Shell.Controls;
using Xunit;

namespace Raffica.Pdf.Tests.Dialogs;

public sealed class DefaultAppCardTests : IDisposable
{
    public void Dispose() => PdfStrings.Culture = CultureInfo.GetCultureInfo("en");

    private sealed class Shown(Window window, Dialog card, ManualTime time) : IDisposable
    {
        public Dialog Card { get; } = card;

        public ManualTime Time { get; } = time;

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

    private static Shown Show(string culture, Action<DefaultAppChoice> chosen)
    {
        PdfStrings.Culture = CultureInfo.GetCultureInfo(culture);
        var time = new ManualTime();
        var card = DefaultAppCard.Create(chosen, time);
        var host = new DialogHost { Content = new Border() };
        var window = new Window { Content = host, Width = 700, Height = 400 };
        window.Show();
        host.Show(card);
        Pump();
        return new Shown(window, card, time);
    }

    [AvaloniaFact]
    public void SetAsDefaultAndDontAskAgainWaitForTheArmingDelayNotNowDoesNot()
    {
        var results = new List<DefaultAppChoice>();
        using var s = Show("en", results.Add);
        Assert.Equal(PdfStrings.DefaultAppSet, s.Button("PART_Set").Content);
        Assert.Contains("primary", s.Button("PART_Set").Classes);
        Assert.DoesNotContain("primary", s.Button("PART_DontAsk").Classes);
        Assert.False(s.Button("PART_Set").IsEnabled);
        Assert.False(s.Button("PART_DontAsk").IsEnabled);
        Assert.True(s.Button("PART_NotNow").IsEnabled);

        // A click that reaches the buttons early (a script, a held key) is no more able to confirm than the pointer.
        s.Click("PART_Set");
        s.Click("PART_DontAsk");
        Assert.Empty(results);

        s.Time.Advance(ArmingLock.DelayMilliseconds - 1);
        s.Click("PART_Set");
        Assert.Empty(results);

        s.Time.Advance(1);
        s.Click("PART_Set");
        s.Click("PART_DontAsk");
        Assert.Equal([DefaultAppChoice.SetDefault], results);
    }

    [AvaloniaFact]
    public void NotNowAndEscapeAreAlwaysPossibleAndAnswerOnce()
    {
        var results = new List<DefaultAppChoice>();
        using (var s = Show("en", results.Add))
        {
            s.Click("PART_NotNow");
            s.Click("PART_NotNow");
        }

        Assert.Equal([DefaultAppChoice.NotNow], results);

        results.Clear();
        using (var s = Show("de", results.Add))
        {
            s.Escape();
        }

        Assert.Equal([DefaultAppChoice.NotNow], results);
    }

    [AvaloniaFact]
    public void TheCardIsWordedInBothLanguages()
    {
        using var en = Show("en", _ => { });
        Assert.Equal("Set as default", en.Button("PART_Set").Content);
        Assert.Equal("Don't ask again", en.Button("PART_DontAsk").Content);
        using var de = Show("de", _ => { });
        Assert.Equal("Als Standard festlegen", de.Button("PART_Set").Content);
        Assert.Equal("Nicht mehr fragen", de.Button("PART_DontAsk").Content);
    }
}
