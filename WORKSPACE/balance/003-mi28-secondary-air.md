# 003 — Mi-28 dangling `secondary-air` armament (advertised AA is non-functional)

Status: PROPOSED
Source: `260802-parity-audit.md` §3 (apache ↔ mi28)

## Evidence

`mods/ww3mod/rules/ingame/aircraft-russia.yaml` wires the Mi-28 to an
armament named `secondary-air` in three places:

- `aircraft-russia.yaml:312` — `AttackAircraft` → `Armaments: primary, secondary-air`
- `aircraft-russia.yaml:322` — `GrantConditionOnPreparingAttack` → `ArmamentNames: primary, secondary-air`
- `aircraft-russia.yaml:367` — `AmmoPool@2` → `Armaments: secondary-air`

But **no armament with `Name: secondary-air` is defined anywhere** in
`mods/ww3mod/rules/ingame/` (verified by search; the HIND has one *commented
out* at `aircraft-russia.yaml:153`). The references dangle.

Meanwhile:

- The Mi-28 buildable description claims **"Can engage aircraft"**
  (`aircraft-russia.yaml:291`).
- Its actual missile, Ataka, has `ValidTargets: Vehicle, Defense` — **no
  Air** (`mods/ww3mod/rules/weapons/weapons-missiles.yaml:111`).
- The US counterpart's Hellfire *does* list Air
  (`weapons-missiles.yaml:158`), so the Apache has functional AA and the
  Mi-28 does not, despite both advertising it.

Net: RU's premier attack helicopter cannot shoot back at the US heli fleet —
compounded by AI-config asymmetry A1 (RU bot fields 7 attack helis vs US 4),
meaning the larger RU fleet is defenseless in heli-vs-heli engagements the
bot actively seeks.

## Proposed change

Define the missing armament on the Mi-28, modeled on the HIND's commented-out
block at `aircraft-russia.yaml:153` and the Apache's air-capable secondary:
add to the `mi28` actor in `aircraft-russia.yaml` an
`Armament@SECONDARY-AIR` with `Name: secondary-air`, an appropriate AA
weapon (either a new `AtakaAA` variant in `weapons-missiles.yaml` with
`ValidTargets: Air`, or reuse of an existing RU AA missile), keeping the
existing `AmmoPool@2` (:367) as its magazine.

Exact weapon choice (damage/range/ROF) is deliberately left to sign-off
discussion — the *bug fix* is making the three dangling references resolve;
the *tuning* should mirror Hellfire's AA envelope unless the user wants a
flavor gap.

Alternative (flavor decision): if the Mi-28 is *intended* to have no AA,
instead remove `secondary-air` from :312/:322, remove `AmmoPool@2` (:367),
and fix the description at :291 to stop claiming air engagement.

## Expected effect

- Mi-28 gains (or honestly loses) air-to-air capability; description matches
  behavior either way.
- Metric: `test-heli-vs-heli-missile` outcome flips for Mi-28 pairings;
  cross parity tournaments should show RU winrate gain in heli-contested
  seeds (fix variant) since A1 already skews the RU bot toward helicopters.

## Risk

- Fix variant: adds real AA to a 7-strong bot fleet (A1) — could overshoot
  and flip heli dominance to RU. Run the parity cross pair before/after; if
  RU jumps past ~55%, retune the AA weapon, not the fleet size, first.
- Dangling-reference behavior: engine may warn-and-ignore or misbehave when
  resolving `secondary-air`; either way current behavior is untested-by-design
  and may change subtly when the name resolves. `test-mi28-fires-ataka`
  guards the primary armament against regression.
- Alternative variant: pure honesty fix, near-zero gameplay risk, but locks
  in Apache air superiority as intended design — document it in the audit if
  chosen.
