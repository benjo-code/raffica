#!/usr/bin/env bash
# Test of macos-paths.sh: which changed files make the macOS job run. Plain bash, stops at the first wrong answer.
set -u
here=$(cd "$(dirname "$0")" && pwd)
script="$here/macos-paths.sh"

check() {
  local expect=$1 file=$2 out code
  out=$(printf '%s\n' "$file" | bash "$script"); code=$?
  if [ "$expect" = match ]; then
    if [ "$code" -ne 0 ] || [ "$out" != "$file" ]; then echo "FAIL: '$file' should match (exit $code, printed '$out')"; exit 1; fi
  else
    if [ "$code" -ne 1 ] || [ -n "$out" ]; then echo "FAIL: '$file' should not match (exit $code, printed '$out')"; exit 1; fi
  fi
  echo "ok: $expect $file"
}

check match src/Raffica.Pdf/Platform/Windows/X.cs
check match src/Raffica.Core/Hosting/SingleInstance.cs
check match src/Raffica.Pdfium/Native/Fpdf.cs
check match tools/package.cs
check match Directory.Packages.props
check match src/Raffica.Pdf/Raffica.Pdf.csproj
check match .github/workflows/ci.yml
check nomatch src/Raffica.Pdf/Panes/MarksPane.cs
check nomatch src/Raffica.Shell/Styles/Tokens.axaml
check nomatch tests/Raffica.Pdf.Tests/Panes/X.cs
check nomatch Docs/ROADMAP.md
check nomatch README.md

# The first match of several files is printed.
out=$(printf '%s\n' README.md tools/package.cs packaging/README.md | bash "$script"); code=$?
if [ "$code" -ne 0 ] || [ "$out" != tools/package.cs ]; then echo "FAIL: first match of a list (exit $code, printed '$out')"; exit 1; fi
echo "ok: first match of a list"
# Empty input matches nothing.
out=$(printf '' | bash "$script"); code=$?
if [ "$code" -ne 1 ] || [ -n "$out" ]; then echo "FAIL: empty input (exit $code)"; exit 1; fi
echo "ok: empty input"
