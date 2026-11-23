using System.Globalization;
using System.Xml.Linq;
using Raffica.Write.Editing;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using Raffica.Write.Tests.Support;
using Xunit;

namespace Raffica.Write.Tests.Layout;

/// <summary>The kinds of section step of the oracle gates.</summary>
internal enum SectionKind
{
    NumberFrom,
    Break,
    RemoveMarker,
    OwnFooter,
    SameAsPrevious,
    FirstPage,
    LeftRight,
    InsertNumber,
    PageSetupThis,
    PageSetupWhole,
    PageSetupFromHere,
    Undo,
    Redo,
}

/// <summary>
/// The section steps of the oracle gates (milestone 4.4b): numbering the pages from a place, section breaks of every kind and their removal, an own or an inherited footer or header,
/// a different first page, left and right pages, an inserted page number, a page setup for this section, the whole document or from the caret on, and undo and redo, each made by the real
/// <see cref="TextEditor"/>, brought into the layout as the session does (a result that swapped section properties starts the layout over with <see cref="DocumentLayout.Resection"/>, any other
/// is <see cref="DocumentLayout.ApplyEdit"/>) and compared with a full layout, exactly, and the page numbers drawn on the pages with the independent <see cref="ReferencePageNumbers"/>. A step the
/// editor refuses (null, or no change) is counted as a refusal and compared nowhere, except an undo and a redo with something on the stack, which must always change something.
/// The page-number comparison leaves out, and counts, only the pages of the unverified states (see <see cref="CheckNumbers"/> and <see cref="ReferencePageNumbers"/>); the generator avoids reaching them most of the time.
/// </summary>
internal sealed class SectionSteps
{
    /// <summary>The number of kinds.</summary>
    public static readonly int Kinds = Enum.GetValues<SectionKind>().Length;

    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly PageNumberWords s_words = new("Page", "of");
    private static readonly string[] s_papers = ["A4", "Letter", "A3", "A5", "Legal"];
    private static readonly string[] s_margins = ["Normal", "Narrow", "Moderate", "Wide"];

    // The order of a forced pass: a break first (the later kinds want a second section), the undo and redo last.
    private static readonly SectionKind[] s_forcedOrder =
    [
        SectionKind.Break,
        SectionKind.NumberFrom,
        SectionKind.OwnFooter,
        SectionKind.SameAsPrevious,
        SectionKind.FirstPage,
        SectionKind.LeftRight,
        SectionKind.InsertNumber,
        SectionKind.PageSetupThis,
        SectionKind.PageSetupWhole,
        SectionKind.PageSetupFromHere,
        SectionKind.RemoveMarker,
        SectionKind.Undo,
        SectionKind.Redo,
    ];

    private readonly WordDocument _model;
    private readonly DocumentLayout _layout;
    private readonly LayoutOptions _options;
    private SectionKind _kind;

    public SectionSteps(WordDocument model, DocumentLayout layout, LayoutOptions options)
    {
        _model = model;
        _layout = layout;
        _options = options;
        Editor = new TextEditor(model, (s, p) => layout.ProjectionHost(s, p));
    }

    public TextEditor Editor { get; }

    /// <summary>Called after every compared step with its label (the recovery-copy oracle compares bytes there).</summary>
    public Action<string>? AfterStep { get; set; }

    /// <summary>For each kind, how many of its steps changed something and were compared.</summary>
    public int[] RanPerKind { get; } = new int[Kinds];

    /// <summary>For each kind, how many attempts the editor refused (null or no change) or the document had nothing for.</summary>
    public int[] RefusedPerKind { get; } = new int[Kinds];

    /// <summary>How many steps were compared.</summary>
    public int Compared { get; private set; }

    /// <summary>How many page numbers were compared with the reference, summed over the steps.</summary>
    public long PagesCompared { get; private set; }

    /// <summary>False for the writing view (its strips are numbered by position, not by section): the layout is still compared with a full one.</summary>
    public bool CompareNumbers { get; set; } = true;

