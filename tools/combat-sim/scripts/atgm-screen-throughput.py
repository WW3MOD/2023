#!/usr/bin/env python3
"""How many humvees does it take to overrun a screen of M AT specialists?

This is the user's actual question. A per-missile hit rate does not answer it:
what matters is whether the DEFENCE'S TOTAL SHOT BUDGET over the time the
attacker spends crossing the engagement envelope is large enough to kill the
attackers arriving in that time. Hit rate is one of three terms, and it is not
the dominant one.

THE THREE TERMS, all read from shipped rules
---------------------------------------------
  ATGM Range     20c0 = 20480          weapons-missiles.yaml:7
  ATGM MinRange   3c0 =  3072          weapons-missiles.yaml:8   (cannot fire inside)
  ATGM BurstWait  200 ticks = 8.0 s    weapons-missiles.yaml:9   (Burst defaults to 1,
                                       so this IS the shot-to-shot cycle)
  AT AmmoPool       3 rounds           infantry.yaml:1650
  Humvee Speed    150 wdist/tick       vehicles-america.yaml:76  (105 measured on
                                       clear terrain -- see the audit run notes)

The engagement envelope is Range - MinRange = 17408 wdist. A humvee crosses it in

    17408 / 150 = 116 ticks   (nominal)
    17408 / 105 = 166 ticks   (measured)

against a shot cycle of 200 ticks. **Both are shorter than one reload.** So each
AT specialist fires exactly ONE missile at a humvee crossing from max range to
contact, no matter which speed figure you believe, and no matter how accurate
that missile is. The magazine of 3 is irrelevant to a single approach; it only
matters across successive waves.

That is the mass-versus-defence result in one line: **the AT screen's stopping
power per approach is M x p, where p is the per-missile kill probability, and it
is capped at M kills however good p becomes.** Raising accuracy to a perfect
100% buys the defender at most 1/p times as many kills -- it cannot buy a second
shot, because the limit is the reload, not the aim.

WHAT THIS SCRIPT ADDS OVER THAT ARITHMETIC
-------------------------------------------
The closed form above assumes perfect fire distribution. Real screens waste
shots: several ATs can commit to the same humvee before the first missile lands
(ATGM flight time at 20 cells is ~68 ticks at Speed 300, which is a long time to
be committed), and a non-lethal hit leaves a damaged but living humvee that the
next missile must re-kill. Both effects cut the defence's efficiency, so this
runs the engagement tick by tick and reports the realised number.

Damage per missile is not a parameter here. It is drawn from
`atgm-terminal-hit-rate.py`, which resolves the actual terminal geometry and the
actual TargetDamage/CenterProximityPercent arithmetic, so the two scripts cannot
drift apart.

Every number this prints is SIMULATED, resting on measured inputs.
No game launch.
"""
import argparse
import importlib.util
import math
import os
import random

HERE = os.path.dirname(os.path.abspath(__file__))
_spec = importlib.util.spec_from_file_location(
    "atgm_terminal", os.path.join(HERE, "atgm-terminal-hit-rate.py"))
term = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(term)

# ------------------------------------------------------------- shipped numbers
RANGE = 20480
MIN_RANGE = 3072
BURST_WAIT = 200
AT_AMMO = 3
AT_COST = 300               # infantry.yaml:1640
HUMVEE_COST = 500           # vehicles-america.yaml:45
HUMVEE_HP = term.HUMVEE_HP
MISSILE_SPEED = term.MAX_SPEED
TICKS_PER_SEC = 25


def draw_damage(rng, hv_speed, alt):
    """One missile's damage against a humvee moving at hv_speed, via the real
    terminal geometry. Bearing and heading are randomised over the same grid the
    terminal script sweeps, because neither averages out."""
    bearing = rng.randrange(8) * 32
    heading = rng.randrange(8) * 32
    r = term.simulate_shot(rng, hv_speed, heading, 0, bearing, None, alt)
    d, hit = term.resolve_damage(r["ix"], r["iy"], r["cx"], r["cy"],
                                 r["hv_facing"])
    return d if hit else 0.0


