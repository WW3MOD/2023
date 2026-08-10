# Visual hit-feedback audit — impacts, muzzles, trails

**Scope:** every weapon in `mods/ww3mod/rules/weapons/` (note: *not* `mods/ww3mod/weapons/`
— that path does not exist), cross-referenced against `mods/ww3mod/sequences/` and the
`Armament`/`WithMuzzleOverlay`/`Contrail` wiring in `mods/ww3mod/rules/ingame/`.

**Ref:** `main @ b3591ef5`, clean w.r.t. all files cited here (the only dirty paths in the
tree are `maps/river-zeta-ww3/*`). Branch is 123 commits ahead of `origin/main`; no upstream
pull was needed since nothing cited is remote-only.

**Method:** data only. I did not build, did not launch the game, did not run autotests.
Everything below is read off YAML, SHP headers, and engine C#. Where a judgement needs eyes
on the screen I say so explicitly in **What I could not determine** at the end.

---

## Executive summary

- **161 weapon entries audited** — 128 concrete weapons + 33 `^Templates` across the seven
  files in `rules/weapons/`.
- **39 effect sequences** are defined in the `explosion` image
  (`sequences/sequences-ingame.yaml:205-271`).
- **Dangling `Explosions:` references: ZERO.** Every effect name used by every warhead
  resolves to a real sequence, and every one of those resolves to a real SHP. The
  "silently-nothing" theory does not hold at the name-resolution level — the engine's
  `[SequenceReference]` lint would have failed the load anyway (proof that the lint is live:
  the MiG's muzzle line is commented out *with the lint error pasted next to it*,
  `rules/ingame/aircraft-america.yaml:586`).
- **But there are four other ways to render silently nothing, and all four are present.**
  A warhead whose `ValidTargets` misses the victim's target types, a `CreateEffect` with no
  `Explosions:` list at all, a `MuzzleSequence` with no `WithMuzzleOverlay` trait to draw it,
  and a 5×4-pixel sprite. Those are the real findings.

### Top 5 worth fixing

| # | Finding | Severity |
|---|---|---|
| 1 | **Every rifle / MG / HMG hit on an airborne helicopter renders nothing at all** — no sprite, no sound. `^PiffEffects`/`^PiffsEffects` `Warhead@AirEffect` is a `CreateEffect` with an empty `Explosions:` list. | Broken |
| 2 | **Hellfire and Ataka hits on helicopters render nothing.** Every visual warhead in `^MediumExplosionEffects` is gated to `Ground/Ship/Trees/Water`; an airborne heli is `Air, Helicopter`. The repo already documents the *damage* half of this bug ("perceived as missile silently vanished") but the visual half was never closed. | Broken |
| 3 | **`hit_minimal` is a 5×4-pixel sprite** and it is the *only* dedicated impact flash the IFV autocannons get. Its authored sibling `hit_small` (10×8) exists and is referenced by nothing. This is the concrete substance behind "the IFVs have no proper hit animation". | Broken |
| 4 | **The `^30mm` family is split.** Tunguska-AG, A-10 and TimerWolf additionally inherit `^PiffEffects` (visible spark); Bradley, BMP-2 and the Apache/Mi-28 chain gun do not. Same weapon class, different treatment — and the Tunguska is exactly the unit the user is calibrating against. | Broken |
| 5 | **Su-25 declares `MuzzleSequence: muzzle` but carries no `WithMuzzleOverlay` trait** → inert. And the **A-10's GAU-8 has neither** — the loudest gun in the game has no muzzle flash while the Apache's chain gun does. | Broken |

---

## 1. BROKEN — renders nothing, or nothing you can see

### 1.1 Small-arms impacts on helicopters are completely invisible

`^PiffEffects` (`rules/weapons/weapons-effects.yaml:13-22`) and `^PiffsEffects` (`:2-11`):

```
Warhead@PiffEffect: CreateEffect
    Explosions: piff
    ValidTargets: Ground, Trees      # weapons-effects.yaml:16
Warhead@AirEffect: CreateEffect
    ValidTargets: Helicopter          # weapons-effects.yaml:21
    ImpactActors: true                # ...and NO Explosions: line
```

An **airborne** helicopter's target types are `Air, AirDetonateAttack` +`Helicopter`
(`rules/ingame/aircraft.yaml:36-38`, `:164-165`) — it is **not** `Ground`. So:

- `Warhead@PiffEffect` is invalid against it. In `CreateEffectWarhead.DoImpact` the
  "only invalid actors at impact" branch returns early
  (`engine/OpenRA.Mods.Common/Warheads/CreateEffectWarhead.cs:113-116`) — no piff.
- `Warhead@AirEffect` *is* valid, but `Explosions` defaults to an empty array
  (`CreateEffectWarhead.cs:26`), so `Explosions.RandomOrDefault(...)` returns null and the
  `SpriteEffect` is never added (`:122`). `ImpactSounds` is empty too, so no sound either.

This propagates to **every infantry and vehicle machine gun in the mod**, because
`^SmallCaliberEffects` (`:25`), `^MediumCaliberEffects` (`:64`) and `^LargeCaliberEffects`
(`:102`) all inherit `^PiffEffects` — and all three calibre families list `Helicopter` in
`ValidTargets` (`weapons-ballistics.yaml:4, 144, 215`). `7.62mm.Minigun` has its own copy of
the same empty stub at `weapons-ballistics.yaml:208-210`.

Tell that this is an accident rather than a decision: `Pistol` and `SilencedPPK` write their
piff warheads by hand and *do* include `Air` in `ValidTargets`
(`weapons-ballistics.yaml:31`, `:49`). The pistol shows sparks on an airframe; the M2 Browning
does not.

> **Proposed:** add `Explosions: piff` to `^PiffEffects Warhead@AirEffect`
> (`weapons-effects.yaml:20-22`) and `Explosions: piffs` to `^PiffsEffects Warhead@AirEffect`
> (`:9-11`); same for `7.62mm.Minigun Warhead@AirEffect` (`weapons-ballistics.yaml:208-210`,
> also flip its `ImpactActors: false` → `true`, or it will never fire). Both sequences are
> defined at `sequences/sequences-ingame.yaml:209-210`.
> *Realism:* rounds striking an airframe throw the same sparks and paint fragments they throw
> off a hull; a door gunner watching tracers vanish into a Hind with zero effect is the one
> thing that never happens.

### 1.2 Hellfire / Ataka hits on helicopters render nothing

`Hellfire` is `ValidTargets: Vehicle, Air, Defense` (`weapons-missiles.yaml:158`) and its
damage warheads explicitly target `Air` (`:183`, `:189`). `Ataka` carries a `Penetration: 20`
tuned specifically for heli-vs-heli (`:147-149`). So both are *meant* to kill helicopters.

But their only visual inheritance is `^MediumExplosionEffects`
(`weapons-missiles.yaml:153`, `:106`), whose every `CreateEffect` is gated to ground/water:

| Warhead | ValidTargets | file:line |
|---|---|---|
| `Warhead@Effect` (explosion_medium) | `Ground, Ship, Trees, Mine` | `weapons-effects.yaml:554` |
| `Warhead@EffectShrapnel` (shrapnel_medium) | `Ground, Ship, Trees, Mine` | `:559` |
| `Warhead@EffectWater` (splash_medium) | `Water, Underwater` | `:565` |

