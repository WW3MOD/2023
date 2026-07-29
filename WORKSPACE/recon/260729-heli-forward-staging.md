# Recon — helicopter forward staging (2026-07-29, main @ c11ce511)

Read-only design recon. Seed: the heli corner-idle analysis in `WORKSPACE/bugs/discovered.md:30-38` (2026-07-21 + the post-`SkipRearmReadyCheck` follow-up). All `file:line` refs code-verified as of main @ `c11ce511`. **No code changed. Everything below is design input to a user decision — nothing here is a mandate to implement.**

Grounding (WW3MOD hard model — do not import RA assumptions): there are **no factories**. Aircraft are called in via the Supply Route and enter from the map edge (`ProductionFromMapEdge`); HPAD/AFLD are rearm/repair *support* buildings, NOT production prerequisites, and the mod builds none (`game-model.md:12`). "Future plans include capturable HPADs on maps" is already documented there.

## The problem in one sentence

Called-in attack helis that are not currently in an active squad have exactly one place the code sends them — the SR's own edge cell — and once they arrive they hover in place forever, so a beachhead SR at a map corner reads on screen as "helis fly to the corner and stay."

## 1. Where helis go after production / attack / rearm today (the corner root cause)

Three destination paths exist; only one reaches a corner, and it is not a bug.

**(a) Fresh call-in → SR cell.** `ProductionFromMapEdge.Produce` builds a waypoint plan: `hasRallyPoint ? rp.Path : { new(self.Location, RallyOrderType.Move) }` (`ProductionFromMapEdge.cs:173-175`, gate `hasRallyPoint = rp != null && rp.Path.Count > 0` at `:89`). The SR `RallyPoint` in `structures.yaml:272-274` sets `LineWidth`/`Dashed` only — **no `Path:`**, so `rp.Path.Count == 0` → `hasRallyPoint = false`. The AI issues no rally order either. So every fresh heli is told to `MoveTo(self.Location)` — the SR building's own cell — and stops (`:230`). The SR is a spawn-edge beachhead, i.e. a corner/edge cell → **this is the sole corner source.**

**(b) Arrived / idle heli → hover in place.** Helis set **no `IdleBehavior`** in any mod YAML (`grep IdleBehavior mods/ww3mod/rules` → no matches), so they take the trait default `IdleBehaviorType.None` (`Aircraft.cs:27`). `OnBecomingIdle` therefore falls to the `else` branch (`Aircraft.cs:921-937`): not at land altitude → `QueueActivity(new FlyIdle(self))` — hover exactly where the last activity ended. So after finishing the production Move (a), the heli hovers *at the SR corner*. There is no drift to `(0,0)`; the corner cell is a legitimate coordinate.

**(c) Low-ammo / damaged / return → also hover in place (near front, not corner).** `HelicopterReturnState` and `SendLowAmmoUnitsHome` issue `Order("ReturnToBase")` (`HelicopterStates.cs:667,103,115`). `ReturnToBase.Tick` calls `ChooseResupplier`, which filters `ActorsHavingTrait<Reservable>()` to `rearmInfo.RearmActors` (`ReturnToBase.cs:45-50`); heli `Rearmable.RearmActors: hpad` (`aircraft-russia.yaml:224`) and **no hpad exists**, so both the reserved and unreserved lookups return null → `QueueChild(new FlyIdle(...)); return true` (`ReturnToBase.cs:106-108`). The heli idles wherever it stopped — typically near the front, **not** the corner.

**Why "corner" specifically:** the only heli that reaches a corner is one that never joins a squad (or sits between missions) and so still holds destination (a). Squad formation needs `AttackSquadSize = 2` (`HelicopterSquadBotModule.cs:25`) ready attack helis; until that threshold is met, or whenever `CleanUpHelicopters` returns a heli to the idle pool (`:211-229`), the heli has no order and hovers at the SR. **No degenerate coordinate to patch** — the fix is a *new* staging behaviour, not a bug fix.

## 2. What determines rearm target selection (HPAD vs map-edge return)

There is effectively **no live selection** in WW3MOD, because the candidate set is empty:

