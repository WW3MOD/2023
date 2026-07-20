# Capture Reliability — Cycle 1 Design

**Date:** 2026-07-20  
**Branch:** main @ 43441501  
**Builds on:** `WORKSPACE/ai-bench/runs/260720_s1_baseline_n10.md`  
**Scope:** Experimental AI only — Normal/Rush/Turtle untouched.

---

## 1. FAILURE TRACE

The baseline characterised failures as "no derrick ever held to term" with flat watcher.log
score curves. Five concrete failure paths inferred from code; correlation to specific runs is
limited because the batch runner overwrites debug.log each match and only success logs were
preserved (baseline §Diagnostics).

### F-1 — TECN killed en route; no immediate replacement available

**Primary failure mode.** The coordinator dispatches at most one TECN per capturable POI per
scan (`ScanInterval: 75`, ai.yaml:135). If that TECN dies before capturing, the next
`ReconcileGuardCommitments` call (CaptureCoordinatorBotModule.cs:429) calls
`Prune(tick, keep)` where `keep = a => !a.IsDead && a.IsInWorld && a.Owner == player`
(CaptureCoordinatorBotModule.cs:432) — the dead TECN is dropped from the ledger, and the
target's commitment is released at line 446. On the same scan, `QueueCaptureOrders` runs
again, but it can only dispatch if `idleCapturers.Length > 0` (CaptureCoordinatorBotModule.cs:241).

If the dead TECN was the only one alive, the AI must wait for production to deliver a
replacement: call-in queue time + edge-to-SR walk at Speed 25 (infantry.yaml:37;
`^Infantry → ^CivInfantry → ^ArmedCivilian → ^TECN`). That walk can span 300–600+ ticks.
On a 7500-tick clock, a TECN lost at t=3000 might not be replaced until t=3500–3600 —
one-sixth of the remaining match spent unable to capture.

`ConsumedByCapture: true` (`^CapturesNeutralBuildings`, infantry.yaml:903) compounds this:
every SUCCESSFUL capture also removes the TECN from the live pool. Successful runs in batch 1
each consumed 1–2 TECNs for income; then the pool had to refill from production before
attempting additional captures. In failing runs the pool was depleted by deaths instead.

UnitLimits are `tecn.america: 3` (ai-america.yaml:37), `tecn.russia: 3` (ai-russia.yaml:37).
The builder weight `tecn.*: 500` (ai-america.yaml:8; ai-russia.yaml:8) is highest in the
mix, so the production system does queue replacements eagerly — but production latency
remains a real gap, not a configuration problem.

**Cannot confirm F-1 from log data.** The exact tick of TECN death vs capture attempt in
failing runs is unknown. The flat score curves (no income ramp) are consistent with "TECN
never survived to capture" but do not distinguish F-1 from F-3.

### F-2 — Commitment TTL shorter than walk time on longer routes

`DefaultCommitmentTicks: 300` (ai.yaml:122; PoiGoalGuard.cs:129). At Speed 25, one cell
takes `⌈1024 / 25⌉ ≈ 41` ticks. An 8-cell route (edge → SR → derrick, plausible on some
spawns) takes ~330 ticks, exceeding the 300-tick commitment window.

`Prune()` drops expired commitments (PoiGoalGuard.cs:104–116). After expiry, if the TECN has
a brief `IsIdle` flicker mid-walk (the original order-overwriting class that motivated
PoiGoalGuard — see header comment), the coordinator sees it as idle + uncommitted and can
issue a new `CaptureActor` order. That new order restarts the walk, aborting the in-progress
approach. The TECN may oscillate between two targets and complete neither.

For River Zeta specifically, baseline reports derricks at ~3–4 cells from each SR; total
route is ~6–8 cells (~250–330 ticks), straddling the TTL boundary. This is a conditional
failure: it fires only if (a) the route exceeds 300 ticks AND (b) an idle-flicker occurs.
It represents a latent fragility that will manifest reliably on larger maps.

### F-3 — No scan shortcut after TECN loss (75-tick dead zone)

After `ReconcileGuardCommitments` drops a dead TECN's commitment, the next capture pass
fires up to 75 ticks later (`captureScanCountdown`, CaptureCoordinatorBotModule.cs:185–188).
Individually small, but it compounds F-1 (production gap already exists; no point adding an
additional 75-tick processing lag).