    /// <summary>How many page numbers were left out of the comparison by the allowance for the unverified states (see <see cref="CheckNumbers"/>), summed over the steps.</summary>
    public long PagesSkipped { get; private set; }

    /// <summary>How many steps started the layout over (a result with section properties).</summary>
    public int Resections { get; private set; }

    /// <summary>One of every kind in a fixed order, then <paramref name="steps"/> random steps, all from <paramref name="random"/>. Returns how many steps were compared.</summary>
    public async Task<int> RunAsync(Random random, int steps, string label)
    {
        var before = Compared;
        foreach (var kind in s_forcedOrder)
        {
            await RunKindAsync(kind, random, $"{label} forced {kind}");
        }

        for (var i = 0; i < steps; i++)
        {
            var kind = (SectionKind)random.Next(Kinds);
            await RunKindAsync(kind, random, $"{label} step {i} {kind}");
        }

        return Compared - before;
    }

    /// <summary>Runs one step of a kind. Returns true when it changed something and was compared; false when the editor refused it or the document has nothing for it.</summary>
    public async Task<bool> RunKindAsync(SectionKind kind, Random random, string label)
    {
        _kind = kind;
        try
        {
            return await RunCoreAsync(kind, random, label);
        }
        catch (InvalidOperationException ex)
        {
            throw new InvalidOperationException($"{label}: {ex.Message} (steps so far: {string.Join(", ", Trace)})", ex);
        }
    }

    /// <summary>The kinds of the steps that were compared, in order (a failure message carries it).</summary>
    public List<string> Trace { get; } = [];

    private async Task<bool> RunCoreAsync(SectionKind kind, Random random, string label)
    {
        switch (kind)
        {
            case SectionKind.NumberFrom:
                return await NumberFromAsync(random, label);
            case SectionKind.Break:
                return await BreakAsync(random, label);
            case SectionKind.RemoveMarker:
                return await RemoveMarkerAsync(random, label);
            case SectionKind.OwnFooter:
                return await OwnAsync(random, own: true, label);
            case SectionKind.SameAsPrevious:
                return await OwnAsync(random, own: false, label);
            case SectionKind.FirstPage:
                return await FirstPageAsync(random, label);
            case SectionKind.LeftRight:
                return await CheckAsync(Editor.SetLeftRightPages(!(_model.Settings?.EvenAndOddHeaders ?? false), RandomCaret(random)), label, required: false);
            case SectionKind.InsertNumber:
                return await InsertNumberAsync(random, label);
            case SectionKind.PageSetupThis:
                return await PageSetupAsync(random, ApplyScope.ThisSection, label);
            case SectionKind.PageSetupWhole:
                return await PageSetupAsync(random, ApplyScope.WholeDocument, label);
            case SectionKind.PageSetupFromHere:
                return await PageSetupAsync(random, ApplyScope.FromHere, label);
            case SectionKind.Undo:
                if (!Editor.History.CanUndo)
                {
                    return Refuse();
                }

                return await CheckAsync(Editor.History.Undo(), label, required: true);
            default:
                if (!Editor.History.CanRedo)
                {
                    return Refuse();
                }

                return await CheckAsync(Editor.History.Redo(), label, required: true);
        }
    }

    private static TextPosition At(Paragraph paragraph) => new(StoryRef.Body, paragraph, 0);

    // A top-level paragraph of the body; null when the body has none.
    private TextPosition? RandomCaret(Random random)
    {
        var paragraphs = _model.Body.Blocks.OfType<Paragraph>().ToList();
        return paragraphs.Count == 0 ? null : At(paragraphs[random.Next(paragraphs.Count)]);
    }

    // A top-level paragraph that does not start its section (a break there makes a new section), a few tries, else any.
    private TextPosition? InsideSectionCaret(Random random)
    {
        var blocks = _model.Body.Blocks;
        var sections = SectionList.Of(_model);
        var candidates = Enumerable.Range(0, blocks.Count).Where(i => blocks[i] is Paragraph && sections.All(s => s.FirstBlock != i)).ToList();
        return candidates.Count == 0 ? RandomCaret(random) : At((Paragraph)blocks[candidates[random.Next(candidates.Count)]]);
    }

