using System.Text.Json;

namespace Raffica.Shell.Frame;

/// <summary>
/// Reads and writes the <see cref="ShellSettings"/> of an app as JSON. The settings are never a reason for the app not to start:
/// a missing, empty, unreadable, oversized or corrupt file gives the defaults and no exception, and a failing write is reported
/// as <c>false</c> by <see cref="TrySave"/>.
/// </summary>
public sealed class SettingsStore(string path)
{
    /// <summary>A real settings file is a few hundred bytes; anything larger is not ours and is not read.</summary>
    private const long MaxBytes = 64 * 1024;

    public string Path { get; } = path ?? throw new ArgumentNullException(nameof(path));

    /// <summary>The settings file of an app: <c>Raffica/&lt;appName&gt;/settings.json</c> in the application-data folder of the user.</summary>
    public static string DefaultPath(string appName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appName);
        return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Raffica", appName, "settings.json");
    }

    /// <summary>The stored settings, or the defaults if the file is missing, empty, unreadable or corrupt.</summary>
    public ShellSettings Load()
    {
        try
        {
            var info = new FileInfo(Path);
            if (!info.Exists || info.Length == 0 || info.Length > MaxBytes)
            {
                return new ShellSettings();
            }

            var settings = JsonSerializer.Deserialize(File.ReadAllBytes(Path), ShellSettingsJsonContext.Default.ShellSettings);
            if (settings is null)
            {
                return new ShellSettings();
            }

            // An unknown language code would throw in Languages.Resolve at startup: treat it as "follow the system".
            return settings with
            {
                Language = settings.Language is "de" or "en" ? settings.Language : null,
                Theme = Enum.IsDefined(settings.Theme) ? settings.Theme : ThemeMode.System,
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException or ArgumentException or InvalidOperationException)
        {
            return new ShellSettings();
        }
    }

    /// <summary>Writes the settings to a temporary file next to the target and moves it over the target, so a failed write keeps the old file.</summary>
    /// <exception cref="IOException">The file could not be written.</exception>
    public void Save(ShellSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temp = Path + ".tmp";
        try
        {
            File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(settings, ShellSettingsJsonContext.Default.ShellSettings));
            File.Move(temp, Path, overwrite: true);
        }
        finally
        {
            try
            {
                File.Delete(temp);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The leftover is harmless; the next save overwrites it.
            }
        }
    }

    /// <summary>Like <see cref="Save"/>, but an IO error is swallowed and reported as <c>false</c>.</summary>
    public bool TrySave(ShellSettings settings)
    {
        try
        {
            Save(settings);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return false;
        }
    }
}
