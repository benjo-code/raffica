# Raffica Write – Design

Status: approved by the owner on 2026-11-13; corrected the same day after the final review of milestone 4 (the 100 ms first page is the headless engine measure, as in the spike; Word's thesis page count marked open; macOS and AltGr shortcut rules; IBus, macOS printer and review references added to the acceptance) · Date: 2026-11-13 · Author: brainstorming session (M4)

Items marked **(call)** are decisions made by the author of this document, not by the project owner; they are part of the document the owner approved on 2026-11-13 as handed over. Everything else was decided by the owner (in this session or in the spike design `2026-11-12-raffica-write-spikes-design.md`, section 2) or is settled in an existing document that this one names.

## 1. Intent

Raffica Write is the word processor of the suite and the owner's Word alternative. Its first documents are theses and long reports. Its promise is the suite's: fast, tidy, and a DOCX that comes back the way it went in.

The pattern behind every rule below (owner, 2026-11-12): **Word hides its structure, so simple wishes become recipes.** Write lets the user say what they want, builds the structure itself and shows it. The owner's own example: page numbers that start at "1" on page 3 take five steps in Word (section break, unlink the footer, format the numbers, start at 1, delete the numbers before); in Write it is one action.

What the research of 2026-11-12 and 2026-11-13 found in other word processors (forums, reviews, vendor documentation; patterns, not measurements):
- **Word:** the Navigation pane (drag a heading to move a chapter) and the thesis tools (captions, cross-references, tables of contents) are what people rely on. The complaints: styles are confusing (document, template and "automatically update" fight each other), long documents lag while typing, fields need Ctrl+A, F9 to be current, AutoSave works only on OneDrive, Copilot cannot be removed, and tracked formatting changes ("Formatted: Bold") bury the real edits.
- **LibreOffice Writer:** slow on large and image-heavy files; it converts DOCX into its own model, so styles, headers and footers shift; a crowded UI.
- **ONLYOFFICE:** good DOCX fidelity because it works on OOXML natively; blurry text and a sluggish desktop app (a web editor inside).
- **Google Docs:** you start typing at once, and the pageless view is calm; little layout control, middling DOCX fidelity.
- **Pages:** tidy, but its DOCX export breaks layouts.
- **WordPerfect:** Reveal Codes shows every formatting change in the text and lets you delete each one; lawyers still miss it. Word's Reveal Formatting only shows.

## 2. Goals

The seven points the owner agreed to on 2026-11-12, then what this session added.

1. **DOCX is the native model.** The document model follows OOXML. Parts Write does not understand are written back unchanged (spike 1: untouched parts stay byte-identical when never loaded).
2. **Long documents are the measured promise.** A 300-page document opens fast and typing stays smooth, also with the outline pane open and with tracked changes in the file.
3. **Formatting is visible and editable.** Write answers "why does this paragraph look like that?" with the style and every direct override, and each override can be removed on its own or taken into the style. There is no "automatically update this style".
4. **Numbering and headings do not break.**
5. **Two views, one document:** a calm writing view without page breaks and an exact page view for printing.
6. **Metric-compatible fonts ship with Write** (Carlito for Calibri, Caladea for Cambria), so a DOCX breaks lines the same on all three systems.
7. **No AI clutter** (anti-goal of the suite spec).
8. **Fields are always current.** Table of contents, list of figures and tables, caption numbers and cross-references never need a manual update. They are current in the page view, at the latest when the layout settles, and always before save, print and PDF export.
9. **The outline pane moves chapters.** Dragging a heading moves the heading with everything under it, on a 300-page document without lag.
10. **Local autosave and recovery**, on by default, no account and no cloud (suite anti-goal 6).
11. **Review without the noise.** Comments and tracked changes from Word files are shown, and changes are accepted or rejected one by one or all at once. Text changes come first; formatting changes are a separate filter.
12. **Spelling in English and German, offline.**
13. **Write prints itself** (owner): its own Print command, Windows draws onto the printer, Linux and macOS hand CUPS a PDF in memory (decision "Raffica Write: printing per operating system (M4)").
14. **"Good enough for a thesis" is the first release** (owner, 2026-11-13): everything in section 7 up to milestone 4.9.

## 3. Non-goals and anti-goals

Not in the first release (owner agreed 2026-11-13; each goes to the roadmap backlog):
- Recording the user's own tracked changes. Write shows, accepts and rejects them; recording comes later. **(call)** When a document has change tracking switched on (`w:trackRevisions`), Write says once per document, in a bar above the page, that its own edits are not tracked, so nobody believes they are.
- An equation editor. Equations (OMML) are kept and shown, not edited.
- Mail merge, drawing shapes, SmartArt, charts. They are kept and shown (as the picture the file carries, where it carries one); they are not edited.
- A grammar check (LanguageTool needs Java).
- **Right-to-left scripts** (Arabic, Hebrew) and bidirectional text (owner, 2026-11-13). Latin scripts are fully supported. Chinese and Japanese are supported simply: shaped and broken with the system's fonts, typed through the IME, without East Asian layout rules such as vertical text, ruby or line-start prohibitions beyond UAX #14. **(call)** A document with right-to-left text opens and saves without loss, but is shown left to right with a note.
- The native macOS print panel (backlog, as before).
- ODF, the legacy `.doc` format, macros, real-time collaboration, cloud (suite spec).
- **(call)** `.docm` and `.dotx`/`.dotm`: a `.docm` opens with its macros kept and never run; saving keeps the macro part. Templates are a later milestone.

Anti-goals (suite spec, applied to Write):
- Any AI feature, ad, nag screen or account.
- A second UI mode next to the one structure.
- A silent change to the user's file: Write never "repairs" a DOCX on open without saying so, and a save writes back every part it did not understand.
- A compatibility claim that is not measured against the corpus (section 6).

## 4. Constraints

- .NET 10 with Native AOT, Avalonia 12, the shared shell and frame. The ink colour is `Raffica.Color.Ink.Write` (already in `Tokens.axaml`).
- **Decisions of milestone 4 that bind this spec** (`Docs/DECISIONS.md`): the Open XML SDK 3.x works under Native AOT; the layout engine is C# (HarfBuzzSharp shaping, UAX #14 line breaks, SkiaSharp drawing) with the incremental re-layout rule of the spike; printing per operating system as in goal 13; the editor is a custom control that implements `TextInputMethodClient` and draws the preedit caret itself.
- **The document model and the layout live in a library with no UI dependency** (suite spec). It references no Avalonia type; the app draws what the library lays out.
- **Spike code never becomes product code.** What is kept from `spikes/write` is rewritten under these milestones with tests.
- **Shared code moves, it is not copied (call).** Where Write needs something that today lives in `Raffica.Pdf` (the platform print services, `WindowHost`, the crash guard, the update flow, the default-app services; `SingleInstance` is already in `Raffica.Core`), the milestone that first needs it moves it into a shared project and Raffica PDF uses it from there. The PDF tests must stay green after each move.
- Dependency and licence guardrails of the suite spec apply. Known so far: Hunspell is MPL 1.1, GPL 2.0 or LGPL 2.1 (tri-licence); the German dictionary igerman98 is GPL-2+ (with an OASIS alternative); both fit GPL-3.0-or-later. The .NET Hunspell port the builder picks gets the usual licence check.
- Every UI string in English and German ("Sie" form), every command with its own Lucide icon, every card through the shell's card footer (the PDF app's rules in `CLAUDE.md`).
- Windows, Linux and macOS from the first Write release (macOS is supported since milestone 3).

