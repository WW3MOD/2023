# Garrisonable civilian structures — end-to-end audit (second pass)

**Date:** 2026-09-15 · **Base:** `main @ 0a94c684` (`git rev-parse --short main`, verified) ·
**Branch:** `wt/civ-garrison`
**Prior pass:** `WORKSPACE/garrison-audit.md` (2026-09-01, `wt/garrison-audit @ f911b6da`). This pass
re-derives its live claims against current code and does **not** repeat findings that have since been
fixed or ruled on.

Claims are marked **[V]** verified by reading the code that proves it, or **[I]** inferred.

---

## 0. What has changed since the 2026-09-01 pass

Five of that audit's fifteen proposals have landed, and one has been ruled on by the user. Re-checked
individually rather than trusted:

| Prior item | Status now | Evidence |
|---|---|---|
| P1 #4 — decide what `Indestructible` is for | **RULED, leave as-is.** `EjectOnDeath`, both `Explodes`, every `SpawnActorOnDeath` husk and `INotifyKilled` stay permanently unreachable, **deliberately** | User ruling 2026-09-01, recorded in manager decision `31-garrison-destructibility-user-ruled-leave-as-is` |
| P3 #13 — HP 2→1 protection cliff | **FIXED.** `CriticalProtection` re-cast as the zero-HP intercept and lowered 70 → 35, spreading the drop across the bar | `civilian.yaml:124-134`; commit `e24e1edb` |
| P3 #11 — `RubbleProtection` implicit on GTWR/PBOX/HBOX | **FIXED.** All three now state it explicitly, and got the same `CriticalProtection` spread (50/50/48) | `structures-defenses.yaml:161-165`, `:255-259`, `:351-355` |
| — `Cargo` emergency bail on buildings | **RULED + FIXED.** `EmergencyBailDamageState: Dead` with the user's reasoning in-tree | `civilian.yaml:66-76`; commits `481faa1f`, `15ae7d18` |
| P3 #10 — `GetCurrentProtection` duplicated | **FIXED HERE** (§2, commit `3465099f`) | — |
| P1 #2 — `CheckOwnershipAfterExit` transfers to any player | **STILL LIVE** | `GarrisonManager.cs:333-336` |
| P1 #3 — `V19.Husk` throws on creation | **HALF-FIXED HERE**; YAML half ranked at #6 | `civilian.yaml:444-450` |
| P2 #6, #7, #9, P3 #12, #14 | **STILL LIVE** | ranked below |

`wt/garrison-panel` (`08c8cce0`, unmerged) fixes `GARRISON_PANEL` never becoming visible — the
`LogicTicker` that carried the only `panel.Visible = true` write was itself a child of the hidden
panel, so it never ticked. Read; not re-found. §5 audits what that panel will now actually show.

**The census.** 38 actors inherit `^CivBuilding`, plus `GTWR`/`PBOX`/`HBOX` declaring the stack
independently = **41 garrisonable actors**, not the brief's ~44. **[V]** The overcount of 3 comes from
`civilian.yaml:145` (`^DesertCivBuilding` — a template, not an actor) and `:210` (a commented-out
inherit). The brief's `Cargo` citation is off by one file revision: the block is `civilian.yaml:58-76`; the brief's
`:58-67` starts right and stops nine lines early, before `EmergencyBailDamageState`. **[V]**

---

## 1. Headline: against a bot, a shelter occupant cannot be killed at all

This is the most consequential new finding, and it falls out of three verified facts that have never
been put next to each other.

**(a) A garrisoned building can only be force-fired.** `^CivBuilding` carries two mutually exclusive
`Targetable` traits: the base one gated `!loaded`, and `Targetable@WhenGarrisoned` gated `loaded` with
`RequiresForceFire: true` (`civilian.yaml:17-23`). `Target.RequiresForceFire` returns true when every
*enabled* targetable requires it (`Target.cs:131-151`), and while loaded exactly one is enabled — so it
does. `AttackBase` then rejects the target outright for any non-force attack:
`if (!forceAttack && (... || target.RequiresForceFire)) return false;` (`AttackBase.cs:442`). **[V]**

