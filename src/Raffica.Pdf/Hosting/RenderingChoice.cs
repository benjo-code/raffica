namespace Raffica.Pdf.Hosting;

/// <summary>Which Avalonia rendering path the app asks for on Windows. Software is the app's default; <c>Default</c> is the opt-out.</summary>
internal enum RenderingChoice
{
    /// <summary>Avalonia's own default path (ANGLE over Direct3D 11, GPU compositing): the opt-out, <c>RAFFICA_RENDERING=default</c>.</summary>
    Default,

    /// <summary>Software rendering as the only mode (<c>Win32RenderingMode.Software</c>): the app's default on Windows.</summary>
    Software,
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
}
