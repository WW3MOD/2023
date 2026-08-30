# Recon — which explosions should carry a shockwave ring

**Status: proposal only. Nothing here is implemented.** Written 2026-08-30 alongside the branch that
cut the volatile-cargo rings to 60%, made the ring's band shape configurable, and gave the TOS a
decorative ring. Those three are shipped; everything below is a recommendation awaiting a ruling.

## What exists today

Twelve weapons declare a ring through `Warhead@Shockwave: ShockwaveDamage`, plus
`IskanderExplosionAirborne` which inherits one:

| Weapon | Ring travel | Damage reach | Notes |
|---|---|---|---|
| `TacticalNuke` `Warhead@BlastWave` | 30c0 | 29c0 | the ring *is* the damage model — 30 falloff steps replacing 53 hand-written warheads |
| `IskanderExplosion` (+`.Airborne`) | 4c0 | 4c0 — MaxRadius binds, the falloff table reaches 6c0 | ballistic missile |
| `HIMARSExplosion` | 2c512 | 2c512 — MaxRadius binds, the falloff table reaches 4c0 | ballistic missile |
| `VolatileLoad1`–`8` | 307 → 2458 (visual) | 512, 1024, then 1364 flat | wave still travels 512 → 4c0; see below |
| `TosRockets` (new) | 2c0 | none — decorative | 24 per salvo |

The `VolatileLoad` row is the important one to understand before proposing anything: its ring travel
and its damage reach are **deliberately different numbers**, per the ruling of 2026-08-21 that these
blasts are spectacle rather than area denial. That decoupling is now explicit — `MaxRadius` bounds the
wave, `ShockwaveVisualRadius` bounds the picture.

## Candidates, ranked

Sizes assume the same idiom as the shipped rings: `WaveSpeed` 5–7, thickness roughly a quarter of
radius (an eighth if it should read thin), outer alpha 12–25, inner alpha 4–8. Every one of these
would be **decorative** (`Damage: 0`), which since this branch also means `ShockwaveEffect` skips its
per-tick actor sweep entirely rather than delivering zero-damage hits that victims still notice.

### Yes — clear wins

| Weapon | Consumers | Radius | Thickness | Alphas (out/in) | Why |
|---|---|---|---|---|---|
| `BuildingExplode` | 7 | 3c0 | 448 | 16 / 5 | A building collapsing is the single most obvious "that was big" moment in the game and currently has no overpressure cue at all. Infrequent enough to stay special. |
| `SmallBuildingExplode` | 4 | 2c0 | 320 | 12 / 4 | Same event, smaller structure; keeps the size ladder legible. |
| `ATMine` | 1 | 1c512 | 224 | 18 / 6 | 10 000 + 4 000 damage arriving from nowhere. A ring is the only thing that would tell a player *what* just happened, and mines are rare enough that novelty is the point. |

### Yes, with a caveat

| Weapon | Consumers | Radius | Why the caveat |
|---|---|---|---|
| `M270Rockets` | 1 (m270) | 1c768, thin, alpha 12 | Parity with the TOS, one tier quieter — 12 conventional 227 mm rockets against 24 thermobaric. **But** if the TOS ring turns out to read as clutter in play, this is the first thing to drop, not the second. |

### No — and these are the interesting ones

- **`VehicleCookoffLarge`** (6 consumers: m270, grad, TOS). Thematically perfect — rocket pods
  cooking off — and it is a trap. `grad` carries `VehicleCookoffLarge` **ungated** alongside all
  eight `VolatileLoad` bands (`vehicles-russia.yaml:526-556`), and `Explodes` does no arbitration
  between instances, so a loaded grad's death already fires a cookoff *and* a band. Adding a ring
  here puts two concentric rings on the same death at slightly different radii. If this is ever
  wanted, gate it to actors that have no `VolatileLoad` band — which today is only the TOS, and the
  TOS already gets rings from its own rockets.
- **`UnitExplode`** (5 consumers) and **`ArtilleryExplode`** (2) — generic vehicle death. This is
  where I disagree with "maybe every major explosion should have at least a faint shockwave". Tank
  deaths are the most common event in a battle; a ring on each turns the signal into wallpaper and
  takes the meaning out of the rings that *are* special. The rings earn their weight by being rare.
- **`UnitExplodeHeliEmpty`** (12 consumers — every helicopter husk in `husks-aircraft.yaml`) and
  **`UnitExplodePlane` / `UnitExplodeHeli`** (1 each). Same frequency argument, more sharply: this is
  the single most-wired explosion family in the mod, and a helicopter falling out of the sky is
  already unmissable without one.
- **`FlamethrowerExplosion`, `BurnFX`, `NapalmFX`**. Deflagration, not detonation — there is no
  supersonic front to condense. Also among the most spammed weapons in the mod.
