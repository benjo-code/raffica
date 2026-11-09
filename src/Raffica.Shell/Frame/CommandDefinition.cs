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
/// command into a dropdown whose items carry their own codes. <paramref name="EnabledWhen"/> is asked whenever the state matters (a
/// click, a field code, a search choice, <see cref="RibbonView.RefreshCommandStates"/>); a command without it is always enabled. A
/// disabled command is dimmed in the ribbon and in the search, never runs, and is skipped by the field codes; <paramref name="DisabledReason"/>
/// (optional) is shown as the description of its tooltip while it is disabled. <paramref name="CheckedWhen"/> makes the command a switch or
/// a choice: it is asked together with <paramref name="EnabledWhen"/> (<see cref="RibbonView.RefreshCommandStates"/>) and a command that is on is
/// drawn pressed and exposed as a toggle to assistive technology; a command without it is a plain action.
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
    IReadOnlyList<CommandDefinition>? Items = null,
    Func<bool>? EnabledWhen = null,
    Func<string?>? DisabledReason = null,
    Func<bool>? CheckedWhen = null)
{
    /// <summary>Whether the command may run now.</summary>
    public bool IsEnabled => EnabledWhen is null || EnabledWhen();

    /// <summary>Whether the command is a switch or a choice (it has a checked state).</summary>
    public bool IsToggle => CheckedWhen is not null;

    /// <summary>Whether the switch or choice is on now; false for a plain command.</summary>
    public bool IsChecked => CheckedWhen is not null && CheckedWhen();

    /// <summary>Why the command is disabled, or null when it is enabled or no reason is given.</summary>
    public string? CurrentDisabledReason => IsEnabled ? null : DisabledReason?.Invoke();
}
