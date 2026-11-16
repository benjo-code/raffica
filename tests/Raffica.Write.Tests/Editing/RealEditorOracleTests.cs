using System.Globalization;
using Raffica.TestSupport;
using Raffica.Write.Docx;
using Raffica.Write.Editing;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using Raffica.Write.Tests.Layout;
using WriteCorpus;
using Xunit;

namespace Raffica.Write.Tests.Editing;

/// <summary>
/// The oracle of the incremental layout with the REAL editor: every corpus file, in both views, gets seeded operations of the real <see cref="TextEditor"/> (with the layout's projection
/// host) and its history, applied through <see cref="DocumentLayout.ApplyEdit"/>; after every operation that changed something the layout must equal a full layout of the edited model.
/// At the end the file is saved, read again and laid out again, and the reopened document must have the text and the layout of the edited model (spec section 9: "an edited thesis saved,
/// read again and laid out equals the edited model's layout"). Operations inside block content controls are forced first where a file has any.
/// The seed is an FNV-1a hash of the file id plus the mode, never <c>GetHashCode</c>; the label of every failure carries the seed and the step.
/// The thesis gets fewer operations (each one is checked against a full layout of 311 pages) to keep the class near a minute.
/// </summary>
public sealed class RealEditorOracleTests
{
    private const int Operations = 30;
    private const int ThesisOperations = 14;

    private static string Root => Path.Combine(TestData.Root, "testdata", "write-corpus");

    public static TheoryData<string, LayoutMode> Cases()
    {
        var data = new TheoryData<string, LayoutMode>();
        foreach (var entry in CorpusManifest.Load(Root).Files)
        {
            data.Add(entry.Id, LayoutMode.Pages);
            data.Add(entry.Id, LayoutMode.Flow);
        }

        return data;
    }

    private static int Seed(string id, LayoutMode mode)
    {
        uint hash = 2166136261;
        foreach (var c in id)
        {
            hash = (hash ^ c) * 16777619;
        }

        return (int)(hash & 0x7fffffff) + (int)mode;
    }

    private static bool InControl(IList<Block> blocks, IList<Block> container)
    {
        foreach (var block in blocks)
        {
            switch (block)
            {
                case ContentControlBlock control:
                    if (ReferenceEquals(control.Content, container) || InControl(control.Content, container))
                    {
                        return true;
                    }

                    break;
                case Table table:
                    foreach (var row in table.Rows)
                    {
                        if (RowHas(row, container))
                        {
                            return true;
                        }
                    }

                    break;
                default:
                    break;
            }
        }

        return false;
    }

    private static bool RowHas(RowItem item, IList<Block> container) => item switch
    {
        TableRow row => row.Items.Any(i => RowHas(i, container)),
        TableCell cell => InControl(cell.Blocks, container),
        _ => false,
    };

    // The paragraphs of the body whose own list is the content of a block content control (at any depth).
    private static List<Paragraph> ControlParagraphs(WordDocument model) =>
        [.. TestEdit.Paragraphs(model).Where(p => !ReferenceEquals(p.Container, model.Body.Blocks) && InControl(model.Body.Blocks, p.Container)).Select(p => p.Paragraph)];

