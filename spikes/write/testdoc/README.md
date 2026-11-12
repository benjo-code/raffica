# Test document generator

Writes the document every Write spike reads: a thesis of about 300 pages A4 (2.5 cm margins), Caladea 11 pt body at 1.5 line spacing, Carlito headings.

```
dotnet run --project spikes/write/testdoc -c Release -- generate --out artifacts/spikes/write/thesis.docx [--words 105000]
dotnet run --project spikes/write/testdoc -c Release -- verify artifacts/spikes/write/thesis.docx
dotnet run --project spikes/write/testdoc -c Release -- selftest      # an empty DOCX must fail verify (exit 1)
```

`verify` exits 0 when the document has the shape below, otherwise 1 with the first broken rule.

- Section 1: cover page and table of contents (field `TOC`), no footer, no page number.
- Section 2: 30 chapters, `w:pgNumType w:start="1"` and its own header and footer with a `PAGE` field, so physical page 3 shows "1".
- Section 3: appendix, `w:pgNumType w:fmt="decimal" w:start="1"`, own footer `A-` plus `PAGE` ("A-1").
- Headings `Heading1` to `Heading3` linked to one multi-level numbering (1, 1.1, 1.1.1); a numbered list (restarting per chapter) and a bulleted list per chapter.
- 20 tables (five with 45 rows or more, which cross a page break), 100 footnotes, five images of about 20 KB.
- Every font is Carlito or Caladea.

Deterministic: seed 4242, fixed package timestamps, explicit relationship ids, a normalised zip. Two runs give the same SHA-256 (checked 2026-11-12). The result is committed as `thesis.docx` (under 1 MB) so a machine without the generator can run the spikes; the generator is the source.

## Owner check on Windows (open)

Open `thesis.docx` in Word, update the table of contents, then note here: Word version, page count, whether physical page 3 shows "1" and the first appendix page shows "A-1".

- Word version: _not yet checked_
- Page count: _not yet checked_
- Page 3 shows "1" / appendix shows "A-1": _not yet checked_
