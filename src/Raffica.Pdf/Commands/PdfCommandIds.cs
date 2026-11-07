namespace Raffica.Pdf.Commands;

/// <summary>The ids of the commands of the catalog (they are also the ids of the ribbon commands).</summary>
internal static class PdfCommandIds
{
    public const string Open = "file.open";
    public const string Print = "file.print";
    public const string CloseTab = "file.close";
    public const string Settings = "file.settings";

    /// <summary>Make Raffica the default app for PDFs: a palette entry and a settings row, no ribbon button (so it is not in the catalog).</summary>
    public const string MakeDefaultApp = "app.default";

    /// <summary>The About card (version, license, third-party notices): a palette entry and a button of the help card, no ribbon button (so it is not in the catalog).</summary>
    public const string About = "app.about";

    public const string Search = "find.search";
    public const string SearchNext = "find.next";
    public const string SearchPrevious = "find.previous";
    public const string GoToPage = "goto.page";
    public const string PreviousPage = "pages.previous";
    public const string NextPage = "pages.next";
    public const string FirstPage = "pages.first";
    public const string LastPage = "pages.last";
    public const string NextTab = "tabs.next";
    public const string PreviousTab = "tabs.previous";
    public const string SelectAll = "text.selectall";
    public const string Copy = "text.copy";
    public const string Help = "help.shortcuts";
    public const string ZoomIn = "zoom.in";
    public const string ZoomOut = "zoom.out";
    public const string FitWidth = "zoom.fitwidth";
    public const string FitPage = "zoom.fitpage";
    public const string ActualSize = "zoom.actual";
    public const string Continuous = "layout.continuous";
    public const string SinglePage = "layout.single";
    public const string TwoPages = "layout.two";
    public const string TwoPagesCover = "layout.twocover";
    public const string DarkPage = "page.dark";
    public const string PagesPane = "panes.pages";
    public const string OutlinePane = "panes.outline";
    public const string Save = "file.save";
    public const string SaveAs = "file.saveAs";
    public const string Undo = "edit.undo";
    public const string Redo = "edit.redo";
    public const string AnnotateMode = "annotate.mode";
    public const string Highlight = "annotate.highlight";
    public const string Underline = "annotate.underline";
    public const string StrikeOut = "annotate.strikeOut";
    public const string SelectMark = "annotate.select";
    public const string Freehand = "annotate.freehand";
    public const string Rectangle = "annotate.rectangle";
    public const string Ellipse = "annotate.ellipse";
    public const string Line = "annotate.line";
    public const string Arrow = "annotate.arrow";
    public const string TextBox = "annotate.textBox";
    public const string AnnotateDone = "annotate.done";
    public const string MarksPane = "annotate.marks";
    public const string DeleteMark = "annotate.delete";
    public const string Sign = "annotate.sign";
    public const string FormsNext = "forms.next";
    public const string FormsPrevious = "forms.previous";
    public const string FormsNextRequired = "forms.nextRequired";
    public const string FormsHighlight = "forms.highlight";
    public const string Organize = "pages.organize";
    public const string RotateLeft = "pages.rotateLeft";
    public const string RotateRight = "pages.rotateRight";
    public const string DuplicatePages = "pages.duplicate";
    public const string BlankPage = "pages.blank";
    public const string InsertFile = "pages.insertFile";
    public const string MovePagesUp = "pages.moveUp";
    public const string MovePagesDown = "pages.moveDown";
    public const string PagesUndo = "pages.undo";
    public const string PagesRedo = "pages.redo";
    public const string ApplyPages = "pages.apply";
    public const string DiscardPages = "pages.discard";
    public const string DeletePages = "pages.delete";
    public const string ExtractPages = "pages.extract";
    public const string SplitDocument = "pages.split";
    public const string ExportImages = "pages.exportImages";
    public const string RedactMode = "redact.mode";
    public const string RedactMarkSelection = "redact.markSelection";
    public const string RedactFind = "redact.find";
    public const string RedactRasterPage = "redact.rasterPage";
    public const string RedactDeleteMarks = "redact.deleteMarks";
    public const string RedactDiscard = "redact.discard";
    public const string RedactApply = "redact.apply";
    public const string OcrRun = "ocr.run";
    public const string OcrLanguages = "ocr.languages";
    public const string OcrCancel = "ocr.cancel";
    public const string TextEdit = "text.edit";
    public const string TextEditDone = "text.editDone";
}
