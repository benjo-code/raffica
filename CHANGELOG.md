# Changelog

All notable changes to this project are documented here.

Format after [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), versioning after [Semantic Versioning](https://semver.org/). Every release entry names the included milestones.

## [Unreleased]

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

