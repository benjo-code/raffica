using System.Globalization;
using System.Reflection;
using Raffica.Core.Pdf;
using Raffica.Pdf.Commands;
using Xunit;

namespace Raffica.Pdf.Tests.Commands;

/// <summary>
/// Every lock shows its reason: a systematic walk over every command id and every state of the rules with a document open. A command
/// that the rules disable in a state must tell why, in both languages, and a command that is on tells nothing.
/// </summary>
public sealed class PdfCommandRulesTests : IDisposable
{
    public void Dispose() => PdfStrings.Culture = CultureInfo.GetCultureInfo("en");

    /// <summary>The answers of the window's questions in one state; the default is a document that is open and untouched.</summary>
    private sealed class Cfg
    {
        public int Tabs { get; set; } = 1;

        public bool PrintAllowed { get; set; } = true;

        public bool PrinterAvailable { get; set; } = true;

        public bool Dirty { get; set; }

        public bool CanUndo { get; set; }

        public bool CanRedo { get; set; }

        public bool BusySaving { get; set; }

        public bool AnnotateAllowed { get; set; } = true;

        public bool Selection { get; set; }

        public bool SaveRunning { get; set; }

        public bool HasForm { get; set; } = true;

        public bool FillAllowed { get; set; } = true;

        public bool EmptyRequired { get; set; }

        public bool PagesReady { get; set; } = true;

        public bool PageSelection { get; set; }

        public bool PlanCanUndo { get; set; }

        public bool PlanCanRedo { get; set; }

        public bool PlanChanged { get; set; }

        public bool MarksUnsaved { get; set; }

        public bool RedactMarked { get; set; }

        public bool OcrLibrary { get; set; } = true;

        public bool OcrRunning { get; set; }

        public PdfMode? Mode { get; set; }
    }

    private static PdfCommandRules Rules(Cfg c) => new(
        () => true,
        () => c.Tabs,
        () => new PdfPrintInfo(false, c.PrintAllowed),
        () => c.PrinterAvailable,
        () => c.Dirty,
        () => c.CanUndo,
        () => c.CanRedo,
        () => c.BusySaving,
        () => c.AnnotateAllowed,
        () => c.Selection,
        () => c.SaveRunning,
        () => c.HasForm,
        () => c.FillAllowed,
        () => c.EmptyRequired,
        () => c.PagesReady,
        () => c.Mode == PdfMode.Organize,
        () => c.PageSelection,
        () => c.PlanCanUndo,
        () => c.PlanCanRedo,
        () => c.PlanChanged,
        () => c.MarksUnsaved,
        () => c.RedactMarked,
        () => c.OcrLibrary,
        () => true,
        () => c.OcrRunning,
        new ModeExclusion(mode => mode == c.Mode));

    private static IEnumerable<(string Name, Cfg State)> States()
    {
        yield return ("untouched", new Cfg());
        yield return ("dirty with history and a selected mark", new Cfg { Dirty = true, CanUndo = true, CanRedo = true, Selection = true, Tabs = 3 });
        yield return ("dirty and a save runs", new Cfg { Dirty = true, BusySaving = true, SaveRunning = true });
        yield return ("untouched and a save runs", new Cfg { BusySaving = true, SaveRunning = true });
        yield return ("a recognition runs", new Cfg { OcrRunning = true, Mode = PdfMode.Ocr });
        yield return ("printing forbidden", new Cfg { PrintAllowed = false });
        yield return ("no printer", new Cfg { PrinterAvailable = false });
        yield return ("annotating forbidden", new Cfg { AnnotateAllowed = false });
        yield return ("no form", new Cfg { HasForm = false });
        yield return ("filling forbidden", new Cfg { FillAllowed = false });
        yield return ("a form with an empty required field", new Cfg { EmptyRequired = true });
        yield return ("pages not read yet", new Cfg { PagesReady = false });
        yield return ("marks unsaved", new Cfg { MarksUnsaved = true, Dirty = true });
        yield return ("no OCR library", new Cfg { OcrLibrary = false });
        yield return ("annotate tool on", new Cfg { Mode = PdfMode.Annotate });
        yield return ("forms on", new Cfg { Mode = PdfMode.Forms });
        yield return ("plan open, nothing selected or changed", new Cfg { Mode = PdfMode.Organize });
        yield return ("plan open, pages selected, changed, with history", new Cfg { Mode = PdfMode.Organize, PageSelection = true, PlanChanged = true, PlanCanUndo = true, PlanCanRedo = true });
        yield return ("plan open and a save runs", new Cfg { Mode = PdfMode.Organize, PlanChanged = true, SaveRunning = true, BusySaving = true });
        yield return ("redact on, nothing marked", new Cfg { Mode = PdfMode.Redact });
        yield return ("redact on, marked", new Cfg { Mode = PdfMode.Redact, RedactMarked = true });
        yield return ("redact on, marked, a save runs", new Cfg { Mode = PdfMode.Redact, RedactMarked = true, SaveRunning = true, BusySaving = true });
        yield return ("text edit on", new Cfg { Mode = PdfMode.TextEdit });
    }