- `ReturnToBase.ChooseResupplier` (`ReturnToBase.cs:39-51`) is the only selector. It requires an actor that (i) has `Reservable`, (ii) is owned by the heli's player, (iii) is named in `RearmableInfo.RearmActors` (`= hpad`), (iv) optionally is unreserved — then `.ClosestToWithPathFrom(self)` picks nearest by path. With zero hpads, it returns null every time.
- The `CanHover` branch at `ReturnToBase.cs:84-99` (helis hover) would, *if* a resupplier existed but were reserved, fly to `WaitDistanceFromResupplyBase` of it and jitter with a random offset — i.e. "loiter near the pad." This is the closest thing the engine has to HPAD-vicinity staging, but it is dead code today (no pad to loiter near).
- Squad-side ammo gating is separate: `IsReadyForMission` benches any heli below full ammo unless `SkipRearmReadyCheck` (`HelicopterSquadBotModule.cs:449-461`), and `SquadHasAmmo` reports an all-attack-heli squad as *no ammo even at full* unless the same bypass (`HelicopterStates.cs:120-146`). Both are experimental-only, default-off. These decide *whether a squad forms/launches*, not *where an idle heli waits*.

Net: rearm-target selection is range-and-occupancy over `Reservable` hpads, but the set is empty, so every path degrades to "hover in place." This is the hook the capturable-HPAD plan will eventually populate (a captured hpad becomes a real `Reservable` resupplier and paths (b)/(c) light up automatically).

## 3. Forward-staging design options

Every option must answer: *where should an idle, not-in-squad attack heli wait, if not the SR corner?* At recruit/idle time there is no live enemy, so a forward point must be synthesised. Cheapest existing source: `PoiMap.GetOffensiveTargets(player)` + the SR cell, exactly the pattern `MountedTransportBotModule.PreContactStagingCell` already uses (`MountedTransportBotModule.cs:539-553`: `stagePos = srPos + (tgtPos − srPos) * Pct/100`).

### Option A — AI staging Move on the heli module (mirror `PreContactStagingCell`)

Add an `@experimental`-only, default-off field to `HelicopterSquadBotModuleInfo` (e.g. `ForwardStaging` + `ForwardStagingPct`). In `HelicopterSquadBotModule`, for each managed heli that is idle, not in a squad, and still within N cells of the SR, issue one `Order("Move", h, stagingCell)` where `stagingCell = PreContactStagingCell(srCell)` (a fraction of SR→top offensive POI). Squad recruitment (`TryLaunchAttackMission`) is unchanged — a staged heli is still in `idleHelicopters` and eligible.

- **Files/traits:** `HelicopterSquadBotModule.cs` (new field + a staging pass in `BotTick`/`CleanUpHelicopters`); reuse `PoiMap`/`InfluenceMap` handles already available to the mounted-transport module. No engine-core, no activity, no YAML on the units.
- **AI vs human:** AI-only (the module manages bot helis). Human helis are unaffected — they idle where the player left them, which is expected.
- **Byte-identity / benchmark:** default-off ⇒ `@stable`/`@poi`/frozen-AI benchmark byte-identical (same gating discipline as `SkipRearmReadyCheck`/`StandoffEngagement`/`DangerFieldAvoidance`). Only `HelicopterSquadBotModule@experimental` opts in.
- **Interaction with capturable-HPAD plan:** orthogonal and complementary — staging chooses a *pre-contact* wait point; once a captured hpad exists, `ReturnToBase` paths (§2) resupply independently. No conflict.
- **Risk:** the seed's own caveat (`discovered.md:38`) — staging *ammo-carrying, target-less* helis toward the enemy POI can fly them into believed/real AA with nothing to shoot, hurting heli K:D. Must clamp the fraction well short of contact and only stage helis that are actually mission-ready; needs a benchmark to settle the fraction. Medium effort, medium risk.

### Option B — SR `RallyPoint` Path (data-only, both AI and human)

Give the SR a default rally `Path` (or have the AI issue one rally order) so `hasRallyPoint` is true and `ProductionFromMapEdge` replays a forward waypoint instead of `self.Location` (`ProductionFromMapEdge.cs:173-177`). Fresh helis then fly to the rally cell, not the corner.

