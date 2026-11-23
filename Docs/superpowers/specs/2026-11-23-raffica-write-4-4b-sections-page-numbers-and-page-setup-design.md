# Raffica Write 4.4b: sections, page numbers, headers and footers, page setup (design)

Status: draft for approval, 2026-11-23. Milestone 4.4.2 of `Docs/roadmap.json`. The subproject spec is `Docs/superpowers/specs/2026-11-13-raffica-write-design.md` (section 6 "Structure", goals 4 and 9); this document only settles how 4.4b builds what that spec already decided. 4.4a (headings, lists, outline) is done and its limits are named where they touch this milestone.

Items marked **(call)** are decisions of the author of this document, not of the owner. The owner decided on 2026-11-23: approach C below (one action, visible sections, smart defaults), that "Insert page number" is part of 4.4b, and that removing a section marker merges the section into the previous one (the previous section's setup wins, unlike Word).

## Intent

The spec's rule is that Word hides its structure, so simple wishes become recipes. The owner's example: page numbers that start at "1" on page 3 take five steps in Word (section break, unlink the footer, format the numbers, start at 1, delete the numbers before). Write does it in one action, shows the section it made, and lets the user take it back with one click. The same goes for the appendix "A-1", Roman front matter, a landscape page, two columns, a different first page and left and right pages. Whatever the user does, the file holds what Word expects, and the number on every page is the number the model says.

## Research behind the design (2026-11-23)

Word needs hidden section breaks, a per-type "Link to Previous" and a "Start at" in a separate dialog; its odd and even setting is document-wide and leaks into earlier sections. Google Docs has coarse "which sections get numbers" and "start at". LibreOffice uses page styles (a different model). Apple Pages shows sections in a sidebar tab ("Start at", "Continue from previous section", "Match previous section", "Hide on first page of section"), the clearest of the four. The OOXML rule that helps most: a missing header or footer reference means "same as the previous section", so an unlink is needed only when the content differs. Write takes Pages' visible sections, keeps Word's file format, and unlinks only when it has to.

## Goals

1. **Number pages from here, in one command** (`section.numberFrom`). At the caret's place the command makes a next-page section start (when the caret is not already at the start of one), sets the number to start at (default 1) and its format (1, i, I, a, A), and makes the numbers appear. An optional prefix ("A-") gives the appendix "A-1". The earlier pages keep what they had.
2. **Sections are visible and removable.** Every section start has a marker in the page view (not printed) and a row in a Sections pane. Removing a marker merges its section into the previous one (one undo step).
3. **Headers and footers per section, first page and left and right pages.** "Different first page" is per section, "Different left and right pages" is one document switch, and "same as previous" or "own" is per section, header and footer, and page type. The status line says which section a header or footer being edited belongs to and which sections share it.
4. **Insert page number** (`PAGE`, "Page N", "Page N of M", "N / M") into a header or footer at a chosen place.
5. **Page setup**: size, margins, orientation, columns, each for this section, the whole document or from here on.
6. **Plain section breaks** (next page, continuous, even page, odd page) as a command, because the primitives are the same **(call)**.
7. **The outline can move a chapter that holds a section break** (4.4a blocked it).
8. **Never break (acceptance).** After every command of this milestone and after undo and redo of each, the incremental layout equals a full layout, every page number drawn equals an independent count of the model, and the saved file read again gives the same numbers (tests); Word opens the files without repair and shows the same numbers (owner gate).

## Non-goals and anti-goals

- No page borders, line numbers, vertical alignment of the page, watermark, header or footer gallery, gutter or mirrored margins (the layout supports neither; `w:gutter` stays as read), text direction, per-section footnote settings, or "Page X of Y in section" looks (`SECTIONPAGES` is read and laid out today, it gets no command).
- No new fields beyond `PAGE` and `NUMPAGES` (the field tools of 4.5 own the rest). No removal of header, footer or numbering parts: like numbering definitions in 4.4a, a part Write made stays in the package unused after an undo, and a redo finds it.
- Anti-goals: a number on screen that differs from the model; a silent change of what an earlier section shows; a header that changes in sections the user did not mean (shared content is said out loud); a marker that reaches print or PDF.

## Constraints that bind

- Everything of `CLAUDE.md`: Native AOT, English source with German localization ("Sie"), every command its own Lucide icon and a reason in every disabled state (`WriteCommandRules`), strings in both resx files (LF), cards through `CardFooter`, no warning suppressed.
- **The model is the vocabulary:** sections are read and written through `SectionProperties`, `Paragraph.SectionBreak`, `Story.FinalSection`, `HeaderFooterReference`, `HeaderFooterPart`, `WordDocument.HeadersFooters` and `DocumentSettings.EvenAndOddHeaders`; the section list is a view, not a second storage; children are placed by `SchemaOrder`; unknown children stay.
- **Every model change goes through the lease and `EditSession.Apply`, one history step per command, every raise attributed** (`DocumentEdits.Attribute`). The body's final `w:sectPr` is not a body block, so a change of it must reach `BodySnapshotCache` and the recovery copy (a case in `SnapshotCacheOracleTests`, byte equality with the user-save writer).
- **Incremental equals full** (`LayoutOracle.AssertEqualToFull`, exact). A command that changes a section's properties restarts the layout as `Restyle` does; a new fast path needs a corpus oracle case first.
- **Start cost.** Nothing of sections, the Layout tab's pickers, the pane, the marker overlay or the page-number builder exists before first use (`StartupCreatesNoSectionWork`). A new ribbon tab must not change the Home band's width hint (`RibbonStartupTests`); measure `--measure-startup` against the commit before 4.4b.
- **Typing stays at most 16 ms** with the Sections pane open (the `typing-pane` rows). The `typing`, `format` and `structure` limits are not loosened.
- Header, footer and section state is read on the UI thread, lazily and bounded like the formatting facts (memoized by model, revision and selection); a document with very many sections never costs a keystroke.
- A save, a snapshot and a prefix open obey the 4.1 rules: a prefix's `Body.FinalSection` is not the file's final section and the commands are refused on a prefix.

## Approaches considered

- **A. Word clone** (separate dialogs, hidden breaks). Cheapest, repeats the pain the milestone exists to remove. Rejected.
- **B. Sidebar only** (Pages' Section tab). Clear, but several clicks and no marker in the page. Rejected as the whole answer; its pane becomes part of C.
- **C. One action, visible sections, smart defaults (chosen by the owner).** One command builds the structure, the page view and a pane show it, unlinking happens only when the content must differ, and page setup takes an "Apply to" scope.

## Design

### Sections as a view

A `SectionList` view (Model or Editing, builder's choice) lists the body's sections in order: the `SectionProperties`, the break paragraph that ends it (null for the last), the first and last top-level block, and for each header or footer kind and page type whether the section has its own reference or inherits (and from which section). Inheritance follows the OOXML rule (a missing reference inherits from the previous section; an empty story is "own and empty" unless the layout reads it otherwise; `titlePg` without a first reference follows what Word does, to be checked in Word and recorded). The layout's `SectionPlan` and this view must agree: one test compares them for every corpus file.

### Number pages from here

`section.numberFrom` has a small card (Start at, Format, Prefix) with the default "1, 1 2 3, no prefix" and a one-click default in the ribbon picker. **The place (call):** the break goes before the top-level body block that holds the caret (a caret in a table or a block content control puts it before the table or control); the earlier half ends with the break, carried by the previous paragraph when that is a plain paragraph with no break, otherwise by a new empty paragraph, so no blank line is added in the usual case. A caret in the first block of the document or of an existing section makes no split and edits that section only; the command never makes an empty or redundant section. A caret in a header, footer, note or comment, or a prefix document, disables it with a reason. When the page the caret is on does not start with that block, the card says "This starts a new page here".

**The structure Write builds.** The earlier half gets a clone of the old section's properties (so its pages keep their headers, footers and numbering); the later half keeps the existing end properties and gets `w:type` next page, `w:pgNumType` with `w:start` and `w:fmt` (the start omitted for "continue"), and, when it is a new section made by a split, no `w:titlePg` (a new section never inherits "different first page", so its first page shows the number; the pane can switch it on again). **Footers (call, see "Settled after the builder check"):** the command looks at every footer type the later half uses (default; first only if `titlePg` stays on; even only if the document has left and right pages). For each such type that resolves to a footer holding a `PAGE` field, nothing is written for it (the section inherits, no unlink). For each that resolves to none, the later half gets its own footer of that type with the number centered; with a prefix, its own footer of each type: a copy of the inherited footer with the prefix text before its `PAGE` field. The earlier half is left as it was, which is the page-3 case: no number before, "1" from here. The prefix is footer text, not a section property, so the pane does not show it.

### Visible markers and the Sections pane

**Marker (call):** a small chip at the start of each section after the first, in the margin or beside the page's top-left corner, drawn by the page view over the layout's pages and never part of a `LaidOutPage` (so print and PDF export never get it). It names the section ("Section 2"), reads "numbers start at 3" or "continues" and opens a popover with the same controls as the pane row and "Remove marker". It has an accessible name and is reachable by the keyboard through the pane. The writing view has one strip and shows no markers. **The pane:** one row per section (start page, number style, first page different, header and footer "same as previous" or "own", the page setup in one line), the document row "Different left and right pages", and a hint at the top. It is read like the formatting inspector (lazy, memoized, bounded; beyond 200 sections it shows "too many to list"); it shares the right pane slot with the formatting pane (one at a time) **(call)**; the View tab's Panes group gets its command. Rows act on the section they name.

**Remove marker (the owner's rule):** the section merges into the previous one: the paragraph that carried the previous section's break loses it and the later section takes the previous section's properties (page setup, numbering, header and footer references, `titlePg`) while keeping its own break or final identity. If the removed section had its own header or footer parts they stay unused in the package. One undo step. The popover says what changes when the two setups differ (page size, orientation, columns) before it is confirmed.

### Headers and footers

Entry stays as in 4.3a (double click in the region; Esc or a click leaves). **New:** while a header or footer is active the status line says which one ("Footer, section 2, shared with sections 3 to 5"), and the pane row of that section is highlighted. **Own or same as previous (call):** "own" creates a new part (new relationship id, a `w:hdr` or `w:ftr` root, a copy of the inherited content so nothing changes on the page) and a reference in this section's properties; "same as previous" removes that reference (the part stays unused). The first section is always "own". **Different first page** sets or clears `w:titlePg` in this section; turning it on gives the section its own empty first header and footer only where no first one resolves (own or inherited), as Word shows an inherited one. **Different left and right pages** sets `evenAndOddHeaders` in the settings (created if missing); turning it on gives each section that has its own default header or footer an even one that copies it, and sections that inherit their default inherit the even one too; turning it off leaves them in place, as Word does. Editing a shared part edits it for every section that shares it, and says so.

### Insert page number

`insert.pageNumber` is a picker: the look ("1", "Page 1", "Page 1 of N", "1 / N"; the words are in the UI language at the time of insertion **(call)** and are plain text the user can edit) and the place (header or footer; left, center, right). It writes into the story the caret's section uses for that place: the caret in a header or footer uses that story at the caret; otherwise the footer or header of the caret's section, which is shared content when inherited (the status line says so). A paragraph that already holds a `PAGE` field in that place is replaced, not added to. The field is a complex field (`fldChar` begin, `instrText`, separate, the result, end) built by a new field writer in the editing library; `NUMPAGES` likewise; the cached result is the number the layout shows for the caret's page. A field stays one unit for the caret, as 4.3a ruled.

### Page setup

`SectionProperties` gets a typed accessor for `w:pgSz` (and `w:orient`, which the layout does not read and Write must write), `w:pgMar` and `w:cols`. Commands (Layout tab, group Page setup): `page.size` (A4, Letter, A3, A5, Legal, and custom in the card), `page.margins` (Normal, Narrow, Moderate, Wide, and exact values; units cm or inch by region as `BlankPaper.For`), `page.orientation` (swaps width and height and sets `w:orient`), `page.columns` (one, two, three, and exact count, spacing and a line between), and `page.setup` (the card with all of them and **Apply to**: this section, the whole document, from here on). **Scope (call):** the default is the whole document when it has one section, otherwise this section. "From here on" splits the section as the numbering command does: a next-page break for size and orientation, a continuous break for columns. Whole document sets every section, including the final one, as one step. Header and footer distances are not touched. The layout already reads these properties and supports continuous sections with different columns; a change restarts the layout like `Restyle`.

### Plain section breaks

`section.break` is a picker (next page, continuous, even page, odd page) that splits at the caret's place by the same rule and writes `w:type`. The marker removes it again.

### The editor and the outline

The editor keeps refusing to delete or merge a paragraph that carries a section break (4.3a); the refusal now shows a reason that points to the marker (**call**). **The outline move:** a subtree that holds a section break moves like any other (the break paragraph travels with its chapter as the same object). A move is refused with a reason when it would leave a section-break paragraph as the last body block (an empty final section, the case 4.4a recorded: Word may show an extra blank page), as the table-last rule refuses a body that ends in a table. The Word-check file `numbered-thesis-chapter-move.docx` is regenerated and checked again.

### Proof of "never break"

An independent reference count (`Support/ReferencePageNumbers`, no layout and no `FieldContext`) gives the number of every page from the sections alone (start, format, continue), and a drawn-numbers reader compares it with what the pages show, as `ReferenceNumbering` and `DrawnLabels` did for lists. `SectionSteps` (new oracle steps beside `StructureSteps`: number from here, break of each type, remove marker, own and same as previous, first page different, left and right pages, insert page number, each page setup command with each scope, undo and redo) runs on the corpus files and on generated documents with several sections; after each step the layout equals a full one exactly and the numbers equal the reference; every kind must run (`RanPerKind`). Hand-made cases pin the Word rules named above (`titlePg` inheritance, an empty story, even and odd without a first-page reference).

### Measuring

A `sections` case (engine bench `edit`, the app switch `--measure-sections`, `measure.cs`): on the thesis, number from page 3, the appendix from "A-1", remove a marker, page setup for the whole document and for this section, insert a page number, the Sections pane open while typing. The command rows are recorded first and one fixed limit is set from the median after the first runs (aim 100 ms on the Linux laptop from the command to the first visible page; the project's measure-then-set rule, **call**); the full layout after a command on the thesis is recorded, not judged.

## Owner gates (open until recorded)

- **Word**, through `pwsh tools/word-open-check.ps1` and Word's own adjusted page number of every page read by automation (as 4.4a read `ListString`): files made by the commands open without repair and show the same numbers: the page-3 "1" on a blank document of three pages; the thesis's appendix "A-1" made with the one command; Roman front matter and Arabic body; different first page; left and right pages; a landscape section; two columns; a moved chapter that holds a section break (rows in `Docs/write-word-check.md`).
- A look at the Native AOT app on a real display on Windows, the Linux laptop and the Intel Mac: the marker chip, the pane, the popover, the cards.
- The `sections` and typing-with-pane cases on the Linux laptop and the Intel Mac (`measure.yml`).

## Left to the builder

The shape of the section step in `EditHistory` (it must undo and redo the properties, references, the settings flag and the paragraph structure as one step); the name and place of `SectionList` and the field writer; the exact marker geometry and popover; whether `section.numberFrom` and `section.break` share one splitter; the ribbon groups of the new Layout tab within the key-tip rules (the Insert tab is 4.5's call); the macOS menu lines (no shortcuts in 4.4b).

## Documents

`Docs/roadmap.json` (plan link, status, tasks) with the regenerated `Docs/ROADMAP.md`; `Docs/DECISIONS.md` gets "Raffica Write: sections, page numbers and page setup (M4.4b)"; `CLAUDE.md` a "Write sections (M4.4b)" section; `CHANGELOG.md` `[Unreleased]` the user-visible changes; `Docs/write-word-check.md` the new rows.

## Settled after the builder check

Calls of the author (a wrong guess here changes the plan little, so they are settled rather than asked; the first two follow Word's own behavior):

- **"From here" reaches the end of the document (call).** A command that starts something at a section (numbering, page setup "from here on") applies to the new section and to every later section that does not set its own value: a later section that inherits a footer inherits the new one, a later section without its own `w:pgNumType` continues the new count. A later section that has its own footer, `w:pgNumType`, size, orientation or columns keeps it. The card names how many sections follow ("Sections 3 and 4 continue from here") when there are any.
- **Page setup "from here on" (call).** It splits at the caret's place and then sets the changed property (size, orientation, margins or columns, not the others) in the new section and in every later section, as Word's "this point forward" does, as one step. The break type is next page, except continuous for columns.
- **Page N of M (call).** `NUMPAGES` is the physical total, as in Word, so "Page 1 of N" on a document whose numbering restarts counts the front matter in N. All looks stay on offer; when the document's numbers are not 1 to N (a restart or another format), the picker marks the "of N" looks with the hint "N counts every page". The reference count of the tests keeps `NUMPAGES` physical.
- **The merge, exactly (call).** Removing the marker of section k merges it into section k-1. The paragraph that carried the break of section k-1 loses its `w:sectPr`. The end properties of section k (the break of its last paragraph, or the body's final `w:sectPr`) keep their object and take the whole content of the former properties of section k-1 as a clone: attributes, every child, unknown ones included, the header and footer references, `w:type`, `w:pgNumType` and `w:titlePg`; a child that section k-1 does not have is absent afterwards. The rule is the same for a middle and the final section. The merge is one undo step and the popover names what changes when the two setups differ.
- **Where "Insert page number" lives (call).** In the Layout tab, group "Header and footer" (the Insert tab is 4.5's call and may move it then). The Layout tab has three groups: Page setup, Sections, Header and footer.
