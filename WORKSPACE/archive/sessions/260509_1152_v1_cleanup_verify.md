# Active session — v1 cleanup + [T] verification

**Started:** 260509_1152
**Status:** in-progress
**Topic:** Aggressive WORKSPACE cleanup followed by Bucket B/C verification of [T] items in `RELEASE_V1.md`

## Parallel-agent awareness
Another agent has `test-evac-suite` running in OpenRA (PID 75720) — owns AUTOTEST harness + crew/vehicle/heli evac code. I am avoiding:
- `tools/test/run-test.sh`, `run-batch.sh`, `list-tests.sh`
- `engine/OpenRA.Mods.Common/Traits/VehicleCrew*`
- `engine/OpenRA.Mods.Common/Traits/Air/HeliEmergencyLanding.cs`, `HeliAutorotate.cs`
- `engine/OpenRA.Mods.Common/Activities/Air/Fly*.cs` if related to evac
- `mods/ww3mod/maps/test-evac-suite/`
- Running OpenRA / engine builds

## What I'm doing
1. Bucket B (trust-the-commit) — for each [T] item in `RELEASE_V1.md` with a commit hash, verify the commit exists + the code matches. Mark `[T:trusted]` if no contradicting later commit.
2. Bucket C (needs human eye) — write a single focused playtest brief listing what the user should look at in their next playtest.
3. Read-only investigations on Phase B items that don't conflict (heli→heli missile vanish, WGM mid-flight loss, bridge pathing, CounterBatteryRadar wiring).

## Intended files
- `WORKSPACE/RELEASE_V1.md` (status flips only — not user-content edits)
- `WORKSPACE/playtests/260509_1152_focused_brief.md` (new)
- `WORKSPACE/DISCOVERIES.md` (append if I find anything durable)

## Won't commit
- Code changes to traits the other agent might touch
- Anything that requires a build
