# Garrisonable civilian structures — end-to-end audit

**Date:** 2026-09-01 · **Ref audited:** `wt/garrison-audit` @ `f911b6da` (branched from `main`)
**Scope:** the user's own request, filed 2026-08-30 — *"civ structures that can be garrisoned is something we need to look at in general to find all issues and improvements."*

This is an **audit**. No behavioural fix is implemented here. Every claim carries `file:line`, and
every claim is marked **[V]** verified by reading the code that proves it, or **[I]** inferred from a
name, comment or convention.

---

## 0. Headline: the brief that commissioned this audit was wrong in three ways

The dispatching brief described the system as `^CivBuilding` carrying a bare `Cargo`, on ~44
permanently-Neutral actors. All three parts are wrong, and the third inverts the security model.

| Brief claimed | Actual | Where |
|---|---|---|
| "`Cargo: Types: Infantry, MaxWeight: 10`" is the mechanism | `Cargo` is one of **six** garrison traits. There is a ~4,700-line engine subsystem: `GarrisonManager` (1587), `AttackGarrisoned` (419), `WithGarrisonDecoration` (422), `GarrisonPanelLogic` (286), `GarrisonProtection` (118), `GarrisonPortOccupant` (126), `GarrisonExitMath` (69), plus 2 bot modules and 3 NUnit files | `civilian.yaml:58-128`; `engine/OpenRA.Mods.Common/Traits/Garrison/` |
| "**41 actors** inherit it, plus GTWR/PBOX/HBOX — roughly **44**" | **38** inherit it; **41** garrisonable actors in total. Overcount of 3 | §1 |
| "**All are Neutral-owned.** That is precisely why they are the fog exposure" | **Ownership transfers to the garrisoning player on entry.** `DynamicOwnership = true` by default and is overridden nowhere in the mod | `GarrisonManager.cs:89`; `:256-260` |

The `Cargo` line reference itself (`civilian.yaml:58-67`) is exact. **[V]**

The third row matters beyond bookkeeping: a garrisoned building is **owned**, which is why its
occupancy pips are invisible to the enemy (§4), why hostile co-garrison is a trap rather than a
stand-off (§3), and why the frozen-under-fog reasoning that produced this brief only describes the
*empty* state of these buildings.

---

## 1. The census

**41 garrisonable actors.** Definition used: carries `Cargo` with `Types: Infantry` **and**
`GarrisonManager`. **[V]**

### 1a. Via `^CivBuilding` — 38 actors

`^CivBuilding` is declared at `civilian.yaml:2`. Its garrison stack is `Cargo` (`:58-67`),
`Inherits@CargoPips` (`:68`), `GarrisonManager` with 8 named ports (`:69-114`), `GarrisonProtection`
(`:115-119`), `Inherits@GarrisonHealthPips` (`:120`), `WithGarrisonDecoration` (`:121-123`) and
`AttackGarrisoned` (`:124-128`). **[V]**

- **20 direct** — `V01`–`V13` (`civilian.yaml:236,258,277,296,315,328,341,354,364,373,382,391,395`),
  `V19` (`:419`), `V19.Husk` (`:429`), `RUSHOUSE` (`:587`), `ASIANHUT` (`:601`), `SNOWHUT` (`:610`),
  `LHUS` (`:626`), `WINDMILL` (`:642`). **[V]**
- **18 via `^DesertCivBuilding`** — `V20`–`V37` (`civilian.yaml:464`–`:570`). `^DesertCivBuilding`
  (`:129`) inherits `^CivBuilding` (`:130`) and changes only palette and tileset gating (`:131-134`),
  so every desert house carries the full stack unmodified. **[V]**

Two lines a naive grep miscounts, and which explain most of the brief's overcount: `civilian.yaml:130`
is `^DesertCivBuilding` inheriting the template — **an abstract template, not an actor** — and
`civilian.yaml:195` is a **commented-out** `# Inherits: ^CivBuilding`. **[V]**

`V19.Husk` is counted because it genuinely inherits the stack, but it is a wreck, not a building, and
its inclusion is itself a bug — see §2a.

### 1b. Not via `^CivBuilding` — 3 actors

`GTWR`, `PBOX`, `HBOX` in `structures-defenses.yaml`, each declaring the full quartet independently:
GTWR `:118/127/153/161`, PBOX `:213/222/238/246`, HBOX `:303/312/328/336`. **[V]**

### 1c. Capacity and weight

`Cargo.Types: Infantry` everywhere, so only infantry-class passengers qualify; the match is
`ci.Types.Contains(Info.CargoType)` against the passenger's own `PassengerInfo.CargoType`
(`Passenger.cs:139-141`). **[V]** Capacity is **not** uniform:

