# Raffica Write 4.5b: bookmarks, captions, cross-references and tables of contents (design)

Status: draft for the owner's approval, 2026-11-25 (builder check done, see "Rules settled after the builder check"). Milestone 4.5.2 of `Docs/roadmap.json`. The subproject spec is `Docs/superpowers/specs/2026-11-13-raffica-write-design.md` (goal 8, section 6 "Long-document tools" and "Field results in the file"); this document settles how 4.5b builds what that spec decided. Hyperlinks, the macOS search key and find and replace are 4.5c and are not built here.

Items marked **(call)** are decisions of the author of this document, not of the owner. The owner decided in the brainstorming of 2026-11-25: **Write recomputes only the fields it fully understands and leaves every other field's stored result untouched, marked "not updated by Write"** (never a number Write computed differently from Word); **refreshed results live in the model** (approach B below); the feature set of "What the user gets" was read back and accepted ("fine").

## Intent

A thesis needs numbered figures and tables, references to them, and a table of contents, and Word makes the user press Ctrl+A, F9 to keep them true. Write's promise (goal 8) is that these are always current: when a heading moves, a figure is inserted or the pagination changes, the numbers, references and the table of contents are right without a command, and the file carries the results Word shows (no `updateFields`, no `w:dirty`). The ground truth stays that of 4.3 to 4.5a: what Write writes, Word opens without repair, and the incremental layout equals a full layout.

## What exists today (checked 2026-11-25)

- **Bookmarks:** `BookmarkStart` (id, name) and `BookmarkEnd` (id) are typed inline markers and round-trip (`StoryXml`); nothing creates, lists, moves or protects them. `Hyperlink.Anchor` can name one.
- **Fields:** `FieldChar` (begin, separate, end) and `InstructionText` are run content, `SimpleField` is an inline; `FieldScanner` is a read-only view. `ParagraphProjection` makes a field result one atomic unit of the visible text (the caret steps over it; Delete selects it first). The layout evaluates only `PAGE`, `NUMPAGES`, `SECTIONPAGES` and `SECTION`, in headers and footers, through `IProjectionHost.FieldValue(instruction)`, which sees the instruction text and nothing else (no identity, no document position). Every other field shows its stored result.
- **Numbers and pages:** heading labels come from `NumberingCounter` (the layout) and, as an independent count, `ReferenceNumbering` (test support); `DocumentLayout.Locate(bodyBlockIndex)` gives the page of a top-level block; note marks come from `IProjectionHost.NoteMarkText`.
- **Styles:** the blank document has `Caption` and `TOC1` to `TOC9` in its 24 style ids; `Heading1` to `Heading9` detection is by style id, never by name (4.4a).
- **Edit machinery:** `DocumentEdits.Revision` is the dirty flag and the base of undo; `EditHistory` clears itself when the revision moves behind its back; steps with more than one `BlockChange` are not recorded unless they carry a swap record (`SectionChange`, `ObjectChange`, `StyleChange`); `EditTracker` counts raises against attributions and a mismatch makes the recovery snapshot write the whole body.
- **Corpus:** files with TOC fields exist (`FeatureDetector` tag `toc`); the generated thesis has headings, tables and figures.

## What the user gets (goals)

1. **Bookmarks.** One command and a card (list, Go to, Add, Delete). Names follow Word's rule (a letter first, then letters, digits and underscores, at most 40). Names that begin with `_` are Write's own (`_Toc…`, `_Ref…`) and are never listed. Bookmarks are not drawn on the page. Deleting text that holds a bookmark moves the anchor to the nearest remaining position (spec section 6); a bookmark whose whole range is deleted is removed, and fields that refer to it show the stored result and the mark "not updated" (never an error text).
2. **Captions.** "Insert caption" takes a label (Figure, Table, Equation and any `SEQ` label already in the file) and the text. It writes a `Caption`-style paragraph in Word's own shape: the label word, a `SEQ` field and the text. Default position above for tables and below for figures, changeable; also offered on a selected picture or table. The label word follows the document language, then the UI language ("Abbildung", "Tabelle", "Gleichung") and is ordinary editable text afterwards **(call)**. An optional chapter number gives "Figure 2-1": `STYLEREF 1 \s`, a separator and `SEQ <label> \s 1`; it needs numbered headings (the 4.4a switch) and refuses with a reason without them **(call)**. Numbers follow any insert, delete or move.
3. **Cross-references.** A card sketch:
   ```
   Reference to:  [ Figure ▾ ]          Insert as:  (•) Label and number  "Figure 3"
    ┌ Figure 1  Overview of …           ( ) Number only      "3"
    │ Figure 2  Results by …            ( ) Caption text
    └ Figure 3  Setup of the …          ( ) Page             "12"
                                               [ Insert ]  [ Cancel ]
   ```
   Targets are headings, captions, footnotes, endnotes and bookmarks (heading: number, text or page; footnote and endnote: number or page). Write adds a hidden `_Ref` bookmark over the target (or reuses the one it made) and writes `REF`, `PAGEREF` or `NOTEREF` with `\h` (a link). The reference is one unit like any field result.
