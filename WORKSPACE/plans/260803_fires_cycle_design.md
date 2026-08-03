# Fires / Artillery Behaviour Cycle — forward design

> **Recon + design doc.** No code changed to produce it. Verified against code
> 2026-08-03 at `main @ 18ff375b` (working tree clean of the fires files; branch
> 130 commits ahead of `origin/main`, all committed). Every current-state claim
> cites `file:line`. Scope-locked: **proposes no unit-stat changes** (stat edits
> are user-gated) and no engine/YAML edits — design only.

## 0. TL;DR — the core goal is already SHIPPED and ENABLED

The roadmap item as briefed ("bots employ artillery doctrinally instead of
today's behaviour where `ai.yaml` lumps artillery/SHORAD in with tanks as 'main
line' and they charge like MBTs") **has already been built, committed, and
enabled in `@experimental`.** The briefing premise is stale. What exists at HEAD:

| Doctrine behaviour (goal) | Status | Flag (all `@experimental`-only, default-off on the class) | Commit |
|---|---|---|---|
| Artillery is its own role, split tube vs rocket | **shipped** | `UseUnitRoles: true` (frees SHORAD/MANPADS off the line too) | `3bddab50` |
| Standoff at max weapon range during assaults | **shipped, enabled** | `FiresStandoff: true` | `6a33813d` |
| Held **behind the MainBattle screen** (defence-in-depth echelon) | **shipped, enabled** | `EchelonPositioning: true` | `c43dc391` |
| Held **behind the believed frontier** (not on it) | **shipped, enabled** | `MinFrontierDistanceCells: 4` | (auto/frontier-standoff) |
| Rocket arty holds fire unless the salvo repays its ammo (EV gate) | **shipped, enabled** | `FiresEvGate: true` | `b25ba2f0` |
| Aim at the **clump**, not the nearest (AoE cluster targeting, shared L3) | **shipped, enabled** | `ClusterTargetingCondition: enable-ai-experimental` | `b25ba2f0` |

So "standoff positioning behind the line at max-range" (goal bullet 1) is **done**.
This doc therefore is **not** a greenfield design; it is a forward design for the
**residual doctrine gaps** the shipped cycle does *not* cover, plus the
**measurement** the cycle is still owed. If instead you wanted a clean re-audit of
the shipped cycle, §1 is that audit standalone.

The two doctrine employments still genuinely missing (goal bullets 2–3):

- **"Suppressive/preparatory fires *during assaults*"** — only *emergently* present
  (arty holds at standoff and `AutoTarget` FireAtWill keeps shooting while the
  screen presses). There is **no preparatory-fire sequencing** (soften the
  objective *before* the screen commits) and **no suppression feedback** (the AI
  never reads whether its fire is actually suppressing, so the screen's advance is
  not coordinated with the barrage).
- **"Continuous bombardment of *static positions*"** — **absent.** Artillery only
  ever fires as a peeled-off member of an *active offensive axis* aimed at a POI.
  There is no standing fire-mission that bombards believed-static enemy positions
  (defences/structures) from safe standoff independent of an assault.

---

## 1. Current-state audit (recon)

### 1.1 Role resolution — tube vs rocket is already derived
`UnitRoleResolver` (`engine/OpenRA.Mods.Common/Traits/World/UnitRoleResolver.cs`)
classifies every actor once at load into `UnitRole` (`:37-48`): the artillery role
is **`IndirectFire`**, assigned when max weapon `MinRange ≥ IndirectMinRange`
(4 cells) **or** max `Range ≥ IndirectRangeFloor` (35 cells) (`Classify`, `:364`).
A second axis `IndirectFireKind` (`:50-60`) splits **Tube** (salvo `Burst` 1–3:
Giatsint/Paladin) from **Rocket** (`Burst ≥ RocketSalvoBurstFloor` = 8:
Grad/TOS/M270), via `ClassifyIndirectKind` (`:308-314`). Facts/`Classify` are a
pure split (`ExtractFacts` `:247-303`), NUnit-pinnable without a game run. Query
seam: `GetRole(actor)` / `GetIndirectKind(actor)`.

### 1.2 Who commands ground units (the seam the whole cycle rides)
For `@experimental` the air `SquadManagerBotModule` sets `IgnoreGroundUnits: true`
(`ai.yaml:629,692`), so **`PoiOffensiveBotModule` owns the entire ground pool**,
issuing one grouped `AttackMove` per axis to the objective cell
(`CommitAndOrder`). The legacy `Squads/States/GroundStates.cs` FSM only drives the
`@stable`/legacy profiles. So all fires behaviour lives in
`PoiOffensiveBotModule`, and a `GroundStates` change would *not* touch
`@experimental` (architecture.md §AI configuration).

### 1.3 Standoff (shipped) — `FiresStandoffMath`
`PoiOffensiveBotModule.CommitAndOrder` peels every `IndirectFire` piece off the
axis group (`:1684-1715`) and calls `OrderFiresStandoff` (`:1819`). Geometry is the
pure `FiresStandoffMath` (`engine/.../BotModules/FiresStandoffMath.cs`): one anchor
at `StandoffRadius = maxRange − margin` (floored) from the axis target on the
target→piece bearing (`StandoffAnchor`, `:80`); `NeedsReposition` (`:98`) holds the
piece inside a hysteresis band so band-edge chatter can't re-order it every
re-eval; `NearestPassableCell` (`:40`) clamps the anchor to passable ground.
Flags: `FiresStandoff` default-false (`PoiOffensiveBotModule.cs:243`),
`FiresStandoffMargin` 2c (`:247`), `Hysteresis` 2c (`:252`), `Floor` 3c (`:256`).

### 1.4 Echelon (shipped) — `EchelonMath`
When a screen exists, the piece anchors behind the **screen centroid**, not the
target: `EchelonDepth = max(EchelonMinDepth, (ownMaxRange − screenRange) +
EchelonBuffer)` (`EchelonMath.EchelonDepth`), anchor offset away from the target
(`EchelonAnchor`), consumed at `PoiOffensiveBotModule.cs:1874-1875`. A piece with
**no screen** (pure-fires axis / deliberate solo tasking) falls back to the
target-standoff (`:1889-1890`) — "explicit tasking beats the echelon bias" is
structural. `MinFrontierDistanceCells` (`:367`) then walks the echelon anchor
rearward until it is ≥ N coarse control-field cells behind the believed enemy
frontier (`PushEchelonBehindFrontier`, `:1922`). Flags: `EchelonPositioning`
default-false (`:283`) + `EchelonBuffer/MinDepth/Tolerance/ScreenRangeFallback`
(`:287-300`).

### 1.5 Ammo expected-value gate (shipped) — `FiresEconMath`
`FiresEvGate` (`:263`) flips **rocket** pieces between HoldFire/FireAtWill:
`RocketFireWorthy` (`:1987-2061`) prices one salvo — `SalvoCost = ceil(Burst /
ReloadCount) × SupplyValue` (`FiresEconMath.SalvoCost`) — against the best
splash-weighted clump value among fog-legally-visible enemies in range
(`ProjectedClumpValue` / `FireWorthy`), and only fires when value ≥ cost ×
`FiresEvMarginPercent`. **Tube pieces are exempt** (may engage singles) — the gate
keys on `GetIndirectKind(u) == Rocket` (`:1859`). A held piece is reconciled back
to FireAtWill on both the targets and no-targets paths so it can never strand in
HoldFire (`ReconcileFiresHoldFire`, the item-19 review catch).

### 1.6 AoE cluster targeting (shipped, shared L3) — `AutoTarget`
`AutoTarget` earns a bounded priority pull for a candidate whose *surrounding
clump* takes the most projected splash (`FiresEconMath.ClusterWeight/Score`,
`AutoTarget.cs:1141-1198`), gated per-unit by `ClusterTargetingCondition`
(`:226`) — granted to `enable-ai-experimental` on the area-weapon templates
(`mods/ww3mod/rules/defaults.yaml:395,413`). The bonus is capped
(`ClusterMaxBonus`, `:245`) so it stays *inside* the range tiebreak and can never
cross a priority bucket → a lone target is byte-identical to the pre-cluster score.
This lives in the **shared** autotargeter on purpose, so human-owned artillery can
be opted in later (ai-realism §4 "build it where both benefit").

### 1.7 Free-pool role exclusion (shipped)
With `UseUnitRoles: true` (`ai.yaml:282`), `IsEligibleCombatUnit`
(`PoiOffensiveBotModule.cs:1455-1460`) admits only `MainBattle`/`IndirectFire`
(minus troop carriers) to the offensive free pool — **SHORAD/MANPADS/Recon are
dropped off offensive axes by class** (the exact "don't send AA with the tanks"
the goal asks for). `SkipOutOfAmmoUnits: true` (`:286`) stops the offense
recruiting an evacuating (spent) rocket piece and cancelling its evac.

### 1.8 What the mechanics already provide, and what the AI ignores
- **Suppression** is a unit/weapon mechanic only: warhead-granted graduated
  conditions degrade speed/accuracy/turret (architecture.md §Suppression system;
  10-tier infantry, 5-tier vehicle). **No fires/offense bot module reads a
  target's suppression state** — verified: the only bot-side `Suppress*` readers
  are `GarrisonManager` (its *own* soldiers ducking) and support-power/base-builder
  paths, none in the fires cycle. So "advance under our own suppressive fire" is
  not coordinated; it only *emerges* from AutoTarget firing at standoff.
- **Belief store** already keeps **static** enemy contacts (structures/defences are
  `IsStatic`, decay-exempt, `BeliefStore.cs:273-278`) and `ControlField` re-asserts
  believed-enemy **site anchors** (`ControlField.cs:493-501`) — a ready, fog-legal
  target set for a bombardment mission that does not yet exist.
- **Aircraft standoff** is the engine-provided precedent the ground standoff
  mirrors (architecture.md §Attack standoff; heli Stage-0 `StandoffEngagement`).

---

## 2. Gap list vs the doctrine goal (ranked by on-screen visibility)

**G1 — Continuous bombardment of static positions *(most visible; absent)*.**
Watching a match, you never see artillery sitting back and methodically shelling a
known enemy strongpoint/defence line unless an offensive axis happens to target it.
Idle arty with ammo and a believed static target in reach does nothing. This is the
clearest "reads like a real battlefield" behaviour still missing.

**G2 — Preparatory fires before the assault steps off *(very visible; absent)*.**
The screen and the guns arrive together; there is no "the barrage lands first, then
the infantry/tanks go in." Assaults look simultaneous, not sequenced.

**G3 — Suppression-coordinated advance *(visible; absent)*.** The screen presses on
a timer/geometry, not on whether the objective is actually suppressed. A viewer
sees tanks walk into an un-softened position; a doctrine-literate viewer notices the
fires and the manoeuvre aren't talking to each other.

**G4 — The whole cycle is unmeasured *(invisible but load-bearing)*.** Every fires
flag is enabled, yet the item-25 re-baseline shows `@experimental` *underperforming*
`@stable` with everything on (influence-stack.md §Known gaps). The fires cycle's
isolated contribution has **never been A/B'd**. Adding more fires behaviour on top
of an unmeasured base compounds the risk.

**G5 — EV discipline is rocket-only; no human opt-in for cluster targeting
*(least visible; partial)*.** ai-realism §5's north-star is "*every* weapon's
ammo-EV," but tube arty (and everything else) has no EV gate; and the shared
cluster-targeting layer has no human-facing toggle yet (ai-realism §4).

---

## 3. Phased design

Design rules honoured by every phase below (from influence-stack.md §Invariants):
zero `SharedRandom`/`LocalRandom` draws; a **fact/decision split** with the decision
in a static pure-math class (NUnit-pinnable, no game run); a **default-off flag on
the class**; and — because every consumer here is `PoiOffensiveBotModule` /
`AutoTarget`, which are **per-profile trait instances** (`@experimental` gated by
`RequiresCondition: enable-ai-experimental`, `@stable` a separate instance) — a
**single default-off flag is sufficient** (the first of influence-stack.md's two
gating patterns). No phase adds a field to a *shared world layer*, so the Phase-4
`RequestFrontlineProfile` per-player opt-in recipe is **not** needed. Reads are
fog-legal (belief store / visible-only), reusing the influence stack rather than a
parallel one.

### Phase 0 — Measurement baseline for the SHIPPED cycle *(do first; user-gated)*
- **Mechanism.** No new behaviour. Run a paired batch A/B on the existing rungs
  with the fires flags **off** (Arm A: `FiresStandoff/FiresEvGate/EchelonPositioning`
  + cluster condition removed on `@experimental`) vs **on** (Arm B, current HEAD),
  everything else fixed, to isolate the fires cycle's net effect against the item-25
  zero. Reuses the `ai-bench/` instrument (item-25 re-baseline harness).
- **Pure-math class.** none (measurement only).
- **Consumers.** none.
- **Flag/gating.** A/B is done by toggling the *existing* `@experimental` flags in a
  scratch config; no code. `@stable` untouched, so it stays the frozen control.
- **Determinism.** Seeded batch (item-15 `--seed`); the flags are already
  byte-identical-off by construction, so Arm A must reproduce the `@stable`-relative
  frozen fires path.
- **Acceptance.** A signed result card in `ai-bench/runs/` stating the fires
  cycle's S1 win-delta and S2 net-swing vs fires-off, ± the noise band. **USER-GATED
  (multi-test batch).**

### Phase 1 — Continuous bombardment tasking *(covers G1)*
- **Mechanism.** A standing, low-priority fire-mission: any idle `IndirectFire`
  piece with ammo and a **believed static enemy position** in weapon range is sent
  to a standoff anchor against that position and left to fire, independent of any
  offensive axis. Targets come **only** from the belief store's `IsStatic` contacts
  / `ControlField` believed-enemy site anchors (fog-legal — no ground-truth scan).
  Positioning **reuses** `FiresStandoffMath`/`EchelonMath`/`PushEchelonBehindFrontier`
  (no parallel standoff machinery), and worthiness **reuses** `FiresEconMath.FireWorthy`
  (rocket pieces still only fire a worthwhile clump; tube pieces may shell a single
  static target — the tube/rocket split the goal's §5 asks for).
- **Pure-math class.** `BombardmentMath` (new, nested in the module file). *Facts:*
  believed-static target positions + their build value, each piece's max range /
  kind. *Decision:* `SelectBombardTarget(pieces, targets, ...)` → the assignment
  (which piece shells which target) + the standoff anchor, and `Worthwhile(...)`
  delegating to `FiresEconMath`. All integer, order-independent (sums / min-max),
  ties broken by cell then ActorID.
- **Consumers.** `PoiOffensiveBotModule` — a new pass in `CommitAndOrder`/tick that
  runs **after** axis assignment on the *residual* free pool (pieces no axis
  claimed), committing each to the `PoiGoalGuard` ledger under `bombard:<targetId>`
  so it isn't double-tasked, releasing when out of ammo (→ evac) or the target
  is verified gone.
- **Flag/gating.** `ContinuousBombardment` default-false on the module; declared only
  in the `@experimental` ai.yaml block. `@stable`/legacy never see it → byte-identical.
- **Determinism.** Belief-store reads (already zero-RNG, per-player); assignment is a
  fixed-order greedy over ActorID-sorted pieces and cell-sorted targets. No draws.
- **Acceptance.** NUnit: `BombardmentMath` assigns the nearest in-range worthwhile
  static target and produces a rear-of-frontier anchor; **and** (user-gated) a demo
  or S2 batch showing idle arty shelling a known enemy defence line from standoff
  with no axis active. **USER-GATED for the in-game leg.**

### Phase 2 — Preparatory fires (bombard-then-assault sequencing) *(covers G2)*
- **Mechanism.** Per offensive axis that contains both a screen and fires pieces:
  when the axis is still in its *approach* (centroid distance to target >
  `AssaultRadiusCells`, the existing cohesion gate at `:1749`), **hold the screen at
  a start line** for up to `PrepFireMaxTicks` while the standoff arty (already in
  position from §1.3–1.4) fires on the objective; release the screen to assault when
  the prep window elapses (Phase 2) or the target is suppressed (Phase 3). Reuses the
  existing per-axis re-eval loop and the dispersion/cohesion state already tracked.
- **Pure-math class.** `PrepFireMath.ShouldHoldScreen(distToTargetCells,
  assaultRadiusCells, prepTicksElapsed, prepMaxTicks)` → bool. Pure, tick-counted,
  no world reads.
- **Consumers.** `PoiOffensiveBotModule.CommitAndOrder` — gate the grouped screen
  `AttackMove` (issue a hold/short start-line move instead) while `ShouldHoldScreen`
  is true; the fires peel-off is unchanged, so the guns are already shooting.
- **Flag/gating.** `PreparatoryFires` default-false on the module; `@experimental`
  block only. Off ⇒ the screen order is exactly today's ⇒ byte-identical.
- **Determinism.** Per-axis tick counter (the module already stamps `tick`); no RNG.
  The hold is a bounded integer countdown, so it can never deadlock an axis.
- **Acceptance.** NUnit: `ShouldHoldScreen` holds in the approach band until
  `prepMaxTicks`, then releases; never holds inside the assault radius. In-game
  (user-gated): screen visibly waits at a start line while shells land, then goes in.
  **USER-GATED for the in-game leg.**

### Phase 3 — Suppression-coordinated advance *(covers G3; upgrades Phase 2)*
- **Mechanism.** Replace/augment Phase 2's pure timer with a **fog-legal
  suppression read**: release the screen early once the *observed* suppression on
  enemies at the objective crosses a threshold (the barrage has done its job), and
  extend the hold (bounded by `prepMaxTicks`) while it hasn't. Suppression is read
  **only** from enemies the player can legally see (`CanBeViewedByPlayer`), summing
  their existing suppression-condition tiers — no new mechanic, just a consumer of
  the shipped suppression system (§1.8).
- **Pure-math class.** `AdvanceUnderCoverMath.ScreenMayAdvance(observedSuppression,
  suppressThreshold, prepTicksElapsed, prepMaxTicks)` → bool. Pure; the world does
  the (fog-legal) suppression tally and passes the scalar in (fact/decision split).
- **Consumers.** same gate site as Phase 2; this swaps the release predicate.
- **Flag/gating.** `SuppressionCoordinatedAdvance` default-false on the module;
  needs `PreparatoryFires` on. `@experimental` only.
- **Determinism.** The suppression tally is an order-independent integer sum over a
  bounded, fog-filtered actor set (same discipline as `RocketFireWorthy`'s clump
  scan); zero draws.
- **Acceptance.** NUnit on the predicate (advance at/above threshold, hold below,
  hard-release at `prepMaxTicks`). In-game (user-gated): the screen steps off *when*
  the objective is visibly suppressed, not on a fixed clock. **USER-GATED in-game.**

### Phase 4 — (backlog) tube EV discipline + human cluster opt-in *(covers G5)*
- **Mechanism.** (a) Extend the ammo-EV gate to **tube** pieces with a laxer margin
  (tube may take singles, but a Paladin 3-round salvo still shouldn't fire at a lone
  cheap target for a loss) — reuses `FiresEconMath.FireWorthy` with a per-kind
  margin. (b) Expose cluster targeting to humans via a stance/toggle so player-owned
  artillery gets the same clump preference (ai-realism §4 shared-L3 promise).
- **Pure-math class.** reuses `FiresEconMath`; no new decision math (a) / none (b).
- **Consumers.** `PoiOffensiveBotModule` (a); a UI stance + `AutoTarget`
  condition-grant (b).
- **Flag/gating.** (a) `TubeEvGate` default-false, `@experimental` only. (b) human
  opt-in is a stance, off by default — no benchmark exposure.
- **Determinism / acceptance.** as §1.5/§1.6. Low visibility → backlog after 1–3.

---

## 4. User-gated measurement steps (collected)

Per the hard rule (no autonomous multi-test runs), these acceptance legs need an
explicit goahead and are parked for a single grant:

- **Phase 0** — the fires-on vs fires-off isolation A/B (the prerequisite baseline).
- **Phase 1** — S2 batch / demo that idle arty bombards a static position.
- **Phase 2 / 3** — S2 batch or demo of prep-fire hold + suppression-timed release.
- Any re-baseline if a phase ships default-on (per the split-SPEC governance:
  a global behaviour change is a re-baseline-class event).

All NUnit acceptance (the `*Math` pins) is **not** gated — it runs headless.

---

## 5. Open design questions, ranked by how much they change the build

1. **Is forward gap-work even wanted, given the core is shipped?** The briefing
   assumed greenfield; the cycle is built and enabled. If the real intent was
   "audit what shipped" then §1 is the deliverable and §3 is optional. **This is the
   biggest fork — it decides whether Phases 1–3 happen at all.**
2. **Measure before building (Phase 0 first) or build then measure?** Given the
   item-25 deficit with everything on, I'd strongly sequence Phase 0 ahead of 1–3 —
   but that front-loads a user-gated batch before any new behaviour lands. If you'd
   rather see behaviour first, Phases 1–3 can ship default-off and be priced later.
3. **Bombardment target set — static-only, or believed mobile clusters too?**
   Phase 1 as scoped hits believed *static* positions (cleanest fog-legal set). Do
   you also want standing bombardment of believed *massing* (mobile clusters)? That
   widens the target source to decaying mobile contacts and changes the worthiness
   maths and the visible behaviour materially.
4. **Prep-fire hold: risk vs realism.** Holding the screen at a start line trades
   tempo for a cleaner assault; on a losing/contested axis a hold could cost the
   initiative. Phase 2's bounded countdown caps the downside, but the *threshold*
   (how long to prep, how suppressed is "enough") is a balance lever that needs a
   sweep — and could hurt win-rate even where it helps watchability.
5. **Does tube arty need its own EV gate now (Phase 4a), or is singles-firing fine?**
   Low stakes; only shifts whether Phase 4 is worth pulling forward.
