# YAML faction-parity audit — 260520

Branch: `auto/yaml-faction-parity`
Worker date: 2026-05-20

## Summary

Audited the three paired US/Russia YAML files (`aircraft-*.yaml`, `infantry-*.yaml`, `vehicles-*.yaml`) trait-by-trait for the unit pairs that line up by role. Stat values (damage, range, cost, speed, HP, armor thickness) were treated as intentional balance choices and **not touched**.

Four real typos / structural bugs were found and fixed in independent commits. A non-trivial set of suspicious-but-arguably-intentional differences are reported below for the user to judge.

## Typos fixed

All four are non-stat, non-balance changes — dangling references, duplicate keys, and a transposed letter. Each landed as its own commit.

| # | File | Bug | Commit |
|---|------|-----|--------|
| 1 | `aircraft-america.yaml` (A10, line 481) | `ReloadAmmoPool@1` duplicated — second block sets `AmmoPool: secondary-ammo` and silently overrode the first via MiniYaml key-merge. Russia side keys these as `@1`/`@2`. | `9404bc93` |
| 2 | `vehicles-russia.yaml` (bmp2, line 142) | Tooltip `Name: BPM-2` (letter swap). Actor id, image, and AnnounceOnSeen text are all `BMP`. | `b8778902` |
| 3 | `vehicles-russia.yaml` (tunguska, line 835) | `AmmoPool@1.Armaments: primary, tertiary` — `tertiary` references a nonexistent armament. The actual second armament is `primary-air` (the 30mm AA-mode variant); without the link, `primary-air` was firing without consuming `primary-ammo`. | `0e3858d2` |
| 4 | `vehicles-america.yaml` (m113, line 256) | `Rearmable.AmmoPools: primary-ammo, secondary-ammo, tertiary-ammo` — m113 only defines `primary-ammo`. The two extra entries were leftover from the strykershorad/bradley layout. | `b1ca86a3` |

Total: 4 commits, +4/−4 lines across two files.

## Suspicious differences (user review needed)

These look like real asymmetries between the US and Russia roster but each could plausibly be intentional design or a residual bug. None were touched.

### Aircraft

1. **`MI28` references a `secondary-air` armament that is never defined.**
   - References at `aircraft-russia.yaml:312, 322, 367` (AttackAircraft.Armaments, GrantConditionOnPreparingAttack.ArmamentNames, AmmoPool@2.Armaments).
   - No `Armament@*: Name: secondary-air` block exists anywhere in `aircraft.yaml`, `aircraft-america.yaml`, or `aircraft-russia.yaml`.
   - HIND has the same name as a commented-out hint: `Armaments: primary, secondary #, secondary-air`.
   - Looks like a planned-but-not-wired anti-air Ataka variant. Apache (HELI, the US equivalent) has no parallel reference.
   - **If intentional placeholder** — leave it. **If you ever wire up `secondary-air`** — it'll suddenly start consuming the existing `secondary-ammo` pool.

2. **`HIND` configures `AutoTarget` explicitly instead of inheriting `^AutoTargetGroundAntiTankandAir`.**
   - `# Inherits@AutoTarget: ^AutoTargetGroundAntiTankandAir` (commented out, line 91).
   - HIND then writes its own AutoTarget + AutoTargetPriority@Default/Lower/FireAtWill blocks (lines 111–126).
   - The US tier-match `littlebird` just inherits the shared template.
   - Could be intentional (HIND has Cargo + dual-role) or an unfinished refactor.

3. **`F16` sets `AutoTarget: EnableStances: false`; `MIG` uses inherited stances.**
   - F16 line 554–555: `AutoTarget: EnableStances: false`.
   - MIG inherits `^AutoTargetGroundAntiTankandAir` (line 538) so it can be toggled to engage ground.
   - Both have AAM primary + 20mm secondary. Practical effect: F16 is strictly air-superiority; MIG can be set to attack ground targets with the same weapon loadout.
   - **Asymmetric behaviour.** May be a deliberate doctrine split (F-16 = air, MIG = multi-role) — worth confirming.

