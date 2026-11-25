using System.Globalization;
using System.Xml.Linq;
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
        var shifted = 0;

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
            var kind = random.Next(18);
            if (id == "thesis" && step is 5 or 31)
            {
                kind = 17;
            }

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
                case 17:
                    {
                        what = "outline-shift";
                        var top = model.Body.Blocks;
                        string? StyleOf(int index) => (top[index] as Paragraph)?.Properties.StyleId;

                        // A range around a heading that has a legal shift (the corpus files define few heading styles), else a random one that is usually refused.
                        var candidates = Enumerable.Range(0, top.Count).Where(i => StyleOf(i) is "Heading2" or "Heading3").ToList();
                        var from = candidates.Count > 0 ? candidates[random.Next(candidates.Count)] : random.Next(top.Count);
                        var to = candidates.Count > 0 ? from : Math.Min(top.Count - 1, from + random.Next(6));
                        result = editor.ShiftOutlineLevels(from, to, -1);
                        if (result is not null && result.Changes.Count > 0)
                        {
                            shifted++;
                        }

                        break;
                    }

                case 16:
                    {
                        what = "move";
                        var blocks = model.Body.Blocks;
                        var count = Math.Min(1 + random.Next(4), blocks.Count);
                        var from = random.Next(blocks.Count - count + 1);
                        var target = random.Next(blocks.Count + 1);
                        result = editor.MoveBlocks(from, count, target);
                        break;
                    }

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
        Assert.True(id != "thesis" || shifted > 0, "no outline shift ran in the sequence");
    }

    [Fact]
    public void AnOutlineShiftAndItsUndoAndRedoGiveTheBytesOfAFullWrite()
    {
        using var file = DocxFile.Open(Path.Combine(Root, "thesis", "thesis.docx"));
        var model = file.Model;
        var blocks = model.Body.Blocks;
        var editor = new TextEditor(model, (_, _) => ProjectionHost.Plain);
        file.SerialiseMainForTest(cached: true);

        void Check(string label)
        {
            var cached = file.SerialiseMainForTest(cached: true);
            Assert.True(cached.AsSpan().SequenceEqual(file.SerialiseMainForTest(cached: false)), label);
        }

        // A run of blocks from a Heading2 up to the next Heading1: a promotion is legal for every heading in it (the thesis defines Heading1 to Heading3).
        static string? Style(Block block) => (block as Paragraph)?.Properties.StyleId;
        var first = Enumerable.Range(0, blocks.Count).First(i => Style(blocks[i]) == "Heading2");
        var last = first;
        while (last + 1 < blocks.Count && Style(blocks[last + 1]) != "Heading1")
        {
            last++;
        }

        var headings = Enumerable.Range(first, last - first + 1).Count(i => Style(blocks[i]) is "Heading2" or "Heading3");
        Assert.True(headings >= 2);
        Assert.NotNull(editor.ShiftOutlineLevels(first, last, -1));
        Check("promote");
        Assert.NotNull(editor.History.Undo());
        Check("undo");
        Assert.NotNull(editor.History.Redo());
        Check("redo");
        Assert.NotNull(editor.ShiftOutlineLevels(first, first, 1));
        Check("demote");
        Assert.NotNull(editor.History.Undo());
        Assert.NotNull(editor.History.Undo());
        Check("undo twice");
    }

    [Fact]
    public void AMoveAndItsUndoAndRedoGiveTheBytesOfAFullWrite()
    {
        using var file = DocxFile.Open(Path.Combine(Root, "poi-test-document", "poi-test-document.docx"));
        var model = file.Model;
        var editor = new TextEditor(model, (_, _) => ProjectionHost.Plain);
        file.SerialiseMainForTest(cached: true);

        void Check(string label)
        {
            var cached = file.SerialiseMainForTest(cached: true);
            Assert.True(cached.AsSpan().SequenceEqual(file.SerialiseMainForTest(cached: false)), label);
        }

        Assert.NotNull(editor.MoveBlocks(1, 3, model.Body.Blocks.Count));
        Check("move down");
        Assert.NotNull(editor.History.Undo());
        Check("undo");
        Assert.NotNull(editor.History.Redo());
        Check("redo");
        Assert.NotNull(editor.MoveBlocks(model.Body.Blocks.Count - 2, 2, 0));
        Check("move up");
        Assert.NotNull(editor.History.Undo());
        Assert.NotNull(editor.History.Undo());
        Check("undo twice");
    }

    [Fact]
    public void AMovedChapterThatHoldsASectionBreakGivesTheBytesOfAFullWriteForTheMoveAndItsUndoAndRedo()
    {
        using var file = DocxFile.Open(Path.Combine(Root, "poi-page-specific-head-foot", "poi-page-specific-head-foot.docx"));
        var model = file.Model;
        var editor = new TextEditor(model, (_, _) => ProjectionHost.Plain);
        file.SerialiseMainForTest(cached: true);
        var carrier = model.Body.Blocks.OfType<Paragraph>().Skip(2).First();
        Assert.NotNull(editor.InsertSectionBreak(new TextPosition(StoryRef.Body, carrier, 0), SectionBreakKind.Continuous));
        var breakAt = model.Body.Blocks.ToList().FindIndex(b => b is Paragraph { SectionBreak: not null });
        Assert.True(breakAt >= 1, "the file has no section break paragraph to move");

        void Check(string label)
        {
            var cached = file.SerialiseMainForTest(cached: true);
            Assert.True(cached.AsSpan().SequenceEqual(file.SerialiseMainForTest(cached: false)), label);
        }

        // A range that ends in the break paragraph moves past the block after it, then a range from after it goes to the top.
        Check("break inserted");
        Assert.NotNull(editor.MoveBlocks(breakAt - 1, 2, breakAt + 2));
        Check("move across the break");
        Assert.NotNull(editor.History.Undo());
        Check("undo");
        Assert.NotNull(editor.History.Redo());
        Check("redo");
        Assert.NotNull(editor.MoveBlocks(breakAt, 2, 0));
        Check("move the break to the top");
        Assert.NotNull(editor.History.Undo());
        Assert.NotNull(editor.History.Undo());
        Check("undo twice");
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

    private sealed class StillClock : TimeProvider
    {
        public override long TimestampFrequency => 1000;

        public override long GetTimestamp() => 0;
    }

    [Fact]
    public async Task ASectionSwapAndItsUndoAndRedoGiveTheBytesOfAFullWrite()
    {
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        using var file = DocxFile.Open(Path.Combine(Root, "poi-page-specific-head-foot", "poi-page-specific-head-foot.docx"));
        var model = file.Model;
        Assert.NotNull(model.Settings);
        // The file has no section break: a paragraph in the middle gets one (a body edit, attributed as the editor does), so the carrier path is covered.
        var middle = model.Body.Blocks.OfType<Paragraph>().ElementAt(model.Body.Blocks.OfType<Paragraph>().Count() / 2);
        middle.SectionBreak = model.Body.FinalSection!.Clone();
        model.Edits.Raise();
        model.Edits.Attribute([new BlockChange(StoryRef.Body, model.Body.Blocks, model.Body.Blocks.IndexOf(middle), 1, 1, [middle])]);
        var sections = SectionList.Of(model);
        var carrier = sections.First(s => s.Break is not null);
        var history = new EditHistory(model, new StillClock(), HistoryLimits.Default);
        using var folder = new Support.TempFolder();
        var copies = 0;
        file.SerialiseMainForTest(cached: true);

        async Task CheckAsync(string label)
        {
            var cached = file.SerialiseMainForTest(cached: true);
            Assert.True(cached.AsSpan().SequenceEqual(file.SerialiseMainForTest(cached: false)), label + ": main part");

            // The whole copy: the settings part carries the flag the model has.
            var target = folder.File($"copy{copies++}.docx");
            await file.SaveSnapshotAsync(target, TestContext.Current.CancellationToken);
            using var zip = System.IO.Compression.ZipFile.OpenRead(target);
            using var reader = new StreamReader(zip.GetEntry("word/settings.xml")!.Open());
            Assert.Equal(model.Settings!.EvenAndOddHeaders, (await reader.ReadToEndAsync(TestContext.Current.CancellationToken)).Contains("evenAndOddHeaders", StringComparison.Ordinal));
        }

        await CheckAsync("before");
        var finalProperties = sections[^1].Properties;
        var swaps = new List<SectionSwap>();
        foreach (var live in new[] { finalProperties, carrier.Properties })
        {
            var old = live.Clone();
            if (ReferenceEquals(live, finalProperties))
            {
                live.Set(w + "pgNumType", new XElement(w + "pgNumType", new XAttribute(w + "start", "7")));
            }
            else
            {
                live.Get(w + "pgSz")!.SetAttributeValue(w + "w", "7000");
            }

            swaps.Add(new SectionSwap(live, old, live.Clone()));
        }

        var flagBefore = model.Settings!.EvenAndOddHeaders;
        var revision = history.BeginOperation();
        model.Settings.EvenAndOddHeaders = !flagBefore;
        model.Edits.Raise();
        var change = new SectionChange(swaps, flagBefore, !flagBefore);
        var caret = new TextPosition(StoryRef.Body, model.Body.Blocks.OfType<Paragraph>().First(), 0);
        var forward = new EditResult(caret, [], Sections: change);
        forward.Attribute(model);
        history.Record(forward, new EditHistory.Point(0, 0), revision, null);
        await CheckAsync("swap");
        Assert.NotNull(history.Undo());
        await CheckAsync("undo");
        Assert.NotNull(history.Redo());
        await CheckAsync("redo");
        Assert.NotNull(history.Undo());
        await CheckAsync("undo again");
    }

    [Fact]
    public async Task TheSectionCommandsAndTheirUndoAndRedoGiveTheBytesOfAFullWrite()
    {
        using var file = DocxFile.Open(Path.Combine(Root, "poi-page-specific-head-foot", "poi-page-specific-head-foot.docx"));
        var model = file.Model;
        var editor = new TextEditor(model, (_, _) => ProjectionHost.Plain, new StillClock());
        var paragraphs = model.Body.Blocks.OfType<Paragraph>().ToList();
        Assert.True(paragraphs.Count >= 4);
        using var folder = new Support.TempFolder();
        file.SerialiseMainForTest(cached: true);

        void Check(string label)
        {
            var cached = file.SerialiseMainForTest(cached: true);
            Assert.True(cached.AsSpan().SequenceEqual(file.SerialiseMainForTest(cached: false)), label + ": main part");
        }

        TextPosition At(Paragraph paragraph) => new(StoryRef.Body, paragraph, 0);

        Check("before");
        Assert.NotNull(editor.NumberPagesFrom(At(paragraphs[2]), new PageNumbering(1, PageNumberFormat.UpperRoman, "A-")));
        Check("number from here");
        Assert.NotNull(editor.InsertSectionBreak(At(paragraphs[1]), SectionBreakKind.Continuous));
        Check("break");
        Assert.NotNull(editor.NumberPagesFrom(At(paragraphs[0]), new PageNumbering(4, PageNumberFormat.Arabic, string.Empty)));
        Check("number the first section");
        Assert.NotNull(editor.RemoveSectionBreak(1));
        Check("remove");
        for (var i = 0; i < 4; i++)
        {
            Assert.NotNull(editor.History.Undo());
            Check("undo " + i);
        }

        for (var i = 0; i < 4; i++)
        {
            Assert.NotNull(editor.History.Redo());
            Check("redo " + i);
        }

        // A copy written now holds the same body as a full save of the model.
        var target = folder.File("copy.docx");
        await file.SaveSnapshotAsync(target, TestContext.Current.CancellationToken);
        using var reopened = DocxFile.Open(target);
        Assert.Equal(SectionList.Of(model).Count, SectionList.Of(reopened.Model).Count);
    }

    [Fact]
    public void PageSetupAndItsUndoAndRedoGiveTheBytesOfAFullWrite()
    {
        using var file = DocxFile.Open(Path.Combine(Root, "poi-page-specific-head-foot", "poi-page-specific-head-foot.docx"));
        var model = file.Model;
        var editor = new TextEditor(model, (_, _) => ProjectionHost.Plain, new StillClock());
        var paragraphs = model.Body.Blocks.OfType<Paragraph>().ToList();
        Assert.True(paragraphs.Count >= 4);
        file.SerialiseMainForTest(cached: true);

        void Check(string label)
        {
            var cached = file.SerialiseMainForTest(cached: true);
            Assert.True(cached.AsSpan().SequenceEqual(file.SerialiseMainForTest(cached: false)), label + ": main part");
        }

        TextPosition At(Paragraph paragraph) => new(StoryRef.Body, paragraph, 0);
        var a3 = PagePresets.Size("A3");
        var big = SectionPageSetup.Read(null) with { WidthTwips = a3.WidthTwips, HeightTwips = a3.HeightTwips, Columns = 2 };

        Check("before");
        Assert.NotNull(editor.ApplyPageSetup(At(paragraphs[2]), new PageSetupChange(big, PageSetupParts.Size | PageSetupParts.Columns), ApplyScope.FromHere));
        Check("from here");
        Assert.NotNull(editor.ApplyPageSetup(At(paragraphs[1]), new PageSetupChange(big.WithLandscape(true), PageSetupParts.Orientation), ApplyScope.WholeDocument));
        Check("whole document");
        Assert.NotNull(editor.ApplyPageSetup(At(paragraphs[0]), new PageSetupChange(big with { LeftTwips = 900 }, PageSetupParts.Margins), ApplyScope.ThisSection));
        Check("this section");
        for (var i = 0; i < 3; i++)
        {
            Assert.NotNull(editor.History.Undo());
            Check("undo " + i);
        }

        for (var i = 0; i < 3; i++)
        {
            Assert.NotNull(editor.History.Redo());
            Check("redo " + i);
        }
    }

    [Fact]
    public async Task InsertNoteAndDeleteReferenceAndTheirUndoAndRedoGiveTheBytesOfAFullWrite()
    {
        using var file = DocxFile.Open(Path.Combine(Root, "poi-test-document", "poi-test-document.docx"));
        var model = file.Model;
        var layout = await SectionOracleTests.LaidAsync(model);
        var editor = new TextEditor(model, (s, p) => layout.ProjectionHost(s, p));
        var paragraphs = model.Body.Blocks.OfType<Paragraph>().Where(p => p.Inlines.Count > 0).ToList();
        Assert.True(paragraphs.Count >= 3);
        file.SerialiseMainForTest(cached: true);

        void Check(string label)
        {
            var cached = file.SerialiseMainForTest(cached: true);
            Assert.True(cached.AsSpan().SequenceEqual(file.SerialiseMainForTest(cached: false)), label + ": main part");
        }

        var holderIndex = model.Body.Blocks.IndexOf(paragraphs[1]);
        Check("before");
        var inserted = editor.InsertNote(new TextPosition(StoryRef.Body, paragraphs[1], 0), NoteType.Footnote);
        Assert.NotNull(inserted);
        Check("insert");
        Assert.NotNull(editor.InsertNote(new TextPosition(StoryRef.Body, paragraphs[2], 0), NoteType.Endnote));
        Check("insert endnote");
        for (var i = 0; i < 2; i++)
        {
            Assert.NotNull(editor.History.Undo());
            Check("undo " + i);
        }

        for (var i = 0; i < 2; i++)
        {
            Assert.NotNull(editor.History.Redo());
            Check("redo " + i);
        }

        // Delete the paragraph that holds a reference: the note goes with it, and comes back with the undo.
        var holder = (Paragraph)model.Body.Blocks[holderIndex];
        Assert.NotNull(editor.Delete(new TextRange(new TextPosition(StoryRef.Body, holder, 0), new TextPosition(StoryRef.Body, holder, int.MaxValue))));
        Check("delete");
        Assert.NotNull(editor.History.Undo());
        Check("undo delete");
        Assert.NotNull(editor.History.Redo());
        Check("redo delete");
    }
    [Fact]
    public void TableOperationsAndTheirUndoAndRedoGiveTheBytesOfAFullWrite()
    {
        using var folder = new Support.TempFolder();
        var path = folder.File("tables.docx");
        Support.DocxBuilder.WithBody(path, TableCorpusStructureTests.GeneratedBody());
        using var file = DocxFile.Open(path);
        var model = file.Model;
        var editor = new TextEditor(model, (_, _) => ProjectionHost.Plain, new StillClock());
        file.SerialiseMainForTest(cached: true);

        void Check(string label)
        {
            var cached = file.SerialiseMainForTest(cached: true);
            Assert.True(cached.AsSpan().SequenceEqual(file.SerialiseMainForTest(cached: false)), label + ": main part");
        }

        // The tables are the blocks 1, 3, 5 and 7 of the generated body.
        var merged = new TableRef(StoryRef.Body, 1);
        var plain = new TableRef(StoryRef.Body, 3);
        (string Label, Func<EditResult?> Run)[] steps =
        [
            ("add row", () => editor.AddRow(new TableRef(StoryRef.Body, 3), 1, after: true)),
            ("add column", () => editor.AddColumn(new TableRef(StoryRef.Body, 3), 1, after: false)),
            ("merge", () => editor.MergeCells(new CellRange(new TableRef(StoryRef.Body, 3), 0, 0, 1, 1))),
            ("split", () => editor.SplitCell(new TableRef(StoryRef.Body, 3), 0, 0)),
            ("move row", () => editor.MoveRow(new TableRef(StoryRef.Body, 5), 0, 1)),
            ("delete rows", () => editor.DeleteRows(new CellRange(new TableRef(StoryRef.Body, 5), 1, 0, 1, 0))),
            ("delete columns", () => editor.DeleteColumns(new CellRange(new TableRef(StoryRef.Body, 5), 0, 0, 0, 0))),
            ("delete table", () => editor.DeleteTable(new TableRef(StoryRef.Body, 5))),
            ("insert", () => editor.InsertTable(new TextPosition(StoryRef.Body, (Paragraph)model.Body.Blocks[0], 2), 3, 2)),
        ];
        Assert.NotNull(merged.Find(model.Body.Blocks));
        Assert.NotNull(plain.Find(model.Body.Blocks));

        Check("before");
        foreach (var (label, run) in steps)
        {
            Assert.True(run() is not null, label + " was refused");
            Check(label);
        }

        for (var i = 0; i < steps.Length; i++)
        {
            Assert.NotNull(editor.History.Undo());
            Check("undo " + i);
        }

        for (var i = 0; i < steps.Length; i++)
        {
            Assert.NotNull(editor.History.Redo());
            Check("redo " + i);
        }
    }

    [Fact]
    public void PictureOperationsAndTheirUndoAndRedoGiveTheBytesOfAFullWrite()
    {
        using var folder = new Support.TempFolder();
        var path = folder.File("pictures.docx");
        Support.DocxBuilder.WithBody(path, Layout.ObjectOracleTests.PictureBody());
        using var file = DocxFile.Open(path);
        var model = file.Model;
        var editor = new TextEditor(model, (_, _) => ProjectionHost.Plain, new StillClock());
        file.SerialiseMainForTest(cached: true);

        void Check(string label)
        {
            var cached = file.SerialiseMainForTest(cached: true);
            Assert.True(cached.AsSpan().SequenceEqual(file.SerialiseMainForTest(cached: false)), label + ": main part");
        }

        ObjectId IdAt(int block, int ordinal = 0)
        {
            var paragraph = (Paragraph)model.Body.Blocks[block];
            var picture = paragraph.Inlines.OfType<Run>().SelectMany(r => r.Content).OfType<Picture>().ElementAt(ordinal);
            var run = paragraph.Inlines.OfType<Run>().First(r => r.Content.Contains(picture));
            return ObjectIds.Of(StoryRef.Body, model.Body.Blocks, paragraph, run, picture)!;
        }

        // Block 8 holds a cropped inline picture, block 9 an anchored one, block 10 a turned one.
        var png = Support.TestPictures.Png(24, 18);
        (string Label, Func<EditResult?> Run)[] steps =
        [
            ("insert", () => editor.InsertPicture(new TextPosition(StoryRef.Body, (Paragraph)model.Body.Blocks[1], 3), png, "inserted")),
            ("resize", () => editor.ResizePicture(IdAt(8), 2000000, 1000000)),
            ("alt text", () => editor.SetAltText(IdAt(8), "cropped")),
            ("wrap to float", () => editor.SetPictureWrap(IdAt(8), PictureWrap.TopAndBottom)),
            ("wrap between floats", () => editor.SetPictureWrap(IdAt(9), PictureWrap.Behind)),
            ("nudge", () => editor.NudgePicture(IdAt(9), 4, -2)),
            ("move", () => editor.MovePicture(IdAt(9), new TextPosition(StoryRef.Body, (Paragraph)model.Body.Blocks[3], 0), 77, 88)),
            ("wrap to inline", () => editor.SetPictureWrap(editor.PictureAt(new TextPosition(StoryRef.Body, (Paragraph)model.Body.Blocks[3], 0))!, PictureWrap.Inline)),
            ("properties: size, description and wrap in one step", () => editor.SetPictureProperties(IdAt(8), new PictureProperties(1_500_000, 900_000, AltText: "all at once", Wrap: PictureWrap.Behind))),
            ("delete", () => editor.DeletePicture(IdAt(10))),
        ];

        Check("before");
        foreach (var (label, run) in steps)
        {
            Assert.True(run() is not null, label + " was refused");
            Check(label);
        }

        // The store keeps the bytes of the inserted picture through every undo and redo (the save decides what is written).
        var entry = Assert.Single(model.Media.Entries);
        for (var i = 0; i < steps.Length; i++)
        {
            Assert.NotNull(editor.History.Undo());
            Check("undo " + i);
            Assert.Equal(png, model.Media.TryGet(entry.Ref.RelationshipId)!.Value.ToArray());
        }

        for (var i = 0; i < steps.Length; i++)
        {
            Assert.NotNull(editor.History.Redo());
            Check("redo " + i);
            Assert.Equal(png, model.Media.TryGet(entry.Ref.RelationshipId)!.Value.ToArray());
        }
    }

    [Fact]
    public void TableFormattingAndItsUndoAndRedoGiveTheBytesOfAFullWrite()
    {
        using var folder = new Support.TempFolder();
        var path = folder.File("tables.docx");
        Support.DocxBuilder.WithBody(path, TableCorpusStructureTests.GeneratedBody());
        using var file = DocxFile.Open(path);
        var model = file.Model;
        var editor = new TextEditor(model, (_, _) => ProjectionHost.Plain, new StillClock());
        file.SerialiseMainForTest(cached: true);

        void Check(string label)
        {
            var cached = file.SerialiseMainForTest(cached: true);
            Assert.True(cached.AsSpan().SequenceEqual(file.SerialiseMainForTest(cached: false)), label + ": main part");
        }

        // The tables are the blocks 1 (merged, fixed), 3 (autofit) and 5 (percentage) of the generated body.
        TableRef Merged() => new(StoryRef.Body, 1);
        TableRef Autofit() => new(StoryRef.Body, 3);
        TableRef Percentage() => new(StoryRef.Body, 5);
        int Columns(TableRef table) => table.Find(model.Body.Blocks)!.GridColumns.Count;
        (string Label, Func<EditResult?> Run)[] steps =
        [
            ("borders", () => editor.SetBorders(new CellRange(Merged(), 0, 0, 2, 1), BorderSpec.All(TableBorderStyle.DoubleLine, 8, 0xFFFF0000))),
            ("borders off", () => editor.SetBorders(new CellRange(Autofit(), 0, 0, 0, 1), BorderSpec.None())),
            ("shading", () => editor.SetShading(new CellRange(Percentage(), 0, 0, 1, 1), 0xFF92D050)),
            ("shading off", () => editor.SetShading(new CellRange(Percentage(), 0, 0, 0, 0), null)),
            ("header row", () => editor.SetHeaderRow(Autofit(), true)),
            ("header row off", () => editor.SetHeaderRow(Merged(), false)),
            ("column widths", () => editor.SetColumnWidths(Autofit(), [1200, 1700, 2100])),
            ("table width", () => editor.SetTableWidth(Percentage(), 6000)),
            ("row height", () => editor.SetRowHeight(Merged(), 1, 700, exact: true)),
            ("distribute", () => editor.DistributeColumns(Merged())),
            ("properties", () => editor.SetTableProperties(Autofit(), new TableProperties(ColumnTwips: [1000, 1500, 2500], RowHeight: new TableRowHeight(0, 1, 500, Exact: false), HeaderRow: true))),
            ("properties table width", () => editor.SetTableProperties(Percentage(), new TableProperties(TableWidthTwips: 5000, HeaderRow: true))),
        ];
        Assert.Equal(3, Columns(Autofit()));
        Assert.Equal(4, Columns(Merged()));

        Check("before");
        foreach (var (label, run) in steps)
        {
            Assert.True(run() is not null, label + " was refused");
            Check(label);
        }

        for (var i = 0; i < steps.Length; i++)
        {
            Assert.NotNull(editor.History.Undo());
            Check("undo " + i);
        }

        for (var i = 0; i < steps.Length; i++)
        {
            Assert.NotNull(editor.History.Redo());
            Check("redo " + i);
        }
    }

    [Fact]
    public void InsertPageNumberAndItsUndoAndRedoGiveTheBytesOfAFullWrite()
    {
        using var file = DocxFile.Open(Path.Combine(Root, "poi-page-specific-head-foot", "poi-page-specific-head-foot.docx"));
        var model = file.Model;
        var editor = new TextEditor(model, (_, _) => ProjectionHost.Plain, new StillClock());
        var paragraphs = model.Body.Blocks.OfType<Paragraph>().ToList();
        Assert.True(paragraphs.Count >= 4);
        file.SerialiseMainForTest(cached: true);

        void Check(string label)
        {
            var cached = file.SerialiseMainForTest(cached: true);
            Assert.True(cached.AsSpan().SequenceEqual(file.SerialiseMainForTest(cached: false)), label + ": main part");
        }

        TextPosition At(Paragraph paragraph) => new(StoryRef.Body, paragraph, 0);
        var words = new PageNumberWords("Page", "of");

        Check("before");
        Assert.NotNull(editor.InsertSectionBreak(At(paragraphs[2]), SectionBreakKind.NextPage));
        Check("break");
        Assert.NotNull(editor.InsertPageNumber(At(paragraphs[3]), PageNumberLook.PageOfTotal, HeaderFooterKind.Footer, PageNumberAlign.Center, words, "2", "4"));
        Check("insert");
        Assert.NotNull(editor.InsertPageNumber(At(paragraphs[3]), PageNumberLook.Plain, HeaderFooterKind.Footer, PageNumberAlign.Right, words, "2", "4"));
        Check("replace");
        for (var i = 0; i < 3; i++)
        {
            Assert.NotNull(editor.History.Undo());
            Check("undo " + i);
        }

        for (var i = 0; i < 3; i++)
        {
            Assert.NotNull(editor.History.Redo());
            Check("redo " + i);
        }
    }

    [Fact]
    public async Task TheHeaderFooterCommandsAndTheirUndoAndRedoGiveTheBytesOfAFullWrite()
    {
        var w = System.Xml.Linq.XNamespace.Get("http://schemas.openxmlformats.org/wordprocessingml/2006/main");
        using var file = DocxFile.Open(Path.Combine(Root, "poi-page-specific-head-foot", "poi-page-specific-head-foot.docx"));
        var model = file.Model;
        var editor = new TextEditor(model, (_, _) => ProjectionHost.Plain, new StillClock());
        Assert.True(model.Body.Blocks.OfType<Paragraph>().Count() >= 4);
        using var folder = new Support.TempFolder();
        file.SerialiseMainForTest(cached: true);

        void Check(string label)
        {
            var cached = file.SerialiseMainForTest(cached: true);
            Assert.True(cached.AsSpan().SequenceEqual(file.SerialiseMainForTest(cached: false)), label + ": main part");
        }

        var at = new TextPosition(StoryRef.Body, model.Body.Blocks.OfType<Paragraph>().ElementAt(2), 0);
        Assert.NotNull(editor.InsertSectionBreak(at, SectionBreakKind.NextPage));
        Check("break");
        var last = SectionList.Of(model).Count - 1;
        var hasOwn = SectionList.Of(model)[last].Properties.HeaderFooterReferences.Any(r => r.Kind == HeaderFooterKind.Footer && r.Type == HeaderFooterType.Default);
        Assert.NotNull(editor.SetOwnHeaderFooter(last, HeaderFooterKind.Footer, HeaderFooterType.Default, !hasOwn, at));
        Check("own or same as previous");
        Assert.NotNull(editor.SetOwnHeaderFooter(last, HeaderFooterKind.Footer, HeaderFooterType.Default, hasOwn, at));
        Check("and back");
        Assert.NotNull(editor.SetDifferentFirstPage(last, !SectionList.Of(model)[last].Properties.GetFlag(w + "titlePg"), at));
        Check("first page");
        var flag = model.Settings?.EvenAndOddHeaders ?? false;
        Assert.NotNull(editor.SetLeftRightPages(!flag, at));
        Check("left and right");
        const int Steps = 5;

        // The settings part and the new header and footer parts reach a recovery copy as they reach a user save.
        var copy = folder.File("copy.docx");
        var user = folder.File("user.docx");
        await file.SaveSnapshotAsync(copy, TestContext.Current.CancellationToken);
        await file.SaveAsync(user, TestContext.Current.CancellationToken);
        using (var a = System.IO.Compression.ZipFile.OpenRead(copy))
        using (var b = System.IO.Compression.ZipFile.OpenRead(user))
        {
            var names = a.Entries.Select(e => e.FullName).Where(n => n.StartsWith("word/", StringComparison.Ordinal) && n.EndsWith(".xml", StringComparison.Ordinal)).Order().ToList();
            Assert.Equal(names, b.Entries.Select(e => e.FullName).Where(n => n.StartsWith("word/", StringComparison.Ordinal) && n.EndsWith(".xml", StringComparison.Ordinal)).Order().ToList());
            Assert.Contains("word/settings.xml", names);
            foreach (var name in names)
            {
                Assert.True(Bytes(a, name).AsSpan().SequenceEqual(Bytes(b, name)), name);
            }
        }

        using (var reopened = DocxFile.Open(copy))
        {
            Assert.Equal(!flag, reopened.Model.Settings?.EvenAndOddHeaders ?? false);
            Assert.Equal(
                SectionList.Of(model).Select(s => s.Properties.HeaderFooterReferences.Count(r => r.Type == HeaderFooterType.Even)),
                SectionList.Of(reopened.Model).Select(s => s.Properties.HeaderFooterReferences.Count(r => r.Type == HeaderFooterType.Even)));
        }

        for (var i = 0; i < Steps; i++)
        {
            Assert.NotNull(editor.History.Undo());
            Check("undo " + i);
        }

        for (var i = 0; i < Steps; i++)
        {
            Assert.NotNull(editor.History.Redo());
            Check("redo " + i);
        }

        Assert.Equal(!flag, model.Settings?.EvenAndOddHeaders ?? false);
    }

    private static byte[] Bytes(System.IO.Compression.ZipArchive zip, string name)
    {
        using var stream = zip.GetEntry(name)!.Open();
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}
