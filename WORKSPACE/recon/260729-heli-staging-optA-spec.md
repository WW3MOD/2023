# Spec — Helicopter forward-staging, Option A (impl-ready)

> Implementation spec for **Option A** of the heli forward-staging recon
> (`WORKSPACE/recon/260729-heli-forward-staging.md`, recon verified @ `c11ce511`).
> This spec code-read against **main @ `c4ba0eee`**. No code changed; no tests run.
> A fresh implementer can execute this without re-deriving anything.
>
> **Label key:** MEASURED = observed from a run · CODE-READ = read directly from
> the cited file:line @ `c4ba0eee` · HYPOTHESIS = reasoned, not yet verified in-run.

## 0. One-paragraph design summary

Add an `@experimental`-only, default-OFF forward-staging pass to
`HelicopterSquadBotModule`. Each slow scan (100 ticks), for every idle attack
heli still loitering within N cells of its own Supply Route, issue **one**
deterministic `Order("Move", h, stageCell)` where `stageCell` is a fixed
fraction of the SR→top-offensive-POI vector — the *exact* computation the
shipped `MountedTransportBotModule.PreContactStagingCell` already uses
(`MountedTransportBotModule.cs:539-553`). The heli flies forward and hovers
there (still in `idleHelicopters`, still eligible for the 2-ready squad
threshold) instead of hovering at the SR corner. The pass is wrapped in a
single `if (!Info.ForwardStaging) return;` guard and issues **zero** RNG draws,
so every non-experimental profile is byte-identical and the ON-path adds no
synced-RNG draw beyond deterministic PoiMap queries.

## 1. Root cause being addressed (CODE-READ, from recon)

Fresh call-in attack helis get `MoveTo(SR cell)` because the SR `RallyPoint`
sets no `Path` (`ProductionFromMapEdge.cs:173-175`; SR yaml `structures.yaml:272-274`),
then hover in place via `FlyIdle` (no `IdleBehavior`, `Aircraft.cs:921-937`).
A heli that never reaches `AttackSquadSize = 2` ready attack helis
(`HelicopterSquadBotModule.cs:25`) sits in `idleHelicopters` at the SR corner
forever. Fix = a *new* forward-staging behaviour, not a coordinate patch.

## 2. Pattern being mirrored (CODE-READ)

`MountedTransportBotModuleInfo.DeliverBeforeContact` / `PreContactStagingPct`
(`MountedTransportBotModule.cs:72-79`) + `PreContactStagingCell`
(`:539-553`). Staging cell:

```csharp
var srPos = world.Map.CenterOfCell(srCell);
var tgtPos = world.Map.CenterOfCell(targets[0].Location);
var stagePos = srPos + (tgtPos - srPos) * Info.PreContactStagingPct / 100;
var cell = world.Map.CellContaining(stagePos);
return world.Map.Contains(cell) ? cell : (CPos?)null;
```

`targets` = `poiMap.GetOffensiveTargets(player)` (default overload,
`suppressOmniscientThreat = false`, `PoiMap.cs:297`). Own-SR lookup:
`FindOwnSupplyRoute()` (`MountedTransportBotModule.cs:194-199`) filtering
`world.Actors` by owner + `Info.SupplyRouteTypes` (default `{ "supplyroute" }`,
`:70`). PoiMap handle acquired in `TraitEnabled` via
`world.WorldActor.TraitOrDefault<PoiMap>()` (`:212`).

## 3. Gating classification (CODE-READ — decides double-gate vs not)

`HelicopterSquadBotModule` has **two YAML instances only**:
- shared base `HelicopterSquadBotModule` — `enable-ai-any && !enable-ai-experimental`
  (`ai.yaml:777-785`) — covers **normal / rush / turtle / stable**
  (comment `ai.yaml:774-776`: "Normal/rush/turtle/stable keep the frozen default").
- `HelicopterSquadBotModule@experimental` — `enable-ai-experimental` (`ai.yaml:790-804`).

There is **no** `HelicopterSquadBotModule@stable` twin — stable rides the shared
base block. So this is the **per-profile** gating pattern from the influence-stack
Invariants (`influence-stack.md:96`): the experimental instance is a *distinct
trait instance* from the frozen one, therefore a **default-OFF Info flag set only
on the experimental block is sufficient** — **no** `InfluenceStack.Participates`
double-gate is required (that double-gate is only for consumers bolted onto a
*shared* instance such as `SupplyFollowerBotModule@supply`). This mirrors how the
existing `SkipRearmReadyCheck` / `StandoffEngagement` / `DangerFieldAvoidance`
flags on this same module are gated (`ai.yaml:799-804`).

