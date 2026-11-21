# Raffica Write 4.4a: headings, lists and the outline (design)

Status: draft for approval, 2026-11-21. Milestone 4.4.1 of `Docs/roadmap.json` (the roadmap's 4.4 is split into 4.4.1 and 4.4.2 by this document, as 4.3 was split into 4.3.1 to 4.3.3). The subproject spec is `Docs/superpowers/specs/2026-11-13-raffica-write-design.md` (section 6 "Structure", goals 4 and 9); this document only settles how 4.4a builds what that spec already decided. Sections, "start page numbering here", headers and footers and page setup are 4.4b and get their own design.

Items marked **(call)** are decisions of the author of this document, not of the owner. The owner decided: the split into 4.4a and 4.4b, heading numbering linked to the heading styles (approach A below) and the outline move as a step of its own (option 1 below), all on 2026-11-21.

## Intent

Write can edit and format text, but it has no tools for structure: no way to make a list, to number headings, or to reorganise a chapter. The spec's rule is that Word hides its structure, so simple wishes become recipes. 4.4a gives a thesis writer three wishes in one action each: "number my headings", "make this a list", and "move this chapter". Whatever they do, a number on the page is always the number the model says, and a move never splits a chapter from its heading.

## Goals

1. **Number the headings in one command.** A switch (`structure.numberHeadings`) numbers Heading 1 to 9 as 1, 1.1, 1.1.1 and so on through the document's own numbering part, and switching it off removes the numbers. A heading that is typed, pasted, moved or restyled gets or loses its number from its style, with nothing per paragraph to go stale.
2. **Lists.** Toggle bullets and numbers on the selected paragraphs, change the level with Tab and Shift+Tab, restart numbering at a paragraph, and continue a list that was restarted. Lists use the numbering definitions the document already has; Write adds its own only when the document has none of the kind.
3. **The outline moves chapters.** In the outline pane a heading moves with everything under it, by drag and by keys, and is promoted or demoted (the heading and its sub-headings together). Each is one undo step and equal to a fresh layout afterwards.
4. **Numbers never go stale (acceptance).** After a move, a delete of a heading or a range of headings, an insert, a level change, a switch of the numbering, and after undo and redo of each, every label equals the one an independent count of the model gives, and the incremental layout equals a full one (tests).
5. **A move on the 300-page thesis is measured** (model change plus the visible pages), recorded first and given a limit afterwards, as 4.3 did (**call**: the spec says "without lag" and gives no number).

## Non-goals and anti-goals

- No list gallery, no multilevel-list designer, no custom number formats, no change of the document's numbering definitions beyond what the commands above write. Collapsing and expanding the outline, dragging text in the page, and showing the numbers in the outline pane are not in 4.4a.
- No chapter numbers in captions, no table of contents (4.5), no sections or page numbers (4.4b).
- A move is not recorded as a tracked change (the first release never records the user's own tracked changes).
- Anti-goals: a number on screen that differs from the model; a silent change to numbering the file already has (a heading with its own `numPr` is left alone and the formatting pane shows it as an override); an "automatically update" style behaviour (the switch is an explicit, undoable style edit).

## Constraints that bind

- Everything of `CLAUDE.md`: .NET 10 Native AOT, English source with German localization ("Sie"), every command its own Lucide icon and a reason in each disabled state (`WriteCommandRules`), strings in both resx files (LF), cards through `CardFooter`, no warning suppressed.
- **The model is the vocabulary** (4.1 rule): numbering is read and written through the existing `NumberingDefinitions`, `NumberingInstance` and `NumberingLevel`, new children are placed by `SchemaOrder`, nothing unknown is touched.
- **Every model change goes through the lease and `EditSession.Apply`; every raise is attributed** (`DocumentEdits.Attribute`, the snapshot-cache rule), with a case in `SnapshotCacheOracleTests`. A new layout fast path needs a corpus oracle case first (`LayoutOracle.AssertEqualToFull`, exact equality).
- **Start cost.** Nothing of the structure commands, the move, the numbering writer or the outline drag exists before first use (tests like `StartupCreatesNoEditorWork`). New ribbon commands must keep the Home band's width hint true (`WriteRibbon.HomeFullWidthAtLeast`, `RibbonStartupTests`); the hint is raised with its test when the real band grows, never ignored.
- **Typing stays at most 16 ms** with the outline pane open (`typing-pane` rows); `format` and `typing` limits are not loosened.
- Keys: Alt+Q, Ctrl+K, AltGr and the macOS reserved list (`ReservedShortcuts`) bind as before; a new key without a command goes into `NavigationHelp`.
- The outline reads the model on the UI thread only and navigates by the paragraph, not by a stored index (4.2 and 4.3a rules).

## Approaches considered

**Heading numbering.**
- **A. Linked to the styles (chosen).** The multilevel list is tied to Heading 1 to 9 by `w:numPr` in each style (and `w:pStyle` in each level of the definition), as Word's "link level to style" writes it. Cost: the switch edits nine styles and may add a definition, and a heading with its own `numPr` is not touched.
- **B. A `numPr` on each heading paragraph.** Simple, but a heading typed afterwards has no number until it is applied, which is the stale state the milestone exists to remove. Rejected.

**The outline move.**
- **1. A move step of its own (chosen).** The history stores indices (container, first block, block count, target) and undo is the inverse move; no block is cloned and bookmarks, comment anchors and paragraph ids stay because the same objects move. The layout is told that the range spanned by source and target changed (`First`, and a removed count equal to the added count) and re-lays out from its first block. Cost: a new step type in `EditHistory`, a move case in `ApplyEdit` and in the oracle.
- **2. One `BlockChange` over the spanned range with clones.** No new step type, but moving the first chapter of the thesis to the end clones most of the body and presses on the 16 MiB history limit. Rejected.
- **3. Cut and paste through the fragment codec.** Loses bookmarks, anchors and ids. Rejected.

## Design

### Heading numbering

`structure.numberHeadings` is a toggle (`CheckedWhen`: headings are numbered when the style of heading level 1 carries a `numPr` with a list that exists). **On:** if the Heading styles already carry a list, nothing is written and the command shows on. Otherwise Write finds a multilevel definition whose levels are not linked to other styles, or adds Write's own (the decimal 1, 1.1, ... of the blank document, nine levels, with `w:pStyle` on each level), adds a `w:num` for it, and sets `numPr` in the paragraph properties of the styles `Heading1` to `Heading9` (the numbering `numId`, and `ilvl` from level 2 on; Word's own spelling). **(call)** The styles are found by their built-in ids and names (`heading 1` to `heading 9`), not by outline level: the outline rule "never style names" is about reading a document's structure, and Word itself links a list to exactly these styles; a document whose headings use other styles gets the command disabled with a reason that names this. **Off:** removes that `numPr` from the styles; the definition stays in the part, as Word leaves it. The whole switch is one undo step (a style step that carries all changed styles; the builder shapes it from `StyleChange`), then `DocumentLayout.Restyle` as for any style change, which must also give the layout a fresh numbering reader (the counter caches facts per list and level). The new definition and `w:num` are not themselves in the history: an undo leaves them unused in the numbering part, which is valid and harmless **(call)**; the oracle and a Word check cover it.

The look of numbered headings (hanging indent and the tab after the number) is the definition's level properties; the builder picks them with the existing heading styles and records the result in the DECISIONS entry. The owner sees it in Word (gate below).

### Lists

Commands (Home, paragraph group; ids `list.bullets`, `list.numbers`, `list.restart`, `list.continue`), each acting on the paragraphs of the selection like paragraph formatting does (one `BlockChange` per operation): toggling a type on a paragraph with no list gives it a `numPr` of that type's list; on a paragraph already in that type removes the `numPr`; on one in the other type switches it. **The list used:** the document's own list of that kind (a `w:num` whose level 0 is a bullet or a number, the one the List Bullet or List Number style points at when the document has them, otherwise the first), else Write's own, copied from the blank document's bullet and numbered definitions and added with its `w:num`. A list started by a command gets its own `w:num` per run of paragraphs so it numbers from 1. **(call)** A paragraph in the Normal style also gets the List Paragraph style (it exists in Write's blank set) so the indent is the list's; a heading or any other style keeps its style.

Keys **(call, as Word)**: Tab and Shift+Tab in a list paragraph with the caret at the start of its text, or with a selection over list paragraphs, change the level (0 to 8; the indent follows the level's properties); in any other place Tab inserts a tab as today and Shift+Tab does nothing as today. Enter on an empty list paragraph ends the list (removes its `numPr`, keeps the paragraph); Backspace at the start of a list paragraph first removes the list from it, keeping the text. `list.restart` gives the paragraph and the following paragraphs of its list a new `w:num` with a `w:startOverride` of 1 for that level; `list.continue` points them back at the previous list's `w:num`. Disabled states carry reasons (not in a list, nothing to continue, the selection reaches a table or a content control that Write does not enter for lists, and so on). Bullets: the layout already maps the Symbol and Wingdings private-use code points (`NumberingCounter.BulletText`); the builder checks that a blank document's bullets draw as bullets in the page view on all three systems and fixes what does not.

### Outline: move, promote, demote

The outline lists headings by resolved outline level (unchanged). A heading's **subtree** is the top-level blocks from the heading up to, not including, the next listed heading of the same or a higher level (or the end of the body). **Move:** drag a row between two rows, or the keys, moves the subtree to that place as one move step: the destination is always a top-level block boundary of the body; a drop inside the subtree itself does nothing; after the move the caret is at the start of the moved heading and its row stays selected; the pane reads again (200 ms rule). **Promote and demote:** the heading and every heading in its subtree change by one level through their paragraph style (`HeadingN` becomes `HeadingN-1` or `HeadingN+1`), or through a direct `w:outlineLvl` where that is what sets the level; a heading whose level comes from a style that is not one of the nine, or a change that would leave 1 to 9, disables the command with a reason. **(call)** A subtree that holds a paragraph with a section break (`w:sectPr` in its properties) is not movable in 4.4a (command and drag disabled, with the reason); 4.4b makes sections first-class and lifts this. Content that lies between source and target (including section breaks) only shifts in the list of blocks, it is not edited. A heading inside a block content control is listed as today and is not movable (reason); a heading in a table is not listed. **(call)** Keys, in the pane and with the caret in a heading: Alt+Shift+Up and Down move, Alt+Shift+Left and Right promote and demote (Word's keys); AltGr stays text; on macOS the Option+Shift arrows extend the selection by paragraph, so the macOS keys come from the `ReservedShortcuts` audit and are pinned there. The commands exist in the command search (`outline.moveUp`, `outline.moveDown`, `outline.promote`, `outline.demote`) and as buttons in the pane's header; the drag has a drop-line marker and an accessible name for the move.

The move step: `EditHistory` gets a second kind of non-text step (beside `StyleChange`) with container path, first, count and target; it is not coalesced; undo is the inverse move; `DocumentEdits` raises a unique revision; `DocumentEdits.Attribute` is told the rotated range so `BodySnapshotCache` writes the same bytes (blocks keep their identity). `DocumentLayout.ApplyEdit` gets the rotated range as a change with the added count equal to the removed count and re-lays out from the first block, using the plan caches as for any block change. A heading that moves keeps its `w14:paraId`.

### Proof of "never stale"

An independent reference count (a small numbering counter in the test project, not the layout's) gives the label of every numbered paragraph from the model. `StructureSteps` (new oracle steps beside `FormattingSteps`: move, promote, demote, delete heading, delete range over headings, insert heading, toggle numbering, toggle list, level change, restart, undo, redo) runs on the corpus files and on random documents with headings; after each step the layout's labels equal the reference and `AssertEqualToFull` holds. A guard asserts that every kind ran (the `RanPerKind` rule of 4.3b).

### Measuring

A `structure` case (engine bench and app switch, as `format`): move a chapter of the thesis (first to last, middle, last to first), switch heading numbering on and off, toggle a list; rows `structure/*`. The move is recorded, then a limit is set from the median (the project's measure-then-set rule, **call**); the numbering switch is recorded and not judged. `typing` and `format` are re-run with the pane open.

## Owner gates (open until recorded)

- Word opens a file with numbered headings (switched on in Write, after a move and a promote), a bullet list, a numbered list with a restart and a multilevel list, with no repair prompt, and the numbers read as in Write (rows in `Docs/write-word-check.md`; `pwsh tools/word-open-check.ps1 <files>`).
- A look at the Native AOT app on a real display on Windows, the Linux laptop and the Intel Mac: pane drag and drop line, keys, list buttons, numbered headings in both views.
- The `structure` and typing-with-pane cases on the Linux laptop and the Intel Mac (`measure.yml`).

## Left to the builder

The shape of the style step that carries several styles; the name and place of the numbering writer and the list finder; whether list commands share code with paragraph formatting; the exact hanging indents and tab of numbered headings; the ribbon placement within the Home band rules; the macOS keys within the audit; how the drop line is drawn.

## Documents

The roadmap splits 4.4 into 4.4.1 (this) and 4.4.2 (sections, page numbering, headers and footers, page setup; the "page-3 1 and A-1" acceptance moves there) and the prerequisite of 4.5 becomes 4.4.2 ("Scope changes" records it). `Docs/DECISIONS.md` gets "Raffica Write: headings, lists and the outline (M4.4a)", `CLAUDE.md` a "Write structure (M4.4a)" section, `CHANGELOG.md` `[Unreleased]` the user-visible changes.

## Settled after the builder check

Answered by the project: **the numbering part is saved like any model part.** `DocxFile.SaveAsync` rewrites every loaded model part (styles, numbering and the body alike), and a recovery copy goes through the same writer; `BodySnapshotCache` caches body blocks only, so a numbering change needs no cache entry. A new definition or `w:num` is never removed by undo, so a redo of the style step always finds what it points at. The style step therefore stays a style step (several styles in one history entry); it needs no snapshot of the numbering part.

Calls of the author (a wrong guess here changes the plan little, so they are settled rather than asked):

- **Which list is "the document's own list of that kind" (call).** A `w:num` whose abstract definition is not linked to a style (no `w:pStyle` on any level), whose level 0 is a bullet or a number; the one the List Bullet or List Number style points at wins, otherwise the first by position in the part. Definitions linked to the heading styles are never picked for a list.
- **Bookkeeping of `w:num` (call).** A toggle reuses a `w:num` that Write created and no paragraph or style references any more, so toggling a list on and off does not grow the part; a restart always adds one (with its `w:startOverride`), and undo leaves it unused. There is no clean-up of unused instances in 4.4a (a known limit in the DECISIONS entry). The reference counter of the tests follows Word's rules for shared abstract definitions and `startOverride`, as the layout's counter does, but is written separately.
- **Promote and demote are paragraph changes, not style edits (call).** They change the style (or `w:outlineLvl`) of paragraphs, so they go through the formatting path (`EditSession.Apply`, `ApplyEdit`), as one `BlockChange` over the range from the heading to the last heading of its subtree (clones of that range, bounded by one chapter). `Restyle` is only for an edit of a `Style` object, that is, the numbering switch.
- **The two level sources (call).** Promote and demote write exactly two kinds of source: the style `HeadingN` and a direct `w:outlineLvl`. Every other source disables the command with a reason. Both kinds are tested, in a mixed subtree too.
- **Keys (call).** Alt+Shift with the arrows is fixed on Windows and Linux. On macOS the commands take a key only if the `ReservedShortcuts` audit has a free one; otherwise they have none there and stay in the command search and the pane buttons.
- **The move target (call).** There is no hard ceiling. The aim is at most 100 ms on the Linux laptop from the command to the first visible page on the thesis; the limit is the median times 1.25 as for every budget, and a result above the aim is a finding for the owner, not a silent change of the limit or the design.
- **The roadmap split is the first task of the plan**, on this branch (`Docs/roadmap.json`, then `dotnet run tools/roadmap.cs -- generate`).
