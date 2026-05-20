# auto/bugs-survey — autoburn 260520

## Status

PARTIAL SALVAGE — original worker was killed when the Maestro daemon was terminated under CPU pressure. The worker built a complete autotest scenario but never ran it, never attempted a fix, never committed. The conductor recovered the uncommitted files and is committing them on the branch as scaffolding.

## What the worker chose

From `WORKSPACE/RELEASE_V1.md` Phase B "Drone fixes": **"Drone autotarget of other drones broken"**.

## What's been built (committed by conductor)

`tools/autotest/scenarios/test-dr-jams-drone/`:
- `description.txt`, `map.bin`, `map.png`, `map.yaml` — 66×34 TEMPERAT map.
- `rules.yaml` — minimal: ConquestVictoryConditions removed, `AutoTarget.ScanRadius: 30`, `InitialStance: FireAtWill`, MusicPlaylist scripts wired.
- `test-dr-jams-drone.lua` (2.9 KB) — full test setup:
  - USA DR ("Operator") at (12,17), `Stance: FireAtWill`.
  - Russian DR ("Enemy") at (35,17), force-ordered to deploy `DroneTargeter` at (22,17).
  - Russian quadcopterdrone flies through ~10 cells in front of USA DR (well inside the DroneJammer's `20c0` range).
  - `DroneJammer`: 3 damage/shot vs `Drone`-typed targets, `BurstWait: 1` → even a single autotarget volley drops drone HP.
- **Pass:** drone airborne AND `HP < starting HP` within 15s.
- **Fail (matches the known bug):** drone airborne, HP unchanged at deadline.

## What's NOT done

- The autotest has not been run. The PASS/FAIL behaviour is unverified.
- No fix attempted. The bug remains as described in RELEASE_V1.

## Suggested next steps (for user or fresh worker)

1. `./tools/autotest/run-test.sh test-dr-jams-drone` — expect FAIL, confirms the scaffold works AND reproduces the bug.
2. Investigate `engine/OpenRA.Mods.Common/Traits/AutoTarget.cs` filtering: how it scores or rejects "Drone"-typed targets vs the DroneJammer's `ValidTargets` list. The DR's secondary armament is the DroneJammer; autotarget needs to see drones and pick that armament.
3. Once fixed, the test should flip to PASS — commit fix + the runtime confirmation together.

## Caveats

- Conductor-committed scaffolding has NOT been launched. There's a chance the scenario won't even load (wrong actor IDs, faction mismatch). User should run it once before treating FAIL as the real bug verdict.
- The test is otherwise the highest-value artifact of this branch — a fresh worker (or the user) can pick up exactly where the killed worker left off.

## Files touched

```
tools/autotest/scenarios/test-dr-jams-drone/description.txt
tools/autotest/scenarios/test-dr-jams-drone/map.bin
tools/autotest/scenarios/test-dr-jams-drone/map.png
tools/autotest/scenarios/test-dr-jams-drone/map.yaml
tools/autotest/scenarios/test-dr-jams-drone/rules.yaml
tools/autotest/scenarios/test-dr-jams-drone/test-dr-jams-drone.lua
```
