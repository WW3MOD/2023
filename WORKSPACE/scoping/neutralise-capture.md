# Scoping: a soldier should NEUTRALISE an enemy-held money structure, not capture it

**Queue item:** `WORKSPACE/PIPELINE.md:185` (item 59). Research only — no production code written.
**Researched against:** `main` @ `b81b0354`, working tree clean of any capture-related change, 0 commits behind `origin/main`.

---

## Verdict on the "missing primitive" claim

**Half true, and the missing half is much smaller than the queue records.** Of the two halves the item needs — *(a)* put the soldier back on the map alive, and *(b)* set the target's owner to Neutral rather than to the captor — **(a) is not missing at all: the base `Enter` activity already returns the actor to its cell unconditionally, and the mod already ships the "enter, act, walk out" shape.** Only **(b)** is genuinely absent, and it is a **one-branch change inside `CaptureActor.DoCapture`**, not a new subsystem.

The item's own note that this needs "a custom on-capture hook" (`PIPELINE.md:191`, quoting `DOCS/reference/supply-route.md:72`) is correct about the *owner* half and silent about the *ejection* half, which is where the perceived cost was.

---

## 1. What actually happens today

### Which infantry can capture what

Two YAML templates, both in `mods/ww3mod/rules/ingame/infantry.yaml`:

| Template | Line | CaptureTypes | ValidRelationships | CaptureDelay | ConsumedByCapture |
|---|---|---|---|---|---|
| `^CapturesOccupiedBuildings` | `:927-938` | `building-occupied` | `Enemy` | 1000 | **not set → `true` by engine default** |
| `^CapturesNeutralBuildings` | `:939-948` | `building-neutral` | (default `Neutral\|Enemy`) | 20 | `true` (explicit, `:945`) |

Inheritors of the OCCUPIED template — the actors this change would touch: `^E1` Conscript (`:1127`), `^E3` **Rifleman** (`:1194`), `^AR` **Automatic Rifleman** (`:1313`), `^TL` Team Leader (`:1450`), `^PILOT` (`:2407`). `^TECN` (`:2262`) is the **only** inheritor of the NEUTRAL template.

Targets: `^NeutralOrOccupiedCapturable` (`structures.yaml:149-157`) declares `CaptureManager`, `Capturable@neutral: Types: building-neutral`, `Capturable@occupied: Types: building-occupied`. It is pulled in by `^BasicBuilding` (`structures.yaml:10`), so it reaches every building that does not explicitly remove it.

### Is capture-from-enemy really permitted today? Yes — the user's report is accurate

The distinguishing mechanism is **capture *type*, not unit class**. A Rifleman carries `Captures` for `building-occupied` only, and `Capturable@occupied` is present on the Derrick, so `CaptureManager.CanTarget` (`CaptureManager.cs:141-154`) returns true for an enemy-owned Derrick and false for a neutral one. So today:

- Soldier + **enemy-held** Derrick → **capture succeeds, soldier takes ownership**.
- Soldier + **neutral** Derrick → **not permitted** (`ValidRelationships: Enemy`, `infantry.yaml:938`).

**The second half already matches the user's desired rule.** The asymmetry the brief asked about is real and already shipped: a technician is *already* required for neutral structures. This item only extends that rule to enemy-held ones.

### The soldier is consumed today — and by default, not by declaration

`^CapturesOccupiedBuildings` never sets `ConsumedByCapture`; `Captures.cs:41` defaults it to `true`; disposal happens at `CaptureActor.cs:136-137`. Repo-wide, the only `ConsumedByCapture` line anywhere in `mods/` is `infantry.yaml:945`.

This explains the user's parenthetical "so we do not lose the soldier" — **they lose the soldier today.**

> ### Documentation defect found (recommend fixing, not fixed here)
> `DOCS/reference/game-model.md:35` states: *"Soldiers use `^CapturesOccupiedBuildings`, which is **not** consumed…"*. That is **wrong** — see `Captures.cs:41` plus the absence of any override in `mods/`. The same line's file:line citations (`infantry.yaml:897`/`:903`) are also stale; the real lines are `:927`/`:945`. `DOCS/reference/` is the curated bank, so this is exactly the "fix verifiably-wrong statements on sight" case in `CLAUDE.md` — left alone only because this task was scoped to one file.

