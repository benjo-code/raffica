using System.Globalization;
using Raffica.TestSupport;
using Raffica.Write.Docx;
using Raffica.Write.Editing;
using Raffica.Write.Model;
using Raffica.Write.Tests.Layout;
using Xunit;

namespace Raffica.Write.Tests.Editing;

/// <summary>
/// The oracle of the recovery snapshot's block cache: after every operation of a long seeded sequence of real editor operations (typing, Enter, multi-paragraph delete, backspace, copy and
/// paste, bold, alignment, undo and redo, in the body, in tables and content controls, in headers and notes) the main part the cache gives must be byte for byte the main part a user save
/// writes. A snapshot is not taken after every operation: the gap is random, so the dirty ranges of several operations add up as they do between two real copies.
/// </summary>
public sealed class SnapshotCacheOracleTests
{
    private static string Root => Path.Combine(TestData.Root, "testdata", "write-corpus");

    private static int Seed(string id)
    {
        uint hash = 2166136261;
        foreach (var c in id)
        {
            hash = (hash ^ c) * 16777619;
        }

        return (int)(hash & 0x7fffffff);
    }

    public static TheoryData<string, int> Files() => new()
    {
        { "poi-bug54849", 150 },
        { "poi-60316", 150 },
        { "poi-table-footnotes", 150 },
        { "poi-test-document", 150 },
        { "poi-complex-numbered-lists", 150 },
        { "poi-page-specific-head-foot", 100 },
        { "thesis", 60 },
    };

    [Theory]
    [MemberData(nameof(Files))]
    public void TheCacheEqualsAFullWriteAcrossARandomEditSequence(string id, int operations)
    {
        using var file = DocxFile.Open(Path.Combine(Root, id, id + ".docx"));
        var model = file.Model;
        var editor = new TextEditor(model, (_, _) => ProjectionHost.Plain) { FragmentCodec = new DocxFragmentCodec() };
        var seed = Seed(id);
        var random = new Random(seed);
        ClipboardContent? clip = null;
        var log = new List<string>();
        var reused = 0;
        var snapshots = 0;

        void Check(string label)
        {
            var cached = file.SerialiseMainForTest(cached: true);
            var full = file.SerialiseMainForTest(cached: false);
            Assert.True(cached.AsSpan().SequenceEqual(full), $"{label}: the cached main part differs from a full write ({cached.Length} against {full.Length} bytes)");
            snapshots++;
            reused += file.LastSnapshotPass.Reused;
        }

        Check($"{id} seed {seed} first");
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

            var body = all.Where(a => a.Story.Kind == StoryKind.Body).ToList();
            var pick = random.Next(10) < 8 && body.Count > 0 ? body[random.Next(body.Count)] : all[random.Next(all.Count)];
            int Len(StoryRef story, Paragraph paragraph) => ParagraphProjection.Build(paragraph, ProjectionHost.Plain).Text.Length;
            var at = new TextPosition(pick.Story, pick.Paragraph, random.Next(Len(pick.Story, pick.Paragraph) + 1));
            var kind = random.Next(16);
            if (kind is 3 or 5 && pick.Story.Kind != StoryKind.Body)
            {
                kind = 0;
            }

            EditResult? result = null;
            string what;
            switch (kind)
            {
                case 0:
                case 1:
                    what = "type";
                    result = editor.InsertText(new TextRange(at, at), random.Next(2) == 0 ? "x" : "some words that were typed ");
                    break;
                case 2:
                    what = "enter";
                    result = editor.InsertText(new TextRange(at, at), "\n");
                    break;
                case 3:
                    {
                        what = "multi-delete";
                        var (container, index) = TestEdit.Find(model, pick.Paragraph);
                        var j = Math.Min(container.Count - 1, index + 1 + random.Next(5));
                        while (j > index && container[j] is not Paragraph)
                        {
                            j--;
                        }

                        if (container[j] is Paragraph end && !ReferenceEquals(end, pick.Paragraph))
                        {
                            result = editor.Delete(new TextRange(at, new TextPosition(pick.Story, end, random.Next(Len(pick.Story, end) + 1))));
                        }

                        break;
                    }

                case 4:
                    what = "backspace";
                    result = editor.Backspace(at with { Offset = random.Next(2) == 0 ? 0 : at.Offset }, DeleteUnit.Cluster);
                    if (result is { Changes.Count: 0, Selected: { } selected })
                    {
                        result = editor.Delete(selected);
                    }

                    break;
                case 5:
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

                case 6:
                    what = "paste";
                    if (clip is not null)
                    {
                        result = editor.Paste(new TextRange(at, at), clip, _ => true);
                    }

                    break;
                case 7:
                    {
                        what = "bold";
                        var end = new TextPosition(pick.Story, pick.Paragraph, Len(pick.Story, pick.Paragraph));
                        result = editor.ApplyCharacterChanges(new TextRange(at with { Offset = 0 }, end), FormatChanges.Toggle(CharacterToggle.Bold, on: random.Next(2) == 0));
                        break;
                    }

                case 8:
                    what = "align";
                    result = editor.ApplyParagraphChanges(new TextRange(at, at), ParagraphChanges.Alignment((ParagraphJustification)random.Next(4)));
                    break;
                case 9:
                case 10:
                    what = "undo";
                    result = editor.History.Undo();
                    break;
                case 12:
                    what = "list";
                    result = editor.ToggleList(new TextRange(at, at), random.Next(2) == 0 ? ListKind.Bullet : ListKind.Numbered);
                    break;
                case 13:
                    what = "list-level";
                    result = editor.ChangeListLevel(new TextRange(at, at), random.Next(3) - 1);
                    break;
                case 14:
                    what = "list-restart";
                    result = editor.RestartNumbering(at);
                    break;
                case 15:
                    what = "list-continue";
                    result = editor.ContinueNumbering(at);
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

            if (random.Next(3) == 0)
            {
                continue;
            }

            Check($"{id} seed {seed} step {step} ({string.Join(",", log.TakeLast(12))})");
        }

        Check($"{id} seed {seed} last");
        Assert.True(reused > 0, $"{id}: the cache never reused a block in {snapshots} snapshots");
    }

