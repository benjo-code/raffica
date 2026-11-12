using Raffica.Core.Settings;
using Xunit;

namespace Raffica.Core.Tests.Settings;

public sealed class UserDataPathsTests
{
    private static Func<string, string?> Env(params (string Name, string Value)[] values) =>
        name => values.Where(v => v.Name == name).Select(v => v.Value).FirstOrDefault();

    [Fact]
    public void WindowsUsesAppData()
    {
        var path = UserDataPaths.ConfigDirectory(Env(("APPDATA", @"C:\Users\x\AppData\Roaming")), UserDataSystem.Windows);

        Assert.Equal(Path.Combine(@"C:\Users\x\AppData\Roaming", "Raffica"), path);
    }

    [Fact]
    public void LinuxPrefersXdgConfigHomeThenHome()
    {
        Assert.Equal(
            Path.Combine("/cfg", "raffica"),
            UserDataPaths.ConfigDirectory(Env(("XDG_CONFIG_HOME", "/cfg"), ("HOME", "/home/x")), UserDataSystem.Linux));
        Assert.Equal(
            Path.Combine("/home/x", ".config", "raffica"),
            UserDataPaths.ConfigDirectory(Env(("HOME", "/home/x")), UserDataSystem.Linux));
    }

    [Fact]
    public void EmptyVariableCountsAsUnset()
    {
        Assert.Equal(
            Path.Combine("/home/x", ".config", "raffica"),
            UserDataPaths.ConfigDirectory(Env(("XDG_CONFIG_HOME", ""), ("HOME", "/home/x")), UserDataSystem.Linux));
        Assert.Equal(
            Path.Combine("/home/x", ".config", "raffica"),
            UserDataPaths.ConfigDirectory(Env(("XDG_CONFIG_HOME", "  "), ("HOME", "/home/x")), UserDataSystem.Linux));
    }

    [Fact]
    public void WithoutAnyVariableTheDirectoryIsStillUsable()
    {
        Assert.False(string.IsNullOrWhiteSpace(UserDataPaths.ConfigDirectory(Env(), UserDataSystem.Windows)));
        Assert.False(string.IsNullOrWhiteSpace(UserDataPaths.ConfigDirectory(Env(), UserDataSystem.Linux)));
    }

    [Fact]
    public void MacOsUsesApplicationSupport()
    {
        Assert.Equal(
            Path.Combine("/Users/x", "Library", "Application Support", "Raffica"),
            UserDataPaths.ConfigDirectory(Env(("HOME", "/Users/x")), UserDataSystem.MacOS));
    }

    [Fact]
    public void MacOsIgnoresXdgConfigHome()
    {
        Assert.Equal(
            Path.Combine("/Users/x", "Library", "Application Support", "Raffica"),
            UserDataPaths.ConfigDirectory(Env(("XDG_CONFIG_HOME", "/cfg"), ("HOME", "/Users/x")), UserDataSystem.MacOS));
    }

    [Fact]
    public void MacOsWithoutHomeFallsBackToTemp()
    {
        var expected = Path.Combine(Path.GetTempPath(), "Raffica");

        Assert.Equal(expected, UserDataPaths.ConfigDirectory(Env(), UserDataSystem.MacOS));
        Assert.Equal(expected, UserDataPaths.ConfigDirectory(Env(("HOME", "  ")), UserDataSystem.MacOS));
    }

    [Fact]
    public void MacOsCacheIsUnderLibraryCaches()
    {
        Assert.Equal(
            Path.Combine("/Users/x", "Library", "Caches", "Raffica"),
            UserDataPaths.CacheDirectory(Env(("HOME", "/Users/x")), UserDataSystem.MacOS));
        Assert.Equal(
            Path.Combine(Path.GetTempPath(), "Raffica"),
            UserDataPaths.CacheDirectory(Env(), UserDataSystem.MacOS));
    }

    [Fact]
    public void LinuxCacheFollowsXdgCacheHome()
    {
        Assert.Equal(
            Path.Combine("/cache", "raffica"),
            UserDataPaths.CacheDirectory(Env(("XDG_CACHE_HOME", "/cache"), ("HOME", "/home/x")), UserDataSystem.Linux));
        Assert.Equal(
            Path.Combine("/home/x", ".cache", "raffica"),
            UserDataPaths.CacheDirectory(Env(("HOME", "/home/x")), UserDataSystem.Linux));
    }

    [Fact]
    public void WindowsCacheIsUnderLocalAppData()
    {
        Assert.Equal(
            Path.Combine(@"C:\Users\x\AppData\Local", "Raffica", "Cache"),
            UserDataPaths.CacheDirectory(Env(("LOCALAPPDATA", @"C:\Users\x\AppData\Local")), UserDataSystem.Windows));
        Assert.Equal(
            Path.Combine(@"C:\Users\x", "AppData", "Local", "Raffica", "Cache"),
            UserDataPaths.CacheDirectory(Env(("USERPROFILE", @"C:\Users\x")), UserDataSystem.Windows));
    }

    [Theory]
    [InlineData(UserDataSystem.Windows)]
    [InlineData(UserDataSystem.Linux)]
    [InlineData(UserDataSystem.MacOS)]
    public void EveryResultIsNonEmptyWithoutEnvironment(UserDataSystem system)
    {
        Assert.False(string.IsNullOrWhiteSpace(UserDataPaths.ConfigDirectory(Env(), system)));
        Assert.False(string.IsNullOrWhiteSpace(UserDataPaths.CacheDirectory(Env(), system)));
    }
}
