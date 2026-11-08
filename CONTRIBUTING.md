# Contributing to Raffica

Thank you for helping. Raffica is a fast, open-source office suite; speed (startup, opening, scrolling) is the core promise.

## Build and test

Prerequisite: the .NET 10 SDK (`global.json` pins the version). The README lists what Native AOT publishing needs on
each system.

```
dotnet build Raffica.slnx -c Release
dotnet run tools/native-fetch.cs -- --rid <win-x64|linux-x64> --out artifacts/native/<rid>    # OCR library, once
RAFFICA_REQUIRE_OCR=1 dotnet test Raffica.slnx -c Release --no-build --max-parallel-test-modules 1
dotnet format Raffica.slnx --verify-no-changes
dotnet run tools/roadmap.cs -- check
```

Set the environment variable `RAFFICA_REQUIRE_OCR=1` as shown (in PowerShell: `$env:RAFFICA_REQUIRE_OCR = '1'`) so
that a missing OCR library fails the tests instead of skipping them. Please make sure these are green before you open a
pull request, and add a test for a fix or a feature.

## Branches and commits

- Branch from `dev` (`feature/<topic>` or `fix/<topic>`) and open the pull request against `dev`. `main` only holds
  releases.
- Commit messages follow Conventional Commits: `feat:`, `fix:`, `docs:`, `refactor:`, `test:`, `chore:`, `ci:`, optionally
  with a scope (`fix(pdf): ...`).
- Keep a pull request to one subject. Everything is in English: code, comments, docs, commits, issues.
- User-visible strings need an English and a German value (`Strings.en.resx` and `Strings.de.resx`).

## License

Raffica is licensed under GPL-3.0-or-later. By contributing you agree that your contribution is licensed under the same
terms.

## Security problems

Do not open an issue for a vulnerability; follow [SECURITY.md](SECURITY.md).