## 4. Code changes — `HelicopterSquadBotModule.cs`

All anchors are line numbers @ `c4ba0eee`.

### 4.1 New Info fields — insert in the blank line at `:89` (after `AirDangerRetreatCells` `:88`, before `Create` `:90`)

```csharp
		[Desc("Experimental (default false = frozen): when an idle attack heli is still loitering",
			"within ForwardStagingMaxDistanceCells of its own Supply Route and no squad has formed,",
			"push it forward to a pre-contact staging cell (a fraction of the way from the SR toward",
			"the top PoiMap offensive target) instead of leaving it hovering at the SR corner. Mirrors",
			"MountedTransportBotModule.DeliverBeforeContact. OFF by default so normal/rush/turtle/stable",
			"stay byte-identical; only HelicopterSquadBotModule@experimental turns it on.")]
		public readonly bool ForwardStaging = false;

		[Desc("Fraction (percent) of the SR->top-offensive-POI distance used as the staging cell.",
			"50 = halfway between our SR and the top offensive POI. Clamp well short of contact so",
			"ammo-carrying, target-less helis do not stage into believed AA. Only used when ForwardStaging is set.")]
		public readonly int ForwardStagingPct = 40;

		[Desc("Only stage attack helis whose distance from the SR is at or below this (map cells).",
			"Helis already forward (e.g. a low-ammo heli that returned near the front) are left alone.",
			"Only used when ForwardStaging is set.")]
		public readonly int ForwardStagingMaxDistanceCells = 8;

		[Desc("Actor types of the bot's home Supply Route — used to anchor the staging vector.",
			"Mirrors MountedTransportBotModuleInfo.SupplyRouteTypes.")]
		public readonly HashSet<string> SupplyRouteTypes = new() { "supplyroute" };
```

`HashSet<string>` requires `using System.Collections.Generic;` — **already
imported** (`:12`). `ForwardStagingPct` default 40 (not 50) applies the recon's
own §Option-A risk clamp ("clamp the fraction well short of contact",
`260729-heli-forward-staging.md:45`); the YAML can override.

### 4.2 New module state — add near the existing collections (`:98-100`) and handles (`:104-105`)

Add beside `idleHelicopters` (`:99`):
```csharp
		readonly Dictionary<Actor, CPos> stagedTo = new Dictionary<Actor, CPos>();
```
Add beside `threatMap` (`:104`):
```csharp
		PoiMap poiMap;
```

### 4.3 Acquire the PoiMap handle — in `Initialize()`, after `:135` (`threatMap = …`)

```csharp
			poiMap = world.WorldActor.TraitOrDefault<PoiMap>();
```
`Initialize()` is idempotent (guarded by `initialized`, `:128-129`); a
`TraitOrDefault` call draws no RNG.

### 4.4 Call the staging pass — in `BotTick`, inside the slow-scan block, after `CleanUpHelicopters()` (`:158`)

```csharp
			if (--scanCountdown <= 0)
			{
				scanCountdown = Info.ScanInterval;
				FindNewHelicopters();
				CleanUpHelicopters();
				StageIdleHelicopters();   // <-- new
			}
```
Runs on the same 100-tick cadence as the other pool passes. `StageIdleHelicopters`
is the FIRST statement's guard = full byte-identity when OFF (see §6).

### 4.5 Prune stale staged entries — extend `CleanUpHelicopters()` (`:205-206`)

Add alongside the existing `idleHelicopters.RemoveAll(...)`:
```csharp
			foreach (var a in stagedTo.Keys.ToList())
				if (a == null || a.IsDead || !a.IsInWorld || !idleHelicopters.Contains(a))
					stagedTo.Remove(a);
```
This drops a heli from the staged set the moment it dies OR leaves the idle pool
(i.e. is recruited into a squad by `TryLaunchAttackMission`, `:294`). When such a
heli later returns to the idle pool it is re-eligible for staging only if it is
again near the SR (§4.6 distance gate) — clean re-arm of the behaviour.

### 4.6 New methods — add after `CleanUpHelicopters()` (before `PruneSquads`, `:238`)

