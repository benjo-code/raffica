# Unicode data files

The data files behind the line breaker of Raffica Write (`src/Raffica.Write.Layout/Text/LineBreaker.cs`). They are the input of
`tools/gen-linebreak.cs`, which writes `src/Raffica.Write.Layout/Text/LineBreakData.g.cs`; CI runs
`dotnet run tools/gen-linebreak.cs -- --check` and fails when the committed table is stale. The files are not shipped; only the
generated table is, so the licence text travels with it (`LICENSE-Unicode.txt`, Unicode License v3, also in the third-party notices).

Version: 18.0.0
Retrieved: 2026-11-14

| File | Source |
|---|---|
| `LineBreak.txt` | https://www.unicode.org/Public/18.0.0/ucd/LineBreak.txt |
| `EastAsianWidth.txt` | https://www.unicode.org/Public/18.0.0/ucd/EastAsianWidth.txt |
| `emoji-data.txt` | https://www.unicode.org/Public/18.0.0/ucd/emoji/emoji-data.txt |
| `DerivedGeneralCategory.txt` | https://www.unicode.org/Public/18.0.0/ucd/extracted/DerivedGeneralCategory.txt |
| `LineBreakTest.txt` | https://www.unicode.org/Public/18.0.0/ucd/auxiliary/LineBreakTest.txt (conformance test, not used by the generator) |
| `LICENSE-Unicode.txt` | https://www.unicode.org/license.txt |

The rules implemented are those of UAX #14 revision 57 (Unicode 18.0.0), https://www.unicode.org/reports/tr14/tr14-57.html.

To move to a newer Unicode version: fetch the six files from the new `Public/<version>/ucd` folder, change `Version` and `Retrieved`
above, read the new revision of UAX #14 and adapt `LineBreaker` where rules changed, run `dotnet run tools/gen-linebreak.cs`, and
run the conformance test (`LineBreakConformanceTests`); every line of the new `LineBreakTest.txt` must pass.
