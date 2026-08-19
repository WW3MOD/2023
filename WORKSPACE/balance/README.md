# WORKSPACE/balance — balance audits & change proposals

Home of faction-balance audit documents and the **proposal flow** that gates
every balance-motivated YAML change behind explicit user sign-off.

## Contents

- `260802-parity-audit.md` — static US/RU parity audit (PIPELINE item 32).
  Dated audit docs use `YYMMDD-short-name.md`.
- `260819-strike-shorad-parity.md` — re-audit of AWAITING-USER items 4 and 5
  (Iskander↔HIMARS, Tunguska↔SHORAD). Confirms and enlarges the item-4 gap,
  refutes item 5, and corrects proposal 002's evidence. Analysis only.
- `260819-penetration-default.md` — chases the `Penetration: 1` lead recorded in
  the strike/SHORAD audit. Corrects the count to 167/238, shows it collapses to
  **zero new defects**, and prices the bulk "fix" the lead invites at a silent
  +15-20% mod-wide buff. Analysis only; no stat changed.
- `NNN-short-name.md` — numbered change proposals (see flow below).

## Proposal flow

1. **Evidence first.** A proposal exists only when an audit (static analysis,
   tournament data, or combat-sim output) shows a concrete problem. Cite
   `file:line` for every claim; cite run artifacts (seed counts, winrates)
   when simulation data exists.
2. **Author a numbered doc** `NNN-short-name.md` (next free number, three
   digits, kebab-case name) with exactly these sections:
   - **Evidence** — what the data shows, with citations. Distinguish
     *verified* facts from inference.
   - **Proposed change** — the exact YAML delta (file, key, old → new value).
     Minimal and mechanical; someone else should be able to apply it verbatim.
   - **Expected effect** — what should change in-game and in which metric
     (e.g. cross-faction winrate, TTK in a stated matchup).
   - **Risk** — what could go wrong, knock-on effects, and how to detect them
     (which test/scenario would catch a regression).
3. **Status header.** Each proposal carries a status line at the top:
   `Status: PROPOSED | APPROVED | APPLIED | REJECTED`. Only the user moves a
   proposal past PROPOSED (sign-off tracked in `WORKSPACE/AWAITING-USER.md`).
4. **No YAML edits before APPROVED.** Proposals are documents only. When a
   proposal is APPROVED, the change is applied in its own commit referencing
   the proposal number, then the status flips to APPLIED with the commit SHA.
5. **One concern per proposal.** If a fix has independent parts the user might
   accept separately, split it.

## Current proposals

| # | Title | Status | Source |
|---|-------|--------|--------|
| 001 | Tunguska duplicate Health key | PROPOSED | 260802 audit §2 |
| 002 | HIMARS ↔ Iskander cost/effect parity | PROPOSED | 260802 audit §2 |
| 003 | Mi-28 dangling `secondary-air` armament | PROPOSED | 260802 audit §3 |
