using System.Security.Cryptography;
using System.Text;
using Raffica.Core.Hosting;
using Raffica.Core.Settings;
using Xunit;
using ShellSettingsStore = Raffica.Shell.Frame.SettingsStore;

namespace Raffica.Hosting.Tests;

public sealed class AppFilesTests
{
    private const string Given = @"C:\t\s.json";

    [Fact]
    public void PdfNamesAreUnchanged()
    {
        var pdf = AppFiles.Pdf;
        var config = UserDataPaths.ConfigDirectory();

        Assert.Equal(ShellSettingsStore.DefaultPath("Pdf"), pdf.ShellPathFor(null));
        Assert.Equal(Path.Combine(config, "pdf-settings.json"), pdf.AppSettingsPathFor(null));
        Assert.Equal(Path.Combine(config, "recent.json"), pdf.RecentPathFor(null));

        var folder = Path.GetDirectoryName(Path.GetFullPath(Given))!;
        Assert.Equal(Given, pdf.ShellPathFor(Given));
        Assert.Equal(Path.Combine(folder, "pdf-settings.json"), pdf.AppSettingsPathFor(Given));
        Assert.Equal(Path.Combine(folder, "recent.json"), pdf.RecentPathFor(Given));

        Assert.Equal(config, AppFiles.ConfigDirectoryFor(null));
        Assert.Equal(folder, AppFiles.ConfigDirectoryFor(Given));
    }

    [Fact]
    public void WriteUsesItsOwnFolderAndNames()
    {
        var write = AppFiles.Write;
        var config = UserDataPaths.ConfigDirectory();

        Assert.Equal(ShellSettingsStore.DefaultPath("Write"), write.ShellPathFor(null));
        Assert.Equal(Path.Combine(config, "write-settings.json"), write.AppSettingsPathFor(null));
        Assert.Equal(Path.Combine(config, "write-recent.json"), write.RecentPathFor(null));

        var folder = Path.GetDirectoryName(Path.GetFullPath(Given))!;
        Assert.Equal(Path.Combine(folder, "write-settings.json"), write.AppSettingsPathFor(Given));
        Assert.Equal(Path.Combine(folder, "write-recent.json"), write.RecentPathFor(Given));
    }

    [Fact]
    public void InstanceNamesDifferBetweenAppsAndBySettingsFile()
    {
        Assert.Equal(SingleInstance.DefaultName, AppFiles.Pdf.InstanceName(null));
        Assert.Equal("raffica-write-" + Environment.UserName, AppFiles.Write.InstanceName(null));

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(Given))), 0, 6);
        Assert.Equal(SingleInstance.DefaultName + "-" + hash, AppFiles.Pdf.InstanceName(Given));
        Assert.Equal("raffica-write-" + Environment.UserName + "-" + hash, AppFiles.Write.InstanceName(Given));

        Assert.NotEqual(AppFiles.Pdf.InstanceName(Given), AppFiles.Pdf.InstanceName(@"C:\t\other.json"));
        Assert.NotEqual(AppFiles.Pdf.InstanceName(Given), AppFiles.Write.InstanceName(Given));
    }

    [Fact]
    public void MalformedSettingsFileGoesToTemp()
    {
        Assert.Equal(Path.GetTempPath(), AppFiles.ConfigDirectoryFor("bad\0path"));
        Assert.Equal(Path.Combine(Path.GetTempPath(), "write-recent.json"), AppFiles.Write.RecentPathFor("bad\0path"));
        Assert.Equal(Path.Combine(Path.GetTempPath(), "pdf-settings.json"), AppFiles.Pdf.AppSettingsPathFor("bad\0path"));
    }
}