- **Files/traits:** `structures.yaml` SR `RallyPoint` (add `Path`), or a small AI rally-order emitter. Touches the production path shared by **all** unit types spawned from the SR, not just helis (ground units replay `Move`/`AttackMove` waypoints too, `:221-231`).
- **AI vs human:** applies to both (human players already can set a rally manually; this only changes the *default*).
- **Byte-identity / benchmark:** **exposed.** A non-empty default Path changes the spawn trajectory of every SR unit in every profile → frozen-AI benchmark diverges. Would need to be map-local or experimental-gated to preserve byte-identity, which a static YAML default cannot do cleanly.
- **Interaction with capturable-HPAD plan:** neutral.
- **Risk:** blunt — a fixed forward rally sends *ground* units forward too (may over-extend infantry), and a static cell ignores where the fight actually is. Low effort but high blast radius and benchmark exposure. Weakest fit.

### Option C — HPAD-vicinity staging (activate the dormant `CanHover` loiter)

Reserved for after capturable HPADs land: when a friendly hpad exists, idle helis loiter at `WaitDistanceFromResupplyBase` of the nearest one (the branch at `ReturnToBase.cs:84-99` already implements the geometry). Pre-capture there is no pad, so this is **inert today** and cannot be the near-term answer.

- **Files/traits:** none now; later a thin "stage at nearest friendly hpad vicinity if idle" nudge, largely already present in `ReturnToBase`.
- **AI vs human:** both (any heli with `ReturnToBase` benefits).
- **Byte-identity / benchmark:** no exposure while no hpad exists.
- **Interaction with capturable-HPAD plan:** this *is* the natural post-capture behaviour — worth noting as the eventual convergence point, but it does not solve the corner-idle that exists *before* any hpad is captured.
- **Risk:** none now (inert); deferred by dependency on the capturable-HPAD feature.

### Option D — Danger-aware staging via believed influence fields (@experimental only)

Layer the Stage-D air-danger field (`DangerFieldLayer`, already consumed by the heli FSM under `DangerFieldAvoidance`, `HelicopterStates.cs:171-206`) onto Option A: pick the staging cell as the safest air-danger cell along the SR→POI corridor rather than a blind fraction, so helis stage forward *without* drifting into believed AA.

- **Files/traits:** Option A's field + `DangerFieldLayer`/`HeliDangerNav` (e.g. `SafestAirCellOnRing`, `HelicopterStates.cs:605`). Rides on the same experimental gate as Stage-D.
- **AI vs human:** AI-only, and only under the fog-respecting experimental profile.
- **Byte-identity / benchmark:** default-off + belief-field discipline (fog-legal, zero-RNG, reads 0 outside believed envelopes — influence-stack invariants). Byte-identical when off.
- **Interaction with capturable-HPAD plan:** orthogonal; the safe-corridor logic also generalises to "stage near a captured hpad, air-safely."
- **Risk:** directly answers the seed's AA caveat (§Option A risk), but is the largest surface and depends on the whole Stage-D stack being enabled. Highest effort; lowest tactical risk *when* the stack is on. Overkill as a first step.

## Recommendation (input to a user call — the user decides)

**Prefer Option A (AI staging Move mirroring `PreContactStagingCell`), gated `@experimental` default-off, as the first step — with Option D held as the follow-on once a benchmark confirms staging helps at all.** Rationale: A is the smallest change that removes the corner-idle for AI helis, it reuses a proven, already-shipped pattern (`MountedTransportBotModule.PreContactStagingCell`), it touches no engine core / no unit YAML / no shared production trajectory, and its default-off gate keeps every frozen-AI benchmark byte-identical. It is also forward-compatible with the capturable-HPAD plan (staging is a *pre-contact* concern; HPAD resupply is a *post-capture* concern via `ReturnToBase`, and the two do not collide).

Option B is rejected for a first step: a static SR rally Path is benchmark-exposed and over-extends ground units. Option C is inert until capturable HPADs exist (but is the right *eventual* convergence for idle/return helis). Option D is the correct end-state for AA-safe staging but is too large and too dependent on the Stage-D stack to lead with.

The deciding evidence the user should weigh before enabling A is the seed's exact open risk (`discovered.md:38`): a benchmark must confirm forward-staging does **not** fly ammo-carrying, target-less helis into AA (measure heli survival / K:D flag-on vs off) and does **not** starve the 2-ready squad-formation threshold — before A graduates from experimental. If that benchmark shows AA attrition, escalate to Option D's danger-aware cell rather than abandoning staging.
