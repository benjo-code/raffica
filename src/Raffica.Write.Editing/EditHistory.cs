using System.Text;
using System.Xml.Linq;
using Raffica.Write.Model;

namespace Raffica.Write.Editing;

/// <summary>
/// How much the history may keep. The oldest steps go first; the newest step is always kept, even when it alone is over
/// <paramref name="MaxBytes"/>. A step's size counts text and property bags of its snapshots only (see <see cref="EditHistory"/>).
/// </summary>
public sealed record HistoryLimits(int MaxSteps, long MaxBytes)
{
    /// <summary>
    /// 1000 steps and 16 MiB of snapshot estimate. On the generated thesis a paragraph's snapshot is about 2.2 KB (median; 4 KB at most),
    /// so a typing step (before and after) is about 4.4 KB and 1000 of them about 4.4 MB; the whole body is 3.6 MB estimated (5.3 MB
    /// real, the estimate is about 70 percent of the heap), so the byte limit holds four select-all deletes of the thesis.
    /// </summary>
    public static HistoryLimits Default { get; } = new(1000, 16L * 1024 * 1024);
}

/// <summary>
/// One linear undo and redo history for a document, across all of its stories. A step is the <see cref="BlockChange"/> of one
/// editing operation: clones of the blocks before and after, the story, the place of the list of blocks, the caret before and after and
/// the document revision at both ends. <see cref="UnknownContent"/> is shared with the document, never copied.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every edit of the document must go through the history's editor.</b> The history checks <see cref="DocumentEdits.Revision"/> before
/// it records, undoes or redoes: when the document was changed by anything else (the revision is not the one the history left it at),
/// the history is dropped and starts again, so a step is never applied to a document it does not fit.
/// </para>
/// <para>
/// Undo and redo put <em>clones</em> into the document, and every position of a step is an index, never an object: after an undo the
/// document holds objects it did not hold before, and the caret of the returned <see cref="EditResult"/> is the restored paragraph.
/// A list of blocks is named by its story and a path from the story's blocks (a block index, then the row items down to a cell, and so
/// on), resolved against the document at the time of the undo; so a step in a table cell still finds its cell after the undo of a
/// structural edit replaced the table by a clone. The blocks of the <see cref="BlockChange.OldBlocks"/> of an undo or redo are the
/// history's own snapshots: callers must not change them.
/// </para>
/// <para>
/// <b>Coalescing.</b> Typing joins the previous step when all of this holds: the operation is an insert that replaced no selection and
/// has one line (no paragraph break); the text consists of letters and digits only; the previous step is the newest one, itself a
/// typing step, and nothing touched the document or the history since (no other operation, undo, redo or save on the
/// previous step's revision); the caret is where that step ended, in the same list of blocks; and less than two seconds passed since
/// the previous keystroke; and the text takes the same pending format as that step (none, or the same list), so a format command given in
/// between, like Ctrl+B, ends the step and the next character starts a new one, as in Word. Text with any other character (a blank,
/// punctuation, a tab) starts a new step, which following letters join: "hello world" is the steps "hello" and " world".
/// </para>
/// </remarks>
public sealed class EditHistory
{
    private static readonly TimeSpan Pause = TimeSpan.FromSeconds(2);

    private readonly WordDocument _document;
    private readonly TimeProvider _time;
    private readonly HistoryLimits _limits;
    private readonly List<Step> _undo = [];
    private readonly List<Step> _redo = [];
    private long _undoBytes;
    private int _expected;
    private Step? _open;
    private IList<Block>? _openContainer;
    private long _openStamp;