    [Fact]
    public void ARaiseWithoutItsBlockRangesWritesTheWholeBodyAgain()
    {
        using var file = DocxFile.Open(Path.Combine(Root, "poi-test-document", "poi-test-document.docx"));
        var model = file.Model;
        file.SerialiseMainForTest(cached: true);
        file.SerialiseMainForTest(cached: true);
        Assert.Equal(0, file.LastSnapshotPass.Written);

        // A change nobody reported: the revision moved (as the restore of a copy does) and a paragraph changed in place.
        var paragraph = TestEdit.Paragraphs(model).First(p => ReferenceEquals(p.Container, model.Body.Blocks) && TestEdit.TextOf(p.Paragraph).Length > 0).Paragraph;
        var editor = new TextEditor(model, (_, _) => ProjectionHost.Plain);
        editor.InsertText(new TextRange(new TextPosition(StoryRef.Body, paragraph, 0), new TextPosition(StoryRef.Body, paragraph, 0)), "q");
        model.Edits.Raise();
        var cached = file.SerialiseMainForTest(cached: true);
        Assert.Equal(model.Body.Blocks.Count, file.LastSnapshotPass.Written);
        Assert.True(cached.AsSpan().SequenceEqual(file.SerialiseMainForTest(cached: false)));
    }

    [Fact]
    public void ATypedParagraphIsTheOnlyBlockWrittenAgain()
    {
        using var file = DocxFile.Open(Path.Combine(Root, "thesis", "thesis.docx"));
        var model = file.Model;
        var editor = new TextEditor(model, (_, _) => ProjectionHost.Plain);
        file.SerialiseMainForTest(cached: true);
        Assert.Equal(model.Body.Blocks.Count, file.LastSnapshotPass.Written);

        var paragraph = model.Body.Blocks.OfType<Paragraph>().First(p => TestEdit.TextOf(p).Length > 20);
        var at = new TextPosition(StoryRef.Body, paragraph, 5);
        editor.InsertText(new TextRange(at, at), "typed");
        var cached = file.SerialiseMainForTest(cached: true);
        Assert.Equal(1, file.LastSnapshotPass.Written);
        Assert.Equal(model.Body.Blocks.Count - 1, file.LastSnapshotPass.Reused);
        Assert.True(cached.AsSpan().SequenceEqual(file.SerialiseMainForTest(cached: false)));
    }
}