None of those match `Air`/`Helicopter`. A Hellfire that kills a Hind produces no impact
flash — only the victim's own `UnitExplodeHeli` death animation, one tick later.

The repo already half-knows about this. `weapons-missiles.yaml:192-201` documents the
*damage* version of the same bug in detail and ends "perceived as 'missile silently
vanished'". The damage was fixed; the picture never was.

> **Proposed:** add to `Hellfire` (`weapons-missiles.yaml:152`) and `Ataka` (`:105`):
> ```
> Warhead@EffectAir: CreateEffect
>     ValidTargets: Air, Helicopter
>     ImpactActors: true
>     Explosions: explosion_air_medium
>     ImpactSounds: kaboom25.aud
> ```
> `explosion_air_medium` is defined at `sequences/sequences-ingame.yaml:229-230` and is
> already what every AA missile uses (`^MediumExplosionEffectsAir`, `weapons-effects.yaml:708`).
> *Realism:* a 9 kg shaped charge into a rotorcraft is an airburst, not a puff of nothing.
>
> `TimerWolf_Missiles` (`weapons-missiles.yaml:219-221`) has the identical hole, but the
> Timber Wolf actor is commented out (`rules/ingame/vehicles.yaml:684-747`) — leave it.

### 1.3 `hit_minimal` is 5×4 pixels — this is the IFV report

I read the SHP headers directly (`bits/weapons/explosions/`, ShpTS layout: `u16 zero,
u16 width, u16 height, u16 frames`):

| Sprite | Size | Frames | Wired to |
|---|---|---|---|
| `hit_minimal.shp` | **5 × 4 px** | 22 | `^MinimalExplosionEffects` (`weapons-effects.yaml:459`) |
| `hit_small.shp` | **10 × 8 px** | 22 | **nothing** |
| `shrapnel_small.shp` | 15 × 15 px | 14 | `^MinimalExplosionEffects` (`:462`) |
| `shrapnel_medium.shp` | 25 × 25 px | 14 | `^SmallMedium`/`^Medium` (`:534`, `:558`) |
| `shrapnel_large.shp` | 50 × 50 px | 14 | `^Large`/`^Huge` (`:588`, `:619`) |

`hit_minimal` and `hit_small` are an obvious authored pair (identical 22-frame length, 2×
linear scale) and only the smaller one was ever wired. A 5×4-px flash is below the size at
which anything reads as an impact at normal zoom — which is exactly the "no proper hit
animation" report, arrived at from the data side.

`^MinimalExplosionEffects` is what `25mm.Bradley` and `30mm.BMP2` get, via
`^30mm` (`weapons-ballistics.yaml:362`). It is also what the **40mm grenade launcher** gets
(`:267`) and what `^SmallerExplosionEffects` reuses (`:483`).

> **Proposed:** `^MinimalExplosionEffects Warhead@Effect` (`weapons-effects.yaml:457-460`)
> `Explosions: hit_minimal` → `Explosions: hit_small`.
> Verified to exist: `sequences/sequences-ingame.yaml:214` (`hit_small: hit_small`), art at
> `bits/weapons/explosions/hit_small.shp`.
> *Realism:* a 25/30 mm HEI round on armour is a visible flash plus a spall spray — bigger
> than a rifle spark, far smaller than an RPG. `hit_small` + the existing `shrapnel_small`
> is precisely that.
>
> Leaves `hit_minimal` free for the genuinely tiny cases it is better suited to — it is
> already the low-power laser hit anim (`sequences-ingame.yaml:350-351`).

### 1.4 The `^30mm` family is split — Tunguska sparks, Bradley doesn't

All six ground-attack members inherit `^30mm` → `^MinimalExplosionEffects`. Three of them
*additionally* inherit `^PiffEffects`, and three don't. `MiniYaml.ResolveInherits` merges
each `Inherits@…` in document order and the parent's own `Inherits@` keys are consumed
inside the parent's recursion (`engine/OpenRA.Game/MiniYaml.cs:458-488`) — so the child's
`Inherits@HitEffects` **adds to** the inherited effects rather than replacing them. Both
sets fire.

| Weapon | Platform(s) | Extra `^PiffEffects`? | file:line |
|---|---|---|---|
| `25mm.Bradley` | Bradley, Stryker SHORAD | **no** | `weapons-ballistics.yaml:387-388` |
| `30mm.BMP2` | BMP-2 | **no** | `:410-411` |
| `30mm.Heli` | Apache, Mi-28 | **no** | `:481-483` |
| `30mm.Tunguska.AG` | Tunguska | **yes** | `:445-447` |
| `30mm.A10` | A-10 | **yes** | `:456-458` |
| `30mm.TimerWolf` | (actor disabled) | **yes** | `:492-494` |

So the Tunguska's ground burst is `hit_minimal` + `shrapnel_small` + **`piff`**; the
Bradley's and BMP-2's is `hit_minimal` + `shrapnel_small`. The user's reading — "the Tunguska
looks right, the IFVs look like nothing" — is a precise description of that one-effect delta,
compounded by 1.3 above.

Note this cuts **across** factions rather than along them: the Bradley (NATO) and the BMP-2
(BRICS) are treated identically-badly, and the Tunguska (BRICS) and A-10 (NATO)
identically-well. Not a faction asymmetry — a per-entry oversight.

> **Proposed:** add `Inherits@HitEffects: ^PiffEffects` to `25mm.Bradley`
> (`weapons-ballistics.yaml:388`), `30mm.BMP2` (`:411`) and `30mm.Heli` (`:482`), matching
> the line already present on `30mm.Tunguska.AG` (`:447`).
> *Realism:* an autocannon burst walking onto a target throws a spark train, not one flash.
> This is also the cheapest possible fix and can be taken **instead of** 1.3 if you'd rather
> not touch the shared `^MinimalExplosionEffects` template — though I'd take both.

### 1.5 Muzzle flashes that render nothing

`WithMuzzleOverlay` is what actually draws `Armament.MuzzleSequence`
(`engine/OpenRA.Mods.Common/Traits/Render/WithMuzzleOverlay.cs:21-22, 46, 86`). A
`MuzzleSequence` without the trait is inert, **and no lint catches it** — unlike a bad
sequence *name*, which does fail the load.

| Actor | Problem | file:line |
|---|---|---|
| **Su-25 (`FROG`)** | `MuzzleSequence: muzzle` on its rocket pods, **no `WithMuzzleOverlay`** anywhere in the actor block (lines 426-534). Inert. | `rules/ingame/aircraft-russia.yaml:462` |
| **A-10** | 30 mm GAU-8 armament has **no `MuzzleSequence` and no `WithMuzzleOverlay`**. The Apache's `30mm.Heli` has both (`aircraft-america.yaml:319`, `:326`). | `rules/ingame/aircraft-america.yaml:438-442` |
| **M270** | Has `WithMuzzleOverlay` but its armament declares no `MuzzleSequence` and the `m270` image defines no `muzzle` sequence. Dead trait. | trait `vehicles-america.yaml:790`; armament `:750-755`; sequences `sequences.yaml:234-249` |
| **HIMARS** | Same shape — `WithMuzzleOverlay` present, nothing to draw. | trait `vehicles-america.yaml:1082`; sequences `sequences.yaml:386-401` |
| **F-16 / MiG-29** | 20 mm gun muzzle deliberately disabled, with the lint error preserved in the comment. Genuinely blocked on missing art. | `aircraft-america.yaml:586`, `aircraft-russia.yaml:600` |