    private static string[] AllIds() =>
        typeof(PdfCommandIds).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToArray();

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    public void EveryDisabledCommandHasAReasonInEveryState(string language)
    {
        PdfStrings.Culture = CultureInfo.GetCultureInfo(language);
        var missing = new List<string>();
        var disabledSeen = 0;
        foreach (var (name, state) in States())
        {
            var rules = Rules(state);
            foreach (var id in AllIds())
            {
                var reason = rules.ReasonFor(id);
                if (!rules.Allows(id))
                {
                    disabledSeen++;
                    if (string.IsNullOrWhiteSpace(reason))
                    {
                        missing.Add($"{id} in \"{name}\"");
                    }
                }
                else
                {
                    Assert.True(reason is null, $"{id} is on in \"{name}\" but says \"{reason}\"");
                }
            }
        }

        Assert.True(disabledSeen > 100, "The walk reached too few disabled commands: " + disabledSeen);
        Assert.True(missing.Count == 0, "Disabled without a reason: " + string.Join("; ", missing));
    }

    [Fact]
    public void WithoutADocumentNothingIsToldBecauseTheStartViewShowsNoCommands()
    {
        var rules = new PdfCommandRules(() => false, () => 0, () => null);
        Assert.All(AllIds(), id => Assert.Null(rules.ReasonFor(id)));
    }

    [Theory]
    [InlineData("en", "Turn on \"Redact\" first")]
    [InlineData("de", "Schalten Sie zuerst „Schwärzen“ ein")]
    public void TheCommandsInsideARedactionNameTheCommandThatTurnsItOn(string language, string expected)
    {
        PdfStrings.Culture = CultureInfo.GetCultureInfo(language);
        var rules = Rules(new Cfg());
        foreach (var id in new[] { "redact.markSelection", "redact.find", "redact.rasterPage", "redact.deleteMarks", "redact.discard", "redact.apply" })
        {
            Assert.Equal(expected, rules.ReasonFor(id));
        }
    }

    [Theory]
    [InlineData("en", "Turn on \"Organize pages\" first", "Turn on \"Edit text\" first")]
    [InlineData("de", "Schalten Sie zuerst „Seiten organisieren“ ein", "Schalten Sie zuerst „Text bearbeiten“ ein")]
    public void ThePageCommandsAndTheEndOfATextEditNameTheirMode(string language, string organize, string edit)
    {
        PdfStrings.Culture = CultureInfo.GetCultureInfo(language);
        var rules = Rules(new Cfg());
        Assert.Equal(organize, rules.ReasonFor("pages.blank"));
        Assert.Equal(organize, rules.ReasonFor("pages.rotateLeft"));
        Assert.Equal(organize, rules.ReasonFor("pages.apply"));
        Assert.Equal(edit, rules.ReasonFor("text.editDone"));
    }

    [Fact]
    public void PrintSaveAndSaveAsGiveTheSaveBusyReasonWhileASaveRuns()
    {
        PdfStrings.Culture = CultureInfo.GetCultureInfo("en");
        var rules = Rules(new Cfg { Dirty = true, BusySaving = true, SaveRunning = true });
        Assert.Equal(PdfStrings.SaveBusy, rules.ReasonFor("file.print"));
        Assert.Equal(PdfStrings.SaveBusy, rules.ReasonFor("file.save"));
        Assert.Equal(PdfStrings.SaveBusy, rules.ReasonFor("file.saveAs"));
        Assert.Equal("A save is running.", PdfStrings.SaveBusy);
    }

    [Fact]
    public void TheGermanReasonsUseTheNamesOfTheCommands()
    {
        PdfStrings.Culture = CultureInfo.GetCultureInfo("de");
        var rules = Rules(new Cfg());

        // The command is "Wiederholen", so the reason says "wiederholen", and undo keeps its phrase.
        Assert.Equal("Wiederholen", PdfStrings.CmdRedo);
        Assert.Equal("Es gibt nichts zu wiederholen.", rules.ReasonFor("edit.redo"));
        Assert.Equal("Es gibt nichts rückgängig zu machen.", rules.ReasonFor("edit.undo"));
    }
}
