# Raffica Write – Editing engine (Milestone 4.3a) – Design

Status: draft for the owner's approval · Date: 2026-11-15 · Author: brainstorming session (M4.3)

This document narrows milestone 4.3 of the Write spec (`2026-11-13-raffica-write-design.md`) to its first part, **4.3a**, and records how it is built. Items marked **(call)** are decisions of the author of this document, not of the project owner; the owner approves them together with the document. What the owner decided in this session: the split of 4.3 into 4.3a, 4.3b and 4.3c, a plan for 4.3a only, and the approach "make the product layout engine resumable" (section 5).

## 1. Intent

Write is a viewer today (milestone 4.2). 4.3a makes it an editor of text: a caret, a selection, typing with an input method, delete, clipboard, undo and redo, and the dirty state, Save, Save as and the close prompt, in every story a file contains. The promise that has to survive is the suite's: typing stays under 16 ms on a 300-page thesis, and a DOCX comes back the way it went in.

Why a milestone of its own: the layout library has no way back from a drawn glyph to the model, no hit test, no caret geometry and no incremental re-layout (the product engine is a forward-only iterator; the spike's incremental rule was never ported). Without these there is no editor, and they are most of the work.

## 2. Scope and the split of 4.3

The roadmap's milestone 4.3 becomes three milestones, in this order. Each gets its own plan; the plan for 4.3a is written now, the others when they start.

| Id | Contents | Acceptance (from the roadmap's 4.3) |
|---|---|---|
| **4.3a** | Layout: source map, hit test, caret geometry, resumable engine with incremental re-layout. Editing core. Caret, selection, typing, delete, clipboard, undo and redo in the stories below. IME. Dirty state, Save, Save as, close prompt. | Typing ≤ 16 ms per keystroke in the app on the generated thesis; IME on Windows, Linux and macOS (owner, recorded) |
| **4.3b** | Paragraph and character formatting, styles, the formatting inspector (remove one override, update style to match, clear direct formatting). | (none in the roadmap; tests and a live look) |
| **4.3c** | New document with Write's built-in style set, autosave as a recovery copy every 30 s, restore after a crash. | Recovery after a killed process restores the text (test) |

Prerequisites: 4.3b needs 4.3a; 4.3c needs 4.3b (the built-in styles are the style set 4.3b edits); 4.4 needs 4.3c. The roadmap records the split under "Scope changes".

**Stories in 4.3a** (spec section 6, "Editing what a file already contains"): body, table cells, footnotes and endnotes, headers and footers. **(call)** Comments' text is edited by the editing core (it is a story like the others and has tests), but has no place on screen before the comments pane of 4.6, so the roadmap's "comments" has no user interface in 4.3a. Tools that change structure (inserting a table, a footnote, a field, a picture) stay in 4.4 and 4.5.

## 3. Goals, non-goals, anti-goals

Goals:
1. A keystroke costs ≤ 16 ms in the app on the generated thesis, in the page view and in the writing view, measured as in section 8. **(call) What the 16 ms covers:** from the key event to the frame in which the page(s) in view are correct again (the page with the caret and any page visible with it). Pages further on, and the pages of other views, are brought up to date in the background and never block input; they are not part of the 16 ms. The median and the 95th percentile of every measured case must be within the limit; a keystroke that moves the page count or crosses a section is a case of its own and counts too, because only the visible pages are in the budget. The first edit after a file's full model arrives and the first edit after a view switch are measured and reported, with no limit (the builder warms the engine on idle time).
2. Text of any story can be edited, selected, copied, cut, pasted and undone; what the model does not understand stays in place (the model's vocabulary rule).
3. IME composition works through `TextInputMethodClient` with Write's own preedit caret, and a German layout types every character (AltGr is never a command).
4. Write never loses the user's work silently: a dirty document asks before it closes, and a save is atomic.
5. The speed of 4.2 does not regress: nothing of the editor exists before the first edit or the first caret.

Non-goals (each goes to a later milestone or the backlog): formatting commands and shortcuts such as Ctrl+B (4.3b); new documents and autosave (4.3c); the "edits are not tracked" bar (4.6); selection across stories; a selection across table cells (**call**: a drag that leaves a cell stops at the cell's edge; cell ranges come with the table tools of 4.5); structure changes of tables, headings and sections; right-to-left editing; East Asian layout rules (the IME types Chinese and Japanese, the layout stays as in 4.2); the screen-reader text of the editor (4.8); spelling (4.6); find and replace (4.5).

Anti-goals: a silent change to the user's file; a second copy of the document in memory for undo that doubles the memory of a long document; an editor that blocks the UI thread for a re-layout.

## 4. Constraints

- The layout library stays free of Avalonia; the model references nothing (`ProjectGuardTests`). The editing core is UI-free too.
- The model is a mutable object graph and nothing guards it (`DocumentLayout`: "the document model must not change while a layout runs"; `DocxFile`: "must not change while a save runs"). The editor owns one gate for all three users of the model (layout runner, save, edit), section 6.
- The editing rules of the spec bind: a field result is one unit and its text is never typed into; typing inside a tracked insertion is allowed and is not tracked; deleting text that holds a comment anchor or a bookmark moves the anchor to the nearest remaining position, and a comment goes only through its own command; content controls keep their element and their text is editable.
- Every UI string in English and German, every command with its own Lucide icon, every card through `CardFooter`, new colour tokens classified in the contrast policy (selection, caret, preedit underline use existing tokens where one fits).
- Speed: the first page of 4.2 (≤ 100 ms engine, the app budgets) must not move; `StartupCreatesNoLayoutOrFontWork` style guards extend to the editor.

## 5. Approaches

The question was how the layout becomes editable.

1. **Make the product engine resumable (chosen by the owner).** Build the source map, hit test and caret first; then give the engine page-start records, a per-block plan cache and the spike's refill rule. The spike's check ("the incremental result equals a full layout after seeded edits") becomes the test oracle. Cost: the largest engine change since 4.2; numbering, notes and field state need snapshots.
2. Re-lay out only the edited paragraph and rebuild the rest in the background (debounced full rebuild, about 374 ms on the thesis). Rejected: page breaks, page numbers and fields lag behind typing and a rebuild restarts during every burst of typing; it breaks goal 8 of the spec (fields are current).
3. Edit in the writing view only and keep the page view read-only. Rejected: the spec wants both views and the 16 ms criterion is judged on the thesis in the page view.

## 6. Design

### 6.1 One definition of a paragraph's text (Model)

Layout (`BlockPlanner.Input`/`AddRun`), editing and positions must agree on what text a paragraph has. Today only the layout knows: it drops hidden text and tracked deletions, substitutes field results, inserts a note-mark number, and turns a picture or break into one character, with no way back to the model. **(call)** The model gets one projection (`ParagraphText` or similar; name left to the builder): the paragraph's visible text as a sequence of pieces, each with a reference to its source (the run, the `RunContent` and the character offset inside it) and a kind (text, tab, break, atomic unit). Atomic units are field results, pictures, note references and comment references: the caret steps over them and a delete takes the whole unit. The layout's input is built from this projection, so the flattened text and the source map cannot drift apart. Moving `BlockPlanner.Input` onto it must not change any laid-out page: the corpus page texts, page counts and item counts before and after are the guard.

A position is a paragraph (an object reference inside one story) and an offset into that paragraph's projection, plus the story it belongs to; a selection is two positions in one story (inside a table: one cell). **(call)** Positions are never stored across edits except by the editing core, which rewrites them.

### 6.2 Layout: source map, hit test, caret

- Shaping keeps the HarfBuzz cluster map (today only used inside `ParagraphShaper`), so a glyph run knows which characters each glyph covers. A caret inside a ligature divides it equally (**call**).
- `ParagraphLayout`/`LineBox` already know their lines and runs (`Start/End`, `TextStart/TextEnd`, `Xs`); `LineItems.Emit` flattens them into page items and throws the link away. The page keeps a compact per-paragraph record (block, line tops, line text ranges, per-cluster x) next to its items, for hit testing and caret geometry. `PageItem`s stay as they are, so the renderer and the 4.2 tests do not change.
- API (UI-free, in the layout library): `HitTest(page, x, y)` gives a position (the hyphen glyph and labels such as list numbers are skipped, as the 4.2 notes say); `Caret(position)` gives page, rectangle and the line; visual movement (up, down, line start, line end, page up and down) is computed here because only the layout knows the lines. Reaching into table cells, notes, headers and footers is part of this (today `BlockAt` and `Locate` are block-granular and see nested blocks as their top-level block).
- A hit test is ≤ 50 µs (the spec's limit; the spike measured 0.79 µs).

### 6.3 Layout: a resumable engine

- Each published page records a `PageStart` (block index, line index), as the spike did. The engine keeps a plan per block (the `BlockPlan` memo, today only the last width) and invalidates the plan of an edited block.
- Order-dependent state needs snapshots at page starts or block starts: list numbering (`NumberingCounter`), the note planner, field state, page and section counters, and floats and notes that reserve space per page.
- **Refill rule** (the spike's, with the product's extras): after an edit in block *b*, restart at the page whose start is at or before *b* (walking back over keep-with-next blocks), lay out forward, and stop when a page starts where the old page started (same `PageStart`, same snapshots); splice the old pages back after it and fix up their page numbers. Pages are replaced by array swap as in 4.2 (`PageList.Replace`; readers keep taking no lock; a view never keeps a `LaidOutPage`).
- **When the rule cannot prove itself** (a section break or header change in the range, a page with `NUMPAGES` or `SECTIONPAGES`, a float or note that carries over, a numbering state that changed) the engine restarts at the nearest safe page start and continues in the background as `LayOutRestAsync` does today. It is then slower, never wrong. The number of such restarts is measured and shown by the bench.
- **Oracle:** for every corpus file, seeded edits (inserts and deletes of text, paragraph splits and merges, edits in cells, notes and headers, half of them long enough to move page breaks); after each edit, once the background layout has finished, the incremental result must equal a full layout from scratch (page count, items, text). **(call)** The comparison is exact, and provisional pages ("…" for `NUMPAGES` and `SECTIONPAGES`) are compared after the layout is complete, so they are not an excuse for a difference. Both paths run the same arithmetic from the same state, so equality is expected to be bit for bit; only if that proves impossible may the builder compare positions to 0.01 pt, and says so in the decision. The spike's check was the same idea on one file; here it runs over the whole corpus and is the gate of this part.
- **Fast path and fallback (call):** the stopping rule and the snapshots are required for body text, cells and notes, and the bench reports how often an edit on the thesis falls back. A fallback (section break or header change in the range, a changed page count, a carried float or note, changed numbering state) still shows the edited page at once: the engine lays out from the nearest safe page start up to the pages in view and continues beyond them in the background. A `NUMPAGES` in a footer does not force a fallback by itself, because an edit that leaves the page count alone leaves the field's result alone.
- The writing view (`LayoutMode.Flow`) uses the same engine. **(call)** Only the active view's layout follows an edit; the other is dropped and made again when the view is switched (the 4.2 switch already rebuilds the strips lazily).
- `Engine.Fill` stops being a bare iterator: its locals (`_pageCount`, `_sectionPages`, `_revisable` and the carry state) become state that can be saved and restored at a page start.

### 6.4 The editing core (UI-free)

A new library referencing only the model (name left to the builder; the references are pinned like the others). It holds:
- **Operations** on one story: insert text, delete a range, split and merge paragraphs, backspace and delete by character, by word and by unit, replace the selection. Each returns the new caret and a description of what changed. Fields, atomic units, anchors, tracked insertions, content controls and unknown content follow the rules of section 4; unknown content stays in place.
- **Merging paragraphs (call):** the first paragraph's properties win, except when the first is empty (then the second's). The paragraph mark's own `rPr` travels with the property bag.
- **Atomic unit and Delete (call, our reading of the spec's "after a confirmation of the selection"):** the first Backspace or Delete next to a field result selects it, the second removes it.
- **History (call):** undo and redo are snapshots of the changed block range (the blocks replaced in their list), not per-character operations, so an operation that touches several blocks is one step. Typing is coalesced into one step per word or after a pause (the timing is left to the builder). The snapshot needs a deep copy of blocks in the model (the model has none today). **(call)** `UnknownContent` is shared by reference between a block and its snapshot, never copied, because the editing core never changes it; a step's size counts text and property bags only. **(call)** There is one linear history per document, across all stories; each step records its story and the caret before and after, and undo puts the caret back in that story (scrolling there). The history is bounded by steps and by bytes (limits left to the builder, measured on the thesis), so a long editing session does not double the document's memory; the newest step is always kept, even when it alone is over the byte limit (a select-all delete is one step, and older steps go first).
- **Dirty state (call):** the model gets a revision counter that every operation raises; the document is dirty when the revision differs from the saved one, so undoing back to the saved state clears the dirty state.
- **Clipboard (call):** rich within Write, plain text outside. A copy puts `text/plain` and a private Write format on the clipboard. The private format carries paragraphs and runs with their direct formatting and paragraph properties as WordprocessingML fragments produced by the DOCX codecs (the model cannot serialise itself; the Docx library gets a fragment reader and writer). Pasting into the same document keeps everything it carries; pasting from another document keeps direct formatting and maps a style id only where the target has it, else the target's default. **Fragments that hold a table, picture, note reference or field paste as their text** until 4.5. A paste from outside Write is `text/plain` only (HTML later). Cut is copy plus delete as one history step.

### 6.5 The app

- **Gate (call):** edits run on the UI thread. Before an operation touches the model the editor cancels the layout runner and waits for it to stop (the runner already stops at the end of the current page); after the edit and its refill the runner is restarted for what is still unlaid. A save takes the same gate for its duration. **(call)** Input that arrives during a save (keys and IME commits) is queued in arrival order and replayed when the save ends; no cap other than the save itself, and the save time on the thesis is measured and recorded (the builder cuts it if it is long enough to be felt). A composition in progress is not touched by a save: the preedit is not in the model, so the save may run, and a commit that arrives meanwhile is queued like a key.
- **Read-only until the full model is there:** a file opened as a prefix (4.2's first-page path) cannot be edited or saved. Input before the full model arrives (`FullModelTask`) is refused with a status hint ("Still opening"), not queued.
- **The editor control:** the page view gains the editing behaviour (or a controller it owns), so it stays one visual. It handles key input (text, navigation, delete, shortcuts), pointer selection (click, drag, double click for a word, triple click for a paragraph, Shift+click), the caret (drawn by the view, blinking, kept visible by scrolling), and the selection highlight. AltGr arrives as Ctrl+Alt and is text, never a command (the same rule as the Alt+Q search). `ViewAnchor` gains a real character offset.
- **IME:** the control answers `TextInputMethodClientRequested` with a `TextInputMethodClient` as in the spike (`SupportsPreedit`, `SupportsSurroundingText`, `SurroundingText` from the paragraph around the caret, `CursorRectangle` from the caret geometry, `Selection`, both `SetPreeditText` overloads, `RaiseCursorRectangleChanged` after every change including scroll and zoom). **(call)** The preedit is not written to the model: it is a transient piece in the projection of the paragraph being edited, laid out with an underline and discarded on commit or cancel, so a composition never marks the document dirty and never enters the history. Write draws the preedit caret itself because the reported preedit cursor differs by system. Keys belong to the IME while a preedit exists. On commit the text goes through the same insert operation as typing. The candidate window follows the caret in a page of text (the spike could not judge this in a small field).
- **Save, Save as, close:** Save uses `DocxFile.SaveAsync`, with the changed-original answer as a card (overwrite, Save as, Cancel; the pattern of Raffica PDF's changed-original card). Save as uses the system picker and exposes the Windows `.pw-*.old` leftover path before the save, and guards against a file that appears at the target between the picker and the write (limit (a) of the 4.1 decision). A dirty document asks on window close, tab close, quit (⌘Q) and the update flow's questions through `DialogQueue` and `WindowHost`, with the same three answers as the PDF app's unsaved card (Save, Don't save, Cancel). `WriteWindow.OnWindowClosing` stops confirming at once. The tab shows a dirty mark.
- **Commands:** Write's catalog gets `file.save`, `file.saveAs`, `edit.undo`, `edit.redo`, `edit.cut`, `edit.copy`, `edit.paste`, `edit.selectAll`, each with an icon, a reason in `WriteCommandRules` for every state in which it is disabled, a ribbon place within the frame's rules (Home tab: clipboard and undo groups) and the macOS menu (`WriteMacMenu`: Undo, Cut, Copy, Paste, Select All in Edit). Keys follow the platform: Ctrl on Windows and Linux, ⌘ on macOS, Option+arrow for words and ⌘+arrow for lines on macOS; every key without a command goes into the help list; macOS keys are checked against `ReservedShortcuts`.
- **Editing in headers, footers and notes** happens on the page where they are drawn. A header or footer part is shared by many pages: an edit changes the part and re-lays out the pages that show it (the engine's `HeaderFooterPlan` already reuses its result per part; the plan is invalidated for that part). Notes are edited where the page shows them; the note's block is invalidated and the pages from the reference onward are refilled.
- **No work before first use:** the editing core, the clipboard and the IME client are made on the first caret or the first key, not at startup.

## 7. Work outside the milestone that it must carry

From the follow-ups of 4.2: nothing is forced into 4.3a except what editing needs. **(call)** The `ImageCache` budget, pixel cap and off-thread decode move to 4.3c (a pasted or inserted picture is not possible before 4.5, so the hostile-image exposure does not grow here) or 4.8 at the latest; the crash guard around `PageRenderer.Draw` stays "before 4.9". The `xml:space="default"` written back as `preserve` (limit (b) of 4.1) stays a recorded limit (typing writes `preserve` where text has edge spaces, which is what Word expects).

## 8. Measuring

- **Engine:** `write-layout-bench` gets an `edit` case: seeded keystrokes in the middle of the thesis, in a paragraph whose change moves a page break, and at the end of a chapter; median per keystroke (the spike measured 0.26 ms) and the hit test per call (limit 50 µs). It runs on a Native AOT publish, as `run` does, and reports how often the engine had to restart further back (section 6.3).
- **App:** the Write app gets a measurement switch (`--measure-typing`, with `--settings-file` and a file, as the other protocols) that injects characters through the same input path as the keyboard on the thesis (middle of a long chapter, a page-moving paragraph, a cell, a footnote, a header) and measures from the key event to the frame that shows the edited page. The limit is 16 ms per keystroke (spec section 5). `measure.cs --target write` gets a `typing` case and `s_writeBudgets` a row for it; the Windows PC is binding first, the Linux laptop and the Intel Mac as in 4.2.
- Re-measured in 4.8 with the outline pane open and with a file that has tracked changes (spec section 5).

## 9. Testing

- Editing core: unit tests per operation and per rule of section 4 (fields, atomic units, anchors, content controls, unknown content, tracked insertions), history (coalescing, redo clearing, bounds, dirty after undo) and the clipboard fragment round trip.
- Layout: the corpus oracle of section 6.3; seeded hit-test and caret round trips (1,000 as in the spike); "laid-out pages unchanged by the projection refactor" on all corpus files; the projection's source map checked against the old flattened text.
- App (headless): key input and navigation, selection by pointer, caret kept visible, IME through a test client that calls `SetPreeditText` and commits, AltGr text, read-only before the full model, save, save over a changed original, Save as, the close and quit questions with every answer, the update flow's question.
- A project guard test for the new library's references; the Native AOT publish of the app, the bench and the corpus tool with the log search for `IL[23][0-9]{3}`; a startup test that nothing of the editor exists before first use; the German strings (`TranslationTests`, register tests).
- Saved files: an edited thesis saved, read again and laid out equals the edited model's layout; the corpus files still open in the round-trip test after edits.
- **Owner, recorded (cannot be tested in CI):** the IME check on Windows (Japanese and Pinyin), Linux (Wayland with Fcitx5; **IBus and an X11 session**, not tested in the spike) and macOS (Japanese, Chinese; **the commit must appear in Write's own log**, the spike's log had none): the candidate window stands at the caret in a page of text, a German keyboard layout types `@`, `z` and `y` correctly. The typing budget on the Linux laptop and the Intel Mac. A look at the AOT build.

## 10. Left to the builder

Names of the new project and types; the internal shape of the projection and of the per-page paragraph record; the snapshot and restore mechanics of the engine state; the history's limits and coalescing timing; caret blink and selection visuals and which tokens they use; the exact key map within the rules above; the ribbon layout; the pointer rules at table edges; how the typing measurement injects input.

## 11. Open

- **(call) How 4.3a ends:** as with 4.2, the code and the tests close the milestone's tasks, the roadmap ticks an acceptance criterion only after it was checked (CLAUDE.md), so 4.3a stays `in-progress` until the owner's IME checks and the typing budgets on the Linux laptop and the Intel Mac are recorded. The pull request to `dev` merges with those gates listed as open; 4.3b may start meanwhile.
- The owner's IME checks of section 9 and the typing budgets on the Linux laptop and the Intel Mac.
- 4.2's owner gates stay open and are independent of this plan (milestone 4.2 is still `in-progress`).