> **Proposed (A-10):** add `MuzzleSequence: muzzle` to `Armament@1`
> (`aircraft-america.yaml:438-442`) and a `WithMuzzleOverlay:` trait to the actor, then add
> `muzzle: gunfire2` (`Length: 5`) to the `a10` sequence block in
> `sequences/sequences-aircraft.yaml`. `gunfire2` is a real SHP
> (`bits/weapons/explosions/gunfire2.shp`) and is already the muzzle asset for every
> cannon-class weapon in the mod (`sequences.yaml:147`, `:217`, `:261`, `:275`, `:314`).
> *Realism:* the GAU-8 is the single most recognisable muzzle flash in modern air-ground
> warfare. It is currently the only gun-armed aircraft with no flash at all.
>
> **Proposed (Su-25):** add `WithMuzzleOverlay:` to the `FROG` actor block. The `frog` image
> already defines a `muzzle` sequence (`sequences/sequences-aircraft.yaml:88-90`,
> `muzzle: minigun`, `Length: 6`, `Facings: 8`) — the wiring is **one line** from working.
> Contrast the `mig` image (`sequences-aircraft.yaml:93-97`), which genuinely has no `muzzle`
> sequence: that is why the MiG's line had to be commented out and the Su-25's did not.
>
> **Proposed (M270/HIMARS):** either delete the two dead `WithMuzzleOverlay` traits, or give
> them a launch plume. I'd give them one — see §3.2.

---

## 2. INCONSISTENT — same class, different treatment

### 2.1 Bradley's muzzle is a tank-gun flash; BMP-2's is a machine-gun flash

Both are ~25/30 mm turret autocannon. They draw different assets, with different frame
counts, and only one of them rotates:

| Unit | Weapon | Muzzle sequence | Frames | Facings | file:line |
|---|---|---|---|---|---|
| Bradley | `25mm.Bradley` | `gunfire2` | 2 | **none** | `sequences.yaml:217-218` |
| Stryker SHORAD | `25mm.Bradley` | `gunfire2` | 2 | **none** | `sequences.yaml:230-231` |
| **BMP-2** | `30mm.BMP2` | `minigun` | 6 | **8** | `sequences.yaml:286-288` |
| **Tunguska** | `30mm.Tunguska.*` | `minigun` | 6 | **8** | `sequences.yaml:381-383` |

A sequence with no `Facings:` renders a single non-directional puff that does not follow the
turret. So the NATO IFV's autocannon flash is a 2-frame static blob borrowed from the tank-gun
asset, while the BRICS IFV's is a 6-frame 8-facing gun flash. **This is a real NATO-vs-BRICS
asymmetry, and NATO has the worse one.** For comparison the tank guns are consistent across
factions — Abrams `gunfire2` L5 (`sequences.yaml:147`), T-90 `gunfire2` L5 (`:275`),
T-72 `gunfire2` L5 (`:261`).

> **Proposed:** change `bradley` (`sequences.yaml:217-218`) and `strykershorad` (`:230-231`)
> from `muzzle: gunfire2 / Length: 2` to `muzzle: minigun / Length: 6 / Facings: 8`, matching
> `bmp2` and `tunguska` exactly.
> *Realism:* the M242 Bushmaster and the 2A42 are the same class of weapon; they should flash
> the same way, and a turret-mounted flash should point where the barrel points.

### 2.2 A hand grenade explodes bigger than a 40 mm grenade launcher round

Both inherit `^MinimalExplosionEffects`. `HandGrenade` then *overrides* the effect upward;
`GrenadeLauncher` leaves it at the 5×4 px default.

| Weapon | Effect | file:line |
|---|---|---|
| `HandGrenade` | `explosion_medium` (= `veh-hit2`) + `kaboom25.aud` | `weapons-ballistics.yaml:261-263` |
| `GrenadeLauncher` | `hit_minimal` (inherited, 5×4 px) + `kaboom12.aud` | `weapons-ballistics.yaml:267` |

A thrown M67 renders four size-classes bigger than a 40 mm HEDP round. That is backwards.

> **Proposed:** demote `HandGrenade Warhead@Effect` (`weapons-ballistics.yaml:261-263`) from
> `explosion_medium` to `explosion_small` (`sequences-ingame.yaml:216`), and let
> `GrenadeLauncher` ride the `hit_minimal` → `hit_small` change in §1.3.
> *Realism:* ~180 g of Composition B (hand grenade) vs ~32 g (40 mm) — the grenade *should*
> be the larger of the two, just not by four rungs, and neither should out-bang an RPG.

### 2.3 M270 gets `explosion_large`, Grad gets `explosion_medium`

Same battlefield role, different impact class:

| System | Calibre | Effect template | Impact sprite | file:line |
|---|---|---|---|---|
| **M270 MLRS** (NATO) | 227 mm | `^LargeExplosionEffects` | `explosion_large` (`frag1`) | `weapons-ballistics.yaml:769` |
| **BM-21 Grad** (BRICS) | 122 mm | `^MediumExplosionEffects` | `explosion_medium` (`veh-hit2`) | `weapons-ballistics.yaml:701` |
| **TOS-1** (BRICS) | 220 mm thermobaric | `^LargeThermobaricEffects` | `flak_large` + `explosion_large` + scorch | `weapons-ballistics.yaml:735` |

The TOS's bespoke thermobaric treatment is good and clearly deliberate. The Grad is the odd
one out: its 122 mm rocket impact currently renders identically to a 73 mm BMP-1 gun round
(`73mm_BMP`, `weapons-ballistics.yaml:549`) and to a tank main gun (`^TankRound`, `:575`).

This one is genuinely arguable on realism — a 122 mm Grad warhead is ~19 kg of HE against
~107 kg for an M26 — so the large/medium split is not absurd. What is hard to defend is Grad
sharing a rung with the BMP-1's low-pressure gun.

> **Proposed:** move `GradRockets` (`weapons-ballistics.yaml:701`) from
> `^MediumExplosionEffects` to `^LargeExplosionEffects`, matching M270. **Or** leave it and
> accept the calibre argument — this is the one item in §2 I'd call close to even.
> *Realism note:* if you keep the split, then `73mm_BMP` and `^TankRound` should move
> *down*, not Grad up. See §4.1.

### 2.4 Damaged aircraft show no damage; damaged vehicles show a ten-stage burn

Vehicles have an elaborate staged burning system driven by
`WithSequentialAnimation@CriticalDamage` and a documented 1→10 stack ramp
(`rules/ingame/vehicles.yaml:160-180`). Aircraft get `^WhenDamagedAir`
(`rules/ingame/aircraft.yaml:321-338`) — **pure stat modifiers, zero visual**. And every
`SmokeTrailWhenDamaged` in the mod is commented out:

