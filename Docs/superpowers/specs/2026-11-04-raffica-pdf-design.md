# Raffica PDF – Design

Status: approved by the owner on 2026-11-04 · Date: 2026-11-04 · Author: brainstorming session (M2)

Items marked **(call)** are decisions made by the author of this document, not by the project owner; they are part of the document the owner approved on 2026-11-04 as handed over. Everything else was decided by the owner in the session or is settled in an existing document that this one names.

## 1. Intent

Raffica PDF is the first app of the Raffica suite: the PDF editor Pagewing (`v1.0.0`) moved onto the new design system and shell. The PDF engine and its features already exist in `Raffica.Core`, `Raffica.Pdfium`, `Raffica.Ocr` and `Raffica.Cli`, imported in M0 and tested. What is missing is the whole application: Pagewing's `Pagewing.App` was deliberately not imported, the UI is built fresh on the frame of milestone 1.2.

This spec therefore does not decide what the PDF app can do. It decides how every existing capability reaches the new UI, in which order, within which speed limits, and what has to be rebuilt for Raffica (installer, updater, default-app registration).

Pagewing's priorities carry over unchanged and in this order: speed (start, open, scroll), then design, then feature breadth. The primary user is the owner, who uses it daily; after that anyone who wants a lean, fast PDF editor.

## 2. Goals

