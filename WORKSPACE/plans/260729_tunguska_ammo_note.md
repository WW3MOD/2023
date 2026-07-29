# Tunguska AA ammo-pool fix — quantified playtest note

**Date:** 2026-07-29
**Branch under review:** `auto/may-salvage` (tip `ec757ad4`; YAML fix in `07aed0ae`)
**Base:** main @ `77dbfb7d`
**Scope:** doc-only analysis. No build, no launch, no autotest, no YAML/C# edits.

## What the branch changes

`07aed0ae` repoints one dangling reference:

```
tunguska AmmoPool@1.Armaments:  primary, tertiary  ->  primary, primary-air
```

`tunguska` has three armaments: `primary` (30mm AG), `primary-air` (30mm AA),
`secondary` (9M311 SAM). There is **no** `tertiary` armament, so the old list left
the AA armament (`primary-air`) owned by **no ammo pool**.

Engine mechanic (`engine/OpenRA.Mods.Common/Traits/AmmoPool.cs:241`
`INotifyAttack.Attacking`): a pool only decrements when the firing armament's name is
in `Info.Armaments`. Because `primary-air` was absent, **firing the AA consumed zero
rounds**. The `!ammo-primary` pause condition on the AA armament still applied, but the
pool only emptied when the *AG* fired — so pure air engagements never drained it →
**effectively infinite AA ammo**.

After the fix `primary-air` joins the shared `primary-ammo` pool (Ammo 180), so AA fire
now draws down the same 180-round magazine the AG uses.

## Firing model (resolved YAML)

Weapon `30mm.Tunguska.AA` ← `^30mm.Tunguska` ← `^30mm`:

| field | value | source |
|---|---|---|
| Burst | 12 shots/burst | `^30mm.Tunguska` |
| BurstDelays | 1 tick (intra-burst) | `^30mm.Tunguska` |
| BurstWait | 12 ticks (inter-burst) | `^30mm.Tunguska` |
| ReloadDelay | 0 (weapon-magazine mechanic unused) | default |
| AmmoUsage | 1 round/shot | Armament default (`primary-air` sets none) |
| Pool `primary-ammo` Ammo | 180 | `vehicles-russia.yaml:838` |
| Pool ReloadCount / ReloadDelay | 30 / 50t (dock-rearm rate only) | pool defaults + YAML |

Burst cycle (`Armament.UpdateBurst`, `engine/.../Armament.cs:624`): 12 shots, spaced
11×1t within the burst, then 12t `BurstWait` after the 12th shot →
**23 ticks per 12 rounds** = 0.5217 rounds/tick.

**Game speed:** default (`normal`) is `Timestep: 60ms` → 16.667 ticks/s
(`mods/ww3mod/mod.yaml:365`). Fastest is 40ms. Numbers below give the default, with the
fastest-speed figure in parentheses.

## Sustain math (after fix)

- **Fire rate:** 0.5217 rounds/tick × 16.667 t/s = **8.7 rounds/s** (13.0 at fastest).
- **Continuous-fire duration to empty 180:** 15 bursts. Tick-exact = 14 full cycles
  (322t) + last burst's 12 shots (11t) = **333 ticks ≈ 20.0 s** (13.3 s at fastest).
- **Effective DPS before empty (nominal, primary TargetDamage 1000 close-range, +100
  spread; falls to ~50/50 at max range 18c0, Penetration 70):**
  8.7 rounds/s × ~1000 ≈ **~8,700 dmg/s close** (down to ~650 dmg/s at max range).
- **Effective DPS after empty: 0** — AA cannot fire (`!ammo-primary` pauses both AG and
  AA) until the unit is rearmed.
- Pips: `WithAmmoPipsDecoration` PipCount 6 → each of the 6 yellow pips = 30 rounds.

### Refill — correction to the "trickle-refills at X/sec" premise

**Tunguska has no in-field trickle.** There is no `ReloadAmmoPool` trait on it — the only
`ReloadAmmoPool` in `vehicles-russia.yaml` is inside a **commented-out** tesla-style block
(`:1127`). Engine-side, `AmmoPool` implements `INotifyAttack`/`INotifyBecomingIdle` but
**not `ITick`**, and its public `Reload()` method (`AmmoPool.cs:361`) has **zero callers**
— it is dead code. So the pool never self-regenerates on the battlefield.

The only refill path is docking at a `logisticscenter` (`Rearmable`,
`vehicles-russia.yaml:884`). Dock rate is the pool's `ReloadDelay 50t / ReloadCount 30`
= 30 rounds per 50 ticks = **10 rounds/s while docked** (3.0 s/batch at default speed);
full 180 in 6×50t = 300t ≈ **18 s docked**, plus travel to and from the logistics center.
So once dry, the tunguska must **retreat and rearm**, not top up in place.

### Fraction of a heli engagement covered

~20 s of continuous trigger-time at default speed is enough for a typical single heli
attack run (approach → fire → break) or roughly 2–3 passes before the pool empties. Note
the AG shares the same 180 pool, so mixed ground+air fighting drains it faster. The
before→after delta only bites when the tunguska faces **sustained / multi-heli pressure**:
previously it could hold an air lane against endless waves indefinitely; now heavy heli
pressure forces a rearm cycle.

## Combat-sim check

`tools/combat-sim` (Phase-1, hardcoded stats) **does not model ammo pools**. Its header
states it computes "static derived numbers (DPS, HP-per-credit) … never simulate combat"
(`src/index.ts:15`); there is no time-stepped `duel` loop and no `AmmoPool` tracking (the
`magazine` field it prints is the unused weapon-level RA magazine, not the pool trait).
It therefore cannot empirically confirm pool sustain. Per instructions, no ammo support
was added to the sim — the numbers above are hand-computed from YAML + engine source.

## m113 comparison

`07aed0ae` also trims `m113` `Rearmable.AmmoPools` from
`primary-ammo, secondary-ammo, tertiary-ammo` → `primary-ammo`. m113 defines only
`AmmoPool@1` (`primary-ammo`, Ammo 500, owns armament `primary`); the other two pool names
never existed. `Rearmable.AmmoPools` is the dock-refill list, and naming non-existent pools
is a harmless no-op — and unlike the tunguska case, m113's single armament was already
correctly owned. **Pure dead-reference cleanup, zero gameplay/sustain effect, no balance
implication.**

## Verdict

**Ship as-is — no balance compensation needed.** The fix restores the intended shared
magazine and closes an unintended "free AA ammo" exploit rather than imposing a new
designed-around constraint; ~20 s of continuous AA (default speed) comfortably covers a
normal heli engagement. The one thing to watch in playtest is the **dry-out-then-retreat**
behavior (no passive regen): if sustained heli pressure makes it feel punishing, the lever
is pool size (180) or adding a `ReloadAmmoPool` trickle — **not** reverting the ownership fix.
