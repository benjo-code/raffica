# PDFium – third-party licenses

Raffica uses the precompiled PDFium binaries from [bblanchon/pdfium-binaries](https://github.com/bblanchon/pdfium-binaries) through the NuGet packages `bblanchon.PDFium.Win32` and `bblanchon.PDFium.Linux` (without V8, without XFA).

## Version

- NuGet packages: `157.0.8100` (see `Directory.Packages.props`)
- `VERSION` file of the release archives: `MAJOR=157`, `MINOR=0`, `BUILD=8100`, `PATCH=0`
- Release: `chromium/8100`

## Source

The NuGet packages themselves contain **no** license files (only binaries, import libraries, headers and a README). The licenses in this folder therefore come from the release archives of the same version:

| Archive | SHA-256 |
|---|---|
| `https://github.com/bblanchon/pdfium-binaries/releases/download/chromium%2F8100/pdfium-win-x64.tgz` | `51621bcc1d9be9ea448f4b84b0516862b928e3968d3fca899af1a6d6e2848d6d` |
| `https://github.com/bblanchon/pdfium-binaries/releases/download/chromium%2F8100/pdfium-linux-x64.tgz` | `1c1894a12d4d96ee01400c3874444126254180c5c7d2ab628576b67f8120320a` |

Taken over are the file `LICENSE` (license of Benoit Blanchon's build scripts, MIT) and the folder `licenses/` (PDFium itself and the libraries linked into it, such as FreeType, ICU, libjpeg-turbo, OpenJPEG, libpng, zlib, Little CMS, HarfBuzz, Abseil).

## Differences between the archives

Both archives contain the same files with the same content. They differ only in the line endings: in the Windows archive some files have CRLF (the `LICENSE` mixed CRLF/LF), in the Linux archive all have LF. The files were taken from the Linux archive, which matches the LF rule of the repository (`.gitattributes`). `licenses/freetype.txt` is ISO-8859-1 encoded in both archives and stays unchanged.

The nuspec of the NuGet packages names `Apache-2.0` as the license; the `LICENSE` of the archives is MIT. Both are compatible with GPL-3.0.

## Build arguments

From `args.gn` of both archives (relevant for Raffica):

```
pdf_enable_v8 = false
pdf_enable_xfa = false
```

So no JavaScript runs in PDFs, and XFA forms are not supported.

## Shipping

Shipping the licenses in the installation package and showing them in the About dialog come with the packaging and UI milestones.
