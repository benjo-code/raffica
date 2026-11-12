# Milestone 4 – Spikes & spec for Raffica Write – Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The four spikes have run on the owner's machines, their results and the layout engine decision are in `Docs/DECISIONS.md`, and the Write spec is approved, linked and split into milestones.

**Architecture (one call of this plan: the page breaker is shared C#, so "own page breaking" of the spec is written once and the incremental rule is the same for both by construction):** Throwaway spike projects under `spikes/write/`, outside `Raffica.slnx` and isolated from the repo's build settings. One generated test document (a 300-page thesis) feeds every spike. The layout spike shares one C# document reader, page breaker and benchmark harness between two paragraph-layout candidates (C# with HarfBuzzSharp, Rust with Parley behind a C interface), so the candidates differ only in the text layer and the boundary cost. Printing and IME are small Avalonia windows checked by hand on the owner's machines.

**Tech Stack:** .NET 10 SDK (`global.json`), Native AOT, DocumentFormat.OpenXml 3.x (MIT), SkiaSharp 3.119.4 and HarfBuzzSharp 8.3.1.3 (MIT, the versions Avalonia 12.1.5 brings), Avalonia 12.1.5, Rust stable with `rustup` and Parley (Apache-2.0/MIT), GDI and CUPS interop copied from `src/Raffica.Pdf/Platform/*`.

**Spec:** `Docs/superpowers/specs/2026-11-12-raffica-write-spikes-design.md` (approved by the owner on 2026-11-12). Read it first; section numbers below refer to it.

## Global Constraints

- Everything in English: code, comments, READMEs, commits, roadmap texts, decision entries.
- Spike code is throwaway: it lives only under `spikes/write/`, is never referenced by `Raffica.slnx` or any product project, and nothing in `src/`, `tests/`, `bench/`, `tools/` or `.github/` changes for a spike.
- `spikes/Directory.Build.props` and `spikes/Directory.Packages.props` stop the repo's files from being inherited (no central package management, no warnings as errors, no repo analyzers); every spike project pins its package versions itself. `global.json` still applies.
- A spike may reference `src/Raffica.Core/Raffica.Core.csproj` by project reference; it never references `Raffica.Pdf` or `Raffica.Shell`. Native print interop is copied into the spike, not referenced.
- Every package or font gets a licence check before it is added (suite spec, "License guardrails"): allowed MIT, Apache-2.0, BSD, OFL for fonts. The check and its result are one line in the spike's `README.md`.
- Fonts for all layout and print runs are Carlito (`third-party/carlito`) and Caladea (`spikes/write/fonts/`). No system font fallback: a run that cannot load them fails.
- Limits of spike 2 (spec section 5, binding on the Linux laptop): first page ≤ 100 ms, full layout ≤ 3 s, keystroke ≤ 16 ms, hit test ≤ 50 µs per call; memory recorded. Median of 10 runs after 2 warm-up runs.
- Decision rule (spec section 5, owner agreed): if both candidates meet every limit, C# wins; Rust wins only if C# misses a limit that Rust meets; if neither meets them, the spec decides with the owner.
- Every recorded number names the command, the machine (the binding machines of `Docs/DECISIONS.md`: Windows Ryzen 7 7800X3D; Linux HP ProBook i5-8265U, CachyOS, Wayland; the owner's Intel Mac) and the commit.
- Steps marked **Owner** run on the owner's machines or need the owner's hands (Word, Excel, real printers, IMEs, Rust install on the laptop and the Mac). The executor prepares the command and the checklist, asks the owner, and records the answer; it never invents a result. Installing Rust on the executor's Windows machine also needs the owner's go at execution time.
- Commit with the GitHub noreply address only; Conventional Commits; stage files by name (never `git add .` or `-A`); check `git show --stat HEAD` after each commit. No force push, no `reset --hard`, no `--no-verify`. The PR goes to `dev`; Claude merges it once all CI checks are green.
- `dotnet format` is not run on `spikes/`. "Done" for the milestone = `dotnet build Raffica.slnx -c Release`, the test command of `CLAUDE.md`, `dotnet format Raffica.slnx --verify-no-changes` and `dotnet run tools/roadmap.cs -- check` green, with commands and results named.
- No `CHANGELOG.md` entry (nothing ships to users).

## Review Focus

- **Incremental re-layout that differs from a full layout** would fake the keystroke number. After every timed insert the harness lays the same document out from scratch and compares the page breaks (block index and line index of every page start); a mismatch fails the run. Pinned in Task 4, step 1 (`IncrementalEqualsFull`).
- **A measurement of the wrong build:** a JIT or Debug C# harness, or a Rust `debug` profile, would make C# look slower or Rust look slower. The harness refuses to time unless `RuntimeFeature.IsDynamicCodeSupported` is false and the Rust library reports `release` from `pc_build_profile()`. Pinned in Task 4, step 5 and Task 5, step 1.
- **A silent font fallback** changes every metric. The harness loads Carlito and Caladea from files and fails when a run's font is missing or a glyph maps to `.notdef` for the test document's text. Pinned in Task 4, step 1 (`FontsLoadFromSpikeFolder`).
- **"Untouched part" compared at the wrong level:** zip entries recompress differently even when the part is unchanged. `openxml-aot compare` compares the decompressed bytes of each part, and lists added, removed and changed parts separately. Pinned in Task 3, step 1.
- **Print route a that is not vector text:** GDI falls back to bitmaps when the font is not selected into the device context. Route a is printed to "Microsoft Print to PDF" and the text must be selectable there. Pinned in Task 7, step 4.

---

### Task 1: Plan, design and roadmap on the branch

**Files:**
- Add: `Docs/superpowers/specs/2026-11-12-raffica-write-spikes-design.md`, `Docs/superpowers/plans/2026-11-12-milestone-04-write-spikes-spec.md`
- Modify: `Docs/roadmap.json` (subproject 5, milestone `4`; `scopeChanges`)
- Regenerate: `Docs/ROADMAP.md`

- [x] **Step 1:** Milestone `4`: add `"plan": "superpowers/plans/2026-11-12-milestone-04-write-spikes-spec.md"` and `"spec": "superpowers/specs/2026-11-12-raffica-write-spikes-design.md"`, set `"status": "planned"`. Change the text of the third task to `Spike: printing per operating system (Write prints itself) and IME behaviour on Windows, Linux and macOS`. Leave subproject 5's `spec` as it is (the Write spec does not exist yet).
- [x] **Step 2:** Append to `scopeChanges`: `{ "date": "2026-11-12", "context": "Milestone 4", "text": "The printing and IME spikes also cover macOS (a supported platform since milestone 3), and printing is checked as Write printing itself (Windows draws directly onto the printer, Linux and macOS hand CUPS a PDF in memory), not through Raffica PDF; see the spike design `superpowers/specs/2026-11-12-raffica-write-spikes-design.md`." }`
- [x] **Step 3:** `dotnet run tools/roadmap.cs -- generate`, then `dotnet run tools/roadmap.cs -- check`. Expected: check passes.
- [ ] **Step 4: Commit** (only after the owner has reviewed this plan)

```bash
git add Docs/superpowers/specs/2026-11-12-raffica-write-spikes-design.md Docs/superpowers/plans/2026-11-12-milestone-04-write-spikes-spec.md Docs/roadmap.json Docs/ROADMAP.md
git commit -m "docs: plan milestone 4 (Write spikes and spec)"
git show --stat HEAD
```
Expected: the four files only.

- [ ] **Step 5:** When execution starts, set milestone `4` to `"status": "in-progress"`, regenerate, check, commit `docs: start milestone 4` with `Docs/roadmap.json` and `Docs/ROADMAP.md`.

### Task 2: Spike folder and the test document

**Files:**
- Create: `spikes/Directory.Build.props`, `spikes/Directory.Packages.props`, `spikes/write/README.md`, `spikes/write/fonts/Caladea-{Regular,Bold,Italic,BoldItalic}.ttf`, `spikes/write/fonts/OFL-Caladea.txt`, `spikes/write/testdoc/TestDoc.csproj`, `spikes/write/testdoc/*.cs`, `spikes/write/testdoc/README.md`
- Output (not committed unless < 1 MB): `artifacts/spikes/write/thesis.docx`

**Interfaces:**
- Produces: the CLI `dotnet run --project spikes/write/testdoc -c Release -- generate --out <path>` and `-- verify <path>` (exit 0 = the document has the shape below); the file `artifacts/spikes/write/thesis.docx` that every later task reads; `spikes/write/fonts/` with Caladea; the constant font paths `third-party/carlito/*.ttf` and `spikes/write/fonts/*.ttf`.

- [ ] **Step 1:** Write `spikes/Directory.Build.props` (`<Project>` with `TargetFramework` `net10.0`, `Nullable` enable, `ImplicitUsings` enable, nothing imported from the root) and `spikes/Directory.Packages.props` (`ManagePackageVersionsCentrally` false). Check: `dotnet build Raffica.slnx -c Release` is still green (the solution does not see `spikes/`).
- [ ] **Step 2:** Licence check for Caladea: download the four TTFs and `OFL.txt` from the Google Fonts repository (`github.com/google/fonts`, folder `ofl/caladea`), confirm SIL OFL 1.1, copy them into `spikes/write/fonts/` (`OFL.txt` as `OFL-Caladea.txt`), and note source URL, commit and licence in `spikes/write/README.md`.
- [ ] **Step 3:** Write the `verify` command first. It opens the DOCX with the Open XML SDK (DocumentFormat.OpenXml 3.x, latest stable, pinned in `TestDoc.csproj`) and fails with a message naming the first broken rule:
  - exactly 3 `w:sectPr`; section 1 (cover page, then table of contents on its own page) has no page-number field in any footer; section 2 has `w:pgNumType w:start="1"` and its own `w:footerReference` (not inherited) with a `PAGE` field; section 3 (appendix) has `w:pgNumType w:start="1"` (decimal) and its own footer with the text `A-` before the `PAGE` field;
  - headings use styles `Heading1` to `Heading3`, each linked to one multi-level `w:abstractNum` (levels 1, 1.1, 1.1.1); at least 30 `Heading1`, 1 numbered and 1 bulleted list per chapter;
  - exactly 20 tables, of which at least 5 have 40 rows or more (so they cross a page break); exactly 100 footnotes; at least 3 images (`a:blip`), each under 100 KB;
  - every run font is Carlito or Caladea (theme fonts resolved), body text Caladea 11 pt, headings Carlito;
  - a `TOC` field in section 1.
- [ ] **Step 4:** Run `verify` on a hand-made empty DOCX (create it in the test with the SDK). Expected: exit 1, message about the section count.
- [ ] **Step 5:** Implement `generate --out <path>`: deterministic text (seeded generator of Latin-like words; no network, no randomness without the fixed seed 4242), about 105,000 words of body text so that A4 with 2.5 cm margins gives about 300 pages, the structure of step 3, images generated as small PNGs by SkiaSharp. The output is byte-identical for the same version (fixed timestamps in the package properties).
- [ ] **Step 6:** `generate` then `verify` on the result. Expected: exit 0. Run `generate` twice and compare SHA-256. Expected: equal.
- [ ] **Step 7: Owner (Windows, Word):** open `thesis.docx` in Word, update the table of contents, note the page count and confirm that physical page 3 shows "1" and the appendix shows "A-1". Record both in `spikes/write/testdoc/README.md` with Word's version.
- [ ] **Step 8: Commit** `feat(spikes): test document generator for the Write spikes` with the files above. `artifacts/` is ignored by git; if `thesis.docx` is under 1 MB, copy it to `spikes/write/testdoc/thesis.docx` and commit it too, otherwise the generator is the source.

### Task 3: Spike 1 – Open XML SDK under Native AOT

**Files:**
- Create: `spikes/write/openxml-aot/OpenXmlAot.csproj` (`PublishAot` true, `TrimmerSingleWarn` false so every warning is listed), `spikes/write/openxml-aot/*.cs`, `spikes/write/openxml-aot/README.md`

**Interfaces:**
- Consumes: `thesis.docx` (Task 2).
- Produces: the CLI `openxml-aot docx <in> <out>`, `xlsx-make <out-plain> <out-formulas>`, `xlsx <in> <out>`, `compare <before> <after> --touched <part>[,<part>...]`.

- [ ] **Step 1:** Write `compare` first: open both files as zip packages, read every part's decompressed bytes, print `added`, `removed`, `changed` part lists; exit 1 if a part not named in `--touched` is changed, added or removed. Check it by comparing `thesis.docx` with itself (expected: exit 0, three empty lists) and with a copy in which the test changes `word/styles.xml` through `System.IO.Compression` (expected: exit 1 without `--touched /word/styles.xml`, exit 0 with it).
- [ ] **Step 2:** `docx <in> <out>`: copy `in` to `out`, open `out` editable, change the text of the first body paragraph of chapter 1 to `Edited by the spike.` and the size of style `Heading2` to 15 pt, save. `xlsx-make`: one plain sheet (1,000 rows × 10 columns of numbers and text) and one with formulas (`SUM`, `VLOOKUP`, a cross-sheet reference), number formats, a merged cell and conditional formatting. `xlsx <in> <out>`: change cell `B2` of the first sheet to `42`, save.
- [ ] **Step 3:** `dotnet publish spikes/write/openxml-aot -c Release -r win-x64` and record every `IL2xxx`/`IL3xxx` warning with its source. Run the published exe for `docx`, `xlsx-make`, `xlsx`, then `compare` with `--touched /word/document.xml,/word/styles.xml` (DOCX) and `--touched /xl/worksheets/sheet1.xml` (XLSX; add `/xl/calcChain.xml` only if the SDK touches it, and record that). Expected: exit 0 everywhere, or the README records which untouched part changed and why.
- [ ] **Step 4:** Record in the README: SDK version, published size (`win-x64`), the median of 10 runs of `docx` on `thesis.docx` (open + edit + save), the warning list with an explanation per warning (an unused code path shown, or a failure).
- [ ] **Step 5: Owner (Linux laptop, Intel Mac):** the same publish and run with `-r linux-x64` and `-r osx-x64`; the executor gives the exact commands in the README; record the results.
- [ ] **Step 6: Owner (Windows):** open the saved DOCX in Word and the saved XLSX files in Excel; executor or owner: open them in LibreOffice (`soffice --headless --convert-to pdf` is enough as an open check on Linux). Record: opens without a repair prompt, the edit is visible.
- [ ] **Step 7 (only if steps 3 to 6 fail under AOT):** Plan B (spec section 5): the same `docx` and `xlsx` commands with an own thin layer over `System.IO.Packaging` and `XmlReader`/`XmlWriter` in `spikes/write/openxml-aot/PlanB/`; run steps 3 to 6 for it and record both.
- [ ] **Step 8: Commit** `feat(spikes): Open XML SDK under Native AOT` with the spike folder.

### Task 4: Spike 2, shared part and the C# candidate

**Files:**
- Create: `spikes/write/layout/Layout.Common/` (`DocModel.cs`, `DocxReader.cs`, `PageBreaker.cs`, `PageRenderer.cs`, `IParagraphLayouter.cs`, `FontSet.cs`), `spikes/write/layout/Layout.CSharp/` (`HarfBuzzParagraphLayouter.cs`), `spikes/write/layout/LayoutBench/` (`LayoutBench.csproj` with `PublishAot` true, `Program.cs`, `Checks.cs`), `spikes/write/layout/README.md`

**Interfaces:**
- Consumes: `thesis.docx`, the fonts (Task 2).
- Produces (used by Tasks 5 and 7):

```csharp
// Layout.Common
public sealed record DocModel(IReadOnlyList<Section> Sections, IReadOnlyList<Block> Blocks, IReadOnlyList<Footnote> Footnotes);
public abstract record Block;                              // Paragraph or Table
public sealed record Paragraph(string Text, IReadOnlyList<TextRun> Runs, ParagraphFormat Format, IReadOnlyList<int> FootnoteRefs) : Block;
public sealed record Table(IReadOnlyList<IReadOnlyList<Paragraph>> Rows, IReadOnlyList<float> ColumnWidths) : Block;
public static class DocxReader { public static DocModel Read(string path); }
public sealed class FontSet : IDisposable { public static FontSet LoadSpikeFonts(string repoRoot); /* throws when a file is missing */ }

public interface IParagraphLayouter : IDisposable { IParagraphLayout Layout(Paragraph paragraph, float width); }
public interface IParagraphLayout : IDisposable
{
    IReadOnlyList<LineMetrics> Lines { get; }
    int HitTest(float x, float y);                  // text offset in the paragraph
    RectF Caret(int offset);
    void Draw(SKCanvas canvas, float x, float y, int firstLine, int lineCount);
}
public readonly record struct LineMetrics(int Start, int Length, float Ascent, float Descent, float Width);

public sealed class PageBreaker
{
    public PageBreaker(DocModel doc, IParagraphLayouter layouter, PageSetup setup);
    public int LayoutUntil(int pageIndex);           // lays out until the page exists; returns the page count so far
    public int LayoutAll();
    public void InsertText(int blockIndex, int offset, string text);   // re-breaks the paragraph, re-fills forward until the page starts converge
    public IReadOnlyList<PageStart> PageStarts { get; } // (BlockIndex, LineIndex) of every page
    public TextPosition HitTest(int page, float x, float y);
    public RectF Caret(TextPosition position);
}
public static class PageRenderer { public static void Draw(PageBreaker layout, int page, SKCanvas canvas); }
```

- [ ] **Step 1:** Write the checks in `LayoutBench/Checks.cs`, run by `layout-bench check --engine <csharp|rust>` (exit 1 on the first failure):
  - `FontsLoadFromSpikeFolder`: `FontSet.LoadSpikeFonts` loads the 8 files; every character of the test document's text maps to a glyph other than `.notdef`.
  - `ReaderSeesTheShape`: `DocxReader.Read(thesis.docx)` gives 3 sections, 20 tables, 100 footnotes.
  - `IncrementalEqualsFull`: for 20 seeded positions, `InsertText` on one `PageBreaker`, then a fresh `PageBreaker` on the edited model with `LayoutAll`; `PageStarts` are equal.
  - `HitTestRoundTrip`: for 1,000 seeded points inside text lines, `Caret(HitTest(p))` lies on the same line and within one glyph advance of `p.X`.
  - `PageNumbers`: page 3 renders the footer text `1`, the first appendix page renders `A-1` (checked on the footer text the renderer emits, not on pixels).
- [ ] **Step 2:** `layout-bench check --engine csharp`. Expected: fails (nothing implemented).
- [ ] **Step 3:** Implement `DocxReader` (Open XML SDK; styles resolved to `ParagraphFormat` and `TextRun` font, size, bold, italic), `FontSet`, `PageBreaker` and `PageRenderer`. Page breaking rules (the same for both candidates, spec section 5): A4, margins 2.5 cm, header and footer per section with the `PAGE` field; widow and orphan control off; a table breaks between rows; a footnote goes to the bottom of the page whose line holds its reference, its space reserved when that line is placed; no floating objects, images inline. Incremental rule: every paragraph keeps its `IParagraphLayout`; `InsertText` re-lays out only that paragraph, then re-fills pages from its page forward and stops once a page starts at the same (block, line) as before the edit with an unchanged footnote set.
- [ ] **Step 4:** Implement `HarfBuzzParagraphLayouter`: HarfBuzzSharp shaping per run, line breaking at UAX #14 opportunities from Avalonia's `Avalonia.Media.TextFormatting.Unicode.LineBreakEnumerator` (reference `Avalonia` 12.1.5; if the type is not public in 12.1.5, break at spaces and after hyphens and record that as a handicap in the README), glyph drawing through `SKCanvas.DrawText` with an `SKTextBlob` of glyph ids. Run `layout-bench check --engine csharp`. Expected: exit 0.
- [ ] **Step 5:** Implement `layout-bench run --engine <e> --doc <path> --runs 10 --warmup 2 --out <csv>`. It refuses to run (exit 2, message) unless `RuntimeFeature.IsDynamicCodeSupported` is false (a Native AOT publish). Metrics (spec section 5 table): `first-page` and `full-layout` each in a fresh child process (`layout-bench child <metric> ...`, timed from process start to the point the child reports via stdout), `first-page` includes reading the DOCX and drawing page 1 into a 794 × 1123 `SKSurface`; `keystroke` = `InsertText` of one character at a seeded offset in a paragraph on page 150 (median over 200 inserts, after `LayoutAll`); `hit-test` = 10,000 `HitTest` + `Caret` pairs, per call; `memory` = working set after `LayoutAll`. CSV columns: `date,machine,os,engine,metric,median,min,max,unit,commit`.
- [ ] **Step 6:** Publish (`dotnet publish spikes/write/layout/LayoutBench -c Release -r win-x64`), run `check` and `run --engine csharp` on Windows, keep the CSV under `spikes/write/layout/results/`. Expected: a CSV with five metrics.
- [ ] **Step 7: Commit** `feat(spikes): layout spike, shared part and C# candidate` with the layout folder and the Windows CSV.

### Task 5: Spike 2, the Rust candidate (Parley)

**Files:**
- Create: `spikes/write/layout/parley-core/Cargo.toml` (`crate-type = ["cdylib"]`, Parley latest release pinned, `Cargo.lock` committed, `[profile.release]` with `lto = true`), `spikes/write/layout/parley-core/src/lib.rs`, `spikes/write/layout/Layout.Rust/RustParagraphLayouter.cs`, `spikes/write/layout/Layout.Rust/ParleyNative.cs`

**Interfaces:**
- Consumes: `IParagraphLayouter`, `IParagraphLayout`, `LineMetrics`, `Paragraph` (Task 4).
- Produces: `RustParagraphLayouter : IParagraphLayouter`, selected by `--engine rust`. The C interface (all `extern "C"`, UTF-16 text, `#[repr(C)]` structs, status `0` = ok):

```c
const char* pc_build_profile(void);                       // "release" or "debug"
PcCtx*  pc_ctx_create(void);
int32_t pc_ctx_add_font(PcCtx*, const uint8_t* data, size_t len, uint32_t* font_id);
PcPara* pc_para_layout(PcCtx*, const uint16_t* text, size_t len, const PcRun* runs, size_t run_count, float width);
size_t  pc_para_lines(const PcPara*, PcLine* out, size_t cap);   // returns the line count
int32_t pc_para_hit_test(const PcPara*, float x, float y, uint32_t* offset);
int32_t pc_para_caret(const PcPara*, uint32_t offset, PcRect* out);
size_t  pc_para_glyphs(const PcPara*, uint32_t first_line, uint32_t line_count, PcGlyph* out, size_t cap); // font_id, glyph_id, x, y, size
void    pc_para_free(PcPara*);
void    pc_ctx_free(PcCtx*);
```

- [ ] **Step 1:** `ParleyNative.cs` with `LibraryImport` for the functions above; `RustParagraphLayouter` throws at construction when `pc_build_profile()` is not `release`. Run `layout-bench check --engine rust`. Expected: fails (library missing).
- [ ] **Step 2:** **Owner go needed:** install Rust with `rustup` (stable) on the Windows machine. Implement `lib.rs` with Parley (fonts registered from the bytes C# passes, no system fonts), `cargo build --release`; the `LayoutBench` project copies the `cdylib` next to the published exe.
- [ ] **Step 3:** `layout-bench check --engine rust`. Expected: exit 0 (the same five checks as C#; `IncrementalEqualsFull` and `PageNumbers` run through the shared `PageBreaker`).
- [ ] **Step 4:** Add a `lines` report: `layout-bench compare-lines` writes the line count per paragraph for both engines and the number of paragraphs that differ (spec: sanity check, recorded, not a failure).
- [ ] **Step 5:** `layout-bench run --engine rust` on Windows, CSV under `results/`. Record the size of the `cdylib` per OS (later steps add Linux and macOS).
- [ ] **Step 6: Commit** `feat(spikes): layout spike, Rust candidate with Parley` with the Rust and C# files, `Cargo.lock` and the CSV (never `target/`; add `spikes/**/target/` to `.gitignore` in this commit).

### Task 6: Spike 2 on the binding machines and the engine decision

**Files:**
- Modify: `spikes/write/layout/README.md`, `spikes/write/layout/results/*.csv`
- Modify: `Docs/DECISIONS.md` (new entry on top)

- [ ] **Step 1:** Write the run instructions into the README per OS: install `rustup`, `cargo build --release`, `dotnet publish ... -r linux-x64` / `-r osx-x64`, `layout-bench check` for both engines, `layout-bench run` for both, where the CSV goes. Mains power and the balanced profile, as in the measurement method of `Docs/DECISIONS.md`.
- [ ] **Step 2: Owner (Linux laptop, binding):** run the instructions; hand back the CSVs and the `compare-lines` report.
- [ ] **Step 3: Owner (Intel Mac):** the same; hand back the CSVs.
- [ ] **Step 4:** Put all CSVs under `results/` and a summary table (engine × metric × machine, limit, verdict) in the README. Apply the decision rule to the Linux laptop's numbers.
- [ ] **Step 5:** New entry on top of `Docs/DECISIONS.md`: `## <date> – Raffica Write: the layout engine (M4)` with Context, Decision (C# or Rust, by the rule), the Linux laptop table, the other machines, the line-count comparison, the native size per OS, the commit and commands, and what a full Rust core (model and layout in Rust, coarser calls) would change compared with the measured per-paragraph boundary.
- [ ] **Step 6: Commit** `docs: layout engine decision for Write (M4)` with the README, the CSVs and `Docs/DECISIONS.md`.

### Task 7: Spike 3 – Write prints itself

**Files:**
- Create: `spikes/write/print/PrintSpike.csproj` (Avalonia 12.1.5, references `Layout.Common`, `Layout.CSharp` and `src/Raffica.Core/Raffica.Core.csproj`), `spikes/write/print/MainWindow.cs`, `spikes/write/print/GdiPagePrinter.cs`, `spikes/write/print/PdfRoute.cs`, `spikes/write/print/CupsJob.cs`, `spikes/write/print/Interop/*.cs` (copied from `src/Raffica.Pdf/Platform/Windows/PrintDialogNative.cs`, `Gdi.cs`, `DevMode.cs` and `src/Raffica.Pdf/Platform/Linux/CupsNative.cs`, each with a header comment naming the source file), `spikes/write/print/README.md`

**Interfaces:**
- Consumes: `PageBreaker`, `PageRenderer`, `IParagraphLayout` glyph data (Task 4); Core's `PdfDocument` for route b.
- Produces: a window with the buttons `Print (route a)`, `Print (route b, PDF)` on Windows and `Print` plus a printer list on Linux and macOS.

- [ ] **Step 1:** `GdiPagePrinter.Print(PageBreaker layout, nint printerDc)`: `StartDoc`/`StartPage`/`EndPage`/`EndDoc`; per glyph run `CreateFontIndirectW` for the run's font (Carlito or Caladea, added for the process with `AddFontMemResourceEx` from the spike font files) and `ExtTextOutW` with `ETO_GLYPH_INDEX` and the glyph ids and advances from the layout; table borders with GDI pens; images with `StretchDIBits`. The device context comes from the system dialog of the copied `PrintDialogNative`. If GDI glyph output cannot give vector text (step 4 fails), add route a2: `SKDocument.CreateXps` into memory, submitted with the Windows print document package API (`IPrintDocumentPackageTarget`), and record both.
- [ ] **Step 2:** `PdfRoute.Render(PageBreaker layout) -> MemoryStream`: `SKDocument.CreatePdf` into memory, one `SKCanvas` page per layout page through `PageRenderer`. On Windows, route b opens that stream as Core's `PdfDocument` and prints it into the device context the way `WindowsPrintService` does (PDFium page rendering per page).
- [ ] **Step 3:** `CupsJob.Print(string printer, Stream pdf, string title)`: `cupsCreateJob`, `cupsStartDocument` with format `application/pdf`, `cupsWriteRequestData` in 64 KiB chunks, `cupsFinishDocument`; if streaming fails on a system, fall back to a temp file and `cupsPrintFile2` and record why. The printer list comes from `cupsGetDests`.
- [ ] **Step 4:** On Windows print route a to "Microsoft Print to PDF", open the result in an independent viewer (Edge) and check that the text is selectable and the page count, margins and page numbers ("1" on page 3, "A-1" in the appendix) match the layout. Do the same for route b. Record time from Print to the job being handed to the spooler, and the spool size (from the PDF file size and the spooler's job size).
- [ ] **Step 5: Owner (Windows, real printer):** route a on the owner's printer; check sharp text, page numbers. Record.
- [ ] **Step 6: Owner (Linux laptop, Intel Mac, real printer):** run the spike, choose the printer, print; check sharp text and page numbers. Record time and job size.
- [ ] **Step 7:** New entry on top of `Docs/DECISIONS.md`: `## <date> – Raffica Write: printing per operating system (M4)`: Windows draws onto the printer (route a), Linux and macOS hand CUPS a PDF in memory, the numbers of a and b, what failed and the fallback used, and the open questions for the spec (shared print project with Raffica PDF, Write's print dialog, native macOS print panel from the backlog).
- [ ] **Step 8: Commit** `feat(spikes): Write prints itself (GDI on Windows, PDF to CUPS)` with the print folder and `Docs/DECISIONS.md`.

### Task 8: Spike 4 – IME in a custom text control

**Files:**
- Create: `spikes/write/ime/ImeSpike.csproj` (Avalonia 12.1.5, Avalonia.Desktop 12.1.5), `spikes/write/ime/ComposeBox.cs`, `spikes/write/ime/MainWindow.cs`, `spikes/write/ime/CHECKLIST.md`, `spikes/write/ime/README.md`

**Interfaces:**
- Produces: a window with a `ComposeBox` (custom control), a `TextBox` (the control group) and an event log (preedit text, cursor position, commits, caret rectangle sent).

- [ ] **Step 1:** `ComposeBox : Control` draws its own text with `TextLayout` and handles `TextInputMethodClientRequested` by returning a `TextInputMethodClient` subclass that implements `SetPreeditText` (with the preedit cursor), `CursorRectangle` (the caret rectangle in the control's coordinates), `TextViewVisual`, `SurroundingText` and `SupportsPreedit` / `SupportsSurroundingText`; `TextInput` commits. Check the member names against the Avalonia 12.1.5 source before writing; record any difference in the README.
- [ ] **Step 2:** Write `CHECKLIST.md`: one table per system with the rows Windows (Microsoft Japanese IME, Microsoft Pinyin), Linux Wayland (Fcitx5 with Mozc and Pinyin, IBus with Anthy or Mozc and Pinyin), Linux X11 session (the same), macOS (Japanese – Romaji). Each row checks, in `ComposeBox` and in the `TextBox`: compose a word, move the candidate selection, commit with Enter, cancel with Esc, the candidate window sits at the caret, Backspace inside the preedit. Result per cell: works, flaw (named), fails.
- [ ] **Step 3:** Run the spike on Windows with both IMEs and fill the Windows table (the executor can do this on the Windows machine when the IMEs are installed; otherwise **Owner**).
- [ ] **Step 4: Owner (Linux laptop):** install the missing one of Fcitx5 and IBus (the owner agreed), run under Wayland and in an X11 session, fill the tables.
- [ ] **Step 5: Owner (Intel Mac):** fill the macOS table.
- [ ] **Step 6:** New entry on top of `Docs/DECISIONS.md`: `## <date> – Raffica Write: IME in a custom text control (M4)`: the tables in short, Avalonia's failures (in both controls) versus ours (only `ComposeBox`), IMM32 versus TSF on Windows if it showed, workarounds.
- [ ] **Step 7: Commit** `feat(spikes): IME in a custom text control` with the IME folder and `Docs/DECISIONS.md`.

### Task 9: Results into the roadmap and the project memory

**Files:**
- Modify: `Docs/DECISIONS.md` (entry for spike 1), `Docs/roadmap.json`, `Docs/ROADMAP.md`, `CLAUDE.md` (section "Project-specific"), `spikes/write/README.md`

- [ ] **Step 1:** New entry on top of `Docs/DECISIONS.md`: `## <date> – Raffica Write: Open XML SDK under Native AOT (M4)` with the results of Task 3 (versions, sizes, warnings, untouched parts, Word/Excel/LibreOffice, Plan B if it ran).
- [ ] **Step 2:** `spikes/write/README.md`: an index of the four spikes with one line of result each and links to their READMEs and decision entries.
- [ ] **Step 3:** `CLAUDE.md`, "Project-specific": a short "Write spikes (M4)" bullet: `spikes/write/` holds the throwaway spikes outside `Raffica.slnx`, isolated by `spikes/Directory.Build.props` and `spikes/Directory.Packages.props`; the test document comes from `spikes/write/testdoc`; the decisions are the four M4 entries in `Docs/DECISIONS.md`; spike code never becomes product code.
- [ ] **Step 4:** Roadmap milestone `4`: tick the three spike tasks with `evidence` naming the decision entries; tick the acceptance criterion "The spike results are recorded and the layout engine decision is in `Docs/DECISIONS.md`" with the entry titles as evidence. Generate and check.
- [ ] **Step 5: Commit** `docs: record the Write spike results (M4)` with the files above.

### Task 10: The Write spec

**Files:**
- Create: `Docs/superpowers/specs/<date>-raffica-write-design.md`
- Modify: `Docs/roadmap.json` (subproject 5 `spec`, milestone `4`, new milestones `4.1` …), `Docs/ROADMAP.md`, `Docs/DECISIONS.md` (lasting decisions of the spec)

- [ ] **Step 1:** Brainstorm the Write spec with the owner (superpowers:brainstorming), starting from section 2 of the spike design (the seven points, page numbering as one action, Write prints itself, theses and long reports) and the four decision entries. Open items to settle there: other document kinds, the first release's feature cut, Ctrl+K and hyperlinks (backlog), how print code is shared with Raffica PDF, Write's print dialog and the macOS print panel, speed budgets of Write (open, first page, keystroke) from the spike numbers.
- [ ] **Step 2:** Write the spec, run the builder check, bring the owner's questions in one round, get the approval.
- [ ] **Step 3:** Roadmap: subproject 5 `spec` = `{ "text": "Raffica Write design, approved <date>", "link": "superpowers/specs/<date>-raffica-write-design.md" }`; enter the spec's milestones (`4.1`, `4.2`, …, each with goal, tasks and verifiable acceptance, the first with prerequisite `"4"`); tick milestone `4`'s last task and the acceptance "The spec is approved and linked here" with evidence; set milestone `4` to `done` with `completedOn`. Move the backlog entry "Ctrl+K and hyperlinks in Write" into the spec (remove it from the backlog when the spec settles it). Generate and check.
- [ ] **Step 4:** Lasting decisions of the spec into `Docs/DECISIONS.md` (one entry, newest on top).
- [ ] **Step 5: Commit** `docs: Raffica Write spec and its milestones (M4)`.
- [ ] **Step 6:** Verify: `dotnet build Raffica.slnx -c Release`, `dotnet run tools/native-fetch.cs -- --rid win-x64 --out artifacts/native/win-x64` (once), `RAFFICA_REQUIRE_OCR=1 dotnet test Raffica.slnx -c Release --no-build --max-parallel-test-modules 1`, `dotnet format Raffica.slnx --verify-no-changes`, `dotnet run tools/roadmap.cs -- check`. Expected: all green; name the results.
- [ ] **Step 7:** Push, open the PR `feature/m04-write-spikes-spec` → `dev` (no attribution line), wait for CI, merge (squash allowed) once green.
