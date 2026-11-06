# Changelog

All notable changes to this project are documented here.

Format after [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), versioning after [Semantic Versioning](https://semver.org/). Every release entry names the included milestones.

## [Unreleased]

### Added

- Raffica PDF viewer: open, tabs, scroll, zoom, search, links, print. Open files by argument, dialog or drag and drop (with a password prompt); a second file opens in the running instance; a file that is already open is selected. Tabs, continuous scroll, single, continuous and two-page layouts, zoom modes (page width, whole page, actual size, free up to 800 percent), go to page, a dark page without inverted images, a pages pane with thumbnails and an outline pane.
- Search with highlights, next and previous; select and copy text; links with a confirmation that shows the address before an external one opens; document info and the F1 help card; command palette entries for open tabs, recent files, pages and outline entries; keyboard shortcuts for every viewer action.
- Settings for PDF (page appearance, default layout and zoom, print scaling); position per file (except a file given on the command line at first launch); printing on Windows (system print dialog) and Linux (CUPS) with page range, copies, fit to paper and a background job with progress and cancel. English and German.
- Raffica PDF annotating and saving: markup (highlight, underline, strike-through) with comments, freehand, rectangle, ellipse, line, arrow and text box tools, select, move, resize and delete, and one undo and redo history for all of them; a colour and width strip, a marks pane that lists every mark with its comment, save and save as (incremental by default, atomic and checked) with a warning for signed files, failure cards, and an unsaved-changes prompt on close. English and German.
- Raffica PDF forms and signing: fill a form by clicking its fields (text, check box, radio button, list and combo box), with Tab and Shift+Tab walking the fields, a switch that highlights the fields, a counter of empty required fields with a card before saving, and a note for what is not supported (calculations, XFA, buttons). Add a visible signature, drawn or imported from an image, from up to ten saved signatures; it is a picture on the page, not a digital signature. A Forms ribbon tab holds the commands. English and German.
- Raffica PDF organizing pages: a Pages ribbon tab and an organize mode in the pages pane to reorder pages by drag or keys, rotate, delete, duplicate and add blank pages, and insert pages of other files (merge); nothing is written until Apply, which lists what a rewrite loses, warns for signed files and offers Save as or the original file. Split a file by a number of pages, by ranges or by bookmarks, extract pages into a new file and export pages as PNG images. Limits: at most 20 inserted files per organize session (leave the mode and enter it again to insert more), 20 000 pages per plan and 64 megapixels per exported image. English and German.

### Changed

- Raffica PDF: faster start on Windows and a third of the idle memory through software rendering; set `RAFFICA_RENDERING=default` to use the GPU path instead.

### Fixed

### Removed