### One timing note worth carrying

`CaptureDelay: 1000` at default game speed (`mod.yaml:350` `DefaultSpeed: default`; `mod.yaml:374` `Timestep: 60`) is **60 seconds** of standing next to the building. PIPELINE item 59 (`:194`) flagged as open whether the user's observation went through this path; the delay is long enough that it is worth re-confirming with them, but nothing in the code offers a shorter soldier route to an enemy building.

---

## 2. Closest existing mechanisms

Ranked by how much of the job they already do.

**(a) `Enter`'s unconditional exit — this is the ejection primitive, and it is free.**
`Enter.cs:158-164`: after `OnEnterComplete`, the state machine moves to `Exiting` and queues `move.ReturnToCell(self)`. Crucially the actor is **never removed from the world**: `MoveIntoTargetRaw` (`Mobile.cs:775-781`) returns a `LocalMoveIntoTarget`, a *local* move that shifts `CenterPosition` only and never releases the reserved cell. `ReturnToCellActivity` (`Mobile.cs:697-744`) then walks it back to `mobile.ToCell` — the cell it still owns.

**(b) `EnterBehaviour: Exit` — already proven in this mod.**
The enum is `Enter.cs:20` (`Exit, Suicide, Dispose`). `Infiltrate.cs:73-76` shows `Exit` is literally the *absence* of an action — only `Dispose` and `Suicide` do anything. In-mod working uses: `Infiltrates@RestoreTechHusk` with `EnterBehaviour: Exit` (`infantry.yaml:1955-1959`) and `RepairsBridges: EnterBehaviour: Exit` (`infantry.yaml:1952-1953`). The field exists on `Demolition.cs:44`, `EngineerRepair.cs:34`, `InstantlyRepairs.cs:34`, `RepairsBridges.cs:31`, `Infiltrates.cs:38` — **and on nothing in the capture path.** That absence is the whole of the ejection gap: a field, not a mechanism.

**(c) `ConsumedByCapture: false` — proves disposal is separable, but is not the requested shape.**
`CaptureActor.cs:65-71` captures **without entering at all** and does not dispose. It gets "soldier survives" for free but loses the walk-in entirely, so it does not match "when a soldier enters it".

**(d) `GarrisonManager` with `DynamicOwnership` — the closest thing in the repo to the whole requested behaviour, and PIPELINE item 59 does not mention it.**
WW3MOD-authored engine code where a soldier enters a building, the **building changes owner** (`GarrisonManager.cs:255-261`, `ChangeOwnerInPlace(passengerOwner, updateGeneration: false)`), and the soldier later leaves alive via `Cargo`. It also already resolves the neutral player (`:227`) and already special-cases neutral ownership (`:260`). It is the wrong trait to extend — it is a garrison/passenger system, not capture — but it is the local precedent for every individual piece, including the `updateGeneration: false` subtlety.

**(e) What genuinely does not exist:** an `InfiltrateForOwnerChange`-style effect trait. The shipped `InfiltrateFor*` set is Cash / Decoration / Exploration / PowerOutage / SupportPower / SupportPowerReset / Transform (`engine/OpenRA.Mods.Cnc/Traits/Infiltration/`). `TemporaryOwnerManager` is the wrong tool — it reverts to the *original* owner after a duration and is driven by the `ChangeOwner` warhead.

---

## 3. What "neutral" means concretely

**An existing map-defined player, and it exists on all ten shipped maps.** Every map under `mods/ww3mod/maps/` opens its `Players:` block with `PlayerReference@Neutral` / `Name: Neutral` / `OwnsWorld: True` / `NonCombatant: True` (e.g. `twin-rivers-ww3/map.yaml:20-23`, `x-lake-ww3/map.yaml:20-23`). Nine of the ten additionally define `Creeps`; `arena-tank-duel` defines only `Neutral`. Nothing is synthesised.