### F-4 — Escort AttackMove targets the derrick, not the TECN

`DispatchEscort` issues `AttackMove` to `target.Location` — the derrick cell
(CaptureCoordinatorBotModule.cs:495). The two escort units race directly to the derrick.
If the TECN approaches from a different angle (terrain, pathfinding variation), the escorts
may arrive at the derrick ahead of the TECN, engage enemies at the derrick, and not be
between the TECN and an intercept threat on the approach. The TECN is fragile (200 HP,
Pistol — infantry.yaml:32–34; `^ArmedCivilian` armament) and dies in 1–2 hits from most
combat infantry.

No direct failing-run correlation available. Given the 8-2 win rate, current escort
outperforms no escort, but the approach-screen gap is real.

### F-5 — Failure break-points not observable in current logging

Existing `[exp-capture]` markers only survive in success logs. Missing markers: TECN killed
while committed; no-idle-capturer scan; commitment released with reason. Without these,
diagnosing the F-1 vs F-2 vs F-4 split for each failing run requires guesswork from score
curves.

---

## 2. MINIMAL MECHANISM

### Candidate summary

| Candidate | Closes | Effort | Confidence |
|---|---|---|---|
| (a) INotifyKilled hook + scan reset | F-3, enables F-5 logging | ~1 hr C# | High |
| (b1) Commitment TTL raise to 600 | F-2 | ~30 min YAML | High |
| (b2) Explicit second TECN dispatch | F-1 (partially) | No code change needed | Medium — system already does this; availability is the constraint |
| (c) Escort redesign (gather-then-escort) | F-4 | 2–4 hr C# | Deferred |

### Detailed evaluation

#### (a) Reissue-on-capturer-loss — scan acceleration + death logging

Add `INotifyKilled` to `CaptureCoordinatorBotModule`. In `Killed()`: check if the killed
actor is in `capturingActors`; if so, log the loss and reset `captureScanCountdown = 0`.
The coordinator's existing `ReconcileGuardCommitments` → `QueueCaptureOrders` flow already
handles re-dispatch; this change just eliminates the up-to-75-tick processing lag (F-3).

The death log line (see §4, M-1) also resolves F-5 for future batch runs.

Limitation: accelerating the scan doesn't help if the TECN pool is empty. A dead TECN with
no replacement is a production-gap problem (F-1), not a scan-timing problem.

#### (b1) Commitment TTL raise to 600 ticks — YAML only

Change `DefaultCommitmentTicks` from 300 to 600 in ai.yaml:122. At Speed 25, 600 ticks
covers a 14.6-cell walk — generous margin for all River Zeta routes. Closes F-2
(commitment-thrash on borderline-long walks).

Side-effect: `PoiGoalGuard@poi` is shared by Stable AI (ai.yaml:121: `enable-ai-experimental || enable-ai-stable`). Raising TTL to 600 also applies to Stable. Per SPEC §Bot pair (Stable mirrors validated Experimental config), this is expected and acceptable.

Tradeoff: if a TECN gets genuinely stuck (terrain block, target invalidated before
`ReconcileGuardCommitments` fires), it stays "committed" ~200 ticks longer before the
coordinator detects the stall and re-evaluates. At the short River Zeta distances this is
an acceptable cost — `ReconcileGuardCommitments` also releases commitments whose target is
no longer capturable (CaptureCoordinatorBotModule.cs:444–446), so hard stalls are covered
by the target-validity check regardless of TTL.

#### (b2) Dual TECN dispatch — already in design, availability is the constraint

`QueueCaptureOrdersFromPoiMap` (CaptureCoordinatorBotModule.cs:340) iterates PoiMap's ranked
targets and assigns the nearest idle+uncommitted TECN to each. With two idle TECNs, it
dispatches two naturally. No code change is required.

The gap is TECN availability: `ConsumedByCapture: true` (infantry.yaml:903) and combat
deaths drain the pool faster than production refills it. The production system already
prioritises TECNs (weight 500 = highest), and UnitLimits = 3 provides headroom. The
practical lever is ensuring the second TECN is alive and idle at the same time as the
first. This emerges from (a)+(b1): with faster scan recovery and longer commitment windows,
an existing second TECN is dispatched before its commitment window closes.

#### (c) Escort gather-then-escort — deferred

