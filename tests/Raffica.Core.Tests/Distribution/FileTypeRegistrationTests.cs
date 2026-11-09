using Raffica.Core.Distribution;
using Xunit;

namespace Raffica.Core.Tests.Distribution;

public sealed class FileTypeRegistrationTests
{
    private const string Exe = @"C:\Apps\Raffica\Raffica.exe";

    [Fact]
    public void EntriesHaveTheOpenCommandForTheExe()
    {
        var entries = FileTypeRegistration.Entries(Exe);

        var progId = Assert.Single(entries, e => e.KeyPath == @"Software\Classes\Raffica.PDF\shell\open\command");
        Assert.Null(progId.ValueName);
        Assert.Equal("\"C:\\Apps\\Raffica\\Raffica.exe\" \"%1\"", progId.Value);
        Assert.Contains(entries, e => e.KeyPath == @"Software\Classes\Applications\RafficaPdf.exe\shell\open\command" && e.Value == progId.Value);
        Assert.Contains(entries, e => e.KeyPath == @"Software\Classes\Raffica.PDF\DefaultIcon" && e.Value == "\"C:\\Apps\\Raffica\\Raffica.exe\",0");
        Assert.Contains(entries, e => e.KeyPath == @"Software\Classes\.pdf\OpenWithProgids" && e.ValueName == "Raffica.PDF" && e.Value.Length == 0);
        Assert.Contains(entries, e => e.KeyPath == @"Software\Raffica\Capabilities" && e.ValueName == "ApplicationName");
        Assert.Contains(entries, e => e.KeyPath == @"Software\Raffica\Capabilities" && e.ValueName == "ApplicationDescription");
        Assert.Contains(entries, e => e.KeyPath == @"Software\Raffica\Capabilities\FileAssociations" && e.ValueName == ".pdf" && e.Value == "Raffica.PDF");
        Assert.Contains(entries, e => e.KeyPath == @"Software\RegisteredApplications" && e.ValueName == "Raffica" && e.Value == @"Software\Raffica\Capabilities");
    }

    [Fact]
    public void CommandQuotesAPathWithSpacesAndUmlauts()
    {
        var entries = FileTypeRegistration.Entries(@"C:\Program Files\Raffica Test ä&\Raffica.exe");

        var command = Assert.Single(entries, e => e.KeyPath == @"Software\Classes\Raffica.PDF\shell\open\command");
        Assert.Equal("\"C:\\Program Files\\Raffica Test ä&\\Raffica.exe\" \"%1\"", command.Value);
    }

    [Fact]
    public void EveryEntryHasARemoval()
    {
        var removals = FileTypeRegistration.Removals();

        foreach (var entry in FileTypeRegistration.Entries(Exe))
        {
            var covered = removals.Any(r => r.ValueName is null
                ? entry.KeyPath.Equals(r.KeyPath, StringComparison.OrdinalIgnoreCase)
                    || entry.KeyPath.StartsWith(r.KeyPath + "\\", StringComparison.OrdinalIgnoreCase)
                : entry.KeyPath.Equals(r.KeyPath, StringComparison.OrdinalIgnoreCase) && entry.ValueName == r.ValueName);
            Assert.True(covered, $"{entry.KeyPath} / {entry.ValueName} is not removed");
        }
    }

    [Fact]
    public void NothingTouchesTheUserChoice()
    {
        var paths = FileTypeRegistration.Entries(Exe).Select(e => e.KeyPath)
            .Concat(FileTypeRegistration.Removals().Select(r => r.KeyPath));

        Assert.DoesNotContain(paths, p => p.Contains("UserChoice", StringComparison.OrdinalIgnoreCase)
            || p.Contains("FileExts", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CommandQuotesAPathWithSpacesAndNonAscii()
    {
        const string Path = @"C:\Users\Zoë Müller\AppData\Local\Programs\Raffica PDF\RafficaPdf.exe";

        var command = Assert.Single(FileTypeRegistration.Entries(Path), e => e.KeyPath == @"Software\Classes\Raffica.PDF\shell\open\command");

        Assert.StartsWith("\"" + Path + "\"", command.Value, StringComparison.Ordinal);
        Assert.EndsWith("\"%1\"", command.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplicationKeyNamesTheRealExecutable()
    {
        var entries = FileTypeRegistration.Entries(@"C:\Apps\Raffica PDF\RafficaPdf.exe");

        Assert.Contains(entries, e => e.KeyPath == @"Software\Classes\Applications\RafficaPdf.exe\shell\open\command");
        Assert.DoesNotContain(entries, e => e.KeyPath.Contains(@"Applications\Raffica.exe", StringComparison.Ordinal));
        Assert.Contains(FileTypeRegistration.Removals(), r => r.KeyPath == @"Software\Classes\Applications\RafficaPdf.exe");
        Assert.Contains(entries, e => e.Value == "Raffica PDF Document");
    }
}
