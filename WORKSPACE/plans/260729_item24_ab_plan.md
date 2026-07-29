# RUN PLAN — item-24 gate-enablement A/B (2026-07-29)

**Status: PLAN ONLY, NOT RUN.** Executable procedure for the A/B that decides whether
item-24's belief-side capture/garrison repoint (merge `646515bd`) is enabled by default
on the `@experimental` bot. No matches were run for this card. The ai.yaml toggles below
are performed at EXECUTION time, never now. Authored against `main @ c11ce511`.

This card is the natural downstream of the item-25 Stage-F re-baseline
(`WORKSPACE/ai-bench/runs/260728_rebaseline_runplan.md`, committed `7fa0b046`) and
DELIBERATELY reuses its instrument, calibration, scenario set, seeds, and metrics so the
two are directly comparable. Where this card says "the ladder," it means that run plan.

---

## What this A/B decides

item-24 repointed two consumers from the omniscient `InfluenceMap` threat read to the
**believed** `DangerFieldLayer.GroundDanger` field, both behind default-OFF gates,
`@experimental`-only, with byte-identity proven off-gate at review:

- **Capture ordering** — `CaptureCoordinatorBotModule`, gate `StrategicCaptureRepointEnabled`.
  When on, capture targets are ordered off the believed anti-ground danger field so a
  capturer is not sent first into a believed weapon envelope (DAMP: safe ×100 / mild ×60 /
  hostile ×20 on the `GroundDanger` scale).
- **Garrison ordering + sizing** — `PoiGarrisonBotModule`, gate `DefendRepointEnabled`.
  When on, held-POI defend urgency AND garrison size are scored off the believed field
  (RAISE, the mirror: calm ×100 / probed ×150 / assaulted ×250; the size bump fires at
  `BelievedDangerHostileThreshold: 120`).

Enablement was deferred until the item-25 ladder re-baseline lands fresh Stable/Stage-F
numbers on the post-26/28 instrument (PIPELINE items 24, 25). This card is the honest
benchmark check owed before any `@experimental` default-on claim. It prices a default; it
does NOT touch the promotion policy (`@experimental`→`@stable`, SPEC §13).

---

## Gate identification — VERIFIED at `main @ c11ce511`

Do NOT trust the line numbers at execution time — the ladder or any intervening merge may
shift them. **Grep the property name (authoritative); use line numbers only as a hint.**

| Gate | Property | ai.yaml block | Verified line | C# default |
|---|---|---|---|---|
| Capture | `StrategicCaptureRepointEnabled` | `CaptureCoordinatorBotModule@experimental.tecn:` (block `:131`, `RequiresCondition: enable-ai-experimental` `:132`) | **`:180`** | `false` (`CaptureCoordinatorBotModule.cs:127`) |
| Garrison | `DefendRepointEnabled` | `PoiGarrisonBotModule@experimental:` (block `:284`, `RequiresCondition: enable-ai-experimental` `:285`) | **`:309`** | `false` (`PoiGarrisonBotModule.cs:106`) |

**`@experimental`-only, verified:** the two `@stable` twins — `CaptureCoordinatorBotModule@stable.tecn:`
(`:830`) and `PoiGarrisonBotModule@stable:` (`:878`) — carry NEITHER property, so they
inherit the C# default `false`. Flipping the gates touches `@experimental` only ⇒ `@stable`
is **byte-identical across both arms** ⇒ both arms share the ONE Stable-vs-Stable
calibration from the ladder's gate (a). This is the same yardstick discipline the ladder's
ambush A/B relies on.

> **State discrepancy to resolve BEFORE running (contamination item C-2):** at `main @
> c11ce511` both gates are committed **`true`** (`:180`, `:309`). A working-tree
> UNCOMMITTED flip to `false` exists in this checkout (ladder-worker-owned; do not touch).
> The stated intent is that main carries the gates OFF pending this A/B. The arms below are
> defined by EXPLICIT gate values, not by whatever main happens to inherit — so this card
> is robust to either resolution. The post-ladder committed gate state MUST be recorded at
> Step 0 (it also determines the reuse optimization below).