## 5. Speed

The engine limits come from the spike and stay (owner, 2026-11-12): **first page ≤ 100 ms on the Linux laptop** (the HP ProBook of the M1 decision), **a keystroke ≤ 16 ms**, **full layout of the generated thesis ≤ 3 s**, **a hit test ≤ 50 µs**. They are measured as the spike measured them: headless, from a Native AOT publish of a benchmark over the UI-free library (process start, open the DOCX, lay out page 1 and draw it into an offscreen surface), not through the app window. The spike measured 108.9 ms for the first page; the miss is startup work (process start, loading eight font files, reading the package), not the engine. Shortening it is a task of milestone 4.2, and the limit is not loosened. The app's own time to the first sharp page in a window is a different number (the PDF app's cold start on the same laptop is above 200 ms) and gets an app budget below.

The app budgets are set like the PDF ones (`Docs/DECISIONS.md`, "Raffica PDF measurement method (M2.1)"): **measure, then set.** Milestone 4.2 measures cold and warm start with a file, a further file in a running instance, the generated thesis to its first sharp page in the window, scrolling it, memory with it open and the published size; the budget is the median, the limit the budget × 1.25; `measure.yml` gets a `write` target and `CheckCommand` a Write budget table. The Windows PC and the Linux laptop are binding, the owner's Intel Mac as in M3.3. A miss is a finding for the owner, never a reason to loosen a limit.

