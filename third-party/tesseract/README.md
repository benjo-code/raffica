# Tesseract, Leptonica and tessdata_fast – third-party licenses

Raffica uses the OCR library (`tesseract.dll` or `libtesseract.so`, Tesseract 5.5.4 with statically linked Leptonica 1.87.0) and the language data `eng` and `deu`. The library is built by the workflow `native-tesseract.yml` of this repository from the unmodified sources of the tags named below (without OpenMP, without libcurl, without libarchive, without image decoders). The prerelease is `native-tesseract-5.5.4-1`; `tools/native-fetch.cs` fetches the pinned files and checks their size and SHA-256.

| File | Component (version) | Source | SHA-256 of the file |
| --- | --- | --- | --- |
| `third-party/tesseract/LICENSE` | Tesseract OCR 5.5.4 (Apache-2.0) | `https://raw.githubusercontent.com/tesseract-ocr/tesseract/5.5.4/LICENSE` | `cfc7749b96f63bd31c3c42b5c471bf756814053e847c10f3eb003417bc523d30` |
| `third-party/leptonica/LICENSE` | Leptonica 1.87.0 (BSD-2-Clause) | `https://raw.githubusercontent.com/DanBloomberg/leptonica/1.87.0/leptonica-license.txt` | `87829abb5bbb00b55a107365da89e9a33f86c4250169e5a1e5588505be7d5806` |
| `third-party/tessdata-fast/LICENSE` | tessdata_fast 4.1.0 (Apache-2.0) | `https://raw.githubusercontent.com/tesseract-ocr/tessdata_fast/4.1.0/LICENSE` | `cfc7749b96f63bd31c3c42b5c471bf756814053e847c10f3eb003417bc523d30` |

The files are unchanged copies, fetched from the sources on 2026-11-02. The language data `eng.traineddata` and `deu.traineddata` come from `https://github.com/tesseract-ocr/tessdata_fast` (tag 4.1.0); their checksums are in `tools/native-fetch.cs`.
