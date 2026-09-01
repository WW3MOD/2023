# Handoff — 2026-09-01 session, machine change

Written for whoever picks this up on the user's other machine. Everything below is on
`origin/main`; nothing of value is left on this machine. **Stamp: `main @ 22d3dccd`.**

The user's standing rule applies to you immediately: **`main` stays checked out, all
implementation happens in worktrees under `C:/Users/fredr/worktrees/ww3mod/<name>` and is
merged back.** If you pin a SHA for a comparison, use a throwaway worktree, never a detached
checkout of the user's tree.

---

## The two operating rules that govern every dispatch

These are manager-facing and **must be restated in every worker brief** — workers never see
`.maestro/MAESTRO.md` and will otherwise follow the recipes, which tell them to run things
themselves.

1. **Only the manager launches anything.** No worker runs `run-test.sh`, `run-batch.sh`,
   `run-tournament.sh`, `launch-game.sh`, or any screenshot script. Concurrent launches crash
   the user's machine. A worker writes down what it needs run *plus what would count as the
   answer*, and the manager runs it serially.
2. **Workers run neither `./utility.sh --check-yaml` nor `make test`.** They serialize badly
   across worktrees (measured: one worker waited ~35 min, another never got a turn). The
   manager runs the YAML gate once at merge. Workers DO run `make all` and `dotnet test`.
   Compensating requirement: each worker must name the YAML files it touched and what it would
   expect lint to say if it got it wrong, so the single gate run can be checked against that.

On Windows the YAML gate is `./make.ps1 test` — `./utility.sh --check-yaml` fails with
*"The OpenRA mod SDK requires make"*, and there is no `utility.ps1`.

**Baseline for the gate, so you can recognise a clean run:** `Errors: 29`, 29 distinct
signatures, 26 amnestied + 3 accepted on purpose. That is GREEN. Anything above 29 is yours.

---

## What shipped today (all merged, verified, pushed)

Build clean, NUnit green, YAML gate at baseline, and `make nav-guard` green at every merge.

| Item | Commit | What it actually was |
|---|---|---|
| 1. Queued evac waypoint line | `94ca9e0f` | `RotateToEdge` assigned `edgeCell` in `OnFirstRun`, which does not run until the activity becomes current; `edgeCell` is the only input to `TargetLineNodes`. Resolution moved into a pure static called from both constructors. |
| 2. Crew auto-evacuate on eject | `3ce18d71` | New `VehicleCrewInfo.AutoEvacuateOnEject` (default **true**) queues a one-shot `RotateToEdge` at spawn. Queued directly, not via an `"Evacuate"` order, because `VehicleCrew`'s tick is synced and `w.IssueOrder` would emit one order per client. |
| 3. Rear dismount + fan-out | `3ce18d71` | Ejection direction was `w.SharedRandom.Next(8)` with no reference to hull facing, so ~3 in 8 crew walked out the front. New pure `DismountGeometry` ranks exit cells rear-first and fans within ±90° of astern. Wired into all three dismount paths. |
| 4. Evac refund indicator | `94ca9e0f` | **Was suppressed for every evacuation that SUCCEEDED.** Fog/shroud answer "hidden" for out-of-bounds (`MapLayers.cs:504-505`, `:576-577`), and a completed evacuation always ends out of bounds. Position now clamped into `Map.Bounds`, visibility gate bypassed, rise lengthened 1.8s → ~4.5s. |
| 5. Fog darkness | `1250d51a` | Was a hardcoded per-layer vertex alpha in `ShroudRenderer.Alpha()`. Now a `FogDarkness` Info field (default `1f` = baseline) with `1.85` set in `mods/ww3mod/rules/world.yaml`. Fogged ground goes 25.6% → 6.4% of lit brightness = **74.8% darker**. |
| 6. Minimap relationship colours | `e8398bdc` | **The mode already shipped** (`UsePlayerStanceColors`, Ctrl+Comma, settings checkbox). What was missing was per-player shading — every enemy was one flat red. New `RelationshipShade` varies HSL lightness only. |
| 9. Technician auto-dispatch | `22d3dccd` | Right-click a capturable with nothing selected → nearest uncommitted technician. F spreads across selected structures via **exact** linear bottleneck assignment (binary search + Kuhn matching), not greedy. |
| 10. River Zeta crossings | `77ccc719` | **Both complaints were one bug.** The broken bridge deck was typed `River`; `Locomotor@FOOT` lists no `River` and no `Water`, and a missing entry is impassable, not expensive. The deck was a *hole* in the ford, and the "one cell" was `(36,1)` type `Rough` — dry land. Retyped to `Rock`, which also gives "infantry yes, no vehicle including amphibious" for free. |

### Three deliberate `@stable` / determinism changes — the benchmark baseline must be re-taken knowingly

