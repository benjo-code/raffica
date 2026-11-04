namespace Raffica.Pdf.Hosting;

/// <summary>Which Avalonia rendering path the app asks for on Windows. A developer and measurement override, not a setting.</summary>
internal enum RenderingChoice
{
    /// <summary>Nothing is set: Avalonia's own default (the GPU path).</summary>
    Default,

    /// <summary>Software rendering as the only mode (<c>Win32RenderingMode.Software</c>).</summary>
    Software,
}

/// <summary>Reads the override <c>RAFFICA_RENDERING</c>.</summary>
internal static class RenderingChoiceParser
{
    /// <summary>The name of the environment variable.</summary>
    public const string VariableName = "RAFFICA_RENDERING";

    /// <summary>
    /// <c>software</c> (trimmed, any case) asks for software rendering; <see langword="null"/>, empty, <c>default</c> and anything
    /// else leave Avalonia's default untouched.
    /// </summary>
    public static RenderingChoice Parse(string? value) =>
        value is not null && string.Equals(value.Trim(), "software", StringComparison.OrdinalIgnoreCase)
            ? RenderingChoice.Software
            : RenderingChoice.Default;
}
