using Raffica.Core.Distribution;
using Raffica.TestSupport;
using Xunit;

namespace Raffica.Core.Tests.Distribution;

public sealed class DesktopEntryTests
{
    [Fact]
    public void ExecEscapesSpacesAndPercent()
    {
        var text = DesktopEntry.Render("/home/a b/Raffica%1.AppImage", "raffica");

        Assert.Contains("Exec=\"/home/a b/Raffica%%1.AppImage\" %F", text.Split('\n'));
    }

    [Fact]
    public void ExecLeavesAPlainPathAlone()
    {
        Assert.Equal("/opt/raffica/Raffica", DesktopEntry.EscapeExec("/opt/raffica/Raffica"));
    }

    [Fact]
    public void ExecEscapesQuotesDollarAndBackticksInsideQuotes()
    {
        Assert.Equal("\"/a b/\\\"x\\\"\\$y\\`z\"", DesktopEntry.EscapeExec("/a b/\"x\"$y`z"));
    }

    [Theory]
    [InlineData("/a/b\nExec=/tmp/evil")]
    [InlineData("/a/b\rX-Other=1")]
    [InlineData("/a/b\u0000c")]
    [InlineData("/a/\u007Fb")]
    public void ExecRefusesAPathWithAControlCharacterInsteadOfStartingAnotherLine(string path)
    {
        // A line break inside Exec= would end the line and let the rest of the name become another key of the entry.
        Assert.Throws<ArgumentException>(() => DesktopEntry.EscapeExec(path));
        Assert.Throws<ArgumentException>(() => DesktopEntry.Render(path, "raffica"));
    }

    [Theory]
    [InlineData("raffica\nExec=/tmp/evil")]
    [InlineData("raffica\r")]
    public void IconNameRefusesAControlCharacter(string icon)
    {
        Assert.Throws<ArgumentException>(() => DesktopEntry.Render("/opt/Raffica", icon));
    }

    [Fact]
    public void RenderHasMimeTypeIconAndWmClass()
    {
        var lines = DesktopEntry.Render("/opt/Raffica", "raffica").Split('\n');

        Assert.Contains("[Desktop Entry]", lines);
        Assert.Contains("MimeType=application/pdf;", lines);
        Assert.Contains("StartupWMClass=RafficaPdf", lines);
        Assert.Contains("Icon=raffica", lines);
        Assert.Contains("Exec=/opt/Raffica %F", lines);
        Assert.EndsWith("\n", DesktopEntry.Render("/opt/Raffica", "raffica"), StringComparison.Ordinal);
    }

    [Fact]
    public void WindowClassIsTheOneAvaloniaSetsOnX11()
    {
        // Open item: the class is checked with xprop WM_CLASS on a Linux desktop (M2.6 Task 6); RafficaPdf is the assembly name.
        Assert.Contains("StartupWMClass=RafficaPdf", DesktopEntry.Render("/opt/Raffica", "raffica").Split('\n'));
    }

    [Fact]
    public void NameIsRafficaPdf()
    {
        Assert.Contains("Name=Raffica PDF", DesktopEntry.Render("/opt/RafficaPdf", "raffica-pdf").Split('\n'));
    }
}
