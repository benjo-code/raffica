# Raffica

Raffica ("gust" in Italian) is a fast, open-source office suite for Windows, Linux and macOS: Raffica Office with Write, Sheets, Slides and PDF. It grows out of the PDF editor [Pagewing](https://github.com/benjo-code/Pagewing); speed is the core promise.

**Status:** in development. So far the repository holds the PDF core imported from Pagewing (no user interface yet) – see [Docs/ROADMAP.md](Docs/ROADMAP.md).

## Setup

Prerequisite: the .NET 10 SDK (`global.json` pins the version). On Linux, Native AOT publishing also needs `clang` and `zlib1g-dev`; on Windows, the Visual Studio C++ build tools.

```
dotnet build Raffica.slnx -c Release
dotnet run tools/native-fetch.cs -- --rid win-x64 --out artifacts/native/win-x64     # linux-x64 on Linux; fetches the OCR library
dotnet test Raffica.slnx -c Release --no-build --max-parallel-test-modules 1         # set RAFFICA_REQUIRE_OCR=1 to fail instead of skip without the OCR library
dotnet format Raffica.slnx --verify-no-changes
dotnet publish src/Raffica.Cli -c Release -r win-x64 -o out/cli                       # the command line tool, Native AOT
```

## License

GPL-3.0-or-later, like Pagewing – see [LICENSE](LICENSE).