4. **Table of contents and lists.** The card sets the levels (1 to 3 by default, up to 9), page numbers, right-aligned numbers and the dot leader; the kind is contents, list of figures, list of tables or any other label. Entries come from `Heading1` to `Heading9` styles (`\o`) and from outline levels (`\u`); each entry is a link to a hidden `_Toc` bookmark on the heading, its number label, a tab and a `PAGEREF`, in styles `TOC1` to `TOC9` and `TableofFigures`, as Word writes them. Lists of figures and tables are `TOC \h \z \c "<label>"`. Removing one is the command "Remove table of contents" or selecting and deleting it.
5. **Always current.** Order-based results (`SEQ`, heading numbers in references, caption text, the entry list of a table of contents) are brought current as soon as the editing burst is quiet (250 ms, never inside a key handler); page-based results (`PAGEREF`, the numbers in a table of contents) when the layout is complete, repeating until stable (at most three passes, then the last result stays and the case is counted). Save runs a synchronous settle first; print and PDF export (4.7) call the same settle. Opening a file and refreshing on screen does not dirty the document; the refreshed results reach the file with the next save the user makes (spec section 6).
6. **Fields Write does not understand are left alone.** The supported set is the table below; any field with another name or another switch keeps its stored result byte for byte and is marked. A document with such fields shows one bar above the page, once per document session ("n fields use options Write does not update; they show Word's last result"), and the status line says so when the caret is inside one **(call)**.
7. **Never break (acceptance).** After every command of this milestone and after every edit that moves a field, and after undo and redo of each, the fields equal an independent count made from the model alone, the incremental layout equals a full layout, the saved file read again gives the same model, and Word opens it without repair (tests, the oracles of 4.3a to 4.5a, `word-open-check`).

### Supported fields (call; every other switch makes the field "not updated")

| Field | Supported | Result |
|---|---|---|
| `SEQ <label>` | `\* ARABIC` and the other number formats of `PageNumbers`, `\n`, `\c`, `\r <n>`, `\s <n>` (reset after the heading of level n) | the running number in document order, restarts per switch |
| `STYLEREF 1` | `\s` (the heading number, for a chapter number) | the number label of the last `Heading1` before the field |
| `REF <bookmark>` | `\h`, `\n`, `\r`, `\* MERGEFORMAT`, `\* CHARFORMAT` (formatting switches are kept, not applied) | the bookmarked text, or with `\n` and `\r` the number label of its paragraph without a trailing period; plain text in the field's own run formatting **(call: Word copies the source formatting, Write does not)** |
| `PAGEREF <bookmark>` | `\h` | the printed page number (section format and start respected) |
| `NOTEREF <bookmark>` | `\h`, `\f` | the note's mark text |
| `TOC` | `\o "a-b"`, `\u`, `\h`, `\z`, `\n [a-b]`, `\c "label"` | entries as in goal 4 |

Fields are refreshed in the body, in table cells and in block and inline content controls. Fields inside a tracked insertion or deletion, a header, a footer, a note or a comment keep their stored result (limit; they are counted for the bar).

## Non-goals and anti-goals

- No index, bibliography, table of authorities, table of contents from custom styles (`\t`), `STYLEREF` other than the chapter number, `\p` above and below, `\w` full context, `MERGEFORMAT` application, equation editing (an equation caption labels a paragraph the user typed), no manual "Update fields" command (the point of goal 8; a field Write cannot update stays marked).
- Anti-goals: a number in a file Write saved that differs from the number on screen at that save; a refresh that makes the document dirty, enters the undo list as a step of its own or loses the user's redo; a refresh inside a key handler or during an IME composition or a save; a table of contents that never settles; a field result that Word shows differently without a recorded reason; a heading, caption or bookmark the file does not carry exactly after a save.

## Constraints that bind

