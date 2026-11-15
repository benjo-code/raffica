namespace Raffica.Write.Model;

/// <summary>What a projection needs to know that the model alone cannot say: field values, note marks and hidden text.</summary>
public interface IProjectionHost
{
    /// <summary>The text that replaces the result of a field with this instruction, or null when the stored result shows.</summary>
    string? FieldValue(string instruction);

    /// <summary>The mark text (the number) of a note reference or of a note's own mark, or null when none is shown.</summary>
    string? NoteMarkText(RunContent reference);

    /// <summary>True when the run's text is hidden (no length in the projection).</summary>
    bool IsHidden(Run run);
}

/// <summary>Hosts that need no outside knowledge.</summary>
public static class ProjectionHost
{
    /// <summary>No field values, no note marks, nothing hidden.</summary>
    public static IProjectionHost Plain { get; } = new PlainHost();

    private sealed class PlainHost : IProjectionHost
    {
        public string? FieldValue(string instruction) => null;

        public string? NoteMarkText(RunContent reference) => null;

        public bool IsHidden(Run run) => false;
    }
}
