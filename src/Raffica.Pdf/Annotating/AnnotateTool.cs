namespace Raffica.Pdf.Annotating;

/// <summary>The tools of the annotate mode; <see cref="None"/> means the mode is off and the viewer behaves as in 2.1.</summary>
internal enum AnnotateTool
{
    /// <summary>No tool: the annotate mode is off.</summary>
    None,

    /// <summary>Selects, moves, comments and deletes existing marks.</summary>
    Select,

    /// <summary>Highlights selected text.</summary>
    Highlight,

    /// <summary>Underlines selected text.</summary>
    Underline,

    /// <summary>Strikes out selected text.</summary>
    StrikeOut,

    /// <summary>Draws a freehand stroke.</summary>
    Freehand,

    /// <summary>Draws a rectangle outline.</summary>
    Rectangle,

    /// <summary>Draws an ellipse outline.</summary>
    Ellipse,

    /// <summary>Draws a straight line.</summary>
    Line,

    /// <summary>Draws an arrow.</summary>
    Arrow,

    /// <summary>Places a text box.</summary>
    TextBox,
}
