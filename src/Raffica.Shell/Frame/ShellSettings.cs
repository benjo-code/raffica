using System.Text.Json.Serialization;

namespace Raffica.Shell.Frame;

/// <summary>
/// The settings every app of the suite keeps (section 6 of the design spec): the theme, the language (<c>de</c> or <c>en</c>; null
/// follows the operating system and is what a first start has), the compact command bar and autosave (on by default).
/// </summary>
public sealed record ShellSettings(
    ThemeMode Theme = ThemeMode.System,
    string? Language = null,
    bool CompactCommandBar = false,
    bool Autosave = true);

/// <summary>The source-generated JSON metadata of <see cref="ShellSettings"/> (no reflection, safe for Native AOT); enums are written as names.</summary>
[JsonSourceGenerationOptions(UseStringEnumConverter = true, WriteIndented = true)]
[JsonSerializable(typeof(ShellSettings))]
internal sealed partial class ShellSettingsJsonContext : JsonSerializerContext;
