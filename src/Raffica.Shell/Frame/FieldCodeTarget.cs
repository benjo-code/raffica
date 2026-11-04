namespace Raffica.Shell.Frame;

/// <summary>
/// A field code of the app that is not a ribbon command: in the first stage of the Alt mode the one character <paramref name="Code"/>
/// ends the mode and calls <paramref name="Invoke"/> (for example Q focuses the search field, 6 and 7 the side panes). The badge of
/// the code sits on the control that carries it as <see cref="FieldCodeBadge.CodeProperty"/>; without a registered target, or while
/// that control is not visible, there is neither a badge nor an action.
/// </summary>
public sealed record FieldCodeTarget(string Code, Action Invoke);
