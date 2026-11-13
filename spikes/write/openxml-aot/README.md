# Spike 1: Open XML SDK under Native AOT

Question: can a Native AOT app open, edit and save DOCX and XLSX with the Open XML SDK 3.x, and do untouched parts survive?

- Package: `DocumentFormat.OpenXml` **3.5.1** (MIT, licence check done), pinned in `OpenXmlAot.csproj`. `PublishAot` is on, `IsAotCompatible` is on, `TrimmerSingleWarn` is off so every trim or AOT warning is listed.
- The test document is `spikes/write/testdoc/thesis.docx` (generated, see `../testdoc/README.md`).

## Commands

```
openxml-aot docx <in> <out> [--runs N]     # change the first body paragraph of chapter 1 and the size of Heading2 (15 pt); --runs N: median of N runs after 2 warm-ups
openxml-aot xlsx-make <plain.xlsx> <formulas.xlsx>
openxml-aot xlsx <in> <out> [--first-part] # set B2 of the first sheet to 42; --first-part finds the sheet without loading workbook.xml
openxml-aot compare <before> <after> [--touched <part>,<part>...]   # decompressed bytes per part; exit 1 for a changed, added or removed part that is not listed
openxml-aot selftest [docx]                # checks compare itself
```

Part names in `--touched` work with or without the leading slash. Git Bash rewrites an argument that starts with `/` into a Windows path; write `word/document.xml` or set `MSYS_NO_PATHCONV=1`.

## Run it

```
export PATH="$PATH:/c/Program Files (x86)/Microsoft Visual Studio/Installer"   # Windows only: vswhere for the AOT link
dotnet publish spikes/write/openxml-aot -c Release -r win-x64      # linux-x64 / osx-x64 on the other machines
X=spikes/write/openxml-aot/bin/Release/net10.0/<rid>/publish/openxml-aot
mkdir -p artifacts/spikes/write/oxa
$X selftest
$X docx spikes/write/testdoc/thesis.docx artifacts/spikes/write/oxa/thesis-edited.docx
$X compare spikes/write/testdoc/thesis.docx artifacts/spikes/write/oxa/thesis-edited.docx --touched word/document.xml,word/styles.xml
$X xlsx-make artifacts/spikes/write/oxa/plain.xlsx artifacts/spikes/write/oxa/formulas.xlsx
for f in plain formulas; do
  $X xlsx artifacts/spikes/write/oxa/$f.xlsx artifacts/spikes/write/oxa/$f-edited.xlsx --first-part
  $X compare artifacts/spikes/write/oxa/$f.xlsx artifacts/spikes/write/oxa/$f-edited.xlsx --touched xl/worksheets/sheet1.xml
done
$X docx spikes/write/testdoc/thesis.docx artifacts/spikes/write/oxa/bench.docx --runs 10
```

On Windows replace `$X` by `...\win-x64\publish\openxml-aot.exe`. Optional on the owner's Linux laptop and Mac: the same with `-r linux-x64` or `-r osx-x64` (Linux needs `clang` and `zlib1g-dev`; macOS needs the Xcode command line tools) and send back the output.

## Results

### Windows, win-x64 (Ryzen 7 7800X3D, .NET SDK 10.0.403), built on top of commit `875d99d` plus this spike's commit

| Item | Result |
|---|---|
| Publish, `dotnet publish -c Release -r win-x64` | succeeds; **0 `IL2xxx` / `IL3xxx` warnings** in the publish log, 0 compiler warnings after the nullable fixes |
| Published size | 31.9 MB (`openxml-aot.exe`, one file; the SDK's generated typed classes dominate it) |
| `selftest` (published exe) | 5 of 5 ok |
| `docx` on `thesis.docx`, `compare --touched word/document.xml,word/styles.xml` | exit 0; changed exactly `/word/document.xml` and `/word/styles.xml`, nothing added or removed. Footnotes, numbering, headers, footers, settings, images and the package properties are byte-identical on the decompressed bytes |
| `docx --runs 10` (open + edit + save of the 468 KB, 300-page DOCX) | median **149 ms** (AOT, includes the file copy) |
| `xlsx-make`, then `xlsx --first-part`, `compare --touched xl/worksheets/sheet1.xml` | exit 0 for both workbooks; only `/xl/worksheets/sheet1.xml` changed |
| `xlsx` without `--first-part` | exit 1: `/xl/workbook.xml` also changed (see below) |
| The edited DOCX through `testdoc verify` | ok, the edit text and the 15 pt style are in the saved file |

### What the SDK does to untouched parts

The SDK re-serialises every part whose DOM it loaded, also when the code only reads it. In the `xlsx` case that finds the sheet through the workbook, `/xl/workbook.xml` is loaded and written back with the namespace declarations in another place (same content, other bytes: `xmlns:r` moves from the `x:sheet` element to the root). A part that is never loaded stays byte-identical: with `--first-part`, which takes the first worksheet part without touching the workbook, only the sheet changes. The same holds for the DOCX: only the two loaded parts differ. Consequence for Write: keep a part unloaded unless it is edited, and treat a loaded part as rewritten (equal content, not equal bytes). Parts Write does not understand must never be loaded at all.

### GitHub-hosted runners (workflow `spikes-write`, run 43281967899, commit `b575087`, all three jobs green)

Hosted virtual machines, so the times are a record and not the binding numbers. Linux x64 on `ubuntu-24.04`, macOS arm64 on `macos-15`, macOS x64 on `macos-15-intel`; the .NET SDK 10.0.x of `actions/setup-dotnet`.

| Machine | Publish warnings | Published size | `docx` median of 10 | `selftest` | `compare` results |
|---|---|---|---|---|---|
| Linux x64 | 0 (`IL2xxx`/`IL3xxx`) | 27.3 MB | 101.3 ms | 5 of 5 ok | DOCX: only `/word/document.xml` and `/word/styles.xml` changed; both XLSX with `--first-part`: only `/xl/worksheets/sheet1.xml` |
| macOS arm64 | 0 | 25.6 MB | 103.3 ms | 5 of 5 ok | the same |
| macOS x64 (Intel) | 0 | 27.5 MB | 243.5 ms | 5 of 5 ok | the same |

LibreOffice (Linux job, `libreoffice-writer` and `libreoffice-calc`, `soffice --headless --convert-to pdf`): the edited DOCX (2.46 MB of PDF), `plain-edited.xlsx` and `formulas-edited.xlsx` all open and convert; no error.

### Word and Excel on Windows (owner, 2026-11-13)

The three saved files were opened by the owner (versions not reported). Checklist: opens without a repair prompt; the edit is visible (`Edited by the spike.` in chapter 1, Heading2 at 15 pt; cell B2 = 42; the formulas workbook shows SUM, VLOOKUP, the cross-sheet value, the merged title `Merged title` and the colour scale).

| File | Word / Excel (Windows) |
|---|---|
| `thesis-edited.docx` | all checks fine |
| `plain-edited.xlsx` | all checks fine |
| `formulas-edited.xlsx` | all checks fine (the merged title reads "Merged title", as the spike writes it) |

### Linux laptop and Intel Mac (binding machines, open)

The same commands on the owner's own machines (optional for this spike: the hosted runs above are the same operating systems and architectures; the owner's machines add the real-hardware timing).

### Plan B

Not needed so far: the AOT build on Windows runs every step without a warning. It runs only if the steps on Linux, macOS or in the Office programs fail (spec section 5: a thin layer over `System.IO.Packaging` and `XmlReader`/`XmlWriter`).