Typing (≤ 16 ms per keystroke, measured in the app, not only in the engine) is re-measured in 4.3 and again in 4.8 with the outline pane open and with a file that holds tracked changes.

Heavy work (spell checking the whole document, full layout of pages not yet seen, field updates that need page numbers) starts after the first page is on screen and never blocks typing.

## 6. Behaviour rules

**Opening and saving.**
- Write opens `.docx` (and `.docm`, see section 3). It reads only what the first page needs before showing it; the rest is read and laid out in the background.
- Saving is atomic (write next to the file, check, replace), as in Raffica PDF. Parts Write did not load are copied unchanged: their decompressed bytes stay identical (the zip compression of an entry may differ). Saving through a symbolic link writes the target (decision "Raffica: saving through a symbolic link (M2.7)").
- Something Write cannot lay out (an unknown element inside a paragraph) is kept in the model and shown as a small marked placeholder, never dropped.
- **Autosave (call):** every 30 s while there are unsaved changes, and when the window loses focus, Write writes a recovery copy into its own data folder, never over the user's file. After a crash or power loss the next start offers the recovery copy (Restore, Discard). Writing the user's file happens only on Save. The shell's autosave setting is shown in Write and switches the recovery copy.
- External relationships (linked pictures, attached templates, `INCLUDETEXT` and similar fields) are never fetched. Links open only after a click and a confirmation that shows the address, as in Raffica PDF. Embedded objects (OLE) show their stored picture and are never activated.

**New documents (call).**
- "New document" exists from 4.3. A blank document carries Write's built-in style set: Normal, Heading 1 to 9, Title, Caption, TOC 1 to 9, Footnote Text, List Paragraph, Hyperlink, plus one multi-level numbering definition for headings and the bullet and number lists. Body text Carlito 11 pt, headings Caladea, A4 with 2.5 cm margins; US Letter with 1 inch margins when the system region is the United States or Canada. The look of the style set is designed once with the shell's design direction in 4.3; templates come later.
- A document that was never saved is recovered like any other (the recovery copy does not need a file name).

**Editing what a file already contains (call).** From 4.3 the editor covers every story of a document: the body, table cells, footnotes and endnotes, headers and footers, comments' text. Before their own milestone (4.4 to 4.6) these elements can be edited as text, but their tools (inserting a table, a footnote, a field) are not there yet. Further rules:
- A field result (table of contents, page number, cross-reference) is one unit: the caret steps over it, Delete removes the whole field after a confirmation of the selection, and its text is never typed into.
- Typing inside a tracked insertion or deletion is allowed and is not tracked (the bar of section 3 says so).
- Deleting text that holds a comment anchor or a bookmark moves the anchor to the nearest remaining position; a comment is deleted only by its own command.
- Content controls (structured document tags) keep their element; their text is editable, their properties are not edited in the first release.

