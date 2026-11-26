namespace Raffica.Write.Model;

/// <summary>
/// Whether a block holds field markup: a field character, an instruction text or a simple field, in any paragraph of it (tables and block content controls entered, to a depth limit past which the answer is yes).
/// Used by <see cref="DocumentEdits"/> to tell an edit that cannot change where a field starts or ends from one that can.
/// </summary>
internal static class FieldMarks
{
    private const int MaxDepth = 64;

    /// <summary>True when the block holds field markup, or when it is nested too deeply to be sure.</summary>
    public static bool Hold(Block block) => Hold(block, 0);

    private static bool Hold(Block block, int depth)
    {
        if (depth > MaxDepth)
        {
            return true;
        }

        switch (block)
        {
            case Paragraph paragraph:
                return Hold(paragraph.Inlines, depth);
            case Table table:
                foreach (var row in table.Rows)
                {
                    if (Hold(row, depth + 1))
                    {
                        return true;
                    }
                }

                return false;
            case ContentControlBlock control:
                foreach (var inner in control.Content)
                {
                    if (Hold(inner, depth + 1))
                    {
                        return true;
                    }
                }

                return false;
            default:
                return false;
        }
    }

    private static bool Hold(RowItem item, int depth)
    {
        if (depth > MaxDepth)
        {
            return true;
        }

        switch (item)
        {
            case TableRow row:
                foreach (var child in row.Items)
                {
                    if (Hold(child, depth + 1))
                    {
                        return true;
                    }
                }

                return false;
            case TableCell cell:
                foreach (var block in cell.Blocks)
                {
                    if (Hold(block, depth + 1))
                    {
                        return true;
                    }
                }

                return false;
            default:
                return false;
        }
    }

    private static bool Hold(IList<Inline> inlines, int depth)
    {
        if (depth > MaxDepth)
        {
            return true;
        }

        for (var i = 0; i < inlines.Count; i++)
        {
            switch (inlines[i])
            {
                case Run run:
                    for (var c = 0; c < run.Content.Count; c++)
                    {
                        if (run.Content[c] is FieldChar or InstructionText)
                        {
                            return true;
                        }
                    }

                    break;
                case SimpleField:
                    return true;
                case Hyperlink link:
                    if (Hold(link.Inlines, depth + 1))
                    {
                        return true;
                    }

                    break;
                case ContentControlInline control:
                    if (Hold(control.Inlines, depth + 1))
                    {
                        return true;
                    }

                    break;
                case TrackedChange change:
                    if (Hold(change.Inlines, depth + 1))
                    {
                        return true;
                    }

                    break;
                default:
                    break;
            }
        }

        return false;
    }

    /// <summary>True when none of the changes can have moved, added or removed field markup or shifted a block index: every change replaces as many blocks as it removes and neither the old nor the new blocks hold any markup.</summary>
    public static bool CannotChangeFields(IReadOnlyList<BlockChange> changes)
    {
        foreach (var change in changes)
        {
            if (change.Removed != change.Added)
            {
                return false;
            }

            foreach (var old in change.OldBlocks)
            {
                if (Hold(old))
                {
                    return false;
                }
            }

            for (var i = 0; i < change.Added && change.First + i < change.Container.Count; i++)
            {
                if (Hold(change.Container[change.First + i]))
                {
                    return false;
                }
            }
        }

        return true;
    }
}
