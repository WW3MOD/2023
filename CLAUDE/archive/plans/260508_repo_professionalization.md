# Repo Professionalization — Master List

Single source of truth for everything that needs to happen to make the public-facing GitHub presence look like a real project rather than a personal scratchpad. Items are not tied to a single session — pull from this list whenever you have time.

## Status legend

- `[ ]` open
- `[~]` in progress
- `[x]` done
- `[admin]` requires GitHub admin (Settings page, secrets, branch rules) — Claude can't do this
- `[file]` Claude can do via file edits
- `[ci]` CI/release infra — being deferred per "tackle later, properly"
- `[deferred]` intentionally pushed out

---

## A. Identity & branding (highest leverage, mostly admin)

- [ ] **A1. Rename `WW3MOD/2023` → `WW3MOD/WW3MOD`** (or `WW3MOD/game`). GitHub auto-redirects all old links/clones. `[admin]`
- [ ] **A2. Rewrite repo description.** Current text has a typo ("escallated") and apologizes for being WIP. Replace with a tight one-liner. `[admin]`
- [ ] **A3. Fix `homepageUrl`.** Currently points at `openra.net` — sends visitors to someone else's project. Replace with itch.io page / Discord / drop. `[admin]`
- [ ] **A4. Set repo topics.** Empty right now. Add: `openra`, `rts`, `mod`, `total-conversion`, `strategy-game`, `red-alert`. Free SEO. `[admin]`
- [ ] **A5. Upload social preview image.** 1280×640 gameplay screenshot. Without one, every link share shows a grey GitHub icon. `[admin]`
- [ ] **A6. Org avatar.** If `WW3MOD` org still has the default identicon, add even a simple text logo. `[admin]`

## B. Front door (the README)

- [ ] **B1. Real README.md** — replace the three-line launch table with: hero section, description, status, features, install/play (link to releases), screenshots, roadmap, contributing, credits, license. `[file]`
- [ ] **B2. Drop in actual screenshots** (3–6 in-game shots). Claude can leave the section scaffolded; team adds images. `[admin]` (asset upload)

## C. Visible signals of "this is real"

- [ ] **C1. Cut a real release.** A repo with zero releases looks abandoned. Even `v0.9.0-beta` makes the right sidebar say "Latest release · X days ago". Depends on packaging workflow actually working. `[ci]` `[admin]`
- [ ] **C2. Fix red CI badge** or hide it. Currently failing on every commit since April. `[ci]`
- [ ] **C3. Enable Issues** with templates ready before flipping the switch. `[admin]` (templates: `[file]`)
- [ ] **C4. Disable empty Wiki tab.** Empty tab labeled "Wiki" is worse than no tab. `[admin]`
- [ ] **C5. Pin this repo on the org page.** `[admin]`
- [ ] **C6. Pin a "Roadmap" or "How to playtest" issue** so it's the first thing visitors see in Issues. `[admin]` (after C3)

## D. Replace upstream OpenRA SDK boilerplate

- [ ] **D1. Rewrite CONTRIBUTING.md.** Currently verbatim OpenRA Mod SDK text — references "the Mod SDK", IRC, OpenRA wiki. Replace with WW3MOD-specific (~20 lines). `[file]`
- [ ] **D2. Rewrite `.github/PULL_REQUEST_TEMPLATE.md`.** Same upstream-boilerplate problem. Short, project-specific. `[file]`
- [ ] **D3. Add `.github/ISSUE_TEMPLATE/bug_report.yml`.** `[file]`
- [ ] **D4. Add `.github/ISSUE_TEMPLATE/feature_request.yml`.** `[file]`
- [ ] **D5. Add `.github/ISSUE_TEMPLATE/config.yml`** (disable blank issues, link to chat/discussions). `[file]`

## E. Polish

- [ ] **E1. SECURITY.md** — minimal, where to mail issues. GitHub surfaces a "Report a vulnerability" link when present. `[file]` (needs an email/contact)
- [ ] **E2. CREDITS.md** — top-level, public-facing attribution. OpenRA attribution at minimum (GPL-3 obligation). Inherited sprite/sound credits filled in by the team — `DOCS/SPRITE_REFERENCES.md` is a "potential references" doc, not actual usage, so it can't be auto-used. `[file]` (stub) + team adds specifics
- [ ] **E3. `.github/dependabot.yml`** — github-actions ecosystem only, monthly. Low noise. `[file]`
- [ ] **E4. CODEOWNERS** — auto-request review on PRs. Needs GitHub usernames for FreadyFish & CmdrBambi. `[file]` (blocked on usernames)
- [ ] **E5. CHANGELOG.md** — defer until first release; GitHub release notes can be the source of truth. `[deferred]`
- [ ] **E6. `.github/FUNDING.yml`** — adds a "Sponsor" button. Optional, per-team decision. `[admin]` (decision)

## F. CI / workflow modernization (deferred — separate session, do properly)

- [ ] **F1. Fix CI lint failure.** `TraitsInterfaces.cs:602 SA1514` — likely one missing blank line. Until fixed, every other "require status checks" rule is meaningless. `[ci]`
- [ ] **F2. Bump action versions.** `actions/checkout@v3 → v4`, `actions/setup-dotnet@v3 → v4`. Node 20 sunset is June 2026. `[ci]`
- [ ] **F3. Bump runner versions.** `windows-2019 → windows-latest`, `macos-11 → macos-13/14`. Both deprecated images. `[ci]`
- [ ] **F4. Decide .NET version.** CLAUDE.md says "targets net6, runs on .NET 8+". Pick one and stop hedging. `[ci]`
- [ ] **F5. Drop the `linux-mono` job.** Mono is dead-end for new work. `[ci]`
- [ ] **F6. Audit `packaging.yml`.** Triggers on any tag push but has never fired. macOS signing secrets (`MACOS_DEVELOPER_*`) referenced — confirm they exist and aren't expired, or strip the signing path. Dry-run on `v1.0.0-rc0` before staking the real release on it. `[ci]`
- [ ] **F7. Branch protection on `main`.** No force-push, require CI green, linear history. Depends on F1 first. `[admin]`

## G. Cleanup

- [ ] **G1. Delete `xavi` remote branch** after confirming nothing's left to cherry-pick. CLAUDE.md says it's stale. `[admin]`
- [ ] **G2. Squash `TODO` commit messages** going forward. One-word `TODO` commits look unprofessional in the public log. Don't rewrite history; just be careful from now on. (Process note, not a task.)

---

## Suggested ordering by leverage

The single biggest visual lift, in order: **A1 rename → B1 README → C1 release → C3 issues → C2 green CI**. Five items, each independently visible.

## Notes for future sessions

- Don't touch CI in pieces. F-items go together as one careful pass.
- B2 (screenshots) is the only file-level item that needs assets from the team. Everything else in `[file]` Claude can do solo.
- Repo rename (A1) auto-redirects clones — safe to do whenever; no need to coordinate.
