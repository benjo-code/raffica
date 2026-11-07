using System.Globalization;
using Avalonia.Input;
using Raffica.Shell.Localization;
using Xunit;

namespace Raffica.Shell.Tests.Localization;

public sealed class KeyNamesTests
{
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de");
    private static readonly CultureInfo En = CultureInfo.GetCultureInfo("en");

    // Fails without KeyNames: a pass-through would leave "Ctrl+K".
    [Fact]
    public void GermanNamesCtrlAsStrg()
    {
        Assert.Equal("Strg+K", KeyNames.Format("Ctrl+K", De, macOs: false));
        Assert.Equal("Strg+S", KeyNames.Format(new KeyGesture(Key.S, KeyModifiers.Control), De, macOs: false));
    }

    [Fact]
    public void GermanShift()
    {
        Assert.Equal("Umschalt+F6", KeyNames.Format("Shift+F6", De, macOs: false));
        Assert.Equal("Strg+Umschalt+E", KeyNames.Format(new KeyGesture(Key.E, KeyModifiers.Control | KeyModifiers.Shift), De, macOs: false));
    }

    [Fact]
    public void GermanDeleteEscAndEnter()
    {
        Assert.Equal("Entf", KeyNames.Format("Delete", De, macOs: false));
        Assert.Equal("Esc", KeyNames.Format("Escape", De, macOs: false));
        Assert.Equal("Alt+Eingabe", KeyNames.Format("Alt+Enter", De, macOs: false));
        Assert.Equal("Entf", KeyNames.Format(new KeyGesture(Key.Delete), De, macOs: false));
    }

    // Fails if English were routed through the German table.
    [Fact]
    public void EnglishStaysCtrl()
    {
        Assert.Equal("Ctrl+Shift+E", KeyNames.Format("Ctrl+Shift+E", En, macOs: false));
        Assert.Equal("Ctrl+K", KeyNames.Format(new KeyGesture(Key.K, KeyModifiers.Control), En, macOs: false));
        Assert.Equal("Delete", KeyNames.Format("Delete", En, macOs: false));
    }

    [Fact]
    public void MacUsesSymbols()
    {
        foreach (var culture in new[] { En, De })
        {
            Assert.Equal("⌘K", KeyNames.Format("Meta+K", culture, macOs: true));
            Assert.Equal("⇧⌘G", KeyNames.Format("Shift+Cmd+G", culture, macOs: true));
            Assert.Equal("⌥⌘X", KeyNames.Format(new KeyGesture(Key.X, KeyModifiers.Meta | KeyModifiers.Alt), culture, macOs: true));
        }
    }

    [Fact]
    public void UnknownKeyIsPassedThrough()
    {
        Assert.Equal("Strg+Banane", KeyNames.Format("Ctrl+Banane", De, macOs: false));
        Assert.Equal("F12", KeyNames.Format("F12", De, macOs: false));
        Assert.Equal("", KeyNames.Format("", De, macOs: false));
    }

    [Fact]
    public void ThePublicOverloadsUseThePlatformOfTheProcess()
    {
        var expected = OperatingSystem.IsMacOS() ? "⌃K" : "Strg+K";
        Assert.Equal(expected, KeyNames.Format("Ctrl+K", De));
        Assert.Equal(expected, KeyNames.Format(new KeyGesture(Key.K, KeyModifiers.Control), De));
    }
}
