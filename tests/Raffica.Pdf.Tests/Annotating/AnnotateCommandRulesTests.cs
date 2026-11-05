using Raffica.Core.Pdf;
using Raffica.Pdf.Commands;
using Xunit;

namespace Raffica.Pdf.Tests.Annotating;

public sealed class AnnotateCommandRulesTests
{
    private static readonly string[] ToolIds =
    [
        "annotate.mode", "annotate.highlight", "annotate.underline", "annotate.strikeOut", "annotate.select", "annotate.freehand",
        "annotate.rectangle", "annotate.ellipse", "annotate.line", "annotate.arrow", "annotate.textBox",
    ];

    private static PdfCommandRules Rules(
        bool document = true,
        bool dirty = false,
        bool undo = false,
        bool redo = false,
        bool busy = false,
        bool annotate = true,
        bool selection = false) =>
        new(() => document, () => 1, () => new PdfPrintInfo(false, true), null, () => dirty, () => undo, () => redo, () => busy, () => annotate, () => selection);

    [Fact]
    public void ToolsAreDisabledWithAReasonWhenTheDocumentForbidsAnnotating()
    {
        var forbidden = Rules(annotate: false);
        foreach (var id in ToolIds)
        {
            Assert.False(forbidden.Allows(id), id);
            Assert.Equal("This document does not allow changing annotations.", forbidden.ReasonFor(id));
        }

        var allowed = Rules();
        foreach (var id in ToolIds)
        {
            Assert.True(allowed.Allows(id), id);
            Assert.Null(allowed.ReasonFor(id));
        }

        // The other commands do not carry the reason.
        Assert.Null(forbidden.ReasonFor("zoom.in"));
        Assert.True(forbidden.Allows("zoom.in"));

        // Without a document there is no reason to tell, the tools are just off.
        var none = Rules(document: false);
        Assert.All(ToolIds, id => Assert.False(none.Allows(id)));
        Assert.Null(none.ReasonFor("annotate.highlight"));
    }

    [Fact]
    public void TheGermanReasonExists() =>
        Assert.Equal("Dieses Dokument erlaubt keine Änderung von Anmerkungen.", PdfStrings.GetIn("AnnotateLockedReason", System.Globalization.CultureInfo.GetCultureInfo("de")));

    [Fact]
    public void SaveNeedsADirtyTabAndNoRunningSaveOrPrint()
    {
        Assert.False(Rules().Allows("file.save"));
        Assert.True(Rules(dirty: true).Allows("file.save"));
        Assert.False(Rules(dirty: true, busy: true).Allows("file.save"));
        Assert.False(Rules(document: false, dirty: true).Allows("file.save"));
    }

    [Fact]
    public void SaveAsNeedsOnlyADocument()
    {
        Assert.True(Rules().Allows("file.saveAs"));
        Assert.False(Rules(document: false).Allows("file.saveAs"));
    }

    [Fact]
    public void UndoAndRedoFollowTheHistory()
    {
        Assert.False(Rules().Allows("edit.undo"));
        Assert.False(Rules().Allows("edit.redo"));
        Assert.True(Rules(undo: true).Allows("edit.undo"));
        Assert.False(Rules(undo: true).Allows("edit.redo"));
        Assert.True(Rules(redo: true).Allows("edit.redo"));
    }

    [Fact]
    public void DeleteNeedsASelectionAndDoneAndMarksNeedADocument()
    {
        Assert.False(Rules().Allows("annotate.delete"));
        Assert.True(Rules(selection: true).Allows("annotate.delete"));
        Assert.True(Rules().Allows("annotate.done"));
        Assert.True(Rules().Allows("annotate.marks"));
        Assert.False(Rules(document: false).Allows("annotate.done"));
        Assert.False(Rules(document: false).Allows("annotate.marks"));
    }
}