---

## Invariants any arm must respect (`DOCS/reference/influence-stack.md`)

- **Zero `SharedRandom`/`LocalRandom` draws in the entire stack** (§Invariants `:94`). The
  repoint adds no draws; the belief fields self-stagger with deterministic offsets. A/B
  seeds therefore pair cleanly — same map/faction/spawn/opponent → identical RNG stream
  save for the gated reshape.
- **Byte-identity when flags off** (§Invariants `:95`). Arm A (gates OFF) is byte-identical
  to the pre-item-24 omniscient-capture/garrison behavior (`suppress ? null : …` /
  `suppress ? 0 : SampleThreat(…)` collapse verbatim; ActorID total-order sorts). So Arm A
  IS the frozen pre-item-24 baseline — the A/B is a clean isolation of the belief-repoint
  delta, nothing else.
- **The `suppressOmniscientThreat` seam skips `SampleThreat` entirely** (§Known gaps `:101`,
  `PoiMap.cs:427/:495`) — its `FindActorsInCircle` fallback is itself omniscient, so nulling
  the layer is not enough. Gates ON (Arm B) is the ONLY path that reads believed danger for
  these two consumers; gates OFF (Arm A) is fully omniscient. No partial state exists.

---

## Preconditions (ALL must hold before ANY match runs)

- **(P1) The item-25 ladder is COMPLETE and its arm-B edit reverted.** The ladder produces
  the fresh Stage-F Exp-vs-Stable baseline AND the fresh Stable-vs-Stable calibration
  (yardstick) this card consumes. Its Step 4 (`git checkout -- mods/ww3mod/rules/ai/ai.yaml`)
  must have restored a clean tree — confirm no residual ambush `RequiresCondition` drift.
- **(P2) Post-ladder `main` SHA recorded, instrument confirmed.** Items 26 (`fc9fe396`) and
  28 (`1f036ecb`) both broke benchmark byte-identity globally; the fresh baseline is zeroed
  on that post-26/28 instrument. If any NEW instrument-breaking merge landed AFTER the
  ladder ran, the reuse optimization is void and BOTH arms must be run fresh (see below).
- **(P3) Fresh calibration medians exist + aggregated** (ladder gate a) — consumed as the
  noise band. Confirm the `*cal-nn*` result dirs are present and aggregated.
- **(P4) Autoburn window active + grant covers this batch.** This is a multi-match batch →
  the CLAUDE.md no-autonomous-multi-test hard rule applies. The PIPELINE item-25 STANDING
  GRANT enumerates the item-25 ladder + gate (b); it does NOT explicitly name this item-24
  A/B. Treat coverage as UNCONFIRMED: fire only under an active autoburn window whose grant
  is confirmed (by the user) to extend to this card, or a fresh explicit goahead. Do not
  assume the enumerated grant covers it.

---

## Arms

Both arms are `@experimental` vs `@stable`, paired seeds, `@stable` byte-identical between
them. The delta between arms is EXACTLY the item-24 belief-repoint.

- **Arm A — gates OFF** (`StrategicCaptureRepointEnabled: false` **AND** `DefendRepointEnabled:
  false`). The fresh Stage-F baseline behavior: `@experimental` with offense-repoint + ambush
  at their post-ladder settings, but capture/garrison still reading the omniscient InfluenceMap.
  ≡ pre-item-24 behavior (byte-identity, §Invariants).
- **Arm B — gates ON** (`StrategicCaptureRepointEnabled: true` **AND** `DefendRepointEnabled:
  true`). Identical to Arm A except capture/garrison re-derive threat from believed danger.

Both gates are flipped TOGETHER (item-24 shipped them as a pair). A mixed result routes to
the split-gate follow-on in the decision rule — do NOT silently enable one without the other.

