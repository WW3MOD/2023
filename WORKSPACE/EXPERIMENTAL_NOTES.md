# 260510 — WGM/Hellfire experimental tuning

> **Status:** Uncommitted. Sitting in your working tree on top of the
> committed accuracy + tree-gating work. `git diff` shows what's loose.

The committed work is solid (3 commits today, all green). These are my
opinion-driven follow-ups — try them in a playtest, then tell me which
ones to keep.

## Files modified (all uncommitted)

| File | What changed |
|---|---|
| `mods/ww3mod/rules/weapons/weapons-missiles.yaml` | WGM/Hellfire splash radius bump; WGM.bradley triple-tap |
| `tools/autotest/scenarios/test-wgm-accuracy/test-wgm-accuracy.lua` | Bumped expected shot count + deadline to fit Burst 3 |
| `tools/autotest/scenarios/test-wgm-accuracy-moving/test-wgm-accuracy-moving.lua` | Same |

The test edits are *follow-on* changes for the Burst 3 experiment. If
you accept Burst 3, keep them. If you reject Burst 3, revert them.

## What I changed and why

### 1. WGM/Hellfire splash radius `Spread: 64 → 192` (3×)

WGM's `Warhead@Spread` had a 64 wdist (1/16 cell) splash radius —
basically a point detonation. A near miss = 0 splash damage. With 192
wdist (~3/16 cell), a missile that lands close to the target still
delivers 2000 splash damage on top of whatever target damage it got.

**Test impact (moving target):** 51 % → 63 % effective hit rate (the
moving-accuracy test runs a t90 perpendicular to 4 firing Bradleys).

**Risk:** modest splash buff to area weapons. Could matter if WGM ever
fires near friendly infantry or grouped vehicles. Damage is still 2000
so it's not a blast weapon, just a wider proximity.

### 2. WGM.bradley triple-tap (`Burst: 2 → 3`, `BurstDelays: 100 → 80`) — **REJECTED 260510**

Tried it; user pointed out the Bradley M2A2/M2A3 actually has **two**
TOW tubes, not three, so a 3-missile burst doesn't match the model.
Reverted to `Burst: 2`. Leaving the rationale here so we don't propose
the same tweak again — if you want more Bradley AT throughput, the
right lever is reload time (`BurstWait`) or ammo capacity, not burst
size.

### 3. (Tried + reverted) `CloseEnough: 200`

I attempted to tighten the missile detonation distance from 298 → 200
to put the warhead closer to target center (less PercentFromEdge
falloff). It actually *reduced* damage because the missile would
sometimes overshoot before reaching close-enough range. Reverted.

## Test results with experimental package on

```
test-wgm-fires-clean              PASS
test-wgm-fires-thru-1-tree        PASS
test-wgm-deny-thru-5-trees        PASS
test-wgm-tree-density-ladder      PASS  — 0/1/2/3 fire, 4/5/6 deny
test-wgm-accuracy                 PASS  dmg=100338/120000 (83%) 12/12 shots
test-wgm-accuracy-moving          PASS  dmg=76669/120000  (63%) 12/12 shots
```

## Things I considered and skipped

| Idea | Why skipped |
|---|---|
| `TopAttack: true` on WGM | Penetration 800 already overpens all t90 facings — no damage gain |
| Speed 300 → 500 | Caused overshoot in tests; missile flew past target before HoR could correct (0 % accuracy) |
| `RetargetTicks: 1` on stationary | Indistinguishable from RT 2; chose 2 for cheaper per-tick cost |
| 25mm Bradley range buff | Would blur the "WGM is the long-arm" identity |
| BMP-2 Burst 3 | Bradley-only buff is more interesting asymmetrically (US gets the alpha-strike vehicle, Russia keeps cheaper IFV with single-burst) |

## How to revert

```bash
git checkout mods/ww3mod/rules/weapons/weapons-missiles.yaml
git checkout tools/autotest/scenarios/test-wgm-accuracy/test-wgm-accuracy.lua
git checkout tools/autotest/scenarios/test-wgm-accuracy-moving/test-wgm-accuracy-moving.lua
```

## How to commit (if you like them)

```bash
git add mods/ww3mod/rules/weapons/weapons-missiles.yaml \
        tools/autotest/scenarios/test-wgm-accuracy \
        tools/autotest/scenarios/test-wgm-accuracy-moving
git commit -m "WGM/Hellfire experimental: 3× splash + Bradley triple-tap"
```
