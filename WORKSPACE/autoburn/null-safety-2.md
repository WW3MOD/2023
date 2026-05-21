# auto/null-safety-2 — autoburn 260521

## Summary

Continuation of `auto/null-safety` (which shipped 1 fix in AutoTarget.ChooseTarget for FrozenActorLayer). This run hunted the same inconsistent-guarding methodology: same identifier dereferenced unguarded in one place and pre-checked in another known-equivalent place.

- **4 fixes** committed, each with a sibling-site cross-reference baked into the commit message.
- **2 reports** filed for sites with weaker evidence (debug-only paths, or non-WW3MOD-touched files).

Build verified after every commit (`dotnet build OpenRA.Mods.Common -c Release` — 0 warnings, 0 errors).

## Fixes

### 1. `SupportPowerDecision.GetAttractiveness` — `firedBy.FrozenActorLayer` (`abf1d9f4`)

- **Site:** `engine/OpenRA.Mods.Common/Traits/BotModules/BotModuleLogic/SupportPowerDecision.cs:85`
- **Pattern:** `firedBy.FrozenActorLayer.FrozenActorsInRegion(...)` — no null check.
- **Sibling guard:** `engine/OpenRA.Mods.Common/Traits/BotModules/SupportPowerBotModule.cs:156` —
  `player.FrozenActorLayer != null ? player.FrozenActorLayer.FrozenActorsInRegion(region) : Enumerable.Empty<FrozenActor>();`
- **Why nullable:** `Player.FrozenActorLayer = PlayerActor.TraitOrDefault<FrozenActorLayer>();` (Player.cs:222). WW3MOD's `player.yaml:199` lists `FrozenActorLayer:` on the `Player:` template only — `EditorPlayer` inherits `^BasePlayer` without it.
- **Reachability:** `SupportPowerBotModule.cs:199` calls `powerDecision.GetAttractiveness(pos, player)`, which is the unguarded overload at line 65 with the line-85 access.
- **Fix:** Mirror the sibling's ternary using `Enumerable.Empty<FrozenActor>()`. Added `using System.Linq;`.

### 2. `SpawnActorOnDeath.Killed` — `e.Attacker.Owner` (`5ff217a9`)

- **Site:** `engine/OpenRA.Mods.Common/Traits/SpawnActorOnDeath.cs:103`
- **Pattern:** `attackingPlayer = e.Attacker.Owner;` — no null check, then `if (attackingPlayer == null) return;` later at line 109 (proving the rest of the code already expects null).
- **Sibling guards** (all in WW3MOD-touched files):
  - `Traits/Player/HarvesterAttackNotifier.cs:64` — `if (e.Attacker != null && e.Attacker.Owner == self.Owner)`
  - `Traits/SmartMove.cs:45` — `if (e.Attacker != null && e.Attacker.IsInWorld && ...)`
  - `Traits/Player/BaseAttackNotifier.cs:71` — `if (e.Attacker == null) return;`
  - `Traits/GivesExperience.cs:59` — `if (... || e.Attacker == null || e.Attacker.Disposed)`
  - `Traits/GivesBounty.cs:79` — `if (e.Attacker == null || e.Attacker.Disposed || IsTraitDisabled)`
- **Why nullable:** `Health.InflictDamage(Actor self, Actor attacker, ...)` accepts a nullable attacker (Health.cs:158); the same file at line 208 guards `if (... && attacker != null && ...)`. Engine framework propagates that null into `AttackInfo.Attacker` (Health.cs:191-193).
- **Reachability:** `Cargo.cs:557, 564` calls `passenger.Kill(e.Attacker)` propagating whatever attacker came in — including null. Many WW3MOD units have `SpawnActorOnDeath` (corpses/husks) and ride as Cargo passengers.
- **Fix:** `attackingPlayer = e.Attacker?.Owner;`. Line 109 already handles null correctly (no spawn).

### 3. `BuildingRepairBotModule.RespondToAttack` — `e.Attacker.Owner` (`f2a0aa10`)

- **Site:** `engine/OpenRA.Mods.Common/Traits/BotModules/BuildingRepairBotModule.cs:33`
- **Pattern:** `self.Owner.RelationshipWith(e.Attacker.Owner)` — no null check.
- **Sibling guards** — all 3 other `IBotRespondToAttack` implementers in the same dir:
  - `BaseBuilderBotModule.cs:173` — `if (e.Attacker == null || e.Attacker.Disposed) return;`
  - `SquadManagerBotModule.cs:428` — `if (!IsPreferredEnemyUnit(e.Attacker)) return;` (with null check inside IsPreferredEnemyUnit at line 145)
  - `BotModuleLogic/MinelayerBotModule.cs:355` — same `IsPreferredEnemyUnit` pattern (null check at line 310)
