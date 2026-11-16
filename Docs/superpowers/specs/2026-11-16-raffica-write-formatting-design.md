# Raffica Write: formatting and styles (milestone 4.3.2, "4.3b") - design

Status: draft after the builder check, awaiting the owner's approval (2026-11-16). Items marked **(call)** are decisions of the author of this document, not of the project owner; the owner approves them together with the document.

This document narrows 4.3b of the Write spec (`2026-11-13-raffica-write-design.md`, "Styles and the formatting inspector") and builds on the editing engine of 4.3a (`2026-11-15-raffica-write-editing-engine-design.md`, decision "Raffica Write: the editing engine (M4.3a)").

## 1. Intent

A person can format text in Write and can always answer "why does this paragraph look like that?". The pane names the paragraph style, the character style and every direct override as its own row; each row can be removed on its own, and the overrides can be taken into the paragraph's style or cleared. This is goal 3 of the Write spec: Word's styles are confusing because document, template and "automatically update" fight each other; Write has one rule, **the style is the document's and changes only when the user says so.**

Acceptance (roadmap): every override row of the inspector removes exactly that override and nothing else (tests).

## 2. Goals, non-goals, anti-goals

**Goals**
- Direct character and paragraph formatting of the selection, in every story that 4.3a edits (body, cells, notes, headers and footers, content-control content).
- Apply a paragraph style or a character style; the pickers list the document's own styles.
- The formatting inspector pane with removable override rows, "Update style to match" and "Clear direct formatting".
- Undo and redo for all of it, each action one step.
- The layout shows the result at once, with the 4.3a rule "incremental equals full".