**Styles and the formatting inspector.**
- A pane shows, for the paragraph or selection at the caret, the paragraph style, the character style, and every direct override as its own row (for example "Bold", "Space after 12 pt"). Each row can be removed. "Update style to match" takes the overrides into the style; "Clear direct formatting" removes them all.
- Styles are the document's. Write does not offer "automatically update" and never changes a style because the user formatted a paragraph.

**Structure.**
- Headings and lists use the document's numbering definitions; renumbering follows the model, so moving or deleting a heading never leaves stale numbers.
- **"Start page numbering here at 1"** (and with another number or format, such as "A-1") is one command. Write creates the section break and the unlinked header or footer, and puts a marker at the section start that can be removed (which merges the section back). The DOCX holds what Word expects (`w:pgNumType w:start`).
- The outline pane lists the headings; dragging one moves the heading with its content, keyboard included (move up and down, promote, demote).

**Long-document tools.**
- Captions (figure, table, equation, with an optional chapter number), cross-references (to a heading, caption, footnote or bookmark: number, text or page), table of contents, list of figures and tables. All are DOCX fields that Word reads, and all are kept current (goal 8).
- **Field results in the file (call):** Write computes field results from its own layout and writes them as the cached results Word shows; it sets neither `w:dirty` nor `updateFields` (Word would ask on every open). Where Write's pagination differs from Word's, the page numbers in a table of contents can differ; the corpus table shows how often. Opening a file and refreshing field results on screen does not make the document dirty; the refreshed values reach the file with the next save the user makes.
- Footnotes and endnotes, tables (insert, rows and columns, merge, borders, header row repeated on each page), pictures (inline; floating pictures are kept, shown at their position and movable), hyperlinks, bookmarks, find and replace (match case, whole words; regular expressions later).
- **Keys (call):** in Write, Ctrl+K inserts a hyperlink, as in Word, Google Docs and Pages (the backlog item "Ctrl+K and hyperlinks in Write"), and the frame's command search moves to Alt+Q in Write, as Word's search box. The frame gets an app-level option for the search shortcut; Raffica PDF keeps Ctrl+K. On Windows and Linux, AltGr+Q (which arrives as Ctrl+Alt+Q and types "@" on a German layout) must never open the search. On macOS Option+letter types characters (Option+Q gives "œ" or "«"), so Write's command search there gets a ⌘ shortcut through `CatalogCommand.ShortcutFor(macOs)`, chosen in 4.5 against the `ReservedShortcuts` audit; ⌘K inserts a hyperlink there, as in Word and Pages for Mac.

