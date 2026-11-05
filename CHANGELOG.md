# Changelog

All notable changes to this project are documented here.

Format after [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), versioning after [Semantic Versioning](https://semver.org/). Every release entry names the included milestones.

## [Unreleased]

### Added

- Raffica PDF viewer: open, tabs, scroll, zoom, search, links, print. Open files by argument, dialog or drag and drop (with a password prompt); a second file opens in the running instance; a file that is already open is selected. Tabs, continuous scroll, single, continuous and two-page layouts, zoom modes (page width, whole page, actual size, free up to 800 percent), go to page, a dark page without inverted images, a pages pane with thumbnails and an outline pane.
- Search with highlights, next and previous; select and copy text; links with a confirmation that shows the address before an external one opens; document info and the F1 help card; command palette entries for open tabs, recent files, pages and outline entries; keyboard shortcuts for every viewer action.
- Settings for PDF (page appearance, default layout and zoom, print scaling); position per file (except a file given on the command line at first launch); printing on Windows (system print dialog) and Linux (CUPS) with page range, copies, fit to paper and a background job with progress and cancel. English and German.
- Raffica PDF annotating and saving: markup (highlight, underline, strike-through) with comments, freehand, rectangle, ellipse, line, arrow and text box tools, select, move, resize and delete, and one undo and redo history for all of them; a colour and width strip, a marks pane that lists every mark with its comment, save and save as (incremental by default, atomic and checked) with a warning for signed files, failure cards, and an unsaved-changes prompt on close. English and German.

### Changed

- Raffica PDF: faster start on Windows and a third of the idle memory through software rendering; set `RAFFICA_RENDERING=default` to use the GPU path instead.

### Fixed

### Removed
