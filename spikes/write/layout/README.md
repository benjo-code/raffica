# Spike 2: layout engine (C# against Rust with Parley)

Status: the shared part and the C# candidate are done; the Rust candidate (Task 5) and the runs on the binding machines (Task 6) follow.

- `Layout.Common`: `DocxReader` (streaming, Open XML SDK), `PageBreaker` (own page breaking, incremental re-layout), `PageRenderer`, `FontSet` (Carlito from `src/Raffica.Shell/Assets/Fonts`, Caladea from `spikes/write/fonts`; no system fonts), the candidate interface `IParagraphLayouter`.
- `Layout.CSharp`: the C# candidate (HarfBuzzSharp 8.3.1.3 shaping, UAX #14 breaks from Avalonia 12.1.5 `LineBreakEnumerator`, SkiaSharp 3.119.4 drawing). Licences: HarfBuzzSharp, SkiaSharp, Avalonia: MIT.
- `LayoutBench`: `check`, `run`, `render`, `child`.

```
dotnet publish spikes/write/layout/LayoutBench -c Release -r <rid>
B=spikes/write/layout/LayoutBench/bin/Release/net10.0/<rid>/publish/layout-bench
$B check --engine csharp
$B run --engine csharp --machine <label> --commit <short-sha> --out spikes/write/layout/results/<machine>-csharp.csv
$B render <page> <out.png> --engine csharp      # one page as a picture; prints the page count
```

`run` refuses to measure unless it is a Native AOT publish. First page and full layout are cold child processes timed from spawn to the child's `done` line. The test document gives 311 pages in this engine.

Page-breaking rules (the same for every candidate): A4, 2.5 cm margins, header and footer per section with the page field, widow and orphan control off, a table breaks between rows (header rows repeat), a footnote sits at the bottom of the page whose line holds its reference (its space is reserved when the line is placed), headings keep with the next block, images are inline. Simplifications of the spike: left and centred alignment only, a tab is three no-break spaces, the footnote reference mark has no glyph, no kerning across runs, fonts are not hinted.

## Results

Windows (Ryzen 7 7800X3D), C#, Native AOT, 10 runs after 2 warm-ups (`results/windows-ryzen-7800x3d-csharp.csv`): first page 86 ms, full layout 451 ms, keystroke 0.34 ms (200 inserts, median), hit test 0.73 us per call, memory 58 MB. The limits bind on the Linux laptop (Task 6).
