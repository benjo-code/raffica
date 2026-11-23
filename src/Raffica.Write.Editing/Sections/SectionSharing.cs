namespace Raffica.Write.Editing;

/// <summary>
/// Which sections show one header or footer part: <paramref name="OwnerSection"/> is the section that holds the reference (-1 when no section resolves a part of that kind and type) and
/// <paramref name="Sections"/> are the indexes, ascending, of every section that resolves to that part (the owner is always one of them). Editing the part edits it for all of them.
/// </summary>
public sealed record SectionSharing(int OwnerSection, IReadOnlyList<int> Sections);
