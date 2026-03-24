# Discovered Bugs

> Bugs found while working on something else. Captured here so they don't get lost.
> Format: `- [DATE] [severity] description (found while working on: X)`

## 2026-03-24: AirstrikePower crash — case-sensitive actor lookup (FIXED)
`Rules.Actors` keys are lowercase but `AirstrikePower.SendAirstrike` looked up `info.UnitType` without lowercasing. Crashed when Russia used Su-25 airstrike (`FROG.Airstrike` → `KeyNotFoundException`). Fixed: added `ToLowerInvariant()` to C# lookup + lowercased YAML UnitType values.

## 2026-03-24: HeliAutorotate/HeliCrashLand build errors
Untracked WIP files `engine/OpenRA.Mods.Common/Activities/Air/HeliAutorotate.cs` and `HeliCrashLand.cs` fail to compile: `IActivity` type not found. These files are interdependent with `HeliEmergencyLanding.cs` trait. Pre-existing issue, not caused by stance rework.
