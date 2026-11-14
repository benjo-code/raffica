namespace Raffica.Write.Layout;

/// <summary>A place where a line may end: the break is before UTF-16 offset <paramref name="Offset"/>; a mandatory one is a hard line break.</summary>
public readonly record struct BreakOpportunity(int Offset, bool Mandatory);
