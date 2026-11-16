using System.Globalization;
using Raffica.Write.Commands;
using Raffica.Write.Editing;
using Raffica.Write.Layout;
using Raffica.Write.Model;

namespace Raffica.Write.Viewing;

/// <summary>A line spacing chosen in a picker: <see cref="Value"/> is a multiple of the single line for <see cref="LineRuleKind.Auto"/> and points for the other two rules.</summary>
internal readonly record struct LineSpacingChoice(float Value, LineRuleKind Rule);

/// <summary>
/// The formatting commands of the page view (<c>format.*</c>): the keys of <see cref="EditingKeys.FormatCommandFor"/>, the ribbon and the pickers end here. Every change is one
/// <see cref="EditSession.Apply"/> through <see cref="ApplyGuarded"/> (one history step, the failure rule of a key); a save queues it like typing, and the operation reads the selection when it runs.
/// </summary>
/// <remarks>
/// A character command at a caret (no selection) sets the session's <see cref="PendingFormat"/>, which the next typed text takes; in a paragraph without text it also formats the paragraph
/// mark, because the layout takes the height of the empty line from it. Such a command is applied with the typing flag, so it does not end the pending format it is part of; a paragraph
/// command at a caret keeps it too. A switch at a caret flips what typing there would give (the pending value first, then the effective formatting of the character before the caret).
/// A held key does not repeat a command. A value command (font, size, colour, highlight, spacing, style) validates its payload and refuses a bad one without a change and without an
/// exception; the size commands take the new size in points (the picker computes a step from the size it shows). Indent more and less step the left indent of the first paragraph of the
/// selection by half an inch and set that value on every paragraph touched.
/// </remarks>
internal sealed partial class PageView
{
    private const float IndentStep = 36f;
    private const float MaxPoints = 1584f;

    private static readonly Dictionary<string, CharacterToggle> s_toggleCommands = new()
    {
        [WriteCommandIds.Bold] = CharacterToggle.Bold,
        [WriteCommandIds.Italic] = CharacterToggle.Italic,
        [WriteCommandIds.Underline] = CharacterToggle.Underline,
        [WriteCommandIds.Strike] = CharacterToggle.Strike,
        [WriteCommandIds.Superscript] = CharacterToggle.Superscript,
        [WriteCommandIds.Subscript] = CharacterToggle.Subscript,
        [WriteCommandIds.SmallCaps] = CharacterToggle.SmallCaps,
        [WriteCommandIds.AllCaps] = CharacterToggle.AllCaps,
    };

    private static readonly HashSet<string> s_highlights = new(StringComparer.Ordinal)
    {
        "black", "blue", "cyan", "green", "magenta", "red", "yellow", "white", "darkBlue", "darkCyan", "darkGreen", "darkMagenta", "darkRed", "darkYellow", "darkGray", "lightGray",
    };

    /// <summary>Runs a formatting command that needs no value; true when it was taken (also when it changed nothing).</summary>
    internal bool RunFormatCommand(string id) => RunFormatCommand(id, null);

    /// <summary>
    /// Runs a formatting command; <paramref name="payload"/> is the value of a value command (see <see cref="WriteCommandIds.TakesPayload"/>) and is ignored by the others. False when the
    /// command is unknown or its payload is not usable (nothing changes then); true when it was taken.
    /// </summary>
    internal bool RunFormatCommand(string id, object? payload)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (_released || !WriteCommandIds.IsFormatting(id))
        {
            return false;
        }

        // The value is checked before the session is touched: a bad payload costs nothing.
        Func<EditSession, Func<TextEditor, EditResult?>>? operation;
        bool characterLevel;
        if (s_toggleCommands.TryGetValue(id, out var toggle))
        {
            operation = ToggleOperation(toggle);
            characterLevel = true;
        }
        else if (CharacterChangesOf(id, payload) is { } characterChanges)
        {
            operation = CharacterOperation(characterChanges);
            characterLevel = true;
        }
        else if (ParagraphOperationOf(id, payload) is { } paragraph)
        {
            operation = paragraph;
            characterLevel = id is not (WriteCommandIds.ParagraphStyle or WriteCommandIds.CharacterStyle or WriteCommandIds.ClearDirect or WriteCommandIds.UpdateStyle);
        }
        else
        {
            return false;
        }

        if (SessionForCommand() is not { } session)
        {
            return true;
        }