**Review.**
- Comments: shown in a comments pane beside the page, with the anchor marked in the text; add, reply, delete and resolve. Resolved state comes from and goes back to the file (`commentsExtended`).
- Tracked changes from a file: shown in the text (insertions and deletions in the author's colour) and listed in a pane; accept or reject one, all, or all of one author; the list filters out formatting changes by default.

**Proofing.**
- Hunspell with English (US and UK) and German dictionaries shipped. The language comes from the text's language attribute (`w:lang`), the document's default and then the UI language. A misspelling is underlined; the context menu offers suggestions, "Ignore" and "Add to dictionary" (a user dictionary in the settings folder).

**Views.**
- Page view: pages as printed. Writing view: the same document without page breaks, headers and footers, at a comfortable line length; footnotes appear where they are referenced. Switching keeps the caret and the scroll position on the same paragraph.

**Input.**
- The editor draws its own preedit caret. The first editor milestone (4.3) checks that the candidate window follows the caret in a page of text and tests with a German keyboard layout (decision "Raffica Write: IME in a custom text control (M4)").

**Printing and PDF.**
- Windows: route a of the print decision (GDI, `ExtTextOutW` with glyph indices), with the Win32 print dialog as Raffica PDF uses it. Linux and macOS: a PDF in memory to CUPS, with the print dialog built on the shell's controls that Raffica PDF uses on Linux, shared with it **(call)**. Copies, page range, duplex and orientation are mapped (the spike mapped none). The no-break space after a heading number prints as a glyph with a `ToUnicode` entry or as an ordinary space (spike finding).
- PDF export: the same PDF that goes to CUPS (Skia), with real text and embedded font subsets. Tagged PDF and PDF/A are later.

**Layout features of the first release (call).** The layout honours: line spacing (single, multiple, at least, exact), space before and after with the contextual-spacing rule, indents and hanging indents, tab stops (left, centre, right, decimal, leaders), justification, automatic hyphenation when the document asks for it (`w:autoHyphenation`, Hunspell hyphenation patterns), widow and orphan control, keep with next, keep lines together, page break before, sections with their own page size, margins, orientation and columns, headers and footers (first page and odd and even pages), footnotes, tables with fixed and auto-fit widths, merged cells and repeated header rows, inline pictures, and floating pictures and text boxes with square and top-and-bottom wrapping. Tight and through wrapping are drawn as square wrapping. Fonts that are neither shipped nor installed fall back to a metric-compatible shipped font where one exists and otherwise to a shipped default, and the document's font name is kept.

**Compatibility corpus.**
- A folder of DOCX files and for each the PDF that Word made of it (the owner makes these on the Windows PC); for files with tracked changes also Word's PDFs after "Accept all" and after "Reject all". A tool compares Write's layout with Word's: page count and the text of each page (through PDFium). The result is a table in the repository; "compatible" is claimed only for what the table shows.
- **Sources (owner, 2026-11-13: a mix):** files of our own generator (the spike's thesis first) and freely licensed public DOCX files (each with its source and a licence check) are committed; the owner also checks their own real documents locally, and only the result (the table row, never the file or its text) is recorded.
- **What a difference does (owner, 2026-11-13):** a difference is recorded in the table and is worked down over time; it does not fail a milestone. The one exception: the generated thesis must have the page count of Word's PDF of it, from 4.2 on (the spike layout counts 311 pages; Word's count is not known yet, see section 9).
- The corpus starts in 4.1 and grows in every milestone with files that exercise that milestone's features.

## 7. Milestones

The roadmap gets these milestones under subproject 5 (ids 4.1 to 4.9). Each gets its own plan. A plan may split a milestone further; the roadmap then gets the extra ids.

| # | Milestone | Contents | Acceptance (verifiable) |
|---|---|---|---|
| 4.1 | Document model and DOCX round trip | The UI-free library: model (paragraphs, runs, styles, numbering, sections, tables, footnotes, fields, comments, tracked changes, pictures, unknown content kept), DOCX read and write through the Open XML SDK under Native AOT, the compatibility corpus with its comparison tool. | Every corpus file opens and saves; parts not loaded are byte-identical (decompressed) after a save; a model change saved and read again gives the same model (tests); the AOT publish has no IL warnings in the library. |
| 4.2 | Write as a fast DOCX viewer | The Write app on the frame (start view, tabs, recent files, single instance, settings), with `WindowHost` and what else a second app needs moved out of `Raffica.Pdf`; the layout library, page view and writing view, scrolling, zoom, outline pane (navigation only), the shipped fonts, the first-page startup work; the app speed budgets are set here. | The engine's first page of the generated thesis ≤ 100 ms on the Linux laptop (headless benchmark, section 5); the app budgets recorded in `Docs/DECISIONS.md` and checked by `measure.yml`; the corpus comparison table for page count and page text exists, and the generated thesis has the page count of Word's PDF. |
| 4.3 | Editing | Caret, selection, typing, delete, clipboard (rich within Write, plain text outside **(call)**), undo and redo, IME, paragraph and character formatting, styles, the formatting inspector, save, Save as, dirty state and close prompt, autosave with recovery. | Typing ≤ 16 ms per keystroke in the app on the generated thesis; IME check on the three systems including the candidate window position, a German layout, a commit seen in the log on macOS, and IBus and an X11 session on Linux (owner, recorded); recovery after a killed process restores the text (test). |
| 4.4 | Structure | Headings, lists and numbering, outline pane with drag and keys, sections, "start page numbering here", headers and footers, page setup (size, margins, orientation, columns). | Moving and deleting headings never leaves stale numbers (tests); the page-3 "1" and the "A-1" appendix of the test document made through the one command open in Word as intended (owner, recorded). |
| 4.5 | Long-document tools | Tables, pictures, footnotes and endnotes, captions, cross-references, table of contents and lists, hyperlinks (Ctrl+K), bookmarks, find and replace. | Fields current after every edit that moves them (tests); a thesis made in Write from scratch opens in Word with working fields (owner, recorded). |
| 4.6 | Review and proofing | Spelling (English and German), comments, tracked changes (show, accept, reject, filter), the "edits are not tracked" bar. | "Accept all" and "Reject all" give the same text as Word's PDFs of them on the corpus files with tracked changes (corpus tool); spelling marks and suggestions in both languages (tests). |
| 4.7 | Print and PDF | Shared print services moved out of `Raffica.Pdf`, Windows GDI printing, CUPS printing, the print dialogs, PDF export. | Real printers on Windows, Linux and the Intel Mac print the test document's ranges with the right page numbers (owner, recorded); the exported PDF has text on every page (PDFium test). |
| 4.8 | Release readiness and security check | Owner's requirement (2026-11-13). Security: malformed and hostile DOCX (oversized parts, zip bombs, deep nesting, broken relationships) are refused or opened safely, fuzzed in CI; no external fetch, no macro run, no OLE activation (tests); dependency licence and vulnerability check. Readiness: live UI pass on the Native AOT build on all three systems, German wording, crash handling, accessibility of the editor (screen reader names the caret's text), the app budgets with a control run of the same day, the release dry run. | Fuzzing finds no crash in its run time; every security rule above has a test; the live pass findings are fixed or recorded for the owner; `measure.yml` within the budgets or the miss recorded; dry run green. |
| 4.9 | Release | Packaging (Write in the installers, the default-app offer for `.docx`, update path), CHANGELOG, README, the release, on the owner's instruction only. | The release run is green and the published files are the checked ones; an installed build opens a `.docx` from the file manager on each system. |