    private async Task<bool> NumberFromAsync(Random random, string label)
    {
        if (RandomCaret(random) is not { } at)
        {
            return Refuse();
        }

        int?[] starts = [null, 1, 1, 5, 0, 12, 40];
        var start = starts[random.Next(starts.Length)];
        var format = (PageNumberFormat)random.Next(5);
        var prefix = random.Next(3) == 0 ? "A-" : string.Empty;

        // A caret that starts a section edits that section alone, whatever its type is. Two states are unverified against Word (see CheckNumbers): a start on a continuous section, which is never drawn here
        // (the section keeps its count), and a start of the wrong parity on an even-page or odd-page section, which is drawn only one time in eight; a caret in the middle of a section makes a next-page section,
        // where every start is fine.
        var facts = Editor.ReadSectionFacts(at);
        if (facts is { InBody: true, AtSectionStart: true })
        {
            var type = (string?)SectionList.Of(_model)[facts.Section].Properties.Get(W + "type")?.Attribute(W + "val");
            if (type == "continuous")
            {
                start = null;
            }
            else if (start is { } value && type is "evenPage" or "oddPage" && random.Next(8) != 0)
            {
                start = type == "evenPage" ? value + (value % 2) : value + 1 - (value % 2);
            }
        }

        return await CheckAsync(Editor.NumberPagesFrom(at, new PageNumbering(start, format, prefix)), label, required: false);
    }

    private async Task<bool> BreakAsync(Random random, string label)
    {
        if (SectionList.Of(_model).Count >= 10 || InsideSectionCaret(random) is not { } at)
        {
            return Refuse();
        }

        return await CheckAsync(Editor.InsertSectionBreak(at, BreakKindOf(random.Next(8))), label, required: false);
    }

    // Next page and continuous are the usual ones; an even-page or odd-page break is one in four (its blank page is left out of the number comparison).
    private static SectionBreakKind BreakKindOf(int draw) => draw switch
    {
        < 3 => SectionBreakKind.NextPage,
        < 6 => SectionBreakKind.Continuous,
        6 => SectionBreakKind.EvenPage,
        _ => SectionBreakKind.OddPage,
    };

    private async Task<bool> RemoveMarkerAsync(Random random, string label)
    {
        var count = SectionList.Of(_model).Count;
        if (count < 2)
        {
            return Refuse();
        }

        return await CheckAsync(Editor.RemoveSectionBreak(1 + random.Next(count - 1)), label, required: false);
    }

    // Own: a section (not the first) that has no own part of a random kind and type gets one; same as previous: one that has an own part gives it up.
    private async Task<bool> OwnAsync(Random random, bool own, string label)
    {
        var sections = SectionList.Of(_model);
        if (sections.Count < 2)
        {
            return Refuse();
        }

        var options = new List<(int Section, HeaderFooterKind Kind, HeaderFooterType Type)>();
        for (var s = 1; s < sections.Count; s++)
        {
            foreach (var kind in new[] { HeaderFooterKind.Footer, HeaderFooterKind.Header })
            {
                foreach (var type in new[] { HeaderFooterType.Default, HeaderFooterType.First, HeaderFooterType.Even })
                {
                    var has = sections[s].Properties.HeaderFooterReferences.Any(r => r.Kind == kind && r.Type == type);
                    if (has != own)
                    {
                        options.Add((s, kind, type));
                    }
                }
            }
        }

        if (options.Count == 0 || RandomCaret(random) is not { } caret)
        {
            return Refuse();
        }

        var (section, k, t) = options[random.Next(options.Count)];
        return await CheckAsync(Editor.SetOwnHeaderFooter(section, k, t, own, caret), label, required: false);
    }