- Everything of `CLAUDE.md`: Native AOT, English source with German localization ("Sie"), every command its own Lucide icon, a reason in every disabled state (`WriteCommandRules`), strings in both resx files (LF), cards through `CardFooter` and registered in `CardFooterTests.Entries` and `CloseGuardTests.CardsAreBuiltByCardFooter`, no warning suppressed, no key with Ctrl plus Alt.
- **The model is the vocabulary:** results are real runs and paragraphs in the model; a typed accessor `FieldView` (instruction, parts, result range) replaces ad-hoc scanning; unknown content stays in place; new children are placed by `SchemaOrder`. `Raffica.Write.Editing` references only Model, so everything that needs layout knowledge (page of a block, heading label, note mark) arrives through a small interface (`IFieldHost`) that the app implements over `DocumentLayout` and `NumberingCounter`.
- **Start cost.** Nothing of fields, bookmarks, the new ribbon tab or its cards is built before first use; the first settle of a document runs after the layout is complete, never before the first sharp page (`StartupCreatesNoFieldWork`); the Home band's width hint is unchanged (`RibbonStartupTests`) and `--measure-startup` is compared with the commit before 4.5b.
- **Speed.** Typing stays within 16 ms with a table of contents of 100 entries in the document (the refresh is debounced and off the key path, `typing-fields` counts into `typing`). A command and a settle on the thesis have the limits the first measurement sets (budget times 1.25, fixed on every platform, the 100 ms aim named and claimed only when met). A refresh that finds nothing to change costs a walk of the paragraphs touched since the last one, never the whole document.
- **Threads and leases.** A refresh writes under the edit lease on the UI thread, never while a save, an IME composition or a measured run holds the session, and is attributed to every observer (layout, snapshot tracker, selection).

## The central design: a derived refresh in the model

**Approaches considered.**

1. **A. Overlay.** The model keeps the stored text; the layout computes the displayed value; a "materialize" step copies values into the model before save. Rejected: a table of contents is many paragraphs, which an overlay would have to swap inside the layout behind the editor's back; the text on screen, in the editor and in the recovery snapshot would differ, which breaks the one text definition (`ParagraphProjection`) and the oracle.
2. **B. A derived refresh in the model (chosen, owner, 2026-11-25).** A `FieldRefresh` service recomputes results and writes only what differs. One text for everything.
3. **C. Every refresh an ordinary edit.** Rejected: opening a file would dirty it and a typed letter would add undo steps for the table of contents (the spec forbids the first).

**Rules of B (the parts that are easy to get wrong):**

- **Not an edit.** A refresh does not call `DocumentEdits.Raise`: the revision, the dirty flag and the expected revision of `EditHistory` are unchanged. It is attributed to the snapshot tracker through a new `DocumentEdits.AttributeDerived(changes)` that marks blocks dirty without counting a raise, and reaches the layout through `DocumentLayout.ApplyEdit` like any change. A document that was clean stays clean, and a recovery copy is not written because of a refresh alone.
- **Content-only changes are not recorded.** Replacing the text inside existing paragraphs (caption and reference numbers, `PAGEREF` numbers, the text of an entry) changes no block index, so no recorded step can be misread. After every edit, undo and redo the refresh runs again, because an undo restores old paragraph snapshots whose results are stale.
- **Structural changes are recorded with the step.** A change of the number of paragraphs of a table of contents or list shifts the indices of everything below it, which every older step relies on. Such a change is a swap record (`FieldChange`, in the way of `SectionChange`) carried by the newest user step: undo reverts it before the step's own blocks, redo replays the step and then the record (replayed, not re-derived). A step that carries one does not coalesce further typing. With an empty history nothing is recorded and nothing is needed. **While the redo stack is not empty a structural refresh waits** (the stale entry list is shown until the redo is used or dropped; this is a named limit), and a structural refresh found necessary after an undo or a redo that should have restored the state exactly is a bug the oracle asserts never happens.
- **Positions.** A caret or selection that lies in a replaced paragraph moves to the start of its field; a field result is atomic, so the caret is never inside one.
- **Who may refresh.** Prefix documents, Faulted sessions, a layout that is not current, a save in progress and an IME composition all skip the refresh without a trace and run it later. A refresh failure is counted and retried; it never shows a card and never breaks typing.

## How it is built (stages, each ending green with its own oracle step kinds)