| Family | `MaxWeight` | Firing ports | Port cone | Protection Base/Crit/Rubble |
|---|---|---|---|---|
| `^CivBuilding` (38 actors) | 10 (`civilian.yaml:61`) | 8 (`:70-110`) | 140 | 95 / 70 / 30 explicit (`:116-118`) |
| GTWR | 6 (`sd.yaml:121`) | 4 (`:128-148`) | 200 | 97 / 80 / **default** (`:154-156`) |
| PBOX | 4 (`sd.yaml:216`) | 2 (`:223-233`) | 300 | 97 / 80 / **default** (`:239-241`) |
| HBOX | 4 (`sd.yaml:306`) | 2 (`:313-323`) | 300 | 96 / 75 / **default** (`:329-331`) |

**No descendant of `^CivBuilding` overrides or removes any garrison trait** — verified by scanning
every line after `civilian.yaml:129` for `Cargo:`, `MaxWeight`, `Garrison*` or a `-` removal of any of
them: zero hits. **[V]** So all 38 civilian buildings are mechanically identical regardless of
sprite footprint: a one-cell desert hut holds the same 10 men and fields the same 8 ports as a large
house. **[V]**

`MinPassThrough: 15` is the only garrison number identical across all four families. **[V]**

---

## 2. Correctness

### 2a. BUG — `V19.Husk` keeps `GarrisonProtection` but deletes `Health`; the trait throws on creation

`V19.Husk` (`civilian.yaml:429`) inherits `^CivBuilding` (`:430`) and then removes `-Selectable:`,
`-Targetable:`, `-HitShape:`, **`-Health:`** (`:434`) and the damage-state rungs — but it does **not**
remove `Cargo`, `GarrisonManager`, `GarrisonProtection` or `AttackGarrisoned`. **[V]**

`GarrisonProtection.Created` runs `health = self.Trait<IHealth>();` (`GarrisonProtection.cs:55`),
unguarded. `Actor.Trait<T>()` → `TraitDictionary.Get<T>` → throws
`InvalidOperationException($"Actor {name} does not have trait of type ...")`
(`Actor.cs:465-468`, `TraitDictionary.cs:87-91`, `:158-165`). So constructing a `V19.Husk` throws. **[V]**

Three things make this worth fixing rather than shrugging at:

1. **The author expected null and got a throw.** `GetCurrentProtection` guards `health == null`
   (`GarrisonProtection.cs:65`) and `Damaged` guards it again (`:78`). Both guards are **dead code** —
   `:55` would already have thrown. The defensive intent is there; the wrong accessor defeats it. **[V]**
2. **Lint cannot catch it.** `GarrisonProtectionInfo` declares `Requires<GarrisonManagerInfo>,
   Requires<CargoInfo>` (`:19`) — **not** `Requires<HealthInfo>`. A YAML validator has nothing to
   flag. **[V]**
3. **It is latent, not live.** `V19.Husk` is placed in no shipped map (grepped
   `mods/ww3mod/maps/`: zero hits) **[V]**, and its only other spawn path is `V19`'s
   `SpawnActorOnDeath` (`civilian.yaml:425-426`), which §2b shows is unreachable by ordinary damage.
   It is a trap armed for whoever fixes §2b or places the husk in an editor — and `MapEditorData` is
   inherited (`civilian.yaml:27-29`), so it *is* placeable. **[V]**

### 2b. BUG — every garrisonable building is indestructible by ordinary damage, and nothing says so

`GarrisonManagerInfo.Indestructible` defaults **`true`** (`GarrisonManager.cs:83-85`): *"If true,
building cannot be destroyed — HP is clamped to 1 minimum."* **Nothing in the mod overrides it** —
zero hits for `Indestructible` across `mods/ww3mod/rules/`. **[V]**

Enforced through `IDamageModifier.GetDamageModifier` (`GarrisonManager.cs:1415-1435`): returns `0` at
`HP <= 1`, otherwise scales a lethal hit down to leave exactly 1 HP (`:1425-1432`). **[V]**

The clamp has **no occupancy gate** — an empty civilian house and an empty pillbox are equally
immortal. **[V]** Consequences, all reachable by inspection:

- `Cargo.EjectOnDeath: True` (`civilian.yaml:67`) never fires.
- `Explodes` / `Explodes@CIVPANIC` (`civilian.yaml:50-53`) never fire.
- `SpawnActorOnDeath` never fires — `V19`→`V19.Husk` (`:425-426`), and GTWR/PBOX husks
  (`sd.yaml:211-212`, `:301-302`).
- `GarrisonManager`'s own `INotifyKilled.Killed` handler (`:1369-1400`) — the code that gives port
  soldiers proportional blast damage and lets them scatter — is dead on the damage path.