- **Why reachable:** `ModularBot.Damaged` (ModularBot.cs:124-126) calls every enabled `IBotRespondToAttack` with the same `AttackInfo e`, no pre-filter. So if any `e.Attacker == null` event reaches a bot-owned building, this is the only of the four modules to crash.
- **Fix:** Added the `BaseBuilderBotModule`-style early return at the top of RespondToAttack.

### 4. `PlayerStatistics.Killed` — `e.Attacker.Owner` (`d5014833`)

- **Site:** `engine/OpenRA.Mods.Common/Traits/Player/PlayerStatistics.cs:234`
- **Pattern:** `var attackerStats = e.Attacker.Owner.PlayerActor.Trait<PlayerStatistics>();` after line 231 `if (e.Attacker == self) return;`. The self-equality check does NOT short-circuit when attacker is null (`null == self` is false for a non-null `self`), so execution falls through and dereferences null at line 234.
- **Sibling guards** in the same dir + same callback type (INotifyKilled):
  - `BaseAttackNotifier.cs:71` — `if (e.Attacker == null) return;`
  - `GivesExperience.cs:59` — `if (exp == 0 || e.Attacker == null || e.Attacker.Disposed)`
  - `GivesBounty.cs:79` — `if (e.Attacker == null || e.Attacker.Disposed || IsTraitDisabled)`
- **Why reachable:** Same Cargo.cs / engine path as fix #2 — INotifyKilled callbacks fire with attacker = null via `passenger.Kill(e.Attacker)`. PlayerStatistics is on every player and every unit ticks through this path on death.
- **Fix:** `if (e.Attacker == null || e.Attacker == self) return;` — preserves the original self-kill skip while adding null safety.

## Reported (insufficient evidence to fix this run)

### R1. `CombatDebugOverlay.Damaged` — `e.Attacker.OwnerColor()`

- **Site:** `engine/OpenRA.Mods.Common/Traits/CombatDebugOverlay.cs:135`
- **Pattern:** `new FloatingText(self.CenterPosition, e.Attacker.OwnerColor(), damageText, 30)` — unguarded.
- **Cross-site:** `Exts.cs:27 OwnerColor(this Actor actor)` accesses `actor.EffectiveOwner` — NRE on null.
- **Why reported, not fixed:** path is gated by `debugVis.CombatGeometry` being enabled (line 126). Practical user exposure is dev-mode only. The other 4 fixes are more impactful; staying within the 3-6 cap.

### R2. `TargetExtensions.WithFrozenReplacement` — `viewer.FrozenActorLayer.FromID`

- **Site:** `engine/OpenRA.Mods.Common/TargetExtensions.cs:65`
- **Pattern:** `var frozen = viewer.FrozenActorLayer.FromID(t.Actor.ActorID);` — unguarded.
- **Cross-site:** `SupportPowerBotModule.cs:156` and now `SupportPowerDecision.cs:85` both guard the same nullable field.
- **Why reported, not fixed:** TargetExtensions.cs is NOT in the WW3MOD-touched set (only base OpenRA + `c5bb5ece` upstream-merge commit). Per the prompt's scoping ("WW3MOD-touched files"), this is an upstream-OpenRA bug. Worth surfacing — if a WW3MOD `viewer` (e.g., a non-Player template that ever gets passed here) lacks FrozenActorLayer, this would NRE.

## Verification

```
cd engine && dotnet build OpenRA.Mods.Common/OpenRA.Mods.Common.csproj -c Release --nologo -clp:ErrorsOnly
```

Run after each fix. Final state: **Build succeeded. 0 Warning(s) 0 Error(s)**.

No unit-test changes — these are surface-area guards on inconsistent patterns; existing tests still pass.

## Files touched

```
engine/OpenRA.Mods.Common/Traits/BotModules/BotModuleLogic/SupportPowerDecision.cs
engine/OpenRA.Mods.Common/Traits/SpawnActorOnDeath.cs
engine/OpenRA.Mods.Common/Traits/BotModules/BuildingRepairBotModule.cs
engine/OpenRA.Mods.Common/Traits/Player/PlayerStatistics.cs
WORKSPACE/autoburn/null-safety-2.md
```

## Methodology notes (for future autoburn runs)

The strongest signal in this codebase was the **`e.Attacker` family** — five distinct call sites in WW3MOD-touched traits explicitly guard, three more were not. The framework (Health.cs:208) proves the contract is nullable. Future scans should grep `e\.Attacker` across `Traits/` and check for unguarded `.Owner` / `.Trait` / `.Info` access.

The **`Player.FrozenActorLayer` family** still has at least one upstream site (TargetExtensions.cs:65) that the prior `auto/null-safety` run didn't reach. Inside WW3MOD-touched code, AutoTarget (fixed in #1 of the prior run) and SupportPowerDecision (fixed in #1 of this run) appear to be the main two; the SupportPowerBotModule sibling guard remains as the canonical pattern.
