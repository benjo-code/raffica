using System.Text.Json;
using System.Text.Json.Serialization;
using Raffica.Core.Settings;
using Raffica.Core.Viewing;

namespace Raffica.Write.Settings;

/// <summary>How a document is shown when its tab opens.</summary>
internal enum WriteView
{
    /// <summary>Pages with their margins on a desk, as printed.</summary>
    Page,

    /// <summary>One wide column of text without page chrome.</summary>
    Writing,
}

/// <summary>
/// The settings of the Write app beyond the shell's (theme, language, compact bar): the view and the zoom a tab opens with. The file is small, read once at
/// start and handed to the window; opening a tab reads no file. <see cref="Normalized"/> repairs a value that came from a hand-edited file.
/// </summary>
/// <param name="DefaultView">The view a new tab starts in.</param>
/// <param name="DefaultZoomMode">The zoom a new tab starts with: actual size, fit width or fit page (a free zoom is no default and is repaired to fit width).</param>
internal sealed record WriteSettings(WriteView DefaultView = WriteView.Page, ZoomMode DefaultZoomMode = ZoomMode.FitWidth)
{
    public static WriteSettings Default { get; } = new();

    /// <summary>The settings with every value inside its range.</summary>
    public WriteSettings Normalized() => this with
    {
        DefaultView = Enum.IsDefined(DefaultView) ? DefaultView : WriteView.Page,
        DefaultZoomMode = DefaultZoomMode is ZoomMode.ActualSize or ZoomMode.FitWidth or ZoomMode.FitPage ? DefaultZoomMode : ZoomMode.FitWidth,
    };
}

/// <summary>Source-generated JSON metadata for the settings file (no reflection, Native AOT safe).</summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(WriteSettings))]
internal sealed partial class WriteSettingsJsonContext : JsonSerializerContext
{
}

/// <summary>
/// Reads and writes the Write settings file through Core's <c>JsonFile</c> (a missing, oversized or corrupt file gives the defaults, a corrupt one is copied to
/// <c>.bad</c> once; a write goes through a temporary file; neither method throws).
/// </summary>
internal sealed class WriteSettingsStore(string path)
{
    public string FilePath { get; } = path;

    /// <summary>Loads the settings; the result is always normalized. With <paramref name="keepBackup"/> false a corrupt file is left alone.</summary>
    public WriteSettings Load(bool keepBackup = false)
    {
        var text = JsonFile.ReadText(FilePath);
        if (text is null)
        {
            return WriteSettings.Default;
        }

        try
        {
            if (JsonSerializer.Deserialize(text, WriteSettingsJsonContext.Default.WriteSettings) is { } settings)
            {
                return settings.Normalized();
            }
        }
        catch (JsonException)
        {
            // Falls through to the backup below.
        }

        if (keepBackup)
        {
            JsonFile.KeepBackup(FilePath);
        }

        return WriteSettings.Default;
    }

    /// <summary>Writes the settings; false if that fails for a file system reason (the old file stays as it was).</summary>
    public bool TrySave(WriteSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return JsonFile.TryWrite(FilePath, JsonSerializer.Serialize(settings, WriteSettingsJsonContext.Default.WriteSettings));
    }
}
