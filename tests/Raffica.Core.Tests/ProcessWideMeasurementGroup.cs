using Xunit;

namespace Raffica.Core.Tests;

/// <summary>
/// Runs alone. The tests of this group assert on process-wide counters (bytes allocated by the whole process, memory held after a full
/// collection) or on a short wall-clock bound; in the parallel run another test that allocates a few hundred megabytes or keeps the
/// cores busy would fail them although the code under test did nothing wrong. A collection that disables parallelization runs after the
/// parallel ones, one test class at a time, so nothing else allocates in their window.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ProcessWideMeasurementGroup
{
    public const string Name = "ProcessWideMeasurement";
}
