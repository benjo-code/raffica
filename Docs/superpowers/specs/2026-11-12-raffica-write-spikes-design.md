# Raffica Write – Spikes before the spec (milestone 4)

Status: approved by the owner on 2026-11-12 · Date: 2026-11-12 · Author: brainstorming session (M4)

Items marked **(call)** are decisions made by the author of this document, not by the owner. The owner should check them.

## 1. Intent

Milestone 4 answers the open technical questions of the suite spec (section 6, items 2 to 5) with measurements, records the layout engine decision, and then writes the Write spec with the owner. This document designs the spikes and collects what the owner has already said about Write, so that the spikes measure what Write will really have to do.

Why the order matters: a layout engine chosen on plain running text would look fast and then fail on real documents. The spikes therefore use a document that looks like the documents the owner writes (a thesis or long report).

## 2. What the owner wants from Write (input for the spec, not decided here)

Collected in the brainstorming of 2026-11-12, after a look at Word, LibreOffice Writer, ONLYOFFICE, Google Docs, iA Writer, Pages and the young Rust word processors (WordCraft, ElyOffice). The owner agreed to all seven points:

1. **DOCX is the native model.** The document model follows OOXML; parts Write does not understand are written back unchanged. (Lesson from ONLYOFFICE, whose fidelity comes from using OOXML natively, against LibreOffice, which converts to its own model.)
2. **Long documents are the measured promise.** A 300-page document opens fast and typing stays smooth, also with the outline pane open and with tracked changes. (Word lags on long documents with the Navigation pane open.)
3. **Formatting is visible.** Word's Style Area and LibreOffice's Style Inspector in one: Write answers "why does this paragraph look like that?" with the style and every direct override.
4. **Numbering and headings do not break.** The most cited pain of Word users.
5. **Two views, one document:** a calm writing view without page breaks and an exact page view for printing.
6. **Metric-compatible fonts ship with Write** (Carlito for Calibri, Caladea for Cambria) so a DOCX breaks lines the same on Linux and macOS.
7. **No AI clutter** (already an anti-goal of the suite spec).

And the owner's own pain: **page numbers that start at "1" on page 3 take a recipe in Word** (section break, unlink the footer, format page numbers, start at 1, delete the numbers before). In Write this is one action ("start numbering here at 1"); Write makes the section and the unlinked footer and shows a marker that can be removed. The DOCX holds what Word expects (a section with `w:pgNumType w:start="1"`).

Also from the owner: **finished Write prints itself** (its own Print command and dialog), the way Word does on each system, never by exporting a PDF to print elsewhere (spike 3).

The pattern behind it (author's reading, confirmed by the owner's "yes"): Word hides its structure, so simple wishes become recipes. A principle for the spec: **the user says what they want, Write builds the structure and shows it.**

Target documents: theses and long reports (confirmed). Other kinds are asked in the spec brainstorming.

From the backlog, "Ctrl+K and hyperlinks in Write" goes into the spec. ODF and legacy formats, macros and real-time collaboration stay in the backlog.

## 3. Goals, non-goals, anti-goals of this milestone