        // A caret keeps its pending format through a command that is part of it (see the remarks).
        var atCaret = characterLevel && session.Selection is { IsEmpty: true };
        ApplyGuarded(session, operation(session), typing: atCaret);
        return true;
    }

    private static Func<EditSession, Func<TextEditor, EditResult?>> ToggleOperation(CharacterToggle toggle) => session => ed =>
    {
        if (session.Selection is not { } selection)
        {
            return null;
        }

        if (!selection.IsEmpty)
        {
            return ed.ToggleCharacter(selection, toggle);
        }

        // A caret: flip what typing here would give; an empty paragraph also formats its mark.
        var on = FormatChanges.IsOn(session.Pending.Changes, toggle) ?? ed.IsToggleOn(selection.End, toggle);
        var result = ed.IsEmptyParagraph(selection.End) ? ed.ToggleCharacter(selection, toggle) : null;
        session.Pending.Set(FormatChanges.Merge(session.Pending.Changes, FormatChanges.Toggle(toggle, !on)));
        return result;
    };

    private static Func<EditSession, Func<TextEditor, EditResult?>> CharacterOperation(IReadOnlyList<PropertyChange> changes) => session => ed =>
    {
        if (session.Selection is not { } selection)
        {
            return null;
        }

        if (!selection.IsEmpty)
        {
            return ed.ApplyCharacterChanges(selection, changes);
        }

        var result = ed.IsEmptyParagraph(selection.End) ? ed.ApplyCharacterChanges(selection, changes) : null;
        session.Pending.Set(FormatChanges.Merge(session.Pending.Changes, changes));
        return result;
    };

    private static Func<EditSession, Func<TextEditor, EditResult?>> ParagraphChangeOperation(IReadOnlyList<PropertyChange> changes) =>
        session => ed => session.Selection is { } selection ? ed.ApplyParagraphChanges(selection, changes) : null;

    // The changes of a character value command, or null when the id is not one or the payload is unusable.
    private static IReadOnlyList<PropertyChange>? CharacterChangesOf(string id, object? payload)
    {
        switch (id)
        {
            case WriteCommandIds.Font:
                return payload is string family && family.Length is > 0 and <= 128 && !family.Any(char.IsControl) ? FormatChanges.Font(family) : null;
            case WriteCommandIds.Size or WriteCommandIds.SizeUp or WriteCommandIds.SizeDown:
                return TryPoints(payload, out var size) && size is >= 1f and <= 1638f ? FormatChanges.Size(size) : null;
            case WriteCommandIds.Color:
                return TryColor(payload, out var rgb) ? FormatChanges.Color(rgb) : null;
            case WriteCommandIds.Highlight:
                return payload is null or "none" ? FormatChanges.Highlight(null) : payload is string name && s_highlights.Contains(name) ? FormatChanges.Highlight(name) : null;
            default:
                return null;
        }
    }

    // The operation of a paragraph, style or clear command, or null when the id is not one or the payload is unusable.
    private Func<EditSession, Func<TextEditor, EditResult?>>? ParagraphOperationOf(string id, object? payload)
    {
        switch (id)
        {
            case WriteCommandIds.AlignLeft:
                return ParagraphChangeOperation(ParagraphChanges.Alignment(ParagraphJustification.Left));
            case WriteCommandIds.AlignCenter:
                return ParagraphChangeOperation(ParagraphChanges.Alignment(ParagraphJustification.Center));
            case WriteCommandIds.AlignRight:
                return ParagraphChangeOperation(ParagraphChanges.Alignment(ParagraphJustification.Right));
            case WriteCommandIds.Justify:
                return ParagraphChangeOperation(ParagraphChanges.Alignment(ParagraphJustification.Justify));
            case WriteCommandIds.IndentMore:
                return IndentOperation(IndentStep);
            case WriteCommandIds.IndentLess:
                return IndentOperation(-IndentStep);
            case WriteCommandIds.KeepNext:
                return SwitchOperation(ParagraphSwitch.KeepNext, p => p.KeepNext);
            case WriteCommandIds.KeepLines:
                return SwitchOperation(ParagraphSwitch.KeepLines, p => p.KeepLines);
            case WriteCommandIds.PageBreakBefore:
                return SwitchOperation(ParagraphSwitch.PageBreakBefore, p => p.PageBreakBefore);
            case WriteCommandIds.WidowControl:
                return SwitchOperation(ParagraphSwitch.WidowControl, p => p.WidowControl);
            case WriteCommandIds.ClearDirect:
                return session => ed => session.Selection is { } selection ? ed.ClearDirectFormatting(selection) : null;
            case WriteCommandIds.UpdateStyle:
                return session => ed => session.Selection is { } selection ? ed.UpdateStyleToMatch(selection) : null;
            case WriteCommandIds.SpaceBefore:
                return TryPoints(payload, out var before) && before is >= 0f and <= MaxPoints ? ParagraphChangeOperation(ParagraphChanges.Spacing(before, null, null, null)) : null;
            case WriteCommandIds.SpaceAfter:
                return TryPoints(payload, out var after) && after is >= 0f and <= MaxPoints ? ParagraphChangeOperation(ParagraphChanges.Spacing(null, after, null, null)) : null;
            case WriteCommandIds.LineSpacing:
                return LineSpacingOf(payload) is { } choice ? ParagraphChangeOperation(ParagraphChanges.Spacing(null, null, choice.Value, choice.Rule)) : null;
            case WriteCommandIds.ParagraphStyle:
                return StyleOperationOf(payload, "paragraph", paragraph: true);
            case WriteCommandIds.CharacterStyle:
                return StyleOperationOf(payload, "character", paragraph: false);
            default:
                return null;
        }
    }

    // A pagination switch flips the effective value of the first paragraph of the selection and sets it on every paragraph touched.
    private Func<EditSession, Func<TextEditor, EditResult?>> SwitchOperation(ParagraphSwitch which, Func<ResolvedParagraph, bool> read) => session => ed =>
    {
        if (session.Selection is not { } selection)
        {
            return null;
        }

        var current = read(ResolverOfModel().ForParagraph(selection.Normalized().Start.Paragraph));
        return ed.ApplyParagraphChanges(selection, ParagraphChanges.Switch(which, !current));
    };

    private Func<EditSession, Func<TextEditor, EditResult?>> IndentOperation(float step) => session => ed =>
    {
        if (session.Selection is not { } selection)
        {
            return null;
        }

        var current = ResolverOfModel().ForParagraph(selection.Normalized().Start.Paragraph).LeftIndent;
        var target = Math.Clamp(current + step, 0f, MaxPoints);
        return target == current ? null : ed.ApplyParagraphChanges(selection, ParagraphChanges.Indent(target, null, null, null));
    };

    private StyleResolver ResolverOfModel()
    {
        var model = _tab.Docx.Model;
        return new StyleResolver(model.Styles, model.Settings, model.Numbering);
    }

    // The style a picker chose: null or "" is "none"; an id the file does not define, or defines for the other kind, is no choice (null is returned: the command is refused).
    private Func<EditSession, Func<TextEditor, EditResult?>>? StyleOperationOf(object? payload, string kind, bool paragraph)
    {
        string? id = null;
        if (payload is not (null or ""))
        {
            if (payload is not string text || _tab.Docx.Model.Styles?.Find(text) is not { } style || (style.Type ?? "paragraph") != kind)
            {
                return null;
            }

            id = text;
        }

        return session => ed => session.Selection is not { } selection ? null : paragraph ? ed.ApplyParagraphStyle(selection, id) : ed.ApplyCharacterStyle(selection, id);
    }

    private static LineSpacingChoice? LineSpacingOf(object? payload)
    {
        if (payload is LineSpacingChoice choice)
        {
            var (low, high) = choice.Rule == LineRuleKind.Auto ? (0.1f, 132f) : (0f, MaxPoints);
            return Enum.IsDefined(choice.Rule) && float.IsFinite(choice.Value) && choice.Value >= low && choice.Value <= high ? choice : null;
        }

        // A plain number is a multiple of the single line.
        return TryPoints(payload, out var multiple) && multiple is >= 0.1f and <= 132f ? new LineSpacingChoice(multiple, LineRuleKind.Auto) : null;
    }

    private static bool TryPoints(object? payload, out float value)
    {
        value = float.NaN;
        switch (payload)
        {
            case float f:
                value = f;
                break;
            case double d:
                value = (float)d;
                break;
            case int i:
                value = i;
                break;
            case long l:
                value = l;
                break;
            case decimal m:
                value = (float)m;
                break;
            case string s when float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed):
                value = parsed;
                break;
            default:
                return false;
        }

        return float.IsFinite(value);
    }

    private static bool TryColor(object? payload, out uint rgb)
    {
        rgb = 0;
        switch (payload)
        {
            case uint u:
                rgb = u & 0xFFFFFF;
                return true;
            case int i when i is >= 0 and <= 0xFFFFFF:
                rgb = (uint)i;
                return true;
            case string s:
                var hex = s.StartsWith('#') ? s[1..] : s;
                return hex.Length == 6 && uint.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out rgb);
            default:
                return false;
        }
    }
}
