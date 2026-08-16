# Weapons × armour matrix — ranked defect list

Companion to [`260816-weapons-armour-matrix.md`](260816-weapons-armour-matrix.md). Read-only audit at
`main` @ `d919c81a`, worktree `wt/weapons`. No builds, no game launches, no autotests were run.

Ranked by **what a player would notice**, not by count. Each entry states whether it is **LIVE** (a
player can meet it in a shipped match today) or **LATENT** (real, but gated behind disabled content or
an authoring step nobody has taken yet).

---

## D1 — The Mi-28 cannot fire at anything in the air. **LIVE**

The brief's claim was "Mi-28 has no AA weapon; `secondary-air` is referenced 3× and defined 0×."
Confirmed, and it understates the result: the Mi-28 has no weapon that can *target* an airborne actor
at all, so there is no fallback.

**The dangling reference.** `MI28` (`rules/ingame/aircraft-russia.yaml`) names `secondary-air` three
times — `AttackAircraft.Armaments` (`:328`), `GrantConditionOnPreparingAttack.ArmamentNames` (`:338`),
`AmmoPool@2.Armaments` (`:383`) — and declares only `Armament@1` (`primary` → `30mm.Heli`) and
`Armament@2` (`secondary` → `Ataka`). `AttackBase` filters its `Armament` traits by name, so an
unmatched name is dropped with no warning. Across all 580 actors in the ruleset **`MI28` is the only
actor whose `Attack*` trait names an armament that does not exist.**

