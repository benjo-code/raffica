# Milestone 4.1 – Document model and DOCX round trip – Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A UI-free library reads and writes DOCX into a model that follows OOXML and keeps everything it does not understand, and a compatibility corpus with a comparison tool measures it.

**Architecture:** Two new libraries. `Raffica.Write.Model` holds the model types and nothing else (BCL only). `Raffica.Write.Docx` holds the XML readers and writers per part and `DocxFile` (open, save). The Open XML SDK is used as the **package layer only** (parts, relationships, content types, stream access); the model is read with `XmlReader` straight from the part streams and written back as raw XML into the part streams, so the SDK's DOM never loads and never re-serialises a part. A model part is rewritten on every save (equal content, not equal bytes); a part the model does not understand is never opened and stays byte-identical on the decompressed bytes. "Keeps what it does not understand" is built into the types: every container keeps unknown child elements in place and unknown attributes, properties are an ordered XML list with typed accessors on top, and fields and tracked changes are views over the kept structure, not a second structure. The corpus and the comparison tool live in `bench/write-corpus` (an AOT console tool, also the AOT publish proof of the libraries).

**Tech Stack:** .NET 10, Native AOT, `DocumentFormat.OpenXml` 3.5.1 (MIT, the version of spike 1), `System.Xml.Linq`, xunit.v3, PDFium through `Raffica.Core` for the comparison tool.

**Spec:** `Docs/superpowers/specs/2026-11-13-raffica-write-design.md` (sections 3, 4, 6 "Opening and saving" and "Compatibility corpus", 7 row 4.1). Decisions that bind this plan: `Docs/DECISIONS.md`, "Raffica Write: Open XML SDK under Native AOT (M4)" (a loaded part is re-serialised, an unloaded one stays identical; never load a part Write does not understand) and "Raffica Write: the spec and the first release (M4)".

## Global Constraints

- Everything in English: code, comments, docs, commits, roadmap text. UI strings: none in this milestone.
- The model and the DOCX library reference no Avalonia, `Raffica.Shell`, `Raffica.Pdf` or networking type (spec section 4). `Raffica.Write.Model` references no `Raffica.*` assembly; `Raffica.Write.Docx` only `Raffica.Write.Model` and `Raffica.Core`.
- "Shared code moves, it is not copied" (spec section 4): the atomic replace moves out of `DocumentSaver` into Core (Task 8); the PDF tests stay green.
- A part the model does not understand is never loaded; a save writes back every part it did not understand; Write never "repairs" a file silently (spec section 3).
- External relationships (linked pictures, attached templates, hyperlinks) are never fetched or resolved.
- Saving is atomic (write next to the file, check, replace) and through a symbolic link writes the target (spec section 6; decision "Raffica: saving through a symbolic link (M2.7)").
- Corpus licences allowed: `MIT`, `Apache-2.0`, `BSD-2-Clause`, `BSD-3-Clause`, `CC0-1.0`, `CC-BY-4.0`, and `GPL-3.0-or-later` for our own generated files. Every public file gets its source URL, licence, retrieval date and SHA-256 in the manifest; its licence text is committed. A file whose licence cannot be established is not committed.
- Commit with the GitHub noreply address only; Conventional Commits; stage by file name (no `git add .`/`-A`), then `git show --stat HEAD` against the expected list; no attribution lines; no force push, no `reset --hard`, no `--no-verify`. The PR goes to `dev`; merge it when all CI checks are green.
- "Done" = `dotnet build Raffica.slnx -c Release`, the test command of `CLAUDE.md` (with `RAFFICA_REQUIRE_OCR=1`, after `native-fetch`), `dotnet format Raffica.slnx --verify-no-changes` and `dotnet run tools/roadmap.cs -- check` green, with commands and results named. Warnings are errors; no suppressions to get green.
- No `CHANGELOG.md` entry (nothing ships to users).
- Steps marked **Owner** need the owner's Windows PC (Word). The executor prepares them, lists them as open, and never invents a result.

## File Structure

| Path | Responsibility |
|---|---|
| `src/Raffica.Write.Model/` | Public model types (vocabulary below). No I/O, no SDK. |
| `src/Raffica.Write.Docx/Xml/` | Internal `XmlReader`/`XmlWriter` code per part: `DocumentXml`, `StoryXml` (body, header, footer, footnotes, endnotes, comments), `StylesXml`, `NumberingXml`, `SettingsXml`, `CommentsExtendedXml`; shared `Names` (the `XName`s) and `SchemaOrder`. |
| `src/Raffica.Write.Docx/DocxFile.cs`, `DocxParts.cs`, `DocxExceptions.cs`, `PackageParts.cs` | Open, part graph, save, guard. |
| `src/Raffica.Core/Saving/AtomicFile.cs` | The replace logic moved out of `DocumentSaver`. |
| `tests/Raffica.Write.Tests/` | All tests of both libraries; `Support/XmlEquivalence.cs`, `Support/DocxBuilder.cs` (small DOCX files made through the SDK), `Support/PartComparer.cs`. |
| `bench/write-corpus/` | Tool `write-corpus` (`check`, `run`, `add`, `pdf-facts`, `compare`, `table`), `PageFacts`, `PageComparer`, `CorpusManifest`, `FeatureDetector`. |
| `testdata/write-corpus/` | `corpus.json`, `README.md`, one folder per file (`<id>/<id>.docx`, later `word.pdf`, `word-accept.pdf`, `word-reject.pdf`), `licenses/`. |
| `Docs/write-corpus.md` | The generated corpus table. |

**Model vocabulary** (namespace `Raffica.Write.Model`, all `public`; later milestones add accessors and types but never a second storage):

