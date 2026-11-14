# Milestone 4.2 – Write as a fast DOCX viewer – Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Raffica Write opens a long DOCX on the shared frame, shows it in a page view and a writing view with zoom, scrolling and an outline pane, and its speed budgets are measured and set.

**Architecture:** Four pieces. (1) `Raffica.Hosting`, a new shared project that takes over what a second app needs from `Raffica.Pdf` (window host, file activation, single-instance names, settings file names, recent files, dialog queue); the PDF app uses it from there. (2) `Raffica.Write.Layout`, a UI-free library (references only `Raffica.Write.Model`, SkiaSharp and HarfBuzzSharp, no Avalonia type) that turns a `WordDocument` into laid-out pages of positioned glyph runs, rules and pictures, and draws a page onto an `SKCanvas`; it has its own UAX #14 line breaker. (3) `Raffica.Write`, the app (`AssemblyName` `RafficaWrite`): window, tabs, start view, open flow, `PageView` (draws a page through Avalonia's Skia lease), outline pane, measurement switches. (4) The speed work: a headless engine benchmark (`bench/write-layout`), a prefix open for the first page, a `write` target in `measure.yml` and `CheckCommand`, and Write columns in the corpus table. Layout runs page 1 first, then the rest on a background thread; pages are immutable once published, so the UI reads them while the layout goes on. The writing view is the same layout with one wide column cut into strips without page chrome.

**Tech Stack:** .NET 10 Native AOT, Avalonia 12.1.5, SkiaSharp 3.119.4 and HarfBuzzSharp 8.3.1.3 (the versions Avalonia.Skia 12.1.5 pins; MIT), Carlito and Caladea (OFL), the Unicode data files for UAX #14, xunit.v3, PDFium through `Raffica.Core` for the corpus comparison.

