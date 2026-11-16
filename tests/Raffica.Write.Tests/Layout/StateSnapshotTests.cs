using System.Text;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using Xunit;
using static Raffica.Write.Tests.Layout.ChromeTestKit;
using static Raffica.Write.Tests.Layout.PageTestKit;

namespace Raffica.Write.Tests.Layout;

public sealed class StateSnapshotTests
{
    private const string Plain = "<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"/>";

    private static string Numbering(string extra = "") =>
        "<w:abstractNum w:abstractNumId=\"0\">"
        + "<w:lvl w:ilvl=\"0\"><w:start w:val=\"1\"/><w:numFmt w:val=\"decimal\"/><w:lvlText w:val=\"%1.\"/></w:lvl>"
        + "<w:lvl w:ilvl=\"1\"><w:start w:val=\"1\"/><w:numFmt w:val=\"lowerLetter\"/><w:lvlText w:val=\"%2)\"/></w:lvl>"
        + "</w:abstractNum><w:num w:numId=\"1\"><w:abstractNumId w:val=\"0\"/>" + extra + "</w:num>";

    private static string Item(int level) =>
        $"<w:p><w:pPr><w:numPr><w:ilvl w:val=\"{level}\"/><w:numId w:val=\"1\"/></w:numPr></w:pPr><w:r><w:t>x</w:t></w:r></w:p>";

    private static BlockPlanner PlannerFor(WordDocument document, PlanContext? context = null)
    {
        var styles = new StyleResolver(document.Styles, document.Settings, document.Numbering);
        var planner = new BlockPlanner(styles, new NumberingCounter(document.Numbering), new ParagraphLayouter(Fonts), document.Settings);
        if (context is not null)
        {
            planner.Context = context;
        }

        return planner;
    }

    private static string? LabelOf(BlockPlan? plan) => ((ParagraphPlan)plan!).Input.Label?.Text;

    [Fact]
    public void NumberingRestoreRepeatsLabels()
    {
        // Levels and a start override are part of the state: the second level restarts under the first.
        var body = new StringBuilder();
        for (var i = 0; i < 50; i++)
        {
            body.Append(Item(i % 5 == 0 ? 0 : 1));
        }

        var document = LayoutModel.Document(body.ToString(), LayoutModel.StylesPart(Plain), LayoutModel.NumberingPart(Numbering("<w:lvlOverride w:ilvl=\"0\"><w:startOverride w:val=\"7\"/></w:lvlOverride>")));
        var planner = PlannerFor(document);
        var flat = FlatBlocks.From(document.Body.Blocks);

        for (var i = 0; i < 20; i++)
        {
            planner.Plan(flat[i]);
        }

        var state = planner.Numbering.Snapshot();
        var first = Enumerable.Range(20, 30).Select(i => LabelOf(planner.Plan(flat[i]))).ToList();

        planner.Numbering.Restore(state);
        var second = Enumerable.Range(20, 30).Select(i => LabelOf(planner.Plan(flat[i]))).ToList();

        Assert.Equal(first, second);
        Assert.All(first, l => Assert.NotNull(l));

        // The snapshot is a copy: planning after it did not change it, and it restores again.
        planner.Numbering.Restore(state);
        Assert.Equal(first, Enumerable.Range(20, 30).Select(i => LabelOf(planner.Plan(flat[i]))).ToList());
    }

    [Fact]
    public void NumberingRestoreForgetsAStartOverrideThatCameLater()
    {
        var document = LayoutModel.Document(Item(0) + Item(0), LayoutModel.StylesPart(Plain), LayoutModel.NumberingPart(Numbering("<w:lvlOverride w:ilvl=\"0\"><w:startOverride w:val=\"5\"/></w:lvlOverride>")));
        var planner = PlannerFor(document);
        var flat = FlatBlocks.From(document.Body.Blocks);
        var empty = planner.Numbering.Snapshot();
        var first = LabelOf(planner.Plan(flat[0]));
        Assert.Equal("5.", first);

        planner.Numbering.Restore(empty);
        Assert.Equal("5.", LabelOf(planner.Plan(flat[0])));
    }

    [Fact]
    public void NoteRestoreRepeatsNoteNumbers()
    {
        var body = new StringBuilder();
        var notes = new List<string>();
        var ends = new List<string>();
        for (var i = 1; i <= 50; i++)
        {
            body.Append(Para(Text($"para {i}") + FootnoteRef(i) + (i % 7 == 0 ? EndnoteRef(i) : string.Empty)));
            notes.Add(Footnote(i, $"note {i}"));
            if (i % 7 == 0)
            {
                ends.Add(Endnote(i, $"end {i}"));
            }
        }

        var document = ChromeTestKit.Document(body.ToString(), footnotes: FootnotesPart([.. notes]), endnotes: EndnotesPart([.. ends]));
        NotePlanner? notePlanner = null;
        var planner = PlannerFor(document);
        notePlanner = new NotePlanner(document, () => PlannerFor(document));
        planner.Context = new PlanContext { Notes = notePlanner };
        var flat = FlatBlocks.From(document.Body.Blocks);

        static string Marks(BlockPlan? plan) => string.Join(",", ((ParagraphPlan)plan!).Input.NoteMarks!.Select(m => (m.Note.IsEndnote ? "e" : "f") + m.Note.Text));

        for (var i = 0; i < 20; i++)
        {
            planner.Plan(flat[i]);
        }

        var state = notePlanner.Snapshot();
        var first = Enumerable.Range(20, 30).Select(i => Marks(planner.Plan(flat[i]))).ToList();
        var endnotesAfter = notePlanner.EndnotePlans().Count();

        notePlanner.Restore(state);
        var second = Enumerable.Range(20, 30).Select(i => Marks(planner.Plan(flat[i]))).ToList();

        Assert.Equal(first, second);
        Assert.Equal("f22", first[1].Split(',')[0]);

        // The endnote list was cut back and refilled: the same number of endnote blocks, not twice as many.
        Assert.Equal(endnotesAfter, notePlanner.EndnotePlans().Count());
    }