```csharp
		Actor FindOwnSupplyRoute()
		{
			return world.Actors.FirstOrDefault(a =>
				a.Owner == player && !a.IsDead && a.IsInWorld
				&& Info.SupplyRouteTypes.Contains(a.Info.Name));
		}

		// Pre-contact forward staging (experimental, ForwardStaging). Push idle attack helis that
		// are still loitering near the SR forward to a fraction of the SR->top-POI vector, so they
		// stage toward the fight instead of hovering at the SR corner. Deterministic: PoiMap query
		// + integer vector math, ZERO random draws. Fully skipped (byte-identical) when the flag is off.
		void StageIdleHelicopters()
		{
			if (!Info.ForwardStaging)
				return;

			var ownSR = FindOwnSupplyRoute();
			if (ownSR == null)
				return;
			var srCell = ownSR.Location;

			var stageCell = ForwardStagingCell(srCell);
			if (!stageCell.HasValue)
				return;

			var maxDistSq = (long)Info.ForwardStagingMaxDistanceCells * Info.ForwardStagingMaxDistanceCells;

			foreach (var h in idleHelicopters)
			{
				if (h.IsDead || !h.IsInWorld || !h.IsIdle)
					continue;
				if (stagedTo.ContainsKey(h))
					continue;

				// Attack helis only — scouts/transports have their own mission paths.
				var role = h.TraitOrDefault<AIHelicopterRole>();
				if (role == null)
					continue;
				var r = role.Info.Role;
				if (r != HelicopterAIRole.AttackHeavy && r != HelicopterAIRole.AttackLight)
					continue;

				// Same readiness definition the squad launch uses (health gate always applies;
				// ammo gate bypassed under SkipRearmReadyCheck exactly as for TryLaunchAttackMission).
				if (!IsReadyForMission(h))
					continue;

				// Only stage helis still loitering near the SR — leave forward/returned helis alone.
				if ((h.Location - srCell).LengthSquared > maxDistSq)
					continue;

				bot.QueueOrder(new Order("Move", h, Target.FromCell(world, stageCell.Value), false));
				stagedTo[h] = stageCell.Value;

				AIUtils.BotDebug("AI ({0}): heli forward-staging {1} {2} -> {3}",
					player.ClientIndex, h.Info.Name, h.Location, stageCell.Value);
			}
		}

		// Staging-cell math — mirrors MountedTransportBotModule.PreContactStagingCell exactly.
		CPos? ForwardStagingCell(CPos srCell)
		{
			if (poiMap == null)
				return null;

			var targets = poiMap.GetOffensiveTargets(player);
			if (targets.Count == 0)
				return null;

			var srPos = world.Map.CenterOfCell(srCell);
			var tgtPos = world.Map.CenterOfCell(targets[0].Location);
			var stagePos = srPos + (tgtPos - srPos) * Info.ForwardStagingPct / 100;
			var cell = world.Map.CellContaining(stagePos);
			return world.Map.Contains(cell) ? cell : (CPos?)null;
		}
```

`AIHelicopterRole` / `HelicopterAIRole.AttackHeavy|AttackLight` — same trait +
enum already used by `TryLaunchAttackMission` (`:270-275`), so no new usings.
`bot` field is set in `BotEnabled` (`:123`) and is non-null by the time BotTick
runs. `Target` / `Order` / `AIUtils` all already in scope (used throughout the file).

### 4.7 Optional (skip for v1): drop staged entries on disable

`TraitDisabled` (`:474-485`) already clears `managedHelicopters` /
`idleHelicopters` / `activeSquads`. Add `stagedTo.Clear();` there for symmetry.
Not load-bearing (the trait is being torn down) but tidy.

## 5. YAML changes — `mods/ww3mod/rules/ai/ai.yaml` (@experimental block ONLY)

Append to the `HelicopterSquadBotModule@experimental` block, after
`DangerFieldAvoidance: true` (`ai.yaml:804`). **Preserve the blank line** before
the next top-level entry (`ai.yaml:805` comment header) — MiniYaml merges
adjacent top-level entries if the blank line is removed (CLAUDE.md hard rule).

```yaml
		# Forward-staging (experimental-only, default-frozen in C#): push idle attack helis that are
		# still loitering near the SR forward to ~40% of the SR->top-POI vector, so they stage toward
		# the fight instead of hovering at the SR corner. Pct clamped short of contact per the recon's
		# AA-attrition caveat. OFF for every non-experimental profile (shared base block sets nothing).
		ForwardStaging: true
		ForwardStagingPct: 40
		ForwardStagingMaxDistanceCells: 8
```

