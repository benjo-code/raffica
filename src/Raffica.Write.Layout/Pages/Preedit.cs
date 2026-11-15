using Raffica.Write.Model;

namespace Raffica.Write.Layout;

/// <summary>
/// The transient text of an input method composition: <see cref="Text"/> is laid out inside the paragraph at the projection offset
/// <see cref="At"/> and drawn underlined, and is in no model. One at a time, per layout.
/// </summary>
internal sealed record PreeditState(StoryRef Story, Paragraph Paragraph, int At, string Text);

/// <summary>
/// Holds the preedit of a layout for the planners of that layout (the body planner and the planners of headers, footers and notes share
/// one slot). Written and read under the layout's gate only; a planner that finds nothing here plans the paragraph as the model says.
/// </summary>
internal sealed class PreeditSlot
{
    private PreeditState? _current;

    public PreeditState? Current
    {
        get => Volatile.Read(ref _current);
        set => Volatile.Write(ref _current, value);
    }
}