Changing `DispatchEscort` from `AttackMove(target.Location)` to a staged
`AttackMove(capturer.Location)` + `AttackMove(target.Location)` would put escorts adjacent
to the TECN before the final approach, screening the route. The current fire-and-forget
architecture (CaptureCoordinatorBotModule.cs:486–501) doesn't track escort state; a staged
order would require either a follow-up scan or a separate escort-tracking sub-loop.

Defer to cycle 2. Collect failing-run data from the F-4 log marker first to confirm
approach-intercepts are actually a significant contributor before redesigning the escort.

### Recommendation

**Primary: (b1) TTL raise to 600 + (a) INotifyKilled hook.**

Apply (b1) first (YAML-only, zero risk). Then (a) (small C# change, closes F-3 and makes
all future failing runs legible). Together they address the two code paths with clear
improvement guarantees (F-2 closed, F-3 closed, F-5 resolved).

**Secondary: (b2)** — no action needed, but verify with the new instrumentation that dual
TECN dispatch is actually occurring in successful runs.

**Deferred: (c)** — cycle 2 after instrumented failure data.

Estimated cycle effort: ~1.5 hours net code change, ~30 min validation (one N=5 diagnostic
batch to confirm ≥3/5 capture rate before full N=10 rerun).

---

## 3. STRUCTURAL OPTION

A first-class **CaptureTask** abstraction would model each capture attempt as a lifecycle
object: `{ Target, Capturer, Escorts[], State (Pending | EnRoute | Capturing | Complete |
Failed), RetryCount }`. The task would own the TECN reservation, manage escort position
(follow capturer, not target), detect stall (capturer not approaching target in N ticks →
re-issue), and auto-retry on TECN loss (re-request from production, re-dispatch to same
target). Success/failure events fire cleanly as state transitions.

The current `CaptureCoordinatorBotModule` + `PoiGoalGuard` pair is structurally most of the
way there: the ledger tracks commitment, the coordinator orchestrates, and the scan cycle
handles re-dispatch. The missing pieces are (1) explicit retry/requeue after TECN loss,
(2) escort position-tracking, and (3) stall detection independent of TTL expiry.

**Is this cycle the right moment?** No, for two reasons:

1. Phase 3 offensive logic (`PoiOffensiveBotModule`) uses the same primitives — axis
   commitment, per-unit goal-guard, reassignment on loss — and is still in active design.
   A "mission" abstraction designed for capture alone risks misaligning with the Phase 3
   API. The unification is most valuable when both consumers are known.

2. The three patches in §2 close the S1 gap with ~1.5 hours of work. A full mission
   abstraction is the right long-term architecture but is 1–2 days of design + refactor.
   Unblocking S1 reliability now and abstracting in the v2/Phase 3 milestone is the correct
   sequencing.

Flag this in `RELEASE_V1.md` or `WORKSPACE/HOTBOARD.md` as a post-S1 v2 design goal:
"Capture (and offense axis) generalized to a first-class mission/task abstraction with
retry + follow-escort + stall detection."

---

## 4. INSTRUMENTATION

### Existing markers (debug.log, per-match, currently overwritten by batch runner)

| Tag | Location | What it records |
|---|---|---|
| `[exp-capture] pre-scan` | CaptureCoordinatorBotModule.cs:219 | Every TECN's state (idle/committed/activity) each scan |
| `[exp-capture] poimap-scan` | CaptureCoordinatorBotModule.cs:349 | Scan summary: idleCapturers count, target count, top target |
| `[exp-capture] issue` | CaptureCoordinatorBotModule.cs:403 | Capture order dispatched: capturer, target, score, tick |
| `[exp-capture] escort dispatched` | CaptureCoordinatorBotModule.cs:500 | Via `AIUtils.BotDebug` |

These are sufficient to reconstruct a SUCCESS trace but leave the failure break-point
invisible.

### Missing markers to add (§2 recommendation a)

**M-1 — TECN killed while committed**

```
[exp-capture] tecn-killed player=X actor=tecn.america@(14,22) committed=true objective=capture:4721 tick=2150
```

Source: implement `INotifyKilled` on `CaptureCoordinatorBotModule`; check `capturingActors`
for the killed actor; log and reset `captureScanCountdown = 0`.  
File: `CaptureCoordinatorBotModule.cs` — add interface + one method (~15 lines).

