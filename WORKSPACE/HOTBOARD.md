# Hotboard

> What's actively in motion **right now**. The v1 release tracker (`RELEASE_V1.md`) is the source of truth for scope and status.
> The ordered roadmap of what's next lives in [`PIPELINE.md`](PIPELINE.md) — the user steers by reordering it.
> Cap ~40 lines. Rotate stale entries out — once shipped or `[T]`, the tracker / commit history tells the story.

**Reconciled 2026-08-11.** Main @ `54ea7210`, **in sync with `origin/main`**, working tree clean. 178 commits landed 08-05 → 08-11. Push policy stands and is settled: **no agent ever pushes; the user pushes manually.** The 08-06 machine hand-off note is retired — that divergence is long closed.

## Working on
- **The danger UNIT, not the thirteen thresholds.** The `[danger] reference` read the 08-09 session asked for came back on 08-10 (`084367b0`) and it is **negative**: `evacLevel` 1,706 against live median cells of 27,919 (USA) / 94,010 (Russia), min/max straddling the median by 2.57 orders, only 92 of 424 types contributing. Every one of the thirteen thresholds sits below the median cell of the field it gates — a scale error in the unit, not thirteen tuning errors. **Binding constraint: settle the durability-weight question (`[med]` in `bugs/discovered.md`) BEFORE re-deriving any threshold.** Plan for user review: `plan-260810-post-measurement.html`.
- **Supply trucks: much built, still not demonstrated fixed.** Six merges plus a direct-to-main run have attacked the loop from every angle (`e79ddd97`, `574a3c08`, `8d0ff18b`, `0eef99d6`, `5642d931`, + `d1f84a30`…`377085db`), and `test-supply-far-front-reached` passes for the first time. **It has been declared fixed to the user three times and there is still no fourth declaration to make** — the evidence gate says the evac guard still trips unconditionally once contact exists.
- **Item 22 — case-01 forest ambush: CALIBRATING, awaiting user bar.** The provisional cost-ratio bar is ill-posed (÷0); reframe awaits user ratify (`AWAITING-USER.md`).

## Recently shipped / landed
- **User gameplay batch, items 1–6 (2026-08-10)** — vehicle occupants `18838dd7` (critical-damage disable, paced dismount, emergency bail, softer passenger damage); FX pass across all 161 weapons `62d8148e`; dry infantry break off to rearm `175a4784`. **Three `@stable` behavioural deltas declared across the batch** — next benchmark baseline must be re-taken knowingly.
- **User-reported bug run (2026-08-10/11)** — elimination cascade `f49b6aca` (**changes multiplayer outcome resolution**: destroying one bot's SR defeated every survivor slotted after it); medic autonomy `cfcc947b`; dry unit drops its attack order `bd7b6bb2`. All three share one root: a unit with a running activity is never idle, so every "on idle" hook was unreachable for it.
- **Live-play batch 2026-08-08, items 36/37/38 SHIPPED** — crew retire + ground units drive off the map `9ab1b2e2`; tank-trap diagonal squeeze closed `dd3430a8` (**also changes `@stable`** — PathFinder stopped skipping the height-discontinuity rule). Items 34 (transport pickup) and 35 (derrick-rush transports) **remain open** — recon only, no implementing commit.
- **Bot attention model Stage 1 (2026-08-08)** — order gate `0eef99d6`: incumbency + dwell at `ModularBot.QueueOrder`. The wiggle was commitment with the wrong lifetime, not missing commitment — which is why seven prior reimplementations had not fixed it.
- **AI plumbing (2026-08-08)** — posture veto `bd3abacf` (the commitment layer existed and was structurally unreachable); unit purpose `09877fd5` (opening technicians garrisoned rear houses with no enemy on the map, unrecoverable for the match).
- **nav-guard (2026-08-09)** `2754f341` — static per-locomotor connectivity gate, now a prerequisite of `make test`. Exists because a blocking-rule change sealed a 335-cell field on river-zeta and only a reviewer's hunch caught it.
- **Release readiness (2026-08-11)** — text reads as WW3MOD `4836ceed`; Ogg enabled + dead `arabs/` voices dropped `2f31404e`; `tools/cameo` `2c110a67`; **`ASSET-LICENSING.md` `17e3ce4c`** — 1,246 redistributed files inventoried with confidence levels and a removal plan. **Ship-as-is is settled; that document is a planning artifact, not a task list.** Credits corrected `54ea7210`. Captured as PIPELINE item 39.
- **08-09 merges** — danger scale `5642d931`, aircraft rearm `af36e686`, plus the six-document `DOCS/bots/` set.

## Pending user sign-off
See [`AWAITING-USER.md`](AWAITING-USER.md) — the full user-gate queue. Nothing there could be proven resolved at the 08-11 reconciliation; **five new items were added** (post-measurement decisions, the widened benchmark debt, aircraft rearm host, asset-licensing acknowledgement, the 8 failing autotests). Standing highlights: balance proposals 001–003 (verified still unapplied in YAML), parity batch runs, case-01 bar, item-24 gate disposition (KEEP OFF recommended, **verified still committed ON in both profiles at HEAD**), item-31 aggro sweep, fires/brain enablement sweep.

## Quick Stats
- Engine files modified: 280+
- Maps: 13
- AI bot types: 3 (Normal, Rush, Turtle)
- Regression tally 2026-08-10: 60 pass / 8 fail (none traceable to the two engine merges)
