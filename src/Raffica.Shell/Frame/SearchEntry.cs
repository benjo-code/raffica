namespace Raffica.Shell.Frame;

/// <summary>
/// One extra result of the <see cref="SearchField"/> that is not a ribbon command (a page, a bookmark, ...): listed under its
/// <paramref name="Group"/> heading, with an optional second line <paramref name="Detail"/>; <paramref name="Run"/> is called when it is chosen.
/// </summary>
public sealed record SearchEntry(string Group, string Name, Action Run, string? Detail = null);
