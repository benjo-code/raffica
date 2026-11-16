using Xunit;

namespace Raffica.Write.Tests.Editing;

/// <summary>
/// A class in this collection runs alone, after every parallel test class has finished, so a wall-clock bound measures the code and not
/// the corpus oracle tests running beside it. Keep a class here only when it asserts a time and the bound must stay.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class WallClockGroup
{
    public const string Name = "Write wall-clock bounds";
}