The shared base `HelicopterSquadBotModule` block (`:777-785`) is **not touched**
→ it inherits the C# default `ForwardStaging = false`. No `@stable` twin exists
for this module (§3), so **all** non-experimental profiles get the OFF path
through that single shared block. The task's "@stable twin carries nothing"
condition is satisfied *by absence of a twin* — there is nothing to add there.

## 6. Byte-identity argument (per change, OFF state)

| Change | Why OFF is byte-identical |
|---|---|
| §4.1 new Info fields | Adding `readonly` fields with defaults changes no executed code. The shared/`@stable` profiles never set them → `ForwardStaging = false`. |
| §4.2 new state (`stagedTo`, `poiMap`) | Declaring an empty dict + a handle field. Never read/written when §4.4/§4.5 short-circuit (see below). |
| §4.3 PoiMap handle in `Initialize()` | `TraitOrDefault<PoiMap>()` is a pure lookup, **no RNG**. It runs for all profiles but only assigns a field that is read solely inside the OFF-guarded pass. |
| §4.4 `StageIdleHelicopters()` call | The method's **first statement** is `if (!Info.ForwardStaging) return;`. With the flag false it returns before any query, order, or RNG draw. Identical instruction stream to pre-patch for normal/rush/turtle/stable. |
| §4.5 `CleanUpHelicopters` prune loop | `stagedTo` is only ever populated inside the OFF-guarded pass, so with the flag off `stagedTo` is permanently empty and `stagedTo.Keys.ToList()` iterates nothing → no observable effect, no RNG. |
| §4.6 new methods | Never invoked on the OFF path (only `StageIdleHelicopters` calls them, and it early-returns). |
| §5 YAML | Only the `@experimental` instance opts in; the frozen instance is unmodified. |

**RNG discipline (ON path):** the staging pass issues only deterministic
`Order("Move", …)` calls and reads `PoiMap.GetOffensiveTargets` (the belief/POI
query seam, deterministic per `influence-stack.md:94-95`) + integer vector math.
It introduces **zero** `world.LocalRandom` / `SharedRandom` draws — CODE-READ of
§4.6 confirms no `.Next(...)` anywhere in the added code. So the ON path does not
shift the RNG stream relative to a run that reaches the same orders, satisfying
the influence-stack ON-path constraint. (The module's *existing* `LocalRandom`
draws at `:280/:351-352` are unchanged and unreached by staging.)

## 7. Interaction with low-ammo return + rearm (CODE-READ)

- A low-ammo heli issues `ReturnToBase` → with no hpad it resolves to `FlyIdle`
  near the front (recon §1c; `ReturnToBase.cs:106-108`). It re-enters
  `idleHelicopters` via `CleanUpHelicopters` (`:227-228`). Because it is **far
  from the SR**, the §4.6 `ForwardStagingMaxDistanceCells` gate excludes it — it
  is left where it is, not dragged forward. HYPOTHESIS (distance-dependent; the
  exact idle position is map/run-specific — MEASURE in the ON autotest).
- The `h.IsIdle` gate (§4.6) means staging never interrupts an in-flight
  `ReturnToBase`/`Resupply` (those helis are non-idle). This deliberately
  diverges from `MountedTransportBotModule`, which drops the `IsIdle` filter to
  un-stick AutoTarget-held carriers (`MountedTransportBotModule.cs:366-371`
  PITFALL) — staging needs no such un-stick because it re-tasks nothing but
  genuinely idle helis, and the corner-idle symptom **is** an idle heli.
- Under `@experimental`, `SkipRearmReadyCheck = true` (`ai.yaml:799`) so
  `IsReadyForMission` ignores ammo (`:449-461`). A near-SR low-ammo heli therefore
  passes readiness and would be staged forward — this is **consistent** with the
  experimental doctrine that already launches low-ammo helis into attack squads.
  The health gate (`ReEngageHealthPercent`, `:441-442`) still holds damaged helis
  back. This is the exact seam the recon's §Option-A risk flags (staging
  ammo-thin helis toward AA) — the `ForwardStagingPct` clamp + the ON benchmark
  (§8) are how that risk is bounded, and escalation is recon Option D.