Goals:
- The four spikes run and their results (numbers, logs, what failed) are recorded in `Docs/DECISIONS.md`.
- The layout engine decision (C# or Rust core) is recorded with the rule in section 5.
- The Write spec is brainstormed with the owner, approved, linked in the roadmap, and its milestones are entered.

Non-goals:
- No Write app, no Write UI, no document model beyond what a spike needs. Spike code is throwaway.
- No change to Raffica PDF or the shell (the print spike copies the native print interop it needs into its own folder).
- No new package in the product projects. Spike projects may reference packages; every package gets a licence check (suite spec, licence guardrails) before it is added, also in a spike.

Anti-goals:
- A decision taken on a toy document (plain text, one style).
- A spike result that cannot be reproduced: every number names the command, the machine and the commit.
- Spike code that silently becomes product code. Whatever is kept later is rewritten under the Write milestones with tests.

## 4. The test document

One generated document, used by all spikes (call: generated, so it is free of licence questions, small in the repo, and every machine measures the same file).

- About 300 pages A4 when laid out with Word-like metrics.
- A cover page and a table of contents without page numbers; three sections, the first numbered page is physical page 3 and shows "1" (the owner's case).
- Headings on three levels with multi-level numbering (1, 1.1, 1.1.1), numbered and bulleted lists, about 20 tables (some across a page break), about 100 footnotes, a header and footer per section, a few images.
- Fonts: Carlito and Caladea (metric-compatible, OFL), so the same file lays out the same everywhere. Carlito is already in the repo (`third-party/carlito`, `src/Raffica.Shell/Assets/Fonts`); Caladea is added under `spikes/write/fonts/` with its OFL text after the licence check (call).
- The first numbered section stores `w:pgNumType w:start="1"` in its `w:sectPr`, with footers unlinked from the earlier sections, exactly as Word writes it.
- The generator is a `dotnet run` script under the spike folder that writes the DOCX with the Open XML SDK (it doubles as part of spike 1). The DOCX itself is committed only if it stays below 1 MB; otherwise the script is the source and the file is generated before a run.

Whether Word opens it and shows about 300 pages with "1" on page 3 is a manual check on the owner's Windows machine (Word is not in CI); Word's page count is recorded and the spike engines' page counts are compared with it (a difference is recorded, not a failure: the spikes do not aim at Word-exact pagination).

## 5. The spikes

All spike code lives under `spikes/write/<name>/` (call: a new top-level folder, outside `Raffica.slnx`, so it never enters the product build, the tests or the published size). Each spike has a short `README.md` with the command to run it and the result.

`spikes/` has its own `Directory.Build.props` and `Directory.Packages.props` that stop the repo's files from being inherited (no central package management, no warnings as errors, no repo analyzers), so a spike is self-contained and its package versions are pinned in its own project file (call). `global.json` still applies (the same .NET 10 SDK). A spike may reference `src/Raffica.Core` by project reference (it has no UI dependency); it never references `Raffica.Pdf` or `Raffica.Shell`.

The binding machines are the owner's measurement machines of `Docs/DECISIONS.md` (Windows: Ryzen 7 7800X3D; Linux: HP ProBook, Core i5-8265U, Wayland on CachyOS; macOS: the Intel Mac). For speed the **Linux laptop is binding** (the slowest), the others are recorded (call).

### Spike 1: Open XML SDK under Native AOT

Question: can a Native AOT app open, edit and save DOCX and XLSX with the Open XML SDK 3.x (MIT), and do untouched parts survive?

- A console app, published with Native AOT for `win-x64`, `linux-x64` and `osx-arm64`/`osx-x64` (CI runners are enough for the macOS build).
- Steps: open the test DOCX, change one paragraph's text and one style property, save to a new file; open an XLSX (a small generated one plus one with formulas and formatting), change one cell, save.
- Pass: no trimming or AOT warnings from the SDK (`IL2xxx`, `IL3xxx`), or each one is traced to a code path the spike shows is not used and the AOT build still runs the steps correctly; the saved files open in Word and Excel (owner, manual) and in LibreOffice; every part of the package that the spike did not touch is byte-identical after the save (compared part by part). If the SDK rewrites an untouched part, the spike records which and why.
- Plan B (call): if the SDK fails under AOT, the same steps are run with a thin own layer over `System.IO.Packaging` and `XmlReader`/`XmlWriter`, and both results are recorded. "DOCX as the native model" does not depend on the SDK; it only decides how much reading and writing code Write owns.
- Recorded: published size of the app, time to open and save the test DOCX, the SDK version.

### Spike 2: layout engine

Question: is a C# layout path fast enough for Write's promise, or does a Rust core (Parley) earn its cost?

Two candidates lay out the test document's text with the same rules (paragraphs with styles, line breaking, page breaking with headers and footers, footnotes at the page bottom, tables as simple grids; no floating objects):

- **C#:** HarfBuzzSharp for shaping and SkiaSharp for font access (both already ship with Avalonia), own line and page breaking.
- **Rust:** Parley (Apache-2.0/MIT) for shaping and line breaking, own page breaking, behind a C interface called from C# with `LibraryImport`. Built as a `cdylib` per machine (`x86_64-pc-windows-msvc`, `x86_64-unknown-linux-gnu`, `x86_64-apple-darwin`); the owner agreed on 2026-11-12 to install Rust with `rustup` on all three machines. CI does not build it.

Both candidates re-lay out **incrementally** with the same rule (call): every paragraph keeps its line breaks; an edit re-breaks only the edited paragraph, then pages are re-filled forward from its page until the page breaks match the previous layout again (footnotes and tables that cross a page break move with their page). A full re-layout per keystroke is not allowed.

The spike is headless: no window. "Drawn" means the page's glyphs are rasterized into an offscreen `SKSurface` with SkiaSharp (for both candidates; Parley returns glyph runs, which C# draws). Hit tests use each candidate's own cluster data.

Measured for each, on each machine (median of 10 runs after 2 warm-up runs):

| Metric | What | Limit (call) |
|---|---|---|
| First page | open the DOCX, lay out and draw page 1 into the offscreen surface | ≤ 100 ms on the Linux laptop |
| Full layout | the whole test document, cold process (in Write this runs in the background after the first page) | ≤ 3 s on the Linux laptop |
| Keystroke | insert one character in a paragraph on page 150 and re-lay out incrementally | ≤ 16 ms (one frame at 60 Hz) on the Linux laptop |
| Hit test | one caret/position query (point to text position and back) from C# | ≤ 50 µs per call, measured over 10,000 calls |
| Memory | working set after full layout | recorded |

**Decision rule (owner agreed 2026-11-12):** if both candidates meet every limit, **C# wins**: one language, one toolchain, no second native build on three operating systems and no extra Mach-O signing. Rust wins only if C# misses a limit that Rust meets. If neither meets the limits, the spike records why and the spec decides with the owner (for example a looser limit, or a different structure).

Also recorded: the native size each candidate adds to a published app, and whether Parley's line breaking matches HarfBuzz's for the test document (same line count per paragraph) as a sanity check.

### Spike 3: printing

Question: how does Write print itself, the way each operating system expects?

The owner's requirement (2026-11-12): finished Write prints itself, with its own Print command and dialog; printing must not go through exporting a PDF and printing it elsewhere. Decided with the owner, following what Word and the platforms do:

- **Windows: Write draws directly onto the printer, no PDF in between** (as Word does through GDI). The spike draws the laid-out pages (spike 2's C# path) into the printer's device context: glyph runs with `ExtTextOutW` and `ETO_GLYPH_INDEX` (the glyph ids from HarfBuzz, so the printed text is vector text in the embedded font), lines and table borders with GDI pens, images with `StretchDIBits`. If GDI glyph output fails, the spike tries Skia's XPS backend (`SKDocument.CreateXps`) with the Windows print document package API and records both.
- **Windows, fallback for comparison: route b.** The same pages drawn into a PDF in memory (`SKDocument.CreatePdf`) and printed the way Raffica PDF prints (PDFium into the device context). Measured only, so the spec can compare.
- **Linux and macOS: a PDF in memory, handed to CUPS.** CUPS takes PDF as its normal job format and every GTK, Qt and Cocoa app prints that way (macOS turns every print job into a PDF itself). The spike writes the pages with `SKDocument.CreatePdf` into memory and sends them as a CUPS job (`cupsCreateJob`, `cupsStartDocument`, `cupsWriteRequestData`; a temp file with `cupsPrintFile2` is acceptable if streaming fails). The native macOS print panel stays a backlog item and a question for the spec.

The spike is a small Avalonia window with a Print button (it needs a window for the Windows system dialog); it copies the native print interop it needs (`PrintDialogNative`, `Gdi`, `DevMode`, `CupsNative`) from `src/Raffica.Pdf/Platform/*` into the spike, never references `Raffica.Pdf`, and changes nothing there. On Linux and macOS a plain list of the CUPS printers (`cupsGetDests`) stands in for Write's own print dialog, which the spec designs.

- Pass: the whole test document prints from the spike window on a real printer on Windows (route a), Linux and macOS; the text is vector text, checked by printing route a to "Microsoft Print to PDF" and finding the text selectable in the result, and on CUPS by the PDF handed over; pages, margins and page numbers match the layout.
- Recorded per system and route: time from Print to the job being handed to the spooler, the job size, and for route b on Windows the same, so the spec can compare a and b.
- How Write later shares print code with Raffica PDF (for example moving the platform print services out of `Raffica.Pdf` into a shared project) is a question for the spec.

### Spike 4: IME

Question: does composition (preedit) work in a custom text control, which is what Write's editor will be (not Avalonia's `TextBox`)?

- A small Avalonia 12 window with a custom control that implements Avalonia's text input client (`TextInputMethodClient`), draws its own text and the preedit, and reports the caret rectangle so the candidate window sits at the caret.
- Manual checks on the owner's machines: Windows with the Microsoft Japanese and Chinese (Pinyin) IMEs; Linux with Fcitx5 and IBus, both under Wayland and under an X11 session (the owner agreed on 2026-11-12 to install whichever of the two is missing and to log into an X11 session for the test); macOS with the Japanese input source. Each case: type a composed word, move the candidate selection, commit, cancel with Esc; the candidate window sits at the caret.
- The same checks in a `TextBox` are the control: a failure in both is Avalonia's, a failure only in the custom control is ours.
- Recorded per case: works, works with a flaw (named), fails. Avalonia uses IMM32 on Windows, not TSF; whether that matters is part of the result.

The roadmap names Windows and Linux for printing and IME; macOS has been a supported platform since milestone 3, so the spikes cover it too and the roadmap task text is widened through a "Scope changes" entry (call). The macOS checks are required, as the owner's Intel Mac is available (it ran the M3 checks).

## 6. After the spikes

1. The results go into `Docs/DECISIONS.md` (one entry per spike, plus the layout engine decision with the rule and the numbers).
2. The Write spec is brainstormed with the owner, starting from section 2 of this document, and written to `Docs/superpowers/specs/YYYY-MM-DD-raffica-write-design.md`. The spec splits Write into milestones (4.1, 4.2, …), which are entered in the roadmap.
3. The roadmap ticks the tasks and the two acceptance criteria of milestone 4 with their proof.

## 7. Constraints

- Everything in English. GPL-3.0-or-later; every package gets a licence check first (Parley, Open XML SDK, HarfBuzzSharp and SkiaSharp are MIT or Apache-2.0).
- The product build, tests and format check stay green and untouched by the spikes (`spikes/` is outside `Raffica.slnx`). `dotnet format` is not run on the spike folder (call).
- CI is not changed for the spikes (call); the Rust build runs on the owner's machines (Rust installed with `rustup`, owner's consent of 2026-11-12). If the macOS AOT build of spike 1 needs CI, a one-off workflow run on a branch is enough.
- Real printers, IMEs and Word are only on the owner's machines; those checks are manual and their results are recorded with date and machine.

## 8. Left to the builder

- The exact structure of each spike project, the generator code, how the timings are taken (Stopwatch, BenchmarkDotNet where it fits).
- How "re-lay out what changed" is done in each candidate, as long as both use the same rule.
- The XLSX samples of spike 1.
- The order of the spikes (spike 1 first is sensible, because the generator needs it).