    private async Task<bool> FirstPageAsync(Random random, string label)
    {
        var sections = SectionList.Of(_model);
        var section = random.Next(sections.Count);
        if (RandomCaret(random) is not { } caret)
        {
            return Refuse();
        }

        return await CheckAsync(Editor.SetDifferentFirstPage(section, !sections[section].Properties.GetFlag(W + "titlePg"), caret), label, required: false);
    }

    private async Task<bool> InsertNumberAsync(Random random, string label)
    {
        if (RandomCaret(random) is not { } at)
        {
            return Refuse();
        }

        var look = (PageNumberLook)random.Next(4);
        var align = (PageNumberAlign)random.Next(3);
        var kind = random.Next(4) == 0 ? HeaderFooterKind.Header : HeaderFooterKind.Footer;
        return await CheckAsync(Editor.InsertPageNumber(at, look, kind, align, s_words, "1", "9"), label, required: false);
    }

    private async Task<bool> PageSetupAsync(Random random, ApplyScope scope, string label)
    {
        if (RandomCaret(random) is not { } at)
        {
            return Refuse();
        }

        var value = SectionPageSetup.Read(null);
        PageSetupChange change;
        switch (random.Next(4))
        {
            case 0:
                var (w, h) = PagePresets.Size(s_papers[random.Next(s_papers.Length)]);
                change = new PageSetupChange(value with { WidthTwips = w, HeightTwips = h }, PageSetupParts.Size);
                break;
            case 1:
                change = new PageSetupChange(value.WithLandscape(random.Next(2) == 0), PageSetupParts.Orientation);
                break;
            case 2:
                var m = PagePresets.Margins(s_margins[random.Next(s_margins.Length)], inch: random.Next(2) == 0);
                change = new PageSetupChange(value with { TopTwips = m.Top, BottomTwips = m.Bottom, LeftTwips = m.Left, RightTwips = m.Right }, PageSetupParts.Margins);
                break;
            default:
                change = new PageSetupChange(value with { Columns = 1 + random.Next(3), ColumnSpaceTwips = 360, ColumnLine = random.Next(2) == 0 }, PageSetupParts.Columns);
                break;
        }

        return await CheckAsync(Editor.ApplyPageSetup(at, change, scope), label, required: false);
    }

    // Brings a result into the layout the way the session does, compares the layout with a full one and the drawn page numbers with the reference.
    private async Task<bool> CheckAsync(EditResult? result, string label, bool required)
    {
        if (result is null || (result.Changes.Count == 0 && result.Style is null && result.Sections is null))
        {
            if (required)
            {
                // An undo and a redo with something on the stack always change blocks, styles or sections: nothing compared would prove nothing.
                Assert.Fail(label + ": the step returned no change");
            }

            RefusedPerKind[(int)_kind]++;
            return false;
        }

        if (result.Sections is not null)
        {
            // Section properties changed: the engine read them once, so the layout starts over and the block changes (a carrier paragraph) are not applied into it.
            _layout.Resection(2);
            Resections++;
        }
        else if (result.Style is not null)
        {
            _layout.Restyle(2);
        }
        else
        {
            foreach (var change in result.Changes)
            {
                var through = Math.Max(1, (_layout.Locate(Math.Min(change.First, Math.Max(0, _model.Body.Blocks.Count - 1)))?.Page ?? 0) + 1);
                _layout.ApplyEdit(change, through);
            }
        }

        await _layout.LayOutRestAsync(null, CancellationToken.None);
        LayoutOracle.AssertEqualToFull(_layout, _model, _options, label);
        if (CompareNumbers)
        {
            var check = CheckNumbers(_layout, _model, label);
            PagesCompared += check.Compared;
            PagesSkipped += check.Skipped;
        }

        Compared++;
        RanPerKind[(int)_kind]++;
        Trace.Add(label);
        AfterStep?.Invoke(label);
        return true;
    }

    private bool Refuse()
    {
        RefusedPerKind[(int)_kind]++;
        return false;
    }