Whatever needs the owner's machines (real printers, real desktops, the Mac, Word for the comparison PDFs) is listed as open in the roadmap with the command that checks it, as in the earlier subprojects.

## 8. Left to the builder

- Project, assembly and executable names (consistent with `RafficaPdf`), the split of the UI-free library into projects, and the name of the shared project that takes over the moved PDF code.
- The ribbon's tabs, groups and field codes within the frame's rules.
- The internal model shape, as long as unknown content survives and the model follows OOXML.
- Which .NET Hunspell implementation, after its licence check.
- The fuzzing tool and its run time in CI.
- Whether more OFL metric fonts (for example Liberation for Arial, Times New Roman and Courier New) ship, weighed against the size budget of 4.2.

## 9. Calls and open questions

Calls of this document (marked above): the "edits are not tracked" bar; `.docm` opens with macros kept and never run; shared code moves into shared projects; the autosave design (a recovery copy every 30 s, the user's file only on Save); Ctrl+K for links and Alt+Q for command search in Write; the print dialogs as in Raffica PDF; rich clipboard only inside Write in the first release; the blank document and its style set; editing of existing tables, notes, headers, fields and tracked ranges from 4.3; field results written from Write's layout without `updateFields`; the list of layout features of the first release; right-to-left text kept but shown left to right with a note.

Open:
- The release number and whether Write ships in the same installer as Raffica PDF (the suite spec says one installer for the suite) or in its own: decided in 4.9 with the owner.
- The owner's Intel Mac: real printer and layout run from the spikes are still open (`spikes/write/print/README.md`, `spikes/write/layout/README.md`).
- Word's page count of the generated thesis and whether Word shows "1" on physical page 3 and "A-1" on the first appendix page were not reported (owner, Word on Windows; `spikes/write/testdoc/README.md`). The 4.2 gate needs the Word PDF of the thesis, which the corpus of 4.1 asks for anyway.
- The macOS IME log of the spike has no commit line (decision "Raffica Write: IME in a custom text control (M4)"); 4.3 has to see one.
