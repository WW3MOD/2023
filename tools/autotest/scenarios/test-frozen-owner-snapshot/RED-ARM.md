# RED arm for test-frozen-owner-snapshot

This scenario passed on 2026-09-01 (run `260901_082819_p3797`, `status: pass`) and its pass is
structurally non-vacuous — see below. What had **never** been shown is that it can go RED for the
reason it claims to guard. A guard that has only ever been observed passing is not yet a guard.

This file is that demonstration: a one-hunk sabotage of the exact mechanism under test, the exact
failure text it must produce, and the reasoning for why it lands on the verdict arm rather than on
one of the setup controls.

## Why the green is not vacuous

`return true` (the only pass) is at `test-frozen-owner-snapshot.lua:213`, inside the phase-4 block.
Phase 4 is reachable only from `Phase = 4` at `:180`, which is inside phase 3; phase 3 is reachable
only from `Phase = 3` at `:139`, which requires `state == "frozen"`; and phase 2 is reachable only
from `Phase = 2` at `:121`, which requires `state == "live"` **and** `SeenOwner == "Russia"`.

So a pass entails, in order: USA really observed Box as Russian, the Scout really died, the ghost
really went frozen, `Test.FrozenClickCursor` really returned a non-empty cursor (`:170` fails
otherwise), the capture really took (`:190`), the ghost was still frozen afterwards (`:195`), and the
snapshot still read `Russia` (`:200`). **Phases 3 and 4 did execute.** The scenario header and
`description.txt` claimed for a day that they never had; that was written before the re-run and is
corrected in place.

## The sabotage

`FrozenUnderFog.OnOwnerChanged` (`engine/OpenRA.Mods.Common/Traits/Modifiers/FrozenUnderFog.cs:217`)
deliberately refreshes only `frozenStates[oldOwnerIndex]` — the ghost of the player who LOST the
actor. Widening it to every player is the change that reads like a consistency cleanup and is
actually a fog leak. That is what this scenario exists to catch, and it is named as the prime
suspect in the verdict failure text at `:206-207`.

```diff
--- a/engine/OpenRA.Mods.Common/Traits/Modifiers/FrozenUnderFog.cs
+++ b/engine/OpenRA.Mods.Common/Traits/Modifiers/FrozenUnderFog.cs
@@ -216,11 +216,14 @@
 		void INotifyOwnerChanged.OnOwnerChanged(Actor self, Player oldOwner, Player newOwner)
 		{
-			// Force a state update for the old owner so the tooltip etc doesn't show them as the owner
-			var oldOwnerIndex = self.World.Players.IndexOf(oldOwner);
-			var frozen = frozenStates[oldOwnerIndex].FrozenActor;
-			UpdateFrozenActor(frozen, oldOwnerIndex);
-			frozen.RefreshHidden();
+			// RED ARM -- DO NOT COMMIT. Widened to every player: the "consistency fix"
+			// test-frozen-owner-snapshot exists to catch. Revert with git checkout --.
+			for (var playerIndex = 0; playerIndex < frozenStates.Count; playerIndex++)
+			{
+				var frozen = frozenStates[playerIndex].FrozenActor;
+				UpdateFrozenActor(frozen, playerIndex);
+				frozen.RefreshHidden();
+			}
 		}
```

Compiles clean (0 warnings, 0 errors) as of `wt/frozen-actor`.

## Running it

```bash
# RED
git apply - <<'EOF'   # (or hand-edit the hunk above)
...
EOF
make all
./tools/autotest/run-test.sh test-frozen-owner-snapshot     # expect status: fail
git checkout -- engine/OpenRA.Mods.Common/Traits/Modifiers/FrozenUnderFog.cs
make all

# GREEN
./tools/autotest/run-test.sh test-frozen-owner-snapshot     # expect status: pass
```

**Read `result.json` in the run directory, not the exit code and never a pipe through `tail`.**

## The RED must produce THIS text, not merely "fail"

Expected `notes`, from `:201-210`:

> `fail: USA's frozen ghost of Box now records owner 'Neutral', but USA last observed it as 'Russia'
> and has not seen it since. ...`

Any other failure string means the sabotage did not land where intended and the run proves nothing
about the guard. In particular a `fail: SETUP` of any kind is **not** the RED — it means the
scenario died before reaching its verdict, which is what happened on the very first run
(`260901_080822`, a mis-sited Observer) and is a different event entirely.

## Why the sabotage lands on the verdict and not a setup control

Phase 4 re-asserts two setup conditions before reading the verdict, and the widened loop perturbs
neither:

- `:190` `liveOwner ~= "Neutral"` — reads `Box.Owner`, the live actor, untouched by the loop.
- `:195` `state ~= "frozen"` — `Test.FrozenActorState` (`TestGlobal.cs:777-780`) branches on
  `fa.Visible` and `fa.Shrouded`. `RefreshState()` (`FrozenActorLayer.cs:121-140`) writes `Owner`,
  `TargetTypes`, `targetablePositions`, `HP`, `DamageState`, `TooltipInfo`, `TooltipOwner` — and
  none of `Visible`, `Shrouded`. `RefreshHidden()` (`:143-154`) writes only `Hidden`.

So the only field the sabotage moves that the scenario reads is `Owner`, which is exactly the
verdict at `:200`.

## What a legitimate future RED looks like

If `FrozenUnderFogUpdatedByGps` ever becomes live — i.e. a `GpsPower` is added to `mods/`, which as
of 2026-09-02 exists nowhere (`GpsWatcher.Granted` is `actors.Count > 0 && Launched`, and `GpsAdd`'s
only callers are `GpsPower.cs:67,102,118`) — this scenario goes red **for a legitimate reason**. The
fix is then to assert the GPS state, not to widen the expectation. `:14-19` says so already.