- `OoxmlElement` (abstract): `IList<XAttribute> UnknownAttributes`. Every type below derives from it.
- `UnknownContent`: wraps one kept `XElement` (outer XML, namespaces it needs re-declared on it). Subtypes `UnknownBlock : Block`, `UnknownInline : Inline`, `UnknownRunContent : RunContent`, `UnknownRowItem : RowItem`, and `PropertyBag` entries.
- `PropertyBag` + `PropertyKind` (`Paragraph`, `Run`, `Table`, `TableRow`, `TableCell`, `Section`, `Style`…): the children of a `*Pr` element as an ordered list of `XElement`. `Get(XName)`, `Set(XName, XElement)`, `Remove(XName)`, `Children`. `Set` inserts a new child where `SchemaOrder` for that kind says; an element `SchemaOrder` does not know keeps its place. Typed accessors in 4.1: `StyleId` (`w:pStyle`/`w:rStyle`/`w:tblStyle` by kind), `Bold`, `NumberingId`, `NumberingLevel`. Revision records inside a bag (`w:rPrChange`, `w:pPrChange`, `w:ins`/`w:del` in a paragraph mark) stay children.
- `Story : OoxmlElement`: `OoxmlRoot Root` (name and the root's attributes and namespace declarations, so `mc:Ignorable` keeps its prefixes), `IList<Block> Blocks`, `SectionProperties? FinalSection` (body only).
- `Block`: `Paragraph` (`PropertyBag Properties`, `SectionProperties? SectionBreak`, `IList<Inline> Inlines`), `Table` (`PropertyBag Properties`, `IList<int> GridColumns`, `IList<RowItem> Rows`), `ContentControlBlock` (`PropertyBag Properties`, `IList<Block> Content`), `UnknownBlock`. `RowItem`: `TableRow` (`Properties`, `IList<RowItem> Items`), `TableCell` (`Properties`, `IList<Block> Blocks`), `UnknownRowItem`.
- `Inline`: `Run` (`Properties`, `IList<RunContent> Content`), `Hyperlink` (`RelationshipId?`, `Anchor?`, `Inlines`), `SimpleField` (`Instruction`, `Inlines`), `BookmarkStart`/`BookmarkEnd` (`Id`, `Name`), `CommentRangeStart`/`CommentRangeEnd` (`Id`), `TrackedChange` (`TrackedChangeKind` `Insert|Delete|MoveFrom|MoveTo`, `RevisionInfo Info` = id, author, date, `Inlines`), `ContentControlInline` (`Properties`, `Inlines`), `UnknownInline`.
- `RunContent`: `TextContent(string)`, `DeletedText(string)`, `InstructionText(string)`, `TabContent`, `BreakContent(BreakKind, string? clear)`, `FieldChar(FieldCharKind Begin|Separate|End, bool Dirty)`, `FootnoteReference(int)`, `EndnoteReference(int)`, `CommentReference(int)`, `Picture` (from `w:drawing`: `bool Inline`, `long WidthEmu`, `long HeightEmu`, `string? EmbedRelationshipId`, `string? AltText`, `XElement Raw`), `UnknownRunContent`.
- Views: `FieldScanner.Scan(IEnumerable<Inline>) → IReadOnlyList<FieldInfo>` (`Instruction`, `Parent`, `IsComplete`; complex and simple fields, nested) and `TrackedChanges.List(Story) → IReadOnlyList<RevisionRecord>` (`Kind` including `ParagraphMark`, `PropertyChange`, `Id`, `Author`, `Date`).
- `SectionProperties : PropertyBag` + `HeaderFooterReference(HeaderFooterKind Header|Footer, HeaderFooterType Default|First|Even, string RelationshipId)` list; the rest stays in the bag.
- `StyleSheet` (`Defaults`, `IList<Style>`, `XElement? LatentStyles`, `Find(string id)`), `Style` (`Id`, `Type`, `Name`, `BasedOn`, `Next`, `Link`, `IsDefault`, `ParagraphProperties`, `RunProperties`, `TableProperties`, kept children). `NumberingDefinitions` (`IList<AbstractNumbering>` with `IList<NumberingLevel>` (`Level`, `Start`, `Format`, `Text`, `Justification`, `ParagraphProperties`, `RunProperties`), `IList<NumberingInstance>` (`NumId`, `AbstractNumId`, level overrides)). `DocumentSettings` (`TrackRevisions`, `AutoHyphenation`, `EvenAndOddHeaders`, `UpdateFields` read from the bag; the rest kept). `NoteCollection` (`IList<Note> Notes`, `Find(int)`; implements `IEnumerable<Note>` only, CA1711), `Note` (`Id`, `NoteKind Normal|Separator|ContinuationSeparator|ContinuationNotice`, `Story Content`). `CommentCollection` (`IList<Comment> Comments`, `Find(int)`, `EnsureParaId(Comment)`), `Comment` (`Id`, `Author`, `Initials`, `Date`, `Story Content`, `ParaId`, `bool Done`, `ParentParaId`). `HeaderFooterPart` (`Story Content`).
- `WordDocument`: `Story Body`, `StyleSheet? Styles`, `NumberingDefinitions? Numbering`, `DocumentSettings? Settings`, `NoteCollection? Footnotes`, `NoteCollection? Endnotes`, `CommentCollection? Comments`, `IDictionary<string, HeaderFooterPart> HeadersFooters` (keyed by the relationship id used in `SectionProperties`). A part that was not loaded is `null` (or absent).

## Review Focus

Failure modes the spec implies that no milestone task names; each is pinned by a test in the owning task:

1. **Content from newer Word versions** (`w14:`/`w15:` attributes and elements, `mc:AlternateContent` text boxes, `mc:Ignorable` on the root) lost or with its prefixes undeclared, so Word reports a corrupt file. Task 4, `NewerWordContentSurvives`.
2. **Text that is exactly what the user typed:** leading and trailing spaces (`xml:space`), tabs, line breaks, non-BMP characters, text that looks like markup. Task 4, `TextSurvivesExactly`.
3. **A damaged or foreign file** (truncated zip, no main part, not XML, wrong root) must end in a typed `DocxFormatException`, never a crash, a partial target or a left-over temporary file. Task 9, `MalformedPackageThrowsDocxFormatException`. (Hostile-file hardening and fuzzing are milestone 4.8.)
4. **A save that fails or is refused halfway** (a check fails, the target is read-only, the target is a symbolic link) leaves the user's file byte-identical and no temporary file behind. Task 9, `FailedSaveLeavesTargetAndFolderUntouched`, `SaveThroughSymlinkWritesTheTarget`.
5. **`.docm`** keeps its macro part untouched and is never run, and a part the model creates (a comments part in a file that had none) gets its relationship and content type. Task 9, `DocmKeepsItsMacroPart`, `ModelChangeRoundTrips`.

---

### Task 1: Branch, plan and roadmap

**Files:**
- Add: this plan.
- Modify: `Docs/roadmap.json` (subproject 5, milestone `4.1`; `scopeChanges`), regenerate `Docs/ROADMAP.md`.

- [ ] **Step 1:** On branch `feature/m41-document-model-docx` (from `dev`): milestone `4.1` gets `"plan": "superpowers/plans/2026-11-13-milestone-04-1-document-model-and-docx-round-trip.md"` and `"status": "planned"`.
- [ ] **Step 2:** Append to `scopeChanges`: `{ "date": "2026-11-13", "context": "Milestone 4.1", "text": "The comparison tool of 4.1 has the Word side only (page count and text per page of Word's PDF through PDFium, the corpus table with the round-trip results and Word's page counts): Write's own layout does not exist before 4.2, which adds the Write columns to the same table and the page-count gate of the generated thesis. The reader and writer keep the Open XML SDK to the package layer (parts, relationships, content types) and read and write the model with XmlReader/XmlWriter, so a loaded part is rewritten with equal content and every other part keeps its bytes (decision \"Raffica Write: Open XML SDK under Native AOT (M4)\"). Word's PDFs of the corpus are made by the owner and are listed as open." }`.
- [ ] **Step 3:** `dotnet run tools/roadmap.cs -- generate`, then `-- check`. Expected: passes.
- [ ] **Step 4: Commit** after the owner has reviewed the plan:

```bash
git add Docs/superpowers/plans/2026-11-13-milestone-04-1-document-model-and-docx-round-trip.md Docs/roadmap.json Docs/ROADMAP.md
git commit -m "docs: plan milestone 4.1 (document model and DOCX round trip)"
git show --stat HEAD
```
- [ ] **Step 5:** When execution starts: status `in-progress`, regenerate, check, commit `docs: start milestone 4.1`. Tick every roadmap task right after its plan task is done; tick acceptance criteria only with evidence (Task 13).

### Task 2: Projects, package and guards

**Files:**
- Create: `src/Raffica.Write.Model/Raffica.Write.Model.csproj`, `src/Raffica.Write.Docx/Raffica.Write.Docx.csproj`, `tests/Raffica.Write.Tests/Raffica.Write.Tests.csproj`, `tests/Raffica.Write.Tests/ProjectGuardTests.cs`.
- Modify: `Raffica.slnx` (the three projects), `Directory.Packages.props` (`DocumentFormat.OpenXml` 3.5.1).

**Interfaces:**
- Produces: the three projects. Both libraries `IsAotCompatible`; `Raffica.Write.Docx` has `InternalsVisibleTo Raffica.Write.Tests` and `WriteCorpus`; Model references nothing; Docx references Model, Core and the SDK. The test project follows `tests/Raffica.Core.Tests` (xunit.v3, `OutputType` Exe) and references Model, Docx and (from Task 10) `bench/write-corpus`.

- [ ] **Step 1: Write the failing tests** in `ProjectGuardTests`:
  - `ModelReferencesNoRafficaAssembly`: the referenced assembly names of `Raffica.Write.Model` contain none that starts with `Raffica.` or `Avalonia`.
  - `DocxReferencesOnlyModelAndCore`: its `Raffica.*` references are exactly `Raffica.Write.Model` and `Raffica.Core`; none starts with `Avalonia`, `Raffica.Shell` or `Raffica.Pdf` (the exact name, `Raffica.Pdfium` is not referenced directly either).
  - `NoNetworkAssemblyIsReferenced`: neither library references `System.Net.Http` or `System.Net.Sockets`.
  - `BothLibrariesAreAotCompatible`: each assembly carries `AssemblyMetadata("IsTrimmable","True")`.
- [ ] **Step 2:** Run `dotnet test tests/Raffica.Write.Tests -c Release --filter-class "*ProjectGuardTests"`. Expected: fail to build (projects missing).
- [ ] **Step 3:** Create the projects as described; add `DocumentFormat.OpenXml` to `Directory.Packages.props`. Each library gets one placeholder-free type later; to build now, add `OoxmlElement` to Model and an internal `Names` class to Docx.
- [ ] **Step 4:** Licence: run `dotnet run tools/licenses.cs -- check`. Expected: green. If it demands a manifest entry for the SDK, add it to `third-party/licenses.json` with the MIT text under `third-party/openxml-sdk/LICENSE`; if it flags the entry as unused, leave it out (4.2 adds it when an app references the library) and record the licence check in the DECISIONS entry of Task 13.
- [ ] **Step 5:** Re-run the tests. Expected: 4 passed. Then `dotnet build Raffica.slnx -c Release` green.
- [ ] **Step 6: Commit** `feat(write): add the document model and DOCX libraries and their guards` (the three csproj, `Raffica.slnx`, `Directory.Packages.props`, `ProjectGuardTests.cs`, `OoxmlElement.cs`, `Names.cs`, licence files if added).

### Task 3: The kernel – unknown content, property bags, test support

**Files:**
- Create: `src/Raffica.Write.Model/{OoxmlElement,UnknownContent,PropertyBag,PropertyKind,OoxmlRoot,Story}.cs`, `src/Raffica.Write.Docx/Xml/SchemaOrder.cs`, `tests/Raffica.Write.Tests/Support/XmlEquivalence.cs`, `tests/Raffica.Write.Tests/PropertyBagTests.cs`, `tests/Raffica.Write.Tests/XmlEquivalenceTests.cs`.

**Interfaces:**
- Produces: `PropertyBag`, `UnknownContent`, `OoxmlRoot`, `Story` as in the vocabulary; `SchemaOrder.IndexOf(PropertyKind kind, XName child) → int?` (null for an unknown name), tables copied from ECMA-376 Part 1 §17 (the order Word writes: `pPr`, `tblPr`, `trPr`, `tcPr`, `sectPr` children; in a `pPr` the children `w:rPr`, `w:sectPr`, `w:pPrChange` always come last in that order); test support `XmlEquivalence.AreEqual(XElement a, XElement b) → bool` and `Explain(a, b) → string?` (first difference, with the path).
- Equivalence rule (documented in the file): same names, same attribute set regardless of order, namespace declarations ignored, same child order, text compared exactly for elements without element children, whitespace-only text between elements ignored.

- [ ] **Step 1: Write the failing tests.**
  - `XmlEquivalenceTests`: equal ignoring attribute order and namespace declaration placement; unequal for a changed attribute value, a swapped child order, a differing text (`" a"` vs `"a"` in a `w:t`); `Explain` names the path of the difference.
  - `PropertyBagTests`: `SetInsertsInSchemaOrder` (a `w:rPr` bag with `w:b`, `w:lang`; `Set(w:sz)` lands between them); `UnknownChildKeepsItsPlace` (a `w14:ligatures` between `w:b` and `w:lang` is still between them after `Set(w:i)`); `RemoveLeavesTheOthers`; `StyleIdReadsByKind` (`w:pStyle` for `Paragraph`, `w:rStyle` for `Run`); `BoldIsFalseForValOff` (`<w:b w:val="0"/>`).
- [ ] **Step 2:** Run both classes. Expected: fail (types missing).
- [ ] **Step 3:** Implement the types and `SchemaOrder`; `UnknownContent` captures with `XElement` deep copy and keeps the namespace prefixes the source used.
- [ ] **Step 4:** Run both classes. Expected: pass.
- [ ] **Step 5: Commit** `feat(write): kernel of the model: kept unknown content and property bags`.

### Task 4: Paragraphs, runs and inline content (`StoryXml`, `DocumentXml`)

**Files:**
- Create: `src/Raffica.Write.Model/{Paragraph,Run,RunContent,Inline,TrackedChange,FieldScanner,TrackedChanges,RevisionInfo}.cs` (one file per type family), `src/Raffica.Write.Docx/Xml/{StoryXml,DocumentXml,Names}.cs`, `tests/Raffica.Write.Tests/InlineXmlTests.cs`.

**Interfaces:**
- Consumes: Task 3 types.
- Produces: `StoryXml.Read(XmlReader reader) → Story` and `StoryXml.Write(Story, XmlWriter)` for the block content shared by every story (the root element decides which story it is; Task 7 adds the header, footer, note and comment roots on top); `DocumentXml.Read(Stream) → Story`, `DocumentXml.Write(Story, Stream)` (UTF-8 without BOM, XML declaration `standalone="yes"`). Reading is forward-only `XmlReader`; an element the code does not know becomes `UnknownContent` via `XElement.ReadFrom`. Blocks at this task: `Paragraph` and `UnknownBlock` only (Task 5 adds the rest). Test helper `XmlSnippets.Document(string bodyXml)` builds a complete `w:document` string with the usual namespaces and `mc:Ignorable`.

- [ ] **Step 1: Write the failing tests** (`InlineXmlTests`, every test reads a snippet, writes it, and asserts `XmlEquivalence.AreEqual` on the whole part in addition to its own assertion):
  - `ReadsParagraphsRunsAndContent`: `<w:p><w:pPr><w:pStyle w:val="Heading1"/></w:pPr><w:r><w:rPr><w:b/></w:rPr><w:t xml:space="preserve"> a </w:t><w:tab/><w:br w:type="page"/></w:r></w:p>` gives `StyleId == "Heading1"`, run `Bold == true`, content `[TextContent(" a "), TabContent, BreakContent(Page)]`.
  - `TextSurvivesExactly` (Review Focus 2): leading and trailing spaces, two consecutive spaces, `"<b>&amp;</b>"` as text, `"😀"`, a tab and a `w:br` inside one run; the written `w:t` carries `xml:space="preserve"` exactly when the text starts or ends with a space.
  - `HyperlinkBookmarkAndCommentRangesSurvive`: `w:hyperlink` with `r:id` and one with `w:anchor`, `w:bookmarkStart/End`, `w:commentRangeStart/End`, a `w:commentReference` run; ids and names read; order kept.
  - `TrackedChangesAreWrappersAndListed`: `w:ins`, `w:del` (text in `w:delText` stays `DeletedText`), `w:moveFrom`, `w:moveTo`, a paragraph-mark `<w:rPr><w:ins .../></w:rPr>`, a `w:rPrChange`; `TrackedChanges.List` returns one record per revision with kind, id, author and date, in document order.
  - `FieldScannerFindsComplexAndSimpleFields`: begin/instrText/separate/result/end, a `PAGEREF` nested in a `TOC`, and a `w:fldSimple`; the instruction is trimmed; the nested field has the `TOC` as parent; a begin without end gives `IsComplete == false` and no exception.
  - `PicturesKeepTheirXmlAndExposeSize`: a `w:drawing` with `wp:inline` (`wp:extent cx cy`, `a:blip r:embed`, `wp:docPr descr`) gives `Inline`, sizes, `EmbedRelationshipId`, `AltText`; a `wp:anchor` gives `Inline == false`; the written XML is equivalent.
  - `NewerWordContentSurvives` (Review Focus 1): root `mc:Ignorable="w14 w15 wp14"` with those declarations, `w14:paraId`/`w14:textId` on `w:p`, `w15:collapsed` on a property, a run holding `mc:AlternateContent` (`mc:Choice` with a `w:txbxContent`, `mc:Fallback`): the written part declares the same prefixes at the root and is equivalent.
  - `UnknownContentSurvivesInInlineContainers` (theory over: body, `w:p`, `w:pPr`, `w:r`, `w:rPr`, `w:hyperlink`, `w:ins`, `w:del`): an unknown element `<x:foo xmlns:x="urn:test" a="1"><w:t>deep</w:t></x:foo>` and an unknown attribute `x:bar="1"` on the container are equivalent after the round trip.
- [ ] **Step 2:** Run `--filter-class "*InlineXmlTests"`. Expected: fail.
- [ ] **Step 3:** Implement the model types and `StoryXml`/`DocumentXml`. Attribute handling: read every attribute of a known element; known ones fill typed members, all others (including `xmlns` declarations at the root) go to `UnknownAttributes`/`OoxmlRoot`. Write with the prefixes `w`, `r`, `wp`, `a` … as in `Names`; the root re-declares exactly the namespaces it was read with.
- [ ] **Step 4:** Run the class. Expected: pass.
- [ ] **Step 5: Commit** `feat(write): read and write paragraphs, runs, fields, revisions, comment ranges and pictures`.

### Task 5: Tables, sections and content controls

**Files:**
- Create: `src/Raffica.Write.Model/{Table,TableRow,TableCell,RowItem,SectionProperties,HeaderFooterReference,ContentControl}.cs`, `tests/Raffica.Write.Tests/BlockXmlTests.cs`.
- Modify: `StoryXml.cs`, `DocumentXml.cs`.

**Interfaces:**
- Consumes: Task 4. Produces: the block types of the vocabulary; `SectionProperties.HeaderFooterReferences` and `Story.FinalSection` (the body-level `w:sectPr`; the others hang on the `Paragraph.SectionBreak` they sit in); `Names` additions.

- [ ] **Step 1: Write the failing tests** (`BlockXmlTests`, each also asserts whole-part equivalence):
  - `TablesKeepGridRowsCellsAndMerges`: a table with `w:tblGrid` of three columns, `w:gridSpan`, `w:vMerge`, a nested table in a cell, a header row (`w:tblHeader`): grid, row count, cell blocks and the bag children read as written.
  - `SectionsAreFoundOnParagraphsAndAtTheEnd`: two `w:sectPr` inside paragraphs and one body-level; header and footer references with `w:type` default/first/even and their `r:id`; `w:pgNumType w:start="1"` stays in the bag.
  - `ContentControlsKeepTheirElement`: block-level and inline `w:sdt` with `w:sdtPr` and `w:sdtContent`.
  - `UnknownContentSurvivesInBlockContainers` (theory over: `w:tbl`, `w:tblPr`, `w:tblGrid`, `w:tr`, `w:trPr`, `w:tc`, `w:tcPr`, `w:sdt`, `w:sdtPr`, `w:sdtContent`, `w:sectPr`): the unknown element and attribute of Task 4 survive.
- [ ] **Step 2:** Run `--filter-class "*BlockXmlTests"`. Expected: fail.
- [ ] **Step 3:** Implement; rows and cells may sit inside `w:sdt`, `w:ins` and `w:customXml` wrappers, which stay as `UnknownRowItem` unless typed.
- [ ] **Step 4:** Run `BlockXmlTests` and `InlineXmlTests`. Expected: pass.
- [ ] **Step 5: Commit** `feat(write): read and write tables, sections and content controls`.

### Task 6: Styles, numbering and settings

**Files:**
- Create: `src/Raffica.Write.Model/{StyleSheet,Style,NumberingDefinitions,AbstractNumbering,NumberingLevel,NumberingInstance,DocumentSettings}.cs`, `src/Raffica.Write.Docx/Xml/{StylesXml,NumberingXml,SettingsXml}.cs`, `tests/Raffica.Write.Tests/StylesNumberingSettingsTests.cs`.

**Interfaces:**
- Produces: `StylesXml.Read(Stream) → StyleSheet` / `Write(StyleSheet, Stream)`, `NumberingXml.Read(Stream) → NumberingDefinitions` / `Write`, `SettingsXml.Read(Stream) → DocumentSettings` / `Write`; the types of the vocabulary. All three keep the root attributes (`OoxmlRoot`) and every unknown child.

- [ ] **Step 1: Write the failing tests** (each reads the part of `spikes/write/testdoc/thesis.docx` through `PartComparer.ReadPart` or a literal snippet, and asserts equivalence after a round trip):
  - `StylesReadTypedAndKeepTheRest`: `Find("Heading2")` has `Name == "heading 2"`, `BasedOn == "Normal"`, `Type == Paragraph`; `w:docDefaults` and `w:latentStyles` survive; an unknown child of a `w:style` survives.
  - `NumberingReadsLevelsAndInstances`: an abstract numbering with nine levels (`Start`, `Format`, `Text` such as `%1.%2`), an instance with a `w:lvlOverride/w:startOverride`; the thesis' heading numbering is multi-level.
  - `SettingsFlagsReadAndTheRestSurvives`: `trackRevisions`, `autoHyphenation`, `evenAndOddHeaders`, `updateFields` true when present and false when absent; `w:compat`, `w:rsids`, `mc:Ignorable` survive.
- [ ] **Step 2:** Run `--filter-class "*StylesNumberingSettingsTests"`. Expected: fail.
- [ ] **Step 3:** Implement. Style kinds and numbering formats the code does not name stay as strings, never an enum that throws.
- [ ] **Step 4:** Run the class. Expected: pass.
- [ ] **Step 5: Commit** `feat(write): read and write styles, numbering and settings`.

### Task 7: Notes, comments and headers and footers

**Files:**
- Create: `src/Raffica.Write.Model/{NoteCollection,Note,CommentCollection,Comment,HeaderFooterPart}.cs`, `src/Raffica.Write.Docx/Xml/{NotesXml,CommentsXml,CommentsExtendedXml}.cs`, `tests/Raffica.Write.Tests/NotesCommentsHeadersTests.cs`.
- Modify: `StoryXml.cs` (roots `w:hdr`, `w:ftr`).

**Interfaces:**
- Produces: `NotesXml.Read(Stream, string rootLocalName) → NoteCollection` / `Write` (root `footnotes` or `endnotes`); `CommentsXml.Read(Stream, Stream? commentsExtended) → CommentCollection` and `CommentsXml.Write(CommentCollection, Stream comments, Stream? commentsExtended)` (the extended stream is written only when some comment has `ParaId` state; `done` and `paraIdParent` come from and go back to `w15:commentEx`, joined on the last paragraph's `w14:paraId`); `StoryXml.ReadPart(Stream) → HeaderFooterPart` / `WritePart` for headers and footers.

- [ ] **Step 1: Write the failing tests:**
  - `FootnotesKeepSeparatorsAndRealNotes`: the thesis' `footnotes.xml` has the separator and continuation separator (`Kind`) and 100 normal notes; every `FootnoteReference` id in the body resolves with `Find`.
  - `CommentsJoinTheExtendedPart`: a literal comments part with two comments (one a reply, one `done`) and its `commentsExtended`: `Done` and `ParentParaId` read, written back, and a comment without an extended entry writes none.
  - `HeadersAndFootersAreStories`: the thesis' footer with a `PAGE` field reads with `FieldScanner` finding `PAGE`; equivalence after the round trip.
  - `UnknownContentSurvivesInNotesAndComments`: the unknown element and attribute inside a note paragraph and a comment paragraph.
- [ ] **Step 2:** Run `--filter-class "*NotesCommentsHeadersTests"`. Expected: fail.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run the class. Expected: pass.
- [ ] **Step 5: Commit** `feat(write): read and write footnotes, endnotes, comments and headers and footers`.

### Task 8: The atomic replace moves into Core

**Files:**
- Create: `src/Raffica.Core/Saving/AtomicFile.cs`, `tests/Raffica.Core.Tests/Saving/AtomicFileTests.cs`.
- Modify: `src/Raffica.Core/Saving/DocumentSaver.cs`, and the callers of `DocumentSaver.MoveIntoPlace`/`TemporaryName` (`src/Raffica.Core/Pages/DocumentSplitter.cs`, `PageImageExporter.cs`).

**Interfaces:**
- Produces (public, `Raffica.Core.Saving`): `AtomicFile.ResolveLink(string fullPath) → string` (a symbolic link is followed to its final target), `AtomicFile.NewTemporaryPath(string targetFullPath) → string` (next to the resolved target, name `.pw-<12 hex>.tmp`), `AtomicFile.MoveIntoPlace(string temporary, string target, bool replaceExisting = true)` (atomic where the file system allows, never replaces a file that appears meanwhile without `replaceExisting`; throws `SaveFailureException` as `DocumentSaver` does today).
- `Replace`, `DeleteAside`, `ResolveLink`, `ShortName`, `TargetExists`, `FailureOf`, `FailureOfWin32` move out of `DocumentSaver` unchanged (the parts that take `SaveFaults` stay `internal`); `DocumentSaver` and the two other callers use `AtomicFile`. Behaviour does not change.

- [ ] **Step 1: Write the failing test** `AtomicFileTests`: `ReplacesAnExistingFile`; `WithoutReplaceExistingAFileAtTheTargetEndsInTargetExistsAndStaysUntouched` (`SaveFailureReason.TargetExists`); `SymbolicLinkIsFollowed` (`ResolveLink` returns the final target; `MoveIntoPlace` onto the link path's resolved target keeps the link a link; create the link the way `DocumentSaverTests` does and follow its handling of a system that may not create links); `NewTemporaryPathLiesNextToTheResolvedTarget`.
- [ ] **Step 2:** Run `dotnet test tests/Raffica.Core.Tests -c Release --filter-class "*AtomicFileTests"`. Expected: fail to build.
- [ ] **Step 3:** Do the move described above.
- [ ] **Step 4:** Run `AtomicFileTests`, then every class under `Saving` and the page tests that use the moved calls: `dotnet test tests/Raffica.Core.Tests -c Release --filter-namespace "*Saving*"` and `--filter-namespace "*Pages*"`. Expected: all pass (check the count is not 0). Then `dotnet test tests/Raffica.Pdf.Tests -c Release --filter-class "*SaveController*"` (name the classes that exist; check the count).
- [ ] **Step 5: Commit** `refactor: move the atomic file replace out of DocumentSaver into AtomicFile`.

### Task 9: `DocxFile` – open, save, guard

**Files:**
- Create: `src/Raffica.Write.Docx/{DocxFile,DocxParts,DocxKind,DocxExceptions,PackageParts}.cs`, `tests/Raffica.Write.Tests/{DocxFileTests,DocxSaveTests}.cs`, `tests/Raffica.Write.Tests/Support/{PartComparer,DocxBuilder,ModelSnapshot}.cs`.

**Interfaces:**
- Consumes: the XML codecs of Tasks 4 to 7, `AtomicFile`.
- Produces (public):
  - `[Flags] enum DocxParts { Document = 1, Styles = 2, Numbering = 4, Settings = 8, Notes = 16, Comments = 32, HeadersFooters = 64, All = 127 }` (the body is always loaded).
  - `enum DocxKind { Docx, Docm }`.
  - `sealed class DocxFile : IDisposable` with `static DocxFile Open(string path, DocxParts parts = DocxParts.All)`, `static DocxFile Open(ReadOnlyMemory<byte> bytes, DocxParts parts = DocxParts.All)`, `WordDocument Model`, `DocxKind Kind`, `Task SaveAsync(string targetPath, CancellationToken cancellationToken = default)`.
  - `DocxFormatException : Exception` (not a package, no main part, a model part that is not well-formed XML or has the wrong root); `DocxSaveException : Exception` with `DocxSaveReason { PartsChanged, ModelNotReadable }`. A failed replace propagates Core's `SaveFailureException`.
  - `Open(path)` reads the whole file into memory (a snapshot: a later change of the file on disk cannot corrupt a save) and never calls the SDK on a part the model does not load. `internal` test seams on `DocxFile`: `Action? BeforeReplace` and `Func<string, Stream, Stream>? WrapPartWrite`.
- Save algorithm: copy the snapshot to `AtomicFile.NewTemporaryPath(target)`; open it with the SDK for editing without auto-save; for each loaded model part write the serialised XML with `part.GetStream(FileMode.Create)` (create the part with its relationship and content type when the model has content and the file had no such part, never delete a part); dispose; **guard:** read the temporary package again and compare the decompressed bytes of every part that was not written against the snapshot, and read the model part by part again; any difference ends in `DocxSaveException` and deletes the temporary file; then `AtomicFile.MoveIntoPlace(temporary, AtomicFile.ResolveLink(target), replaceExisting: true)`. Header and footer parts are found through the `RelationshipId`s of the section properties; a `.docm` keeps `word/vbaProject.bin` and its relationships untouched.

- [ ] **Step 1: Write the failing tests** (the document is the committed `spikes/write/testdoc/thesis.docx`, copied to a temp folder; no test writes into `testdata`):
  - `OpenReadsTheThesisModel`: three `SectionProperties` in the body (two on paragraphs, one final), 20 `Table`s at the top level, 100 `FootnoteReference`s, 102 notes in `Footnotes`, a `TOC` field found by `FieldScanner`.
  - `SavedWithoutChangesKeepsEveryOtherPartByteIdentical`: `PartComparer.Diff(original, saved)` has no added and no removed part; every changed part is one of the loaded model parts and is `XmlEquivalence`-equal to its original.
  - `PartsNotLoadedStayByteIdentical`: `Open(path, DocxParts.Document)` then save: the only changed part is `/word/document.xml`.
  - `ModelChangeRoundTrips`: on the thesis, append a paragraph with a run, rename the style `Heading2`, delete one table row, add a footnote note; on a small document made by `DocxBuilder` without a comments part, add a comment with a reference and range; save, open, and `ModelSnapshot.Of(reopened)` (every part serialised to `XElement`) is equivalent to `ModelSnapshot.Of(mutated)`; the new comments part has its relationship and content type (the saved file is also opened by the SDK's own `WordprocessingDocument.Open` without exception).
  - `MalformedPackageThrowsDocxFormatException` (Review Focus 3): theory over a truncated zip, a zip without `word/document.xml`, a `document.xml` that is not XML, a root that is not `w:document`; each throws `DocxFormatException`, and when saving to a target nothing is created.
  - `FailedSaveLeavesTargetAndFolderUntouched` (Review Focus 4): with `BeforeReplace` throwing, and separately with `WrapPartWrite` corrupting a non-model part (expects `DocxSaveException(PartsChanged)`), the target keeps its bytes and the folder holds no `.tmp`; saving onto a read-only target ends in `SaveFailureException` and the same.
  - `SaveThroughSymlinkWritesTheTarget`: as in Task 8's link handling.
  - `DocmKeepsItsMacroPart` (Review Focus 5): a `.docm` made by `DocxBuilder` (macro-enabled main content type, a `word/vbaProject.bin` part with a few bytes): `Kind == Docm`; after a save the macro part is byte-identical and still related to the main part.
  - `NothingIsFetchedFromAnExternalRelationship`: a document with a linked picture (`TargetMode="External"`, `http://127.0.0.1:1/x.png`) and a hyperlink to the same address opens and saves, and the relationships are written back unchanged (the guard `NoNetworkAssemblyIsReferenced` of Task 2 is the other half).
- [ ] **Step 2:** Run `--filter-class "*DocxFileTests"` and `"*DocxSaveTests"`. Expected: fail.
- [ ] **Step 3:** Implement as described; `PartComparer` reuses the idea of spike 1's `Compare.cs` (decompressed bytes per part, three lists) but is written fresh with tests.
- [ ] **Step 4:** Run both classes. Expected: pass; run once more under `-r <rid>`-independent JIT only (AOT comes in Task 10).
- [ ] **Step 5: Commit** `feat(write): DocxFile opens and saves a DOCX through the Open XML package layer`.

### Task 10: The `write-corpus` tool, the manifest and the AOT proof in CI

**Files:**
- Create: `bench/write-corpus/{WriteCorpus.csproj,Program.cs,CorpusManifest.cs,CorpusCheck.cs,FeatureDetector.cs,CorpusRun.cs}`, `testdata/write-corpus/corpus.json` (empty `files`), `testdata/write-corpus/README.md`, `tests/Raffica.Write.Tests/CorpusToolTests.cs`.
- Modify: `Raffica.slnx` (under `/bench/`), `.github/workflows/ci.yml` (steps below), the test csproj (project reference).

**Interfaces:**
- Produces: `CorpusManifest` (`schema: 1`, `files[]`: `id`, `path`, `sha256`, `source`, `license` (SPDX id), `licenseFile`, `retrieved` (`YYYY-MM-DD`), `features[]`); `CorpusFeatures.Required` = `paragraphs-runs, styles, numbering, sections, tables, merged-cells, footnotes, endnotes, fields, toc, comments, tracked-changes, pictures-inline, pictures-floating, text-boxes, headers-footers, hyperlinks, bookmarks, content-controls, equations, rtl-text`; `FeatureDetector.Detect(string docxPath) → IReadOnlySet<string>` (scans the part XML with `XmlReader` for `w:ins|w:del|w:moveFrom|w:moveTo`, `w:commentReference`, `w:footnoteReference`, `wp:inline`, `wp:anchor`, `w:txbxContent`, `w:sdt`, `m:oMath`, `w:rtl`/`w:bidi`, `w:gridSpan|w:vMerge`, `w:fldChar|w:fldSimple`, a `TOC` instruction, and so on); `CorpusCheck.Run(string root) → IReadOnlyList<string>` problems.
- Commands (exit 0 ok, 1 findings, 2 usage): `write-corpus check --root <dir>`; `write-corpus run --root <dir> [--out <csv>]` (for every entry: open, save to a temp file, compare as `DocxSaveTests` do, print one row `id, opens, saves, unloadedIdentical, loadedEquivalent, ms`; exit 1 on any false); `write-corpus add <docx> --id <id> --source <url|text> --license <spdx> --license-file <path>` (copies the file into `<root>/<id>/`, computes the hash, detects the features, appends the entry).
- `check` problems: a file not in the manifest or an entry without its file; a hash mismatch; a licence not in the allowed list (Global Constraints) or without `licenseFile`; a `features` tag that `FeatureDetector` does not find in the file; a required feature with no file; a missing `source` or `retrieved`.

- [ ] **Step 1: Write the failing tests** in `CorpusToolTests` (small DOCX files from `DocxBuilder` in a temp root): `CheckAcceptsAConsistentRoot`; `CheckRejectsAnUnknownLicence`; `CheckRejectsAHashMismatch`; `CheckRejectsAFileMissingFromTheManifest`; `CheckRejectsAFeatureTagThatIsNotInTheFile`; `CheckReportsAFeatureNoFileCovers`; `FeatureDetectorFindsRevisionsCommentsAndFields` (a builder document with `w:ins`, a comment reference and a `TOC` field); `RunReportsAFailingEntry` (an entry whose file is not a package gives `opens == false` and a nonzero exit).
- [ ] **Step 2:** Run `--filter-class "*CorpusToolTests"`. Expected: fail.
- [ ] **Step 3:** Implement the tool. `WriteCorpus.csproj`: `OutputType` Exe, `AssemblyName` `write-corpus`, `PublishAot`, `IsAotCompatible`, `TrimmerSingleWarn` false, `InternalsVisibleTo` the tests, references `Raffica.Write.Docx` and `Raffica.Core`.
- [ ] **Step 4:** Run the class. Expected: pass. Then publish and run: `dotnet publish bench/write-corpus -c Release -r win-x64 -o out/write-corpus` (Windows needs the `Microsoft Visual Studio\Installer` folder on `PATH`) and `out/write-corpus/write-corpus.exe check --root testdata/write-corpus`. Expected: publish with 0 `IL2xxx`/`IL3xxx` warnings; the check lists the required features as uncovered (exit 1) until Task 11.
- [ ] **Step 5:** CI, `ci.yml` job `build` (both rids), after "Publish the PDF app (Native AOT)": a step "Publish the Write corpus tool (Native AOT)" that runs `dotnet publish bench/write-corpus -c Release -r ${{ matrix.rid }} -o out/write-corpus 2>&1 | tee out/write-corpus-publish.log` and fails when the log matches `IL[23][0-9]{3}` (shell and PowerShell variants, like the neighbouring steps); then "Corpus check and round trip of the published tool" running `write-corpus check --root testdata/write-corpus` and `write-corpus run --root testdata/write-corpus`. In `build-macos`, add the same publish line to the existing loop (`osx-arm64`, `osx-x64`) and the check and run steps for the architecture that executes natively. Add `bench/write-corpus/**`, `src/Raffica.Write.*/**` and `testdata/write-corpus/**` to `.github/scripts/macos-paths.sh` if the file lists paths by project (read it first; keep its test script green: `bash .github/scripts/macos-paths.test.sh`).
- [ ] **Step 6: Commit** `feat(write): corpus manifest, checks and the AOT round-trip tool` (list the files; the CI steps are part of this commit).

### Task 11: Fill the corpus and prove the round trip on it

**Files:**
- Create: `testdata/write-corpus/<id>/<id>.docx` per file, `testdata/write-corpus/licenses/*`, entries in `corpus.json`, `tests/Raffica.Write.Tests/CorpusRoundTripTests.cs`.

- [ ] **Step 1:** The thesis: add `spikes/write/testdoc/thesis.docx` with `write-corpus add` as id `thesis` (source `generated: spikes/write/testdoc, seed 4242`, licence `GPL-3.0-or-later`, licence file `LICENSE`).
- [ ] **Step 2:** Public files until `write-corpus check` reports no uncovered feature. Candidate sources (check each file's licence in its repository before taking it, record the exact URL of the file at a commit, copy the licence text and any `NOTICE` into `testdata/write-corpus/licenses/<source>/`): Apache POI `test-data/document` (Apache-2.0), python-docx `features/steps/test_files` and `tests/test_files` (MIT), the Open XML SDK repository's test files (MIT), docx4j samples (Apache-2.0), other repositories whose licence is on the allowed list. Prefer small files (each ≤ 1 MB, the whole corpus ≤ 15 MB). Each download goes into its own empty temporary folder first and is never opened by anything but `write-corpus add`.
- [ ] **Step 3: Write the failing test** `CorpusRoundTripTests` (theory over the manifest entries; the corpus root comes from the repository like `TestData` does in `tests/Shared`): for each entry, `Open` then `SaveAsync` to a temp file succeeds; every part not loaded is byte-identical to the original; every loaded part is `XmlEquivalence`-equal to its original part; reopening the saved file gives a `ModelSnapshot` equal to the first model's; plus `EveryEntryIsInTheManifestAndTheCorpusCheckPasses` (calls `CorpusCheck.Run` and expects no problems).
- [ ] **Step 4:** Run `--filter-class "*CorpusRoundTripTests"`. Expected: **failures name real gaps** (an element dropped, an ordering, a namespace). Fix the reader or writer (with a new test in Tasks 4 to 7's classes for each gap); never loosen `XmlEquivalence`, remove an entry or skip a row. Repeat until the whole class passes.
- [ ] **Step 5:** `write-corpus run --root testdata/write-corpus` (JIT build and the published AOT build): every row true.
- [ ] **Step 6: Commit** `test(write): the compatibility corpus and its round-trip tests` (the corpus files and licences in the same commit, listed by name).

### Task 12: Word's PDFs, the comparer and the corpus table

**Files:**
- Create: `bench/write-corpus/{PageFacts,PdfPageFacts,PageComparer,TextNormalizer,CorpusTable}.cs`, `tests/Raffica.Write.Tests/PageComparerTests.cs`, `Docs/write-corpus.md` (generated).
- Modify: `bench/write-corpus/Program.cs`, `testdata/write-corpus/README.md`.

**Interfaces:**
- Produces: `PageFacts(int PageCount, IReadOnlyList<string> PageTexts)` (JSON-serialisable with source-generated context, AOT-safe); `PdfPageFacts.ReadAsync(string pdfPath, CancellationToken) → Task<PageFacts>` (through `PdfEngine.OpenAsync`, `PdfDocument.GetPageTextAsync`, `PageText.GetText(0, Chars.Count)`); `TextNormalizer.Normalize(string) → string` (NFC, every whitespace run including no-break space becomes one space, soft hyphens and control characters removed, trimmed); `PageComparer.Compare(PageFacts reference, PageFacts candidate) → PageComparison` (`PageCountEqual`, `ReferencePages`, `CandidatePages`, `EqualPages`, `MeanDice`, `FirstDifferentPage`; the per-page score is the Dice coefficient over the multiset of normalised words, 1.0 for two empty pages; pages are paired by index and surplus pages score 0).
- Commands: `write-corpus pdf-facts <pdf> [--out <json>]`; `write-corpus compare --reference <pdf|json> --candidate <pdf|json> [--require-same-page-count]` (prints the numbers; exit 1 only with the flag and a different count); `write-corpus table --root <dir> --out Docs/write-corpus.md` (one row per entry: id, source, licence, features, opens/saves/identical from the round trip, Word's page count when `word.pdf` exists, otherwise "–"; for files with tracked changes also the page counts of `word-accept.pdf` and `word-reject.pdf`; columns for Write's own layout stay out until 4.2). The README explains the file names and how the owner makes the PDFs (Word on Windows, "Save as PDF", for tracked-change files once after "Accept All" and once after "Reject All").

- [ ] **Step 1: Write the failing tests** (`PageComparerTests`): `NormalizeCollapsesWhitespaceAndRemovesSoftHyphens`; `IdenticalFactsAreEqualWithDiceOne`; `ADifferentWordLowersTheScoreButNotTheCount`; `ASurplusPageCountsAsZero`; `TwoEmptyPagesAreEqual`; `PdfPageFactsReadsTheTestPdf` (`testdata/pdfs/text-basic.pdf`: page count 1, the text contains `Hello,`); `CompareOfAPdfWithItselfIsPerfect`; `TableLeavesWordColumnsEmptyWithoutAPdf`.
- [ ] **Step 2:** Run `--filter-class "*PageComparerTests"`. Expected: fail.
- [ ] **Step 3:** Implement. `table` is deterministic (rows sorted by id) so `Docs/write-corpus.md` can be regenerated and diffed.
- [ ] **Step 4:** Run the class. Expected: pass. Generate the table: `dotnet run --project bench/write-corpus -c Release -- table --root testdata/write-corpus --out Docs/write-corpus.md`; read it.
- [ ] **Step 5: Owner (open):** make `word.pdf` for the thesis and every corpus file, and `word-accept.pdf` and `word-reject.pdf` for files with tracked changes, in Word on the Windows PC, into the folder of each entry; the owner also reports Word's page count of the thesis and whether Word shows "1" on physical page 3 and "A-1" on the first appendix page (open since milestone 4). Command that checks it once they are in: `write-corpus table --root testdata/write-corpus --out Docs/write-corpus.md` (a Word column that is not "–").
- [ ] **Step 6: Commit** `feat(write): comparison of page facts and the corpus table`.

### Task 13: Documents, roadmap evidence and final verification

**Files:**
- Modify: `Docs/DECISIONS.md`, `CLAUDE.md`, `Docs/roadmap.json`, `Docs/ROADMAP.md`.

- [ ] **Step 1:** `Docs/DECISIONS.md`: a new entry "Raffica Write: document model and DOCX package layer (M4.1)" in the file's format: the SDK as package layer only with the model read through `XmlReader` and written as raw XML; "a model part is rewritten on every save, every other part keeps its bytes, a guard compares them before the replace"; the kept-unknown design (unknown elements and attributes in place, property bags, fields and revisions as views); `DocxParts` as the hook for 4.2's lazy loading; the corpus design (manifest, detected feature tags, allowed licences, own files plus public files, the owner's local files record only the result); the AOT proof in CI; the licence check of the SDK; what is open (the owner's Word PDFs). Note the rejected alternatives: the SDK's typed DOM as the model (re-serialises, ties the model to the SDK), one project (the layout library of 4.2 needs the model without the SDK), a dirty flag per part (not needed for the acceptance, 4.3 can add it).
- [ ] **Step 2:** `CLAUDE.md`, section "Project-specific", a paragraph "Write library (M4.1)": where the projects are, the vocabulary rule (later milestones add accessors, never a second storage), the save rule and guard, `AtomicFile`, the corpus (`testdata/write-corpus`, `write-corpus` commands, manifest rules, `Docs/write-corpus.md` is generated), the pitfalls found while doing the work (write them from what actually happened, none from this plan). References name types and sections, never line numbers.
- [ ] **Step 3:** Run the full verification and read each result: `dotnet build Raffica.slnx -c Release`; `dotnet run tools/native-fetch.cs -- --rid win-x64 --out artifacts/native/win-x64`; `RAFFICA_REQUIRE_OCR=1 dotnet test Raffica.slnx -c Release --no-build --max-parallel-test-modules 1`; `dotnet format Raffica.slnx --verify-no-changes`; `dotnet run tools/licenses.cs -- check`; the AOT publish of `bench/write-corpus` with its log searched for `IL[23][0-9]{3}` (expected: no match); `out/write-corpus/write-corpus run --root testdata/write-corpus` (every row true); `dotnet run tools/roadmap.cs -- check`.
- [ ] **Step 4:** `Docs/roadmap.json`: tick the three tasks; tick the acceptance criteria with evidence: criterion 1 (`CorpusRoundTripTests` and the published tool's `run` over N files, the commit), criterion 2 (`DocxFileTests.ModelChangeRoundTrips`), criterion 3 (the publish log of the tool, 0 `IL2xxx`/`IL3xxx`, and the CI step name). Add the open owner items as a block (Word's PDFs, Word's page count of the thesis, the check command of Task 12). Status `done` with `completedOn` only when every criterion above has evidence; regenerate `Docs/ROADMAP.md`; `dotnet run tools/roadmap.cs -- check`.
- [ ] **Step 5: Commit** `docs: milestone 4.1 done (document model and DOCX round trip)`, then push the branch and open the PR to `dev` (body without attribution lines); merge once every check is green.

---

## Self-review

- **Spec coverage (row 4.1 and the sections it names):** model of paragraphs, runs, styles, numbering, sections, tables, footnotes and endnotes, fields, comments, tracked changes, pictures, unknown kept → Tasks 3 to 7; SDK under AOT, unloaded parts keep bytes, atomic save, symlink → Tasks 8 and 9; corpus with source and licence check, comparison tool through PDFium → Tasks 10 to 12; the three acceptance criteria → Tasks 9, 11 and 10 (CI) with evidence in Task 13. `.docm` kept, external relationships never fetched, no silent repair (typed exception instead of a repair) → Task 9. Right-to-left text is kept like any content and detected as a corpus feature. Gap on purpose: the Write side of the comparison (4.2) and the owner's Word PDFs (open, Task 12).
- **Type consistency:** `Story`, `PropertyBag`, `UnknownContent`, `SectionProperties.HeaderFooterReferences`, `DocxParts`, `DocxFile.SaveAsync`, `AtomicFile.*`, `PageFacts` are named once in the vocabulary or an Interfaces block and used with the same names later.
- **Proportion:** the plan is longer than the spec's row for 4.1 because the row hides a large library; each task gives signatures, test names with their assertions and the pinned values, and no method bodies.