- GTWR and PBOX still inherit `MustBeDestroyed` from `^Defense` (`sd.yaml:13`), while HBOX removes it
  (`:268`). An indestructible `MustBeDestroyed` structure is a plausible victory-condition stall.
  **[I]** — static reasoning only; see §7.

**There is one bypass.** `Health.Kill` calls `InflictDamage(..., ignoreModifiers: true)`
(`Health.cs:243-246`), and `InflictDamage` skips all `IDamageModifier`s when that flag is set
(`:177`). So `Actor.Kill()` (`Actor.cs:634-640`) — Lua scripting, crush, `KillsSelf` — still destroys
the building. **[V]** On that one reachable path the occupants **die rather than eject**: `Killed`
computes `damageToDeal = soldierHealth.MaxHP * damage / self.MaxHP` (`GarrisonManager.cs:1391`), and
`Kill` passes `damage == MaxHP`, so the expression is exactly `soldierHealth.MaxHP` — 100% of the
soldier's health — plus `SharedRandom.Next(MaxHP/5)` (`:1392`). Every port soldier dies with
certainty. **[V]**

So the honest answer to "what happens when the building is destroyed while occupied" is: **it cannot
be destroyed by weapons at all, and on the one script path that does destroy it, the survival code
that was written is arithmetically guaranteed to kill everyone it applies to.** Neither half is
likely to be what was intended.

### 2c. Evacuation is wired carefully and appears correct

The `Unload` path (`GarrisonManager.cs:1441-1472`) clears port soldiers in-world, then explicitly
calls `CheckOwnershipAfterExit()` (`:1471`). The comment at `:1460-1470` documents exactly why: port
soldiers take no `Cargo` exit path, and `UnloadCargo`'s own revert is skipped when the shelter is
empty, so without this call a building emptied through the port path *"stays owned by the garrisoning
player forever — for a neutral civilian house, a permanent silent annexation."* **[V]** That is a bug
that was found and fixed; the reasoning is preserved in-tree. Good.

A matching gap was closed on the order side: `GarrisonManager` emits its own `Unload` targeter when
the shelter is empty but ports are manned (`:1337-1352`), which `Cargo`'s targeter would miss. **[V]**

### 2d. `OwnerLostAction: ChangeOwner` — mostly unreachable, and misread by the brief

`civilian.yaml:56-57`. Because ownership transfers on entry (§0), an **occupied** building is owned by
the garrisoning player, so `OwnerLostAction` fires when *that player* is defeated, flipping the
building — with the loser's men still inside. **[I]** — I read the YAML and the ownership transfer,
but did not trace `OwnerLostAction`'s interaction with a non-empty `Cargo`.

For an **empty** building the trait is inert: the owner is the Neutral player, who is never defeated.
**[V]**

The brief asks what it does "when two players have men inside". `OwnerLostAction` is not the trait
that decides that — `CheckOwnershipAfterExit` is, and it does something more surprising. See §3.

---

## 3. The simultaneous-entry mechanic — reachable, and it is a trap

**Two hostile players CAN garrison the same building simultaneously.** This is the most consequential
finding in the audit. **[V]**

### 3a. There is exactly one relationship check in the entire entry chain

I traced order issue → resolve → activity → load. The relationship gate appears **once**, at
targeting time:

`EnterAlliedActorTargeter.CanTargetActor` admits allied **or neutral** owners —
`if (!self.Owner.IsAlliedWith(target.Owner) && !self.Owner.IsNeutralWith(target.Owner)) return false;`
(`EnterAlliedActorTargeter.cs:49-54`). **[V]**

After that, nothing re-checks:

| Stage | Checks performed | Relationship check? |
|---|---|---|
| `Passenger.ResolveOrder` | target type, alive, in-world, `CanEnter` (space), `IsCorrectCargoType` (`Passenger.cs:228-239`) | **none** **[V]** |
| `RideTransport.TryStartEnter` | cargo exists, `Reserve` succeeds (`RideTransport.cs:49-61`) | **none** **[V]** |
| `RideTransport.OnEnterComplete` | not dead, same actor, `CanLoad` (`:69-87`) | **none** **[V]** |
| `Cargo.CanLoad` | `LoadingBlocked`, `ICargoCanLoadFilter`s, space (`Cargo.cs:518-529`) | **none** **[V]** |

The `ICargoCanLoadFilter` extension point exists and would be the natural place for such a check, but
its **only implementor mod-wide is `SupplyProvider`** (`SupplyProvider.cs:225`, `:1245`), which is not
on any civilian building. **[V]**

### 3b. The race

1. Building is Neutral. Players A and B each order infantry in. Both orders are legal — the targeter
   admits neutral. **[V]**
