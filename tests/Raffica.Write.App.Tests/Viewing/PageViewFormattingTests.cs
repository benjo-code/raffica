using System.IO.Compression;
using System.Text;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Raffica.Write.Commands;
using Raffica.Write.Editing;
using Raffica.Write.Model;
using Raffica.Write.Viewing;
using Xunit;

namespace Raffica.WriteApp.Tests.Viewing;

/// <summary>The formatting commands of the page view (Task 9 of milestone 4.3b): the caret rules, the value commands and the refusal of a bad payload.</summary>
public sealed class PageViewFormattingTests : IDisposable
{
    private static readonly TimeSpan GateTimeout = TimeSpan.FromSeconds(60);
    private static readonly RawInputModifiers Command = OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;
    private static readonly System.Xml.Linq.XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private readonly string _folder = Directory.CreateTempSubdirectory("raffica-write-formatting-view-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // The system cleans the temp folder.
        }
    }

    private string Document(params string[] paragraphs)
    {
        var path = Path.Combine(_folder, "format.docx");
        var body = string.Concat(paragraphs.Select(p => $"<w:p>{p}</w:p>"));
        using (var stream = File.Create(path))
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            Entry(zip, "[Content_Types].xml", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
                </Types>
                """);
            Entry(zip, "_rels/.rels", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
                </Relationships>
                """);
            Entry(zip, "word/document.xml", $"""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:body>
                    {body}
                    <w:sectPr><w:pgSz w:w="12240" w:h="15840"/><w:pgMar w:top="1440" w:right="1440" w:bottom="1440" w:left="1440" w:header="720" w:footer="720" w:gutter="0"/></w:sectPr>
                  </w:body>
                </w:document>
                """);
        }

        return path;
    }

    private static void Entry(ZipArchive zip, string name, string text)
    {
        var entry = zip.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(text.TrimStart());
    }

    private static async Task<ViewHarness> OpenWholeAsync(string path)
    {
        var harness = new ViewHarness(path, 900, 1000);
        harness.View.Runner.LeaseWaitTimeout = GateTimeout;
        harness.View.Runner.Start();
        await ViewHarness.WaitAsync(() => harness.View.Runner.FullModelReady || harness.View.Runner.IsFaulted, 30000);
        Assert.True(harness.View.Runner.FullModelReady);
        return harness;
    }

    private static EditSession Place(ViewHarness h, int paragraph, int offset, int? to = null)
    {
        var session = h.View.Tab.EnsureEditor();
        _ = session.Selection;
        var start = new TextPosition(StoryRef.Body, P(h, paragraph), offset);
        session.SetSelection(new TextRange(start, to.HasValue ? start with { Offset = to.Value } : start));
        h.View.Focus();
        return session;
    }

    private static Paragraph P(ViewHarness h, int index) => (Paragraph)h.View.Tab.Docx.Model.Body.Blocks[index];

    private static string AttributeOf(Paragraph paragraph, string element) => paragraph.Properties.Get(W + element)?.Attribute(W + "val")?.Value ?? string.Empty;

    private static string RunValue(Paragraph paragraph, string element, string attribute = "val") =>
        string.Join('|', paragraph.Inlines.OfType<Run>().Select(r => r.Properties.Get(W + element)?.Attribute(W + attribute)?.Value ?? "-"));

    [AvaloniaFact]
    public async Task AnEmptyParagraphAlsoFormatsItsMarkAndSetsThePendingFormat()
    {
        using var h = await OpenWholeAsync(Document("<w:r><w:t>Text</w:t></w:r>", string.Empty));
        var session = Place(h, 1, 0);

        Assert.True(h.View.RunFormatCommand(WriteCommandIds.Bold));

        // The mark of the empty paragraph has the property (the line takes its height from it), as one history step; the next text is bold too.
        Assert.NotNull(P(h, 1).Properties.Get(W + "rPr")?.Element(W + "b"));
        Assert.Equal(1, session.Editor.History.UndoCount);
        Assert.True(FormatChanges.IsOn(session.Pending.Changes, CharacterToggle.Bold));
        h.Window.KeyTextInput("Z");
        ViewHarness.Pump();
        Assert.Contains(P(h, 1).Inlines.OfType<Run>(), r => r.Properties.Get(W + "b") is not null);
    }

    [AvaloniaFact]
    public async Task AValueCommandAppliesItsValueToTheSelection()
    {
        using var h = await OpenWholeAsync(Document("<w:r><w:t>abcdefgh</w:t></w:r>", "<w:r><w:t>second</w:t></w:r>"));
        var session = Place(h, 0, 2, 5);

        Assert.True(h.View.RunFormatCommand(WriteCommandIds.Size, 14));
        Assert.Equal("-|28|-", RunValue(P(h, 0), "sz"));
        Assert.True(h.View.RunFormatCommand(WriteCommandIds.Color, "#FF0000"));
        Assert.Equal("-|FF0000|-", RunValue(P(h, 0), "color"));
        Assert.True(h.View.RunFormatCommand(WriteCommandIds.Font, "Carlito"));
        Assert.Equal("-|Carlito|-", RunValue(P(h, 0), "rFonts", "ascii"));
        Assert.True(h.View.RunFormatCommand(WriteCommandIds.Highlight, "yellow"));
        Assert.Equal("-|yellow|-", RunValue(P(h, 0), "highlight"));
        Assert.True(h.View.RunFormatCommand(WriteCommandIds.Highlight, null));
        Assert.Equal("-|-|-", RunValue(P(h, 0), "highlight"));
        Assert.Equal(5, session.Editor.History.UndoCount);

        // A paragraph command takes a number of points; the line spacing a multiple.
        Assert.True(h.View.RunFormatCommand(WriteCommandIds.SpaceAfter, 12.0));
        Assert.Equal("240", P(h, 0).Properties.Get(W + "spacing")?.Attribute(W + "after")?.Value);
        Assert.True(h.View.RunFormatCommand(WriteCommandIds.LineSpacing, new LineSpacingChoice(1.5f, LineRuleKind.Auto)));
        Assert.Equal("360", P(h, 0).Properties.Get(W + "spacing")?.Attribute(W + "line")?.Value);
    }

    [AvaloniaFact]
    public async Task ABadPayloadIsRefusedWithNoChangeAndNoException()
    {
        using var h = await OpenWholeAsync(Document("<w:r><w:t>abcdefgh</w:t></w:r>"));
        var session = Place(h, 0, 2, 5);
        var revision = h.View.Tab.Docx.Model.Edits.Revision;

        object?[] garbage = [null, "", "abc", "NaN", "1e999", double.NaN, double.PositiveInfinity, float.NegativeInfinity, -5, long.MaxValue, 100000, new object(), new[] { 1 }, "12px", true, "#12345", "GGGGGG", "\u0001"];
        string[] valueCommands =
        [
            WriteCommandIds.Font, WriteCommandIds.Size, WriteCommandIds.SizeUp, WriteCommandIds.SizeDown, WriteCommandIds.Color, WriteCommandIds.SpaceBefore, WriteCommandIds.SpaceAfter, WriteCommandIds.LineSpacing,
            WriteCommandIds.ParagraphStyle, WriteCommandIds.CharacterStyle, WriteCommandIds.Highlight,
        ];

        foreach (var id in valueCommands)
        {
            foreach (var payload in garbage)
            {
                // Null and "" are real choices for the highlight and the two style commands ("none": they remove), so they are not garbage there.
                if (payload is null or "" && id is WriteCommandIds.Highlight or WriteCommandIds.ParagraphStyle or WriteCommandIds.CharacterStyle)
                {
                    continue;
                }

                // Any plain name is a font family (the catalog may not know it: the layout falls back), so only the unusable names are garbage for the font.
                if (id == WriteCommandIds.Font && payload is string { Length: > 0 } name && !name.Any(char.IsControl))
                {
                    continue;
                }

                // A non-negative int up to 0xFFFFFF is a colour (0 is black).
                if (id == WriteCommandIds.Color && payload is int rgb && rgb >= 0)
                {
                    continue;
                }

                _ = h.View.RunFormatCommand(id, payload);
                Assert.True(revision == h.View.Tab.Docx.Model.Edits.Revision && session.Editor.History.UndoCount == 0, $"{id} with {payload ?? "null"} ({payload?.GetType().Name})");
            }
        }

        Assert.Equal(0, session.Editor.History.UndoCount);
        Assert.Empty(session.Pending.Changes);

        // A style the file does not define is no choice (this document has no styles part).
        Assert.False(h.View.RunFormatCommand(WriteCommandIds.ParagraphStyle, "Heading1"));
        Assert.False(h.View.RunFormatCommand(WriteCommandIds.CharacterStyle, "Strong"));

        // Not a formatting id, or no such command.
        Assert.False(h.View.RunFormatCommand("edit.undo"));
        Assert.False(h.View.RunFormatCommand("format.nothing"));
    }

    [AvaloniaFact]
    public async Task AParagraphCommandAtACaretKeepsThePendingFormat()
    {
        using var h = await OpenWholeAsync(Document("<w:r><w:t>abcdefgh</w:t></w:r>"));
        var session = Place(h, 0, 3);

        Assert.True(h.View.RunFormatCommand(WriteCommandIds.Italic));
        Assert.True(h.View.RunFormatCommand(WriteCommandIds.AlignCenter));

        Assert.Equal("center", AttributeOf(P(h, 0), "jc"));
        Assert.True(FormatChanges.IsOn(session.Pending.Changes, CharacterToggle.Italic));
        Assert.Equal(1, session.Editor.History.UndoCount);

        // A size at the caret joins the pending format and replaces a size that was pending before.
        Assert.True(h.View.RunFormatCommand(WriteCommandIds.Size, 20));
        Assert.True(h.View.RunFormatCommand(WriteCommandIds.Size, 22));
        Assert.Equal(3, session.Pending.Changes.Count);
        Assert.Contains(session.Pending.Changes, c => c.Name == W + "sz" && c.Value?.Attribute(W + "val")?.Value == "44");
    }

    [AvaloniaFact]
    public async Task IndentAndTheSwitchesStepOrFlipTheParagraphOfTheSelection()
    {
        using var h = await OpenWholeAsync(Document("<w:r><w:t>one</w:t></w:r>", "<w:r><w:t>two</w:t></w:r>"));
        Place(h, 0, 0, 2);

        Assert.True(h.View.RunFormatCommand(WriteCommandIds.IndentMore));
        Assert.Equal("720", P(h, 0).Properties.Get(W + "ind")?.Attribute(W + "left")?.Value);
        Assert.True(h.View.RunFormatCommand(WriteCommandIds.IndentMore));
        Assert.Equal("1440", P(h, 0).Properties.Get(W + "ind")?.Attribute(W + "left")?.Value);
        Assert.True(h.View.RunFormatCommand(WriteCommandIds.IndentLess));
        Assert.Equal("720", P(h, 0).Properties.Get(W + "ind")?.Attribute(W + "left")?.Value);

        // Less at zero changes nothing.
        Place(h, 1, 0);
        var revision = h.View.Tab.Docx.Model.Edits.Revision;
        Assert.True(h.View.RunFormatCommand(WriteCommandIds.IndentLess));
        Assert.Equal(revision, h.View.Tab.Docx.Model.Edits.Revision);

        // A switch flips the effective value: on, then an explicit off.
        Assert.True(h.View.RunFormatCommand(WriteCommandIds.KeepNext));
        Assert.NotNull(P(h, 1).Properties.Get(W + "keepNext"));
        Assert.Null(P(h, 1).Properties.Get(W + "keepNext")?.Attribute(W + "val"));
        Assert.True(h.View.RunFormatCommand(WriteCommandIds.KeepNext));
        Assert.Equal("0", P(h, 1).Properties.Get(W + "keepNext")?.Attribute(W + "val")?.Value);
    }

    [AvaloniaFact]
    public async Task FormattingKeysAreIgnoredWhileTheDocumentCannotBeEdited()
    {
        // More than 64 blocks: the open reads a prefix first (a smaller document is whole at once).
        using var h = new ViewHarness(Document([.. Enumerable.Range(0, 90).Select(i => $"<w:r><w:t>Paragraph {i}</w:t></w:r>")]), 900, 600);
        var gate = new ManualResetEventSlim(false);
        h.View.Runner.BeforeFullOpen = () => gate.Wait(TimeSpan.FromSeconds(30));
        h.View.Runner.Start();
        var refused = new List<EditBlockedReason>();
        h.View.InputRefused += refused.Add;
        h.View.Focus();

        // Still opening: the key is claimed, nothing changes, and the view says why (like typing).
        h.Window.KeyPressQwerty(PhysicalKey.B, Command);
        h.Window.KeyReleaseQwerty(PhysicalKey.B, Command);
        ViewHarness.Pump();
        Assert.Equal([EditBlockedReason.StillOpening], refused);

        gate.Set();
        await ViewHarness.WaitAsync(() => h.View.Runner.FullModelReady, 30000);
        Assert.Null(h.View.Tab.Editor);
    }
}