**But the lookup style matters, and the engine is inconsistent about it:**

- `OwnerLostAction.cs:52` uses `self.World.Players.First(p => p.InternalName == Info.Owner)` with `Owner` defaulting to `"Neutral"` (`:29`). `First` **throws** on a map with no Neutral player.
- WW3MOD's own code uses the defensive form: `GarrisonManager.cs:227` and `HeliEmergencyLanding.cs:354` both use `FirstOrDefault`, and `GarrisonManager.cs:254` null-guards the result.

So the design does not break on the shipped maps, but a hand-authored or third-party map that omits `Neutral` is a real hazard. **Follow the `FirstOrDefault` + null-guard precedent, not `OwnerLostAction`.** Failure mode if you copy `OwnerLostAction`: unhandled `InvalidOperationException` at the moment of capture — a mid-match crash, not a graceful no-op.

A second consequence, which is a design question rather than a bug: `NonCombatant: True` means `Player.RelationshipWith` returns `Neutral` for everyone (`Player.cs:255`), so a neutralised structure is immediately a valid `building-neutral` target for **either side's** technician. That is presumably intended ("we must still get our own technician there") but it also means the *original* owner can re-take it with their own technician. Worth confirming with the user.

---

## 4. The ejection half

**Requires no new code and has no all-cells-blocked failure mode**, because the soldier never leaves the map. See §2(a): the cell is reserved throughout, and `ReturnToCellActivity` returns to that same reserved cell.

The one degenerate case is sub-cell, not cell: `Mobile.cs:731-733` carries a standing `// TODO: solve/reduce cell is full problem` and falls back to `SubCell.Invalid → Grid.DefaultSubCell`, which can stack two infantry in one sub-cell. That is a pre-existing engine wart on every `Enter` user (bridge repair, husk restore, garrison) and this change adds no new exposure to it.

**Named failure mode, then:** not "nowhere to eject to" but "sub-cell overlap on a crowded tile", cosmetic, pre-existing, shared with three shipped mechanics.

---

## 5. Blast radius

**Bots: no impact, and the exclusion is deliberate.**
`CaptureCoordinatorBotModule` is the live module (the legacy `CaptureManagerBotModule` is "not instantiated in ww3mod", `CaptureManagerBotModule.cs:49`). Both profiles set `CapturingActorTypes: tecn,tecn.russia,tecn.america` and `UseUnitRoles: true` (`ai.yaml:113,118` experimental; `ai.yaml:1924-1927` stable). Under `UseUnitRoles` the pool is drawn from `UnitRoleResolver` role `CaptureSpecialist` (`CaptureCoordinatorBotModule.cs:573`), and that role is assigned **by neutral-capture type, explicitly excluding line infantry**:

> `UnitRoleResolver.cs:352-353` — *"CaptureSpecialist — neutral-tech capture TYPE (not mere Captures presence: line infantry also carry Captures for occupied buildings)."*
> with `NeutralCaptureType = "building-neutral"` (`UnitRoleResolver.cs:168`).

So bots never order a soldier to capture anything. No re-target loop is possible. **Caveat for the implementer:** `CheckUnitRoleTable.cs:195-204` is a lint asserting the `CaptureSpecialist` class equals `CapturingActorTypes`. Any shape that gives soldiers a `building-neutral` capture type would silently reclassify them as `CaptureSpecialist`, break that lint, **and** hand the bot's capture coordinator a pool of riflemen. Do not go near `building-neutral` on soldiers.

**Tests: one scenario is in the blast radius, and whether it breaks is shape-dependent.**
`tools/autotest/scenarios/test-capture-rules/test-capture-rules.lua` asserts four capture *capabilities*, of which two matter here:
- `:21-25` — Soldier must NOT capture neutral OILB.
- `:27-31` — Soldier MUST capture enemy OILB.

