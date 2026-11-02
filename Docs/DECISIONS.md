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