**(b) No bot module ever force-fires an actor.** Across all of `Traits/BotModules/`, the only
`ForceAttack` order issued is `DroneOperatorBotModule.cs:686`, and it targets a **cell**, not an actor.
The one actor-directed attack order is `PoiOffensiveBotModule.cs:4720`, which issues `"Attack"` —
`forceAttack: false`. **[V]**

**(c) Shelter occupants are only reachable through damage to the building.** They are inside `Cargo`,
hence out of world and not targetable by anything. The sole path to them is
`GarrisonProtection.Damaged`, an `INotifyDamage` on the building that forwards a slice of the
building's incoming damage to one random shelter occupant (`GarrisonProtection.cs:91-125`). **[V]**

Put together: **a bot can never deal damage to a garrisoned building, so it can never deal any damage
to anyone sheltering inside one.** Port soldiers remain killable — `GarrisonPortOccupant` sets
`RequiresForceFire => false` (`:89`) and is arc-gated (`:91-122`), so units auto-engage the men at the
ports normally. But the reserve never takes a scratch, and because the building's HP never falls, the
protection curve never degrades either.

The same mechanism has a milder human-facing half, already noted in the prior pass: a player on
attack-move walks past a building that is shooting at them and does not return fire. Clearing a
garrison is a manual force-fire gesture that the game never teaches.

**Severity: high.** It is not a crash and not an exploit either player can trigger on demand, but it
means the AI cannot contest the mechanic at all, which silently caps how much garrison play the
shipped bots can express.

---

## 2. Fixes made on this branch

Four commits, each self-contained. Nothing behavioural was changed without the reasoning going into
the commit message.

| Commit | What |
|---|---|
| `5a9791bc` | **`GarrisonProtection` no longer throws on an actor that removed `Health`.** `Created` called `self.Trait<IHealth>()` unguarded (pre-fix `:61`; now `TraitOrDefault` at `:70`); `V19.Husk` inherits the garrison stack and then removes `Health` (`civilian.yaml:445-450`), so constructing one throws `InvalidOperationException` out of `TraitDictionary.Get`. `GarrisonProtectionInfo` requires `GarrisonManagerInfo` and `CargoInfo` but **not** `HealthInfo`, so no lint can see the combination. The trait's author expected null — both `GetCurrentProtection` and `Damaged` open with a `health == null` guard, and both were dead code because the `Created` line threw first. Now `TraitOrDefault<IHealth>()`. **[V]** |
| `3465099f` | **One copy of the protection curve, not two.** `Damaged` carried a verbatim reimplementation of `GetCurrentProtection`'s body and never called it — so the panel readout came from one copy and the damage that actually lands came from the other. Behaviour-preserving: `Damaged` already early-returns on the only two cases `GetCurrentProtection` folds to 0. Closes prior P3 #10. **[V]** |
| `22f409d6` | **Garrisoned mortar and AT infantry stop being shootable through the wall.** See §3 — the one live defect found this pass that changes what a player experiences. |
| `0139ae1b` | **`Cargo.Neutral`: say that it does nothing.** `[Desc]`-only. See §4. |

---

## 3. The one live defect fixed: `^MT` / `^AT` defeat the port firing-arc protection

`GarrisonPortOccupant` exists to make a man at a firing port shootable **only from within that port's
arc** (`GarrisonPortOccupant.cs:91-122`), and its `[Desc]` states the precondition in as many words:
*"The regular Targetable trait should have RequiresCondition: !garrisoned-at-port."* `^Infantry`
honours it — `Targetable` is gated `!parachute && !garrisoned-at-port` (`infantry.yaml:62-63`), as is
`Targetable@Heal` (`:65-66`). **[V]**

**`^MT` and `^AT` do not.** Both declare a third targetable, `Targetable@HighPriority`, with **no
`RequiresCondition` at all** (pre-fix `infantry.yaml:1618-1619` and `:1752-1753`). **[V]**

`Actor.IsTargetableBy` ORs across every targetable and returns on the first `true`
(`Actor.cs:671-678`), and the base `Targetable.TargetableBy` answers on trait-enabled alone. So that
one ungated trait said "yes" for every attacker and `GarrisonPortOccupant.TargetableBy` was never
reached to say no. Two effects, both confined to `MT` and `AT` (the only actors inheriting these
templates, `infantry.yaml:1673`, `:1819`), and both reachable in ordinary play since both units are
`CargoType: Infantry` through `^CamoSoldier` → `^Soldier` → `^Infantry` (`:170`): **[V]**