`CanCapture` resolves to `captureManager.CanTarget(targetManager)` (`CaptureProperties.cs:41-45`), which reads **only** `CaptureTypes` × `ValidRelationships` — it never inspects the *effect*. So a shape that keeps the `Captures` trait and changes only what happens on success leaves this test **green and untouched**; a shape that swaps `Captures` for `Infiltrates` turns `:27-31` **red**. This is the sharpest discriminator between the candidate shapes below.

The other capture scenarios (`test-experimental-poi-capture`, `demo-experimental-capture-coordinator`) are TECN-only and unaffected. NUnit `PoiCaptureMetricsTest` / `CaptureFanoutMathTest` / `CaptureSupplyMathTest` test ownership-transition classification and target-selection maths generically, with no soldier/technician distinction — **but `PoiCaptureMetricsTest` classifies Capture/Steal/Recapture events, and a new "owner became Neutral" transition is exactly the kind of input it has never seen.** Worth reading before implementing; I did not trace whether a to-Neutral transition would be miscounted.

**Actors affected.** Capturable today: `OILB` (`CashTrickler: 50`), `FCOM` (100), `BIO` (150) — the money structures — plus `MISS` and `HOSP` (no income), all via `^TechBuilding` → `^BasicBuilding`. Explicit removals exist on `^CivBuilding` (`civilian.yaml:6-10`), three defence structures (`structures-defenses.yaml:81-85,171-175,257-261`) and two aircraft husks. **`SUPPLYROUTE` is confirmed outside this entirely** — it does not inherit `^BasicBuilding` and carries no `CaptureManager`/`Capturable`, matching `CLAUDE.md` and `supply-route.md:68`.

> **CORRECTION (2026-08-13, found during implementation review).** The five actors named above are **not** the blast radius, and taking them as such would have shipped a serious balance change. `^NeutralOrOccupiedCapturable` sits on `^BasicBuilding` (`structures.yaml:10`); `^Building` inherits `^BasicBuilding` (`:69-70`); `^Defense` inherits `^Building` (`structures-defenses.yaml:2-3`). Resolving the inheritance graph gives **23 actors** carrying `Capturable@occupied`:
>
> - **`^TechBuilding` descendants (11):** `OILB`, `FCOM`, `BIO`, `MISS`, `HOSP`, `AMMOBOX1`, `AMMOBOX2`, `AMMOBOX3`, `BARL`, `BRL3`, `CTFLAG`.
> - **Non-tech (12):** `AFLD`, `AGUN`, `CRAM`, `FTUR`, `GUN`, `HGATE`, `HPAD`, `HSAM`, `LOGISTICSCENTER`, `MSLO`, `SAM`, `VGATE`.
>
> Only `BIO`/`FCOM`/`OILB` carry `CashTrickler`. Applying `CaptureToNeutral` to the whole `building-occupied` type — as Shape A originally described — would have let one surviving rifleman walk an enemy base turning every AA gun, SAM, silo, airfield and logistics centre Neutral at **no unit cost**, against a bot with no logic to reclaim its own neutralised defences. The shipped change therefore splits the occupied type: `^TechBuilding` gets `building-occupied-tech` (evict to Neutral, soldier survives), everything else keeps `building-occupied` and the classic capture-and-be-consumed rule.

**Supply Route design: same primitive, and building it here unblocks item 17.** `supply-route.md:72` and `DISCOVERIES.md:3118` both record that the intended "capturer can never keep it, it just goes Neutral" SR behaviour cannot be built from stock capture traits. That is *the same missing (b)*. A flag on `CapturesInfo` would serve both; a soldier-specific `Infiltrates` trait would not.

---

## 6. Candidate shapes

### Shape A — add `EnterBehaviour` + a neutralise flag to `CapturesInfo` *(recommended)*

Two new fields on `CapturesInfo` (`Captures.cs`), both defaulting to today's behaviour, honoured in `CaptureActor.DoCapture` (`CaptureActor.cs:94-139`): resolve the Neutral player instead of `self.Owner` at `:126`, and gate the `self.Dispose()` at `:136-137` on `EnterBehaviour` rather than on `ConsumedByCapture`. YAML change is two lines on `^CapturesOccupiedBuildings`.

