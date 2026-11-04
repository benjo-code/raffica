using Avalonia;
using Avalonia.Input;
using Raffica.Pdf.Viewing;

namespace Raffica.Pdf.Annotating;

/// <summary>
/// The pointer logic of one family of annotate tools (text markup, drawing, text box, select). <see cref="AnnotateInteraction"/> routes the
/// mouse of the page view to the controller that handles the active tool; a controller may draw a live preview as the overlay of the view.
/// </summary>
internal interface IToolController : IPageOverlay, IDisposable
{
    /// <summary>Whether this controller serves the tool.</summary>
    bool Handles(AnnotateTool tool);

    /// <summary>Starts a gesture. The point is in the coordinates of the page view.</summary>
    void Press(Point point, int clickCount);

    /// <summary>Moves the pointer (while a gesture runs or not).</summary>
    void Move(Point point);

    /// <summary>Ends the gesture where the button came up.</summary>
    void Release(Point point);

    /// <summary>
    /// Drops what the controller holds that Esc may drop: a gesture in progress, then whatever is selected or waiting. Returns whether
    /// something was dropped; when nothing was, the same Esc ends the mode.
    /// </summary>
    bool Cancel();

    /// <summary>The cursor over a point of the view when no gesture runs; null for the default one.</summary>
    Cursor? CursorAt(Point point);
}