1. **Everything Pagewing 1.0 had, in the first release** (owner). The list that follows is illustrative, not the definition: view, search, copy text, print; annotate (highlight, underline, strike-through, comments, freehand, shapes, line, arrow, text boxes); fill AcroForm fields; visible signature; organize pages (reorder, rotate, delete, insert, merge, split, export as image); redact; OCR; edit text lines within the limits below. It also includes the comfort features of Pagewing's milestone 6 and its viewer milestones, for example the dark page without inverted images (switchable per tab), the position remembered per file, the command palette with tabs and contents, keyboard shortcuts for every viewer action, outline and bookmarks, and recent files. **(call)** The definition is Pagewing's own roadmap (`Docs/ROADMAP.md` and `README.md` of the `v1.0.0` tag): the first task of milestone 2.1 is an inventory of it, written into the roadmap as a checklist per milestone, so nothing is dropped by accident and nothing new is added.
2. **Fast.** The budgets are set from measurements (section 5), not inherited from Pagewing.
3. **Built on the shared frame:** title bar with document tabs and search (Ctrl+K), ribbon with compact mode, side panes, status bar, start view, settings dialog, field codes, F6 pane focus, light, dark and high contrast, English and German. The PDF app adds its own ribbon definition, panes and document view; it does not change the frame's behaviour.
4. **Never damages the original.** Saving is atomic and checked; digital signatures stay valid where technically possible (Pagewing's rules, section 6).
5. **The command line stays.** `raffica` (already imported) shares the core with the app and ships with it.

## 3. Non-goals and anti-goals

Same limits as Pagewing (owner):
- Certificate signatures, XFA forms, JavaScript in PDFs, PDF to Word, cloud, accounts, AI assistant, preflight, 3D and multimedia.
- No telemetry and no online services. The only network access is the update check, which can be switched off.

Also not in the first release:
- **Nothing new beyond Pagewing 1.0 (owner).** New PDF features go to the roadmap backlog; the release stays "Pagewing on the new UI and shell".
- **No migration from Pagewing (existing decision, `Docs/DECISIONS.md`).** No settings import, no support for old installs. PDFs edited in Pagewing are foreign files: their text boxes and signatures are not editable and redoing OCR adds a second text layer (decision "PDF markers: no compatibility with files written by Pagewing").
- **No macOS build in this subproject.** macOS is subproject 4 (milestone 3). The PDF code must not block it: no Windows- or Linux-only code outside the platform parts (printing, default-app registration, updater, installer). **(call)** The owner said "release with everything Pagewing had"; Pagewing shipped for Windows and Linux only, so the first release does too.
- Not restored: sessions beyond what Pagewing's updater did (tabs reopen after an update restart). Session restore stays in the backlog.

Anti-goal: a UI that looks like the old Pagewing. The look is direction D "Formular Neutral" with the PDF ink plum, as approved.

## 4. Constraints

- .NET 10 with Native AOT, Avalonia 12, PDFium (no V8, no XFA), Tesseract through the C API, loaded on first use. Dependency and license guardrails in `Docs/DECISIONS.md` apply to every new dependency.
- The app project depends only on `Raffica.Core` and `Raffica.Shell`; it knows no PDFium types (Pagewing's rule). PDFium runs on one dedicated thread; the UI thread never waits for it.
- Only the shell's tokens and controls; no hex literals in component styles. Every UI string exists in `Strings.en.resx` and `Strings.de.resx`.
- GPL-3.0-or-later.
- **(call) Platform code may be ported, UI code may not.** The platform implementations behind the core's interfaces (print on Windows and Linux, default-app registration, single-instance transport, the update applier) and the packaging scripts lived in `Pagewing.App`, `packaging/` and `tools/` and were not imported. They may be ported from the read-only Pagewing checkout when their milestone needs them, only the parts needed, in an unchanged-import commit followed by the adaptation commit (as in M0), translated to English and renamed. Views, view models, styles and controls of Pagewing are never ported; the Linux print dialog is rebuilt on the shell's controls.
- Reference, read-only: the Pagewing `v1.0.0` repository (never modified, never copied wholesale). Its per-feature specs hold the behaviour rules that the Raffica milestones re-use and translate into their own plans: `2026-10-24-teilprojekt2-annotieren-design.md`, `2026-10-25-m10-zeichnen-formen-design.md`, `2026-10-26-m11-formulare-design.md`, `2026-10-29-m12-sichtbare-unterschrift-design.md`, `2026-10-31-teilprojekt3-seiten-design.md`, `2026-11-01-m14-schwaerzen-design.md`, `2026-11-01-m14-1-schwaerzen-muster-fuellungen-design.md`, `2026-11-02-teilprojekt4-ocr-design.md`, `2026-11-02-m16-text-bearbeiten-design.md`, `2026-10-23-m81-automatische-updates-design.md` (all in German; the Raffica plans cite the rules they use in English).
- The PDF screens follow the approved canvas (version 9): pages pane 204 px, marks pane 256 px, comments pane 300 px, plum ink, one separated destructive group in the ribbon. Where the shell spec's "Deviations from the boards" list differs, the list wins.

## 5. Speed

Owner's decision: **measure, then set.**

- Milestone 2.1 sets the PDF budgets the way milestone 1 did: the frame baseline from `Docs/DECISIONS.md` ("Gallery frame baseline (M1.2)") plus the measured cost of opening and rendering the first page, with the limit at budget × 1.25. They are recorded in `Docs/DECISIONS.md` and checked in CI with the existing `measure.yml` machinery.
- **(call)** The budget is the measured median of the PDF app (the frame baseline already contains the frame's cost, so it is not added a second time to the frame's limit) rounded as in M1; the limit is budget × 1.25 once, as in M1.
- Raffica PDF should not be slower than Pagewing 1.0 on the same machines. Pagewing 1.0 and Chrome are measured next to it as references (Pagewing's old budgets were cold 400 ms, warm 200 ms, next file in a running instance 100 ms, 1000-page file 300 ms, to the first sharp page; they are context, not limits). **(call)** Being slower than Pagewing 1.0 is a recorded finding for the owner, like a miss of the budget; it does not by itself fail the milestone, but the plan of 2.1 includes a task to look for the cause first (for example deferring PDFium initialisation or the ribbon) before the finding is raised.
- Budgets are never loosened to make a check pass; a miss is a finding for the owner.
- The cases 2.1 must cover, because milestone 1 left them to the app specs: cold and warm start with a file, a further file in a running instance, a 1000-page file to the first page, scrolling a long document, memory with a large file open, published size. Heavy work (thumbnails, text index, OCR) starts only after the first sharp page.
- Windows and Linux are binding (the owner's machines named in the M1 decision); macOS stays indicative until subproject 4.

## 6. Rules that carry over from Pagewing

The behaviour is already built in the core; the UI must expose it correctly.
- **Saving:** incremental by default; full rewrite for redaction and page operations (with a warning when the file is digitally signed); always atomic, checked after writing; unsaved changes shown on the tab and in the status bar and asked about on close and before an update install; no autosave and no recovery file. (Note: the shell's settings dialog has an "autosave" setting from 1.2. **(call)** In the PDF app it is hidden, because autosave contradicts "never damages the original" and Pagewing decided against it; if the owner wants it, that is a scope change. The dialog builds the checkbox itself, so hiding it needs a small additive option in the shell (the app tells the dialog which rows to show). The plan of 2.1 lists this change explicitly and the roadmap records it under "Scope changes"; the frame's behaviour for the other apps stays as it is.)
- **Dirty state and closing:** **(call)** the document and tab model of 2.1 carries the "unsaved changes" state, its marker on the tab and in the status bar and the close prompt (Save, Don't save, Cancel), so the tab design does not change later; 2.1 has nothing to mark yet. The save pipeline itself and its UI are built in 2.2, together with the first command that changes a document.
- **Settings by milestone:** **(call)** 2.1 ships the dialog with theme, language and compact command bar (autosave hidden) plus the PDF settings that 2.1 needs (page appearance, default layout and zoom); the update-check switch is added in 2.6 with the updater.
- **Security:** no JavaScript, launch actions or embedded files ever run; external links open only after a click and a confirmation that shows the URL.
- **Redaction** removes content for real and ends with a full rewrite; the checks on extracted text and raw bytes stay.
- **Signature wording:** a visible signature is never called legally binding; the dialog carries the hint from Pagewing.
- **Printing:** Windows through Win32 print dialog and `FPDF_RenderPage`, Linux through CUPS with an own Avalonia dialog; always light; annotations printed; a document that forbids printing locks the command; jobs run in the background with progress and cancel.
- **Forms:** values written by PDFium's form engine; entry in Avalonia elements over the field; limited character set; no calculations without JavaScript (the app says so).
- **Text editing is limited by design:** correct existing lines and typos; no paragraph editing.

## 7. Milestones

The roadmap gets the milestones below under subproject 3 (ids 2.1 to 2.6, as milestone 1 has 1.1 and 1.2). Each milestone gets its own plan; the plan may need a short design note of its own where a rule above is not enough (the order and size are the author's call).

| # | Milestone | Contents | Acceptance (verifiable) |
|---|---|---|---|
| 2.1 | Viewer | Inventory of Pagewing 1.0's features as a checklist per milestone; app project and executable on the frame; the document and tab model with dirty state and close prompt; PDF ribbon, panes and start view for PDF; open (file argument, dialog, drag and drop, password), single instance, tabs, continuous scroll with render scheduler and tile cache, zoom modes, layouts (single, continuous, two-page), dark page, position per file, pages pane, outline pane, full-text search with highlights, select and copy text, internal and external links, recent files, command palette entries, settings that apply to PDF, printing on Windows and Linux. The PDF speed budgets are set here. | The render-regression and viewer tests pass; the budgets are recorded and `measure.yml` is within them on Windows and Linux; the app opens a file passed as argument and a second file in the running instance. |
| 2.2 | Annotate | Markup (highlight, underline, strike-through), comments with the comments pane and the marks pane, freehand, rectangle, ellipse, line, arrow, text boxes, move, resize, colour, delete, undo; the save pipeline and its UI (save, save as, signed-file warning, failure cards) through the existing core. | Tests for every tool and for save, reopen and undo; a saved file opens with the marks in an independent viewer (manual check, recorded). |
| 2.3 | Forms and signing | Fill AcroForm fields with keyboard navigation, the required-field card before saving, the visible signature (draw, import, saved signatures, placement). | Tests for each field kind and for the signature store; a filled form opens with the values in an independent viewer. |
| 2.4 | Organize pages | Reorder, rotate, delete, insert, merge, split, export as image, in the pages pane and the ribbon; the signed-file warning. | Tests on the page edit plan and its report; merge and split results open with the right page count. |
| 2.5 | Redact, OCR and line editing | The destructive group of the ribbon, redaction by selection, area and pattern with the removal checks; OCR with language management; text line editing. First task: build the Tesseract library in this repository (workflow and build scripts; the backlog entry "Build the OCR library in this repository") and drop the fetch from Pagewing's prerelease, so the installers do not depend on another repository. **(call, the owner delegated it: "your recommendations")** The CodeQL, release and hardware-measure workflows that M0 left out come back where 2.5 and 2.6 need them. | The redaction removal tests (extracted text and raw bytes) pass through the UI path; OCR makes a scanned test file searchable; text editing changes a line and keeps the rest. |
| 2.6 | Packaging and release | Windows installer and portable ZIP, Linux `.deb` and AppImage, default-PDF-app registration and the first-start card, the updater with its own signing key and the release address of this repository (the updater reads the public release page without a token, so the repository must be public by the first release; the owner decides when, nothing is changed on GitHub without that instruction), license notices, the release workflow, README and CHANGELOG for users. | Installers build in CI; an installed build runs, registers as the PDF handler and updates from one test release to the next; the first release is published on instruction of the owner only. |

Whatever is not done after 2.6 and needs the owner's machines (real Windows/Linux desktops, printers, installers) is listed as open in the roadmap with the command that checks it, as in milestone 1.2.

## 8. Left to the builder

- Project and executable names for the PDF app (consistent with `RafficaGallery` and the CLI `raffica`).
- The ribbon's tabs, groups and field codes within the frame's rules (unique codes, no prefix clashes).
- Whether the test PDF generator that Pagewing had is imported or rebuilt (backlog entry on the fixture catalogue), decided in 2.1.
- Splitting a milestone further if its plan is too large; the roadmap then gets the extra ids.
- The version number of the first release and its name are not decided here (open for 2.6).

## 9. Decided with the owner's delegation, and open

- **Updater key (call, owner delegated):** the owner generates the signing key pair and keeps the private key in a CI secret; it is never in a file, a commit, a log or a reply. Until then only the test key of the update tests exists, so "updates from one test release to the next" is proven with the test build and the real-key path is listed as open with its command.
- **Release repository (call, owner delegated):** this repository. Making it public and publishing the first release are actions only the owner can order.
- Which test machines run installers and printers is the owner's to provide; the plans mark what stays unverified.
- The first release number (Pagewing was at 1.0; the suite has no version yet).
