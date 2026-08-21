# `AmmoPool.Essential` — authoring decision for every pool in the game

**Date:** 2026-08-21 · **Branch:** `wt/essential-census` · **Base:** `main @ 697be28e`
**Scope:** authoring only. The `Essential` field is being built on `wt/resupply-tiers`; **nothing in
this document has been applied**, and applying it before that branch lands will fail YAML validation.

> **APPLIED 2026-08-21 on `wt/essential-apply`. The two lines above are superseded.** The mechanism
> landed (`main @ 2d575a8e`) and all 34 flags from §2 are now authored, plus **one** SHORAD flag from
> §3 — **35 `Essential: true` lines total.** The user ruled §3 **option A, Stinger-only**:
> `secondary-ammo` is Essential, the Hellfire is not, on the rule that an AA vehicle flags only its AA
> missiles. Note the arithmetic: §2's 34 never contained a SHORAD pool, so the ruling ADDS one — the
> total is 35, not 34 and not 33.
>
> Three verdicts here rested on premises that the mechanism could have invalidated. All three survived,
> but one of them only by accident:
> - **§1 `^E6` C4 (`:69`)** — the stated safety ("`AutoSeekSupplies.ReturnWhenEmpty: false` … the seek
>   is idle-only") is **wrong**. That field does not gate `AmmoPool.AutoRearmIfDry`, which now also
>   fires from `INotifyAttack` the tick a pool empties. The verdict holds for an unrelated reason: the
>   C4 is spent by the `Demolition`/`Minelayer` traits rather than an Armament, so the attack-path
>   trigger cannot see it. The `^E6` comment rewritten in this commit states the real mechanism; see
>   `WORKSPACE/DISCOVERIES.md`.
> - **§1 / §5 Trap 2 `^E6` SMG (`:68`, `:371`)** — correct, and now **enforced at load time** rather
>   than resting on authoring discipline. Verified RED: setting it fails `--check-yaml`.
> - **§5 Trap 2's premise (`:371`)** — confirmed. A host refills only the pools named in
>   `Rearmable.AmmoPools`, at all three refill sites.

## What the flag means, and what it replaces

Today a unit only goes looking for resupply when **every** pool it carries is empty —
`AmmoPool.AutoRearmIfAllEmpty` early-returns unless `AllPoolsEmpty(ammoPools)`
(`engine/OpenRA.Mods.Common/Traits/AmmoPool.cs:281-283`). That is why a rifleman with a spent
magazine and one unfired RPG round stands in a firefight doing nothing: `AllPoolsEmpty` is false, so
he never asks for ammunition, and his RPG declares `InvalidTargets: Infantry` so it was never
usable against what is in front of him.

`Essential` marks a pool whose emptiness alone should trigger that seek. **Default OFF**, so every
pool not named in the patch below keeps today's behaviour.

### The rule applied throughout

> **A pool is Essential when the unit cannot do its defining job without it.**

That is the principle behind the user's three worked examples, and it is the only rule used here:

| Example | Verdict | Why |
|---|---|---|
| Rifleman (`^E3`) | rifle Essential, RPG not | His job is shooting infantry; the AT round is a bonus. |
| IFV (`bradley`, `bmp2`) | **both** Essential, either triggers | Genuinely dual-role — losing either capability means it should top up. |
| AA vehicle (`tunguska`) | only the SAMs Essential | Its job is shooting aircraft; the cannon is self-defence. |

The Tunguska case is why "primary-ammo is essential" is not a usable default: the Tunguska's cannon
**is** its `primary-ammo` pool. Pool names are not roles — see the traps section.

### Where the flag does nothing

Two whole categories carry pools that `Essential` cannot affect. They are listed for completeness of
the census and given **no verdict**, because a verdict would imply an effect that does not exist:

- **Aircraft (18 pools).** `AutoRearmIfAllEmpty` returns immediately for anything with an
  `AircraftInfo` (`AmmoPool.cs:283`), and `CannotFight` carves them out identically (`:277`). A dry
  airframe recovers through its own idle `ReturnToBase` flow instead. Setting `Essential` on an
  airframe pool changes nothing today, and would only start to matter if that carve-out were lifted.
- **Static defenses (3 pools).** `CRAM`, `AGUN` and `FTUR` are immobile buildings with no
  `Rearmable`. They cannot travel to a host, so "go looking for resupply" has no meaning for them.

---

## 1. Decision table

63 `AmmoPool` declarations, enumerated from `mods/ww3mod/rules/` at `697be28e`. `Ammo` is pool
capacity; `SV` is `SupplyValue` (cost per rearm batch).

### Infantry — `mods/ww3mod/rules/ingame/infantry.yaml` (18 pools, 14 classes)

| Actor | Line | Pool | Ammo | Weapon | Essential | Reason |
|---|---|---|---|---|---|---|
| `^E1` Conscript | 1097 | `primary-ammo` | 100 | `5.56mm.E3` | **YES** | Only pool, only weapon. Reproduces today's behaviour. |
| `^E3` Rifleman | 1178 | `primary-ammo` | 100 | `5.56mm.DMR` | **YES** | User ruling. The rifle is the job. |
| `^E3` Rifleman | 1206 | `secondary-ammo` | 1 | `RPG` | no | User ruling. Opportunistic AT; `InvalidTargets: Infantry`. |
| `^AR` Automatic Rifleman | 1294 | `primary-ammo` | 500 | `5.56mm.AR` | **YES** | Only pool. Suppressive fire is the whole role. |
| `^E2` Grenadier | 1360 | `primary-ammo` | 30 | `GrenadeLauncher.5mag` | **YES** | Only pool, only weapon. |
| `^TL` Team Leader | 1437 | `primary-ammo` | 100 | `7.62mm.DMR` | **YES** | Rifleman precedent — the DMR is his line-combat weapon. |
| `^TL` Team Leader | 1461 | `secondary-ammo` | 6 | `GrenadeLauncher` | no | Rifleman precedent. Grenades augment a rifle role, they are not it. |
| `^MT` Mortar | 1526 | `primary-ammo` | 25 | `60mm_Mortar` | **YES** | Only pool. A mortarman without shells is a $300 walking man. |
| `^SN` Sniper | 1593 | `primary-ammo` | 50 | `7.62mm.Sniper` | **YES** | Only pool. |
| `^AT` AT Specialist | 1667 | `primary-ammo` | 3 | `ATGM` | **YES** | Only pool, and the whole reason the unit exists. |
| `^AA` AA Specialist | 1737 | `primary-ammo` | 3 | `MANPAD` | **YES** | Only pool. Same shape as the Tunguska verdict — the SAM is the job. |
| `^E6` Combat Engineer | 1824 | `primary-ammo` | 100 | `MP5` | **no — forced** | Two reasons, either sufficient. His job (capture / repair / demolition / mine-clear) needs no bullets; and **this pool is not in his `Rearmable.AmmoPools`** (`:1907`), so no host will ever refill it — see Trap 2. |
| `^E6` Combat Engineer | 1852 | `secondary-ammo` | 3 | `secondary` (C4 / AT mine) | **YES** | His one combat-relevant pool and the only one a host refills. Safe because `AutoSeekSupplies.ReturnWhenEmpty: false` on `^E6` (`:1913-1914`) — the seek is idle-only, so it never breaks off a capture run. |
| `^E4` Flamethrower | 1965 | `primary-ammo` | 90 | `Flamespray` | **YES** | Only pool. |
| `^SF` Special Forces | 2042 | `primary-ammo` | 100 | `5.56mm.DMR.silencer` | **YES** | Rifleman precedent. |
| `^SF` Special Forces | 2072 | `secondary-ammo` | 3 | `c4` | no | Rifleman precedent — demolition is a capability on a shooter, not the shooter's job. |
| `^DR` Drone Operator | 2305 | `primary-ammo` | 1 | `DroneTargeter` | **YES** | The drone *is* the unit. (`secondary`/`DroneJammer` draws no pool.) |
| `^PILOT` Pilot | 2389 | `primary-ammo` | 100 | `MP5` | **YES** | Only pool. |

### Crew — `crew.yaml` (1 pool)

| Actor | Line | Pool | Ammo | Weapon | Essential | Reason |
|---|---|---|---|---|---|---|
| `^CrewMember` | 28 | `primary-ammo` | 24 | `primary` (pistol) | **YES** | Only pool; reproduces today's behaviour exactly. |

### America — `vehicles-america.yaml` (11 pools)

| Actor | Line | Pool | Ammo | Weapon | Essential | Reason |
|---|---|---|---|---|---|---|
| `humvee` | 89 | `primary-ammo` | 300 | 7.62mm MG | **YES** | Only pool. |
| `m113` | 244 | `primary-ammo` | 500 | 12.7mm HMG | **YES** | Only pool. |
| `bradley` | 374 | `primary-ammo` | 900 | 25mm Bushmaster | **YES** | User ruling — IFV, both pools, either triggers. |
| `bradley` | 398 | `secondary-ammo` | 8 | TOW (SV 75) | **YES** | User ruling. 40% of unit cost; the IFV's anti-armour half. |
| `abrams` | 526 | `primary-ammo` | 40 | 120mm | **YES** | Only pool. A tank with no shells is a bunker that moves. |
| `m109` Paladin | 642 | `primary-ammo` | 40 | 155mm | **YES** | Only pool. |
| `m270` MLRS | 800 | `primary-ammo` | 12 | 227mm rocket | **YES** | Only pool. **Note:** `m270` is `ResupplyBehavior: Evacuate`, so this trigger means *leave the map for a refund*, not *go rearm* — see Trap 3. Identical to today because it is single-pool. |
| `strykershorad` | 918 | `primary-ammo` | 400 | `25mm.Bradley` (SV 1) | ***unsure*** | See §3. |
| `strykershorad` | 946 | `secondary-ammo` | 8 | `Stinger.quad` (SV 65) | ***unsure*** | See §3. |
| `strykershorad` | 980 | `tertiary-ammo` | 4 | `Hellfire.strykershorad` (SV 200) | ***unsure*** | See §3. |
| `HIMARS` | 1094 | `primary-ammo` | 2 | HIMARS missile (SV 1500) | **YES** | Only pool; the launcher is the missiles. |

### Russia — `vehicles-russia.yaml` (10 pools)

| Actor | Line | Pool | Ammo | Weapon | Essential | Reason |
|---|---|---|---|---|---|---|
| `btr` | 70 | `primary-ammo` | 500 | 14.5mm HMG | **YES** | Only pool. |
| `bmp2` | 192 | `primary-ammo` | 900 | 30mm | **YES** | User ruling — IFV, both pools. Bradley's mirror. |
| `bmp2` | 221 | `secondary-ammo` | 8 | WGM ATGM (SV 65) | **YES** | User ruling. 40% of cost. |
| `t90` | 345 | `primary-ammo` | 40 | 125mm | **YES** | Only pool. Mirrors Abrams. |
| `giatsint` | 459 | `primary-ammo` | 40 | 152mm | **YES** | Only pool. Mirrors Paladin. |
| `grad` | 608 | `primary-ammo` | 40 | 122mm rocket | **YES** | Only pool. Evacuate-stance, as `m270`. |
| `tos` | 734 | `primary-ammo` | 24 | 220mm thermobaric | **YES** | Only pool. Evacuate-stance, as `m270`. |
| `tunguska` | 864 | `primary-ammo` | 180 | `30mm.Tunguska.AG` **+** `.AA` | **no** | User ruling. Cannon is self-defence; the SAMs are the AA job. **Factual footnote, because the ruling's stated reason is not literally true here:** this pool feeds *two* armaments, `primary` (AG) and `primary-air` (AA, `:852-860`), so the Tunguska's cannon does have a real anti-air mode. The verdict is unchanged — a 30mm gun is not why you field a 2K22 — but do not repeat "the cannon can't shoot planes" as though it were a fact about this actor. |
| `tunguska` | 894 | `secondary-ammo` | 8 | `9M311` SAM (SV 65) | **YES** | User ruling. The defining capability. |
| `iskander` | 993 | `primary-ammo` | 2 | Iskander missile (SV 1500) | **YES** | Only pool. |

### Ukraine — `vehicles-ukraine.yaml` (1 pool)

| Actor | Line | Pool | Ammo | Weapon | Essential | Reason |
|---|---|---|---|---|---|---|
| `t72` | 53 | `primary-ammo` | 40 | 125mm | **YES** | Only pool. Mirrors Abrams / T-90. |

### Support — `vehicles.yaml` (1 pool)

| Actor | Line | Pool | Ammo | Feeds | Essential | Reason |
|---|---|---|---|---|---|---|
| `MNLY` Minelayer | 484 | `mines-ammo` | 10 | `Minelayer` trait, **not an armament** | **YES** | Only pool; mines are the unit's entire payload, and `Rearmable` already names it (`:500-502`). Unchanged from today. |

### Static defenses — `structures-defenses.yaml` (3 pools) — flag inert

| Actor | Line | Pool | Ammo | Weapon | Verdict | Reason |
|---|---|---|---|---|---|---|
| `CRAM` | 643 | *(unnamed)* | 24 | 20mm CRAM | **n/a — leave OFF** | Immobile, no `Rearmable`. Self-reloads via `ReloadAmmoPool` (`:658`). Nowhere to go. |
| `AGUN` | 721 | *(unnamed)* | 24 | dual cannon | **n/a — leave OFF** | As `CRAM` (`ReloadAmmoPool` at `:735`). |
| `FTUR` Flame Turret | 932 | *(unnamed)* | 10 | `FireballLauncher` + `Flamespray.heavy` | **n/a — leave OFF** | Immobile, no `Rearmable`, **and no `ReloadAmmoPool` either** — see the bug filed in §4. |

### Aircraft — 18 pools — flag inert (`AmmoPool.cs:283`)

Listed for census completeness. **No verdict is assigned**: `Essential` cannot affect any of these
while the aircraft carve-out stands. The "if the carve-out were ever lifted" column is advisory only.

| Actor | File:line | Pool | Ammo | Weapon | *(advisory)* |
|---|---|---|---|---|---|
| `littlebird` | `aircraft-america.yaml:175` | `primary-ammo` | 160 | 7.62mm minigun + `primary-air` | would be Essential |
| `littlebird` | `:210` | `secondary-ammo` | 2 | Hellfire (SV 200) | would not |
| `HELI` Apache | `:350` | `primary-ammo` | 200 | 30mm chain gun | would not |
| `HELI` Apache | `:379` | `secondary-ammo` | 8 | Hellfire (SV 200) | would be Essential — the rack is the gunship |
| `A10` | `:475` | `primary-ammo` | 100 | 30mm GAU-8 | would be Essential |
| `A10` | `:505` | `secondary-ammo` | 4 | Hellfire (SV 200) | would be Essential |
| `A10.Airstrike` | `:691` | `AmmoPool@1` *(override)* | 40 | *inherits `A10`* | inherits |
| `A10.Airstrike` | `:693` | `AmmoPool@2` *(override)* | 2 | *inherits `A10`* | inherits |
| `F16` | `:600` | **`primary-ammo`** | 6 | **AAM** (SV 100) | would be Essential — **name inversion, see Trap 1** |
| `F16` | `:626` | **`secondary-ammo`** | 150 | **20mm M61 cannon** (SV 1) | would not |
| `HIND` | `aircraft-russia.yaml:175` | `primary-ammo` | 150 | 12.7mm chin gun + `primary-air` | would not |
| `HIND` | `:206` | `secondary-ammo` | 80 | S-8 rocket pod (SV 80) | would be Essential — the pod is the payload |
| `MI28` | `:350` | `primary-ammo` | 200 | 30mm chain gun | would not |
| `MI28` | `:392` | `secondary-ammo` | 8 | Ataka + `secondary-air` (SV 150) | would be Essential |
| `FROG` Su-25 | `:501` | `primary-ammo` | 60 | RocketPods (SV 75) | would be Essential — only pool |
| `FROG.Airstrike` | `:719` | `AmmoPool@1` *(override)* | 30 | *inherits `FROG`* | inherits |
| `MIG` | `:620` | **`primary-ammo`** | 6 | **AAM** (SV 100) | would be Essential — **name inversion** |
| `MIG` | `:646` | **`secondary-ammo`** | 150 | **20mm cannon** (SV 1) | would not |

### Tally

| | Pools |
|---|---|
| **Essential: true** | **34** |
| Deliberately left OFF (real decision, ground units) | 5 |
| Unsure — user to decide (§3) | 3 |
| Flag inert — aircraft | 18 |
| Flag inert — static defenses | 3 |
| **Total** | **63** |

Every ground actor in the roster ends up with **at least one** Essential pool. That is deliberate —
see the mechanism question in §5.

---

## 2. Ready-to-apply patch

**Do not apply until `Essential` exists on `AmmoPoolInfo`.** All 34 edits are the same shape: insert
one line, indented with **two tabs**, inside the named `AmmoPool` block. Position within the block is
irrelevant to MiniYaml; placing it directly under `Name:` keeps the diff readable. Line numbers are
against `697be28e` and will drift — the anchor is the actor + pool name.

The Stryker SHORAD is **not** in this patch; it is in §3 pending the user's call.

```
Essential: true
```

### `mods/ww3mod/rules/ingame/infantry.yaml` — 14 insertions

| Actor | `AmmoPool` block | at line |
|---|---|---|
| `^E1` | `AmmoPool` / `primary-ammo` | 1097 |
| `^E3` | `AmmoPool@1` / `primary-ammo` | 1178 |
| `^AR` | `AmmoPool@1` / `primary-ammo` | 1294 |
| `^E2` | `AmmoPool@1` / `primary-ammo` | 1360 |
| `^TL` | `AmmoPool@1` / `primary-ammo` | 1437 |
| `^MT` | `AmmoPool@1` / `primary-ammo` | 1526 |
| `^SN` | `AmmoPool@1` / `primary-ammo` | 1593 |
| `^AT` | `AmmoPool@1` / `primary-ammo` | 1667 |
| `^AA` | `AmmoPool@1` / `primary-ammo` | 1737 |
| `^E6` | `AmmoPool@2` / **`secondary-ammo`** | 1852 |
| `^E4` | `AmmoPool@1` / `primary-ammo` | 1965 |
| `^SF` | `AmmoPool@1` / `primary-ammo` | 2042 |
| `^DR` | `AmmoPool@1` / `primary-ammo` | 2305 |
| `^PILOT` | `AmmoPool@1` / `primary-ammo` | 2389 |

Worked example, so the shape is unambiguous (`^E3`, `:1178-1185`):

```yaml
	AmmoPool@1:
		Name: primary-ammo
		Essential: true
		Armaments: primary
		Ammo: 100
		ReloadCount: 20
		AmmoCondition: ammo-primary
		# 5.56mm rifle: 5 batches × 20 rounds × 1 supply = 5 (~5% of cost 100). E3 rifleman.
		SupplyValue: 1
```

**One comment must be updated in the same commit.** `infantry.yaml:1908-1912` justifies `^E6`'s
`AutoSeekSupplies.ReturnWhenEmpty: false` with *"The all-pools test already means he must have burned
all 100 SMG rounds AND all 3 charges before this could fire; this makes it structural rather than
unlikely."* Marking `secondary-ammo` Essential makes that sentence false — the trigger becomes 3
charges alone. The `ReturnWhenEmpty: false` decision is still correct (and is exactly what keeps the
change safe), but its stated reason has to be rewritten to say so.

### `mods/ww3mod/rules/ingame/crew.yaml` — 1

| Actor | Block | at line |
|---|---|---|
| `^CrewMember` | `AmmoPool@1` / `primary-ammo` | 28 |

### `mods/ww3mod/rules/ingame/vehicles-america.yaml` — 8

| Actor | Block | at line |
|---|---|---|
| `humvee` | `AmmoPool@1` / `primary-ammo` | 89 |
| `m113` | `AmmoPool@1` / `primary-ammo` | 244 |
| `bradley` | `AmmoPool@1` / `primary-ammo` | 374 |
| `bradley` | `AmmoPool@2` / `secondary-ammo` | 398 |
| `abrams` | `AmmoPool@1` / `primary-ammo` | 526 |
| `m109` | `AmmoPool@1` / `primary-ammo` | 642 |
| `m270` | `AmmoPool@1` / `primary-ammo` | 800 |
| `HIMARS` | `AmmoPool@1` / `primary-ammo` | 1094 |

### `mods/ww3mod/rules/ingame/vehicles-russia.yaml` — 9

| Actor | Block | at line |
|---|---|---|
| `btr` | `AmmoPool@1` / `primary-ammo` | 70 |
| `bmp2` | `AmmoPool@1` / `primary-ammo` | 192 |
| `bmp2` | `AmmoPool@2` / `secondary-ammo` | 221 |
| `t90` | `AmmoPool@1` / `primary-ammo` | 345 |
| `giatsint` | `AmmoPool@1` / `primary-ammo` | 459 |
| `grad` | `AmmoPool@1` / `primary-ammo` | 608 |
| `tos` | `AmmoPool@1` / `primary-ammo` | 734 |
| `tunguska` | `AmmoPool@2` / **`secondary-ammo` only** | 894 |
| `iskander` | `AmmoPool@1` / `primary-ammo` | 993 |

### `mods/ww3mod/rules/ingame/vehicles-ukraine.yaml` — 1

| Actor | Block | at line |
|---|---|---|
| `t72` | `AmmoPool@1` / `primary-ammo` | 53 |

### `mods/ww3mod/rules/ingame/vehicles.yaml` — 1

| Actor | Block | at line |
|---|---|---|
| `MNLY` | `AmmoPool` / `mines-ammo` | 484 |

### Files deliberately untouched

`aircraft-america.yaml`, `aircraft-russia.yaml`, `structures-defenses.yaml`. No insertions — the flag
has no effect on any actor in them (see "Where the flag does nothing"). The two `.Airstrike` variants
need no lines of their own in any case: both are `Inherits:` overrides of their parent airframe
(`aircraft-america.yaml:670`, `aircraft-russia.yaml:696`) that set only `Ammo:`, so whatever the
parent carries flows through.

### Verification after applying

- `make test` (YAML validation) — catches a typo'd field name or a bad indent.
- Diff line count should be exactly **34** additions, no deletions, plus the one `^E6` comment rewrite.
- `grep -c 'Essential: true' mods/ww3mod/rules/` should return 34 (or 35/36/37 once §3 is decided).

---

## 3. Unsure — one item, for the user to decide

### Stryker SHORAD (`vehicles-america.yaml:864-1018`) — which of its three pools is Essential?

**The facts, read from the actor rather than from its name.** Cost 2500; three armaments, three pools,
all three listed in `Rearmable.AmmoPools` (`:1010`) and all three in `AttackTurreted.Armaments`
(`:1013`):

| Pool | Rounds | Weapon | Role | Budget |
|---|---|---|---|---|
| `primary-ammo` | 400 | `25mm.Bradley` | anti-**ground** autocannon | 8 supply (0.3% of cost) |
| `secondary-ammo` | 8 | `Stinger.quad` | **the only anti-air weapon on the vehicle** | 520 (21%) |
| `tertiary-ammo` | 4 | `Hellfire.strykershorad` | anti-armour, ground | 800 (32%) |

**The load-bearing difference from the Tunguska, which cuts in favour of a clean answer.** The
Tunguska's cannon pool feeds a dedicated `primary-air` armament (`vehicles-russia.yaml:852-860`), so
its gun genuinely engages aircraft. The SHORAD's autocannon is literally the Bradley's IFV gun —
`Weapon: 25mm.Bradley` at `:912`, with a stale `# 30mm.Stryker` comment beside it — and there is **no
`primary-air` armament anywhere on the actor.** Strip the Stingers and the SHORAD has *zero* ability
to shoot down an aircraft. So the user's AA rule maps onto this vehicle more cleanly than onto the
Tunguska it was written for.

**My recommendation: `secondary-ammo` (Stinger) only.** An air-defence vehicle out of SAMs is not
providing air defence, and the worst outcome is the one where it *looks* like it is — sitting on the
pad, sprite intact, while a strike goes through unopposed. Pulling it back to reload is the behaviour
that matches what the player thinks the unit is doing.

**The tension, which is why this is not decided silently.** Under Stinger-only, a SHORAD that fires
all four Hellfires but still holds Stingers never seeks resupply — 800 supply of anti-armour
capability, its single most expensive pool and *more* than the Stingers, sits unreplenished
indefinitely. Compare the Bradley, where the user ruled the 600-supply TOW pool Essential precisely
because "the missile load is the IFV's main combat value above the autocannon". By that reasoning the
SHORAD's Hellfire has an equal claim. The question underneath is a design one only the user can
settle: **is the Stryker SHORAD an air-defence vehicle that happens to carry Hellfires, or a
three-role platform?** Its `Tooltip.GenericName` says "Stryker Short Range Air Defense" (`:866`) and
its `Buildable.Description` says "Wheeled IFV for rapid troop transport… Autocannon, Carries
infantry" (`:879`) — the two shipped descriptions of this unit disagree with each other, which is a
fair summary of the problem. (That description is also just wrong; filed in §4.)

**The three options, in the order I'd rank them:**

| | Setting | Behaviour | Cost of being wrong |
|---|---|---|---|
| **A** *(recommended)* | `secondary-ammo` only | Reloads when out of SAMs. Holds position with 400 cannon rounds + 4 Hellfires. | A Hellfire-dry SHORAD never restocks its best weapon. |
| **B** | `secondary-ammo` + `tertiary-ammo` | Reloads when out of *either* missile type; the cheap autocannon never triggers. Treats it as a missile platform with a gun. | Leaves an air-defence position for an anti-armour reload. |
| **C** | all three | Any empty pool triggers. Maximum "always topped up". | The 400-round, 8-supply autocannon — the cheapest thing on the vehicle — gets to pull a SHORAD off station. This is the option I'd argue against. |

Nothing else in the census was a close enough call to be worth the user's attention. The other four
deliberate OFFs (`^E3` RPG, `^TL` grenades, `^SF` C4, `tunguska` cannon) all follow directly from a
worked example the user gave; `^E6`'s SMG is forced by a mechanical constraint rather than judged.

---

## 4. Bugs found while reading (not caused by, and not fixed on, this branch)

1. **`FTUR` (Flame Turret) is permanently disarmed by a single close-range burst.** Pool of 10
   (`structures-defenses.yaml:932-941`) shared by both armaments; `Armament@2` / `Flamespray.heavy`
   declares `AmmoUsage: 10` (`:927`), so one melee burst empties the pool outright. The actor has no
   `Rearmable` and — unlike `CRAM` (`:658`) and `AGUN` (`:735`) — **no `ReloadAmmoPool`**, so nothing
   ever gives it a round back. After one secondary shot a 1000-credit defensive structure is inert
   scenery for the rest of the match. Filed in `WORKSPACE/bugs/discovered.md`.
2. **`strykershorad.Buildable.Description` is copy-pasted from the Stryker IFV** (`:879`): *"Wheeled
   IFV for rapid troop transport.\n\n - Autocannon\n - Carries infantry\n - Armor: Medium"*. It
   mentions neither the Stingers nor the Hellfires — i.e. the sidebar text for an air-defence vehicle
   does not mention air defence. Also filed.

---

## 5. Notes for the mechanism worker (`wt/resupply-tiers`)

Three properties of the existing code that this authoring pass depends on. If any is not true of the
implementation, the table above needs revisiting.

**Trap 1 — the pool NAME does not tell you the pool's ROLE, and the corpus proves it three ways.**
`primary-ammo` is used 40 times and `secondary-ammo` 15, and the convention is broken by: the Tunguska
(whose `primary-ammo` is the self-defence cannon, the case that killed a "primary is essential"
default); the `F16` and `MIG`, where `primary-ammo` is the **missile** rack and `secondary-ammo` is
the gun — the exact inverse of every other airframe; and `MNLY`, whose `mines-ammo` feeds the
`Minelayer` trait rather than any armament at all. Always resolve `Armaments:` → `Armament@N.Weapon:`
→ that weapon's `ValidTargets`.

**Trap 2 — `Essential` must not be set on a pool absent from `Rearmable.AmmoPools`, or the unit
loops.** The two sets are different (`Rearmable.cs:44` filters to its declared list) and on 13 of 14
infantry classes they coincide, which is what makes the wrong one look right. **`^E6` is the single
divergence in the whole corpus:** it declares `AmmoPools: secondary-ammo` (`infantry.yaml:1907`) while
also carrying a 100-round `primary-ammo` (`:1824`). Marking that SMG pool Essential would mean the
engineer seeks a host, is refilled on `secondary-ammo` only, goes idle still-Essential-empty, and
re-seeks forever. This is why his SMG is OFF above regardless of the role argument — and it is worth
a validation check in the trait: *Essential ⊆ Rearmable.AmmoPools, for any actor that has a
`Rearmable` at all.*

**Trap 3 — on an `Evacuate`-stance actor the trigger does not mean "rearm", it means "leave the map".**
`AmmoPool.cs:337-347` queues `RotateToEdge` with an evacuation refund. Four actors set
`InitialResupplyBehavior: Evacuate`: `TRUK` (unarmed, no pool) and the three rocket artillery pieces
`m270` / `grad` / `tos`. All three are **single-pool**, so `Essential` changes nothing for them today.
But if any Evacuate-stance actor ever gains a second pool, an Essential flag on one of them
permanently removes a still-combat-capable vehicle from the match. Worth a comment at the trigger
site.

**Open mechanism question — what happens to an actor with pools but *no* Essential pool?** Does the
trigger fall back to `AllPoolsEmpty` (today's behaviour) or does the unit simply never self-dispatch?
The table above sidesteps the question — every ground actor ends up with at least one Essential pool
— but the answer should be stated in the trait's summary rather than left to be discovered, because
the safe default and the intuitive default differ here.
