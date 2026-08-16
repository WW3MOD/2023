# 260816 desync — root cause

Repo state: `main @ d5b52893`, clean except the untracked client sync report. Diagnosis only; nothing changed.

Sources: `WORKSPACE/audit/logs-260816-snapshot/Logs/syncreport-2026-08-16T162525Z-1.log` (host, FreadyFish, .NET 8.0.27) and `…-FRIEND-…-7.log` (client, Commanderbambi, .NET 10.0.10).

---

## 1. The diff is smaller than the brief states, and the missing field is the key

A full diff of the two reports yields **one actor and nothing else** (plus the player header and the frame ring). The brief's table omits one line that changes the reading:

| | Host | Client |
|---|---|---|
| `Mobile.FromCell` | 32,48 | **32,48** |
| `Mobile.ToCell` | 33,47 | **32,48** |
| `Mobile.CenterPosition` | 32993,49396,0 | 32981,49408,0 |
| `Mobile.Facing` | 896 | 716 |

`FromCell == ToCell` on the client means `Mobile.IsMovingBetweenCells` is **false** there (`Mobile.cs:264`). The client's soldier is **standing still**; the host's has **begun a step** toward 33,47 and is 12 world units into it (`+12,-12` = north-east, the direction of 33,47).

The facing gap is not gradual turning. `Move.cs:586` **snaps** `mobile.Facing = ToFacing` the instant a move part completes. Host 896 is a snapped value; client 716 is the un-snapped interpolated one.

**The two machines are exactly one tick apart in movement progress.** Nothing more.

---

## 2. Cause vs effect — resolved, and not in the direction the brief's hypothesis proposed

### `Detectable.CurrentVisibility` is provably terminal

`Detectable` grants `visibility-N` at `Detectable.cs:162`. Every consumer of that condition in the mod is render-only:

- `WithRangeCircle@Detectable1..12` — `mods/ww3mod/rules/ingame/infantry.yaml:744-838`, all `Visible: WhenSelected`
- `WithDecoration@Visibility_1..12` (`^VisibilityPips`) — `infantry.yaml:842-925`, all `RequiresSelection: true`

`grep -rn "visibility-" mods/ww3mod/` returns nothing else but sequence names. `CurrentVisibility` has **zero simulation consumers**. It cannot be a cause of anything, in this bug or any other.

Its inputs are also clean. `Detectable.cs:80-87` reads only `IDetectableAddativeModifier`; the sole implementer is `DetectableAddativeModifier.cs:29-32`, which returns `VisionModifier` iff the trait is condition-enabled. `Util.ApplyAddativeModifiers` (`Util.cs:248-256`) sums in `decimal` — software arithmetic, deterministic. **No fog, shroud, `LocalPlayer` or "can this player see it" term reaches `CurrentVisibility`.** Answer to brief item 1: **no client-local input feeds this `[Sync]` field.**

### `InfantryStates.IsProne` is also downstream

`ProneCondition` for `^AmphibiousSoldier` is `!inwater && (deployed || suppressed > 30 || !moving || critical-damage)` (`infantry.yaml:313`; `^CamoSoldier` variant at `:294`). The live term is **`!moving`**. `moving` is granted by `GrantConditionOnMovement` (`infantry.yaml:138-141`), driven by `Mobile.CurrentMovementTypes`.

So: **client stops moving → `moving` revoked → `!moving` true → `IsProne` → `prone` granted (`InfantryStates.cs:212`) → `DetectableAddativeModifier@Prone` enables (`infantry.yaml:717-719`) → `CurrentVisibility` shifts.**

The only back-edge from prone into simulation is `ISpeedModifier.GetSpeedModifier` (`InfantryStates.cs:189-190`, 60%). That changes how fast a step *completes*; it cannot change whether a step *starts*, which is decided in `Move.PopPath`. The reverse chain does not close.

**Verdict: `Mobile` is the root. `InfantryStates` and `Detectable` are both effects, two and three links downstream respectively.**

### Corroboration from trait counts

The dump omits any trait whose sync hash is `0` (`SyncReport.cs:80-81`) — so an *enabled* `ConditionalTrait` (`IsTraitDisabled: False` → hash 0) is invisible, and a *disabled* one (hash 1) is listed. Host lists 10 `DetectableAddativeModifier`, client 9: the client has exactly one more modifier **enabled**. Consistent with `@Prone` switching on. `MapLayers.VisionLayers = 11` (`MapLayers.cs:75`), so the clamp at `Detectable.cs:84-85` is 10 — the client's 5 is a true sum, not a clamp artefact.

---

## 3. The two "absent" trait blocks are not structural

Brief item 3 asks what makes a trait appear in one dump and not the other. `SyncReport.cs:80-81`:

```csharp
var hash = syncHash.Hash();
if (hash != 0)
```

**Traits whose sync hash happens to be zero are dropped from the report entirely.** Both machines instantiate both traits; presence in the dump is a *value* signal, not a structural one:

- `InfantryStates` absent on host → all of `IsProne/IsPanicking/IsActive/IsTraitPaused/IsTraitDisabled` false and `QuantizedFacings` 0 → hash 0.
- `DetectableAddativeModifier` count differs → one instance flipped from disabled (hash 1) to enabled (hash 0).

No rules divergence, and nothing to investigate here. It is worth noting as an instrumentation trap: **the all-defaults state of a trait is indistinguishable from the trait not existing.**

---

## 4. Root cause: load-bearing simulation state that `[Sync]` does not cover

Every `[Sync]`-covered value in the entire world at frame 1264 is identical except this one actor's `Mobile`, and the RNG stream matches exactly (`SharedRandom: 1077851139 (#18240)`). Therefore the first divergence **cannot** live in synced state — it lives in state the detector cannot see. In the movement path that state is:

| Field | Location | Synced? |
|---|---|---|
| `Mobile.CurrentSpeed` | `Mobile.cs:299` | **No** |
| `MovePart.progress` | `Move.cs:473` | **No** (activities are never dumped) |
| `MovePart.Distance` | `Move.cs:470` | **No** |
| `Move.lastMovePartCompletedTick` | `Move.cs:39` | **No** |

`Move.cs:571` accumulates `progress += mobile.CurrentSpeed`, and `Move.cs:583` fires `if (progress >= Distance)` → `SetCenterPosition(To)`, `Facing = ToFacing`, complete the part, pop the next cell. **A one-unit difference in accumulated `progress` moves that threshold crossing by one tick — which is precisely and exactly the observed state.**

And `CurrentSpeed` is fed by the one floating-point expression in the movement hot path:

```csharp
// Move.cs:566-567
var currentAcceleration = ((float)mobile.CurrentSpeed / (float)movementSpeedForCell * (float)mobile.AccelerationSteps.Length) - 1f;
var flooredValue = (int)Math.Ceiling((double)currentAcceleration);
mobile.CurrentSpeed += mobile.AccelerationSteps[flooredValue >= 0 ? flooredValue : 0];
```

This path is live for infantry: `MobileInfo.Acceleration = { 3, 2, 1 }` (`Mobile.cs:44`) with no YAML override anywhere in `mods/ww3mod/rules/`.

**Named root cause:** the movement accelerator (`Mobile.CurrentSpeed` + `MovePart.progress`) is simulation-critical, computed through IEEE floating point, and **invisible to the desync detector**. It can drift for an unbounded number of ticks with zero detection, and surfaces only when the drift finally flips a cell transition — at which point the report names `Mobile`, `InfantryStates` and `Detectable`, none of which is the bug.

This also explains the bug's history: every prior fix targeted `[Sync]`-visible gameplay logic, and the accelerator was never in the search space.

### Determinism trace for `CurrentSpeed` (required before recommending `[Sync]`)

Complete set of writes, engine-wide:

- `Move.cs:136, 206, 555, 559, 566-568, 600, 602` — all inside the `Move` activity (simulation)
- `Mobile.cs:919` — reset on husk transfer; `Mobile.cs:914` `HuskSpeedInit`

Reads: the above, plus `Armament.cs:521`. **No writer or reader touches `World.LocalPlayer`, `RenderPlayer`, `Viewport`, `Game.Settings`, fog or shroud.** The field is pure simulation state that was simply never annotated. `[Sync]` on it is safe.

### A second consumer that raises the stakes

`Armament.cs:521-524` reads the same unsynced field, through more float math, into RNG:

```csharp
var maxInaccuracy = (int)((float)bullet.Inaccuracy.Length * Info.MovementInaccuracy / 100 * targetMobile.CurrentSpeed / targetMobile.Info.Speed * distanceToTarget / args.Weapon.Range.Length);
var wVec = new WVec(0, self.World.SharedRandom.Next(-maxInaccuracy, maxInaccuracy), 0).Rotate(...);
```

`MersenneTwister.Next(low, high)` (`MersenneTwister.cs:51-61`) consumes **exactly one draw when `diff > 1`, and zero when `diff <= 1`**. So two machines with different *nonzero* `maxInaccuracy` produce **different aim offsets with an identical draw count** — which is exactly the reported signature (`#18240` matching on both while world state diverges). Any hypothesis requiring a lost or extra RNG draw is excluded by the evidence; this one is not. This is not the immediate cause of *this* actor's divergence (its `Health` is identical), but it is a live second path by which an unsynced `CurrentSpeed` becomes a full desync.

---

## 5. Verdict on the .NET 8 / .NET 10 mismatch

**Not supported as the cause, and not exonerated as a risk. Do not let it become the story.**

Against it:

