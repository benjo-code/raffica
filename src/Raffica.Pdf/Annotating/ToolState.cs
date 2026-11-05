using Raffica.Core.Annotations;
using Raffica.Core.Settings;

namespace Raffica.Pdf.Annotating;

/// <summary>
/// The tool model of the annotate mode: which tool is active and the colour and width each tool remembers. The remembered values live in
/// the viewer settings (<see cref="AppSettings"/>), which this class reads and replaces through two delegates (copy on write with
/// <c>with</c>), so it needs no window and no file. Used on the UI thread only.
/// </summary>
internal sealed class ToolState
{
    private readonly Func<AppSettings> _read;
    private readonly Action<AppSettings> _write;

    /// <param name="read">Gets the settings in force.</param>
    /// <param name="write">Takes the changed settings (it keeps them in memory and saves them).</param>
    public ToolState(Func<AppSettings> read, Action<AppSettings> write)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(write);
        _read = read;
        _write = write;
    }

    /// <summary>Raised when the active tool changed.</summary>
    public event Action? Changed;

    /// <summary>Raised when the colour or width a tool remembers changed (not when the active tool changed: that is <see cref="Changed"/>).</summary>
    public event Action? StyleChanged;

    /// <summary>Gets the active tool; <see cref="AnnotateTool.None"/> while the annotate mode is off.</summary>
    public AnnotateTool Tool { get; private set; }

    /// <summary>Gets the author name for new annotations, or <see langword="null"/> for none (a blank name counts as none).</summary>
    public string? Author => string.IsNullOrWhiteSpace(_read().AuthorName) ? null : _read().AuthorName;

    /// <summary>Starts the mode with <paramref name="tool"/>; choosing the active tool again ends the mode.</summary>
    /// <param name="tool">The tool; <see cref="AnnotateTool.None"/> ends the mode.</param>
    /// <returns><see langword="true"/> when a tool is active afterwards; <see langword="false"/> when this call ended the mode.</returns>
    public bool Choose(AnnotateTool tool)
    {
        if (tool == AnnotateTool.None || tool == Tool)
        {
            End();
            return false;
        }

        Tool = tool;
        Changed?.Invoke();
        return true;
    }

    /// <summary>Ends the annotate mode.</summary>
    public void End()
    {
        if (Tool == AnnotateTool.None)
        {
            return;
        }

        Tool = AnnotateTool.None;
        Changed?.Invoke();
    }

    /// <summary>Gets the colour of a text markup tool (the other tools have a <see cref="StyleOf"/>).</summary>
    /// <param name="tool">The tool.</param>
    /// <returns>The colour; the highlight colour for any tool that is no text markup.</returns>
    public AnnotationColor MarkupColor(AnnotateTool tool)
    {
        var settings = _read();
        return AnnotationPalette.ColorOf(tool switch
        {
            AnnotateTool.Underline => settings.UnderlineColor,
            AnnotateTool.StrikeOut => settings.StrikeOutColor,
            _ => settings.HighlightColor,
        });
    }

    /// <summary>Gets the style (colour and width, for the text box the size step) of a drawing tool.</summary>
    /// <param name="tool">The tool.</param>
    /// <returns>The style; the default style for a tool that has none.</returns>
    public ToolStyle StyleOf(AnnotateTool tool) =>
        KindOf(tool) is { } kind ? _read().StyleOf(kind) : AppSettings.DefaultToolStyle;

    /// <summary>Remembers the colour of a tool; a colour that is not in the palette of the tool changes nothing.</summary>
    /// <param name="tool">The tool.</param>
    /// <param name="color">The colour.</param>
    public void SetColor(AnnotateTool tool, AnnotationColor color)
    {
        var settings = _read();
        switch (tool)
        {
            case AnnotateTool.Highlight or AnnotateTool.Underline or AnnotateTool.StrikeOut:
                if (AnnotationPalette.All.Cast<MarkupColor?>().FirstOrDefault(c => AnnotationPalette.ColorOf(c!.Value) == color) is not { } markup)
                {
                    return;
                }

                _write(tool switch
                {
                    AnnotateTool.Underline => settings with { UnderlineColor = markup },
                    AnnotateTool.StrikeOut => settings with { StrikeOutColor = markup },
                    _ => settings with { HighlightColor = markup },
                });
                StyleChanged?.Invoke();
                return;
            default:
                if (KindOf(tool) is not { } kind
                    || DrawPalette.All.Cast<DrawColor?>().FirstOrDefault(c => DrawPalette.ColorOf(c!.Value) == color) is not { } draw)
                {
                    return;
                }

                _write(settings.WithToolStyle(kind, settings.StyleOf(kind) with { Color = draw }));
                StyleChanged?.Invoke();
                return;
        }
    }

    /// <summary>Remembers the width of a drawing tool (the size step of the text box); other tools ignore it.</summary>
    /// <param name="tool">The tool.</param>
    /// <param name="width">The width.</param>
    public void SetWidth(AnnotateTool tool, StrokeWidth width)
    {
        if (KindOf(tool) is not { } kind || !Enum.IsDefined(width))
        {
            return;
        }

        var settings = _read();
        _write(settings.WithToolStyle(kind, settings.StyleOf(kind) with { Width = width }));
        StyleChanged?.Invoke();
    }

    private static AnnotationKind? KindOf(AnnotateTool tool) => tool switch
    {
        AnnotateTool.Freehand => AnnotationKind.Ink,
        AnnotateTool.Rectangle => AnnotationKind.Rectangle,
        AnnotateTool.Ellipse => AnnotationKind.Ellipse,
        AnnotateTool.Line => AnnotationKind.Line,
        AnnotateTool.Arrow => AnnotationKind.Arrow,
        AnnotateTool.TextBox => AnnotationKind.TextBox,
        _ => null,
    };
}