- F-16 — `rules/ingame/aircraft-america.yaml:614-616`
- Su-25 — `rules/ingame/aircraft-russia.yaml:517-519`
- MiG-29 — `rules/ingame/aircraft-russia.yaml:631-633`

So a Su-25 at 5 % HP is visually identical to one at 100 %.

> **Proposed:** uncomment the three `SmokeTrailWhenDamaged` blocks and gate them on the
> existing `critical-damage` condition (already granted mod-wide — see
> `DOCS/reference/conventions.md:52`).
> *Realism:* a smoking engine is the single most legible "that one's hurt" cue in air combat,
> and it is the one the mod already uses for ground units.

### 2.5 A-10 has no contrails; every other fixed-wing does

| Aircraft | Contrails | file:line |
|---|---|---|
| Badger | yes, `TrailLength: 8` | `rules/ingame/aircraft.yaml:403-408` |
| F-16 | yes | `rules/ingame/aircraft-america.yaml:610-613` |
| Su-25 | yes, `TrailLength: 10` | `rules/ingame/aircraft-russia.yaml:512-517` |
| MiG-29 | yes, `TrailLength: 6` | `rules/ingame/aircraft-russia.yaml:626-631` |
| **A-10** | **commented out** | `rules/ingame/aircraft-america.yaml:508-513` |

Realism actually argues the *other* way here: contrails form from engine exhaust at altitude
and humidity that low-level CAS aircraft never see. So the physically-correct fix is to strip
the Su-25's rather than add the A-10's — the two are the same mission profile. But contrails
are also the main readability cue for "there is a fast mover overhead", which is a legitimate
gameplay reason to keep them.

> **Proposed:** pick one and apply it to both CAS aircraft. My preference: **uncomment the
> A-10's** (`aircraft-america.yaml:508-513`) for readability parity, since the Su-25's is
> already shipping and removing it is a visible regression for BRICS players. Flagging that
> this is the less realistic of the two directions.

---

## 3. Muzzle flashes — full inventory

Every live `MuzzleSequence` resolves (the engine lint guarantees it). The complete map:

### 3.1 Wired and working

| Unit | Weapon | Sequence → asset | Frames / Facings | Sequence file:line |
|---|---|---|---|---|
| Abrams | `TankRound.Abrams` | `gunfire2` | 5 / — | `sequences.yaml:147-148` |
| T-90 | `TankRound.T90` | `gunfire2` | 5 / — | `sequences.yaml:275-276` |
| T-72 | `TankRound.T72` | `gunfire2` | 5 / — | `sequences.yaml:261-262` |
| Giatsint | `ArtilleryRound.Giatsint` | `gunfire2` | 5 / — | `sequences.yaml:314-315` |
| Paladin | `ArtilleryRound.Paladin` | `smokeygun` | 12, `Tick: 30` / — | `sequences.yaml:203-205` |
| Bradley | `25mm.Bradley` | `gunfire2` | 2 / — | `sequences.yaml:217-218` — **see §2.1** |
| Stryker SHORAD | `25mm.Bradley` | `gunfire2` | 2 / — | `sequences.yaml:230-231` — **see §2.1** |
| BMP-2 | `30mm.BMP2` | `minigun` | 6 / 8 | `sequences.yaml:286-288` |
| Tunguska | `30mm.Tunguska.AG/AA` | `minigun` | 6 / 8 | `sequences.yaml:381-383` |
| Humvee | `7.62mm.MG` | `minigun` | 6 / 8 | `sequences.yaml:169-171` |
| M113 | `12.7mm.MG` | `minigun` | 6 / 8 | `sequences.yaml:179-181` |
| BTR-80 | `12.7mm.MG` | `minigun` | 6 / 8 | `sequences.yaml:132-134` |
| Littlebird | `7.62mm.Minigun` | `minigun` | 6 / 8 | `sequences-aircraft.yaml:139-141` |
| Apache (`heli`) | `30mm.Heli` | `minigun` | 6 / 8 | `sequences-aircraft.yaml:149-151` |
| Hind | `12.7mm.Hind` | `minigun` | 6 / 8 | `sequences-aircraft.yaml:61-63` |
| Mi-28 | `30mm.Heli` | `minigun` | 6 / 8 | `sequences-aircraft.yaml:71-73` |
| Pillbox / Heavy pillbox | MG | `minigun` | — | `sequences-defenses.yaml:42`, `:66` |
| CRAM | `20mm_CRAM` | `gunfire2` | — | `sequences-defenses.yaml:224` |
| AGUN | `AACannon` | `gunfire2` | — | `sequences-defenses.yaml:266` |
| SAM / HSAM | `SurfaceToAirMissile.double` | **`samfire`** | — | `sequences-defenses.yaml:296`, `:369` |
| GUN turret | `TankRound.t90` | `gunfire2` | — | `sequences-defenses.yaml:847` |
| Flame turret | `Flamespray.heavy` | `muzzle-spray` | — | `sequences-defenses.yaml:90` |
| Flamethrower infantry | `Flamespray` | `e4` | — | `sequences-infantry.yaml:420` |

### 3.2 Missing where a flash is warranted

| Unit / weapon | Real-world class | Current | Proposed |
|---|---|---|---|
| **A-10 / `30mm.A10`** | GAU-8 Avenger 30 mm rotary | nothing (§1.5) | `muzzle: gunfire2`, `Length: 5` on the `a10` image + `MuzzleSequence: muzzle` + `WithMuzzleOverlay:` |
| **Su-25 / `RocketPods`** | S-8 80 mm pods | `MuzzleSequence` present but inert (§1.5) | add `WithMuzzleOverlay:` — `muzzle` already exists at `sequences-aircraft.yaml:88-90` |
| **Tunguska / `9M311`** | 57E6 SAM launch | none — `Armament@2`, `rules/ingame/vehicles-russia.yaml:858-865` | `muzzle-missile: smokey` on the `tunguska` image + `MuzzleSequence: muzzle-missile` |
| **Stryker SHORAD / `Stinger.quad`, `Hellfire.strykershorad`** | Stinger / AGM-114 launch | none — `rules/ingame/vehicles-america.yaml:896-935` | `muzzle-missile: smokey` on the `strykershorad` image + `MuzzleSequence: muzzle-missile` |
| **M270, HIMARS, Grad, TOS** | MLRS launch plume | M270/HIMARS carry a dead `WithMuzzleOverlay`; Grad/TOS have none | `muzzle-missile: smokey` on all four images + `MuzzleSequence: muzzle-missile` on the armaments |

