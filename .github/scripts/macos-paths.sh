#!/usr/bin/env bash
# Reads changed file names (one per line) on stdin. Exits 0 and prints the first platform-near file (one that can behave
# differently on macOS, spec section 7), otherwise exits 1. Plain bash 3.2: in `case`, `*` also matches `/`.
while IFS= read -r f; do
  case "$f" in
    src/*/Platform/*|src/Raffica.Pdf/Updating/*|src/Raffica.Pdf/Packaging/*|src/Raffica.Core/Hosting/*|src/Raffica.Pdfium/*|src/Raffica.Ocr/*) ;;
    packaging/*|tools/package.cs|tools/native-fetch.cs|tools/check-binary.cs) ;;
    Directory.*.props|*.csproj|.github/workflows/*|.github/scripts/*) ;;
    *) continue ;;
  esac
  printf '%s\n' "$f"
  exit 0
done
exit 1