- **`BarrelExplode`** (3 consumers). Civilian scenery; a fuel fire, not a charge. Marginal at best.
- **`CrateNuke`, `MiniNuke`, `NapalmExplosion`, `UnitExplodeSmall`, `UnitExplodePlaneEmpty`** all have
  **zero consumers** anywhere in `mods/ww3mod/` — nothing fields them and nothing inherits them. They
  are dead weapons and tuning them would be tuning something no player can ever see. (`CrateNuke` in
  particular looks like an obvious candidate on the page — rare, huge, already has a flash — which is
  exactly why it is called out here rather than quietly omitted.)

## The derive-it-from-the-explosion idea

The proposal: stop hand-tuning, and compute radius / thickness / alpha from the explosion's own
damage, spread and falloff, so the ring always represents the blast honestly.

**It is buildable.** `ShockwaveDamageWarhead` already implements `IRulesetLoaded<WeaponInfo>`, and
that hands it the whole `WeaponInfo` — so it can read its sibling warheads at load time, find the
`SpreadDamage`/`TargetDamage` on the same weapon, and derive its own numbers once, with no per-tick
cost and no new plumbing. The question is only whether the answers would be good.

I worked out what two candidate formulas actually produce for the mod's real weapons.

**Formula A — cube root of damage** (Hopkinson-Cranz: blast radius scales with the cube root of
yield). Calibrated so `IskanderExplosion` keeps its 4c0 ring:

| Weapon | Damage | Formula says | Hand-tuned today | Error |
|---|---|---|---|---|
| `IskanderExplosion` | 4000 | 4c0 | 4c0 | calibration point |
| `HIMARSExplosion` | 2500 | 3c430 | 2c512 | +37% |
| `VolatileLoad8` | 800 | 2c347 | 2c410 visual | −3% |
| `VolatileLoad1` | 100 | 1c174 | 307 | **+290%** |
| `TacticalNuke` | 100000 | 11c744 | 30c0 | **−61%** |

It fails at both ends, and it fails for a reason that will not go away: **`Damage` in this mod is a
balance number scaled against HP pools, not an energy.** The nuke is 25× the Iskander's damage number
and is meant to look a hundred times bigger; the empty supply truck's 100 is a *small* real explosion
that only looks large next to 200-HP infantry.

**Formula B — the damage footprint**, `(Falloff.Length - 1) × Spread`, which is the actual ground
area the warhead affects:

| Weapon | Footprint | Hand-tuned ring | Verdict |
|---|---|---|---|
| `IskanderExplosion` | 4096 | 4c0 | exact |
| `TacticalNuke` | 29696 | 30c0 | near-exact |
| `HIMARSExplosion` | 3072 | 2c512 | 0.83× — fine |
| `VolatileLoad1` | 1024 | 307 | 0.30× |
| `VolatileLoad8` | 1024 | 2458 | 2.40× |

Formula B is genuinely good for the missiles and the nuke — close enough that it would have been a
reasonable *default*. Then it walks straight into the wall: **all eight `VolatileLoad` bands have the
identical 1024 footprint**, because they differ only in `Damage`. A footprint-derived ring collapses
the whole family to one size and destroys the load-proportional spectacle that is the entire point of
that weapon family.

Three more places any derived formula gives a bad answer:

- **`BuildingExplode` carries no damage warhead at all** — only `CreateEffect` and `LeaveSmudge`. Every
  damage-derived formula gives it a ring of size zero, and it is the strongest candidate on the list.
- **`FlamethrowerExplosion`** has six `GrantExternalCondition` warheads reaching 1536 against 50 damage
  at Spread 256. Footprint-derived, it gets a wide ring; physically it should get none.
- **`ATMine`** is 10 000 damage inside half a cell. Damage-derived it outranks a ballistic missile;
  it is a buried charge under one tank.

### Verdict

**Hand-tuning wins, and I would not build the derivation as runtime behaviour.** The recurring reason
is the same in every failure above: a ring is a *dramatic* statement about an event, and this mod's
damage numbers are a *balance* statement. Where the two coincide the formula looks excellent, which is
exactly what makes it dangerous — it would ship looking correct and then quietly flatten the one
weapon family that was deliberately built to violate it.

What I would take from the idea instead: a **one-off authoring aid**, not engine behaviour. Formula B
run offline over `weapons-explosions.yaml` produces a decent first-draft radius for any weapon you are
adding a ring to, and it costs nothing to ignore. Roughly ten lines of script against the existing
MiniYaml reader; it needs no engine change and cannot regress anything.

The one piece of the idea worth building for real is narrower and I would support it: derive the ring's
**thickness and alpha from its own radius** rather than from the damage — the shipped rings already
sit at a fairly consistent quarter-of-radius thickness, and defaulting `ShockwaveThickness` to
`MaxRadius / 4` when unset would remove one hand-tuned number per call site with no expressiveness
lost. That is a small, safe change; the damage-derived version is neither.
