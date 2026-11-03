# Product

<!-- impeccable:product-schema 1 -->

## Platform

web

## Users

The owner and people like them: someone who works with documents every day and wants a fast, tidy tool, not an enterprise administrator. Their work is a mix of:

- **Personal admin:** budgets, invoices, tax and insurance PDFs, formal letters.
- **Office work:** reports, project plans, meeting slides, forms to fill in and sign.
- **Study or research:** papers with references, data tables and charts, lecture slides.

The owner uses spreadsheets most. They often receive Word, Excel, PowerPoint and PDF files from other people and have to open, edit and send them back without breaking them.

## Product Purpose

Raffica Office is a fast, open-source office suite for Windows, Linux and macOS with four apps: **Raffica Write** (word processor), **Raffica Sheets** (spreadsheet), **Raffica Slides** (presentations) and **Raffica PDF** (viewer and editor). It grows out of the finished PDF editor Pagewing.

Success means that cold start, opening a file and scrolling are fast, that DOCX, XLSX and PPTX files open and look right, and that commands are as easy to find as in Microsoft Office without LibreOffice's clutter.

## Positioning

- **Speed is the core promise.** A Native AOT desktop app with no web runtime. The speed targets are set from measurements, not invented.
- **One structured UI.** Commands are grouped by task and easy to find. There is one UI structure, not several competing modes (LibreOffice has seven under View > User Interface).
- **Respect for the user.** No ads, nag screens, upsell prompts, subscription-only features or account requirements. AI features are optional and off until the user turns them on. No telemetry without opt-in. User data is never changed silently (no automatic date or number conversion on import).
- **Compatibility is measured.** Claims about DOCX/XLSX/PPTX fidelity rest on a corpus of real files compared with Word, Excel and PowerPoint output.
- **Free software:** GPL-3.0-or-later.

## Operating Context

- Long working sessions at a desktop or laptop with keyboard and mouse, on Windows, Linux or macOS.
- Files arrive from others (mail attachments, downloads, shared folders) in Microsoft formats and as PDFs; they are edited and sent back.
- PDFs are read, annotated, filled in, signed, redacted, OCR'd, and their pages are merged, split and rotated.
- Several documents are open at once; the PDF core already manages document tabs.
- Autosave and crash recovery are on by default and local.

## Capabilities and Constraints

- **Desktop application, not a website.** Built with .NET 10 (LTS), Native AOT and Avalonia 12. The platform value `web` above means one custom design language that is the same on every operating system. It does not mean the product runs in a browser. Design directions are prototyped as HTML pages; the chosen system is implemented by hand in AXAML.
- **Four apps, one suite:** one installer, apps start individually, one shared design system (tokens, typography, spacing, components) with one accent color per app.
- **The PDF core exists** (imported from Pagewing, no UI yet). It covers rendering, tabs, thumbnails, search, annotations (highlight, markup, drawing, arrows, text boxes), forms, image signatures, redaction, OCR, page operations (merge, split, rotate) and printing. Its command palette matching already exists (`Raffica.Core.Palette`).
- **Write, Sheets and Slides do not exist yet.** Their scope is set in their own specs. Write's layout engine and Sheets' formula engine are not decided.
- **Command structure is open.** Each design direction may propose its own (ribbon tabs, a slim toolbar with command search, a contextual side panel). The owner chooses the structure together with the look. Every option keeps commands grouped by task.
- **Frequent commands are visible and one click away.** The owner rejected a direction whose slim toolbar left most commands behind a search field and a side panel ("terrible for clicking things quickly"). Search, command palettes and panels are accelerators, never the only path to a frequent command. The owner wants operation that is simple and a screen that is easy to take in at a glance.
- **Light and dark theme**, both first-class.
- **Languages:** English is the UI source language; German ships from the first release. German labels run longer, so the layouts must leave room for them.
- **Not planned for now:** ODF and the legacy .doc/.xls/.ppt formats, macros and extensions, real-time collaboration, any cloud service, mobile and web versions.

## Brand Commitments

- Names: **Raffica** (Italian for "gust"), the suite **Raffica Office**, the apps **Raffica Write**, **Raffica Sheets**, **Raffica Slides** and **Raffica PDF**.
- One accent color per app: **Write blue, Sheets green, Slides orange** (the hues people know from Microsoft Office) and **PDF plum**. Red is reserved for destructive, irreversible actions, so it is not an app accent. The exact shades belong to the design system. Every accent passes a WCAG contrast check in the light and the dark theme.
- The chosen design direction is D "Formular Neutral" (see `Docs/DECISIONS.md`): neutral chrome, the app accent only for active and focused states, a ribbon with named command groups.
- Raffica's design system is created from scratch; it does not inherit the owner's other design systems.
- The design must not feel **dated or cluttered** (LibreOffice: too many icons, grey bevels, no clear structure) and must not feel **playful or trendy** (gradients, glass, mascots or a loud personality that wears thin over a workday).

## Evidence on Hand

- No logo, icon set, screenshots or brand assets exist yet.
- No measured speed figures exist yet; Milestone 1 measures them. Mockups and copy must not state speed numbers.
- No users, testimonials, reviews or download counts exist. Do not invent any.
- `src/Raffica.Core/Assets/Fonts/Geist-Regular.ttf` is embedded in the PDF core for PDF text boxes. It is not a UI typeface commitment.
- The suite spec (`Docs/superpowers/specs/2026-11-03-raffica-suite-design.md`) and `Docs/DECISIONS.md` hold the decisions behind this file.

## Product Principles

1. **Nothing stands between the user and the document.** Speed is felt in every interaction, not advertised.
2. **One structure, learned once.** The same command structure, panels and shortcuts work across all four apps.
3. **Find, don't hunt.** Frequent commands are in sight with a name and one click away; everything else is one step deeper in its task group or found by search. Nothing hides behind a mode.
4. **Respect is a feature.** No interruptions, upsells or silent changes to the user's data.
5. **Calm for a full workday.** Clear and quiet; distinct without being loud.

## Accessibility & Inclusion

- WCAG 2.2 AA contrast for text and every app accent, in the light and the dark theme.
- Every command reachable from the keyboard.
- Layouts tolerate German strings, which run about 30% longer than English.
