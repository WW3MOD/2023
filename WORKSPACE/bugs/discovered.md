# Discovered Bugs

> Bugs found while working on something else. Captured here so they don't get lost.
> Format: `- [DATE] [severity] description (found while working on: X)`

## 2026-03-24: AirstrikePower crash — case-sensitive actor lookup (FIXED)
`Rules.Actors` keys are lowercase but `AirstrikePower.SendAirstrike` looked up `info.UnitType` without lowercasing. Crashed when Russia used Su-25 airstrike (`FROG.Airstrike` → `KeyNotFoundException`). Fixed: added `ToLowerInvariant()` to C# lookup + lowercased YAML UnitType values.

## 2026-07-21: [high] AI attack helicopters permanently benched with no HPAD (found while: playtest bug triage)
`HelicopterSquadBotModule.IsReadyForMission` (`engine/OpenRA.Mods.Common/Traits/BotModules/HelicopterSquadBotModule.cs:399-408`) requires every AmmoPool `HasFullAmmo`; attack helis only refill while `unit.docked` at an `hpad` (`mods/ww3mod/rules/ingame/aircraft-russia.yaml:178` etc.) and the mod builds no HPAD, so after the first shot no squad ever forms and the heli idles at its edge/rally cell forever. Distinct from the production-side `SkipRearmBuildingCheck`, which does not cover the squad path. Fix options in `WORKSPACE/plans/260721_playtest_bugs_triage.md` (Bug 2).

## 2026-07-21: [med] Out-of-ammo evac units recruited onto offensive axes (found while: playtest bug triage)
`PoiOffensiveBotModule.IsEligibleCombatUnit` (`PoiOffensiveBotModule.cs:403-412`) has no ammo filter, so an evacuating (RotateToEdge) zero-ammo unit re-enters the free pool and its AttackMove cancels the evac. `LayeredDefenceBotModule` already guards this (`SkipOutOfAmmoUnits`/`IsOutOfAmmo`, `:102,:273,:465-471`); PoiOffensive needs the same. Fix in `WORKSPACE/plans/260721_playtest_bugs_triage.md` (Bug 1).

## 2026-03-24: HeliAutorotate/HeliCrashLand build errors
Untracked WIP files `engine/OpenRA.Mods.Common/Activities/Air/HeliAutorotate.cs` and `HeliCrashLand.cs` fail to compile: `IActivity` type not found. These files are interdependent with `HeliEmergencyLanding.cs` trait. Pre-existing issue, not caused by stance rework.
