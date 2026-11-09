using System.Text;
using Raffica.Cli;
using Xunit;

namespace Raffica.Core.Tests.Cli;

public sealed class ConsoleOutputTests
{
    [Theory]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    public void OnlyAnUnredirectedWindowsConsoleUsesTheConsoleWriter(bool isWindows, bool isRedirected, bool expected) =>
        Assert.Equal(expected, ConsoleOutput.UsesConsoleWriter(isWindows, isRedirected));

    [Fact]
    public void RedirectedOutputStaysTheRawStream()
    {
        using var raw = new MemoryStream();
        var consoleOpened = false;

        using var stream = ConsoleOutput.Open(true, true, () => raw, () =>
        {
            consoleOpened = true;
            return new StringWriter();
        });

        Assert.Same(raw, stream);
        Assert.False(consoleOpened);
    }

    [Fact]
    public async Task AWindowsConsoleGetsTheTextThroughTheWriterEvenWhenACharacterIsSplit()
    {
        var console = new StringWriter();
        var bytes = new UTF8Encoding(false).GetBytes("Grüße 日本 😀\n");
        await using var stream = ConsoleOutput.Open(true, false, () => throw new InvalidOperationException("raw stream"), () => console);

        // One byte at a time: every multi-byte character is split between writes.
        foreach (var b in bytes)
        {
            await stream.WriteAsync(new[] { b }, TestContext.Current.CancellationToken);
        }

        await stream.FlushAsync(TestContext.Current.CancellationToken);

        Assert.Equal("Grüße 日本 😀\n", console.ToString());
    }
}