**Why there is no fallback.** An airborne helicopter carries `Air, AirDetonateAttack` (from
`^NeutralAirborne`'s `Targetable@Airborne`) plus `Helicopter`; a fixed-wing carries `Air`. The Mi-28's
two real weapons:

| armament | weapon | `ValidTargets` | reaches airborne? |
|---|---|---|---|
| `primary` | `30mm.Heli` | `Ground` (`InvalidTargets: Wall, Husk`) | no |
| `secondary` | `Ataka` | `Vehicle, Defense` | no |

`Vehicle` is on the *landed* `Targetable@Ground`, not the airborne one, so `Ataka` reaches a helicopter
only after it has landed. Nothing on the Mi-28 lists `Air` or `Helicopter`.

**Three independent signals say this is unintended, not a design choice.**

1. Its `Buildable.Description` reads `- Can engage aircraft` (`:307`).
2. Its AI template is `^AutoTargetGroundAntiTankandAir` — the only actor in the mod whose AutoTarget
   template promises air and whose weapons cannot deliver it.
3. Both other attack helicopters carry the dedicated split this unit is missing: `HIND` has
   `Armament@1_Air` (`primary-air` → `12.7mm.Hind.AA`, `ValidTargets: Helicopter`) and `littlebird` has
   `primary-air` → `7.62mm.Minigun.AA`. The `secondary-air` reference is the unfinished third instance
   of that same pattern.

**What the player sees.** A 6000-credit Russian heavy attack helicopter, advertised as anti-air, that
ignores enemy helicopters entirely — while its 6000-credit American counterpart `HELI` engages them
fine, because `Hellfire` lists `ValidTargets: Vehicle, Air, Defense`. A same-price faction asymmetry on
a headline unit.

**Also wrong in the same tooltip, cosmetically:** the description says `Armor: Medium`; the actor
carries `Armor: Type: Heavy, Thickness: 20`.

**To settle it in-engine** (I hold no test grant): a scenario placing an `MI28` on `FireAtWill` opposite
an airborne `HIND` at ~10 cells, asserting the Mi-28's damage output is zero across ~500 ticks. Static
evidence above is already conclusive on the targeting types; the run would only confirm no other path
fires.

---

## D2 — The Iskander / HIMARS designator deals full damage to infantry. **LIVE**

Confirmed exactly as briefed, with the numbers pinned.

`IskanderTargeter` (`weapons-missiles.yaml:327`, inherited verbatim by `HIMARSTargeter` at `:346`) is a
designator: `InstantHit`, `Range: 50c0`, `MinRange: 16c0`, `BurstWait: 250`, `Damage: 50`. Its entire
`Versus` table exists to make that 50 land as zero:

```yaml
Versus:
    None: 0
    Wood: 0
    Concrete: 0
    Light: 0
    Medium: 0
    Heavy: 0
    Brick: 0
```

- **`Brick` is not an armour class in this mod.** The nine are `Unarmored Kevlar None Light Medium
  Heavy Wood Concrete Indestructable`. `Versus.ContainsKey` never matches it; the line is a silent
  no-op.
- **Three real classes are omitted: `Unarmored`, `Kevlar`, `Indestructable`.** An omission is not a
  zero — the filter matches nothing, `ApplyPercentageModifiers` runs over an empty sequence, and the
  victim takes the unmodified **100 %**.

Of the three omissions only two carry weight, and only one is live:

| omitted class | who wears it | effect |
|---|---|---|
| `Kevlar` | all 69 combat-infantry actors, via `^Soldier` | **50 damage per designation**, Thickness 0 so `Penetration` does not reduce it. A rifleman is 200 HP → 4 shots. |
| `Unarmored` | `TRUK` (10000 HP), `quadcopterdrone` (50 HP, Thickness 3 → 16 dmg) | negligible on the truck; 3 shots on a landed drone. |
| `Indestructable` | `SUPPLYROUTE` only | **inert.** Its only target type is `NoAutoTarget`, which no weapon in the mod lists, so nothing can aim at it. |

**Rank.** Real and live, but small: 50 damage on a 250-tick cycle at 16–50 cells. It is ranked below D1
because a player is far more likely to notice a helicopter that ignores aircraft than a designator that
slowly plinks infantry. It matters mainly because the table's *entire purpose* is to deal zero, and it
fails at that for the single largest actor population in the game.

---

## D3 — 18 actors carry duplicate trait keys; any second YAML source naming one throws at rules load. **LATENT**

The brief claimed `humvee` declares `RenderSprites` twice and that a map overriding it fails to load.
**Both halves confirmed** — and it is an 18-actor class, not one actor.

| actor(s) | duplicated key | resolved effect today |
|---|---|---|
| `humvee` (`vehicles-america.yaml:28`, `:156`) | `RenderSprites` | harmless — `Scale: 0.9` and `Image: humvee` are disjoint fields and merge |
| `^CivField` (`civilian.yaml:145`, `:179`) | `RenderSprites` | harmless — disjoint fields |
| `A10` (`aircraft-america.yaml:444`, `:560`) | `RenderSprites` | harmless — disjoint fields |
| `A10` (`:483`, `:513`) | `ReloadAmmoPool@1` | **not harmless — see D4** |
| 14 × `*.Husk.EMP` (`husks-aircraft.yaml`) | `Inherits` | harmless — `ResolveInherits` walks the raw child list, so both parents are applied |

**The load-time mechanism, verified.** `MergePartial(MiniYaml, MiniYaml)` calls
`IntoDictionaryWithConflictLog` on **both** argument child lists (`MiniYaml.cs:522-529`), and that
helper **throws `ArgumentException`** when it finds a duplicate key (`Exts.cs:484-491`) — it is not a
log, despite the name. That function is reached for an actor only when a *second source* declares the
same actor key: `MiniYaml.Load` keeps every rules file, and the map's `rules.yaml`, as separate sources
(`MiniYaml.cs:625-637`) and folds them with `.Aggregate(MergePartial)`. On the first source an actor is
appended as-is; on the second it is merged, and the merge inspects the accumulated children.

I mirrored `MergeSelfPartial` / `MergePartial` / `ResolveInherits` in a throwaway resolver and replayed
it over the real 35 rule files. Baseline merges clean. Adding one synthetic map source that names each
actor:

```
map overriding humvee        -> THROWS: duplicate values for RenderSprites   (vehicles-america.yaml:156)
map overriding A10           -> THROWS: duplicate values for ReloadAmmoPool@1 (aircraft-america.yaml:513)
map overriding ^CivField     -> THROWS: duplicate values for RenderSprites   (civilian.yaml:179)
map overriding B52.Husk.EMP  -> THROWS: duplicate values for Inherits        (husks-aircraft.yaml:97)
map overriding t90 / abrams / MI28 -> OK
```

**Why it is latent, and why that is fragile.** No shipped map currently overrides any of the 18, so
nothing is broken today. But `^CivField` is a *template*, and `conventions.md` explicitly recommends
overriding a `^Template` from map rules as the reliable idiom (because overriding a faction-suffixed
concrete key throws for a different reason). The documented safe path is mined. Any map author touching
crop fields, the Humvee, or an EMP'd aircraft husk hits this.

**One half I could not verify: the "presents as a hang" framing.** `Ruleset.Load` runs the merge on a
`Task` polled by `loader.Wait(40)`, which rethrows as `AggregateException` — the normal surfacing for
that is a load-error dialog, not a hang. I did not run a map to find out. Do not repeat the hang claim
without evidence.

---

## D4 — The A-10's primary ammo pool has no reload path. **LATENT (disabled content)**

Falls out of D3's scan and is the one duplicate that is *not* field-disjoint.

`A10` declares `ReloadAmmoPool@1` twice: `aircraft-america.yaml:483` with `AmmoPool: primary-ammo`, and
`:513` with `AmmoPool: secondary-ammo`. Duplicate trait keys inside one actor resolve last-value-wins at
the first key's position, so the actor fields **one** `ReloadAmmoPool@1`, bound to `secondary-ammo`. The
30 mm GAU-8's `primary-ammo` pool has no `ReloadAmmoPool` at all — it would never refill at a supply
point. The `WithAmmoPipsDecoration@1`/`@2` pair beside it is correctly numbered, which is what makes the
mistake read as a copy-paste.

`A10` is `Prerequisites: ~disabled`, so **zero player impact today**. Recorded because it is a genuine
functional defect rather than a cosmetic one, and because it corroborates the separate finding that the
A-10's weapons are entirely unreachable.

---

## D5 — Every dedicated anti-air platform omits `Penetration`. **LATENT (all five actors disabled)**

This one looked like the headline until the reachability filter was applied. It is worth recording
precisely because of that.

`Penetration` defaults to **1** (`DamageWarhead.cs:24`), and `InflictDamage` reduces damage by
`Penetration / Thickness` whenever penetration is short (`:216-231`). Every *fieldable* anti-air weapon
specifies a penetration; every *disabled* one omits it:

| weapon | platform | fieldable? | damage | penetration |
|---|---|---|--:|--:|
| `MANPAD` | `AA` infantry, 300 cr | yes | 3000 | 15 |
| `Stinger.quad` | `strykershorad`, 2500 cr | yes | 5000 | 20 |
| `9M311` | `tunguska`, 1700 cr | yes | 5000 | 20 |
| `12.7mm.Hind.AA` | `HIND` | yes | 300 | 5 |
| `7.62mm.Minigun.AA` | `littlebird` | yes | 150 | 4 |
| `Hellfire` | `HELI` | yes | 10000 | 800 |
| `SurfaceToAirMissile.double` | `SAM` 2000 cr / `HSAM` 3000 cr | **no — `~disabled`** | 2000 | **_1_** |
| `AACannon` | `AGUN` 800 cr | **no — `~disabled`** | 100 | **_1_** |
| `AirToAirMissile` | `F16`, `MIG` | **no — `~disabled`** | 1000 | **_1_** |

If those five actors were re-enabled unchanged: a `SAM` would deal `2000 × 1/20 = 100` to an Mi-28
(800 HP) — eight hits — while a single 300-credit MANPAD infantryman deals 2250 in one shot. An `F16`'s
dedicated air-to-air missile would deal 50 to that same Mi-28, out-damaged **12×** by the jet's own
secondary cannon (`20mm_CRAM`, 600 damage, `Penetration: 40`). None of these has a penetrating
`Warhead@Target` sibling to fall back on — the omitting warhead is the only damage warhead on the
weapon.

**No action needed now.** This is a trap for whoever re-enables static air defence, and the right place
for it is a note beside those actors, not a fix to disabled content.

---

## D6 — REFUTED: "supply caches below 50 serve nobody and never despawn"

The claim is stale **and inverted**.

`SUPPLYCACHE` (`rules/misc.yaml:436`) carries `RemoveBelowSupply: 1`, with an inline comment naming this
exact bug: *"A higher threshold would silently vanish a freshly-dropped crate carrying less than that
value — the 'inert crate' report."* Git bears that out: `1bd7b5c4` introduced `RemoveBelowSupply: 50`,
and `092db848` ("dropped crate matches truck — enemy auto-targets it, **fresh drops persist**") changed
it to `1`. `092db848` is an ancestor of `d919c81a`.

The real historical bug was the opposite of the claim: a sub-50 cache **despawned immediately**, not
that it never despawned. Today a cache below 50 both serves and despawns correctly — `AbsorbsSupplyCache`
transfers `min(TransferRate, headroom, CurrentSupply)` (so a 20-supply crate hands over its 20), and
`SupplyProvider.Tick` disposes the actor once `currentSupply < 1`.

---

## Two structural notes the matrix surfaced, offered as observations rather than defects

**`Versus` is essentially unused in WW3MOD.** 7 of 205 damage warheads carry a table. The mod grades
damage through `Penetration` vs the victim's per-actor `Armor.Thickness`, not through armour class. The
brief's framing — "an unlisted armour class takes 100 % damage, so every omission is a balance bug" — is
correct about the engine but describes a mechanism this mod has almost entirely stopped using. The
equivalent trap here is an omitted `Penetration`, and the matrix's Matrix B is where that lives.

**No instance of the conditional-multiplier trap is live.** The two `FirepowerMultiplier` traits with
`Modifier: 0` (`@CrashDisabled` on `crash-disabled`, `@EmergencyDescent` on `autorotation ||
crash-landing`) both sit on conditions that are genuinely granted and are transient crash states. The
15 armaments gated `PauseOnCondition: !alwaysdisabled` are all husks, where a permanently-paused
armament is the intent. The `FirepowerMultiplier@NoGunner` that once zeroed a helicopter's guns is gone.
1230 firepower/damage multipliers were scanned; none gate live damage on a condition I could not show is
grantable.

---

## Predictions registered before verifying — including the wrong ones

| # | prediction | outcome |
|---|---|---|
| 1 | D3's "map override fails to load" is **false**, because `conventions.md` states a duplicate trait key merges last-wins and "does not throw" | **WRONG.** That rule describes the single-source path only. `MergePartial` throws via `IntoDictionaryWithConflictLog` the moment a second source names the actor. Verified by simulation. |
| 2 | `^Wall` and `^SummonBase` have `Health` but no `Armor`, so `victim.Trait<Armor>()` (`:216`, unguarded) would throw on any hit | **WRONG.** Both are abstract templates; all six concrete wall actors carry `Armor`. No crash path. |
| 3 | The SAM / AGUN / F16 default-`Penetration` gap is the headline live defect | **WRONG.** All five platforms are `Prerequisites: ~disabled`. Demoted to D5, latent. Caught only by adding a fieldability filter — without it this would have been reported as the top finding. |
| 4 | Damage grading in this mod lives in `Versus`, as the brief's framing implies | **WRONG.** 7 of 205 warheads use it. The real model is `Penetration`/`Thickness`. |
| 5 | Defect #1 (Mi-28) is true | correct, and understated — no fallback weapon reaches air either |
| 6 | Defect #2 (Iskander/HIMARS) is true | correct, exactly as stated: one nonexistent class zeroed, three real ones omitted |
| 7 | Defect #4 (supply caches) is stale | correct, and the claim is also directionally inverted |

## What I could not verify, and where I may be wrong

- **The player-facing surfacing of D3.** I proved the merge throws; I did not observe what a player
  sees. Calling it a hang is unsupported and I have not repeated it.
- **The fieldability filter is the load-bearing judgement in this report and it is heuristic.** "Player
  can meet this" = actor is `Buildable` without `~disabled`, **or** appears as an actor type in a
  shipped `map.yaml`. The map half is a regex over `map.yaml` actor blocks; it will miss anything spawned
  only from Lua, from a support power, or from a bot module's build list. If `SAM`/`AGUN` are in fact
  reachable by some path I did not model, **D5 is live and belongs above D2.** That is the single
  finding in this report most likely to be wrong.
- **`Warhead@Spread` with default penetration is assumed intentional.** I read the pattern off 23 of 30
  fieldable cases having a penetrating `Warhead@Target` sibling and concluded the splash/aimed split is
  deliberate. That is inference from consistency, not from a design document or a comment. If the split
  was never intended, a large number of Matrix B's bolded cells become defects rather than design.
- **No in-engine measurement.** Every number here is static: resolved YAML plus the damage formula read
  from `DamageWarhead.cs`. Nothing was run. Damage modifiers applied outside the warhead
  (`args.DamageModifiers`, veterancy, suppression, `DamageAtMaxRange` range falloff, `SpreadDamage`
  falloff at distance) are **not** folded into Matrix B — it shows the thickness reduction alone, at the
  point of impact.
- **`conventions.md:181` appears to be wrong** and I did not edit it (read-only brief). It says two
  `Warhead@Smudge` blocks in one weapon "really do both load" because `LoadWarheads` does a raw
  `StartsWith("Warhead")` with no de-duplication. But `WeaponInfo`'s constructor runs
  `MiniYaml.Merge` over the weapon's own nodes first (`WeaponInfo.cs:182`), and `MergeSelfPartial`
  merges same-key children before `LoadWarheads` ever sees them. Two *differently*-keyed warheads
  (`@Smudge1` / `@Smudge2`) both load; two identically-keyed ones merge. Worth a curation pass.