**Spec:** `Docs/superpowers/specs/2026-11-13-raffica-write-design.md` (sections 4, 5, 6 "Opening and saving", "Views", "Layout features of the first release", "Compatibility corpus", 7 row 4.2). Decisions that bind this plan: `Docs/DECISIONS.md`, "Raffica Write: the layout engine (M4)" (C#, HarfBuzz, Skia, incremental refill rule), "Raffica Write: the spec and the first release (M4)", "Raffica Write: document model and DOCX package layer (M4.1)" (`DocxParts` is the hook for lazy loading), "Raffica PDF measurement method (M2.1)".

## Global Constraints

- Everything in English. Every UI string in `Strings.en.resx` and `Strings.de.resx` ("Sie" form); every command has its own Lucide icon (`Icons.axaml`, a guard test like `CommandIconTests`); every card footer is built by `CardFooter.Build`; no hex literals in component styles; a new colour token is classified in the contrast policy (none is expected: `Raffica.Color.Ink.Write` exists).
- `Raffica.Write.Layout` references no Avalonia type, no `Raffica.Core`, no `Raffica.Write.Docx`; `Raffica.Hosting` references only Core and Shell; `Raffica.Write` references Core, Shell, Hosting, Write.Model, Write.Docx, Write.Layout and never `Raffica.Pdf`, `Raffica.Pdfium` or `Raffica.Ocr` (guard tests pin each).
- "Shared code moves, it is not copied" (spec section 4): the PDF tests stay green after Task 2.
- Spike code never becomes product code: `spikes/write/layout` is read for what it measured and is not copied; every class is written again with tests. The spike's limits stay: **first page ≤ 100 ms on the Linux laptop** (headless), full layout of the thesis ≤ 3 s, a limit is never loosened.
- Metric-compatible fonts ship: Carlito for Calibri, Caladea for Cambria. A requested font that is neither shipped nor installed falls back to a shipped default and the document keeps its font name. No Liberation fonts in 4.2 (size budget; recorded in the decision).
- Right-to-left text is laid out left to right (spec section 3); the note that says so is a later milestone's (4.8), not part of 4.2.
- Tracked changes in 4.2: the layout shows the text as if every change were accepted (insertions in, deletions out); comments are not shown. The corpus compares this with `word-accept.pdf` where there is one. The review UI is 4.6.
- A part or element the layout cannot draw is shown as a small marked placeholder box and the model keeps it (spec section 6); the layout never throws on content, only on a programming error.
- Heavy work starts after page 1 is on screen and never blocks the UI thread; the app creates no layout, font or DOCX work before the first file is opened (a test pins it).
- Commit with the GitHub noreply address only; Conventional Commits; stage by file name, then `git show --stat HEAD` against the expected list; no attribution lines; no force push, no `reset --hard`, no `--no-verify`. The PR goes to `dev`; merge it when all CI checks are green.
- "Done" = `dotnet build Raffica.slnx -c Release`, the test command of `CLAUDE.md` (with `RAFFICA_REQUIRE_OCR=1`, after `native-fetch`), `dotnet format Raffica.slnx --verify-no-changes` and `dotnet run tools/roadmap.cs -- check` green, with commands and results named. Warnings are errors; no suppressions to get green.
- No `CHANGELOG.md` entry (Write is not packaged before 4.9). Steps marked **Owner** need the owner's machines or Word; the executor prepares them, lists them as open and never invents a result.
- Every task: write the tests first and see them fail for the right reason, implement, run the named command, commit.

## File Structure

| Path | Responsibility |
|---|---|
| `src/Raffica.Hosting/` | Moved from `Raffica.Pdf`: `WindowHost` with `IHostedWindow` and `IReopenSource`, `FileActivationBridge`, `AvaloniaFileActivationSource`, `InstanceHandover`, `RenderingChoice` with `RenderingChoiceParser`, `CoalescingWriter`, `RecentService`, `DialogQueue`, `WindowSettingsSource<T>`; new `AppFiles`. |
| `src/Raffica.Write.Layout/` | `Fonts/` (`FontCatalog`, `FontFace`, `ISystemFonts`, embedded fonts), `Text/` (`LineBreaker`, generated `LineBreakData`, `Hyphenator`), `Styles/` (`StyleResolver`, `NumberingCounter`), `Paragraphs/`, `Pages/` (`DocumentLayout`, `LaidOutPage`, sections, columns, headers and footers, footnotes), `Tables/`, `Floats/`, `Drawing/PageRenderer`. |
| `src/Raffica.Write/` | The app: `Program`, `WriteOptions`, `App`, `WriteWindow` (partials), `WriteRibbon`, `Commands/WriteCommandCatalog`, `Documents/` (`WriteTab`, `WriteWorkspace`, `OpenFlow`), `Viewing/` (`PageView`, `PageScroller`, `PageDrawOperation`, `ViewAnchor`), `Panes/OutlinePane`, `Settings/`, `Platform/MacOS/WriteMacMenu`, `Measurement`, `Resources/Strings.*.resx`. |
| `src/Raffica.Write.Docx/` | Gains `DocxFile.OpenPrefix` (Task 14). |
| `bench/write-layout/` | AOT console tool `write-layout-bench`: `check`, `first-page` (child), `phases`, `run`, `render`. |
| `tools/gen-linebreak.cs` | Generates `LineBreakData.g.cs` from the Unicode files in `third-party/unicode/`; `--check` fails when the committed file is stale. |
| `tests/Raffica.Hosting.Tests/`, `tests/Raffica.Write.Tests/Layout/`, `tests/Raffica.Write.App.Tests/` | Tests of each piece. |
| `bench/shell-probe/measure.cs`, `.github/workflows/measure.yml`, `ci.yml` | The `write` target, its budget table and the CI steps. |

**Layout API** (namespace `Raffica.Write.Layout`; later tasks extend it, none replaces it):

- `FontCatalog.CreateShipped(ISystemFonts? system = null)`; `FontFace Resolve(string family, bool bold, bool italic)` (never null, never throws); `ISystemFonts { FontFace? Match(string family, bool bold, bool italic); }`; `FontFace` (`Family`, `Bold`, `Italic`, `SKTypeface Typeface`, `HarfBuzzSharp.Font Shaper`, `float Ascent`, `float Descent`, `float LineGap` per em).
- `readonly record struct BreakOpportunity(int Offset, bool Mandatory)`; `LineBreaker.Find(ReadOnlySpan<char> text) → IReadOnlyList<BreakOpportunity>` (a break before UTF-16 offset `Offset`; the one at `text.Length` is always there).
- `LayoutMode { Pages, Flow }`; `LayoutOptions(LayoutMode Mode = Pages, float FlowTextWidth = 520f)` (points; 520 pt is about 85 characters of Carlito 11 pt).
- `DocumentLayout.Create(WordDocument document, FontCatalog fonts, LayoutOptions options)`; `int LaidOutPages` (read with `Volatile`), `bool IsComplete`, `LaidOutPage Page(int index)` (valid below `LaidOutPages`), `void LayOutFirstPage()`, `void LayOutThrough(int pageIndex)`, `Task LayOutRestAsync(IProgress<int>? progress, CancellationToken token)`, `bool FirstPageIsFinal` (page 1 ended before the last block of a prefix model), `(int Page, float Y)? Locate(int bodyBlockIndex)`, `int BlockAt(int pageIndex, float y)`.
- `LaidOutPage`: `int Index`, `string Number` (display number: `"1"`, `"A-1"`, `"iii"`), `float Width`, `float Height` (points), `bool IsStrip` (flow mode: no paper, no margins), `IReadOnlyList<PageItem> Items`, `string Text` (the page's text for the corpus comparison, lines joined by `\n`). `PageItem` is `GlyphRunItem` (font, size, colour, glyph ids, x positions, baseline), `RuleItem`, `FillItem`, `ImageItem`, `PlaceholderItem`; all positions in points from the top left of the page.
- `PageRenderer.Draw(SKCanvas canvas, LaidOutPage page, float scale, ImageCache images)` draws one page; it draws paper and shadow never (the app does).

## Review Focus

Failure modes the spec implies that no milestone task names; each is pinned by a test in the owning task:

1. **Content bigger than the page** (a word wider than the text area, a table wider than the page, a row taller than a page, a picture bigger than the page) must neither loop nor drop content: it is clipped or shrunk and the page count stays finite. Task 10 `OversizedContentTerminates`, Task 6 `UnbreakableWordOverflowsInsteadOfLooping`.
2. **Degenerate documents** (an empty body, only empty paragraphs, a section with no content, a page size of zero or absurd margins) give one blank page, never zero pages and never an exception. Task 8 `EmptyDocumentHasOneBlankPage`, `AbsurdPageGeometryIsRepaired`.
3. **A file that cannot be opened** (not a DOCX, zero bytes, a corrupt zip, deleted or locked between pick and open) ends in a card with a reason, no tab, the app alive, and a recent entry leaves the list only for "file not found". Task 16 `OpenFailureShowsACardAndMakesNoTab`.
4. **Closing a tab or the window while the background layout runs** cancels the layout, leaves no thread writing into a disposed font or model and no exception in the log. Task 17 `ClosingATabCancelsItsLayout`.
5. **Text the shaper cannot draw** (emoji, CJK with no installed font, control characters, lone surrogates, a 1 MB paragraph without spaces) gives a visible `.notdef` or placeholder, no crash and a bounded time. Task 6 `UndrawableCharactersDoNotThrow`, `HugeParagraphLaysOutInBoundedTime`.

---

### Task 1: Branch, plan and roadmap

**Files:**
- Add: this plan.
- Modify: `Docs/roadmap.json` (subproject 5, milestone `4.2`; `scopeChanges`), regenerate `Docs/ROADMAP.md`.

- [ ] **Step 1:** Branch `feature/m42-write-viewer` exists (from `dev`). Milestone `4.2` gets `"plan": "superpowers/plans/2026-11-14-milestone-04-2-write-as-a-fast-docx-viewer.md"` and `"status": "planned"`.
- [ ] **Step 2:** Append to `scopeChanges` one entry dated 2026-11-14, context "Milestone 4.2", with these facts: the shared code moves into a new project `Raffica.Hosting` (window host and file activation, instance and settings file names, recent files, dialog queue); the crash guard, update flow, default-app and print services stay in `Raffica.Pdf` until a Write milestone first needs them (4.7, 4.8, 4.9); the macOS menu bar of Write is its own small `WriteMacMenu` because `MacMenuBinder` is bound to the PDF command catalog (generalizing it is a backlog item, to be done when the Write menu grows); the layout library has its own UAX #14 line breaker, generated from the Unicode data files and checked against `LineBreakTest.txt` (owner's choice, 2026-11-14, to keep the no-Avalonia rule and the startup budget); the layout shows tracked changes as accepted text and shows no comments until 4.6; the engine benchmark is a new tool `bench/write-layout`; the app command search moves to Alt+Q through a frame option (spec section 6, keys), on macOS ⌘K stays until 4.5 chooses the ⌘ shortcut; the page-count gate of the thesis needs Word's PDF of it, which is the owner's and is open.
- [ ] **Step 3:** Read the backlog by hand (the skill `/ideen` of `benjo-code/Labjo` is not installed here; `bash tools/install-ideen-skill.sh` there installs it). Adopted into 4.2: nothing. Not adopted: "Remove the overlap between Core and Shell settings" (Write's own `WriteSettings` file holds only the default view and zoom, so it adds no overlap), "Third-party licences of the Write projects" (4.2 adds notice entries for Caladea, the hyphenation patterns and the Unicode data under `third-party/`; the `tools/licenses.cs` extension stays in the backlog for 4.8 and 4.9).
- [ ] **Step 4: Owner (prepare, list as open).** Ask the owner for `word.pdf` of the thesis (Word, Windows, "Save as PDF", default options) into `testdata/write-corpus/thesis/word.pdf`, and `word.pdf` for as many other corpus files as they like; `word-accept.pdf` and `word-reject.pdf` for `poi-tika-792`. Task 15 needs the thesis one; every earlier task goes on without it.
- [ ] **Step 5:** `dotnet run tools/roadmap.cs -- generate`, then `-- check`. Expected: passes.
- [ ] **Step 6: Commit** after the owner has reviewed the plan:

```bash
git add Docs/superpowers/plans/2026-11-14-milestone-04-2-write-as-a-fast-docx-viewer.md Docs/roadmap.json Docs/ROADMAP.md
git commit -m "docs: plan milestone 4.2 (Write as a fast DOCX viewer)"
git show --stat HEAD
```
- [ ] **Step 7:** When execution starts: status `in-progress`, regenerate, check, commit `docs: start milestone 4.2`. Tick every roadmap task right after its plan task is done; tick acceptance criteria only with evidence (Task 20).

### Task 2: `Raffica.Hosting`, the shared app code

**Files:**
- Create: `src/Raffica.Hosting/Raffica.Hosting.csproj` (references Core, Shell, `Avalonia`; `IsAotCompatible`; `InternalsVisibleTo` the two test projects), `src/Raffica.Hosting/AppFiles.cs`, `tests/Raffica.Hosting.Tests/` (xunit.v3 with the `VersionOverride` of the other UI test projects, `Avalonia.Headless.XUnit`), `tests/Raffica.Hosting.Tests/HostingProjectTests.cs`, `tests/Raffica.Hosting.Tests/AppFilesTests.cs`.
- Move (git mv, namespace `Raffica.Hosting`, `internal` stays `internal`): from `src/Raffica.Pdf/Hosting` `WindowHost.cs` (with `IHostedWindow`, `IReopenSource`), `FileActivationBridge.cs`, `AvaloniaFileActivationSource.cs`, `InstanceHandover.cs`, `RenderingChoice.cs`; from `src/Raffica.Pdf/Settings` `CoalescingWriter.cs`, `RecentService.cs`; from `src/Raffica.Pdf/Dialogs` `DialogQueue.cs`; `WindowSettingsSource.cs` becomes `WindowSettingsSource<TAppSettings>` (takes `Func<string?, TAppSettings> loadApp`). Move with them their tests from `tests/Raffica.Pdf.Tests` (`WindowHostTests`, `FileActivationBridgeTests`, `InstanceHandoverTests`, `RenderingChoiceTests`, `CoalescingWriterTests`, `RecentServiceTests`) to `tests/Raffica.Hosting.Tests`; tests that need `PdfWindow` stay.
- Modify: `Raffica.slnx`, `src/Raffica.Pdf/Raffica.Pdf.csproj` (reference Hosting), `src/Raffica.Pdf/Settings/PdfSettingsFiles.cs` (keeps its public surface, delegates to `AppFiles.Pdf`), `Program.cs` (`InstanceName` becomes `AppFiles.InstanceName`), `App.axaml.cs` and every file that names a moved type, `tests/Raffica.Pdf.Tests/AppProjectTests.cs`.

**Interfaces:**
- Produces: `public sealed record AppFiles(string AppFolder, string SettingsName, string RecentName)` with `static AppFiles Pdf` (`"Pdf"`, `"pdf-settings.json"`, `"recent.json"`) and `static AppFiles Write` (`"Write"`, `"write-settings.json"`, `"write-recent.json"`); `string ShellPathFor(string? settingsFile)`, `string AppSettingsPathFor(string? settingsFile)`, `string RecentPathFor(string? settingsFile)`, `static string ConfigDirectoryFor(string? settingsFile)`, `string InstanceName(string? settingsFile)` (Pdf: `SingleInstance.DefaultName` and its hash suffix exactly as before; Write: `"raffica-write-" + Environment.UserName` plus the same hash suffix).

- [ ] **Step 1: Tests.** `AppFilesTests`: `PdfNamesAreUnchanged` (the six paths for `settingsFile` null and for `C:\t\s.json` equal what `PdfSettingsFiles` gave before: assert the literal file names and folders), `WriteUsesItsOwnFolderAndNames`, `InstanceNamesDifferBetweenAppsAndBySettingsFile`, `MalformedSettingsFileGoesToTemp` (copy the cases of `PdfSettingsFilesTests`). `HostingProjectTests`: `HostingReferencesOnlyCoreAndShell`. In `AppProjectTests` rename the test to `TheAppReferencesOnlyCoreShellAndHosting` and expect three references.
- [ ] **Step 2: Run to see it fail.** `dotnet test tests/Raffica.Hosting.Tests -c Release` → build failure (no project yet).
- [ ] **Step 3: Implement** the project, `AppFiles`, the moves and the call-site updates. No behaviour change: `git diff -M --stat` shows the moved files as renames.
- [ ] **Step 4: Verify.** `dotnet build Raffica.slnx -c Release`; `dotnet test tests/Raffica.Hosting.Tests -c Release`; `dotnet test tests/Raffica.Pdf.Tests -c Release --max-parallel-test-modules 1` (the whole PDF suite is the proof that nothing changed; with `RAFFICA_REQUIRE_OCR=1` after `native-fetch`); `dotnet format Raffica.slnx --verify-no-changes`. Expected: all green, the same test count in Pdf plus Hosting as Pdf alone before.
- [ ] **Step 5: Commit** (`refactor: move the shared app code into Raffica.Hosting`); `git show --stat HEAD` lists renames, the two new test files and the project files.

### Task 3: The layout library and the fonts

**Files:**
- Create: `src/Raffica.Write.Layout/Raffica.Write.Layout.csproj` (references `Raffica.Write.Model`; `PackageReference` SkiaSharp, HarfBuzzSharp; `IsAotCompatible`; fonts as `EmbeddedResource`), `Fonts/FontCatalog.cs`, `Fonts/FontFace.cs`, `Fonts/ISystemFonts.cs`, `Fonts/Caladea-{Regular,Bold,Italic,BoldItalic}.ttf` and `OFL-Caladea.txt` (from `spikes/write/fonts`), `tests/Raffica.Write.Tests/Layout/FontCatalogTests.cs`.
- Modify: `Directory.Packages.props` (SkiaSharp 3.119.4, HarfBuzzSharp 8.3.1.3, `SkiaSharp.NativeAssets.Linux.NoDependencies` for the test and bench hosts only), `Raffica.slnx`, `tests/Raffica.Write.Tests/Raffica.Write.Tests.csproj` (reference Layout), `tests/Raffica.Write.Tests/ProjectGuardTests.cs` (Layout references only Model), `third-party/` (notice entry for Caladea, as Carlito has one).
- Carlito is linked, not copied: `<EmbeddedResource Include="..\Raffica.Shell\Assets\Fonts\Carlito-*.ttf" LogicalName="Raffica.Write.Layout.Fonts.%(Filename)%(Extension)" />`; the Caladea files get the same logical names.

**Interfaces:** the `FontCatalog`/`FontFace`/`ISystemFonts` signatures of the Layout API above.

- [ ] **Step 1: Tests.** `ShippedFacesLoad` (all eight faces: every one maps `'A'` and `'ä'` to a non-zero glyph, ascent and descent positive), `MetricAliasesResolve` (`Resolve("Calibri", false, false).Family == "Carlito"`, `"Cambria"` gives `"Caladea"`, `"Calibri Light"` gives Carlito, case-insensitive), `ServesInstalledFontsBeforeTheDefault` (a fake `ISystemFonts` returning a face for `"Segoe UI"` is used; one returning null gives the default), `UnknownFontNeverThrows` (serif-like names such as `"Times New Roman"`, `"Georgia"` fall back to Caladea, every other unknown name to Carlito), `FacesLoadOnFirstUse` (a catalog that was created and not asked for a face has loaded none: the catalog exposes `int LoadedFaces`), `LayoutGuard` (the Layout project references only `Raffica.Write.Model` and no Avalonia assembly).
- [ ] **Step 2:** Run `dotnet test tests/Raffica.Write.Tests -c Release --filter-class "*FontCatalogTests"` → fails (type missing).
- [ ] **Step 3: Implement.** A face is created on first `Resolve` of its key and cached (`ConcurrentDictionary`); the bytes come from the embedded resource without a copy where the stream is an `UnmanagedMemoryStream`, else one copy; `SKTypeface.FromData` over a non-copying `SKData` and a HarfBuzz `Blob` over the same memory (`MemoryMode.ReadOnly`), both kept alive by the face. `ISystemFonts` default implementation uses `SKFontManager.Default.MatchFamily`; it is only asked for a name that is not shipped and not aliased.
- [ ] **Step 4: Verify.** The command of step 2, expected: all pass; `dotnet build Raffica.slnx -c Release` clean.
- [ ] **Step 5: Commit** (`feat(write): layout library with the shipped fonts and font fallback`).

### Task 4: The line breaker (UAX #14)

**Files:**
- Create: `third-party/unicode/{LineBreak.txt,EastAsianWidth.txt,emoji-data.txt,DerivedGeneralCategory.txt,LineBreakTest.txt,LICENSE-Unicode.txt,README.md}` (one Unicode version, the newest published when the files are fetched; its version and the retrieval date go into `README.md` and into the header of the generated file), `tools/gen-linebreak.cs`, `src/Raffica.Write.Layout/Text/LineBreakData.g.cs` (generated), `Text/LineBreaker.cs`, `Text/LineBreakClass.cs`, `tests/Raffica.Write.Tests/Layout/LineBreakConformanceTests.cs`, `LineBreakerTests.cs`.
- Modify: `.github/workflows/ci.yml` (step "Check that the line break data is current": `dotnet run tools/gen-linebreak.cs -- --check`).

**Interfaces:** `BreakOpportunity` and `LineBreaker.Find` of the Layout API above. The generator writes a two-stage table (`ReadOnlySpan<byte>` literals, no static initialization at startup) of the line break class per code point with the East Asian width and Extended_Pictographic facts that the rules LB30, LB30b and the quotation rules need.

- [ ] **Step 1: Tests.** `LineBreakConformanceTests.EveryLineOfLineBreakTestPasses` parses `LineBreakTest.txt` (`×` no break, `÷` break), builds the string, and asserts `Find` returns exactly the `÷` offsets; the test reports the count of lines it ran and fails below 15,000 (a parser that read nothing must not pass). `LineBreakerTests`: `MandatoryBreakAfterNewline` (`"a\nb"`: offset 2 is `Mandatory`), `NoBreakInsideNumber` (`"1,000.50"`), `BreakAfterHyphen` (`"well-known"` allows offset 5), `NoBreakBeforeClosePunctuation`, `SurrogatePairsAreOneCharacter` (`"a😀b"` has no offset between the two surrogates), `CjkBreaksBetweenIdeographs`, `EndOffsetIsAlwaysPresent` (empty and one-character text), `LongTextIsLinear` (a 1,000,000-character string finishes in under 2 s).
- [ ] **Step 2:** Run `dotnet test tests/Raffica.Write.Tests -c Release --filter-class "*LineBreak*"` → fails (missing type). Check the count of discovered tests, not only the exit code.
- [ ] **Step 3: Implement** the generator (parses the four property files for the pinned version, writes the table deterministically, `--check` rewrites to a temporary file and compares) and `LineBreaker` (the pair-rule algorithm of UAX #14 for that version, rules LB1 to LB31 including LB8a, LB9 to LB13, LB15a to LB15d, LB21a, LB28a, LB30a, LB30b; classes AI, SG, XX, SA and CJ resolve as LB1 says, SA as AL).
- [ ] **Step 4: Verify.** The step 2 command, expected: every conformance line passes; `dotnet run tools/gen-linebreak.cs -- --check` exits 0; change one byte of `LineBreakData.g.cs` and see `--check` exit 1, then restore it.
- [ ] **Step 5: Commit** (`feat(write): UAX #14 line breaker generated from the Unicode data`).

### Task 5: Style resolution and numbering

**Files:**
- Create: `src/Raffica.Write.Layout/Styles/StyleResolver.cs`, `ResolvedParagraph.cs`, `ResolvedRun.cs`, `ResolvedTable.cs`, `NumberingCounter.cs`, `tests/Raffica.Write.Tests/Layout/StyleResolverTests.cs`, `NumberingCounterTests.cs` (small DOCX files from the existing `Support/DocxBuilder.cs`).

**Interfaces:**
- Produces: `StyleResolver(StyleSheet? styles, DocumentSettings? settings)`; `ResolvedParagraph ForParagraph(Paragraph p)`; `ResolvedRun ForRun(ResolvedParagraph owner, Run r)`; `ResolvedTable ForTable(Table t)`. `ResolvedParagraph` (alignment, left, right, first-line and hanging indents, spacing before and after in points, line spacing rule `Auto|AtLeast|Exact` and value, `ContextualSpacing`, `KeepNext`, `KeepLines`, `PageBreakBefore`, `WidowControl`, `TabStops`, `OutlineLevel`, `NumberingId`, `NumberingLevel`, `SuppressAutoHyphens`). `ResolvedRun` (font name, size in points, bold, italic, underline kind, strike, colour, highlight, caps, small caps, vertical alignment, language, hidden, `ParagraphMarkOnly`). `NumberingCounter(NumberingDefinitions? numbering)`; `NumberingLabel? Next(ResolvedParagraph p)` returns the label text, the label's `ResolvedRun` and the suffix (`Tab|Space|Nothing`) for a numbered paragraph and advances the counters; called in document order only.
- Rules: docDefaults, then the `basedOn` chain (cycle-safe: a cycle stops at the repeat), then the paragraph's character style, then direct formatting; toggle properties (`w:b`, `w:i`) use the OOXML toggle rule; table styles apply base properties and `firstRow`, `lastRow`, `firstCol`, `lastCol`, `band1Horz`, `band2Horz` as `w:tblLook` enables them; numbering formats decimal, lowerLetter, upperLetter, lowerRoman, upperRoman, bullet, none, with `%1.%2` level text, `w:start`, `w:lvlRestart`, `w:lvlOverride` start overrides.

- [ ] **Step 1: Tests.** `BasedOnChainResolves`, `CyclicBasedOnTerminates`, `DirectFormattingWinsOverStyle`, `ToggleBoldTwiceIsOff`, `DocDefaultsApplyToUnstyledParagraphs`, `MissingStyleIdFallsBackToDefault`, `TableStyleFirstRowAndBands`, `HeadingNumbersCount` (`1`, `1.1`, `1.2`, `2`, `2.1` over a multi-level list), `LowerRomanAndLetters` (`iv`, `c`), `RestartAfterHigherLevel`, `StartOverrideApplies`, `BulletHasNoNumber`, `UnknownNumIdGivesNoLabel`.
- [ ] **Step 2:** Run `--filter-class "*StyleResolverTests"` and `"*NumberingCounterTests"` → fail.
- [ ] **Step 3: Implement** with the resolver caching one `ResolvedParagraph` per style id (direct properties are merged on top per paragraph; never re-walk the style chain per run).
- [ ] **Step 4: Verify** both commands pass.
- [ ] **Step 5: Commit** (`feat(write): style and numbering resolution for the layout`).

### Task 6: Paragraph layout

**Files:**
- Create: `src/Raffica.Write.Layout/Paragraphs/ParagraphLayouter.cs` (shaping, line breaking, line filling), `TabStops.cs`, `Justifier.cs`, `LineBox.cs`, `InlineBox.cs`, `tests/Raffica.Write.Tests/Layout/ParagraphLayoutTests.cs`.

**Interfaces:**
- Consumes: `FontCatalog`, `LineBreaker.Find`, `ResolvedParagraph`, `ResolvedRun`, `NumberingLabel` (Task 5).
- Produces: `ParagraphLayouter(FontCatalog fonts)`; `ParagraphLayout Layout(ParagraphInput input, float width)` where `ParagraphInput` carries the resolved paragraph, its inlines flattened to `TextSpan`s with their `ResolvedRun` (field results as their cached text, tracked insertions in, deletions out, hyperlink text as text, `UnknownInline` as a one-em placeholder span), inline pictures as `InlineBox(width, height, ImageRef)`, the optional numbering label, and the break kinds (`Line`, `Page`, `Column`). `ParagraphLayout` (`IReadOnlyList<LineBox> Lines`, `float Height`, each line with ascent, descent, baseline offset, glyph runs and the text range `[Start, End)`; `bool EndsWithPageBreak`). Hit testing is not part of 4.2.

- [ ] **Step 1: Tests** (real shipped fonts, widths in points). `GreedyBreakAtSpaces` (a known sentence in Carlito 11 pt at 200 pt gives the line texts the test spells out), `LineHeightSingleIsFontHeight` (single spacing equals ascent + descent + line gap of the face times the size), `MultipleAtLeastExactSpacing` (1.5, at least 20 pt with a small font, exact 14 pt with a large font), `FirstLineAndHangingIndent`, `RightAndCenterAlignment`, `JustifyStretchesAllButTheLastLine` (line widths equal the text width, last line natural), `TabStopsLeftCenterRightDecimalWithLeaders`, `DefaultTabEvery720Twips`, `NumberingLabelTabsToTheHangingIndent`, `SuperscriptShrinksAndRaises`, `SmallCapsAndAllCaps`, `RunsOfDifferentFontsShareALine` (Carlito and Caladea on one line, baseline shared), `InlinePictureRaisesTheLineBox`, `PageBreakEndsTheParagraphLayout`, `UnknownInlineShowsAPlaceholder`, `UnbreakableWordOverflowsInsteadOfLooping` (a 300 pt word at width 100 gives one line and terminates), `UndrawableCharactersDoNotThrow` (emoji, `\u0001`, a lone surrogate, CJK with no installed font), `HugeParagraphLaysOutInBoundedTime` (1 MB without spaces and 200 KB of words each finish under 3 s on the CI runner).
- [ ] **Step 2:** `--filter-class "*ParagraphLayoutTests"` → fails.
- [ ] **Step 3: Implement.** Shape each run with HarfBuzz at a scale of 1024 units per point, one shaping per run and line-break chunk, cluster-aware advances; break with `LineBreaker.Find` over the paragraph text; fill greedily; trailing spaces do not count toward the width. A face lacks a glyph: use the catalog's default face for that character and, if that lacks it too, the face's `.notdef`. Hyphenation hook: `IHyphenator? Hyphenator` on the layouter, unused until Task 7.
- [ ] **Step 4: Verify** the command passes.
- [ ] **Step 5: Commit** (`feat(write): paragraph layout`).

### Task 7: Hyphenation

**Files:**
- Create: `src/Raffica.Write.Layout/Text/Hyphenator.cs` (Liang's algorithm), `Text/HyphenationData.cs` (loads the pattern sets from embedded resources on first use), `Text/Patterns/hyph-en-us.pat.txt`, `hyph-en-gb.pat.txt`, `hyph-de-1996.pat.txt` with their licence texts, `tests/Raffica.Write.Tests/Layout/HyphenatorTests.cs`.
- Modify: `ParagraphLayouter.cs` (use the hook), `third-party/` (one notice entry per set).

**Interfaces:** `IHyphenator { IReadOnlyList<int> Points(ReadOnlySpan<char> word, string language); }`, `Hyphenator.Default`. The layouter calls it only when `DocumentSettings.AutoHyphenation` is on, the paragraph does not suppress it, and the run language resolves to `en-US`, `en-GB` or `de-DE` (any other language: no hyphenation).

- [ ] **Step 1: Licence check first.** Source: the `hyph-utf8` project's TeX patterns; each file's licence is read and written to `third-party/`; a set whose licence cannot be established, or is not compatible with GPL-3.0-or-later, is left out and its language gets no hyphenation (recorded in the decision). Do not start Step 3 for a set before this.
- [ ] **Step 2: Tests.** `EnglishPoints` (`"hyphenation"` gives `hy-phen-ation`), `GermanPoints` (`"Silbentrennung"` gives `Sil-ben-tren-nung`), `ShortWordsAreNotBroken` (fewer than five letters), `UnknownLanguageHasNoPoints`, `HyphenAddsAHyphenGlyphAtTheBreak` (a justified paragraph at a narrow width breaks a long word with a visible hyphen and the line fits), `OffByDefault` (a document without `w:autoHyphenation` never hyphenates), `SuppressAutoHyphensWins`, `PatternLoadIsLazy` (no pattern is parsed before the first hyphenated word).
- [ ] **Step 3: Implement** and run `--filter-class "*HyphenatorTests"` → pass.
- [ ] **Step 4: Commit** (`feat(write): hyphenation with licence-checked patterns`).

### Task 8: Page layout, sections, columns and the layout API

**Files:**
- Create: `src/Raffica.Write.Layout/Pages/DocumentLayout.cs`, `LaidOutPage.cs`, `PageItem.cs`, `PageList.cs` (append-only, safe to read while it grows), `PageFiller.cs` (blocks into pages and columns), `SectionPlan.cs`, `PageGeometry.cs`, `FlowSegmenter.cs`, `tests/Raffica.Write.Tests/Layout/PageLayoutTests.cs`, `DocumentLayoutApiTests.cs`.

**Interfaces:** the `DocumentLayout`, `LaidOutPage`, `PageItem`, `LayoutOptions` of the Layout API above. `PageFiller` consumes paragraphs through `ParagraphLayouter`; headers, footers and notes arrive in Task 9, tables in Task 10 and floats in Task 11 through the same `IBlockFlow` seam (`float Height`, `Split(float available)`), so this task's filler already treats a block as splittable.

- [ ] **Step 1: Tests.** `EmptyDocumentHasOneBlankPage`, `AbsurdPageGeometryIsRepaired` (zero or negative size, margins larger than the page: the page keeps a 36 pt text area at least and the repair is not silent: `DocumentLayout.Notes` lists it), `PageSizeMarginsAndOrientation` (A4, Letter, landscape with swapped `w:pgSz`), `SectionTypes` (next page, continuous, even page and odd page insert a blank page where needed), `ColumnsFlowAndBalance` (two columns fill the first then the second; a continuous section break balances the columns by height), `KeepNextKeepLinesPageBreakBefore`, `WidowAndOrphanControl` (on by default as `w:widowControl` says, off when the style turns it off), `SpacingCollapsesWithContextualSpacing`, `NumberedHeadingsAreLabelled` (uses `NumberingCounter`), `FirstPageIsFinalForAPrefix` (a prefix model whose last block lies beyond page 1 is final; one that ends on page 1 is not), `LocateAndBlockAtRoundTrip`, `ReadingWhileLayingOutIsSafe` (a reader thread polls `LaidOutPages` and walks `Items` while `LayOutRestAsync` runs on another: no exception, the count never decreases), `CancellationStopsTheLayout`, `FlowModeHasNoPageChrome` (strips of at most 2,000 pt, no margins, no header or footer, width `FlowTextWidth`, a block never splits mid-line across a strip), `FlowAndPagesShareLineBreaks` (the same paragraph breaks into the same lines when the text widths are equal).
- [ ] **Step 2:** `--filter-class "*PageLayoutTests"` and `"*DocumentLayoutApiTests"` → fail.
- [ ] **Step 3: Implement.** Sections come from the paragraphs' `SectionBreak` and the body's `FinalSection`; page numbers format as `decimal`, `lowerRoman`, `upperRoman`, `lowerLetter`, `upperLetter` with `w:pgNumType` start and format per section (the display string is built here; the footer field in Task 9 reads it). `LayOutFirstPage` returns when page 1 is published; `LayOutRestAsync` publishes each page as it completes and reports its count. Pages are immutable after publication.
- [ ] **Step 4: Verify** both commands pass.
- [ ] **Step 5: Commit** (`feat(write): page layout, sections, columns and the layout API`).

### Task 9: Headers, footers, page-number fields and notes

**Files:**
- Create: `src/Raffica.Write.Layout/Pages/HeaderFooterPlan.cs`, `FieldValues.cs`, `NotePlanner.cs`, `tests/Raffica.Write.Tests/Layout/HeaderFooterTests.cs`, `NoteLayoutTests.cs`.
- Modify: `PageFiller.cs` (reserve header and footer space, place notes).

- [ ] **Step 1: Tests.** `FirstPageAndEvenOddHeaders` (`w:titlePg`, `w:evenAndOddHeaders`), `SectionWithoutOwnReferenceInheritsThePreviousOne`, `PageFieldShowsTheDisplayNumber` (the thesis' physical page 3 shows `1`, the appendix' first page `A-1`: a small generated three-section file with the same `pgNumType` rules), `NumPagesAndSectionPages`, `UnknownFieldShowsItsCachedResult` (a `TOC` or `REF` field keeps its stored text), `FootnoteSitsAtTheBottomOfThePageOfItsReference` (its space is reserved when the line is placed; the reference mark is a superscript number), `FootnoteWithSeveralLinesContinuesOnTheNextPage` (separator and continuation separator), `EndnotesCollectAtTheEndOfTheDocument`, `FootnoteNumbersCountPerDocument`, `HeaderTallerThanTheMarginPushesTheBody`.
- [ ] **Step 2:** `--filter-class "*HeaderFooterTests"` and `"*NoteLayoutTests"` → fail.
- [ ] **Step 3: Implement.** Fields evaluated in 4.2: `PAGE`, `NUMPAGES`, `SECTIONPAGES`, `SECTION`; every other field shows its cached result. A header or footer is laid out once per distinct (part, page-number text) pair and reused.
- [ ] **Step 4: Verify** both commands pass; **Step 5: Commit** (`feat(write): headers, footers, page fields and notes`).

### Task 10: Tables

**Files:**
- Create: `src/Raffica.Write.Layout/Tables/TableLayouter.cs`, `GridSolver.cs`, `CellBox.cs`, `BorderResolver.cs`, `tests/Raffica.Write.Tests/Layout/TableLayoutTests.cs`.

**Interfaces:** a table is an `IBlockFlow` (Task 8) that splits between rows and, for a row taller than the page, inside the row at line boundaries of its cells.

- [ ] **Step 1: Tests.** `FixedWidthColumns` (`w:tblLayout fixed`, `w:gridCol` widths), `AutoFitFollowsContent` (min and max content widths, shrunk to the page), `PercentAndAutoPreferredWidths`, `GridSpanAndVerticalMerge`, `CellMarginsAndRowHeightRules` (`atLeast`, `exact`), `HeaderRowsRepeatOnEachPage` (`w:tblHeader`), `RowsDoNotSplitWhenCantSplitIsSet`, `BordersAndShadingResolve` (conflicting neighbour borders: the wider one, else the table's), `NestedTable`, `OversizedContentTerminates` (a table wider than the page is scaled to the text width; a row taller than a page splits inside the row; an unbreakable word in a narrow cell overflows; page count stays finite), `TableAfterTableKeepsAnEmptyParagraph`.
- [ ] **Step 2:** `--filter-class "*TableLayoutTests"` → fails. **Step 3: Implement. Step 4: Verify** passes. **Step 5: Commit** (`feat(write): table layout`).

### Task 11: Pictures and floating objects

**Files:**
- Create: `src/Raffica.Write.Layout/Floats/FloatPlanner.cs`, `WrapRegions.cs`, `ImageCache.cs`, `tests/Raffica.Write.Tests/Layout/FloatLayoutTests.cs`, `ImageCacheTests.cs`.
- Modify: `ParagraphLayouter.cs` (lines query the wrap regions), `PageFiller.cs`.

- [ ] **Step 1: Tests.** `InlinePictureUsesExtentAndScalesToTheTextWidth`, `FloatingPictureSquareWrapNarrowsTheLines`, `TopAndBottomWrapMovesTextBelow`, `TightAndThroughAreDrawnAsSquare`, `AnchorOffsetsRelativeToPageMarginAndParagraph`, `TextBoxLaysOutItsParagraphsInsideItsBox` (an `mc:AlternateContent` text box from `poi-shapes-with-text`), `UnsupportedPictureFormatIsAPlaceholder` (EMF and WMF: Skia cannot decode them; a marked box of the stated size, the part stays in the model), `CorruptImageBytesArePlaceholderNotAnException`, `FloatNeverLeavesThePage` (clamped inside the page), `ImagesDecodeOnceAndLazily` (`ImageCache` decodes at first draw, not at layout).
- [ ] **Step 2:** `--filter-class "*FloatLayoutTests"` and `"*ImageCacheTests"` → fail. **Step 3: Implement** (`ImageRef` holds the relationship id; `ImageCache` takes a `Func<string, ReadOnlyMemory<byte>>` so the library never reads a package). **Step 4: Verify** pass. **Step 5: Commit** (`feat(write): pictures and floating objects`).

### Task 12: The page renderer

**Files:**
- Create: `src/Raffica.Write.Layout/Drawing/PageRenderer.cs`, `GlyphDrawing.cs`, `tests/Raffica.Write.Tests/Layout/PageRendererTests.cs`.

**Interfaces:** `PageRenderer.Draw(SKCanvas, LaidOutPage, float scale, ImageCache)` of the Layout API. Text is drawn as glyph ids with subpixel positioning and no hinting (`SKFont { Subpixel = true, Hinting = None, Edging = SubpixelAntialias }`, one `SKTextBlob` per glyph run, fonts cached per face and size).

- [ ] **Step 1: Tests** (draw into an `SKBitmap`). `TextIsDrawnWhereTheLayoutSaysItIs` (dark pixels inside the bounding box of the first line, none above the top margin), `ScaleScalesTheOutput`, `UnderlineStrikeAndHighlight`, `FillAndRuleItems`, `ImagesAreDrawnFromTheCache`, `PlaceholderIsVisible`, `DrawingTwiceIsIdentical` (byte-equal bitmaps), `DrawingAnEmptyPageIsFast` (no allocation of a bitmap inside `Draw`).
- [ ] **Step 2:** `--filter-class "*PageRendererTests"` → fails. **Step 3: Implement. Step 4: Verify. Step 5: Commit** (`feat(write): page renderer`).

### Task 13: The engine benchmark

**Files:**
- Create: `bench/write-layout/WriteLayoutBench.csproj` (`AssemblyName` `write-layout-bench`, `PublishAot`, references Write.Model, Write.Docx, Write.Layout), `Program.cs`, `FirstPage.cs`, `Phases.cs`, `Run.cs`, `Render.cs`, `tests/Raffica.Write.Tests/Layout/EngineBenchTests.cs`.
- Modify: `Raffica.slnx`, `.github/workflows/ci.yml` (publish step with the `IL[23][0-9]{3}` log search on Linux and Windows and in `build-macos`, then `check`).

**Interfaces:** commands `check` (open `thesis.docx`, lay out everything twice, assert both runs give the same page count and page texts, that physical page 3 has the number `1` and the first appendix page `A-1`; exit 0, 1 findings, 2 bad input), `first-page --file <docx>` (the child: process start to "page 1 drawn into a bitmap", prints `done ms=<n>`), `phases` (the spike's breakdown: process start to `Main`, fonts, package, parse, layout, draw), `run --file <docx> [--runs 10 --warmup 2]` (cold child processes, median, refuses to time anything but a Native AOT publish), `render <page> <out.png>`.

- [ ] **Step 1: Tests.** `CheckPassesOnTheThesis`, `RunRefusesAJitBuild`, `FirstPageChildPrintsDone` (in-process call of the same code path), `PhasesNamesEveryPhase`.
- [ ] **Step 2:** `--filter-class "*EngineBenchTests"` → fails. **Step 3: Implement** the tool (the code of the spike is read, not copied). **Step 4: Verify.** Tests pass; `dotnet publish bench/write-layout -c Release -r win-x64 -o out/write-layout` has no `IL2xxx` or `IL3xxx`; `out/write-layout/write-layout-bench check --file testdata/write-corpus/thesis/thesis.docx` exits 0.
- [ ] **Step 5: Commit** (`feat(write): headless engine benchmark`).

### Task 14: Startup work for the first page

**Files:**
- Modify: `src/Raffica.Write.Docx/DocxFile.cs`, `Xml/DocumentXml.cs`, `DocxParts.cs` (new `DocxFile.OpenPrefix`), `src/Raffica.Write.Layout/Fonts/FontCatalog.cs`, `Pages/DocumentLayout.cs` (`ReplaceModel`), `bench/write-layout/Phases.cs`.
- Test: `tests/Raffica.Write.Tests/DocxPrefixTests.cs`, `Layout/StartupTests.cs`.

**Interfaces:** `static DocxFile DocxFile.OpenPrefix(string path, int maxBlocks)`: loads styles, numbering, settings and header and footer parts, the body's first `maxBlocks` top-level blocks, and the final section properties (found by a backwards search for the last `<w:sectPr` in the decompressed `document.xml` bytes, then read alone); `DocxFile.IsPrefix` is true and `SaveAsync` throws `InvalidOperationException`. `DocumentLayout.ReplaceModel(WordDocument full)` swaps the model of a layout whose page 1 is final (`FirstPageIsFinal`); the already published page 1 stays.

- [ ] **Step 1: Tests.** `PrefixHasTheFirstBlocksAndTheFinalSection` (thesis: 64 blocks, the final `sectPr` equals the full open's), `PrefixOfAShortFileIsTheWholeFile`, `PrefixCannotBeSaved`, `PrefixOpenReadsLessThanTheFullOpen` (counted parse events through an internal seam), `PageOneOfPrefixEqualsPageOneOfFull` (item lists equal for the thesis and four corpus files), `OnlyTheFacesOfPageOneAreLoaded` (`LoadedFaces <= 3` after page 1 of the thesis), `FontLoadingDoesNotCopy` (the loaded bytes of a shipped face are the resource memory: no allocation over the font size).
- [ ] **Step 2:** `--filter-class "*DocxPrefixTests"` and `"*StartupTests"` → fail.
- [ ] **Step 3: Implement,** then measure: `write-layout-bench phases` on the Windows PC and CI Linux before and after, recorded in the commit message. The levers, in the order of their measured cost: faces on first use and without a copy (Task 3 made this possible), the prefix open, reading the package parts concurrently with the font load on the thread pool. A lever that saves less than 2 ms on the Windows PC is dropped and named in the decision.
- [ ] **Step 4: Verify.** Tests pass. **Owner:** on the Linux laptop `write-layout-bench run --file testdata/write-corpus/thesis/thesis.docx` (Native AOT publish of the commit) gives a median first page of at most 100 ms; the CSV goes to `Docs/measurements/write-engine-2026-11-xx/`. If it misses, that is a finding: the next lever is named in the decision, the limit does not move.
- [ ] **Step 5: Commit** (`perf(write): first-page startup work`).

### Task 15: Corpus comparison with Write columns and the thesis gate

**Files:**
- Modify: `bench/write-corpus/WriteCorpus.csproj` (reference Layout), `CorpusTable.cs`, `PageComparer.cs` (no change to its contract), `Program.cs` (`compare --layout`, `table` adds columns), `testdata/write-corpus/README.md`, `Docs/write-corpus.md` (regenerated by hand), `.github/workflows/ci.yml` (step "Thesis page count against Word's PDF", added in the commit that adds `word.pdf`).
- Test: `tests/Raffica.Write.Tests/CorpusToolTests.cs` (extend), `Layout/CorpusLayoutTests.cs`.

**Interfaces:** a new internal `WriteLayoutFacts.Load(string docxPath) → PageFacts` (lays out the file with `LayoutOptions` default and returns `PageFacts(PageCount, PageTexts)` from `LaidOutPage.Text`, the text normalised by the existing `TextNormalizer`). `table` gains the columns **Write pages**, **Pages equal** (page count equals Word's), **Text match** (mean Dice of the per-page word multisets, from `PageComparer`) for every entry with a `word.pdf`, and "–" otherwise. `compare --id <id> --require-same-page-count` exits 1 on a different count and 2 when a PDF is missing.

- [ ] **Step 1: Tests.** `EveryCorpusFileLaysOutWithoutAnException` (all 16 files, page count at least 1), `LayoutFactsAreDeterministic` (two runs equal), `TableHasTheWriteColumns` (header row and a row with a fake `word.pdf` made by the test), `CompareRequiresSameCountExitCodes` (0, 1, 2 cases), `AcceptViewMatchesWordAcceptForTrackedChanges` (the layout of `poi-tika-792` has no deleted text; skipped only when `word-accept.pdf` is absent, and then asserts the absence by name).
- [ ] **Step 2:** `--filter-class "*CorpusLayoutTests"` and `"*CorpusToolTests"` → fail. **Step 3: Implement.**
- [ ] **Step 4: Verify.** Tests pass. `dotnet run --project bench/write-corpus -c Release -- table --root testdata/write-corpus --out Docs/write-corpus.md` and commit the file.
- [ ] **Step 5: Commit** (`feat(write): corpus table with Write's own layout`).
- [ ] **Step 6: Owner gate, spec acceptance 3.** With `testdata/write-corpus/thesis/word.pdf` in place: `write-corpus compare --id thesis --require-same-page-count` must exit 0. Where it does not, read the per-page difference (`compare` names the first different page), adjust the layout rule that causes it (line height from the face's metrics, space handling, footnote space, table row heights), add a regression test for the rule in the owning task's test class, and repeat; each rule change is a commit `fix(write): ...` with the count before and after. Then add the CI step and commit `ci: check the thesis page count against Word's PDF` together with `word.pdf`. If the count cannot be matched by rules that are correct for other files too, stop and report to the owner with the numbers instead of fitting the thesis.

### Task 16: The Write app: project, files, window and open flow

**Files:**
- Create: `src/Raffica.Write/Raffica.Write.csproj` (`WinExe`, `AssemblyName` `RafficaWrite`, `Product` "Raffica Write", `PublishAot`, `AvaloniaUseCompiledBindingsByDefault`, `SatelliteResourceLanguages` en;de; references Core, Shell, Hosting, Write.Model, Write.Docx, Write.Layout, `Avalonia.Desktop`; `TrimmerRootAssembly` Shell and Fluent; the placeholder icon `Assets/raffica-write.ico` from `Docs/design/icons/brand/`), `Program.cs` (the `AppContext.GetData("System.GC.Server")` first statement, as `ProgramStartsWithTheAppContextWorkaround` pins for PDF; single instance through `AppFiles.Write.InstanceName`; hand-over; `BuildAvaloniaApp` with `RenderingChoiceParser`), `WriteOptions.cs` (`--lang`, `--theme`, `--high-contrast`, `--settings-file`, `--print-string`, one `--measure-*` switch, files), `App.axaml(.cs)` (`ShellTheme.Apply(..., AppInk.Write, ...)`; macOS `WindowHost`, activation bridge and menu as in `Raffica.Pdf`'s `App`), `WriteWindow.axaml(.cs)` (frame, title bar mark, document tabs, start view with `StartTile.OpenDocumentOnly()` and `OwnApp = StartApp.Write`, notification bar, status bar), `Documents/WriteTab.cs`, `WriteWorkspace.cs` (over `TabList<WriteTab>`), `OpenFlow.cs`, `Dialogs/OpenErrorCard.cs`, `Settings/WriteSettings.cs` (record: `DefaultView`, `DefaultZoomMode`; JSON through Core's `JsonFile` with a source-generated context) and `WriteSettingsController.cs` (the shell settings dialog plus two rows), `Platform/MacOS/WriteMacMenu.cs`, `Resources/Strings.en.resx` and `.de.resx`, `WriteStrings.cs`, `PrintStringCommand.cs`, `tests/Raffica.Write.App.Tests/` (headless with Skia, as the PDF tests, `RepoPaths`, `TestApp`).
- Modify: `src/Raffica.Shell/Frame/StartTile.cs` (`OpenDocumentOnly()`; `ShellStrings.StartOpenDocument` in both resx), `FrameKeys.cs` and `SearchField.cs` (an app-level `SearchShortcut` option: `CtrlK` for PDF, `AltQ` for Write; on Windows and Linux `Alt+Q` never matches when Ctrl is also down, so AltGr+Q types `@`; on macOS the Write option keeps ⌘K until 4.5), `Raffica.slnx`.

**Interfaces:** `OpenFlow.OpenAsync(string path, CancellationToken)` returns `OpenResult` (`Opened(WriteTab)` | `Failed(OpenError)`); `OpenError { NotFound, AccessDenied, NotADocx, Corrupt, TooLarge, Other }` with wording in both languages (guard test like `RedactWordingTests`); opening runs on a pool thread: `DocxFile.OpenPrefix(path, 64)`, `FontCatalog.CreateShipped()` (one per process, made at first open), `DocumentLayout.Create(..., Pages)`, `LayOutFirstPage()`; the UI thread then adds the tab and shows page 1; the full `DocxFile.Open` and `LayOutRestAsync` follow on the pool. The same path (compared with `PathRules`) opened twice focuses the existing tab.

- [ ] **Step 1: Tests.** `WriteOptionsTests` (the PDF option tests' cases for the shared switches), `WriteProjectTests` (references; no `Raffica.Pdf`), `ProgramStartsWithTheAppContextWorkaround`, `PrintStringPrintsAnEnglishAndAGermanValue`, `StartupCreatesNoLayoutOrFontWork` (a window made with no file: `FontCatalog`, `DocumentLayout` and `DocxFile` were never constructed: counters on internal factories), `StartViewShowsRecentFiles`, `OpenFailureShowsACardAndMakesNoTab` (not a DOCX, zero bytes, a corrupt zip, deleted before open, locked: card text per error, tab count 0, window alive), `OnlyFileNotFoundRemovesARecentEntry`, `OpenSameFileTwiceFocusesTheTab` (also by a different spelling of the path), `HandoverOpensFilesInTheRunningWindow` (as the PDF hand-over tests), `SecondInstanceNameDiffersFromPdf`, `TranslationTests` and `WordingStyleTests` equivalents (every key in both files, no "du"), `SearchShortcutIsAltQ` (Alt+Q focuses the search; Ctrl+Alt+Q does not), `PdfKeepsCtrlK`.
- [ ] **Step 2:** `dotnet test tests/Raffica.Write.App.Tests -c Release` → fails (no project). Check the discovered count.
- [ ] **Step 3: Implement** as listed. The settings are read once at start and handed to the window (opening a tab reads no file); `WriteMacMenu` builds the application menu (About is not there yet: Hide, Quit through `WindowHost`) and per window File (Open, Close tab), View (page view, writing view, zoom) and Window, attached to each window as `CLAUDE.md` says for the PDF app.
- [ ] **Step 4: Verify.** The step 2 command passes; `dotnet build Raffica.slnx -c Release`; `dotnet format Raffica.slnx --verify-no-changes`; `dotnet test tests/Raffica.Pdf.Tests -c Release` still green (the shell change). Start the app from the build (`dotnet run --project src/Raffica.Write -c Release -- --settings-file <temp>\s.json testdata/write-corpus/poi-test-document/poi-test-document.docx`) and see the start view and the opened tab.
- [ ] **Step 5: Commit** (`feat(write): the Write app window, tabs, start view and open flow`).

### Task 17: Page view, writing view, zoom and the ribbon

**Files:**
- Create: `src/Raffica.Write/Viewing/PageView.cs`, `PageScroller.cs` (pure math: page rectangles, visible range, scroll offset ↔ `ViewAnchor`, zoom around a point), `PageDrawOperation.cs` (`ICustomDrawOperation` that takes `ISkiaSharpApiLeaseFeature` and calls `PageRenderer.Draw`), `ViewAnchor.cs` (body block index plus character offset), `LayoutRunner.cs` (owns the background layout of a tab: start, progress, cancel, dispose), `WriteRibbon.cs`, `Commands/WriteCommandCatalog.cs`, `WriteCommandRules.cs`, `Commands/WriteCommandIds.cs`, `Styles/PageView.axaml`, icons in `src/Raffica.Shell/Styles/Icons.axaml` (own Lucide key for every new command), tests `PageScrollerTests.cs`, `PageViewTests.cs`, `WriteCommandTests.cs`.
- Modify: `WriteWindow.*.cs` (the document slot, status bar page text `Page 3 of 311` with the final count shown once the layout is complete and `Page 3 of 311+` before), `WriteSettingsController.cs`.

**Interfaces:** commands `file.open`, `file.close`, `view.page`, `view.writing` (a switch: `toggle: true`), `view.zoomIn`, `view.zoomOut`, `view.actualSize`, `view.fitWidth`, `view.fitPage`, `view.outline` (switch), `app.settings`, `app.search`; ribbon tabs Home and View with field letters (`H`, `V`), ids as listed; every disabled command with a document open has a reason in `WriteCommandRules.ReasonFor` (a test walks every id in every state, as `PdfCommandRulesTests` does). `PageScroller(IReadOnlyList<(float Width, float Height)> pages, float gap, double zoom)`: `Rect RectOf(int page)`, `(int First, int Last) Visible(double offset, double viewport)`, `ViewAnchor AnchorAt(double offset)`, `double OffsetOf(ViewAnchor)`. Zoom steps and modes come from Core's `ZoomSteps` and `ZoomMode`. Switching view recreates the layout in the other mode (`DocumentLayout.Create` with `Flow`) and restores the scroll position through the `ViewAnchor`; both layouts are kept after first use.

- [ ] **Step 1: Tests.** `PageScrollerTests` (`RectsStackWithGap`, `VisibleRangeAtOffsets`, `AnchorRoundTrip`, `ZoomKeepsThePointUnderTheCursor`, `ExtentGrowsWhenPagesArePublished`), `PageViewTests` (headless Skia: `FirstPageIsDrawn` raises `FirstSharpPage` once with dark pixels in the text area; `WheelScrollsAndClamps`; `CtrlWheelZooms`; `PageDownMovesOnePage`; `HomeAndEndGoToTheEnds`; `PagesAppearWhileTheLayoutGoesOn`; `NoPaperChromeInWritingView`), `WriteCommandTests` (`EveryCommandHasItsOwnIcon`, `EveryDisabledCommandHasAReason`, `ViewSwitchKeepsTheAnchor` (scroll to a heading on page 40 in the page view, switch, the same heading is within one screen of the top; and back), `ZoomModesFitWidthAndFitPage`, `ClosingATabCancelsItsLayout` (the token is cancelled, `LayoutRunner` is disposed, no exception reaches the crash-less log, a second tab keeps working), `ClosingTheWindowWhileLayingOutIsClean`), `DefaultViewAndZoomFromSettings`.
- [ ] **Step 2:** `dotnet test tests/Raffica.Write.App.Tests -c Release --filter-class "*PageScrollerTests"` and the other classes → fail.
- [ ] **Step 3: Implement.** Only the visible pages are drawn; `PageView` asks `DocumentLayout.LaidOutPages` on each frame and redraws when the count or the viewport changed; the background layout posts progress to the UI thread at most every 100 ms. Paper, shadow and the page gap are drawn by the view with shell tokens (add `Raffica.Color.*` tokens only if none fits, and classify them).
- [ ] **Step 4: Verify** the three classes pass; run the app on the thesis and look at the first page, a table page, the footnotes and the writing view (**the live pass on the Native AOT build is Task 19 step 4**).
- [ ] **Step 5: Commit** (`feat(write): page view, writing view, zoom and ribbon`).

### Task 18: The outline pane

**Files:**
- Create: `src/Raffica.Write/Panes/OutlinePane.cs`, `OutlineSource.cs`, `Styles/OutlinePane.axaml`, `tests/Raffica.Write.App.Tests/OutlinePaneTests.cs`.
- Modify: `WriteWindow.*.cs` (left pane, 240 px, closed until asked for, toggled by `view.outline`).

**Interfaces:** `OutlineSource.Read(WordDocument) → IReadOnlyList<OutlineEntry>` (`Level` 1 to 9, `Text`, `BodyBlockIndex`; a paragraph is a heading when its resolved outline level is set, text from its runs with field results, empty headings skipped); `OutlinePane` raises `Navigate(int bodyBlockIndex)`; the window answers with `DocumentLayout.LayOutThrough` for the page that `Locate` needs (a navigation target beyond the laid-out pages waits for the layout up to it, with the status bar saying so, never blocking the UI thread) and then scrolls so the heading is at the top.

- [ ] **Step 1: Tests.** `ThesisOutlineHasThirtyChaptersAndTheirSections`, `LevelsFollowOutlineLevelNotStyleNames`, `EmptyHeadingsAreSkipped`, `NavigateToALateHeadingWaitsForTheLayout` (before the layout is complete the target is reached after it reaches that page, the UI thread is not blocked: a dispatcher probe keeps ticking), `KeyboardNavigation` (Up, Down, Enter, Home, End; the pane is a tree with names for assistive technology), `PaneIsClosedAndUnbuiltUntilAskedFor` (no pane object before the first `view.outline`), `OutlineOfADocumentWithoutHeadingsSaysSo`.
- [ ] **Step 2:** `--filter-class "*OutlinePaneTests"` → fails. **Step 3: Implement. Step 4: Verify. Step 5: Commit** (`feat(write): outline pane for navigation`).

### Task 19: Measurement, budgets and CI

**Files:**
- Create: `src/Raffica.Write/Measurement.cs`, `tests/Raffica.Write.App.Tests/MeasurementTests.cs`, `testdata/write-corpus/README.md` (the files the cases use: `poi-test-document/poi-test-document.docx` for cold and warm, `poi-comment/poi-comment.docx` as the second file, `thesis/thesis.docx` for the rest), `Docs/measurements/write-viewer-2026-11-xx/` (CSVs, after the owner's runs).
- Modify: `bench/shell-probe/measure.cs` (target `write`: `WriteRun` next to `PdfRun`, cases `cold`, `warm`, `thesis`, `second`, `scroll`, `memory`, `engine` (runs `write-layout-bench run`); `size` and `check` accept `--target write` with `RafficaWrite` as the main exe; `s_writeBudgets` table; selftests for every new path as the PDF target has them), `.github/workflows/measure.yml` (option `write` in `target`, builds and publishes `src/Raffica.Write` and `bench/write-layout`, `ref` rules unchanged), `ci.yml` (publish of the Write app with the `IL[23][0-9]{3}` log search, `--print-string` smoke test in English and German on Linux and Windows, "Size of the published Write app" recording form, and the same for both architectures in `build-macos`; the engine bench publish of Task 13).

**Interfaces:** the app's `Measurement` has the protocol of the PDF app (`first-page` when page 1 is drawn, `second-page ms=`, `scroll p99-ms= max-ms=` over 600 wheel steps down the thesis, `memory-bytes <n>` two seconds after the first page); the first page is "the first sharp page in the window": the second animation frame after `PageView` raised `FirstSharpPage`. The `engine` case writes the row `write/engine` with the bench's median. The budget table `s_writeBudgets` is filled only from the owner's runs (step 3); until then `check --target write` exits 2 ("no limits recorded for target write"), as the PDF target did before its budgets existed. The engine entry is the exception that is a fixed limit: budget 80, so that budget × 1.25 is the spec's 100 ms.

- [ ] **Step 1: Tests.** `MeasurementTests` (the lines of each switch in the invariant culture, the 30 s watchdog exit code 3, a scroll run with no document exits 1, a measurement run never reads or writes the user's files: `--settings-file` required, as `MeasurementTests` of the PDF app asserts), and the new `measure.cs selftest` cases (`write run: arguments`, `write run: an unknown case is a usage error`, `write check: an empty table exits 2`, `write check: values at the limit exit 0, 0.1 above exit 1`, `size --target write`). Run `dotnet run bench/shell-probe/measure.cs -- selftest`: all PDF and gallery cases stay green.
- [ ] **Step 2:** The test commands fail first (missing switch, missing target). **Implement.**
- [ ] **Step 3: Owner (measure, then set).** On the Windows PC, the Linux laptop and the Intel Mac: `gh workflow run measure.yml --ref dev -f platform=<windows|linux|macos> -f target=write -f note="<machine note>" -f ref=<branch or commit>` (the same procedure as `bench/shell-probe/RUNNERS.md`), and `-f platform=macos-hosted` for the record. Commit the CSVs under `Docs/measurements/write-viewer-2026-11-xx/`; fill `s_writeBudgets` with each median (time to 10 ms, memory to 5 MiB, size to 1 MiB, as M1 and M2.1 round), the limit is budget × 1.25; add the control run rule: a time row is a verdict only with a Pagewing control run of the same day (`target=reference`). A miss of the engine limit on the laptop is a finding for the owner, never a loosened limit. Record the medians, the limits and the method in `Docs/DECISIONS.md`, "Raffica Write: speed budgets (M4.2)".
- [ ] **Step 4: Live pass on the Native AOT build** (the owner's rule from M2.7: headless JIT tests never showed a tab list that was empty on first open and a window that grew by 30 px per start). Publish `src/Raffica.Write` with `-r win-x64` and look at: start view, opening the thesis, first page, scrolling, zoom with Ctrl+wheel, both views, the outline pane and a click on a late heading, a second file from a second launch, closing during the layout, German and English, light and dark, the high-contrast theme. List each finding; fix those that are 4.2's; open items go to the roadmap's open list with the command that checks them. Linux and macOS are the owner's (Intel Mac) or CI's; mark them open until seen.
- [ ] **Step 5: Verify and commit.** `dotnet run bench/shell-probe/measure.cs -- check --target write --platform <p> --in "<csv glob>"` exits 0 on the committed CSVs for each platform; `size --check <rid> --target write --publish-dir <dir> --fonts-dir src/Raffica.Shell/Assets/Fonts` exits 0 for `win-x64` and `linux-x64` (and `osx-*` in CI); commit in two steps: `feat(write): measurement switches and the write target` (before the owner's runs) and `docs: Write speed budgets (M4.2)` (with the table and the CSVs).

### Task 20: Documents, roadmap and final verification

**Files:**
- Modify: `Docs/DECISIONS.md` (entries "Raffica Write: the layout library and the viewer (M4.2)" and, from Task 19, "Raffica Write: speed budgets (M4.2)"), `CLAUDE.md` (section "Write viewer (M4.2)" under "Project-specific": where the code is, the Layout API names, the pitfalls found, the `Raffica.Hosting` move, the owner gates), `Docs/roadmap.json`, `Docs/ROADMAP.md`, `Docs/write-corpus.md`, `testdata/write-corpus/README.md`.

- [ ] **Step 1:** The decision entry names: the project split and why the layout takes no Avalonia type; the own UAX #14 breaker and the Unicode version; the font fallback order (shipped and aliases, installed, shipped default) and that no Liberation font ships; the accepted-changes view; the placeholder rule; the writing view as strips; the `Raffica.Hosting` contents and what stayed in `Raffica.Pdf` and why (crash guard, update flow, default-app, print; the macOS menu binder); the prefix open; the levers kept and dropped from Task 14 with their measured effect; the hyphenation pattern sets that shipped and those left out for licence; the alt+Q frame option; known limits (EMF and WMF are placeholders, tight and through wrapping drawn as square, no East Asian layout rules, right-to-left shown left to right without the note until 4.8, field results other than `PAGE`, `NUMPAGES`, `SECTIONPAGES`, `SECTION` are the cached ones, no hit test or caret yet). The `CLAUDE.md` section follows the style of the "Write library (M4.1)" section and names types, never line numbers.
- [ ] **Step 2: Roadmap.** Tick each task of milestone 4.2 as its plan task finished. Tick acceptance criteria only with proof: criterion 1 with the laptop's median from Task 14 step 4 (`Docs/measurements/...`) or leave it open with that command; criterion 2 with `Docs/DECISIONS.md` and a green `measure.yml` check run; criterion 3 with `write-corpus compare --id thesis --require-same-page-count` exit 0 and the regenerated `Docs/write-corpus.md`. Whatever needs the owner (Word's PDF, the three machines, the Intel Mac live pass, the Wayland and X11 look) stays in the milestone's open list with its command. Status `done` with `completedOn` only when no owner gate of the three criteria is open; otherwise leave `in-progress` and say which.
- [ ] **Step 3: Verify.** `dotnet build Raffica.slnx -c Release`; `RAFFICA_REQUIRE_OCR=1 dotnet test Raffica.slnx -c Release --no-build --max-parallel-test-modules 1`; `dotnet format Raffica.slnx --verify-no-changes`; `dotnet run tools/roadmap.cs -- generate` then `-- check`; `dotnet run tools/gen-linebreak.cs -- --check`; `dotnet run bench/shell-probe/measure.cs -- selftest`; the AOT publishes of `src/Raffica.Write`, `src/Raffica.Pdf` and `bench/write-layout` without `IL[23][0-9]{3}`. Expected: every command exits 0; name each result in the PR.
- [ ] **Step 4: PR** to `dev` (squash allowed), body: what the milestone delivers, the commands and results, the open owner gates. Merge when all CI checks are green (CI needs the `ci:macos` label or a path from `.github/scripts/macos-paths.sh` for the macOS job; `Raffica.Write` and `Raffica.Hosting` paths must be in that list: check it). Then `git switch dev && git pull`.

---

## Self-review

- **Spec coverage.** Section 7 row 4.2: the Write app on the frame (Task 16), shared code moved (Task 2), layout library with the section 6 features (Tasks 3 to 12: fonts and fallback, breaks, styles, paragraphs with tabs, justification, spacing, hyphenation, widows, keep rules, sections, columns, headers and footers, notes, tables, pictures, floats, placeholders), page view and writing view, scrolling, zoom, outline (Tasks 17, 18), the shipped fonts (Task 3), first-page startup work (Task 14), budgets (Task 19), corpus table and thesis page count (Task 15). Section 5 limits: Task 13 and 14. Spec "the app creates nothing before first use": Task 16 `StartupCreatesNoLayoutOrFontWork`.
- **Left out on purpose** (later milestones): caret, selection, hit test (4.3), links and their confirmation (4.5), comments and tracked-change UI (4.6), print and PDF export (4.7), crash handling and hostile files (4.8), packaging, default app and updates (4.9).
- **Open risks, named.** Matching Word's page count of a 311-page document needs Word's PDF and may need several rule fixes (Task 15 step 6 says when to stop). The 100 ms first page on the laptop was 108.9 ms in the spike and 4.2 shortens it by measured levers only (Task 14). The own UAX #14 breaker is a sizeable task whose conformance file is the proof (Task 4).