- Scalar `float`/`double` `+ - * /` and `Math.Ceiling` are IEEE-754 exactly specified. RyuJIT on x64 emits SSE scalar ops and `roundsd`; it does not reassociate float expressions or auto-contract into FMA. Given identical inputs, `Move.cs:566-567` produces bit-identical output on both runtimes.
- Float→int conversion became saturating on all platforms in **.NET 7**, so both 8 and 10 agree on the NaN/∞ edge (`0/0` when `movementSpeedForCell == 0` → NaN → `0`).
- Both machines are the same OS and architecture (`Windows NT 10.0.26200.0`, per the report headers).
- `Dictionary`/`HashSet` ordering is a red herring in a *sharper* way than the brief assumes: `string.GetHashCode()` has been randomised **per process** since .NET Core, so any simulation depending on string-keyed hash order would desync between two processes on the *same* runtime, constantly. This bug is intermittent, so that is not the mechanism.
- The pathfinder ties break on cost alone (`IPathGraph.cs:80-86`), but insertion order comes from a fixed direction array, so it is deterministic in practice.

For it: `Move.cs:566-567` sits on a genuine knife edge independent of runtime. When `CurrentSpeed / movementSpeedForCell` is exactly ⅓ or ⅔, float rounding decides the acceleration step — e.g. `18f/54f = 0.33333334` (rounds **up**), `*3 = 1.0000001`, `-1 = 1e-7`, `Ceiling → 1`, selecting step `2` where exact arithmetic selects step `3`. Any future JIT change, any inlining difference, any constant-folding change flips that. It should not be floating point at all, on principle.

**Assessment: the runtime mismatch is a hygiene problem the users should fix anyway, but naming it as the root cause would be the fifth dissolved confident cause. The float expression is the real defect; the runtime gap merely makes it harder to reason about.**

---

## 6. Relationship to `c440906e`

`c440906e` added sync coverage to `Detectable` and the boards recorded *"the `Detectable` sync change is NOT a fix."* **That conclusion was correct and this finding confirms it rather than superseding it.** With two live reports we can now say something stronger than "it didn't fix it": `CurrentVisibility` has no simulation consumer at all (§2), so `Detectable` was never capable of being the cause. The trait is exonerated permanently, not just for this scenario. The two-sided evidence changes the *strength* of the old conclusion, not its direction.

---

## 7. Fix shape (manager decides; nothing implemented)

**A — Close the observability hole (do this first, it is diagnostic, not speculative).** Add `[Sync]` to `Mobile.CurrentSpeed` (`Mobile.cs:299`). The determinism trace in §4 shows no client-local writer. This does not fix anything; it makes the detector fire at the *first* tick the accelerator diverges instead of ~N ticks later at a cell flip, which is what has been missing from every prior investigation. Consider also surfacing `MovePart.progress` — harder, since activities are not dumped at all (`SyncReport.cs:76-94` covers traits and effects only).

**B — Remove floating point from the movement hot path.** Replace `Move.cs:566-567` with integer ceiling division, guarding `movementSpeedForCell == 0`:

```
flooredValue = ceil(CurrentSpeed * Length / movementSpeedForCell) - 1
             = (CurrentSpeed * Length + movementSpeedForCell - 1) / movementSpeedForCell - 1
```

This changes behaviour at the exact-third boundaries, so it is a balance-visible change and needs a note in the commit message. Per CLAUDE.md this flows to `@stable` and that is fine, but it must be stated so the next benchmark baseline is retaken knowingly.

**C — Same treatment for `Armament.cs:521`.** Float math feeding an RNG range. Integer arithmetic, same reasoning.

**D — Ask the two users to match runtimes.** Cheap, removes a variable, but per §5 do not expect it to fix anything on its own.

### NEEDS A RUN

Simulation authority is the manager's. These are the runs that would settle what is left.

1. **Kills or confirms the .NET hypothesis with no second human — do this one first.** Replay a recorded desyncing match twice on one machine, once per runtime, with diagnostic dumps on, and diff:
   ```
   ./launch-game.sh Game.Mod=ww3mod Game.Replay=<replay> Test.ForceSyncReports=true
   ```
   run once under .NET 8 and once with `DOTNET_ROLL_FORWARD=LatestMajor` under .NET 10, then diff the `syncdiag-*` files. **Identical dumps ⇒ the runtime gap is not the mechanism** and B/C become pure hygiene. Differing dumps ⇒ the runtime gap is live and B becomes urgent. Either result is decisive and costs one replay.
2. **After change A**, a fresh two-machine game. The expected outcome is that the desync now fires *earlier* and names `Mobile.CurrentSpeed` directly. If it still first fires on `ToCell`, `CurrentSpeed` is not the drifting quantity and the search moves to `progress`/`Distance`.
3. **After any change**, build and unit tests:
   ```
   dotnet test engine/OpenRA.Test/OpenRA.Test.csproj --configuration Release
   ```

### What I could not verify

I have one frame. `SyncReport` writes only the report whose `Frame` matches the desync frame (`SyncReport.cs:150`), so the 32-frame ring in both files is unreadable — I cannot see the ticks *before* 1264 and therefore cannot show `CurrentSpeed` actually diverging. The claim that it did is an inference from "all synced state matches, so the divergence must be in unsynced state, and `CurrentSpeed`/`progress` are the unsynced state on the only code path that produces exactly this outcome." That inference is strong but it is not a measurement. Run 1 above is the cheapest thing that turns it into one.
