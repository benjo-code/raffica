using System.Diagnostics;
using System.Text;
using Raffica.Core.Saving;
using Raffica.TestSupport;
using Xunit;

namespace Raffica.Core.Tests.Saving;

public sealed class FileIdentityTests : IDisposable
{
    private readonly DirectoryInfo work = Directory.CreateTempSubdirectory("raffica-identity-tests-");

    public void Dispose() => work.Delete(recursive: true);

    private static byte[] StatBuffer(int device, ulong inode)
    {
        var buffer = new byte[144];
        BitConverter.TryWriteBytes(buffer.AsSpan(0, 4), device);
        BitConverter.TryWriteBytes(buffer.AsSpan(8, 8), inode);
        return buffer;
    }

    private string NewFile(string name)
    {
        var path = Path.Combine(work.FullName, name);
        File.WriteAllText(path, "content");
        return path;
    }

    [Fact]
    public void DarwinStatBuffersWithSameInodeAndDeviceAreEqual() =>
        Assert.Equal(FileIdentity.IdentityFromDarwinStat(StatBuffer(16777234, 4242)), FileIdentity.IdentityFromDarwinStat(StatBuffer(16777234, 4242)));

    [Fact]
    public void DarwinStatBuffersWithDifferentInodeAreDifferent() =>
        Assert.NotEqual(FileIdentity.IdentityFromDarwinStat(StatBuffer(1, 4242)), FileIdentity.IdentityFromDarwinStat(StatBuffer(1, 4243)));

    [Fact]
    public void DarwinStatBuffersWithDifferentDeviceAreDifferent() =>
        Assert.NotEqual(FileIdentity.IdentityFromDarwinStat(StatBuffer(1, 4242)), FileIdentity.IdentityFromDarwinStat(StatBuffer(2, 4242)));

    [Fact]
    public void DarwinStatBufferTooShortGivesNull() =>
        Assert.Null(FileIdentity.IdentityFromDarwinStat(new byte[15]));

    [Fact]
    public void TwoNamesOfOneFileAreTheSameFile()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix only");
        var first = NewFile("a.pdf");
        var second = Path.Combine(work.FullName, "b.pdf");
        var start = new ProcessStartInfo("ln", ["--", first, second]) { CreateNoWindow = true };
        using (var process = Process.Start(start)!)
        {
            process.WaitForExit();
            Assert.Equal(0, process.ExitCode);
        }

        Assert.True(FileIdentity.AreSameFile(first, second));
    }

    [Fact]
    public void ACopyIsAnotherFile()
    {
        var first = NewFile("a.pdf");
        var copy = Path.Combine(work.FullName, "copy.pdf");
        File.Copy(first, copy);
        Assert.False(FileIdentity.AreSameFile(first, copy));
    }

    [Fact]
    public void AMissingFileIsFalse() =>
        Assert.False(FileIdentity.AreSameFile(NewFile("a.pdf"), Path.Combine(work.FullName, "missing.pdf")));

    [Fact]
    public void MacOsAnswersAndDoesNotGiveUp()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "macOS only");
        var first = NewFile("a.pdf");
        var second = NewFile("b.pdf");
        Assert.NotNull(FileIdentity.AreSameFile(first, first));
        Assert.NotNull(FileIdentity.AreSameFile(first, second));
    }

    [Fact]
    public void OtherSpellingOfTheNameIsTheSameFile()
    {
        Assert.SkipUnless(TestVolume.IgnoresCase(work.FullName), "the volume is case-sensitive");
        var path = NewFile("Bericht.pdf");
        Assert.True(FileIdentity.AreSameFile(path, Path.Combine(work.FullName, "bericht.pdf")));
    }

    [Fact]
    public void OtherNormalizationOfTheNameIsTheSameFile()
    {
        Assert.SkipUnless(TestVolume.TreatsNfcAndNfdAsOneName(work.FullName), "the volume keeps NFC and NFD names apart");
        var nfc = "Übersicht.pdf".Normalize(NormalizationForm.FormC);
        var path = NewFile(nfc);
        Assert.True(FileIdentity.AreSameFile(path, Path.Combine(work.FullName, nfc.Normalize(NormalizationForm.FormD))));
    }
}
