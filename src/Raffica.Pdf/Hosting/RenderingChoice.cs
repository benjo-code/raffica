namespace Raffica.Pdf.Hosting;

/// <summary>Which Avalonia rendering path the app asks for. On Windows software is the app's default and <c>Default</c> the opt-out; on macOS software is an opt-in.</summary>
internal enum RenderingChoice
{
    /// <summary>Avalonia's own default path (ANGLE over Direct3D 11, GPU compositing): the opt-out, <c>RAFFICA_RENDERING=default</c>.</summary>
    Default,

    /// <summary>Software rendering as the only mode (<c>Win32RenderingMode.Software</c>): the app's default on Windows.</summary>
    Software,
}

/// <summary>The platform families the rendering choice tells apart.</summary>
internal enum RenderingPlatform
{
    /// <summary>Windows: software is the default.</summary>
    Windows,

    /// <summary>macOS: Avalonia's own path is the default, software is asked for.</summary>
    MacOS,

    /// <summary>Linux: the variable has no effect.</summary>
    Linux,

    /// <summary>Anything else: the variable has no effect.</summary>
    Other,
}

/// <summary>Reads the opt-out <c>RAFFICA_RENDERING</c>.</summary>
internal static class RenderingChoiceParser
{
    /// <summary>The name of the environment variable.</summary>
    public const string VariableName = "RAFFICA_RENDERING";

    /// <summary>
    /// <c>default</c> (trimmed, any case) asks for Avalonia's own default path; <see langword="null"/>, empty, <c>software</c> and
    /// anything else give software rendering, the app's default on Windows.
    /// </summary>
    public static RenderingChoice Parse(string? value) =>
        value is not null && string.Equals(value.Trim(), "default", StringComparison.OrdinalIgnoreCase)
            ? RenderingChoice.Default
            : RenderingChoice.Software;

    /// <summary>The platform family of the running system.</summary>
    public static RenderingPlatform CurrentPlatform =>
        OperatingSystem.IsWindows() ? RenderingPlatform.Windows
        : OperatingSystem.IsMacOS() ? RenderingPlatform.MacOS
        : OperatingSystem.IsLinux() ? RenderingPlatform.Linux
        : RenderingPlatform.Other;

    /// <summary>
    /// The choice for a platform. Windows follows <see cref="Parse"/> (software unless <c>default</c>). macOS gives software only for
    /// <c>software</c> (trimmed, any case); <see langword="null"/>, empty, <c>default</c> and anything else keep Avalonia's own path, as before.
    /// Linux and every other system ignore the variable and keep Avalonia's own path.
    /// </summary>
    public static RenderingChoice For(RenderingPlatform platform, string? value) => platform switch
    {
        RenderingPlatform.Windows => Parse(value),
        RenderingPlatform.MacOS when value is not null && string.Equals(value.Trim(), "software", StringComparison.OrdinalIgnoreCase) => RenderingChoice.Software,
        _ => RenderingChoice.Default,
    };
}
