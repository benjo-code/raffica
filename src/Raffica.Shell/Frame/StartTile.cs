using Raffica.Shell.Localization;

namespace Raffica.Shell.Frame;

/// <summary>The apps of the suite a start tile can start.</summary>
public enum StartApp
{
    Write,
    Sheets,
    Slides,
    Pdf,
}

/// <summary>A tile of the "New" section of the start view.</summary>
public sealed record StartTile(StartApp App, string Label)
{
    /// <summary>The four tiles of the suite with their localized labels ("Blank document", "Blank spreadsheet", "Blank presentation", "Open PDF").</summary>
    public static IReadOnlyList<StartTile> Standard() =>
    [
        new(StartApp.Write, ShellStrings.StartBlankDocument),
        new(StartApp.Sheets, ShellStrings.StartBlankSpreadsheet),
        new(StartApp.Slides, ShellStrings.StartBlankPresentation),
        new(StartApp.Pdf, ShellStrings.StartOpenPdf),
    ];

    /// <summary>The app mark shown in the corner of the tile (not translated).</summary>
    internal string Mark => App switch
    {
        StartApp.Write => "W",
        StartApp.Sheets => "S",
        StartApp.Slides => "SL",
        _ => "PDF",
    };
}