- **Size:** ~20 lines engine, 2 lines YAML, plus a `FirstOrDefault` null-guard.
- **Ejection:** free — `ConsumedByCapture: true` keeps the walk-in path, suppressing the dispose lets `Enter.cs:158-164` walk the soldier out.
- **Keeps:** capture cursor, `CapturableProgressBar`/`Blink`, the 1000-tick delay, the `BeingCaptured` re-entrancy guard, `test-capture-rules.lua` green.
- **Serves the Supply Route item too.**
- **Cost:** `ConsumedByCapture` currently conflates "enter or not" with "dispose or not"; this separates them, so the interaction with the progress-duration estimate at `CaptureManager.cs:220` needs a look.

### Shape B — swap soldiers to `Infiltrates` + a new `InfiltrateForOwnerChange`

New ~60-line trait modelled on `InfiltrateForTransform.cs`, placed on the structures; soldiers' template swaps `Captures@OCCUPIED` for `Infiltrates` with `EnterBehaviour: Exit`.

- **Size:** ~60 lines engine (new file), YAML on both infantry and structures, plus a new `Targetable` type.
- **Loses:** the capture cursor and progress bar (both key off `Capturable`), the 1000-tick delay (`Infiltrate` has no delay concept), and the `BeingCaptured` guard.
- **Breaks `test-capture-rules.lua:27-31`** — `Soldier.CanCapture(EnemyOilb)` becomes false.
- Does nothing for the Supply Route item.
- Only advantage: touches no shared capture code.

### Shape C — leave capture alone, bounce the owner afterwards via `INotifyCapture`

A trait on the structure reacting to `OnCapture` by flipping to Neutral.

- **Rejected.** The soldier is already disposed by the time it fires (`CaptureActor.cs:136`); it produces a two-step owner change (a frame of real ownership, a doubled `INotifyCapture` fan-out, and an income tick to the wrong player); and it would have to infer "was the captor a technician" from unit identity, which is exactly the brittleness the type-based system exists to avoid. Named because it is the shape someone will propose.

---

## 7. Recommendation

**Shape A.** It is the smallest of the three, it is the only one that leaves the existing test suite untouched, it preserves all the capture UI the player already reads, and it is the same primitive the parked Supply-Route-capture item needs. The ejection half — the part the queue believed was missing — costs nothing under this shape.

**What would falsify it:**

1. **If the user does not actually want the walk-in animation.** If "when a soldier enters" is loose phrasing and they would accept a touch-and-flip, `ConsumedByCapture: false` (`CaptureActor.cs:65-71`) already does the whole job today with **zero engine changes** — one YAML line. This is worth asking before writing any code.
2. **If a to-Neutral transition corrupts `PoiCaptureMetricsTest`'s Capture/Steal/Recapture classification** — I did not trace that, and it would add scope.
3. **If separating dispose from `ConsumedByCapture` disturbs the progress-bar duration estimate** at `CaptureManager.cs:220`.
4. **If the exit looks wrong on a 1×1 building footprint.** Only the running game can settle this.

---

## 8. Could not determine without running the game

Stated as unknowns rather than guessed:

- **Whether the capture the user observed was the 60-second `building-occupied` path.** Nothing in the code offers a shorter route, but 60 s is long enough that the observation deserves re-confirmation. (Carried over from `PIPELINE.md:194`, still open.)
- **How `ReturnToCell` looks coming out of a 1×1 building.** Static reading says the soldier re-emerges in the cell it already held; whether that reads as "walked back out" or "teleported off the roof" is a visual question.
- **Whether a neutralised structure being re-takeable by the *original* owner's technician matches the user's intent.** This follows necessarily from `NonCombatant` neutrality (`Player.cs:255`) and is a design decision, not a code fact.
- **Whether `CashTrickler` income stops cleanly on a to-Neutral flip.** Not traced.