1. a garrisoned mortar or AT man was shootable from **outside** his port's arc, including from
   directly behind the building — unlike every other infantry type;
2. `GetEnabledTargetTypes` kept unioning `HighPriorityInfantry` while he was inside
   (`Actor.cs:660-668`), so auto-target bands naming that type actively **preferred** the man in the
   building over targets in the open.

Fixed by adding `RequiresCondition: !garrisoned-at-port`, matching the base template exactly. The
condition is granted on the same template (`ExternalCondition@GarrisonPort`, `infantry.yaml:74-75`),
which MT and AT inherit — so this adds a consumer of an already-granted condition and cannot orphan
anything. **[V]**

**Deliberately not folded in:** those same two blocks also lack `!parachute`, which the base
`Targetable` has. That is a second, independent behavioural change and is ranked at #11 instead.

---

## 4. `Cargo.Neutral` is dead config that describes a real behaviour

`CargoInfo.Neutral` is declared at `Cargo.cs:29-30` and **read nowhere in the engine** — no reference
in `Cargo.cs`, the `Garrison` traits, `Passenger`, or anywhere else. **[V]** Its `[Desc]` nonetheless
promised *"Should this actor turn nutral when not loaded? For civilian buildings"* (sic), and all four
garrisonable families set it: `civilian.yaml:59`, `structures-defenses.yaml:120`, `:224`, `:323`. **[V]**

What makes it worth a commit rather than a shrug is that **the described behaviour genuinely exists**
— delivered by `GarrisonManager.DynamicOwnership` through `CheckOwnershipAfterExit`
(`GarrisonManager.cs:306-337`). A reader who wants a garrisoned building to stay owned when it empties
will set `Neutral: false`, watch nothing happen, and have no way to discover why. The `[Desc]` now says
it is unimplemented and names the trait that actually decides. Deleting the field and its four YAML
setters is the better end state and is ranked at #9, not taken here — it is four YAML edits whose lint
this branch is not permitted to run.

---

## 5. The panel, audited against what `wt/garrison-panel` will make it show

The content is good: per-port occupant name, ammo, live suppression level (`PINNED n` / `SUPP n`) and a
derived cover figure read from the soldier's own enabled `DamageMultiplier` traits rather than assumed
(`GarrisonPanelLogic.cs:158-204`). The header carries `GARRISON [Shield: n%]` from
`GetCurrentProtection` (`:99-104`) — which commit `3465099f` above has now made the same number the
damage path uses.

Three gaps, in descending order:

**5a. The panel shows at most 4 of up to 10 reserve occupants.** The shelter loop runs `for (var i = 0;
i < 4; i++)` over `RESERVE_LABEL_{i}` (`GarrisonPanelLogic.cs:78-89`), and `ingame-player.yaml` declares
exactly `RESERVE_LABEL_0..3` (`:987-1010`). **[V]** `^CivBuilding` is `MaxWeight: 10` with 8 ports
(`civilian.yaml:61`, `:79-119`). In the steady state that is fine — 8 at ports leaves 2 in reserve. It
fails precisely when the panel matters most: soldiers recalled under fire go back to the shelter and
cannot re-man a port until suppression decays below `SuppressionRedeployThreshold`, so a garrison being
suppressed is exactly the state that can hold up to 10 men in shelter at once, and the player sees 4.
**[I]** on the 10-at-once case — I verified capacity, port count and the recall path, not a live count.
Note the panel body already runs to y=216 inside a 240-high container (`:1011-1014`), so six more rows
needs the container resized, not just declared — effort M, not S.

**5b. The panel is owner-only, which is correct, but nothing replaces it for the enemy.** See #2 below.

**5c. `chrome/garrison-panel.yaml` remains a dead near-duplicate** — not referenced from `mod.yaml`
(only `ingame-player.yaml` is, `:193`). **[V]** Already recorded at `WORKSPACE/bugs/discovered.md:3398`;
`wt/garrison-panel` edits it, which will make the duplicate look maintained. Not re-filed.

