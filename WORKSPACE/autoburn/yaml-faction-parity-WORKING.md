# YAML faction parity audit — working notes

Branch: `auto/yaml-faction-parity`
Date: 2026-05-20

## Pairs

| File | US | Russia | Other |
|---|---|---|---|
| Aircraft | `aircraft-america.yaml` (650 lines) | `aircraft-russia.yaml` (670 lines) | — |
| Infantry | `infantry-america.yaml` (128 lines) | `infantry-russia.yaml` (137 lines) | `infantry-neutral.yaml` |
| Vehicles | `vehicles-america.yaml` (1234 lines) | `vehicles-russia.yaml` (1169 lines) | `vehicles-ukraine.yaml` (86 lines) |

## Equivalence tables

### Aircraft
| US | Russia | Role |
|---|---|---|
| TRAN | HALO | Transport heli |
| littlebird | HIND | Light heli |
| HELI | MI28 | Attack heli |
| A10 | FROG | Ground-attack jet |
| F16 | MIG | Fighter |
| A10.Airstrike | FROG.Airstrike | Airstrike variant |

### Infantry (all `.america` ↔ `.russia` — symmetric)
E1, E1R1, E3, E3R1, AR, E2, E2R1, MT, TL, AT, AA, MEDI, SN, E6, SF, TECN, DR, E4.

### Vehicles
| US | Russia | Role |
|---|---|---|
| humvee | btr | Wheeled APC / scout |
| m113 | bmp2 | Tracked APC / IFV |
| bradley | — | IFV (US-only) |
| abrams | t90 | MBT |
| m109 | giatsint | SP howitzer |
| m270 | grad | MLRS |
| — | tos | Thermobaric MLRS (Russia-only) |
| strykershorad | tunguska | SHORAD AA |
| HIMARS | iskander | Long-range missile |
| HIMARSMissile | IskanderMissile | Projectile |

---

## Audit findings

### Infantry pair — CLEAN

The infantry pair is virtually identical. Differences:

1. **Convention drift (low risk):** Russia uses `Inherits@BaseUnit: ^X` while America uses `Inherits: ^X`. Functionally identical for a single base inheritance — the `@Name` suffix is needed only when multiple Inherits stanzas are merged. **Not a bug.** Just style drift.

   Exception inside Russia file: `DR.russia` uses bare `Inherits: ^DR` (matching America). Internal inconsistency in the Russia file — Russia has 16× `Inherits@BaseUnit` and 1× `Inherits`. **Cosmetic.**

2. **Commented-out DOG (attack dog) at Russia bottom** — America has no equivalent. Already disabled, not active.

3. **E1.russia** has `Prerequisites: ~disabled` (matching America's E1) — both Conscripts are intentionally not buildable. Consistent.

Verdict: **No typos. No structural drift. No actionable changes.**

### Aircraft pair

**TYPO (real bug — to fix):**

1. **`aircraft-america.yaml` A10, line 481:** Trait key `ReloadAmmoPool@1` is duplicated. The block at line 451 sets `AmmoPool: primary-ammo`; the block at line 481 sets `AmmoPool: secondary-ammo` — clearly meant to be `ReloadAmmoPool@2`. MiniYaml merges same-key entries, so the second silently overrides the first → primary-ammo never gets a Reload registration with the correct sound, and secondary-ammo gets `@1`'s margin and pip config. Russia side (FROG/MIG) uses correct `@1`/`@2` keying. **FIX.**

**Suspicious differences (flag for user — do NOT touch):**

1. **`MI28` references `secondary-air` armament that is never defined.**
   - References at lines 312, 322, 367 in `aircraft-russia.yaml` (AttackAircraft.Armaments, GrantConditionOnPreparingAttack.ArmamentNames, AmmoPool@2.Armaments).
   - No `Armament@*: Name: secondary-air` block exists in the file (or in `aircraft.yaml`, `aircraft-america.yaml`).
   - HIND has the same name in a comment: `# Armaments: primary, secondary #, secondary-air`.
   - Looks like a planned-but-not-wired air-engagement variant of the Ataka. Engine likely treats the dangling reference as no-op. Apache (HELI) has no equivalent dangling reference.

2. **`HIND` configures `AutoTarget` explicitly instead of inheriting `^AutoTargetGroundAntiTankandAir`.**
   - HIND comments out `# Inherits@AutoTarget: ^AutoTargetGroundAntiTankandAir` and defines its own stances + priorities (lines 111–126).
   - US-side `littlebird` (closest tier match) just inherits the shared template.
   - Could be intentional asymmetry (HIND has Cargo carry capacity + dual role) or unfinished refactor.

3. **`F16` sets `AutoTarget: EnableStances: false`; `MIG` uses inherited stances.**
   - F16 (line 554–555): `EnableStances: false`
   - MIG inherits `^AutoTargetGroundAntiTankandAir` (line 538) so stances are active.
   - Both have AAM primary + 20mm secondary. F16 is effectively air-only; MIG can be stance-toggled to engage ground.
   - **Asymmetric behavior.** May be intentional (F16 = air-superiority, MIG = multi-role) but worth a glance.

4. **Stale comment in `F16` and `MIG` references `mig`:** Line 586 in F16 says
   `# MuzzleSequence: muzzle # Error: Actor type 'mig' trait 'Armament' field 'MuzzleSequence' references an undefined sequence 'muzzle' on image 'mig'.`
   — almost certainly copy-pasted from the MIG file when F16 was authored. Cosmetic.

5. **TakeoffSounds asymmetry:** `MIG` (line 564) and `FROG` (line 454) define `TakeoffSounds: migtoff1.aud`. `F16` and `A10` don't define a takeoff sound. Asymmetric SFX. Likely an oversight rather than a deliberate choice (the sound is named `migtoff` which suggests it was added with MiG first).

6. **`FROG` re-inherits `^GainsExperience`** (line 430: `Inherits@GainsExperience: ^GainsExperience`). Already inherited via `^Aircraft` → `^Airborne`. Redundant but harmless. `A10` doesn't re-inherit.

7. **`HIND` has Cargo (capacity 8)**; `HELI` (Apache) has no Cargo trait. Historical Mi-24 carries troops, so likely intentional. But this is a real gameplay asymmetry the user may want to confirm.

8. **`HIND.AttackAircraft` has `ForceFireIgnoresActors: True`**; HELI/MI28 do not. Russia-only forced-fire behavior.

9. **`MI28` has SACLOS slowdown** (`GrantConditionOnPreparingAttack` + `SpeedMultiplier@FiringAtaka`, lines 321–327) — Apache (HELI) does not. **Already commented as intentional** ("Mirrors the Bradley/WGM pattern").

10. **FROG has blank line inside trait `WithAmmoPipsDecoration@1`** (`aircraft-russia.yaml` line 485 blank, 486–487 continue the children). Cosmetic formatting noise — indent is preserved so the parse should still treat 486–487 as children. CLAUDE.md notes blank lines matter only at top level.