**M-2 — No idle capturer available on scan**

```
[exp-capture] no-idle-capturers player=X total-tecns=1 committed=1 idle=0 tick=2200
```

Source: branch at CaptureCoordinatorBotModule.cs:241 (`if (idleCapturers.Length == 0) return;`).
Add log line before the return, reporting total capturingActors count and committed count.

**M-3 — Commitment released (with reason)**

```
[exp-capture] commitment-released player=X actor=tecn.america objective=capture:4721 reason=dead tick=2225
[exp-capture] commitment-released player=X actor=tecn.america objective=capture:4721 reason=captured tick=3100
```

Source: in `ReconcileGuardCommitments` after `goalGuard.Ledger.Release(tecn)` at
CaptureCoordinatorBotModule.cs:446 (reason = "captured/gone"); and after `Prune()` at line
432 by comparing ledger count before/after (reason = "dead/expired").

### debug.log per-match preservation

The batch runner (`tools/autotest/run-batch.sh`) overwrites debug.log each match. **Cheapest
fix:** after each match's result is copied to `<results-dir>/match_N/`, also copy debug.log
there as `match_N_debug.log`. One bash line added in the post-match result-copy block.

**Alternative (cheaper, no runner change):** fold M-1, M-2, M-3 into the existing per-match
`watcher.log` output — watcher.log IS preserved per match. The watcher trait already writes
periodic state; adding capture-event hooks there (~20 lines of C#) would make every failing
run self-explaining without runner changes.

With M-1/M-2/M-3 in place, the next N=10 batch's failing runs would show either:
- `tecn-killed` → F-1 confirmed; focus on production gap or escort redesign
- `no-idle-capturers` at a specific tick → F-1 gap duration quantified
- `commitment-released reason=expired` → F-2 confirmed; TTL was borderline

---

## 5. RISKS + NON-GOALS

### Invariants that must not change

| Invariant | Guarantee |
|---|---|
| Normal / Rush / Turtle untouched | `CaptureCoordinatorBotModule` and `PoiGoalGuard` are gated `RequiresCondition: enable-ai-experimental` and `enable-ai-experimental \|\| enable-ai-stable`. Legacy bots use `CaptureManagerBotModule@tecn` under `enable-ai-legacy-only` (ai.yaml:101–102). No proposed change touches that path. |
| No unit stat changes | All changes are C# coordinator logic and YAML config values (`DefaultCommitmentTicks`, log lines). No edits to `Armor`, `Health`, `Speed`, `Captures`, `CaptureDelay`, or any unit YAML. |
| SUPPLYROUTE deny-only | SRs have no `CaptureManager` trait → harmlessly skipped at CaptureCoordinatorBotModule.cs:362–366; PoiMap already excludes SRs from `GetCaptureTargets()`. No proposed change alters this. |
| Stable AI | Shares `PoiGoalGuard@poi`; TTL raise also applies to Stable. Per SPEC §Bot pair (Stable = validated Experimental snapshot), this is within policy. |

### Non-goals for this cycle

- **Capture rate beyond 6/10** — goal is ≥6/10 in-window reliability (S1 advancement bar);
  further tuning belongs in cycle 2 with instrumented data.
- **Escort overhaul** — deferred; collect F-4 evidence first.
- **S1 LADDER bar re-form** — flagged in baseline as a separate user-ratification item.
- **Map geometry changes** — SR spawn placement, derrick distance, map layout are fixed for v1.
- **PoiMap scoring or IncomeWeights changes** — out of scope; scoring/pipeline is validated.

---

## Implementation checklist (cycle 1)

- [ ] **ai.yaml:122** — raise `DefaultCommitmentTicks: 300 → 600`
- [ ] **CaptureCoordinatorBotModule.cs** — add `INotifyKilled`, reset `captureScanCountdown = 0`, log M-1
- [ ] **CaptureCoordinatorBotModule.cs:241** — add M-2 log before `return`
- [ ] **CaptureCoordinatorBotModule.cs:432 + 446** — add M-3 log after Prune + Release
- [ ] **run-batch.sh or watcher** — preserve debug.log per match (or fold M-1/M-2/M-3 into watcher.log)
- [ ] **Validation batch** — N=5 diagnostic run; confirm ≥3/5 in-window capture rate before full N=10