def engage(rng, n_humvees, m_at, hv_speed, alt, sample):
    """One approach. Humvees start at RANGE and drive to MIN_RANGE; each AT
    fires at the nearest live humvee it has not already got a missile in flight
    against. Returns how many humvees reach MIN_RANGE alive."""
    dist = [float(RANGE)] * n_humvees
    hp = [float(HUMVEE_HP)] * n_humvees
    alive = [True] * n_humvees
    cooldown = [0] * m_at
    ammo = [AT_AMMO] * m_at
    inflight = []          # (arrival_tick, target_index)
    committed = set()      # targets with a missile already inbound

    tick = 0
    while True:
        tick += 1
        for i in range(n_humvees):
            if alive[i]:
                dist[i] -= hv_speed

        for k in range(len(inflight) - 1, -1, -1):
            at_tick, tgt = inflight[k]
            if tick < at_tick:
                continue
            inflight.pop(k)
            committed.discard(tgt)
            if not alive[tgt]:
                continue
            hp[tgt] -= sample(rng, hv_speed, alt)
            if hp[tgt] <= 0:
                alive[tgt] = False

        for a in range(m_at):
            if cooldown[a] > 0:
                cooldown[a] -= 1
                continue
            if ammo[a] <= 0:
                continue
            cand = [i for i in range(n_humvees)
                    if alive[i] and MIN_RANGE <= dist[i] <= RANGE
                    and i not in committed]
            if not cand:
                continue
            tgt = min(cand, key=lambda i: dist[i])
            flight = max(1, int(dist[tgt] / MISSILE_SPEED))
            inflight.append((tick + flight, tgt))
            committed.add(tgt)
            ammo[a] -= 1
            cooldown[a] = BURST_WAIT

        if all((not alive[i]) or dist[i] < MIN_RANGE for i in range(n_humvees)):
            # let anything still inbound land
            for at_tick, tgt in inflight:
                if alive[tgt]:
                    hp[tgt] -= sample(rng, hv_speed, alt)
                    if hp[tgt] <= 0:
                        alive[tgt] = False
            break
        if tick > 4000:
            break

    return sum(1 for i in range(n_humvees) if alive[i])


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--reps", type=int, default=300)
    ap.add_argument("--speed", type=int, default=150,
                    help="150 nominal, 105 measured on clear terrain")
    ap.add_argument("--alt", type=int, default=400)
    ap.add_argument("--seed", type=int, default=20260821)
    args = ap.parse_args()

    rng = random.Random(args.seed)

    print(__doc__.split("\n")[0])
    print(f"\nSIMULATED, {args.reps} approaches per cell, humvee speed "
          f"{args.speed} wdist/tick, seed {args.seed}.")

    cross = (RANGE - MIN_RANGE) / args.speed
    print(f"\nEnvelope {RANGE - MIN_RANGE} wdist crossed in {cross:.0f} ticks "
          f"({cross/TICKS_PER_SEC:.1f} s) against a {BURST_WAIT}-tick "
          f"({BURST_WAIT/TICKS_PER_SEC:.1f} s) shot cycle")
    print(f"  => shots per AT per approach: "
          f"{max(1, 1 + int(cross // BURST_WAIT))}")

    # Calibrate p on this speed so the closed form can be shown alongside.
    cal = [draw_damage(rng, args.speed, args.alt) for _ in range(4000)]
    p_kill = sum(1 for d in cal if d >= HUMVEE_HP) / len(cal)
    p_land = sum(1 for d in cal if d > 0) / len(cal)
    print(f"  per-missile kill probability at this speed: {100*p_kill:.1f}% "
          f"(landed anywhere: {100*p_land:.1f}%)")

    print("\nHumvees reaching contact (mean), N attackers vs M AT specialists:")
    ms = [2, 4, 6, 8, 12]
    ns = [2, 4, 6, 8, 12, 16]
    print("      " + "".join(f"{'M=' + str(m):>9}" for m in ms))
    for n in ns:
        row = []
        for m in ms:
            got = sum(engage(rng, n, m, args.speed, args.alt, draw_damage)
                      for _ in range(args.reps)) / args.reps
            row.append(f"{got:>9.1f}")
        print(f"N={n:<3} " + "".join(row))

    print("\nBreakpoint -- smallest N where the screen leaks, per M:")
    print(f"  {'M AT':>5} {'closed form M/p':>16} {'measured N*':>12} "
          f"{'attacker cost':>14} {'defender cost':>14} {'ratio':>7}")
    for m in ms:
        nstar = None
        for n in range(1, 40):
            got = sum(engage(rng, n, m, args.speed, args.alt, draw_damage)
                      for _ in range(max(60, args.reps // 3)))
            if got / max(60, args.reps // 3) >= 1.0:
                nstar = n
                break
        cf = m / p_kill if p_kill else float("inf")
        if nstar:
            ac, dc = nstar * HUMVEE_COST, m * AT_COST
            print(f"  {m:>5} {cf:>16.1f} {nstar:>12} {ac:>14} {dc:>14} "
                  f"{ac/dc:>7.2f}x")
        else:
            print(f"  {m:>5} {cf:>16.1f} {'>40':>12}")

    print("\nHow the breakpoint moves with hit rate -- the user's question.")
    print("Kill probability is FORCED here rather than simulated, to isolate it:")
    print(f"  {'forced p':>9} {'N* for M=6':>12} {'vs shipped':>11}")
    base = None
    for p in (0.25, 0.40, 0.55, 0.70, 0.85, 1.00):
        def forced(r, _s, _a, _p=p):
            return HUMVEE_HP if r.random() < _p else 0.0
        nstar = None
        for n in range(1, 40):
            got = sum(engage(rng, n, 6, args.speed, args.alt, forced)
                      for _ in range(120)) / 120
            if got >= 1.0:
                nstar = n
                break
        if base is None:
            base = nstar
        print(f"  {p:>9.2f} {nstar if nstar else '>40':>12} "
              f"{(nstar - base) if nstar and base else 0:>+11}")


if __name__ == "__main__":
    main()
