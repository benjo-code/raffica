namespace Raffica.Shell.Frame;

/// <summary>How much room a command takes in a ribbon group.</summary>
public enum CommandSize
{
    Large,
    Small,
}

/// <summary>Where a command goes when its ribbon group is too narrow.</summary>
public enum CompactPlacement
{
    Inline,
    Overflow,
}

/// <summary>
/// A ribbon command. <paramref name="FieldCode"/> (1-2 characters from A-Z0-9) is declared here, is
/// language independent and is never derived from the position. <paramref name="Items"/> turns the
/// command into a dropdown whose items carry their own codes.
/// </summary>
public sealed record CommandDefinition(
    string Id,
    string Name,
    string IconKey,
    string FieldCode,
    Action? Run = null,
    CommandSize Size = CommandSize.Small,
    CompactPlacement Compact = CompactPlacement.Inline,
    bool IconOnly = false,
    string? Shortcut = null,
    string? Description = null,
    IReadOnlyList<CommandDefinition>? Items = null);