### Toggle procedure (EXECUTION time only — NOT now)

```
# Locate the gates by NAME (line numbers are a hint, verified 180/309 at c11ce511):
grep -nE "StrategicCaptureRepointEnabled|DefendRepointEnabled" mods/ww3mod/rules/ai/ai.yaml

# Arm A — set BOTH false:
#   StrategicCaptureRepointEnabled: false     (CaptureCoordinatorBotModule@experimental.tecn)
#   DefendRepointEnabled: false               (PoiGarrisonBotModule@experimental)
# Arm B — set BOTH true.
# Mod rules are read at game load → a relaunch suffices; `make all` is NOT needed
# (no C# change; this is a YAML-only flag flip, same mechanism as the ladder's arm B).
# The edit is uncommitted, local, and REVERTED after the run:
git checkout -- mods/ww3mod/rules/ai/ai.yaml
git status -sb        # confirm clean — only this run plan / result card committed
```

### Reuse optimization — run ONE new arm, not two (when eligible)

The ladder's Arm A is "`main` as-shipped." IF post-ladder `main` commits the item-24 gates
in a state matching one of this card's arms, that arm's numbers are ALREADY produced by the
ladder and are reused verbatim (same instrument SHA, same calibration, same seeds) — only
the OTHER arm is new work (20 matches). Eligibility is checked at Step 0:

- Post-ladder `main` gates **OFF** ⇒ ladder Arm A == this card's **Arm A**; run only **Arm B**.
- Post-ladder `main` gates **ON** ⇒ ladder Arm A == this card's **Arm B**; run only **Arm A**.
- Instrument SHA differs (P2 fails), OR ladder did not preserve per-match logs for the reused
  arm ⇒ reuse VOID; run **both arms fresh** (40 matches).

Reuse is valid only when the reused arm's instrument SHA is byte-identical to the new arm's.
Record the decision explicitly in the result card.

---

## Scenario set + seeds — REUSED from the ladder for comparability

Identical rungs, seeds, and flags to the ladder (`260728_rebaseline_runplan.md` §3), stated
explicitly so the numbers sit on the same axis:

- `tournament-s1-eco-river-zeta` + `--mirror tournament-s1-eco-river-zeta-mirror`, N=10
  (`tournament-eco-5min.yaml`, `TimeLimitSeconds 300` / `SpeedMultiplier 8`).
- `tournament-s2-combat-river-zeta` + `--mirror tournament-s2-combat-river-zeta-mirror`, N=10
  (`tournament-combat-12min.yaml`, `TimeLimitSeconds 720` / `SpeedMultiplier 8`).
- Hidden (default `--background`); `--mirror` alternates even=primary / odd=mirror inside the
  N=10, so N=10 already spans both faction assignments. Paired seeds across arms (same
  map/faction/spawn/opponent) give the clean A/B.
- `--max-wall-secs` 150 (S1) / 400 (S2) — generous per the ladder's S2 card R-2 note (the
  paired model breaks if a natural-length match is culled early).

### Commands (per NEW arm, run from repo root)

```
# S1 eco
./tools/autotest/run-tournament.sh tournament-s1-eco-river-zeta \
    --seeds 10 --mirror tournament-s1-eco-river-zeta-mirror \
    --max-wall-secs 150
# S2 combat
./tools/autotest/run-tournament.sh tournament-s2-combat-river-zeta \
    --seeds 10 --mirror tournament-s2-combat-river-zeta-mirror \
    --max-wall-secs 400
```

`aggregate-tournament.sh` runs automatically on ≥1 verdict → `summary.{json,csv}` (win
split, per-side winrate, capture counts, cost-weighted exchange).

---

## Metrics + decision rule

Metrics are the ladder's, so the comparison is like-for-like. **Both arms are compared
against the same fresh Stable-vs-Stable calibration** (P3) as the noise band; the item-24
verdict is the **Arm B − Arm A** delta, NOT either arm's absolute vs `@stable`.