1. `EvacuateEjectedCrew` was default-false and set true only on `@experimental`. The
   disposition moved from the bot module to the unit, so **`@stable` bots now self-evacuate
   crew** where they previously left them by the wreck. Allowed per `CLAUDE.md` (improvement
   flows to `@stable`, never gated off) but not silent. The flag's `[Desc]` claim that OFF
   means byte-identical was false and is corrected in place.
2. **Three `SharedRandom.Next` calls became deterministic fan indices**, so the shared RNG
   stream shifts. Replays and benchmark runs diverge from before `3ce18d71` regardless of
   anything else.
3. `CrossingMap` keys on `WaterTerrainTypes = { Water, River }` (`CrossingMap.cs:336`). The
   retyped deck stops counting as water and starts counting as land — a *correction*, since
   `IsCrossingSpan` looks for land flanked by water — and should make the `@experimental` AI
   detect both River Zeta fords as crossings for the first time. Unobserved.

---

## NOT DONE — pick up here

### A. Nothing in items 1–6 has been visually verified

Every one of them is merged and pushed on the strength of build + NUnit + code reading. **The
game was never launched for items 1, 4, 5 or 6.** Each worker handed up a capture recipe:

- **Item 5 (fog)** — highest value, easiest. `mods/ww3mod/rules/world.yaml` `FogDarkness: 1.85`
  → `1` is the "before", and **rules load at runtime so no rebuild is needed**. Launch
  `woodland-warfare-ww3`, don't move, screenshot the start. Skirmish begins fully explored
  (`ExploredMapCheckboxEnabled: true`, `mods/ww3mod/rules/player.yaml:183`), so fog and clear
  terrain are adjacent in frame one. Ask: is fogged terrain still legible as *shape*? Retune
  ladder, one YAML line, no rebuild: `1.5`→11.7%, `1.75`→7.7%, `1.25`→17.5%.
  **Known risk:** `^StandardVision` is a falloff, not a switch, so bands 2–9 are a player's own
  sight *periphery* and get dimmed too. If the user cannot read their own surroundings, that is
  this, and the fix is a lower number — not a different mechanism.
- **Item 6 (minimap)** — `river-zeta-ww3`, 6 spawns, set up **2v2v2**, press **Ctrl+Comma**,
  capture the minimap panel. Expect two blues (you + ally) and four reds. The question the image
  answers: do four reds read as four players at minimap scale, or do the middle two blur?
  Shade separation is 0.12 lightness up to four in a band; at 7–8 it compresses to 0.063–0.073,
  which is *arithmetic, not legibility*.
- **Items 1 and 4 (evac feedback)** — need interactive play. Item 1: select a unit, shift-click
  3–4 waypoints, shift-E. The evac leg (`Color 180,255,200,80`, distinct from white move legs)
  should draw from the last waypoint to the map edge **immediately**, while the unit is still on
  the first leg. Item 4: evacuate one unit and follow it; `+$N` should appear just inside the map
  boundary, drift slowly, readable ~4.5s. Also evacuate a nearly-dead unit — it should read
  `+$0`, not nothing.

### B. Three authored autotest scenarios have never been run green

All three exist on `main` and none has a passing run:

```
./tools/autotest/run-test.sh --hidden test-crew-rear-dismount
./tools/autotest/run-test.sh --hidden test-crew-auto-evacuate
./tools/autotest/run-test.sh --hidden test-capture-dispatch-bottleneck
```

The first two were run on 2026-09-01 and came back **RED**; a follow-up commit (`0b630f0c`)
fixed what the worker diagnosed as staging defects in the scenarios themselves — *"33,27 is the
hull's own spawn cell, a man who never moved"* — and changed **no engine code**. **That fix is
unverified.** If `test-crew-auto-evacuate` still reports the overridden crew member away from
his ordered cell, the one-shot semantics really are wrong and `AutoEvacuateOnEject` should go
default-false while it is fixed.

`test-capture-dispatch-bottleneck` passes only if `TechFar` is sent at the **near** derrick —
greedy produces the exact mirror image, which is what its failure message describes.

### C. Item 9's weakest link, which the user will hit first

`SubsetWithHighestSelectionPriority` (`SelectableExts.cs:96-103`) keeps only the top priority
group. OILB/MISS/HOSP set `Priority: 0`; FCOM/BIO default to 10. So box-selecting **three oil
derricks works**, but **an oil derrick + a reactor selects only the reactor**, and any box
containing one of your own units selects only your units. Part (c) therefore works within one
priority class and silently drops the rest. Fixing it means equalising those `Priority` values
or special-casing selection — both change selection behaviour globally, so it was not done
unasked. **This is the thing most likely to make the feature feel broken.**

