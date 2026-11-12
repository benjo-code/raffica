# Spike 2: layout engine (C# against Rust with Parley)

Status: both candidates are built and measured on Windows (the owner's PC), on the binding Linux laptop and on GitHub-hosted runners. The decision is recorded in `Docs/DECISIONS.md`, "Raffica Write: the layout engine (M4)": **C#**.

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
| **Linux laptop (HP ProBook, i5-8265U), binding** | C# | **108.9 ms** | 403 ms | 0.26 ms | 0.79 us | 86 MB |
| | Rust | **110.5 ms** | 392 ms | 0.26 ms | 3.99 us | 131 MB |
| Intel Mac (owner's) | C# and Rust | open (a record, cannot change the decision) | | | | |

On the binding Linux laptop both candidates miss only the first-page limit (100 ms) by about 10 %; every other value is far inside its limit. The cause is shared startup work, not the engine: laying out and drawing page 1 takes about 5 ms, and `layout-bench child first-page-phases` (Windows PC) shows about 30 ms from process start to `Main`, about 30 ms to load the eight font files, about 15 ms to read the DOCX, 0.6 ms of layout and 4 ms of drawing. The hosted rows are virtual machines: a record, not the binding numbers (the hosted Intel Mac measured 84.5 ms and 99.7 ms for the same C# build in two runs).

Line breaking: **0 of 1,484 paragraphs differ** between HarfBuzz with UAX #14 (C#) and Parley (Rust); both give 9,532 lines (`compare-lines`, the same on Windows, Linux and macOS arm64).

Native size added by Rust: `parley_core.dll` 1.74 MB (win-x64), `libparley_core.so` 2.19 MB (linux-x64), `libparley_core.dylib` 2.00 MB (macOS arm64), 2.07 MB (macOS x64). Rust also costs about 50 MB more working memory (107 MB against 58 MB on Windows) and about 5 times the hit-test time (a call across the C boundary per query).

## Open (optional): the owner's Intel Mac

A record only, it cannot change the decision. On mains power with the display on: install `rustup` and the Xcode command line tools, run the block under "Run it" with `<rid>` = `osx-x64` and `--machine macos-intel` for both engines, and send back the CSV.