**Signals, by which gate they exercise:**

- **S2 combat (primary for the garrison gate)** = net cost-weighted combat swing
  `median(kills_cost − deaths_cost)` (Exp − opponent), sign-count X/10, both-spawn, validity
  `engaged ≥ 6/10`. Garrison repoint changes defend ordering + garrison SIZE → the defensive
  exchange is where a believed-danger RAISE should show (holding contested POIs at correct
  strength, not over/under-committing off omniscient truth).
- **S1 eco (primary for the capture gate)** = win split (win rate) + Exp capture X/10 +
  `resources_earned` median. Capture repoint changes capture ordering → capture reliability
  (X/10) is the direct signal; not lunging capturers into believed danger should also lift
  the cost-weighted exchange (fewer capturer losses) without starving the eco race.

**Decision rule (tie goes to OFF — do not ship neutral cost):**

The repoint carries a real per-tick cost (item-24 review OBS: the capture path runs
suppress+rescale twice per tick under repoint; garrison consumes threat twice). So a pure
wash does not clear the bar for turning it on.

- **ENABLE** (commit both gates `true` on `@experimental`) **iff Arm B is net non-harmful on
  BOTH rungs, ideally with a measured edge:**
  - S2: `swing_B ≥ swing_A − noise` **AND** `engaged_B ≥ 6/10` (still a valid fight), ideally
    `swing_B > swing_A` (a measured belief-repoint edge in cost-weighted $).
  - S1: `capture_B ≥ capture_A − 1/10` (capture reliability not regressed) **AND**
    win-rate_B `≥ 0.40` **AND** `resources_earned` median not materially below Arm A.
- **KEEP OFF** (revert to gates `false` on `@experimental`; leave item-24 dead-but-shipped)
  **iff Arm B regresses either rung:** S2 swing measurably worse than Arm A, OR S2 `engaged`
  drops below the `6/10` validity floor (repoint suppressing engagement), OR S1 capture falls
  `> 1/10` below Arm A, OR S1 win-rate below floor.
- **NEEDS ITERATION** (neither clean enable nor clean keep-off) **iff the two rungs disagree
  in sign beyond noise** — e.g. capture repoint lifts S1 while garrison repoint drags S2, or
  vice-versa. Because the gates are INDEPENDENT booleans, a mixed both-on result cannot be
  attributed. Route to a **split-gate follow-on A/B**: capture-only ON (`StrategicCaptureRepointEnabled:
  true`, `DefendRepointEnabled: false`) vs garrison-only ON, each vs the same Arm A baseline,
  same rungs/seeds — attribute the delta to a gate before enabling it alone.
- **Within-noise / neutral on both → KEEP OFF.** (Fog-legality — removing the omniscient
  reads — is an independent design goal that the user MAY weight to break a tie toward ON.
  That is a user call: record it, do not infer it. On pure benchmark grounds the tie is OFF.)

---

## Runtime estimate + run count

Per the ladder's measured per-arm wall-clock (hidden, 8×, YAML-only, no rebuild):

| Path | New arms | Matches | Est. wall-clock |
|---|---|---|---|
| Reuse eligible (P2 holds, ladder Arm A matches one arm) | 1 | 20 (S1 N=10 + S2 N=10, mirrors folded) | ~31 min + ~5 min aggregate/greps ≈ **~36 min** |
| Reuse void (both arms fresh) | 2 | 40 | ~62 min + ~5 min ≈ **~65–70 min** |

Calibration is NOT in this budget — it is the ladder's gate (a), consumed not re-run. Run
count is therefore **20 matches (reuse)** or **40 matches (both-fresh)**, gated by the
standing autoburn grant (P4). Prefer the reuse path when eligible to conserve the grant's
window budget.

---

## Contamination checklist — verify at Step 0 BEFORE the first match

