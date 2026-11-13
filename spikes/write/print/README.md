# Spike 3: Write prints itself

Question: how does Write print itself, the way each operating system expects, without exporting a PDF for another program to print?

- **Windows, route a (Word's way):** the laid-out pages are drawn straight onto the printer's device context with GDI: glyph runs with `ExtTextOutW` and `ETO_GLYPH_INDEX` (the glyph ids come from HarfBuzz, through `IGlyphSource` of the C# candidate), table borders and shading with GDI pens and brushes, images with `StretchDIBits`. The eight font files are added to the process with `AddFontMemResourceEx`. `GdiPagePrinter.cs`.
- **Windows, route b (measured for comparison):** the pages drawn into a PDF in memory (`SKDocument.CreatePdf`), opened as Raffica's `PdfDocument` and printed into the device context by PDFium, as `WindowsPrintService` of Raffica PDF does. `PdfRoute.cs`.
- **Linux and macOS:** the PDF in memory is handed to CUPS as a job: `cupsCreateJob`, `cupsStartDocument` (`application/pdf`), `cupsWriteRequestData` in 64 KiB chunks, `cupsFinishDocument`. `CupsJob.cs`. The window lists the CUPS printers (`cupsGetDests2`), which stands in for Write's own print dialog.
- Interop is copied into `Interop/` (`Gdi.cs`, `PrintDialogNative.cs` trimmed, `CupsNative.cs` extended), each with a header comment naming its source in `src/Raffica.Pdf/Platform`; the spike references `Raffica.Core` (for route b) and never `Raffica.Pdf` or `Raffica.Shell`.
- Licences: Avalonia 12.1.5 MIT.

## Run it

```
dotnet run --project spikes/write/print -c Release                      # the window; Windows: two Print buttons, Linux and macOS: a printer list and Print
dotnet run --project spikes/write/print -c Release -- --pdf out.pdf     # every system: the PDF of route b and CUPS, to a file
dotnet run --project spikes/write/print -c Release -- --cups [printer]  # Linux and macOS, no window: list the printers, print to one
dotnet run --project spikes/write/print -c Release -- --auto a "Microsoft Print to PDF" out.pdf   # Windows, no dialog: route a (or b) into a file
dotnet run --project spikes/write/print -c Release -- --text out.pdf 3  # the text PDFium finds on page 3 of a PDF
```

## Results (2026-11-12)

### Windows, "Microsoft Print to PDF" (Ryzen 7 7800X3D, Windows 11), 311 pages, commit `c6bd243`

| | Route a (GDI direct) | Route b (PDF in memory, PDFium) |
|---|---|---|
| Time until the whole job is handed over | **11.0 s** (`EndDoc` returned; the file was complete 12.1 s after the start) | 0.7 s to make the PDF in memory, **460.6 s** to print it through PDFium (461.3 s in total) |
| Size of the print output | **1.66 MB** (311 pages) | **100.0 MB** (311 pages) |
| Text in the output | real text: 4 embedded fonts (Type 0, `FontFile2`, `ToUnicode`), 5 images; PDFium finds the text of page 3 (`Seeded thesis 1 1 Vitavontcar ge taetufon ...`, the footer "1") and of page 311 (`Appendix A-8`); page 3 shows "1", the first appendix page "A-1" (see `GdiPagePrinter` and `PageNumbers`) | none: PDFium draws every page as a picture, 0 characters of text on page 3, 0 fonts, 0 images in the first 3 MB |
| Note | The no-break space after a heading number comes back as a replacement character in the text of the output (the driver's `ToUnicode` map has no entry for it) | |

Route a is the clear winner on time (42 times faster), on size (60 times smaller) and on what the output is (text, not pictures); it is also what the owner asked for (Write prints itself). Route b through PDFium is not usable for long documents.

The PDF in memory, which CUPS gets: 2.17 MB for 311 pages, made in 0.7 s on Windows (`--pdf`), 0.66 s on macOS arm64 and 0.85 s on Linux (hosted).

### Linux and macOS, hosted runners (workflow `spikes-write`, a virtual CUPS queue each)

| System | Queue | Result |
|---|---|---|
| Ubuntu 24.04 (CUPS 2.4.7) | `PDF` (`cups-pdf`) | `cupsCreateJob`, `cupsStartDocument`, `cupsWriteRequestData` (64 KiB chunks), `cupsFinishDocument` all succeed; job 1, 2.17 MB, handed over in 791 ms in total (PDF made in memory in 772 ms), the job shows as completed in `lpstat` |
| macOS 15 arm64 | `spikenull` (`file:///dev/null`) | the same calls succeed; job 1, handed over in 511 ms in total (PDF in memory 487 ms), job size 2,167,808 bytes in the completed list |

No temporary file was needed: streaming a PDF from memory works on both. A real printer on Linux and on the Intel Mac still has to confirm sharp text and page numbers (open, below).

### Linux laptop (the binding machine), CUPS with a printer chosen in the window, 2026-11-13

| Page range | Result reported by the owner |
|---|---|
| `1-6` | CUPS job 1: PDF 604 KiB in memory (59 ms), handed over in 84 ms in total |
| `304-311` | CUPS job 2: PDF 618 KiB in memory (66 ms), handed over in 73 ms in total |

Both jobs were accepted by CUPS through `cupsCreateJob` and the streaming calls, and a range of pages is made in about 60 ms. On paper (owner, 2026-11-13) the page number "1" stands on page 3 of the range `1-6` and "A-1" on the first page of `304-311`. Sharpness of the text was not reported separately.

### Windows, a real printer (owner, 2026-11-13), route a from the window

| Page range | Result |
|---|---|
| `1-6` | 6 pages handed to the spooler in 215 ms |
| `304-311` | 8 pages handed to the spooler in 162 ms |

On paper the owner found everything fine ("passt alles"): sharp text, "1" on page 3 of the first range, "A-1" on the first page of the second, margins, table borders and images. The make and model of the printer were not reported.

## Open (needs the owner's hands)

- **Intel Mac, a real printer:** `dotnet run --project spikes/write/print -c Release`, choose the printer, Print; sharp text, page numbers; record the time and the job size.
- The first-page speed of the window and the print dialog of Write are questions for the spec (shared print project with Raffica PDF, Write's own dialog, the native macOS print panel from the backlog).

## What failed or was left out

- Route a2 (Skia XPS) was not needed: GDI glyph output gave vector text on the first try.
- The Windows dialog result is reduced to the printer's device context and the page range; duplex, orientation and scaling (`DEVMODE`) are not mapped (Write's dialog decides that).
- The spike prints only what the spike's layout knows: left and centred text, tables with borders, inline images, footnotes, headers and footers. A footnote reference mark has no glyph.