Also: dispatch distance is **straight-line, not path length**. Exact only while capturers move
at the same speed over comparable ground; wrong when a wall or cliff sits between. If dispatches
look silly on obstructed maps, that is why.

### D. OPEN QUESTION to the user — unanswered

**Should the River Zeta river channel itself become crossable, and for whom?** Posted, not
answered. The worker `5a408806` did the analysis and its findings are the whole basis:

- The 588-cell channel is `w1.tem`/`w2.tem` generic deep-water fill, so it is **`Water`**. Only
  34 cells map-wide are `River`, and those are the ford templates.
- `foot` is blocked from **both** `Water` and `River`, so retyping the channel to `River` would
  not let a single soldier wade. Only **`RiverShallow`** (`foot: 40`) would.
- Letting river-capable tanks ford is a *different* edit again — adding `Water` to their
  locomotors. **"Cross anywhere" for infantry and for tanks are two separate changes.**
- Fording costs ~14× driving on grass, so even if permitted, the pathfinder will keep choosing
  the bridges unless speeds are raised — which is what would kill the chokepoint.

Options put to the user: infantry wade anywhere (RiverShallow channel) / stop here / shallow
bands at intervals / everything fords. **If the user answers on the new machine, that is the
context needed to act.**

### E. Items 7 and 8 — never started, gated behind the milestone

The user explicitly ordered these after a milestone note for items 1–6:

- **7.** Infantry visibility −2 cells, plus visibility-modifier adjustments, so infantry can
  actually be sneaked through instead of being spotted and pinned.
- **8.** Vehicle modifiers, weaker than infantry's — stationary roughly −1, firing more visible.

Relevant: the YAML gate output already shows every vehicle granting unconsumed
`visibility-1 … visibility-10` conditions, so a visibility-modifier scaffold appears to exist
and is worth grepping before designing anything. **Check the premise before dispatching** —
five queue items in one week described already-merged work, and it happened twice again today
(the minimap mode already shipped; the AA pump's premise was invalidated by `27d25f1c`).

### F. The milestone note itself is NOT written

The user asked for it after items 1–6. It was deliberately deferred because those items are
unverified — writing "milestone reached" over four unverified visual changes would be dishonest.
Write it once §A is confirmed.

### G. AA overkill bound — protocol written, six runs not taken

`WORKSPACE/audits/260901-aa-overkill-bound-protocol.md` (merged `c6f63dfa`). The user chose
"commission real bounds for both"; the design worker then found **only one of the two can be
honestly bounded**:

- `test-aa-overkill-pump`'s premise is **stale** — `27d25f1c` (2026-08-21) made a commitment an
  attacker-owned claim and `OverkillClaim.Claim()` releases before re-taking
  (`OverkillClaim.cs:31,52`), so the `V = V/2 + 120 => 240` fixed point cannot occur. The tally
  sits near 10 against a threshold of 100, and `MarkForDestruction` has **zero callers
  engine-wide**. The honest guard is the inverse, derived in-run against lane R.
- `test-aa-overkill-cadence` should **not** be bounded — its quantity is
  `test-aa-battery-volleys`' `test.spread` computed twice, with no control arm to cancel the
  16–32 tick rescan stagger. It keeps its skip declaration and gets a staging guard only.

The six runs (4 GREEN baseline needing **no code change**, then a RED pair under a one-line
`OverkillClaim.cs:52` edit) are listed with seeds in the protocol document. **Abort E matters:
if the RED control also reds `battery-volleys`, that scenario subsumes the pump and the right
answer flips to retiring the pump with no guard.**

### H. Pre-existing, not caused by this session

`./make.ps1 check` is **red on pristine `main`** — verified by building a throwaway detached
worktree at `ee059361` and getting the identical 16-file error set. 28 `RCS1226`/`SA1612`/
`CS0419` doc-comment errors surfaced only by `check`'s `-p:GenerateDocumentationFile=true`. The
Release build that `make all` runs has no analyzers, so nothing catches these day to day.
Worth a hotboard entry; not urgent.

---

## Autotest suite state (separate strand, largely closed today)

`WORKSPACE/audits/260901-autotest-suite-audit.md` (merged `0d2fdb25`) is the full picture. Short
version: 259 scenarios, ~3 ever genuinely retired, no CI runs the suite at all, storage is a
non-issue (231 of 259 `map.bin` byte-identical). The suite is **symbol-clean** — a verified
negative, with the checker self-tested by injection.

Thirteen scenarios that **could not fail** now declare `Test.Skip` + an `expected-status`
(merged `ee059361`). The two halves are coupled and must never be split: an *undeclared* SKIP
grades RED, so shipping the verdict edits without the declarations would have turned 13 false
greens into 13 reds. Grading table, confirmed by `expected-status.sh --selftest`:
declared-skip + SKIP → GREEN, + PASS → **STOPPED**, + FAIL → RED.