    [Fact]
    public void ForgetReplansOnlyThatBlock()
    {
        var document = LayoutModel.Document(string.Concat(Enumerable.Repeat(Item(0), 6)), LayoutModel.StylesPart(Plain), LayoutModel.NumberingPart(Numbering()));
        var planner = PlannerFor(document);
        var flat = FlatBlocks.From(document.Body.Blocks);
        var start = planner.CurrentState();
        var plans = flat.Select(planner.PlanFor).ToList();
        Assert.Equal(6, planner.PlansMade);

        // A second pass from the same state is all hits and returns the same objects (numbered blocks are hits only from the state they were planned in).
        planner.RestoreState(start.Numbers, start.Notes);
        var again = flat.Select(planner.PlanFor).ToList();
        Assert.Equal(6, planner.PlansMade);
        Assert.All(plans.Zip(again), pair => Assert.Same(pair.First, pair.Second));

        planner.Forget(flat[3].Block);
        Assert.False(planner.HasPlan(flat[3].Block));
        planner.RestoreState(start.Numbers, start.Notes);
        var third = flat.Select(planner.PlanFor).ToList();
        Assert.Equal(7, planner.PlansMade);
        Assert.NotSame(plans[3], third[3]);
        for (var i = 0; i < 6; i++)
        {
            if (i != 3)
            {
                Assert.Same(plans[i], third[i]);
            }
        }
    }

    [Fact]
    public void StructuralChangeKeepsOtherPlans()
    {
        var document = LayoutModel.Document(string.Concat(Enumerable.Repeat("<w:p><w:r><w:t>x</w:t></w:r></w:p>", 3)), LayoutModel.StylesPart(Plain), LayoutModel.NumberingPart(Numbering()));
        var planner = PlannerFor(document);
        var before = FlatBlocks.From(document.Body.Blocks);
        var target = before[1].Block;
        var plan = planner.PlanFor(before[1])!;
        Assert.Equal(1, plan.BodyIndex);

        // Measure at a width so a flow is memoised: it carries the old index and must not survive the shift.
        Assert.Equal(1, plan.Layout(400f).BodyIndex);

        var inserted = LayoutModel.Document("<w:p><w:r><w:t>y</w:t></w:r></w:p>").Body.Blocks[0];
        document.Body.Blocks.Insert(0, inserted);
        var after = FlatBlocks.From(document.Body.Blocks);
        var count = planner.PlansMade;
        var same = planner.PlanFor(after[2])!;

        Assert.Same(plan, same);
        Assert.Same(target, after[2].Block);
        Assert.Equal(count, planner.PlansMade);
        Assert.Equal(2, same.BodyIndex);
        Assert.Equal(2, same.Layout(400f).BodyIndex);

        // A removed block leaves the cache with Retain.
        document.Body.Blocks.RemoveAt(2);
        planner.Retain(FlatBlocks.From(document.Body.Blocks));
        Assert.False(planner.HasPlan(target));
    }

    [Fact]
    public void NumberedBlockIsPlannedAgainWhenTheStateBeforeItDiffers()
    {
        // Three items and a plain paragraph between them; an item inserted in front shifts the label of every later item but not the plain block.
        var plain = "<w:p><w:r><w:t>x</w:t></w:r></w:p>";
        var document = LayoutModel.Document(Item(0) + plain + Item(0) + Item(0), LayoutModel.StylesPart(Plain), LayoutModel.NumberingPart(Numbering()));
        var planner = PlannerFor(document);
        var start = planner.CurrentState();
        var first = FlatBlocks.From(document.Body.Blocks);
        var plans = first.Select(planner.PlanFor).ToList();
        Assert.Equal(["1.", null, "2.", "3."], plans.Select(LabelOf));

        var inserted = LayoutModel.Document(Item(0), LayoutModel.StylesPart(Plain), LayoutModel.NumberingPart(Numbering())).Body.Blocks[0];
        document.Body.Blocks.Insert(0, inserted);
        var second = FlatBlocks.From(document.Body.Blocks);
        planner.RestoreState(start.Numbers, start.Notes);
        var made = planner.PlansMade;
        var again = second.Select(planner.PlanFor).ToList();

        Assert.Equal(["1.", "2.", null, "3.", "4."], again.Select(LabelOf));
        Assert.Same(plans[1], again[2]);
        Assert.Equal(made + 4, planner.PlansMade);
        Assert.NotSame(plans[0], again[1]);
    }
}
