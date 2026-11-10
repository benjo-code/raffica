#!/usr/bin/env bash
# Reads changed file names (one per line) on stdin. Exits 0 and prints the first platform-near file (one that can behave
# differently on macOS, spec section 7), otherwise exits 1. Plain bash 3.2: in `case`, `*` also matches `/`.
# It always reads all of stdin: a producer that is still writing (printf with a long list, under `set -o pipefail`) must not die
# of SIGPIPE, because the pipeline would then fail and the filter would answer "no match".
hit=
while IFS= read -r f; do
  [ -z "$hit" ] || continue
  case "$f" in
    src/*/Platform/*|src/Raffica.Pdf/Updating/*|src/Raffica.Pdf/Packaging/*|src/Raffica.Pdf/Hosting/*|src/Raffica.Pdf/App.axaml.cs|src/Raffica.Core/Hosting/*|src/Raffica.Pdfium/*|src/Raffica.Ocr/*) ;;
    packaging/*|tools/package.cs|tools/native-fetch.cs|tools/check-binary.cs) ;;
    Directory.*.props|global.json|*.csproj|.github/workflows/*|.github/scripts/*) ;;
    *) continue ;;
  esac
  hit=$f
done
if [ -n "$hit" ]; then printf '%s\n' "$hit"; exit 0; fi
exit 1
