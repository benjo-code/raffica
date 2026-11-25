# Changelog

All notable changes to this project are documented here.

Format after [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), versioning after [Semantic Versioning](https://semver.org/). Every release entry names the included milestones.

## [Unreleased]

### Added

- **Raffica Write: tables, pictures, footnotes and endnotes.** The new Insert tab makes a table (a grid picker up to 10 by 8 or a card for any size up to 63 columns by 500 rows), a picture from a file (PNG, JPEG, GIF or BMP, with an optional description for screen readers) and footnotes and endnotes, and the new Table tab adds and deletes rows and columns, merges and splits cells, sets borders, shading, a repeated header row, row heights and column widths, and moves rows. The pointer works on the objects directly: drag a column border or the right edge of a table, drag a row by its handle, drag across cells to select a range, click a row, column or table margin to select it, resize a picture by its handles (Shift frees the proportions), change a picture's wrapping (in line, square, top and bottom, in front of or behind the text) and drag a floating picture to another paragraph or nudge it with the arrow keys; every gesture has a card or command as well, and each is one undo step. A footnote or endnote moves the caret into the note, Esc or "Back to the text" returns, and a double click on a reference or a note number jumps between them; deleting the reference deletes the note. A picture can also be pasted from the clipboard (a screenshot or an image copied from another program); a picture inside Write cannot be copied or cut yet, and Write says so, and a picture can be selected with Shift and an arrow key to reach its description, size and wrapping without the mouse. Word opens the saved files without a repair prompt (checked with 21 files on Windows).
- **Raffica Write: page numbers and sections in one action.** Layout > "Number pages from here" starts the page numbers at the caret (for example "1" on page 3, or "A-1" for an appendix, in Arabic, Roman or letter format) and makes the section it needs, with a small marker at its start; removing the marker takes the section away again. Plain section breaks (next page, continuous, even page, odd page), "different first page", "different left and right pages", own or same-as-previous header and footer per section, and "Insert page number" (page, page of total, with a position) are on the same tab, and a Sections pane (View) lists every section with its start page, numbering and page setup.
- **Raffica Write: page setup.** Paper size, margins, orientation and columns for this section, the whole document or from here on, from one card or the Layout tab, with US Letter and inches in the United States and Canada.
- **Raffica Write: the outline moves a chapter that holds a section break**, and says why when a move would leave an empty last section.
- **Raffica Write: numbered headings, lists and a movable outline.** One command (Home, "Number headings") numbers the headings of a document in the document's own heading styles (1, 1.1, 1.1.1), switches the numbers off again and keeps them current when you move, promote, demote or delete a heading. Bullets and numbered lists have levels (Tab and Shift+Tab at the start of an item or over a selection), "Restart numbering" and "Continue numbering"; Enter on an empty item ends the list and Backspace at the start of an item removes the list from it first. In the outline pane a chapter moves with its content by drag (a line shows where it lands) or by buttons, and promotes or demotes with its sub-headings; the keys are Alt+Shift with the arrow keys (Up and Down move, Left and Right promote and demote; no key on a Mac, use the Format menu, the ribbon or the pane buttons). A chapter that sits in a content control cannot be moved, and a move never leaves a table as the last block of the document. Every action is one undo step.
- **Raffica Write: new documents and recovery.** Home > New document (Ctrl+N, ⌘N on a Mac) opens an empty document as "Untitled" ("Unbenannt" in German), with a built-in set of styles (Normal, Heading 1 to 9, Title, Caption, contents and note styles) on A4, or US Letter in the United States and Canada. Saving an untitled document asks for a name. While a document has unsaved changes, Write keeps a recovery copy next to its settings every 30 seconds and when you switch to another window; your own file is only written when you save. If Write closes unexpectedly, the next start offers to restore the copies as unsaved documents (Restore, Discard or decide later; a restored document is saved under a name you choose, never over the old file by itself). The setting "Keep a recovery copy of unsaved work" is in the settings dialog.
- **Raffica Write: pictures scroll more smoothly.** Pictures are decoded in the background instead of while a page is drawn, and the memory they use is limited; a picture that is not ready yet shows as a marked box and fills in when it is.
- **Raffica Write: formatting and styles.** Format the selection or the next typed text with bold, italic, underline, strikethrough, superscript, subscript, small and all caps, font, size, text colour and highlight; set alignment, indents, spacing before and after, line spacing and the pagination switches; apply a paragraph or a character style from the document's own styles. Each formatting action is one undo step, and text typed after a format command such as Ctrl+B is a step of its own, as in Word. The new Formatting pane answers "why does this paragraph look like that?": it names the styles and lists every direct override as its own row, each row can be removed on its own, and "Update style to match" and "Clear direct formatting" are one click. A style is changed only when you ask for it. Keys: Ctrl+B, I, U, E, L, R and J (⌘ on a Mac).

### Fixed

- **Raffica Write: moving a chapter in the outline while the caret is far away no longer stalls.** A chapter move (the outline pane, a drag or Alt+Shift with Up and Down) made while the cursor sat many pages from the view laid out the whole document first, about half a second on the thesis; it now takes a few milliseconds, as with the cursor in view.
- **Raffica Write: a saved file no longer asks Word to repair it.** Pressing Enter or pasting several lines into a plain-text, combo box, drop-down list or date content control made a second paragraph that Word refuses; these controls now keep one paragraph (Enter makes a line break only in a multi-line text control).
- **Raffica Write: page breaks match Word.** The extra room of a line spacing such as 1.5 is below the text of a line and no longer needed to fit the last line of a page; the thesis test document is 309 pages, as in Word (it was 311).

## [0.2.0] – 2026-11-12

Raffica PDF now runs on macOS (Milestones 3.1, 3.2 and 3.3).

### Added

- **Raffica PDF for macOS 15 or newer**, as a disk image for Apple silicon (`arm64`) and one for Intel Macs (`x64`). Open the image that matches your Mac and drag Raffica PDF into Applications. It has the Mac menu bar and shortcuts, opens PDFs from the Finder and can become your default PDF app.
- **First start:** the app is not notarized by Apple (there is no Apple developer account behind this project), so macOS blocks it once. Allow it under System Settings, Privacy & Security, "Open Anyway"; the README has the steps.
- **Updates:** Raffica PDF on macOS offers new versions itself, from the next release on.
- **Tested:** the Intel version was checked on a real Mac, the Apple-silicon version has not been tried on one yet.

### Fixed

- Opening a file in the running app from a second start could lose the file on a slow computer.

## [0.1.0] – 2026-11-09

First release: Raffica PDF for Windows and Linux (Milestones 0 to 2.7: repository and CI, design system and app frame, viewer, annotating, forms and signing, organizing pages, redacting, text recognition and text editing, packaging and release, release readiness).

### Added

- **View and print:** open PDFs in tabs, scroll, zoom, search, follow links and print (Windows print dialog, Linux CUPS). Page thumbnails, outline, a dark page and your position in each file are remembered.
- **Annotate:** highlight, underline, strike through, draw, shapes, arrows and text boxes, with comments and undo. Saving is safe and keeps the original data (incremental by default).
- **Forms and signatures:** fill in forms by clicking, walk the fields with Tab, add a visible signature (drawn or from an image; it is a picture, not a digital signature).
- **Organize pages:** reorder, rotate, delete, duplicate, add blank pages, merge files, split, extract and export pages as PNG. Nothing is written until you apply.
- **Redact:** black out text and areas (also email addresses, IBANs, phone and card numbers); the result is checked and the report says what was and was not checked.
- **Text recognition (OCR) and text editing:** recognize text in scans (English and German included) and correct a line of text.
- **Command line:** the tool `raffica` works on PDF files without a window.
- **Packages:** a Windows installer and a portable ZIP (not code-signed, so SmartScreen shows a notice), and a Linux `.deb` and AppImage (tested in a container, not yet on a real Linux desktop).
- **Default app and updates:** the app asks whether it should be your default PDF app, and checks for updates at most once a day (switch in Settings). An update is installed only after you confirm; its signature, size and SHA-256 are checked first. This is the first release, so no update is offered yet.
- English and German.