2. A's soldier loads first. `GarrisonManager.OnPassengerEntered` flips the building to A —
   but only `if (self.Owner == neutralPlayer || self.Owner.InternalName == "Neutral")`
   (`:256-260`). **[V]**
3. B's soldier is already mid-`RideTransport`. Nothing cancels it — and note this is **deliberate**:
   the transfer passes `updateGeneration: false` precisely so in-flight `Enter` activities are *not*
   invalidated (`:251-255`, comment). The mechanism that would have closed the race was
   intentionally disabled to fix a different bug. **[V]**
4. B's soldier loads into a building owned by his enemy. Step 2's `if` does not re-fire, so ownership
   stays with A. **[V]**

### 3c. What happens next is worse than a stand-off

- **B's soldier will be deployed to a firing port and shot under A's control.**
  `FindBestShelterSoldier` iterates `shelterPassengers` filtering only on dead / suppressed / armed
  (`GarrisonManager.cs:539-558`) — **no owner filter**. **[V]** Target selection is the building's,
  and the building is A's. B's rifleman fires at A's chosen targets, which may include B's own units.
  **[I]** on the final step — I verified the absence of the owner filter and that the building drives
  target selection, but did not trace a shot end-to-end to a victim.
- **The building can defect to the hostile occupant.** `CheckOwnershipAfterExit` builds
  `remainingOwners` from every living occupant with no relationship filter (`:305-317`), and if the
  current owner has none left it transfers to `remainingOwners.First()` (`:325-329`). The comment
  says *"but an ally does → transfer"* — the code says any player at all. **[V]** So if A's men die
  and B's survive, the building hands itself to B, with no capture, no technician, and no
  `Capturable` trait involved. That is a fourth ownership-change route the capture documentation in
  `game-model.md` §"Capturing neutral buildings" does not mention.
- **B's men are probably stuck.** Evacuation is an `Unload` order issued to the **building**
  (`GarrisonManager.cs:1341-1357`), which B does not own. The `DeployOrderTargeter` at `:1350`
  carries no ownership predicate of its own, so whether B can issue it depends on the engine's
  selection/order pipeline rather than on anything in the garrison code. I could not settle this
  statically — see §7, Q3.

### 3d. Is it intended?

**No. [I]** Three pieces of evidence, none conclusive alone: `CheckOwnershipAfterExit`'s comment says
"ally" while its code says "any player" (`:324`, `:325-329`); `OnPassengerEntered`'s neutral-only
guard (`:258-259`) reads as an assumption that the only transition is Neutral→owner; and no test
covers a mixed-ownership garrison. The design clearly contemplated *allied* co-garrison and did not
contemplate hostile co-garrison.

**Exploitability is low-frequency but not theoretical** — it needs both players to commit infantry to
the same neutral house within one traversal window, which is exactly what happens over a contested
mid-map building.

---

## 4. Legibility — the weakest area, and it fails in the direction that matters

| Question | Answer |
|---|---|
| Is this building garrisonable? | **Only by probing.** Cursor turns `enter` / `enter-blocked` when infantry are selected and hovering (`Passenger.cs:87-94`, `:58-62`; choice at `EnterAlliedActorTargeter.cs:56`). Nothing renders on an empty garrisonable building. **[V]** |
| Is it occupied? | **Owner and allies only.** See below. **[V]** |
| Whose men are inside? | **Only via the building's colour.** `RenderSprites: Palette: player` (`civilian.yaml:25`) recolours it on the ownership transfer. That is the sole cross-player signal. **[I]** on palette remap mechanics. |
| How full is it? | **Not shown.** Filled slots only, no denominator. **[V]** |

**The occupancy decoration is invisible to the enemy.** `WithGarrisonDecorationInfo` extends
`WithDecorationBaseInfo`, whose `ValidRelationships` defaults to `PlayerRelationship.Ally`
(`WithDecorationBase.cs:107-108`), and `civilian.yaml:121-123` does **not** override it. `ShouldRender`
computes `self.Owner.RelationshipWith(self.World.RenderPlayer)` and bails unless the `Ally` flag is
set (`:165-170`). **[V]** Because garrisoning transfers ownership (§0), the building's owner *is* the
garrisoning player — so the opponent always evaluates to `Enemy` and every pip is suppressed. The
same default silences `^GarrisonHealthPips` (`defaults.yaml:209-226`). **[V]**

That is the ambush-legibility theme in `project_ambush_legibility` again: the readout exists, is
well-built, and is shown to the one player who already knows. An attacker's only cues are the
building shooting at him, and its `Targetable` swapping to `RequiresForceFire: true` under `loaded`
(`civilian.yaml:20-23`) — which surfaces as attack-move mysteriously declining to engage a house.
**[V]**