All five proposals use `smokey`, which is a real image (`sequences-ingame.yaml:24-27`) and has
an existing `muzzle-missile: smokey` precedent in the tree at `sequences.yaml:473` (the
Timber Wolf's missile muzzle — actor disabled, but the sequence pattern is proven).

*Realism:* a vertical- or box-launched missile leaves a large white efflux cloud at the
launcher, and an MLRS salvo is the most visually dramatic launch signature on a modern
battlefield. Right now all six rocket platforms fire in complete silence, visually.

### 3.3 Deliberately absent — leave alone

- **All infantry small arms.** Only the flamethrower has a muzzle
  (`rules/ingame/infantry.yaml:1965`). Infantry sprites are far too small for a flash to read
  as anything but noise, and the garrison variants already have `garrison-muzzle` for the
  building-window case (`sequences-infantry.yaml:207` and 8 others).
- **F-16 / MiG-29 20 mm.** Blocked on missing art, correctly disabled with the lint error
  documented inline (`aircraft-america.yaml:586`, `aircraft-russia.yaml:600`).
- **Iskander.** `IskanderTargeter` is an `InstantHit` designator, not a gun
  (`weapons-missiles.yaml:244-250`). The real launch visual is the spawned missile actor with
  its `LeavesTrailsCA` exhaust (`rules/ingame/vehicles-russia.yaml:1036-1040`) — correct.

---

## 4. Impact / explosion ladder — the full table

`Explosions:` names are sequences inside the `explosion` image
(`CreateEffectWarhead.cs:29`, `Image` defaults to `"explosion"`), defined at
`sequences/sequences-ingame.yaml:205-271`.

### 4.1 The ladder as it stands

| Template | Impact sprite → asset | Shrapnel | Water | Sound | file:line |
|---|---|---|---|---|---|
| `^PiffEffects` | `piff` | — | `water_piff` | — | `weapons-effects.yaml:13-22` |
| `^PiffsEffects` | `piffs` | — | `water_piffs` | — | `:2-11` |
| `^MinimalExplosionEffects` | `hit_minimal` (5×4 px) | `shrapnel_small` | `splash_small` | `kaboom12` | `:448-470` |
| `^SmallerExplosionEffects` | `hit_minimal` (**identical visual**) | `shrapnel_small` | `splash_small` | `kaboom12` | `:472-494` |
| `^SmallExplosionEffects` | `explosion_small` → `veh-hit3` | `shrapnel_small` | `splash_small` | `kaboom12` | `:496-518` |
| `^SmallMediumExplosionEffects` | `explosion_medium` → `veh-hit2` | `shrapnel_medium` | `splash_medium` | `kaboom12` | `:520-542` |
| `^MediumExplosionEffects` | `explosion_medium` (**identical visual**) | `shrapnel_medium` | `splash_medium` | `kaboom12` | `:544-566` |
| `^LargeExplosionEffects` | `explosion_large` → `frag1` | `shrapnel_large` | `splash_large` | `kaboom15` | `:568-597` |
| `^HugeExplosionEffects` | `building, building2` → `fball1` | `shrapnel_large` | `splash_large` | `kaboom22` | `:599-628` |
| `^LargeThermobaricEffects` | `flak_large` + `explosion_large` + scorch | — | `splash_large` | `firebl3`+`kaboom12` | `:630-681` |

**Two rungs are visual duplicates.** `^MinimalExplosionEffects` and
`^SmallerExplosionEffects` render identically (they differ only in shrapnel damage, 25 vs 50);
so do `^SmallMediumExplosionEffects` and `^MediumExplosionEffects` (150 vs 200). So the ten
named templates cover only **eight** distinct pictures, and the compression is in exactly the
band where most of the game's weapons live.

### 4.2 Weapon → template map

| Weapon | Real-world class | Template | Verdict |
|---|---|---|---|
| `9mm`, `5.56mm.*`, `MP5` | rifle / SMG | `^SmallCaliberEffects` → `piff` | OK |
| `7.62mm.*` | GPMG / DMR | `^MediumCaliberEffects` → `piff` | OK |
| `12.7mm.*` | HMG | `^LargeCaliberEffects` → `piff` | **Taste** — same picture as a 5.56; see §4.3 |
| `7.62mm.Minigun` | minigun | `^PiffsEffects` → `piffs` | OK |
| `25mm.Bradley` / `30mm.BMP2` / `30mm.Heli` | autocannon | `^MinimalExplosionEffects` | **Broken** §1.3, §1.4 |
| `30mm.Tunguska.AG` / `30mm.A10` | autocannon | `^Minimal` + `^PiffEffects` | reference point |
| `HandGrenade` | frag grenade | `^Minimal`, overridden to `explosion_medium` | **Inconsistent** §2.2 |
| `GrenadeLauncher` | 40 mm HEDP | `^MinimalExplosionEffects` | **Broken** §1.3 |
| `60mm_Mortar` | 60 mm mortar | `^SmallExplosionEffects` | OK |
| `RPG` | RPG-7 / AT4 | `^MediumExplosionEffects` | OK |
| `73mm_BMP` | 2A28 low-pressure gun | `^SmallMediumExplosionEffects` | **Taste** — see §4.3 |
| `^TankRound` (Abrams/T-72/T-90) | 120/125 mm APFSDS-HE | `^MediumExplosionEffects` | **Taste** — see §4.3 |
| `ATGM`, `WGM`, `Hellfire`, `Ataka` | ATGM | `^MediumExplosionEffects` | OK on ground; **Broken** vs air §1.2 |
| `RocketPods` | S-8 / Hydra 70 | `^MediumExplosionEffects` | **Taste** — an 80 mm rocket rendering as a 125 mm tank round |
| `^ArtilleryRound` (Paladin/Giatsint) | 155/152 mm | `^LargeExplosionEffects` | OK |
| `M270Rockets` | 227 mm MLRS | `^LargeExplosionEffects` | OK |
| `GradRockets` | 122 mm MLRS | `^MediumExplosionEffects` | **Inconsistent** §2.3 |
| `TosRockets` | 220 mm thermobaric | `^LargeThermobaricEffects` | OK — best-modelled weapon in the file |
| `ATMine` | AT mine | `^HugeExplosionEffects` | OK |
| `IskanderExplosion` / `HIMARSExplosion` | SRBM / GMLRS | `^HugeExplosionEffects` + `ShockwaveDamage` | OK |
| `Atomic` | tactical nuke | bespoke, 5-phase | OK — `nuke_large` at `ScalePercent: 300` |

### 4.3 The middle of the ladder is flat (Taste)

A 73 mm low-pressure gun, a 120 mm APFSDS, an ATGM, a Hellfire and an 80 mm rocket pod all
render the **same** `explosion_medium` sprite. Meanwhile the two rungs below it are the same
picture as each other, and `explosion_minimal` (`sequences-ingame.yaml:215`) sits unused.

There is genuine headroom to spread this out, but it is a taste call about how much visual
vocabulary the mod wants, so I've kept it out of the ordered list below except as an
optional last item.

> **Optional:** `^SmallerExplosionEffects` (`weapons-effects.yaml:481-484`) → `explosion_small`
> so it stops duplicating `^MinimalExplosionEffects`; `^SmallMediumExplosionEffects`
> (`:529-532`) → `explosion_small` so a 73 mm gun stops matching a 125 mm round. Both target
> sequences verified at `sequences-ingame.yaml:215-216`.

### 4.4 `VehicleCookoffTiny` uses a bullet-spark sprite for a burning vehicle (Broken-ish)

The three cook-off tiers (`rules/weapons/weapons-explosions.yaml`):

| Weapon | Used by | Effect | Sound | file:line |
|---|---|---|---|---|
| `VehicleCookoff` | crewed vehicles generally | `explosion_small` | `kaboom25.aud` | `:24-27` |
| **`VehicleCookoffTiny`** | Humvee, M113, BTR-80 | **`piff`** | **`gun27.aud`** (a pistol shot) | `:49-52` |
| `VehicleCookoffLarge` | M270, Grad, TOS | `explosion_medium` | `kaboom30.aud` | `:71-74` |

The authoring comment right above it says "fuel fire, not catastrophic ammo cookoff"
(`:32-34`) — the *intent* is fire. `piff` is the bullet-ricochet spark, and `gun27.aud` is
the pistol report used by `Pistol` and `DogJaw` (`weapons-other.yaml:318`). So a burning
Humvee currently goes *tink*.

> **Proposed:** `VehicleCookoffTiny Warhead@Effect` (`weapons-explosions.yaml:49-52`):
> `Explosions: piff` → `Explosions: napalm_small`, `ImpactSounds: gun27.aud` →
> `ImpactSounds: firebl3.aud`. Both verified: `napalm_small` at `sequences-ingame.yaml:250`,
> `firebl3.aud` already used by `NapalmFX`/`Flamespray`/`BarrelExplode`.
> *Realism:* a light APC brewing up is a fuel fire — small, orange, smoky. The existing
> `GrantExternalCondition Warhead@Fire` right below it (`:44-48`) already models it as fire;
> only the picture disagrees.

---

## 5. Anti-air — and a purpose-built asset family nobody wired up

### 5.1 Current AA picture

| Weapon | Platform | Air effect | Ground/miss effect | file:line |
|---|---|---|---|---|
| `20mm_CRAM` | CRAM, F-16, MiG-29 | `explosion_air_small` → `flak` | none | `weapons-ballistics.yaml:326` |
| `AACannon` | AGUN | `explosion_air_small`, `ValidTargets` incl. Ground/Water/Trees | same warhead | `weapons-ballistics.yaml:357-359` |
| `30mm.Tunguska.AA` | Tunguska | `explosion_air_small` | none | `weapons-ballistics.yaml:454` |
| `30mm.Fighter` | (fighters) | `explosion_air_small` | none | `weapons-ballistics.yaml:508` |
| `FlakFX` | ambient flak | `explosion_air_small` + `aacanon3.aud` | none | `weapons-explosions.yaml:426-429` |
| `SurfaceToAirMissile` | SAM/HSAM | `explosion_air_medium` → `veh-hit1` | `explosion_small` | `weapons-missiles.yaml:269`, `:296-299` |
| `AirToAirMissile` | F-16, MiG-29 | `explosion_air_medium` | `explosion_small` | `weapons-missiles.yaml:308`, `:332-335` |
| `MANPAD` | AT infantry | `explosion_air_medium` | `explosion_small` | `weapons-missiles.yaml:338`, `:365-368` |
| `Stinger` / `.quad` / `9M311` | Stryker, Tunguska | `explosion_air_medium` | `explosion_small` | `weapons-missiles.yaml:371`, `:398-401` |

Misses are handled correctly: every SAM/AAM sets `ExplodeWhenEmpty: true` and carries a
`Warhead@EffectGround`, so an overshooting missile self-destructs with a visible burst rather
than vanishing. `^MediumExplosionEffectsAir Warhead@AirEffect` has `ImpactActors: true` and
`ValidTargets: Air`, and `IsValidAgainstTerrain` promotes any position above `AirThreshold` to
the `Air` target type (`CreateEffectWarhead.cs:150-157`), so an airburst with no actor at the
point still renders. **AA is the healthiest part of the FX stack.**

### 5.2 `flak_small` and `flak_medium` are authored, matched, and used by nothing

| Sprite | Size | Frames | References |
|---|---|---|---|
| `flak_small.shp` | 15 × 15 px | 12 | **0** |
| `flak_medium.shp` | 25 × 25 px | 12 | **0** |
| `flak_large.shp` | 50 × 50 px | 12 | 1 — and not as flak (`weapons-effects.yaml:667`, ground thermobaric flash) |

All three are defined as sequences (`sequences-ingame.yaml:221-223`) and all three ship as
custom art in `bits/weapons/explosions/`. They are a matched 15/25/50-px, 12-frame ladder —
unmistakably authored as an AA burst family. Every AA weapon in the mod instead uses
`explosion_air_small` (= RA's stock `flak` sprite, `sequences-ingame.yaml:227`) or
`explosion_air_medium` (= `veh-hit1`, `:229`).

Also unused: `flak_explosion_ground` (`:220`), which maps to the same stock `flak` asset.

> **Proposed:** point the air templates at the purpose-built art —
> `^MinimalExplosionEffectsAir` / `^SmallExplosionEffectsAir` (`weapons-effects.yaml:691`,
> `:701`) `explosion_air_small` → `flak_small`; `^MediumExplosionEffectsAir` (`:708`)
> `explosion_air_medium` → `flak_medium`; `^Large`/`^HugeExplosionEffectsAir` (`:718`, `:728`)
> → `flak_large`.
> *Realism:* a proximity-fuzed AA burst is a dark puff with a bright core, which is what
> these three sprites are and what `veh-hit1` (a vehicle hit spark) is not. This also finally
> gives the AA ladder three distinct rungs instead of two.
>
> **Caveat — this is the one proposal I cannot sanity-check from data.** I have the pixel
> dimensions and frame counts but not the pixels. If `flak_*` were drawn for a different
> palette or a different zoom they could look wrong. Worth eyeballing one shot before
> committing to all five call sites. Everything else in this document I'm confident in.

---

## 6. Projectile visuals — trails, contrails, tracers

### 6.1 In-flight sprite per weapon class

| Projectile `Image` | Used by | Sequence | Notes |
|---|---|---|---|
| `tracer_small` | `^30mm.Tunguska` | `sequences-ingame.yaml:524-528`, 32 facings | correct — visible tracer |
| `tracer_large` | `7.62mm.Minigun`, `20mm_CRAM`, `30mm.A10`, `30mm.TimerWolf`, `30mm.Fighter` | `sequences-ingame.yaml:530-534`, 32 facings | correct |
| **`grenade_small`** | **`^30mm` base** → Bradley, BMP-2, Apache/Mi-28 gun; also `GrenadeLauncher`, `60mm_Mortar` | `sequences-ingame.yaml:519-523`, **no facings** | **Inconsistent** — see below |
| `tankround` | `RPG`, `73mm_BMP`, `^TankRound` | `sequences-ingame.yaml:1-3` | OK |
| `120mm` | `^ArtilleryRound` | `sequences-ingame.yaml:6-8` | OK |
| `dragon` | ATGM/WGM/Hellfire/Ataka, `RocketPods`, `GradRockets`, `TimerWolf_Barrage`, `MANPAD`, `FlakFX`, `NapalmFX` | `sequences-ingame.yaml:19-22`, 32 facings | OK |
| `missile` | `TosRockets`, `M270Rockets`, `SurfaceToAirMissile`, `Stinger` | `sequences-ingame.yaml:34-37`, 32 facings | OK |
| `bomb` | `HandGrenade`, `DepthCharge` | `sequences-ingame.yaml:29-32` | OK |
| `fb5` / `fb6` / `FB1` | flamethrowers | `sequences-ingame.yaml:73-81`, `:53-56` | OK |

**The IFV autocannon fires a grenade sprite.** `^30mm` sets `Image: grenade_small`
(`weapons-ballistics.yaml:375`) with `ContrailStartWidth: 12` (`:373`). `25mm.Bradley`
(`:398-400`) and `30mm.BMP2` (`:421-423`) override only `Speed` and `Inaccuracy`, so they
inherit it — while `^30mm.Tunguska` (`:444`) and `30mm.A10` (`:469`) override to
`tracer_small` / `tracer_large`. `grenade_small` also has no `Facings:`, so it does not
orient to its flight path.

So on top of the impact gap in §1.3–1.4, the IFV round *in flight* is a non-rotating grenade
blob with a fat contrail, where the Tunguska's is a proper directional tracer. The two
halves of "the IFV autocannon looks wrong" are the same oversight in two places.

> **Proposed:** add `Image: tracer_small` to the `Projectile: Bullet` blocks of
> `25mm.Bradley` (`weapons-ballistics.yaml:398-400`), `30mm.BMP2` (`:421-423`) and
> `30mm.Heli` (`:489-491`), matching `^30mm.Tunguska` (`:444`).
> *Realism:* 25/30 mm autocannon belts are loaded roughly 1-in-4 tracer; the round you see is
> a streak, not a tumbling grenade.

### 6.2 Smoke trails (`TrailImage`)

`TrailImage: smokey` (`sequences-ingame.yaml:24-27`) is on: `RocketPods` (`:665`),
`GradRockets` (`:718`), `TosRockets` (`:752`), `M270Rockets` (`:785`), `ATGM` (`:23`),
`WGM` (`:81`), `Ataka` (`:137`), `SurfaceToAirMissile` (`:289`), `MANPAD` (`:356`),
`Stinger` (`:389`), `Flamespray`/`Flamespray.heavy` (`weapons-other.yaml:18`, `:91`).
`FireballLauncher` uses `fb2` (`weapons-other.yaml:167`).

**Missing:** `Hellfire` has `ContrailLength: 10` but **no `TrailImage`**
(`weapons-missiles.yaml:180-181`) — while `Ataka`, its direct BRICS counterpart, has both
(`:134-138`). Same for `AirToAirMissile` (`:324`) and `TimerWolf_Missiles` (`:236`).

> **Proposed:** add `TrailImage: smokey` + `TrailScalePercent: 75` to `Hellfire`'s projectile
> block (`weapons-missiles.yaml:180-181`), matching `Ataka` (`:137-138`) exactly.
> *Realism:* a solid-rocket ATGM leaves a smoke trail; the Hellfire and the Ataka use the same
> propulsion class and should trail the same. This is a straight NATO-vs-BRICS asymmetry with
> no design reason behind it.

### 6.3 `LeavesTrailsCA` — ballistic missile exhaust

Both live users are correct and symmetric: HIMARS (`rules/ingame/vehicles-america.yaml:1118`)
and Iskander (`rules/ingame/vehicles-russia.yaml:1040`, gated on an `ignited` condition so the
plume only appears after the rocket lights rather than during erection — nice detail). No
action.

---

## 7. Other findings in this family

- **Water impacts are complete and consistent.** `splash_small`/`_medium`/`_large`
  (`sequences-ingame.yaml:245-247`) are wired at every rung, and bullets get
  `water_piff`/`water_piffs` (`:211-212`). Every water warhead correctly sets
  `InvalidTargets: Ship, Structure, Bridge` so a hit on a hull doesn't also splash. Nothing
  to do.
- **Smudges/craters/scorch are consistent.** Every explosion template above
  `^MinimalExplosionEffects` leaves a `Crater`, and the thermobaric/nuke/napalm paths add
  `Scorch`. The `InvalidTargets: Vehicle, Structure, Wall, Husk, Trees` guard is applied
  uniformly. Nothing to do. (Cosmetic: `^LargeExplosionEffects` and `^HugeExplosionEffects`
  each declare `Warhead@Smudge` **twice** — `weapons-effects.yaml:579`+`:590` and `:608`+`:621`.
  MiniYaml merges duplicate keys, so the second silently wins and the effect is identical.
  Dead lines; harmless.)
- **Debris and shell casings: neither exists anywhere in the mod.** No `SpawnActorOnDeath`
  debris beyond husks, no casing ejection. That's a from-scratch feature, not a gap in
  existing wiring — out of scope for a fix list.
- **Death animations.** Infantry have them (`rules/ingame/infantry.yaml:74`, `:290`,
  `:298-300` for a dedicated in-water death, `:1088-1089` for rot); structures have them
  (`rules/ingame/structures.yaml:406-407` and 5 more). Vehicles deliberately have **no**
  generic death explosion — they leave a husk and fire only `Explodes@CrewCookoff`
  (`rules/ingame/vehicles.yaml:263-265`, `:294-296`), which is consistent with the crew-bailout
  model landed at `b3591ef5`. Correct as designed; only the `VehicleCookoffTiny` picture is
  wrong (§4.4).
- **Naval is entirely dead content.** `rules/ingame/naval.yaml`, `naval-america.yaml` and
  `naval-russia.yaml` contain **zero live actor definitions** (all commented) despite being
  loaded by `mod.yaml:122-124`. So `DepthCharge`, `DepthChargeDual`, `MarineSapper`,
  `UnitExplodeShip`, `UnitExplodeSubmarine`, the 11 commented `MuzzleSequence` lines in
  `naval.yaml`, and all 14 muzzle definitions in `sequences-naval.yaml` are unreachable.
  Nothing to fix; noting it so no one spends effort there.
- **Unused-but-defined effect sequences** (candidates to fill gaps): `hit_small` (→ §1.3),
  `flak_small`, `flak_medium` (→ §5.2), `explosion_minimal` (→ §4.3),
  `flak_explosion_ground`, `mininuke`, `hvnd`, `corpse`, `invisblank`
  (`sequences-ingame.yaml:214, 215, 220-222, 253, 260, 261, 269`).
- **`artillery_explosion` is defined (`sequences-ingame.yaml:239`) and referenced only from
  commented-out blocks** (`weapons-superweapons.yaml:19`, `weapons-explosions.yaml:604`). It
  maps to `art-exp1`, the same asset as `self_destruct`. Harmless.
- **`kaboom12.aud` is the impact sound for five consecutive rungs** — `^Minimal`, `^Smaller`,
  `^Small`, `^SmallMedium` and `^Medium` (`weapons-effects.yaml:460, 484, 508, 532, 556`). So
  a 25 mm ping and a 125 mm tank round land with the identical report. Out of strict scope
  (audio, not visual) but it compounds the flat-middle problem in §4.3.

---

## What I could not determine from data alone

These need eyes on the screen; I have deliberately not guessed:

1. **Whether `shrapnel_small` is actually legible on an IFV hit.** By the data, Bradley/BMP-2
   hits *do* spawn a 15×15-px `shrapnel_small` alongside the 5×4 `hit_minimal` — so they are
   not literally rendering nothing. My reading is that a 15-px debris spray with no flash in
   front of it doesn't register as an impact. **Fire a Bradley at a stationary T-90 and watch
   one burst.** If you can see the spray clearly, §1.3 is a smaller problem than I've rated it
   and §1.4 alone may be enough.
2. **Whether `flak_small`/`flak_medium` look right** (§5.2 caveat). Dimensions and frame
   counts say "AA burst family"; I have not seen the pixels or confirmed the palette.
3. **Whether `gunfire2 Length: 2` on the Bradley reads as a flash at all** — two frames is
   very short. Compare against the Abrams' five-frame version of the same asset.
4. **Whether the A-10's contrails were commented out deliberately** (§2.5). Nothing in the
   file explains it, and there's no commit reference inline. If it was a considered call,
   overrule me.
5. **Anything about how the effects composite in motion** — layering, Z-order, palette. All
   of that is `effect` palette with `ZOffset: 2047`, which I can read but not evaluate.

---

## Changes I would make, in order

Each line is a self-contained edit. Nothing here has been applied.

1. **Add `Explosions: piff` to `^PiffEffects Warhead@AirEffect`** (`rules/weapons/weapons-effects.yaml:20-22`) **and `Explosions: piffs` to `^PiffsEffects Warhead@AirEffect`** (`:9-11`). Fixes every rifle/MG/HMG hit on a helicopter rendering nothing. *(§1.1)*
2. **Add `Explosions: piff` and flip `ImpactActors: false` → `true` on `7.62mm.Minigun Warhead@AirEffect`** (`rules/weapons/weapons-ballistics.yaml:208-210`). Same bug, hand-rolled copy. *(§1.1)*
3. **Add a `Warhead@EffectAir: CreateEffect` with `ValidTargets: Air, Helicopter`, `ImpactActors: true`, `Explosions: explosion_air_medium`, `ImpactSounds: kaboom25.aud` to `Hellfire`** (`rules/weapons/weapons-missiles.yaml:152`) **and to `Ataka`** (`:105`). Fixes ATGM hits on helicopters rendering nothing. *(§1.2)*
4. **Add `Inherits@HitEffects: ^PiffEffects` to `25mm.Bradley`** (`rules/weapons/weapons-ballistics.yaml:388`), **`30mm.BMP2`** (`:411`) **and `30mm.Heli`** (`:482`), matching `30mm.Tunguska.AG` (`:447`). Brings the IFV and heli chain guns up to the Tunguska reference. *(§1.4)*
5. **Change `^MinimalExplosionEffects Warhead@Effect` from `Explosions: hit_minimal` to `Explosions: hit_small`** (`rules/weapons/weapons-effects.yaml:459`). Replaces a 5×4-px flash with its authored 10×8-px sibling. *(§1.3)*
6. **Add `Image: tracer_small` to the `Projectile: Bullet` blocks of `25mm.Bradley`** (`rules/weapons/weapons-ballistics.yaml:398-400`), **`30mm.BMP2`** (`:421-423`) **and `30mm.Heli`** (`:489-491`). Stops the IFV autocannon firing a non-rotating grenade sprite. *(§6.1)*
7. **Change `bradley` and `strykershorad` muzzle from `gunfire2 / Length: 2` to `minigun / Length: 6 / Facings: 8`** (`mods/ww3mod/sequences/sequences.yaml:217-218` and `:230-231`), matching `bmp2` (`:286-288`). Removes a NATO-vs-BRICS asymmetry in the IFV muzzle flash. *(§2.1)*
8. **Add `WithMuzzleOverlay:` to the `FROG` (Su-25) actor block** (`rules/ingame/aircraft-russia.yaml`, anywhere in 426-534). Its `MuzzleSequence: muzzle` at `:462` is currently inert; the `frog` image already defines `muzzle` at `sequences/sequences-aircraft.yaml:88-90`. One line. *(§1.5)*
9. **Give the A-10 a muzzle flash**: add `muzzle: gunfire2` / `Length: 5` to the `a10:` sequence block (`mods/ww3mod/sequences/sequences-aircraft.yaml:104-109`, which currently defines only `idle` and `icon`), `MuzzleSequence: muzzle` to `Armament@1` (`rules/ingame/aircraft-america.yaml:438-442`), and a `WithMuzzleOverlay:` trait to the actor. *(§1.5, §3.2)*
10. **Change `VehicleCookoffTiny Warhead@Effect` from `Explosions: piff` / `ImpactSounds: gun27.aud` to `Explosions: napalm_small` / `ImpactSounds: firebl3.aud`** (`rules/weapons/weapons-explosions.yaml:49-52`). A burning Humvee currently goes *tink*. *(§4.4)*
11. **Add `TrailImage: smokey` + `TrailScalePercent: 75` to `Hellfire`'s projectile block** (`rules/weapons/weapons-missiles.yaml:180-181`), matching `Ataka` (`:137-138`). *(§6.2)*
12. **Demote `HandGrenade Warhead@Effect` from `explosion_medium` to `explosion_small`** (`rules/weapons/weapons-ballistics.yaml:261-263`). Stops a thrown grenade out-exploding a 40 mm GL round by four rungs. *(§2.2)*
13. **Uncomment `SmokeTrailWhenDamaged` on F-16, Su-25 and MiG-29** (`rules/ingame/aircraft-america.yaml:614-616`, `rules/ingame/aircraft-russia.yaml:517-519`, `:631-633`) and gate on `critical-damage`. Damaged aircraft currently look pristine. *(§2.4)*
14. **Add `muzzle-missile: smokey` to the `tunguska`, `strykershorad`, `m270`, `himars`, `grad` and `tos` sequence blocks** (`mods/ww3mod/sequences/sequences.yaml`) **and `MuzzleSequence: muzzle-missile` to their missile/rocket armaments**; add `WithMuzzleOverlay:` to `grad` and `tos` (the M270's and HIMARS' existing ones are currently dead traits). Gives all six rocket/SAM platforms a launch plume. *(§3.2)*
15. **Repoint the AA templates at the unused purpose-built flak art** — `^MinimalExplosionEffectsAir` (`rules/weapons/weapons-effects.yaml:691`) and `^SmallExplosionEffectsAir` (`:701`) → `flak_small`; `^MediumExplosionEffectsAir` (`:708`) → `flak_medium`; `^Large`/`^HugeExplosionEffectsAir` (`:718`, `:728`) → `flak_large`. **Eyeball one burst before taking all five.** *(§5.2)*
16. **Uncomment the A-10's `Contrail@1`/`Contrail@2`** (`rules/ingame/aircraft-america.yaml:508-513`) for parity with the Su-25 — *or* delete the Su-25's for realism. Pick one; don't leave the split. *(§2.5)*
17. *(Optional, taste)* **Un-flatten the middle of the explosion ladder**: `^SmallerExplosionEffects` (`rules/weapons/weapons-effects.yaml:483`) → `explosion_small`, and `^SmallMediumExplosionEffects` (`:531`) → `explosion_small`, so ten templates stop rendering as eight pictures. *(§4.3)*
18. *(Optional, cosmetic)* **Delete the duplicate `Warhead@Smudge` entries** in `^LargeExplosionEffects` (`rules/weapons/weapons-effects.yaml:590-592`) and `^HugeExplosionEffects` (`:621-623`). Dead lines. *(§7)*

Items 1–6 are the ones that change what the user reported. 7–12 are clear correctness wins.
13–16 are visible improvements with a judgement call attached. 17–18 are housekeeping.