    [Fact]
    public void TheCorpusHasParagraphsInsideContentControls()
    {
        foreach (var id in new[] { "poi-bug54849", "poi-60316" })
        {
            using var file = DocxFile.Open(Path.Combine(Root, id, id + ".docx"));
            Assert.NotEmpty(ControlParagraphs(file.Model));
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task RealEditorOperationsStayEqualToFullLayoutAndSurviveASave(string id, LayoutMode mode)
    {
        var token = TestContext.Current.CancellationToken;
        using var file = DocxFile.Open(Path.Combine(Root, id, id + ".docx"));
        var model = file.Model;
        var options = new LayoutOptions(mode);
        var layout = DocumentLayout.Create(model, PageTestKit.Fonts, options);
        await layout.LayOutRestAsync(null, token);
        var editor = new TextEditor(model, (s, p) => layout.ProjectionHost(s, p)) { FragmentCodec = new DocxFragmentCodec() };
        var seed = Seed(id, mode);
        var random = new Random(seed);
        var operations = id == "thesis" ? ThesisOperations : Operations;
        ClipboardContent? clip = null;
        var log = new List<string>();
        var forcedInControl = 0;
        var hadControls = ControlParagraphs(model).Count > 0;

        int Len(StoryRef story, Paragraph paragraph) => ParagraphProjection.Build(paragraph, layout.ProjectionHost(story, paragraph)).Text.Length;

        for (var step = 0; step < operations; step++)
        {
            var all = new List<(StoryRef Story, Paragraph Paragraph)>();
            foreach (var (paragraph, _, _) in TestEdit.Paragraphs(model))
            {
                all.Add((StoryRef.Body, paragraph));
            }

            foreach (var (headerId, part) in model.HeadersFooters)
            {
                foreach (var paragraph in part.Content.Blocks.OfType<Paragraph>())
                {
                    all.Add((new StoryRef(StoryKind.HeaderFooter, headerId), paragraph));
                }
            }

            foreach (var note in model.Footnotes?.Notes ?? [])
            {
                if (note.Kind == NoteKind.Normal)
                {
                    foreach (var paragraph in note.Content.Blocks.OfType<Paragraph>())
                    {
                        all.Add((new StoryRef(StoryKind.Note, note.Id.ToString(CultureInfo.InvariantCulture)), paragraph));
                    }
                }
            }

            if (all.Count == 0)
            {
                return;
            }

            var body = all.Where(a => a.Story.Kind == StoryKind.Body).ToList();
            var pick = random.Next(10) < 7 && body.Count > 0 ? body[random.Next(body.Count)] : all[random.Next(all.Count)];
            var kind = random.Next(9);

            // The first steps of a file with content controls work inside them: type, Enter, delete across paragraphs, backspace at a start, copy and paste, undo.
            var controls = ControlParagraphs(model);
            var forced = step < 7 && controls.Count > 0;
            if (forced)
            {
                pick = (StoryRef.Body, controls[random.Next(controls.Count)]);
                kind = step switch { 0 => 0, 1 => 1, 2 => 2, 3 => 3, 4 => 4, 5 => 5, _ => 6 };
                forcedInControl++;
            }

            if (kind is 2 or 4 && pick.Story.Kind != StoryKind.Body)
            {
                kind = 0;
            }

            var at = new TextPosition(pick.Story, pick.Paragraph, random.Next(Len(pick.Story, pick.Paragraph) + 1));
            EditResult? result = null;
            string what;
            switch (kind)
            {
                case 0:
                    what = "type";
                    result = editor.InsertText(new TextRange(at, at), random.Next(2) == 0 ? "x" : string.Concat(Enumerable.Repeat("wrapping words here ", 12)));
                    break;
                case 1:
                    what = "enter";
                    result = editor.InsertText(new TextRange(at, at), "\n");
                    break;
                case 2:
                {
                    what = "multi-delete";

                    // A range from this paragraph to one 1-6 blocks later in the same list (tables in between go too).
                    var (container, index) = TestEdit.Find(model, pick.Paragraph);
                    var j = Math.Min(container.Count - 1, index + 1 + random.Next(6));
                    while (j > index && container[j] is not Paragraph)
                    {
                        j--;
                    }

                    if (container[j] is Paragraph end && !ReferenceEquals(end, pick.Paragraph))
                    {
                        var to = new TextPosition(pick.Story, end, random.Next(Len(pick.Story, end) + 1));
                        result = editor.Delete(new TextRange(at, to));
                    }

                    break;
                }

                case 3:
                    what = "backspace";
                    result = editor.Backspace(at with { Offset = random.Next(2) == 0 ? 0 : at.Offset }, DeleteUnit.Cluster);
                    if (result is { Changes.Count: 0, Selected: { } selected })
                    {
                        result = editor.Delete(selected);
                    }

                    break;
                case 4:
                {
                    what = "copy";
                    var (container, index) = TestEdit.Find(model, pick.Paragraph);
                    var j = Math.Min(container.Count - 1, index + random.Next(4));
                    while (j > index && container[j] is not Paragraph)
                    {
                        j--;
                    }

                    var end = (Paragraph)container[j];
                    clip = editor.Copy(new TextRange(at with { Offset = 0 }, new TextPosition(pick.Story, end, Len(pick.Story, end))));
                    break;
                }

                case 5:
                    what = "paste";
                    if (clip is not null)
                    {
                        result = editor.Paste(new TextRange(at, at), clip, _ => true);
                    }

                    break;
                case 6:
                case 7:
                    what = "undo";
                    result = editor.History.Undo();
                    break;
                default:
                    what = "redo";
                    result = editor.History.Redo();
                    break;
            }

            log.Add(what);
            if (result is null || result.Changes.Count == 0)
            {
                continue;
            }

            var label = $"{id} {mode} seed {seed} step {step} ({string.Join(",", log)})";
            foreach (var change in result.Changes)
            {
                layout.ApplyEdit(change, 2);
            }

            await layout.LayOutRestAsync(null, token);
            LayoutOracle.AssertEqualToFull(layout, model, options, label);
        }

        if (hadControls)
        {
            Assert.True(forcedInControl > 0, $"{id} {mode} seed {seed}: no operation ran inside a content control");
        }

        // Save, read again, lay out again: the same text and the same layout as the edited model.
        var temp = Path.Combine(Path.GetTempPath(), "raffica-realedit-" + Guid.NewGuid().ToString("N") + ".docx");
        try
        {
            await file.SaveAsync(temp, token);
            using var reopened = DocxFile.Open(temp);
            var edited = string.Join("\n", TestEdit.Paragraphs(model).Select(p => TestEdit.TextOf(p.Paragraph)));
            var again = string.Join("\n", TestEdit.Paragraphs(reopened.Model).Select(p => TestEdit.TextOf(p.Paragraph)));
            Assert.Equal(edited, again);

            var first = DocumentLayout.Create(model, PageTestKit.Fonts, options);
            await first.LayOutRestAsync(null, token);
            var second = DocumentLayout.Create(reopened.Model, PageTestKit.Fonts, options);
            await second.LayOutRestAsync(null, token);
            var where = $"{id} {mode} seed {seed} after save and reopen";
            Assert.True(first.LaidOutPages == second.LaidOutPages, $"{where}: pages {first.LaidOutPages} against {second.LaidOutPages}");
            for (var i = 0; i < first.LaidOutPages; i++)
            {
                Assert.True(first.Page(i).Text == second.Page(i).Text, $"{where}: text of page {i} differs");
                Assert.True(first.Page(i).Items.Count == second.Page(i).Items.Count, $"{where}: item count of page {i} differs");
            }
        }
        finally
        {
            File.Delete(temp);
        }
    }
}