**`^CargoPips` is dead weight on all 41 actors.** `WithCargoPipsDecoration` records
`hasGarrisonDecoration = self.Info.HasTraitInfo<WithGarrisonDecorationInfo>()` (`:70`) and its
`RenderDecoration` opens `if (hasGarrisonDecoration)` → early out (`:97`). `^CivBuilding` has both
(`civilian.yaml:68`, `:121`), so the inherited cargo pips never draw. **[V]** The `Inherits@CargoPips`
line is not harmless clutter — it is the line a future reader will trust when asking "does fullness
render?".

**No denominator anywhere.** `WithGarrisonDecoration` renders `slotCount = totalCount` — occupied
slots only (`:253-260`) — so 3-of-10 and 3-of-4 draw identically. `Info.EmptySequence` is referenced
at `:337` on a `soldier == null` branch that `slotCount == soldiers.Length` makes unreachable. **[V]**

**`GarrisonPanelLogic` is wired but owner-only.** `Container@GARRISON_PANEL` with
`Logic: GarrisonPanelLogic` is at `chrome/ingame-player.yaml:610-611`, and that file is registered in
`mod.yaml:186`. **[V]** It names each occupant, port, ammo and cover percentage (`:159-185`) — genuinely
good — but `UpdateSelection` filters `a.Owner == world.LocalPlayer` (`:135-137`) and requires exactly
one selected actor (`:139-140`). **[V]** Separately `chrome/garrison-panel.yaml` is a near-duplicate
absent from `mod.yaml` and therefore dead — already recorded at `WORKSPACE/bugs/discovered.md:2904`,
not a new finding.

---

## 5. AI use — both bot profiles garrison; the capability is wired

**Yes, and this refutes the natural assumption that garrison is human-only. [V]**

`GarrisonBotModule@defenses` sits at `ai.yaml:1001` under `RequiresCondition: enable-ai-any`
(`:1002`). `GrantConditionOnBotOwner@anyai` grants `enable-ai-any` to `Bots: experimental, stable`
(`ai.yaml:73-75`). **[V]** There is exactly one `GarrisonBotModule` instance mod-wide, so both
profiles run the same shared module rather than gated twins. **[V]**

It issues `Order("EnterTransport", infantry, Target.FromActor(building))` against actors carrying
`GarrisonManager` (`GarrisonBotModule.cs:331`, `:262`). Shipped tuning: `ScanInterval: 200`,
`MaxGarrisonRadius: 25`, `MaxOrdersPerTick: 2`, `RequireBelievedThreat: true`,
`MinBelievedDanger: 1`, `MinGarrisonDwellTicks: 750` (`ai.yaml:1003-1031`). **[V]**

Nothing renders it inert. `GarrisonActorTypes` is unset, and the empty-list branch is the
*permissive* one — it falls back to "any `PassengerInfo` holder" (`GarrisonBotModule.cs:495-503`).
**[V]** The one real gate is `RequireBelievedThreat`, which narrows both profiles to believed-danger
cells; `InfluenceStack.Participates` returns true for both `"experimental"` and `"stable"`
(`InfluenceStack.cs:42-48`). **[V]**

**`PoiGarrisonBotModule` is a false friend.** Despite the name it never garrisons a building — its
only order is `AttackMove` onto a POI cell (`PoiGarrisonBotModule.cs:514`), and it contains no
`Cargo`/`Passenger`/`GarrisonManager`/`EnterTransport` reference in 599 lines. **[V]** Anyone auditing
"does the AI garrison?" by grepping for `Garrison` in `BotModules/` will read the wrong file.

**Test coverage of AI garrison is zero.** `PoiGarrisonTest.cs` exercises only pure arithmetic —
`PoiScoring.DefendThreatFactor` (`:34-73`) and `PoiGarrisonMath` (`:77-185`) — with no `World`, no
actors and no orders, and it does not touch `GarrisonBotModule` at all. **[V]** Every line of the
module that actually garrisons is untested.

Per `CLAUDE.md`, `@stable` inheriting this is **fine and settled policy** — flagged here only so the
next benchmark baseline is taken knowingly. `ai.yaml:1015` already says "THIS CHANGES @stable".

---

## 6. Balance

**Damage taken (shelter occupants).** `GarrisonProtection` implements `INotifyDamage`, **not**
`IDamageModifier` (`:38`) — it does not shield the building; it *forwards* a slice of the building's
damage to one randomly-chosen shelter occupant (`:112-115`). The direction-settling line is
`var passThrough = incomingDamage * (100 - protection) / 100;` (`:107`), so `BaseProtection: 95` means
occupants take **5%**. **[V]**

Three findings:

1. **The tiers are not damage-state tiers.** `DamageState` is never read. Selection is
   `HP <= 1 → RubbleProtection`, else a **continuous linear interpolation** between `CriticalProtection`
   at HP→0 and `BaseProtection` at full HP (`:91-99`). "Critical" is an endpoint, not a threshold.
   **[V]** This creates a **discontinuity cliff at HP 2→1**: `^CivBuilding` jumps 70 → 30, GTWR/PBOX
   80 → 30. **[V]**
2. **`MinPassThrough: 15` is a hard immunity floor, not a floor on damage.** If `passThrough < 15` the
   function `return`s — the damage is **discarded**, not clamped to 15 (`:108-109`). At full HP that
   makes shelter occupants immune to any weapon under ~300 raw damage in a `^CivBuilding`, ~500 in
   GTWR/PBOX, ~375 in HBOX. **[V]** (arithmetic from `:107` integer division).
3. **`GetCurrentProtection()` (`:63-74`) duplicates `Damaged`'s tier logic verbatim and is not called
   by it.** Two copies free to drift — precisely the shape `feedback_duplication_vs_verification`
   warns about. **[V]**

Port occupants use a **different mechanism with a different number**: `DamageMultiplier@GarrisonCover:
Modifier: 20` gated on `garrisoned-at-port` (`infantry.yaml:212-214`) — 80% reduction, versus the
shelter's 95%. **[V]** Nothing documents why the two differ or that they are meant to.

**Damage dealt.** `AttackGarrisoned` fires the **soldier's own armaments**
(`AttackGarrisoned.cs:292`, `:305`) — no building weapon, and no accuracy or rate-of-fire modifier
applied for being garrisoned. **[V]** All deployed ports fire independently every tick (`:252-334`);
`FlashCooldownTicks: 25` throttles only the muzzle flash (`:50`, `:322-324`). **[V]** Firing is arc-limited
via `IsTargetInPortArc` (`:266` → `GarrisonManager.cs:1133-1154`); `Cone` is a **half-angle** in
`WAngle` (`GarrisonManager.cs:32`; 1024 = 90°), so **140 ≈ 12.3° either side, ~25° total** — very
narrow. GTWR 200 ≈ 35° total, PBOX/HBOX 300 ≈ 53°. **[V]**

The `PITFALL` at `GarrisonManager.cs:94-98` forbids a garrison-side suppression fire penalty because
the soldier's own `^SuppressionEffects` already degrades its armament. **`AttackGarrisoned` complies**
— it applies no such penalty and carries a matching comment at `:288-291`. **[V]** Suppression appears
only as *recall* (`:713-723`). This is correct and should stay.

**Consistency.** All four families carry the full quartet; no actor has a partial stack. **[V]**
But they are not uniform, and one asymmetry is silent: **GTWR/PBOX/HBOX omit `RubbleProtection`
entirely**, inheriting the C# default of 30 (`GarrisonProtection.cs:30`). It happens to match
`^CivBuilding`'s explicit 30, so today the behaviour is identical and the difference is invisible —
which is exactly what makes it a drift hazard if the default ever changes. **[V]**

**Dead duplicated geometry.** Every actor states its ports **twice** — in `GarrisonManager.Ports` and
again in `AttackGarrisoned.PortOffsets/PortYaws/PortCones` (e.g. `civilian.yaml:70-110` vs `:125-127`).
The `AttackGarrisoned` copy is only a fallback for actors with no `GarrisonManager`
(`AttackGarrisoned.cs:33-40`, `:55-56`, `:180-181`), so it is inert on all 41. **[V]** Two sources of
truth, one silently ignored, currently in sync by luck.

---

## 7. What only a run can answer

| # | Question | Scenario | Observation that decides it |
|---|---|---|---|
| Q1 | Does the `V19.Husk` crash actually fire? | Place `V19.Husk` in a test map, load it. | Game throws `InvalidOperationException: Actor V19.Husk does not have trait of type OpenRA.Traits.IHealth` on world load. A clean load **refutes** §2a. |
| Q2 | Does an indestructible `MustBeDestroyed` GTWR/PBOX stall victory? | Skirmish, one side owning only a GTWR, kill everything else. | Game either declares a winner or hangs with no victory. A declared winner refutes the §2b `MustBeDestroyed` concern. |
| Q3 | **Can a hostile occupant evacuate?** (the one gap I could not close statically) | Two hostile infantry ordered into the same neutral house on the same tick; after both are in, the non-owner selects the house and tries Unload. | Whether an Unload order is offered/accepted for a non-owned building. If refused, B's men are permanently trapped and §3 escalates from oddity to serious bug. |
| Q4 | Does the hostile occupant actually fire under the owner's control? | Same setup; give the owner a target in a port arc. | Whether B's rifleman muzzle-flashes at A's target. Confirms or refutes the `[I]` in §3c. |
| Q5 | Does the building defect when the owner's men die? | Same setup; kill only A's occupant. | Whether the house recolours to B. Would confirm `CheckOwnershipAfterExit` `:325-329` end-to-end. |
| Q6 | Does the AI garrison in a real match? | Skirmish vs `@stable` with civilian houses near the AI base. | Any AI infantry entering a house within ~25 cells. Settles whether `RequireBelievedThreat` ever passes in practice. |