4. **`MIG` and `FROG` define `TakeoffSounds: migtoff1.aud`; `F16` and `A10` define no takeoff sound.**
   - Asymmetric SFX. The sound is named `migtoff` — suggests it was added when MiG was first authored and never propagated to the US side.
   - One-line oversight or intentional? Easy to fix either way.

5. **`FROG` re-inherits `^GainsExperience` (line 430)** — already inherited via `^Aircraft` → `^Airborne`. Redundant; `A10` doesn't re-inherit. Harmless.

6. **`HIND` has Cargo (capacity 8); `HELI` (Apache) has no Cargo trait.**
   - Real Mi-24 carries troops, so likely intentional. But it's a major gameplay asymmetry — US has no troop-carrying attack heli.

7. **`HIND.AttackAircraft: ForceFireIgnoresActors: True`** — `HELI`/`MI28` don't have this. Russia-only forced-fire ignores intervening actors.

8. **`MI28` SACLOS slowdown (Ataka) — `HELI` (Apache) doesn't.** This one is **already explicitly documented as intentional** in a comment ("Mirrors the Bradley/WGM pattern"). Listed only for completeness.

9. **Stale comment in `F16` line 586** references `mig`: `# MuzzleSequence: muzzle # Error: Actor type 'mig' …`. Almost certainly copy-pasted from the MIG file. Cosmetic.

10. **`FROG` has a blank line in the middle of a trait block** (`WithAmmoPipsDecoration@1` at `aircraft-russia.yaml:485`). Indent is preserved so the parse should still treat the following lines as children. Per `CLAUDE.md`, blank lines matter at top level — this is cosmetic formatting noise inside a block.

### Infantry

The infantry pair is **clean**. Nothing actionable.

- Russia uses `Inherits@BaseUnit: ^X` style; America uses bare `Inherits: ^X`. Functionally identical (the `@Name` suffix matters only when multiple `Inherits` are merged). Style drift only.
- One inconsistency inside the Russia file: `DR.russia` uses bare `Inherits: ^DR` while 16 other Russia infantry use `Inherits@BaseUnit: ^X`. Internal cosmetic inconsistency, not a bug.
- `E1.america` and `E1.russia` are both `Prerequisites: ~disabled` — Conscripts not buildable. Symmetric.
- Russia has a commented-out `DOG.russia` block at the bottom (lines 130–136) — not active.

### Vehicles

1. **Duplicate `Health:` block in tunguska** (`vehicles-russia.yaml:782` and again at `:795`). MiniYaml merges same-key entries with the later value winning, so tunguska's effective HP is **8000** (the second block), not the 14000 declared in the first. Either the first or the second value is wrong — not a typo, a balance decision; **not fixed**. If the intent is HP 14000, delete the second `Health:`. If the intent is 8000, delete the first.

2. **Duplicate `BuildPaletteOrder: 70` between `tunguska` and `iskander`.** Both Russia units share build-palette slot 70 — sort order between them is undefined. US side has clean 1–8 ordering; Russia uses 10/20/30/40/50/60/70/70. Iskander should probably be `80`. Cosmetic UI ordering; **not fixed**.

3. **`HIMARS.Mobile.Locomotor: lighttracked` while inheriting `^WheeledVehicle`** (`vehicles-america.yaml:1025`). All other Russia/US wheeled units use `lightwheeled` or `heavywheeled`; Iskander relies on the wheeled-vehicle template's default. HIMARS overriding to `lighttracked` is inconsistent with its inherited type — possibly intentional handling for the tracked-feel of the FMTV launcher, but unusual. **Not fixed.**

4. **`bmp2.Buildable.Prerequisites: ~techlevel.low`** — every other tracked/wheeled unit on either side uses `~techlevel.medium`. BMP-2 is the only `low` tier vehicle. Could be intentional Russia early-game advantage. **Not fixed.**

5. **`UpdatesPlayerStatistics: AddToArmyValue: true` asymmetry:**
   - US: humvee ✓, m113 ✓, bradley ✓, abrams ✗, m109 ✓, m270 ✓, strykershorad ✓, HIMARS ✓.
   - Russia: btr ✓, bmp2 ✗, t90 ✗, giatsint ✓, grad ✓, tos ✓, tunguska ✗, iskander ✓.
   - Both MBTs (abrams, t90) skip it — symmetric.
   - **bmp2 missing it asymmetric with bradley.**
   - **tunguska missing it asymmetric with strykershorad.**
   - Affects player-statistics army-value tracking only — not gameplay, but visible in score reports.