**Non-goals (not in 4.3b)**
- Creating, renaming, deleting or restyling styles in a dialog, "new style from selection", style sets, themes (owner, 2026-11-16: apply and update only; creating styles arrives with the built-in set in 4.3c, a style manager is a backlog item).
- Lists and numbering (4.4), tab stops and a ruler, borders and shading of paragraphs, table formatting (4.5), page setup (4.4), the format painter, find and replace of formatting.
- Recording formatting as tracked changes (the spec's bar says own edits are not tracked, 4.6). Existing `rPrChange` and `pPrChange` children are kept untouched and are not shown as rows.
- Editing document defaults (`w:docDefaults`) and table styles. Table styles are applied by the table tools of 4.5.

**Anti-goals**
- "Automatically update this style" in any form. A direct format never changes a style.
- A silent change of the user's file: formatting only touches the properties the user asked for.
- A second undo system or a second place where the document changes (every change goes through the lease and `EditSession.Apply`).

## 3. What can be formatted (call)

The set is what the layout honours today (so every change is visible); a property the layout does not read is not offered.

| | Properties (OOXML) | Controls |
|---|---|---|
| Character | `b`, `i`, `u` (single; the other kinds are kept and shown), `strike`, `vertAlign` (superscript, subscript), `rFonts` (font family), `sz` (size), `color`, `highlight`, `caps`, `smallCaps` | bold, italic, underline, strikethrough, superscript, subscript, font, size (with larger and smaller), text colour, highlight, small caps and all caps |
| Paragraph | `jc`, `ind` (left, right, first line, hanging), `spacing` (before, after, line with rule), `contextualSpacing`, `keepNext`, `keepLines`, `pageBreakBefore`, `widowControl` | align left, centre, right, justify; indent more and less; spacing before and after; line spacing (single, 1.15, 1.5, double, exact, at least); the four pagination switches |

Other children of a direct `w:pPr` or `w:rPr` (for example `tabs`, `lang`, `vanish`, `bdr`, `shd`) have **no** control but are listed as rows and can be removed.

## 4. Behaviour rules

**Applying character formatting.** The range is the selection. Runs are split at the edges (`InlineSplitter`), the property is set on the covered runs, and afterwards neighbouring runs with the same properties and plain text content are merged again so history and file do not fragment. **(call)** Runs are merged only when their properties, their attributes (ignoring `w:rsid*`) and their kept children are equal; a run that holds anything but text, tabs and breaks is never merged. Merging touches only runs this operation touched.

**Toggles.** Bold, italic, underline, strikethrough, small and all caps, superscript and subscript act on the *effective* value of the whole selection: if all of it is on, the command turns it off; otherwise it turns it on (so a mixed selection becomes all on, as in Word). Turning off a value that comes from a style writes an explicit off (`w:val="0"`); turning off a value that is only a direct override removes the override, so no redundant element stays. Superscript and subscript exclude each other.

**Value commands** (font, size, colour, highlight, alignment, indents, spacing) set the value on every covered run or paragraph. Choosing the value the style already gives still writes the override **(call)** (it is what the user asked for and the row shows it); only the toggle-off rule above removes elements.

**Caret without a selection (call).** Character commands set a *pending format* that the next typed text takes; it is cleared by a caret move, an edit elsewhere or Esc. It lives in the session only: it never enters the model, the history or the revision, like the IME preedit. In an empty paragraph the command also sets the paragraph mark's run properties, because the layout takes the line height of an empty paragraph from them. Paragraph commands act on the paragraph at the caret, or on every paragraph the selection touches.

**Paragraph and character styles.**
- Apply paragraph style: sets `w:pStyle` on every paragraph the selection touches. The direct overrides stay (Word's behaviour is also to keep them); "Clear direct formatting" is the explicit way to drop them.
- Apply character style: sets `w:rStyle` on the selection's runs; choosing "(none)" removes it.
- The pickers list the styles of the document with `qFormat` or in use, without `semiHidden`/`hidden`, in the document's `uiPriority` then name order **(call)**; the default style of each kind is first. A style id the file does not define is shown as its id, never dropped.

**Formatting inspector** (a right pane, ribbon command Formatting on the View tab; it reads the caret's paragraph, or the first paragraph of the selection, and the character formatting of the selection).
- Lines: paragraph style, character style (or "mixed" / "none"). Rows: every direct child of the paragraph's `pPr` and of the runs' `rPr` except `pStyle`, `rStyle`, `numPr` (the list line of 4.4), `sectPr`, the paragraph mark's `rPr`, `rPrChange`, `pPrChange`. A row says what it is and its value in the user's words ("Bold", "Space after 12 pt", "Font Carlito"); an element Write has no wording for is a row named by the element, so *every* override has a row. For a selection, a character override is shown when it is on the whole selection, and as "partly" when only on some runs; removing a "partly" row removes it from all runs.
- **Selection over several paragraphs (call).** Paragraph rows and the two style lines work like the character rows: a row shows when the override is on *every* touched paragraph with the same value, and as "partly" (or "mixed" for differing values) otherwise; removing a row removes it from all touched paragraphs. "Update style to match" acts on the first paragraph of the selection and is disabled, with a reason, when the selection touches paragraphs of different styles.
- Remove row: removes exactly that element from exactly the paragraph(s) or run(s) it was read from. Nothing else changes: not the other rows, not the style, and **runs are never merged by a removal (call)**; the merge rule above belongs to Set, Toggle and Apply style only. So after Remove row the document equals the document before minus that one element, byte for byte in the model.
- **Paragraph mark (call).** The mark's `rPr` has rows ("Paragraph mark: Bold") only while the paragraph has no text, because only then it changes how the paragraph looks (line height); Clear direct formatting removes them in that case. In a paragraph with text the mark's `rPr` is left alone.
- **One row, one element (call).** A font row is the whole `w:rFonts` element and a colour row the whole `w:color` element, whatever attributes it has. The font command writes `ascii` and `hAnsi` and removes the theme attributes (`asciiTheme`, `hAnsiTheme`) of that element, because Word resolves theme attributes first; `eastAsia` and `cs` stay. The colour command writes `val` and removes `themeColor`, `themeShade` and `themeTint`. A row reads its value from `ascii`, then `hAnsi`, then the theme name (the layout reads the theme attributes today and maps them to Calibri or Calibri Light).
- **Update style to match:** applies to the *paragraph style* of the caret's paragraph. The paragraph's direct paragraph overrides, and the character overrides that are on every run of the paragraph, are written into the style (`pPr` and `rPr` of the `w:style`), then removed from that paragraph, in one undo step. Other paragraphs of the style change because the style changed; their own direct formatting stays and may now equal the style. Other style properties (`basedOn`, `next`, `link`, the linked character style) are not touched **(call)**. A direct override is never copied if the paragraph's style is not defined in the file (the command is disabled with that reason). The pane says once that the style is shared.
- **Clear direct formatting:** removes every row's override (all of the above except the excluded elements) from the selection's paragraphs and runs, keeps styles and the list, in one step. A paragraph mark's `rPr` is kept unless the selection covers the whole paragraph **(call)**.
- Every disabled command shows a reason (`WriteCommandRules.ReasonFor`), nothing is dimmed silently.

**Keys (call).** Ctrl+B, Ctrl+I, Ctrl+U, Ctrl+E (centre), Ctrl+L, Ctrl+R, Ctrl+J, and on macOS the ⌘ versions; none is in the reserved list or the ones 4.3a uses (checked against `ReservedShortcuts` in the plan). No shortcut for style commands in 4.3b.

**The existing 4.3a rules stay:** formatting acts only in a healthy session (a Faulted session disables it with the same reason), runs through the lease, and typing during a save is queued; text editing is unchanged.

## 5. How it is built

**Approaches considered** (owner picked B on 2026-11-16).
- **A. One path for everything:** direct formatting *and* "Update style" as `BlockChange` replacements. Rejected: a style edit touches every paragraph of the style, so history would hold thousands of blocks and could hit the 16 MiB cap.
- **B. Two kinds of edit (chosen).** Direct formatting rides the 4.3a path unchanged (one `BlockChange` per operation); a style edit is a new history step holding the old and the new `Style`, and the layout restarts from a new engine. "Update style to match" is one composite step: the style swap and the paragraph's changes together.
- **C. In-place property edits with their own history entries.** Rejected: a second undo system next to `EditHistory`, and it breaks "every change through `EditSession.Apply`".

**Parts.**
1. **Model (`Raffica.Write.Model`).** `Style.Clone`; a UI-free reader for the *effective* properties of a run or paragraph, used for toggles and the inspector. **It reads every layer the layout reads (call):** document defaults, the based-on chain, the character style, the table style's layers for text in a table, the numbering level's paragraph and run properties, then direct formatting (reading numbering and table styles is not authoring them; that stays with 4.4 and 4.5). Otherwise a toggle in a table header whose bold comes from the table style would be wrong. **One definition:** its answers must equal the layout's `StyleResolver` for every run and paragraph of every corpus file, tables and lists included (a test walks them; no tolerance). Correctness comes before leaving Layout alone: if the cheapest correct way is to move the layering rules of `PropertyLayers` into Model and let Layout use them, the plan may do so, guarded by `LayoutGoldenTests`, the oracle tests and the first-page limit of 4.2.
2. **Editing library (`Raffica.Write.Editing`).** Operations on `TextEditor`: `SetCharacterFormat`, `ToggleCharacterFormat`, `SetParagraphFormat`, `ApplyParagraphStyle`, `ApplyCharacterStyle`, `ClearDirectFormatting`, `RemoveOverride`, `UpdateStyleToMatch`, each returning an `EditResult` with the block changes. `FormattingInspector` (pure: caret or selection in, lines and rows out; each row knows how to remove itself) and `RunMerger`. `EditResult` gets an optional `StyleChange`; `EditHistory.Step` becomes a step with an optional block swap and an optional style swap (one undo = one step, the revision bookkeeping and `Validate` unchanged, `SizeOf` counts a style). A style step ends typing coalescing.
3. **Layout (`Raffica.Write.Layout`).** `DocumentLayout` gets a public `Restyle(throughPage)` that builds a new engine (a new `StyleResolver` and plan cache; today the resolver snapshots styles at construction and cannot see a changed `Style`) and restarts the layout from page 1, laying out through the pages in view first and the rest in the background, exactly as after a header change that restarts. Direct formatting needs no layout change: the paragraph objects are replaced and `ApplyEdit` re-plans them. The oracle (`LayoutOracle.AssertEqualToFull`) gets formatting steps and a style step for every corpus file, in both views.
4. **App (`Raffica.Write`).** `EditSession.Apply` handles a result with a `StyleChange` (calls `Restyle` on the active layout, drops the other view's layout as now); a `SelectionChanged` event on the session (today a caret move raises nothing) with a debounced pane refresh; `FormattingPane` in the frame's right pane, read on the UI thread and created only when first opened (`StartupCreatesNoEditorWork` extends to it); Home tab groups Font, Paragraph, Styles; View command Formatting; catalog ids, icons (own Lucide icon each), rules with reasons, macOS menu items, both languages ("Sie" form), key handling in the page view.
5. **Shell.** The ribbon today hosts buttons only. The font, size and style pickers need a ribbon item that hosts a picker (built on the shell's `SelectBox`, with the shell's tokens, focus, key tips and UIA); designing it with the shell's design direction is part of this milestone.

**Speed (call).** Formatting a word in the generated thesis (a paragraph edit) must stay inside the keystroke limit: a `format` case next to `typing` in the engine bench and the app measurement, ≤ 16.0 ms CPU cost judged on the Windows PC (same method as typing). A style update is not judged in 4.3b: the time until the pages in view are correct, and the full layout, are measured and recorded in the decision, and a limit is set from them later (the repository's "measure, then set").

**Proof.** The acceptance criterion is a model-level test over the inspector: for every row of a paragraph and a selection with many overrides, remove the row and compare the whole document (every part, serialized) with the document before, minus that one element; plus a property test over the corpus (each override of every paragraph and run, removed one at a time). Further tests: toggle and style-origin cases, merging, update-style semantics, undo and redo of each operation (document and layout equal to before), oracle runs, saved file reopens and lays out identically (`DocxFile` round trip of a changed style), the pane under `Raffica.Write.App.Tests`. **Owner gates (listed open until checked):** a look at the AOT app on a real display (ribbon pickers, pane, key tips), Word accepts a file after formatting and a style update (`Docs/write-word-check.md` gets rows).

## 6. Known limits (to be recorded in the decision)
- Update style to match works on the paragraph style only; a linked character style, table styles and `basedOn` ancestors are not edited.
- A character override is shown and removed per element; two different underline kinds across a selection show as "partly".
- Properties the layout does not read (character spacing, kerning, run borders, shading) can be seen as rows and removed, but have no control.
- Formatting inside a tracked change is not tracked; stale `rPrChange` children are kept.
- The writing view draws no header, footer or note, so formatting there is only reachable in the page view (as for text in 4.3a).

## 7. Left to the builder
The exact home of the effective-property reader; the wording of every row in both languages; pane layout and the picker control's look; key-tip letters and icons; the cap on how many paragraphs a single format command may touch before it asks (a "select all" must stay fast and within the history limit; the plan measures it).