---

## 6. Balance: the 38 civilian buildings are two silently different classes

**22 of the 38 declare no `Health` and no `Armor` of their own** and inherit `HP: 60000` /
`Armor: Type: Concrete` from `^TechBuilding` (`structures.yaml:217-221`). The other 16 declare both.
**[V]**

| Class | Actors | HP | Armor |
|---|---|---|---|
| Inherited (undeclared) | `V12`, `V13`, `V19`, `V19.Husk`, **`V20`–`V37`** (all 18 desert houses) | 60000 | **Concrete** |
| Declared | `V09`, `ASIANHUT`, `SNOWHUT`, `WINDMILL` | 25000 | Light |
| | `V08` | 40000 | Light |
| | `V05`, `V07` | 50000 | Light |
| | `V04`, `V06` | 65000 | Light |
| | `V01`, `V02` | 75000 | Light |
| | `V03` | 90000 | Light |
| | `V11` | 25000 | Medium |
| | `V10`, `LHUS` | 50000 | Medium |
| | `RUSHOUSE` | 120000 | Heavy |

The armour half only bites for one family of weapons, which is what makes it easy to miss: **the only
warheads in the mod that declare a `Versus` entry for `Concrete` or `Light` are the explosive and
nuclear ones** — `IskanderExplosion`, `HIMARSExplosion`, `VolatileLoad1`–`8`, `OreshnikRVExplosion` and
every `Nuke*`/`Atomic` warhead. **[V]** Everything else leaves both at the 100% default, so ordinary
fire sees no difference at all. For the ordnance a player actually reaches for to break a garrison:

- `HIMARSExplosion` / `IskanderExplosion` / `VolatileLoad*`: `Light: 80`, `Concrete: 25` — **3.2×**
- `Nuke*` primary warhead: `Light: 120`, `Concrete: 20` — **6×**

Compounded with the HP split, rubbling a garrisoned desert house (60000 HP, Concrete) with HIMARS
takes **≈7.7×** the delivered damage of rubbling a garrisoned `WINDMILL` (25000 HP, Light) —
`60000/25000 × 80/25`. **[V]** (arithmetic; `Indestructible` clamps both at 1 HP rather than killing
them, so "rubble" is the terminal state in both cases.)

Nothing in `civilian.yaml` says any of this. The 18 desert houses are not tuned to be tougher — they
are untuned, and `^TechBuilding`'s defaults are what a *tech* building wants, where `Concrete` pairs
with a `Targetable` that most weapons cannot hit at all (`structures.yaml:186-215`). A civilian house
overrides that `Targetable` (`civilian.yaml:17-19`) and so is fully hittable while keeping the armour
class chosen for something unhittable.