Q3 is the highest-value run and the cheapest — it needs one map and no instrumentation.

---

## 8. Prioritized suggestions

Effort: **S** ≈ under an hour · **M** ≈ half a day · **L** ≈ multi-day.

### P1 — correctness

| # | Type | Item | Effort | Evidence that settles it |
|---|---|---|---|---|
| 1 | **BUG** | **Close the hostile-entry race** (§3). Add an `ICargoCanLoadFilter` to `GarrisonManager` rejecting passengers not allied-or-neutral with the building's *current* owner. The extension point already exists and `SupplyProvider` shows the pattern; this is the intended seam. Re-checking in the activity would be the wrong layer. | M | Run Q3/Q4/Q5. A RED first: assert two hostile soldiers both load today, then that the second is refused. |
| 2 | **BUG** | **`CheckOwnershipAfterExit` transfers to any player, not any ally** (`GarrisonManager.cs:325-329`) — code and its own comment disagree. Filter `remainingOwners` by relationship to the current owner. Fixing #1 makes this unreachable, but it should not depend on #1 to be correct. | S | Code read is sufficient; the comment at `:324` is the spec. |
| 3 | **BUG** | **`V19.Husk` throws on creation** (§2a). Minimal fix: add `-Cargo:`, `-GarrisonManager:`, `-GarrisonProtection:`, `-AttackGarrisoned:`, `-WithGarrisonDecoration:` to the husk — a wreck should not be garrisonable. Then separately change `GarrisonProtection.cs:55` to `TraitOrDefault<IHealth>()` so its two existing null guards stop being dead code, and add `Requires<HealthInfo>` so lint catches the next one. | S | Run Q1. **Note:** the removals must match existing keys exactly or MiniYaml throws — `civilian.yaml:439-440` documents that trap for `@CriticalDamage`. |
| 4 | **BUG/DESIGN** | **Decide what `Indestructible` is for** (§2b). It is defaulted-on globally with no override and no occupancy gate, which silently kills `EjectOnDeath`, both `Explodes`, all three `SpawnActorOnDeath` husks, and `INotifyKilled`. Either that is the design — in which case delete the dead traits and say so — or it is an accident, in which case it needs a real decision. **This is a user question, not a worker's call.** | S to ask, L to change | Run Q2 for the `MustBeDestroyed` half. The rest is a design ruling. |
| 5 | **BUG** | If #4 lands on "destructible": `damageToDeal` at `:1391` is exactly `soldierHealth.MaxHP` on the `Kill()` path, so the "survive with proportional damage" code always kills. Scale it, or accept and delete the pretence. | S | Arithmetic in §2b is verified; a run only confirms. |

### P2 — legibility

| # | Type | Item | Effort | Evidence |
|---|---|---|---|---|
| 6 | **IMPROVEMENT** | **Show the enemy that a building is occupied** (§4). Add `ValidRelationships: Ally, Enemy, Neutral` to `WithGarrisonDecoration` at `civilian.yaml:121-123`, or a reduced enemy-facing variant. This is the single highest-value change in the audit: the readout is already built and is currently shown only to the player who does not need it. Fog gating at `WithDecorationBase.cs:152-153` stays and keeps it honest. | S | Screenshot pair from a two-player observer view — a `SCREENSHOT.md` job, not an autotest. |
| 7 | **IMPROVEMENT** | **Show the denominator.** `WithGarrisonDecoration` draws only filled slots (`:253-260`), so 3-of-10 and 3-of-4 look identical. `Info.EmptySequence` already exists (`:337`) on an unreachable branch — wire it by rendering `MaxWeight`-derived empty slots. | S | Screenshot at 1, 5 and 10 occupants. |
| 8 | **IMPROVEMENT** | **Mark garrisonable buildings before the player probes.** Today the only signal is a cursor change requiring infantry already selected. A subtle idle decoration, or a highlight while infantry are selected, would make the mechanic discoverable at all. | M | Screenshot; genuinely a taste call — worth asking the user before building. |
| 9 | **IMPROVEMENT** | **Delete `Inherits@CargoPips`** (`civilian.yaml:68`). Provably a no-op on all 41 actors (`WithCargoPipsDecoration.cs:70`, `:97`) and actively misleading. **Careful:** the template also supplies `Cargo:` keys (`LoadedCondition`, `NoUnloadNotification`, `defaults.yaml:947-953`) — removing the inherit without re-stating those would silently break the `loaded` condition that gates `Targetable@WhenGarrisoned`. Verify before cutting. | S | YAML read plus a lint run. |