- **C-1** — `git status -sb` clean except this run plan / result card. No stray mod-YAML or
  ai.yaml diff at start.
- **C-2** — **The item-24 gate state is deconflicted.** At authoring (`main @ c11ce511`) the
  gates are committed `true` with a working-tree uncommitted flip to `false` (ladder-owned).
  Confirm the post-ladder committed state and RECORD it; the arms set explicit values
  regardless, but this determines the reuse optimization and must not be a surprise mid-run.
- **C-3** — Post-ladder `main` SHA recorded in the result card; confirm items 26 + 28 merged
  and NO newer instrument-breaking merge landed after the ladder (else reuse void, P2).
- **C-4** — Ladder (P1) fully complete and its arm-B ai.yaml edit reverted (clean tree, no
  ambush `RequiresCondition` drift).
- **C-5** — Fresh Stable-vs-Stable calibration (P3) present + aggregated; medians recorded as
  the noise band both arms are read against.
- **C-6** — Re-verify gates are `@experimental`-only (grep: property appears ONLY under the
  two `@experimental` blocks; `@stable`/`@stable.tecn` twins carry none) so `@stable` is
  byte-identical across arms and the single shared calibration is valid.
- **C-7** — No other worker running games in this shared checkout (window focus / shared
  `debug.log`). Do not launch concurrently.

---

## Where numbers get recorded

1. **Raw** (git-ignored, harness-owned): `tools/autotest/tournament-results/<ts>_<scenario>/`
   — per-match `match_*.json`, `match_*_debug.log`, `summary.{json,csv}`, `batch.meta.json`.
2. **Result card** (committed): `WORKSPACE/plans/260729_item24_ab_result.md` — post-ladder
   `main` SHA; reuse decision (which arm was reused vs run fresh); consumed calibration
   medians; Arm A + Arm B per-rung medians (S1 win split + capture X/10 + earned; S2 swing +
   sign X/10 + both-spawn + engaged X/10); the **B − A** delta per rung; the verdict
   (ENABLE / KEEP OFF / NEEDS ITERATION) against the rule above; firing-proof grep counts.
3. **`WORKSPACE/PIPELINE.md`** item 24 — flip *"Gate enablement awaits the item-25
   re-baseline"* → the recorded verdict + result-card SHA.
4. **`DOCS/reference/influence-stack.md` §Known gaps** — annotate the capture/garrison item
   with the enablement verdict (curation pass, not a direct knowledge-bank addition).
5. On **ENABLE** or **KEEP OFF**, the ai.yaml gate values are committed to their decided
   state (path-limited, no attribution trailer, never pushed); on NEEDS ITERATION the gates
   are left OFF pending the split-gate follow-on.

## Firing proofs (per arm, from preserved per-match debug logs)

```
# capture repoint fired in Arm B, ABSENT in Arm A:
grep -c "RescaleCaptureByBelievedDanger\|\[capture\].*believed" <armB-result-dir>/match_*_debug.log
# garrison repoint fired in Arm B, ABSENT in Arm A:
grep -c "RescaleDefendByBelievedDanger\|\[garrison\].*believed" <armB-result-dir>/match_*_debug.log
# (Confirm the log markers exist at execution time; if item-24 emits no dedicated log line,
#  fall back to the review's byte-identity proof + the arm's committed gate values as the
#  firing evidence, and note the absence of a runtime marker in the result card.)
```

## Guardrails

- **Shared checkout:** another worker owns the ladder run + the uncommitted ai.yaml flip.
  Do NOT touch running processes, their result dirs, or the working-tree gate edit until P1
  confirms the ladder is done.
- **Never push.** Commits/merges local only. No attribution trailers.
- **Only artifacts committed from this card:** this run plan, the result card, and the doc
  updates in §"Where numbers get recorded" — plus the ai.yaml gate values on a decisive
  verdict. The per-arm toggle edits are reverted; intermediate arm edits are never committed.
