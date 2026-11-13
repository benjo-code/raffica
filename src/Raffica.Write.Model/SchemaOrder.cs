using System.Xml.Linq;

namespace Raffica.Write.Model;

/// <summary>
/// The order of the children of each <c>*Pr</c> element, from ECMA-376 Part 1 section 17 as Word writes it.
/// Names that share a slot (the header and footer references of a section) are separated by <c>|</c>.
/// It lives in the model assembly because <see cref="PropertyBag.Set"/> needs it and the model references nothing else.
/// </summary>
public static class SchemaOrder
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    // CT_PPrBase, then rPr, sectPr, pPrChange last.
    private static readonly string[] s_paragraph =
    [
        "pStyle", "keepNext", "keepLines", "pageBreakBefore", "framePr", "widowControl", "numPr", "suppressLineNumbers",
        "pBdr", "shd", "tabs", "suppressAutoHyphens", "kinsoku", "wordWrap", "overflowPunct", "topLinePunct",
        "autoSpaceDE", "autoSpaceDN", "bidi", "adjustRightInd", "snapToGrid", "spacing", "ind", "contextualSpacing",
        "mirrorIndents", "suppressOverlap", "jc", "textDirection", "textAlignment", "textboxTightWrap", "outlineLvl",
        "divId", "cnfStyle", "rPr", "sectPr", "pPrChange",
    ];

    // CT_RPr (EG_RPrBase), then rPrChange.
    private static readonly string[] s_run =
    [
        "rStyle", "rFonts", "b", "bCs", "i", "iCs", "caps", "smallCaps", "strike", "dstrike", "outline", "shadow",
        "emboss", "imprint", "noProof", "snapToGrid", "vanish", "webHidden", "color", "spacing", "w", "kern",
        "position", "sz", "szCs", "highlight", "u", "effect", "bdr", "shd", "fitText", "vertAlign", "rtl", "cs",
        "em", "lang", "eastAsianLayout", "specVanish", "oMath", "rPrChange",
    ];

    // CT_TblPrBase, then tblPrChange.
    private static readonly string[] s_table =
    [
        "tblStyle", "tblpPr", "tblOverlap", "bidiVisual", "tblStyleRowBandSize", "tblStyleColBandSize", "tblW", "jc",
        "tblCellSpacing", "tblInd", "tblBorders", "shd", "tblLayout", "tblCellMar", "tblLook", "tblCaption",
        "tblDescription", "tblPrChange",
    ];

    // CT_TrPrBase (a choice in the schema; Word writes this order), then ins, del, trPrChange.
    private static readonly string[] s_tableRow =
    [
        "cnfStyle", "divId", "gridBefore", "gridAfter", "wBefore", "wAfter", "cantSplit", "trHeight", "tblHeader",
        "tblCellSpacing", "jc", "hidden", "ins", "del", "trPrChange",
    ];

    // CT_TcPrBase, then cellIns, cellDel, cellMerge, tcPrChange.
    private static readonly string[] s_tableCell =
    [
        "cnfStyle", "tcW", "gridSpan", "hMerge", "vMerge", "tcBorders", "shd", "noWrap", "tcMar", "textDirection",
        "tcFitText", "vAlign", "hideMark", "headers", "cellIns", "cellDel", "cellMerge", "tcPrChange",
    ];

    // CT_SectPr: the header and footer references come first in any order, then CT_SectPrBase, then sectPrChange.
    private static readonly string[] s_section =
    [
        "headerReference|footerReference", "footnotePr", "endnotePr", "type", "pgSz", "pgMar", "paperSrc", "pgBorders",
        "lnNumType", "pgNumType", "cols", "formProt", "vAlign", "noEndnote", "titlePg", "textDirection", "bidi",
        "rtlGutter", "docGrid", "printerSettings", "sectPrChange",
    ];

    // CT_Style.
    private static readonly string[] s_style =
    [
        "name", "aliases", "basedOn", "next", "link", "autoRedefine", "hidden", "uiPriority", "semiHidden",
        "unhideWhenUsed", "qFormat", "locked", "personal", "personalCompose", "personalReply", "rsid", "pPr", "rPr",
        "tblPr", "trPr", "tcPr", "tblStylePr",
    ];

    // CT_Settings, as far as Word writes the children (children not listed keep their place).
    private static readonly string[] s_settings =
    [
        "writeProtection", "view", "zoom", "removePersonalInformation", "removeDateAndTime", "doNotDisplayPageBoundaries",
        "displayBackgroundShape", "printPostScriptOverText", "printFractionalCharacterWidth", "printFormsData",
        "embedTrueTypeFonts", "embedSystemFonts", "saveSubsetFonts", "saveFormsData", "mirrorMargins",
        "alignBordersAndEdges", "bordersDoNotSurroundHeader", "bordersDoNotSurroundFooter", "gutterAtTop",
        "hideSpellingErrors", "hideGrammaticalErrors", "activeWritingStyle", "proofState", "formsDesign",
        "attachedTemplate", "linkStyles", "stylePaneFormatFilter", "stylePaneSortMethod", "documentType", "mailMerge",
        "revisionView", "trackRevisions", "doNotTrackMoves", "doNotTrackFormatting", "documentProtection",
        "autoFormatOverride", "styleLockTheme", "styleLockQFSet", "defaultTabStop", "autoHyphenation",
        "consecutiveHyphenLimit", "hyphenationZone", "doNotHyphenateCaps", "showEnvelope", "summaryLength",
        "clickAndTypeStyle", "defaultTableStyle", "evenAndOddHeaders", "bookFoldRevPrinting", "bookFoldPrinting",
        "bookFoldPrintingSheets", "drawingGridHorizontalSpacing", "drawingGridVerticalSpacing",
        "displayHorizontalDrawingGridEvery", "displayVerticalDrawingGridEvery", "doNotUseMarginsForDrawingGridOrigin",
        "drawingGridHorizontalOrigin", "drawingGridVerticalOrigin", "doNotShadeFormData", "noPunctuationKerning",
        "characterSpacingControl", "printTwoOnOne", "strictFirstAndLastChars", "noLineBreaksAfter", "noLineBreaksBefore",
        "savePreviewPicture", "doNotValidateAgainstSchema", "saveInvalidXml", "ignoreMixedContent",
        "alwaysShowPlaceholderText", "doNotDemarcateInvalidXml", "saveXmlDataOnly", "useXSLTWhenSaving",
        "saveThroughXslt", "showXMLTags", "alwaysMergeEmptyNamespace", "updateFields", "hdrShapeDefaults", "footnotePr",
        "endnotePr", "compat", "docVars", "rsids", "mathPr", "attachedSchema", "themeFontLang", "clrSchemeMapping",
        "doNotIncludeSubdocsInStats", "doNotAutoCompressPictures", "forceUpgrade", "captions", "readModeInkLockDown",
        "smartTagType", "schemaLibrary", "shapeDefaults", "doNotEmbedSmartTags", "decimalSymbol", "listSeparator",
    ];

    private static readonly Dictionary<PropertyKind, Dictionary<XName, int>> s_index = Build();

    /// <summary>The position of a child among its siblings for this kind, or null for a name the table does not know.</summary>
    public static int? IndexOf(PropertyKind kind, XName child)
    {
        ArgumentNullException.ThrowIfNull(child);
        return s_index[kind].TryGetValue(child, out var index) ? index : null;
    }

    private static Dictionary<PropertyKind, Dictionary<XName, int>> Build()
    {
        var result = new Dictionary<PropertyKind, Dictionary<XName, int>>();
        foreach (var (kind, table) in new (PropertyKind Kind, string[] Table)[]
        {
            (PropertyKind.Paragraph, s_paragraph),
            (PropertyKind.Run, s_run),
            (PropertyKind.Table, s_table),
            (PropertyKind.TableRow, s_tableRow),
            (PropertyKind.TableCell, s_tableCell),
            (PropertyKind.Section, s_section),
            (PropertyKind.Style, s_style),
            (PropertyKind.ContentControl, []),
            (PropertyKind.Settings, s_settings),
        })
        {
            var map = new Dictionary<XName, int>();
            for (var i = 0; i < table.Length; i++)
            {
                foreach (var local in table[i].Split('|'))
                {
                    map[W + local] = i;
                }
            }

            result[kind] = map;
        }

        return result;
    }
}
