namespace Raffica.Write.Model;

/// <summary>One <c>w:num</c>: the abstract definition it uses and the level overrides.</summary>
public sealed class NumberingInstance : OrderedElement
{
    /// <summary><c>w:numId</c>; -1 when absent or not a number.</summary>
    public int NumId { get; set; } = -1;

    public int? AbstractNumId { get; set; }

    public IList<LevelOverride> Overrides { get; } = new List<LevelOverride>();
}

/// <summary>One <c>w:lvlOverride</c>: a restart value and/or a replacement level definition.</summary>
public sealed class LevelOverride : OrderedElement
{
    /// <summary><c>w:ilvl</c>; -1 when absent or not a number.</summary>
    public int Level { get; set; } = -1;

    /// <summary><c>w:startOverride</c>.</summary>
    public int? StartOverride { get; set; }

    /// <summary>The <c>w:lvl</c> inside the override, if any.</summary>
    public NumberingLevel? Definition { get; set; }
}