    /// <summary>The pages whose number was compared with the reference and the pages left out by the bounded allowance (see <see cref="CheckNumbers"/>).</summary>
    internal readonly record struct NumberCheck(int Compared, int Skipped);

    /// <summary>Fails when the page numbers drawn by the layout differ from the independent count (and returns how many pages were compared and how many were left out).</summary>
    public static int AssertNumbersEqualTheReference(DocumentLayout layout, WordDocument model, string label) => CheckNumbers(layout, model, label).Compared;

    /// <summary>
    /// Compares the page numbers drawn by the layout with <see cref="ReferencePageNumbers"/>, every page exactly, except the pages of the <b>unverified states</b>, which are left out and counted
    /// (<see cref="NumberCheck.Skipped"/>). A page is left out when
    /// <list type="bullet">
    /// <item>it may be the blank page of an even-page or odd-page section (<see cref="DrawnPageNumbers"/>: the layout gives it the format of the section after it; what Word does is not documented), or</item>
    /// <item>it belongs to a section whose rule is unverified, or to the pages after one up to (excluding) the next page where another section restarts with an explicit <c>w:start</c> (the count after an
    /// unverified section depends on what it did): an even-page or odd-page section whose <c>w:start</c> has the wrong parity (the layout numbers the blank page with the start and the section's first page
    /// start + 1; the reference starts that page at the start; Word may refuse the start or change it), and a continuous section that carries a <c>w:start</c> (the sources disagree on whether Word restarts
    /// there: see <see cref="ReferencePageNumbers"/>).</item>
    /// </list>
    /// Every other page, in every other section, is compared exactly; the oracle's generator avoids reaching these states most of the time and the tests bound the left-out share.
    /// </summary>
    internal static NumberCheck CheckNumbers(DocumentLayout layout, WordDocument model, string label)
    {
        var sections = ReferencePageNumbers.Sections(model);
        bool Unverified(int index)
        {
            var s = sections[Math.Min(index, sections.Count - 1)];
            var type = (string?)s?.Get(W + "type")?.Attribute(W + "val");
            var hasStart = int.TryParse((string?)s?.Get(W + "pgNumType")?.Attribute(W + "start"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var start);
            return hasStart && (type == "continuous" || (type == "evenPage" && start % 2 != 0) || (type == "oddPage" && start % 2 == 0));
        }

        bool HasStart(int index) =>
            sections[Math.Min(index, sections.Count - 1)]?.Get(W + "pgNumType")?.Attribute(W + "start") is not null;

        var drawn = DrawnPageNumbers.Read(layout, model);
        var left = new bool[drawn.Count];
        var skipping = false;
        for (var p = 0; p < drawn.Count; p++)
        {
            if (Unverified(drawn[p].Section))
            {
                skipping = true;
            }
            else if (drawn[p].StartsSection && HasStart(drawn[p].Section))
            {
                skipping = false;
            }

            left[p] = skipping || drawn[p].MaybeBlank;
        }

        var expected = ReferencePageNumbers.Numbers(model, [.. drawn.Select(d => d.Section)], [.. drawn.Select(d => d.StartsSection)]);
        var differing = Enumerable.Range(0, drawn.Count).Where(p => !left[p] && expected[p] != drawn[p].Number).ToList();
        Assert.True(
            differing.Count == 0,
            $"{label}: the drawn page numbers [{string.Join(",", drawn.Select(d => d.Number))}] differ from the reference [{string.Join(",", expected)}] at pages [{string.Join(",", differing)}]; section of each page [{string.Join(",", drawn.Select((d, p) => d.Section + (d.StartsSection ? "!" : string.Empty) + (left[p] ? "_" : string.Empty)))}]; sections: {string.Join(" / ", sections.Select(s => s is null ? "none" : string.Concat(s.Get(W + "type")?.ToString() ?? "no type", s.Get(W + "pgNumType")?.ToString() ?? "no numbering")))}");
        return new NumberCheck(left.Count(l => !l), left.Count(l => l));
    }
}