    public EditHistory(WordDocument document, TimeProvider time, HistoryLimits limits)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(limits);
        _document = document;
        _time = time;
        _limits = limits;
        _expected = document.Edits.Revision;
    }

    public bool CanUndo
    {
        get
        {
            Validate();
            return _undo.Count > 0;
        }
    }

    public bool CanRedo
    {
        get
        {
            Validate();
            return _redo.Count > 0;
        }
    }

    /// <summary>The number of steps that can be undone.</summary>
    public int UndoCount
    {
        get
        {
            Validate();
            return _undo.Count;
        }
    }

    /// <summary>The estimated size of the steps that can be undone (text and property bags of their snapshots).</summary>
    public long UndoBytes
    {
        get
        {
            Validate();
            return _undoBytes;
        }
    }

    /// <summary>
    /// Undoes the newest step: the blocks it added are replaced by the ones it removed and the document's revision is set back to the one the step
    /// started from. The caret is where it was before the step. Null when there is nothing to undo.
    /// </summary>
    public EditResult? Undo()
    {
        Validate();
        if (_undo.Count == 0)
        {
            return null;
        }

        var step = _undo[^1];
        if (step.Move is not null)
        {
            return StepMove(step, undo: true);
        }

        if (Resolve(step) is not { } container || !Fits(container, step.First, step.New.Length) || !TrySwapStyle(step, undo: true))
        {
            Clear();
            return null;
        }

        Swap(container, step.First, step.New.Length, step.Old);
        _undo.RemoveAt(_undo.Count - 1);
        _undoBytes -= step.Bytes;
        _redo.Add(step);
        _document.Edits.Restore(step.StartRevision);
        _expected = step.StartRevision;
        _open = null;
        var undone = Finish(step, container, step.Before, step.New.Length, step.Old.Length, step.New);
        _document.Edits.Attribute(undone.Changes);
        return undone;
    }

    /// <summary>Redoes the step that was undone last. Null when there is nothing to redo.</summary>
    public EditResult? Redo()
    {
        Validate();
        if (_redo.Count == 0)
        {
            return null;
        }

        var step = _redo[^1];
        if (step.Move is not null)
        {
            return StepMove(step, undo: false);
        }

        if (Resolve(step) is not { } container || !Fits(container, step.First, step.Old.Length) || !TrySwapStyle(step, undo: false))
        {
            Clear();
            return null;
        }

        Swap(container, step.First, step.Old.Length, step.New);
        _redo.RemoveAt(_redo.Count - 1);
        _undo.Add(step);
        _undoBytes += step.Bytes;
        _document.Edits.Restore(step.EndRevision);
        _expected = step.EndRevision;
        _open = null;
        var redone = Finish(step, container, step.After, step.Old.Length, step.New.Length, step.Old);
        _document.Edits.Attribute(redone.Changes);
        return redone;
    }

    /// <summary>Forgets every step (the file was reloaded, or the document was changed outside the history).</summary>
    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        _undoBytes = 0;
        _open = null;
        _openContainer = null;
        _expected = _document.Edits.Revision;
    }

    /// <summary>The place in a list of blocks a caret has: the index of its paragraph and the offset.</summary>
    internal readonly record struct Point(int Index, int Offset);

    /// <summary>The revision to hand to <see cref="Record"/> as <c>revisionBefore</c>: the history drops its steps first when the document changed behind its back.</summary>
    internal int BeginOperation()
    {
        Validate();
        return _document.Edits.Revision;
    }

    /// <summary>
    /// Records the operation that just ran. <paramref name="typed"/> is the inserted text when the operation was an insert without a
    /// selection and without a line break, else null.
    /// </summary>
    internal void Record(EditResult result, Point before, int revisionBefore, string? typed, object? typingFormat = null)
    {
        if (result.Changes.Count > 1 || (result.Changes.Count == 0 && result.Style is null))
        {
            return;
        }

        if (result.Move is { } move)
        {
            RecordMove(result, move, revisionBefore);
            return;
        }

        if (result.Changes.Count == 0)
        {
            RecordStyleOnly(result, before, revisionBefore);
            return;
        }

        var change = result.Changes[0];
        var index = change.Container.IndexOf(result.Caret.Paragraph);
        if (Root(change.Story) is not { } root || PathTo(root, change.Container) is not { } path || index < 0)
        {
            Clear();
            return;
        }

        var after = new Point(index, result.Caret.Offset);
        var newBlocks = CloneRange(change.Container, change.First, change.Added);
        var stamp = _time.GetTimestamp();
        var endRevision = _document.Edits.Revision;
        _redo.Clear();

        if (result.Style is null && typed is not null && IsWord(typed) && CanJoin(change, before, stamp, revisionBefore, typingFormat))
        {
            var step = _open!;
            _undoBytes -= step.Bytes;
            step.New = newBlocks;
            step.After = after;
            step.EndRevision = endRevision;
            step.Bytes = SizeOf(step.Old) + SizeOf(newBlocks);
            _undoBytes += step.Bytes;
            _openStamp = stamp;
            _expected = endRevision;
            return;
        }

        var added = new Step(change.Story, path, change.First, change.OldBlocks.ToArray(), newBlocks, before, after, revisionBefore, endRevision)
        {
            Style = result.Style,
            TypingFormat = typed is not null && result.Style is null ? typingFormat : null,
        };
        added.Bytes = SizeOf(added.Old) + SizeOf(added.New) + SizeOf(added.Style);
        _undo.Add(added);
        _undoBytes += added.Bytes;
        _expected = endRevision;
        if (typed is not null && result.Style is null)
        {
            _open = added;
            _openContainer = change.Container;
            _openStamp = stamp;
        }
        else
        {
            _open = null;
            _openContainer = null;
        }

        Trim();
    }

    // A move of top-level blocks: the step holds indices only (no block is cloned, the blocks are the document's own objects), so it costs next to nothing and never joins typing.
    private void RecordMove(EditResult result, BlockMove move, int revisionBefore)
    {
        var change = result.Changes[0];
        if (Root(move.Story) is not { } root || !ReferenceEquals(root, change.Container))
        {
            Clear();
            return;
        }

        var endRevision = _document.Edits.Revision;
        _redo.Clear();
        var step = new Step(move.Story, [], move.First, [], [], default, default, revisionBefore, endRevision)
        {
            Move = move,
            Bytes = 64,
        };
        _undo.Add(step);
        _undoBytes += step.Bytes;
        _expected = endRevision;
        _open = null;
        _openContainer = null;
        Trim();
    }

    // Undo or redo of a move: the same rotation (or its inverse) on the same objects, the revision set back like any step's.
    private EditResult? StepMove(Step step, bool undo)
    {
        var forward = step.Move!;
        var move = undo ? forward.Inverse : forward;
        if (Resolve(step) is not { } container || !move.IsValidFor(container.Count))
        {
            Clear();
            return null;
        }

        var old = move.ApplyTo(container);
        if (undo)
        {
            _undo.RemoveAt(_undo.Count - 1);
            _undoBytes -= step.Bytes;
            _redo.Add(step);
        }
        else
        {
            _redo.RemoveAt(_redo.Count - 1);
            _undo.Add(step);
            _undoBytes += step.Bytes;
        }

        var revision = undo ? step.StartRevision : step.EndRevision;
        _document.Edits.Restore(revision);
        _expected = revision;
        _open = null;
        var result = move.ResultIn(container, old);
        _document.Edits.Attribute(result.Changes);
        return result;
    }

    // A style change without a block change (the paragraph held nothing to move, or a switch that touches styles only): the step has no blocks, only the caret and the style swap.
    // The caret paragraph's own container is the step's container, so undo and redo put the caret back in that paragraph.
    private void RecordStyleOnly(EditResult result, Point before, int revisionBefore)
    {
        var story = result.Caret.Story;
        if (Root(story) is not { } root)
        {
            Clear();
            return;
        }

        // The caret paragraph may sit in a table cell or a content control: the step keeps the path of its container, like a block step does.
        if (TextEditor.FindIn(root, result.Caret.Paragraph) is not { } found || PathTo(root, found.Container) is not { } path)
        {
            Clear();
            return;
        }

        var endRevision = _document.Edits.Revision;
        _redo.Clear();
        var step = new Step(story, path, 0, [], [], before, new Point(found.Index, result.Caret.Offset), revisionBefore, endRevision)
        {
            Style = result.Style,
        };
        step.Bytes = SizeOf(step.Style);
        _undo.Add(step);
        _undoBytes += step.Bytes;
        _expected = endRevision;
        _open = null;
        _openContainer = null;
        Trim();
    }

    // Puts the styles of the step (the style and its companions) into the sheet in place: Old on undo, New on redo, all of them or none. False (nothing changed) when the sheet or one style is gone.
    private bool TrySwapStyle(Step step, bool undo)
    {
        if (step.Style is not { } change)
        {
            return true;
        }

        var sheet = _document.Styles;
        if (sheet is null)
        {
            return false;
        }

        // All or nothing: every style of the step must still be there before the first one is swapped.
        var swaps = new List<(Style Current, Style Replacement)>();
        foreach (var item in change.All)
        {
            if (sheet.Find(item.New.Id) is not { } current)
            {
                return false;
            }

            swaps.Add((current, (undo ? item.Old : item.New).Clone()));
        }

        foreach (var (current, replacement) in swaps)
        {
            if (!sheet.Replace(current, replacement))
            {
                return false;
            }
        }

        return true;
    }

    private bool CanJoin(BlockChange change, Point before, long stamp, int revisionBefore, object? typingFormat)
    {
        var step = _open;
        return step is not null
            && ReferenceEquals(step.TypingFormat, typingFormat)
            && _undo.Count > 0
            && ReferenceEquals(_undo[^1], step)
            && ReferenceEquals(_openContainer, change.Container)
            && step.EndRevision == revisionBefore
            && step.EndRevision != _document.Edits.SavedRevision
            && step.Story == change.Story
            && change.First == step.First
            && change.Removed == 1
            && change.Added == 1
            && before == step.After
            && _time.GetElapsedTime(_openStamp, stamp) < Pause;
    }

    private static bool IsWord(string text)
    {
        if (text.Length == 0)
        {
            return false;
        }

        foreach (var rune in text.EnumerateRunes())
        {
            if (!Rune.IsLetterOrDigit(rune))
            {
                return false;
            }
        }

        return true;
    }

    private void Validate()
    {
        if (_document.Edits.Revision != _expected)
        {
            Clear();
        }
    }

    private void Trim()
    {
        var drop = 0;
        var bytes = _undoBytes;
        while (_undo.Count - drop > 1 && (_undo.Count - drop > _limits.MaxSteps || bytes > _limits.MaxBytes))
        {
            bytes -= _undo[drop].Bytes;
            drop++;
        }

        if (drop > 0)
        {
            _undo.RemoveRange(0, drop);
            _undoBytes = bytes;
        }
    }

    private IList<Block>? Resolve(Step step) => Root(step.Story) is { } root ? FollowPath(root, step.Path) : null;

    private IList<Block>? Root(StoryRef story) => StoryResolver.TryBlocks(_document, story);

    private static bool Fits(IList<Block> container, int first, int count) => first >= 0 && first + count <= container.Count;

    private static void Swap(IList<Block> container, int first, int remove, Block[] insert)
    {
        if (container is List<Block> list)
        {
            list.RemoveRange(first, remove);
            list.InsertRange(first, insert.Select(b => b.Clone()));
            return;
        }

        for (var i = remove - 1; i >= 0; i--)
        {
            container.RemoveAt(first + i);
        }

        for (var i = 0; i < insert.Length; i++)
        {
            container.Insert(first + i, insert[i].Clone());
        }
    }

    private static EditResult Finish(Step step, IList<Block> container, Point caret, int removed, int added, Block[] oldBlocks)
    {
        var index = Math.Clamp(caret.Index, 0, Math.Max(0, container.Count - 1));
        var paragraph = (index < container.Count ? container[index] as Paragraph : null) ?? container.OfType<Paragraph>().FirstOrDefault()
            ?? throw new InvalidOperationException("The restored blocks hold no paragraph.");
        IReadOnlyList<BlockChange> changes = removed == 0 && added == 0 && step.Style is not null
            ? []
            : [new BlockChange(step.Story, container, step.First, removed, added, oldBlocks)];
        return new EditResult(new TextPosition(step.Story, paragraph, caret.Offset), changes, null, step.Style);
    }

    private static Block[] CloneRange(IList<Block> container, int first, int count)
    {
        var clones = new Block[count];
        for (var i = 0; i < count; i++)
        {
            clones[i] = container[first + i].Clone();
        }

        return clones;
    }

    // The path of a list of blocks below the root list: a block index (a table, or a content control whose step ends in its content list), then row item indices down to a cell, then the next
    // block index when the list is inside a nested table, and so on. An empty path is the root list itself.
    private static int[]? PathTo(IList<Block> root, IList<Block> target)
    {
        var path = new List<int>();
        return Search(root, target, path) ? path.ToArray() : null;
    }

    private static bool Search(IList<Block> blocks, IList<Block> target, List<int> path)
    {
        if (ReferenceEquals(blocks, target))
        {
            return true;
        }

        for (var i = 0; i < blocks.Count; i++)
        {
            path.Add(i);
            var found = blocks[i] switch
            {
                Table table => SearchItems(table.Rows, target, path),
                ContentControlBlock control => Search(control.Content, target, path),
                _ => false,
            };
            if (found)
            {
                return true;
            }

            path.RemoveAt(path.Count - 1);
        }

        return false;
    }

    private static bool SearchItems(IList<RowItem> items, IList<Block> target, List<int> path)
    {
        for (var i = 0; i < items.Count; i++)
        {
            path.Add(i);
            var found = items[i] switch
            {
                TableCell cell => Search(cell.Blocks, target, path),
                TableRow row => SearchItems(row.Items, target, path),
                _ => false,
            };
            if (found)
            {
                return true;
            }

            path.RemoveAt(path.Count - 1);
        }

        return false;
    }

    private static IList<Block>? FollowPath(IList<Block> root, int[] path)
    {
        var blocks = root;
        var at = 0;
        while (at < path.Length)
        {
            if (path[at] < 0 || path[at] >= blocks.Count)
            {
                return null;
            }

            var step = blocks[path[at]];
            at++;
            if (step is ContentControlBlock control)
            {
                blocks = control.Content;
                continue;
            }

            if (step is not Table table)
            {
                return null;
            }

            var items = table.Rows;
            while (true)
            {
                if (at >= path.Length || path[at] < 0 || path[at] >= items.Count)
                {
                    return null;
                }

                var item = items[path[at++]];
                if (item is TableCell cell)
                {
                    blocks = cell.Blocks;
                    break;
                }

                if (item is not TableRow row)
                {
                    return null;
                }

                items = row.Items;
            }
        }

        return blocks;
    }

    /// <summary>The estimated size of the text and the property bags of the blocks in bytes; <see cref="UnknownContent"/> is shared with the document and not counted.</summary>
    internal static long SizeOf(IEnumerable<Block> blocks)
    {
        long size = 0;
        foreach (var block in blocks)
        {
            size += SizeOf(block);
        }

        return size;
    }

    private static long SizeOf(Block block) => block switch
    {
        Paragraph p => 48 + SizeOf(p.Properties) + SizeOf(p.Inlines),
        Table t => 64 + SizeOf(t.Properties) + (t.GridColumns.Count * 4L) + t.Rows.Sum(SizeOf),
        ContentControlBlock c => 48 + SizeOf(c.Properties) + SizeOf(c.Content),
        _ => 32,
    };

    private static long SizeOf(RowItem item) => item switch
    {
        TableRow r => 48 + SizeOf(r.Properties) + r.Items.Sum(SizeOf),
        TableCell c => 48 + SizeOf(c.Properties) + SizeOf(c.Blocks),
        _ => 32,
    };

    private static long SizeOf(IEnumerable<Inline> inlines)
    {
        long size = 0;
        foreach (var inline in inlines)
        {
            size += 32;
            if (inline is Run run)
            {
                size += SizeOf(run.Properties);
                foreach (var content in run.Content)
                {
                    size += 32 + content switch
                    {
                        TextContent t => 2L * t.Text.Length,
                        DeletedText d => 2L * d.Text.Length,
                        InstructionText i => 2L * i.Text.Length,
                        _ => 0,
                    };
                }
            }
            else if (ParagraphMap.ChildrenOf(inline) is { } children)
            {
                size += SizeOf(children);
            }
        }

        return size;
    }

    private static long SizeOf(StyleChange? change) => change is null ? 0 : change.All.Sum(item => SizeOf(item.Old) + SizeOf(item.New));

    private static long SizeOf(Style style)
    {
        long size = 64;
        foreach (var text in new[] { style.Id, style.Type, style.Name, style.BasedOn, style.Next, style.Link, style.DefaultAttribute })
        {
            size += 2L * (text?.Length ?? 0);
        }

        return size + SizeOf(style.ParagraphProperties) + SizeOf(style.RunProperties) + SizeOf(style.TableProperties);
    }

    private static long SizeOf(PropertyBag bag)
    {
        long size = 32;
        foreach (var child in bag.Children)
        {
            size += SizeOf(child);
        }

        return size;
    }

    private static long SizeOf(XElement element)
    {
        long size = 48 + (2L * element.Name.LocalName.Length);
        foreach (var attribute in element.Attributes())
        {
            size += 32 + (2L * attribute.Value.Length);
        }

        foreach (var node in element.Nodes())
        {
            size += node switch
            {
                XElement child => SizeOf(child),
                XText text => 24 + (2L * text.Value.Length),
                _ => 24,
            };
        }

        return size;
    }

    private sealed class Step(StoryRef story, int[] path, int first, Block[] old, Block[] added, Point before, Point after, int startRevision, int endRevision)
    {
        public StoryRef Story { get; } = story;

        public int[] Path { get; } = path;

        public int First { get; } = first;

        public Block[] Old { get; } = old;

        public Block[] New { get; set; } = added;

        public Point Before { get; } = before;

        public Point After { get; set; } = after;

        public int StartRevision { get; } = startRevision;

        public int EndRevision { get; set; } = endRevision;

        public long Bytes { get; set; }

        public StyleChange? Style { get; init; }

        /// <summary>Set for a move of top-level blocks: the step then holds no blocks.</summary>
        public BlockMove? Move { get; init; }

        /// <summary>The pending format the typed text of this step took (the list of the session, compared by reference), or null for plain typing.</summary>
        public object? TypingFormat { get; init; }
    }
}