- Orthogonal to the capturable-HPAD plan (recon §2): staging is a *pre-contact*
  wait-point choice; once a captured hpad becomes a real `Reservable`,
  `ReturnToBase` resupply lights up independently. No collision.

## 8. Verification plan (spec only — DO NOT run; harness occupied)

**Existing NUnit pins at risk:** none. The influence-stack math tests
(`ControlFieldMathTest`, `HeliDangerNavTest`, `GroundDangerNavTest`,
`PoiOffenseTest`, `influence-stack.md:118`) do not touch
`HelicopterSquadBotModule`; no existing NUnit test drives this IBotTick module
(it needs a world). Adding default-false `readonly` Info fields cannot change any
pinned math (CODE-READ).

**New NUnit pin — proves ON-path math determinism (necessary for the §6 ON-path
RNG-freedom claim):** extract the vector math into a pure static and pin it.
Add a nested `static class HeliStagingMath { public static CPos StageCell(WPos sr, WPos tgt, int pct, Map map) => map.CellContaining(sr + (tgt - sr) * pct / 100); }` (mirroring the `HeliDangerNav`/`PoiOffenseMath` split-for-NUnit
convention, `influence-stack.md:118`) and have `ForwardStagingCell` call it. New
`HelicopterStagingMathTest.cs` pins:
- `pct = 0` → SR cell; `pct = 100` → target cell; `pct = 50` → midpoint.
- same inputs → identical output across repeated calls (no hidden state / RNG).

This does **not** prove the OFF-gate byte-identity (that is a determinism/replay
property, not a pure-math property).

**Proving the OFF-gate (byte-identity):** structural argument §6 (early return)
+ a **frozen-AI replay byte-diff**: run the frozen/`@stable` benchmark profile
with and without this patch and assert byte-identical replay/state hashes. This
is the authoritative OFF proof; it is the standard the recon and influence-stack
doc use (`260729-heli-forward-staging.md:43`). SPEC ONLY — not run here.

**Single gated autotest for ON behaviour** (`test-heli-forward-staging`):
corner-SR map, one `@experimental` bot, no enemy contact near the SR, ≥1 called-in
attack heli. Assert, after ~AttackCooldown+ScanInterval ticks:
- flag OFF (RED expectation): an idle attack heli's cell stays ≈ the SR cell.
- flag ON (GREEN): the heli's distance-from-SR has **increased** and it is within
  a few cells of `ForwardStagingCell(srCell)` at `pct = 40`.
One test, two profiles. DO NOT run — the autotest harness is occupied by another
agent (per task constraints).

## 9. Effort + risk

**Effort:** small-to-medium. ~28 lines of new C# (4 Info fields, 2 state fields,
1 handle init, 1 call site, 1 prune loop, 2 methods) + ~1 pure-math extraction +
1 NUnit test + 3 YAML lines. The core staging-cell math is copied verbatim from a
shipped, working pattern. Est. ~2–3 h implementation + test authoring, excluding
benchmark time.

**Biggest risk (MEASURED-pending → currently HYPOTHESIS):** the recon's own
open caveat (`260729-heli-forward-staging.md:45`, seed `discovered.md:38`) —
staging ammo-carrying, target-less helis toward the enemy POI can fly them into
believed/real AA with nothing to shoot, hurting heli K:D and potentially starving
the 2-ready squad-formation threshold (staged helis are still counted, but if they
die forward the count never reaches 2). Mitigations already in this spec: (a)
`ForwardStagingPct` defaulted to 40 (short of contact); (b) `IsReadyForMission`
health gate holds damaged helis back; (c) `ForwardStagingMaxDistanceCells` limits
staging to SR-loiterers. Settle the fraction with the ON benchmark before
graduating the flag; if AA attrition shows, escalate to recon **Option D**
(danger-aware staging via the Stage-D air field) rather than abandoning staging —
`DangerFieldAvoidance` is already wired on this same module (`ai.yaml:804`).

**Secondary risk (low):** idempotence. `stagedTo` prevents per-scan Move
re-issue; a staged heli that reaches the cell hovers via `FlyIdle` and stays
squad-eligible. If the POI shifts materially the heli is NOT re-pointed in v1
(it re-points only after leaving+re-entering the idle pool near the SR) — an
acceptable v1 coarseness, matching MountedTransport's per-pass single-cell choice.
