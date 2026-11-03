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
        Assert.Contains("StartupWMClass=Raffica", lines);
        Assert.Contains("Icon=raffica", lines);
        Assert.Contains("Exec=/opt/Raffica %F", lines);
        Assert.EndsWith("\n", DesktopEntry.Render("/opt/Raffica", "raffica"), StringComparison.Ordinal);
    }

    [Fact]
    public void RenderIsTheSameForTheTemplateInPackaging()
    {
        var template = File.ReadAllText(Path.Combine(TestData.Root, "packaging", "linux", "raffica.desktop")).Replace("\r\n", "\n", StringComparison.Ordinal);
        var rendered = DesktopEntry.Render("/usr/lib/raffica/Raffica", "raffica");

        Assert.Equal("Exec=/usr/lib/raffica/Raffica %F", template.Split('\n').Single(line => line.StartsWith("Exec=", StringComparison.Ordinal)));
        Assert.Equal(WithoutExec(rendered), WithoutExec(template));
        Assert.Equal(template, rendered);
    }

    [Fact]
    public void WindowClassIsTheOneAvaloniaSetsOnX11()
    {
        // Verified with xprop on the Native AOT build under Xvfb: WM_CLASS(STRING) = "Raffica", "Raffica".
        Assert.Contains("StartupWMClass=Raffica", DesktopEntry.Render("/opt/Raffica", "raffica").Split('\n'));
    }

    private static string WithoutExec(string text) =>
        string.Join('\n', text.Split('\n').Where(line => !line.StartsWith("Exec=", StringComparison.Ordinal)));
}