1. **Bookmarks and the field model.** `FieldView`, the bookmark service (create, rename-free list, go to, delete, anchor movement under edits), the Bookmark card, the independent `ReferenceFields` oracle (counts everything from the model alone, like `ReferenceNumbering`; change it only to follow Word's rule).
2. **The refresh engine.** `FieldRefresh` with `IFieldHost`, the derived-change plumbing (`AttributeDerived`, the `FieldChange` record in `EditHistory`, the session trigger and debounce, the lease), `SEQ`, `STYLEREF`, `REF`, `NOTEREF` and the "not updated" mark, in step kinds of `FieldSteps` and `SnapshotCacheOracleTests`.
3. **Captions and cross-references.** The commands and cards, the hidden `_Ref` bookmarks, the caption on a picture or table, the chapter number.
4. **Tables of contents and lists, and the page loop.** `TOC` with the entry paragraphs and styles, `PAGEREF`, the settle with at most three passes, Save's synchronous settle, the bar and the status text.

## Ribbon and commands (call)

A new **References** tab (key tip `R`): groups Table of contents, Captions and Links. Commands (each with its own Lucide icon, strings in both languages, a reason in every disabled state and a line in `WriteMacMenu`; no keys, no macOS key): `toc.insert`, `toc.remove`, `caption.insert`, `xref.insert`, `bookmark.insert`. `WriteCommandCatalog.Find` skips them. The Home band's hint is unchanged.

## Word compatibility (what a file must satisfy)

A complex field is begin, instruction, separate, result, end with the instruction in one `w:instrText` run; a `TOC` field wraps its entry paragraphs (begin in the first, end in the last); an entry is a `w:hyperlink` with `w:anchor`, its text, a tab and a `PAGEREF … \h` field; bookmark ids are unique and every `bookmarkStart` has its end; `_Toc` and `_Ref` bookmarks are hidden by name; no `w:dirty` and no `w:updateFields`; a caption paragraph is style `Caption` with the `SEQ` field in the order Word writes. Checked on the Windows PC with `tools/word-open-check.ps1` and a new script `tools/word-field-results.ps1` that opens a file in Word, updates all fields through automation and compares Word's results with the stored ones: **everything but page numbers must be equal** (page numbers equal where the pagination equals; each difference is recorded with its reason in `Docs/write-word-check.md`).

## Measuring

New cases on the thesis, each undone outside the timing: `caption-insert`, `xref-insert`, `toc-insert`, `bookmark-add`, `refresh-heading-edit` (an edit that changes the entry list), `refresh-pages` (a settle after a pagination change), `first-settle` (a document with a stale table of contents), each recorded; the metric `fields` is the worst median of the command cases, judged against a fixed limit set from the first measurement as in 4.4a to 4.5a; the settle is recorded and judged only against its own fixed limit once measured; `typing-fields` counts into `typing`. The case lists change together (the app's places, the bench's cases, `measure.cs`).

## Owner gates (planned)

Word opens the files made by every command without repair and Word's own field update gives the same results (script check as in 4.4 and 4.5a; owner, recorded); a look at the Native AOT app on a real display on Windows, the Linux laptop and the Intel Mac for the References tab, the cards, the bar and a table of contents that follows an edit (owner); `measure.yml` target `write` with the new cases on the Linux laptop (mains) and the Intel Mac; the IME check is not part of 4.5b.

## Known limits that will be named in the decision

Fields in notes, headers, footers, comments and tracked changes are not refreshed; `REF` does not copy the source formatting; a structural refresh waits while a redo is pending; a table of contents with `\t`, `\w`, `\p`, `\x` or another switch is not updated; page numbers in a table of contents can differ from Word's where the pagination differs (the corpus table shows how often); the writing view shows results but draws no table of contents entries differently; a bookmark inside a deleted text range is removed with it.

## Left to the builder

The names of the new types and files beyond those given; the exact shape of `FieldView`, `IFieldHost` and the `FieldChange` swap record; the debounce and settle scheduling inside the 250 ms and three-pass bounds; how the entry text is cleaned (line breaks become spaces, hidden text and tracked deletions are left out); the layout of the cards and the bar; the German wording; which Word check files; the number of random oracle steps and the order of the step kinds.

## Rules settled after the builder check (all calls)

A reviewer who had only this document asked seven questions; each is answered here (by the code, by the owner's spec or by a call) so the plan does not guess.

1. **State keyed on the revision.** About 40 uses in 20 files memoize on `DocumentEdits.Revision` (`LayoutEffectiveFormat`, the facts sources, `SectionsSource`, `OutlinePane`, the save and edit stamp checks, `WriteTabRecoverySubject`, the view's change events). A refresh that wrote something raises a second counter, `DocumentEdits.DerivedVersion`, and `DocumentEdits.Stamp` is the pair. Every consumer that reads text, runs or paragraph identity (the outline, the facts, the effective-format index, the view's change event) keys on the pair; the dirty flag, `EditHistory`, undo, the save stamp and the recovery scheduler stay on `Revision` alone (a refresh must not cause a copy or a dirty mark). The first task of the plan is the audit: a table of every use, classified, each class with a test that a refresh after the memo was filled gives the new answer.
2. **Editing in and around a field result.** Today a single-paragraph field result is atomic (the caret steps over it, a partial range is widened to it, Backspace and Delete select it first). That stays for `SEQ`, `REF`, `PAGEREF`, `NOTEREF` and `STYLEREF`. A `TOC` is protected as a whole: typing, Enter, paste, a cut or delete whose range touches an entry but does not cover the whole field, and a split inside it are refused with a reason ("This table of contents updates itself. Change the headings, or remove it."); a range that covers the whole field deletes it in one step (Remove table of contents does the same). A caret may rest between two entries only at the field's edges.
3. **The clipboard.** Today a paste flattens every complex field to its result text and drops bookmarks (`FragmentCleaner`); that stays for `TOC`, `REF`, `PAGEREF` and `NOTEREF` (their bookmarks do not travel). A paste keeps `SEQ` and `STYLEREF` fields that are complete inside the fragment, so a copied caption stays a caption and renumbers; an incomplete field is flattened. A copied `_Toc` or `_Ref` bookmark is never pasted (the refresh makes new ones); a copy within Write of a user bookmark pastes none either (names are unique).
4. **Where a structural refresh is recorded.** The carrier is a property of every history step, not of one kind: a step (typing, style-only, section, object, move, format, field) may hold an ordered list of derived records that undo reverts first and redo replays last; a step that holds one is closed to further coalescing. A structural refresh attaches to the newest step; if the history is empty, or the step that would have been recorded was refused (the history then cleared itself), there is nothing to protect and nothing is attached; if the redo stack is not empty it waits. A refresh is always computed from the state at the moment it runs, so an undo within the 250 ms is harmless (the redo then makes it wait). The first oracle of stage 2 is the history matrix: every step kind, then a structural refresh, then undo and redo in both orders, comparing bytes and block indices.
5. **A clean document with stale results.** Settled by the spec and kept: a refresh never dirties, so Save stays disabled for a clean document and a stale table of contents of a Word file reaches the file with the user's next edit and save (Save as writes the refreshed model at once). A recovery copy of a dirty document holds the refreshed text; a refresh alone writes no copy.
6. **Where the logic lives and how a page is found.** `FieldRefresh` and `FieldView` live in `Raffica.Write.Editing` (Model only) behind `IFieldHost`; the app implements it over `DocumentLayout`, the numbering counter and the note marks. `IFieldHost.PageOf(position)` answers for a paragraph anywhere in a body story; for a paragraph nested in a table or a content control it uses the page of the enclosing top-level block's first line when the layout gives no finer answer (a long table that spans pages can differ from Word; a known limit). The layout's `IProjectionHost.FieldValue(instruction)` stays as it is for `PAGE`, `NUMPAGES`, `SECTIONPAGES` and `SECTION`: `SEQ`, `REF` and `PAGEREF` show the stored result in the layout, and only the model refresh changes it.
7. **Bookmarks in today's editor.** Delete already collects anchors (`EditRules.CollectAnchors`) and re-places them at the nearest remaining position; undo restores snapshots; a paste drops them; a split leaves markers where they are. New work: a bookmark whose whole range lies inside a deleted range is removed with its end marker (user bookmarks, `_Toc` and `_Ref` alike; fields that refer to a missing bookmark keep the stored result and are marked), and the bookmark list, Go to and the creation command.
8. **"Equal to Word".** Word means the Word of the earlier checks (16.0, Microsoft 365, Windows PC, through automation). The gate is judged on purpose-made files (as in 4.4b): every result except page numbers must be equal, and a difference is a bug until a Word behaviour is recorded in `Docs/write-word-check.md` and the engine follows it. Page numbers are compared on those small files, where the pagination is equal; on the corpus thesis (Word 320 or 321 pages, Write 309 or 310) only the entries before the first pagination difference are compared and the rest is recorded with the reason.
9. **Switch spelling.** The instruction is parsed into tokens; field and switch names are case-insensitive, an argument may be quoted or not, switches may come in any order, and an unknown token makes the field "not updated" (the stored text of the instruction is never rewritten by a refresh, only the result).

## Open questions for the owner

None. The builder check raised none that only the owner can answer.