**This is the highest-value cheap balance item in the audit** and it is a pure YAML decision — but it
is a *decision*, not a defect, so it is ranked (#3) rather than fixed.

---

## 7. Vision after the vision fix

Garrison vision is delivered by `^StandardVisionWhenLoaded` (`defaults.yaml:156-171`), which re-declares
`^StandardVision`'s `Vision@4`–`@10` bands with `RequiresCondition: loaded`. Combined with the
ownership flip on entry (`GarrisonManager.cs:263-267`), the garrisoning player owns the building and
therefore receives its vision. **[V]** That works, and the mechanic reads correctly now.

**`PBOX` is the odd one out.** `GTWR` (`structures-defenses.yaml:80`) and `HBOX` (`:274`) both carry
`Inherits@DetectionWhenLoaded: ^StandardVisionWhenLoaded`; `PBOX` does not (`:176-195`). **[V]** Both
lines were added together in `4eed77af` ("Progressive fog (#5)", 2023-07-17) and `PBOX` was never given
one — an omission at the time, not a later regression. **[V]**

The consequence is not "PBOX is missing vision" — it is the reverse. All three inherit
`^StandardVision` unconditionally from `^Defense` (`structures-defenses.yaml:5`); the `WhenLoaded`
template's job is to **gate that vision behind occupancy**. So today an **empty `GTWR` or `HBOX` gives
its owner no vision at all**, while an empty `PBOX` gives full standard vision. For a structure named
Guard Tower that is a strange resting state, and it is not obvious which of the two behaviours was
intended. Ranked at #7 as a question, deliberately not "fixed" in either direction.

---

## 8. Two arc formulas for one geometry — latent, not live

The port cone is computed twice, in two different ways:

- `GarrisonManager.IsTargetInPortArc` (`:1160-1181`) — who a port can **shoot**. Reads the building's
  facing: `var bodyYaw = self.TraitOrDefault<IFacing>()?.Facing ?? WAngle.Zero; var portYaw = bodyYaw +
  port.Yaw;`
- `GarrisonPortOccupant.TargetableBy` (`:105-121`) — who can **shoot** the port occupant. Uses
  `port.Yaw` raw, with **no `bodyYaw` term at all**.

**[V]** The normalisation halves are equivalent (`Math.Min(leftTurn, rightTurn) <= Cone` versus a
signed wrap to ±512 compared against ±`Cone`; `WAngle.Angle` is always in `[0, 1024)`,
`WAngle.cs:28-33`). The facing term is the whole difference.

**It is inert today.** No garrisonable actor carries an `IFacing` trait — none of the 38 civilian
actors has `Mobile`/`Turreted`/`Aircraft`/`Husk`, and of the defences only `CRAM`, `AGUN`, `SAM`,
`HSAM` and `GUN` have `Turreted`, none of which is garrisonable. **[V]** So `bodyYaw` is `WAngle.Zero`
everywhere and both formulas agree.

It is still worth recording, because the failure mode is nasty and silent: give any garrisonable actor
a facing and a port soldier can shoot in one arc while being shootable from a different one. This is
the third instance of the same pattern in this subsystem — port geometry is *also* stated twice in YAML
(`GarrisonManager.Ports` and the inert `AttackGarrisoned.PortOffsets/PortYaws/PortCones`,
`civilian.yaml:79-119` vs `:141-143`), and `GarrisonProtection` had two copies of its curve until
commit `3465099f` above.

---

## 9. Ranked issue list

Severity: **high** = caps or breaks the mechanic · **med** = wrong but survivable · **low** = hygiene.
Effort: **S** ≈ under an hour · **M** ≈ half a day · **L** ≈ multi-day.
Ordered by value-per-effort against release, per the user's standing instruction.

| # | Sev | Title | Proposed shape | Effort |
|---|---|---|---|---|
| 1 | **high** | **Bots can never damage a garrisoned building, so shelter occupants are invulnerable to the AI** (§1) | Two candidate shapes, and the cheap one is probably right. **(a)** Drop `RequiresForceFire: true` from `Targetable@WhenGarrisoned` (`civilian.yaml:20-23`) — a garrisoned building becomes auto-engageable by everyone, human and bot, which also fixes the "attack-move walks past" half. Risk: units now auto-acquire an *indestructible* building and can waste fire on it forever, which is exactly what the flag was presumably added to prevent. **(b)** Keep the flag and give one bot module a force-fire path against buildings carrying `GarrisonManager` with live occupants. Strictly better behaviour, strictly more code. Recommend costing (a) first with a combat-sim pass, because it is a one-line YAML change and its failure mode is visible immediately. | S to try (a), M for (b) |
| 2 | **high** | **The enemy is never shown that a building is occupied** (prior P2 #6, still live) | `WithGarrisonDecoration` inherits `WithDecorationBaseInfo.ValidRelationships`, which defaults to `Ally` (`WithDecorationBase.cs:107-108`) and is not overridden (`civilian.yaml:136-138`). Because garrisoning transfers ownership, the opponent always evaluates to `Enemy` and every pip is suppressed — the readout is built, correct, and shown only to the player who already knows. Add `ValidRelationships: Ally, Enemy, Neutral`, or a reduced enemy-facing variant. Fog gating at `WithDecorationBase.cs:152-153` stays and keeps it honest. Still the single highest-value legibility change in the subsystem. | S |
| 3 | **med** | **22 of 38 civilian buildings are an untuned second class: 60000 HP / `Armor: Concrete` inherited from `^TechBuilding`** (§6) | Decide the intended armour class for a civilian house and state it on `^CivBuilding` so nothing inherits the tech-building default by accident; then decide whether the 18 desert houses should have the temperate range's per-actor HP or one shared value. Pure YAML. A ~7.7× spread in effort-to-rubble between a desert house and a windmill under HIMARS is almost certainly not a designed difference. | S to decide, M to retune with combat-sim |
| 4 | **med** | **`CheckOwnershipAfterExit` transfers to *any* remaining occupant, not any *ally*** (prior P1 #2, still live) | `GarrisonManager.cs:333-336` reads `remainingOwners.First()` with no relationship filter, while its own comment two lines up says *"but an ally does → transfer"* (`:334`). Code and spec disagree; the comment is the spec. Filter `remainingOwners` by relationship to the current owner. Independent of #5 and should not wait on it. | S + a test |
| 5 | **med** | **Two hostile players can garrison the same building** (prior §3, unresolved) | The relationship gate appears exactly once, at targeting (`EnterAlliedActorTargeter.cs:49-54`, which admits allied **or neutral**); nothing re-checks through `Passenger.ResolveOrder`, `RideTransport.TryStartEnter`/`OnEnterComplete` or `Cargo.CanLoad`. Fix shape unchanged from the prior pass: an `ICargoCanLoadFilter` on `GarrisonManager` rejecting passengers not allied-or-neutral with the building's *current* owner — the extension point exists and `SupplyProvider` is the worked example. **Blocked on run Q3** (below): whether a non-owner can evacuate decides if this is an oddity or a men-permanently-lost bug. | M |
| 6 | **med** | **`V19.Husk` still carries a garrison stack it cannot support** (prior P1 #3, engine half fixed here) | Commit `5a9791bc` stops the throw, but a wreck should not be garrisonable at all. The YAML half is **not as cheap as it looks and that is the finding**: removing `Cargo` from the husk orphans every consumer of the `loaded` condition that `^CivBuilding` supplies — `Targetable@WhenGarrisoned` (`civilian.yaml:20-23`) and all seven `Vision@4`–`@10` bands from `^StandardVisionWhenLoaded` — each of which then becomes a condition consumed but never granted. Whoever takes it must remove those too and must run the YAML lint, which this branch may not. | S once lint is available |
| 7 | **med** | **An empty `GTWR` or `HBOX` gives its owner no vision; an empty `PBOX` gives full vision** (§7) | `PBOX` never received `Inherits@DetectionWhenLoaded: ^StandardVisionWhenLoaded` when `GTWR` and `HBOX` did (`4eed77af`, 2023). Decide which is intended and make all three match. A guard tower that is blind until garrisoned is defensible as a design, but it should be a decision. | S |
| 8 | **med** | **The panel shows at most 4 of up to 10 reserve occupants** (§5a) | Loop bound is 4 (`GarrisonPanelLogic.cs:78`) against `MaxWeight: 10`. The panel body already fills its 240-high container to y=216, so this needs the container resized as well as rows added — or, cheaper and arguably better, collapse the reserve list to one summary row (`[S] 7 in reserve, 2 pinned`) that cannot overflow at any capacity. | M for rows, S for the summary row |
| 9 | **low** | **Delete `Cargo.Neutral` rather than documenting it** (§4) | Commit `0139ae1b` corrects the `[Desc]`; the field is still there to be set. Remove the field (`Cargo.cs:29-30`) and its four setters (`civilian.yaml:59`, `structures-defenses.yaml:120`, `:224`, `:323`). Provably inert — nothing reads it — but it is four YAML edits and wants one lint run. | S |
| 10 | **low** | **Port geometry is stated twice in YAML, one copy inert** (prior P3 #12) | `AttackGarrisoned.PortOffsets/PortYaws/PortCones` (`civilian.yaml:139-143` and the GTWR/PBOX/HBOX equivalents) is only a fallback for actors with no `GarrisonManager` (`AttackGarrisoned.cs:33-40`, `:180-181`), so it is dead on all 41. Two sources of truth for one geometry, in sync by luck. Delete the dead copy. | S |
| 11 | **low** | **`^MT` / `^AT` `Targetable@HighPriority` also lacks `!parachute`** (§3) | The base `Targetable` is gated `!parachute && !garrisoned-at-port`; commit `22f409d6` added only the second conjunct, deliberately. A mortarman is still targetable through `Targetable@HighPriority` while parachuting, unlike every other infantry type. Same one-line shape, but it is an independent behavioural change and wants its own measurement. | S |
| 12 | **low** | **`GarrisonPortOccupant.TargetableBy` omits the building facing that `IsTargetInPortArc` applies** (§8) | Inert today — no garrisonable actor has an `IFacing` trait — so this is a trap, not a bug. Extract one shared arc helper taking `(bodyYaw, portYaw, cone, targetYaw)` and call it from both sites. Cheap now, and the third instance of this exact duplication pattern in this subsystem. | S |
| 13 | **low** | **`Inherits@CargoPips` is a no-op on all 41 actors** (prior P2 #9) | `WithCargoPipsDecoration` early-returns when the actor also has `WithGarrisonDecoration` (`:70`, `:97`), which all 41 do. The inherit is not harmless clutter — it is the line a future reader trusts when asking "does fullness render?". **Careful:** the same template also supplies `Cargo.LoadedCondition` and `NoUnloadNotification` (`defaults.yaml:1016-1023`); removing the inherit without re-stating those silently breaks the `loaded` condition that gates `Targetable@WhenGarrisoned`. Verify before cutting. | S |
| 14 | **low** | **No denominator anywhere in the occupancy readout** (prior P2 #7) | `WithGarrisonDecoration` draws filled slots only, so 3-of-10 and 3-of-4 are identical. `Info.EmptySequence` already exists on a branch made unreachable by `slotCount == soldiers.Length`. Wire it from `MaxWeight`. Pairs naturally with #2 — do them together or not at all. | S |
| 15 | **low** | **`GarrisonBotModule` has zero test coverage** (prior P3 #14) | `PoiGarrisonTest.cs` tests a different module's arithmetic and never touches it. Module is live on both bot profiles (`ai.yaml:1511-1512`, `RequiresCondition: enable-ai-any`). At minimum a unit test over the candidate filter. Uncontended, runnable by any worker. | M |
| 16 | — | **Question: should a 1×1 desert hut hold the same 10 men and 8 firing ports as a large house?** | No descendant of `^CivBuilding` overrides any garrison trait, so all 38 are mechanically identical regardless of footprint. Consistent by construction, possibly wrong by design. User call; listed so it is not silently inherited. | — |

### Deliberately not proposed

- **Anything that reopens garrison destructibility** — user ruled leave-as-is on 2026-09-01. The
  unreachable `EjectOnDeath` / `Explodes` / `SpawnActorOnDeath` / `INotifyKilled` content is
  *deliberate* dead code, not a bug, and should not be re-filed.
- **A garrison-side suppression fire penalty** — forbidden by the PITFALL at
  `GarrisonManager.cs:94-98`; it would double-apply `^SuppressionEffects`.
- **Gating the AI garrison away from `@stable`** — settled policy in `CLAUDE.md`.

---

## 10. Files touched, and what lint would say

| File | Change | Lint exposure |
|---|---|---|
| `engine/OpenRA.Mods.Common/Traits/Garrison/GarrisonProtection.cs` | `Trait<IHealth>` → `TraitOrDefault<IHealth>`; `Damaged` calls `GetCurrentProtection()` | None — C# only. Covered by `make check` (Debug + analyzers) and `dotnet test`, both run below. |
| `engine/OpenRA.Mods.Common/Traits/Cargo.cs` | `[Desc]` text only | None. |
| `mods/ww3mod/rules/ingame/infantry.yaml` | `RequiresCondition: !garrisoned-at-port` added to two `Targetable@HighPriority` blocks | **Adds a consumer of an already-granted condition.** `garrisoned-at-port` is granted by `ExternalCondition@GarrisonPort` on `^Infantry` (`:74-75`), which both `^MT` and `^AT` inherit via `^CamoSoldier` → `^Soldier` → `^Infantry`, so `CheckConditions` has a granter to find on both actors. No key is removed, so no `-Key:`-not-found risk. **I could not run the YAML lint** — this branch is barred from `./utility.sh --check-yaml` and `make test` — so this is reasoning, not a green run. It is the single thing in this report most worth the manager verifying. |

No scenario under `tools/autotest/scenarios/` was added or edited, so `tools/lua-gate` has nothing to
check for this branch.

---

## 11. Runs for the manager

I launched nothing. In value order:

| # | Question | Setup | What counts as the answer |
|---|---|---|---|
| **R1** | **Does the `^MT`/`^AT` fix do what it claims, and does it regress anything?** | Garrison an `MT` at a port of a `^CivBuilding`. Position an attacker (a) inside that port's cone and (b) directly behind the building, outside every cone. | **Before:** both attackers engage the mortarman. **After:** only (a) does; (b) has no valid target. A RED first — revert the two lines locally and confirm (b) *does* engage — is what makes this evidence rather than a coincidence. Also confirm a **non**-MT rifleman is unchanged in both positions, and that an MT in the open is still targetable and still high-priority. |
| **R2** | **Q3 from the prior audit, which the user already selected and which has still not been run.** Can a non-owner evacuate men from a building an enemy now owns? | Two hostile infantry ordered into the same neutral house on the same tick; once both are inside, the **non-owner** selects the house and tries Unload. | Whether an Unload order is offered and accepted for a building the player does not own. If refused, B's men are permanently lost and item #5 promotes from oddity to a real bug. One map, no instrumentation. |
| **R3** | **Is §1 true in play — can a bot ever damage a garrisoned building?** | Skirmish vs `@stable`, garrison a civilian house on the bot's attack path with men in **shelter** (more than 8, or eject-to-shelter), and let the bot engage. | Whether the building's HP ever falls below max, and whether any shelter occupant takes a point of damage. If HP never moves, §1 is confirmed end-to-end and item #1 is the release-relevant one. |
| **R4** | **Does the panel actually appear, and what does it show at depth?** (needs `wt/garrison-panel` merged) | Select an owned garrison holding 8 at ports and 2+ in shelter. | Panel visible; port rows correct; **count the reserve rows** — 4 is the predicted cap from §5a. Screenshot job per `SCREENSHOT.md`, not an autotest. |

---

## 12. Verification ledger

**Verified by reading the code:** the census (38 + 3 = 41) and the two lines that make the brief's ~44
an overcount; the `Cargo` block's real line range; `Targetable@WhenGarrisoned`'s `RequiresForceFire` and
`Target.RequiresForceFire`'s all-or-nothing aggregation; `AttackBase.cs:442` rejecting non-force
attacks; the absence of any actor-directed `ForceAttack` in every bot module; `GarrisonPortOccupant`'s
`RequiresForceFire => false` and its arc test; `Actor.IsTargetableBy`'s OR semantics and
`GetEnabledTargetTypes`'s union; the ungated `Targetable@HighPriority` on `^MT`/`^AT` and their lineage
to `^Infantry`; the `garrisoned-at-port` granter; `CargoInfo.Neutral` having no reader and four
setters; the HP/armour split across all 38 actors and `^TechBuilding` as its source; which warheads
declare `Versus` for `Concrete`/`Light`; `PBOX` lacking the vision template and the 2023 commit that
gave it to the other two; both arc formulas and the absence of `IFacing` on every garrisonable actor;
the panel's reserve loop bound and the four declared rows; `GarrisonBotModule` still live on both
profiles; `CheckOwnershipAfterExit` still transferring to `.First()`.

**Inferred, not verified:** that up to 10 men can be in shelter simultaneously under suppression
(capacity, port count and the recall path verified; a live count not observed); that a bot's `"Attack"`
order against a garrisoned building is *selected and then silently discarded* rather than never
selected (the discard at `AttackBase.cs:442` is verified, the bot's target-selection reaching a
garrisoned building is not); that `PBOX`'s missing vision line was an omission rather than a decision
(no ruling in tree, inferred from the two siblings getting it in one commit).

**Not verified, and it matters:** **no YAML lint was run on the `infantry.yaml` change** — barred by
dispatch. The condition-granter reasoning in §10 is a code read, not a green gate.

**Ran:** `make all`, `make check`, `dotnet test` (results in the branch report). **No game launch, no
`run-test.sh`, no `--check-yaml`, no `make test`** — all per dispatch constraints.
