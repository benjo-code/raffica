# Spike 2: layout engine (C# against Rust with Parley)

Status: both candidates are built and measured on Windows (the owner's PC) and on GitHub-hosted runners for Linux x64, macOS arm64 and macOS Intel. The binding numbers (the Linux laptop) are open, so the engine decision is not recorded yet (Task 6 of the plan).

- `Layout.Common`: `DocxReader` (streaming, Open XML SDK), `PageBreaker` (own page breaking, incremental re-layout), `PageRenderer`, `FontSet` (Carlito from `src/Raffica.Shell/Assets/Fonts`, Caladea from `spikes/write/fonts`; no system fonts), the candidate interface `IParagraphLayouter`.
- `Layout.CSharp`: the C# candidate (HarfBuzzSharp 8.3.1.3 shaping, UAX #14 breaks from Avalonia 12.1.5 `LineBreakEnumerator`, SkiaSharp 3.119.4 drawing). Licences: HarfBuzzSharp, SkiaSharp, Avalonia: MIT.
- `Layout.Rust` and `parley-core`: the Rust candidate. Parley 0.13.0 (Apache-2.0 or MIT) with default features off (no system fonts), a `cdylib` with the C interface `pc_*` (UTF-16 text and offsets, fonts registered from the bytes C# passes), called with `LibraryImport`. Built with `cargo build --release`; `LayoutBench` copies the library next to the exe. `RustParagraphLayouter` refuses to start unless `pc_build_profile()` says `release`.
- `LayoutBench`: `check`, `run`, `render`, `compare-lines`, `child`.

```
cargo build --release --manifest-path spikes/write/layout/parley-core/Cargo.toml     # needs rustup (stable); skip for the C# candidate only
dotnet publish spikes/write/layout/LayoutBench -c Release -r <rid>
B=spikes/write/layout/LayoutBench/bin/Release/net10.0/<rid>/publish/layout-bench
$B check --engine csharp            # and --engine rust
$B run --engine csharp --machine <label> --commit <short-sha> --out spikes/write/layout/results/<machine>.csv   # and --engine rust
$B compare-lines                    # line count per paragraph, C# against Rust
$B render <page> <out.png> --engine csharp      # one page as a picture; prints the page count
```

`run` refuses to measure unless it is a Native AOT publish. First page and full layout are cold child processes timed from spawn to the child's `done` line. The test document gives 311 pages in this engine.

Page-breaking rules (the same for every candidate): A4, 2.5 cm margins, header and footer per section with the page field, widow and orphan control off, a table breaks between rows (header rows repeat), a footnote sits at the bottom of the page whose line holds its reference (its space is reserved when the line is placed), headings keep with the next block, images are inline. Simplifications of the spike: left and centred alignment only, a tab is three no-break spaces, the footnote reference mark has no glyph, no kerning across runs, fonts are not hinted.

## Results (2026-11-12, medians of 10 runs after 2 warm-ups, Native AOT)

The checks (`FontsLoadFromSpikeFolder`, `ReaderSeesTheShape`, `IncrementalEqualsFull`, `HitTestRoundTrip`, `PageNumbers`) pass for both engines on all four systems. Each was seen to fail when the code it guards was broken (refill stopped after one page, page-number restart ignored, hit test shifted, a font file missing). The test document lays out to **311 pages**; Word's count is the owner's check (`../testdoc/README.md`).

Limits (spec section 5, binding on the Linux laptop): first page 100 ms, full layout 3 s, keystroke 16 ms, hit test 50 us per call; memory recorded.

| Machine (what it is) | Engine | First page | Full layout | Keystroke | Hit test | Memory |
|---|---|---|---|---|---|---|
| Windows, Ryzen 7 7800X3D (owner's PC) | C# | 86.3 ms | 451 ms | 0.34 ms | 0.73 us | 58 MB |
| | Rust | 90.5 ms | 429 ms | 0.27 ms | 4.34 us | 107 MB |
| Linux x64, GitHub-hosted `ubuntu-24.04` | C# | 38.8 ms | 266 ms | 0.18 ms | 0.66 us | 85 MB |
| | Rust | 46.0 ms | 289 ms | 0.19 ms | 3.60 us | 134 MB |
| macOS arm64, hosted `macos-15` | C# | 74.4 ms | 449 ms | 0.23 ms | 1.30 us | 69 MB |
| | Rust | 76.1 ms | 344 ms | 0.19 ms | 4.38 us | 122 MB |
| macOS Intel, hosted `macos-15-intel` | C# | 99.7 ms | 557 ms | 0.40 ms | 1.04 us | 64 MB |
| | Rust | 114.0 ms | 779 ms | 0.32 ms | 6.76 us | 104 MB |
| **Linux laptop (HP ProBook, i5-8265U), binding** | C# and Rust | open | open | open | open | open |
| Intel Mac (owner's) | C# and Rust | open | open | open | open | open |

All rows except one (CSVs in `results/`) meet every limit with room. The exception: on the hosted Intel Mac (a shared virtual machine; the same C# build measured 84.5 ms in the first run and 99.7 ms in the second) the Rust first page was 114 ms, above the 100 ms limit; the limits bind on the Linux laptop, not here. The hosted rows are virtual machines: a record, not the binding numbers (the hosted rows are virtual machines: a record, not the binding numbers). The laptop is slower than any of them, so the first-page limit (100 ms) is the one to watch: it is the cold-start metric that includes the process start, the fonts, the DOCX read and the first page.

Line breaking: **0 of 1,484 paragraphs differ** between HarfBuzz with UAX #14 (C#) and Parley (Rust); both give 9,532 lines (`compare-lines`, the same on Windows, Linux and macOS arm64).

Native size added by Rust: `parley_core.dll` 1.74 MB (win-x64), `libparley_core.so` 2.19 MB (linux-x64), `libparley_core.dylib` 2.00 MB (macOS arm64), 2.07 MB (macOS x64). Rust also costs about 50 MB more working memory (107 MB against 58 MB on Windows) and about 5 times the hit-test time (a call across the C boundary per query).

## Open: the binding machines (owner, 10 minutes each)

On the Linux laptop and the Intel Mac, on mains power and the balanced profile, with the display on (as `bench/shell-probe/RUNNERS.md`): install `rustup`, then run the block under "Run it" for `<rid>` = `linux-x64` or `osx-x64`, `--machine linux-probook` or `--machine macos-intel`, both engines, and send back the two CSVs and the `compare-lines` line. Linux needs `clang` and `zlib1g-dev`, macOS the Xcode command line tools. Then the layout engine decision is made by the rule (C# wins when both meet every limit).