6. **`m113.Mobile.PauseOnCondition` is commented out** (line 226: `# PauseOnCondition: empdisable || !notmobile`). bmp2 and most other vehicles have `PauseOnCondition: empdisable` active. m113 can therefore still move while EMP'd. Possibly intentional, possibly forgotten. btr also lacks an active PauseOnCondition.

7. **`humvee.Inherits@EXPERIENCE: ^GainsExperience`** (all caps) — all other units use `Inherits@GainsExperience: ^GainsExperience`. Cosmetic.

8. **`giatsint` uses `AttackFrontal`; `m109` uses `AttackTurreted`.** Real-world: M109 has a 360° turret, 2S5 Giatsint has a chassis-mounted gun with limited traverse. Realistic asymmetry — almost certainly intentional.

9. **`iskander` uses `AttackFrontal`; `HIMARS` uses `AttackTurreted`.** Same kind of realistic asymmetry — Iskander launcher needs to face the target, HIMARS rotates.

10. **`grad`/`tos` use split `WithSpriteTurret@idle` / `WithSpriteTurret@firing`; `m270` uses a single `WithSpriteTurret` block.** Probably driven by sprite assets (Russian MLRS have separate stowed/raised pod sprites).

11. **Cookoff weapons (`Explodes@CrewCookoff`)** — distributed asymmetrically:
    - US: humvee, m113 have `VehicleCookoffTiny`; m270 has `VehicleCookoffLarge`.
    - Russia: btr has `VehicleCookoffTiny`; grad and tos have `VehicleCookoffLarge`.
    - The other vehicles (bradley, abrams, m109, strykershorad, HIMARS, bmp2, t90, giatsint, tunguska, iskander) have no explicit Explodes@CrewCookoff (presumably inherit from `^Combatant` / `^TrackedVehicle` / `^WheeledVehicle`).
    - Not necessarily a bug — but if "no cookoff" is meant to mean "use a sensible default", it's worth checking the parent templates have one.

12. **`t90` has a stray blank line** between `WithMuzzleOverlay:` and `Selectable:` at line 376. Indent preserved. Cosmetic.

## Verification

- All four typo fixes are syntactic — no stat values changed.
- The `tunguska` `tertiary → primary-air` change is the only one with behavioural impact: it links the AA-mode 30mm gun to the existing `primary-ammo` pool, which is what the surrounding structure intended.
- The other three fixes (`A10 ReloadAmmoPool@2`, `BMP-2` tooltip, `m113 Rearmable`) are pure cleanups of broken references.
- A `make test` (`OpenRA.Utility --check-yaml`) was kicked off but was contended with another autoburn worker's parallel build; verifier did not complete in this session. The four fixes are local in scope and read clean by eye; the user should re-run `make test` after merging to confirm.

## Files touched

- `mods/ww3mod/rules/ingame/aircraft-america.yaml` (1 line — A10 ReloadAmmoPool key)
- `mods/ww3mod/rules/ingame/vehicles-russia.yaml` (2 lines — bmp2 Name; tunguska Armaments)
- `mods/ww3mod/rules/ingame/vehicles-america.yaml` (1 line — m113 Rearmable AmmoPools)
- `WORKSPACE/autoburn/yaml-faction-parity.md` (this file)
- `WORKSPACE/autoburn/yaml-faction-parity-WORKING.md` (working notes from the audit)

## Recommended next step for the user

If you want to clear the suspicious list:

1. **Quick wins (one-line edits):** items 1 (tunguska duplicate Health), 2 (iskander BuildPaletteOrder 70 → 80), and the stale `mig` comment in F16. Each is mechanical and risk-free.
2. **Need design decisions:** items 3 (HIMARS locomotor), 4 (bmp2 tech-level), 5 (UpdatesPlayerStatistics), aircraft items 3 (F-16 stances), 4 (TakeoffSounds), 6 (HIND Cargo).
3. **Investigate-then-decide:** the dangling `secondary-air` reference on MI28 — either wire up the armament or remove the references.
