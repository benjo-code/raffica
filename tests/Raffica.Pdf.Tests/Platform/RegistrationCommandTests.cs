using Raffica.Core.Distribution;
using Raffica.Pdf.Platform;
using Xunit;

namespace Raffica.Pdf.Tests.Platform;

public sealed class RegistrationCommandTests
{
    private const string Exe = @"C:\Users\Zoë Müller\AppData\Local\Programs\Raffica PDF\RafficaPdf.exe";

    private sealed class FakeWriter : IRegistryWriter
    {
        public List<(RegistrationScope Scope, IReadOnlyList<RegistryEntry> Entries)> Writes { get; } = [];

        public List<(RegistrationScope Scope, IReadOnlyList<RegistryRemoval> Removals)> Removes { get; } = [];

        public bool Fail { get; init; }

        public void Write(RegistrationScope scope, IReadOnlyList<RegistryEntry> entries)
        {
            if (Fail)
            {
                throw new UnauthorizedAccessException("denied");
            }

            Writes.Add((scope, entries));
        }

        public void Remove(RegistrationScope scope, IReadOnlyList<RegistryRemoval> removals)
        {
            if (Fail)
            {
                throw new UnauthorizedAccessException("denied");
            }

            Removes.Add((scope, removals));
        }
    }

    [Fact]
    public void RegisterWritesEntriesAndExitsZeroWithoutAWindow()
    {
        var writer = new FakeWriter();

        var code = RegistrationCommand.Run(PdfOptions.Parse(["--register-file-types=machine"]), writer, Exe);

        Assert.Equal(0, code);
        var write = Assert.Single(writer.Writes);
        Assert.Equal(RegistrationScope.Machine, write.Scope);
        Assert.Equal(FileTypeRegistration.Entries(Exe), write.Entries);
        Assert.Empty(writer.Removes);
        Assert.DoesNotContain(write.Entries, e => e.KeyPath.Contains("UserChoice", StringComparison.OrdinalIgnoreCase)
            || e.KeyPath.Contains("FileExts", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void UnregisterRemovesAndExitsZero()
    {
        var writer = new FakeWriter();

        var code = RegistrationCommand.Run(PdfOptions.Parse(["--unregister-file-types=user"]), writer, Exe);

        Assert.Equal(0, code);
        var remove = Assert.Single(writer.Removes);
        Assert.Equal(RegistrationScope.User, remove.Scope);
        Assert.Equal(FileTypeRegistration.Removals(), remove.Removals);
        Assert.Empty(writer.Writes);
    }

    [Fact]
    public void AFailingWriterExitsTwo()
    {
        var code = RegistrationCommand.Run(PdfOptions.Parse(["--register-file-types=user"]), new FakeWriter { Fail = true }, Exe);

        Assert.Equal(2, code);
        Assert.Equal(2, RegistrationCommand.Run(PdfOptions.Parse(["--unregister-file-types=user"]), new FakeWriter { Fail = true }, Exe));
    }

    [Theory]
    [InlineData("--register-file-types=everyone")]
    [InlineData("--unregister-file-types=")]
    [InlineData("--register-file-types")]
    public void AnUnknownScopeIsAUsageError(string arg)
    {
        Assert.Throws<ArgumentException>(() => PdfOptions.Parse([arg]));
    }

    [Fact]
    public void RegisterAndUnregisterTogetherAreAUsageError()
    {
        Assert.Throws<ArgumentException>(() => PdfOptions.Parse(["--register-file-types=user", "--unregister-file-types=user"]));
    }

    [Fact]
    public void WithoutASwitchThereIsNothingToRun()
    {
        Assert.Equal(0, RegistrationCommand.Run(PdfOptions.Parse([]), new FakeWriter(), Exe));
    }
}
