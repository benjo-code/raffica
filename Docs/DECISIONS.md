# Raffica – Decisions

Log of architecture and technology decisions. Newest entries on top.

**When an entry is due:** choice of framework, library or version, architecture and interface decisions, deliberately rejected approaches, workarounds with consequences – anything you would otherwise discuss again in half a year.

**Rules:** Do not rewrite entries afterwards. If a decision is revised, add a new entry and note "Superseded by: <title>" in the old one.

<!-- Template:

## YYYY-MM-DD – <Title>
**Context:** <Which problem, which constraints?>
**Decision:** <What was decided?>
**Rationale:** <Why?>
**Rejected alternatives:** <What else was considered and why not>
-->

## 2026-11-03 – Dependency and license guardrails
**Context:** Raffica is GPL-3.0-or-later. Research on document libraries showed several popular .NET libraries with licenses that do not fit.
**Decision:** Not allowed: EPPlus 5 and later (Polyform Noncommercial), DocX/Xceed (non-commercial), NPOI binaries from 2.8.0 on (EULA with a maintenance fee; building from source needs a separate legal check), Avalonia Pro controls such as the RichTextEditor, and proprietary document libraries (Aspose, Syncfusion, Spire, GemBox, FlexCel). MuPDF (AGPL) is avoided. Preferred licenses: MIT, Apache-2.0, BSD. Every new dependency gets a license check before it is added.
**Rationale:** Keeps the suite redistributable under the GPL without per-user fees or revenue limits.
**Rejected alternatives:** Commercial libraries with free tiers (revenue caps and page limits conflict with an open-source suite).

## 2026-11-03 – Core-only import, fresh UI, new order of work
**Context:** The decision of 2026-11-02 planned Raffica PDF as a pure move of Pagewing. The owner wants a completely fresh UI. A read-only look at the Pagewing `v1.0.0` tag shows that `Pagewing.Core`, `Pagewing.Pdfium`, `Pagewing.Ocr` and `Pagewing.Cli` do not depend on Avalonia.
**Decision:** M0 imports only the core, the CLI, their tests and test data, the bundled PDFium and OCR binaries and the build config. `Pagewing.App`, the Avalonia-related third-party bundles and the installer packaging are not imported. Order: Foundation (M0) → design system and shell → Raffica PDF on the new UI → macOS → Write → Sheets and Slides. The design system starts with at least three design directions made with Impeccable and shown as Claude artifacts for all four apps; the owner chooses one.
**Rationale:** Reuses the proven PDF engine and tests without carrying over a UI that would be replaced. The design is decided on all apps together, so PDF does not set the look alone.
**Rejected alternatives:** Importing the whole app and restyling it (keeps a UI the owner does not want); designing the PDF app first and extending the look to the other apps later.
Supersedes the order of work in "Suite structure and order of work".

## 2026-11-03 – Stack: .NET and Avalonia for the shell, Write's layout engine decided later
**Context:** Research compared .NET 10 with Native AOT and Avalonia 12 against Rust (Slint, GPUI, egui, iced, Xilem, Parley, IronCalc) and others. Mostly vendor claims and project READMEs, few independent benchmarks.
**Decision:** The suite shell uses .NET 10 (LTS), Native AOT and Avalonia 12. The document model and layout of Write live in a library with no UI dependency; whether that engine is C# or a Rust core behind a C interface is decided in the Write spec, after spikes (Open XML SDK under Native AOT, text layout speed, printing, IME). For Sheets, IronCalc is compared with an own formula engine in the Sheets spec.
**Rationale:** The core already runs on .NET. No Rust GUI toolkit is ready for a ribbon, grids and accessibility. A Word-grade layout engine is the largest cost in any language, so the choice for it should rest on measurements.
**Rejected alternatives:** Rust for the whole suite (immature GUI toolkits, rich-text accessibility missing in AccessKit); C++ and Qt (heavy for a solo developer, no reuse of the core); Tauri or web UI (undermines the speed promise).

## 2026-11-02 – Migration from Pagewing: new repo, no history, only what is needed
**Context:** Pagewing (PDF editor) becomes the first app of an office suite. Nobody except the owner uses Pagewing, and its public history contains commits with a private email address.
**Decision:** A new repo `benjo-code/raffica` (created from `project-template`) without Pagewing's git history. Import only what is strictly needed (core/engine code, tests, build config, CI, LICENSE, a rewritten README) from the `v1.0.0` tag; commit 1 = unchanged import, commit 2 = scripted rename. Old plans and docs stay in the old repo, and `Docs/DECISIONS.md` here starts fresh with the decisions that still apply. No bridge release and no user migration. Pagewing is finished first (v1.0.0), then migrated.
**Rationale:** A clean start with a small surface; no translation of Pagewing's large German decision log; no need to support existing installations.
**Rejected alternatives:** Renaming Pagewing in place (keeps the history and the old identity); carrying over all docs (they are German and mostly obsolete).

## 2026-11-02 – English everywhere
**Context:** `project-template` and Pagewing use German for docs and commits.
**Decision:** Docs, README, commits, issues, PRs, code and comments are English. The UI source language is English, and German localization stays on the plan (to be confirmed in the M0 spec). The template block in `CLAUDE.md`, the roadmap tool output and the repo templates are translated to English in this repo.
**Rationale:** An international open-source project with a single language for contributors.
**Rejected alternatives:** Keeping the template German; mixing languages.

## 2026-11-02 – Suite structure and order of work
**Context:** Raffica Office is meant to contain Write, Sheets, Slides and PDF.
**Decision:** Brand Raffica, suite Raffica Office, apps Raffica Write, Sheets, Slides and PDF. One repo (monorepo) with one installer for the suite and individually started apps; platforms Windows, Linux and macOS. Order: first Raffica PDF as a pure move of Pagewing, then the shared design system and shell project, then Write (its text layout engine also serves Slides), later Sheets and Slides. macOS is its own milestone after the design system. Details are worked out in the M0 spec.
**Rationale:** Do not start all apps in parallel; extract shared infrastructure before a second app exists.
**Rejected alternatives:** Developing all apps in parallel.
Order of work superseded by: Core-only import, fresh UI, new order of work (2026-11-03).

## 2026-11-02 – Design system and Impeccable
**Context:** The UI is designed from scratch and should look consistent across the apps.
**Decision:** One shared design system (tokens, typography, spacing, components), one accent color per app, designed with the Impeccable skill. Impeccable targets web frontends, so it provides direction and critique and the tokens are implemented by hand (Avalonia/AXAML).
**Rationale:** Consistent look, different colors per app.
**Rejected alternatives:** Designing each app on its own.

## 2026-11-02 – macOS without paid Apple account
**Context:** No Apple Developer account is paid for.
**Decision:** Ad-hoc signing only, no Developer ID and no notarization; distribution as `.dmg`. First launch needs "Open Anyway" in System Settings → Privacy & Security, which the README documents.
**Rationale:** Ad-hoc signing is free and required to run native binaries on Apple Silicon.
**Rejected alternatives:** Unsigned builds (do not start on Apple Silicon).