### P3 — consistency and hygiene

| # | Type | Item | Effort | Evidence |
|---|---|---|---|---|
| 10 | **IMPROVEMENT** | **De-duplicate `GetCurrentProtection()`** (`GarrisonProtection.cs:63-74` vs `:91-101`) — two verbatim copies, the public one uncalled by the other. Extract one helper. Directly the `feedback_duplication_vs_verification` pattern. | S | Code read; a unit test over the tier function would be cheap and there is none. |
| 11 | **IMPROVEMENT** | **State `RubbleProtection` explicitly on GTWR/PBOX/HBOX** (`sd.yaml:154-156`, `:239-241`, `:329-331`). Currently silently inherits the C# default 30. Same value today; a drift hazard tomorrow. | S | YAML read. |
| 12 | **IMPROVEMENT** | **Remove the dead `AttackGarrisoned.PortOffsets/PortYaws/PortCones` blocks** (e.g. `civilian.yaml:125-127`) — inert on all 41 actors and a second source of truth for port geometry. | S | `AttackGarrisoned.cs:33-40`, `:180-181`. |
| 13 | **IMPROVEMENT** | **Smooth the HP 2→1 protection cliff** (70→30 for `^CivBuilding`). Interpolate into `RubbleProtection` over the last few HP instead of stepping. Only matters once §2b resolves — a building pinned at 1 HP forever is currently the *normal* end state. | M | Combat-sim per `BALANCE.md`. |
| 14 | **IMPROVEMENT** | **Test `GarrisonBotModule`** — currently zero coverage (`PoiGarrisonTest.cs` tests a different module's arithmetic). At minimum a unit test over the candidate filter. | M | NUnit; uncontended, runnable by any worker. |
| 15 | **QUESTION** | **Should a 1×1 desert hut hold the same 10 men and 8 firing ports as a large house?** All 38 civilian actors are mechanically identical regardless of footprint (§1c). Consistent by construction, possibly wrong by design. User call. | — | Design ruling. |

### Deliberately not proposed

- **A garrison-side suppression fire penalty** — forbidden by the `PITFALL` at
  `GarrisonManager.cs:94-98`; it would double-apply `^SuppressionEffects`.
- **Folding `CanEnter` into `IsCorrectCargoType`** — ruled against by the user on 2026-08-30
  (`Passenger.cs:116-126`), "an order must never silently become a move order".
- **Gating the AI garrison away from `@stable`** — `CLAUDE.md` settles this: never build a gate whose
  only purpose is to withhold a fix from `@stable`.

---

## 9. Verification ledger

**Verified by reading code:** the census and all its line numbers; the six-trait stack; `DynamicOwnership`
and both ownership-transfer sites; `Indestructible` and its total absence from YAML; the `Kill()`
bypass and the `damageToDeal` arithmetic; the complete absence of a relationship check across all
four entry stages; `ICargoCanLoadFilter`'s sole implementor; the absence of an owner filter in
`FindBestShelterSoldier`; `WithDecorationBase.ValidRelationships` defaulting to `Ally` and not being
overridden; `WithCargoPipsDecoration`'s early return; `GarrisonPanelLogic`'s chrome wiring and its
`LocalPlayer` filter; `GarrisonBotModule` reaching both bot profiles; `GarrisonProtection`'s
arithmetic, tier interpolation and `MinPassThrough` discard; `AttackGarrisoned` firing the soldier's
own armament and complying with the suppression PITFALL.

**Inferred, not verified:** that `MustBeDestroyed` on an indestructible structure stalls victory
(static reasoning only — Q2); that a hostile port occupant actually lands shots for the building's
owner (owner filter verified absent, shot not traced — Q4); `OwnerLostAction`'s behaviour with a
non-empty `Cargo` (§2d); that `RenderSprites: Palette: player` is the operative cross-player
ownership cue (standard OpenRA remap, not traced); that hostile co-garrison is unintended (three
converging signals, no ruling in tree).

**Could not settle statically:** whether a non-owner can issue `Unload` (§3c, Q3) — this is an engine
selection/order-pipeline question, and the garrison code contributes no predicate either way. It is
the gap that decides how serious §3 is.

**Ran:** no game, no `utility.sh --check-yaml`, no `make test` — all per dispatch constraints. **No
YAML or C# was modified by this audit**, so no lint exposure was created.
