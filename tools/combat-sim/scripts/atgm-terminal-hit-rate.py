#!/usr/bin/env python3
"""ATGM (Javelin) terminal geometry against a MOVING humvee.

Supersedes the stationary model in `humvee-hitshape-ladder.py`, which assumed the
missile detonates exactly at its aim point and that every landed missile kills.
Both assumptions are wrong, and they are wrong in opposite directions, so the net
effect could not be guessed -- hence this.

WHAT THIS MODELS, AND WHY IT IS ARRANGED THIS WAY
-------------------------------------------------
Three distinct points are in play on every ATGM flight, and the mod's own audit
(WORKSPACE/audit/javelin-terminal-geometry-run.md) established that confusing them
is what made the previous investigation stall:

  P1 = targetPosition                        the humvee (+32 Z, AirburstAltitude)
  P2 = targetPosition + leadTarget           what the SEGMENT fuse is centred on
  P3 = targetPosition + leadTarget + offset  what the missile STEERS at, and what
                                             the endpoint fuse is centred on

The missile flies at P3. It detonates on whichever of two clauses fires first:

  clause 4  Missile.cs:1163   relTarDist < CloseEnough        (sphere about P3)
  clause 9  Missile.cs:1188   swept-segment closest approach  (sphere about P2)

and damage is then decided by where `pos` happens to be, NOT by which sphere
fired. That is the whole mechanism this script exists to quantify.

TWO TICK-ORDER FACTS THAT DOMINATE THE RESULT
----------------------------------------------
1. `relTarDist` is computed BEFORE the move (Missile.cs:1103-1105) and tested
   AFTER it (Missile.cs:1163), with `pos` already advanced (Missile.cs:1134).
   So a clause-4 detonation lands up to one full tick of travel -- 300 wdist for
   ATGM, which is more than the humvee's 470 width -- PAST the point that
   satisfied the fuse. This is the real form of the `Speed > CloseEnough` PITFALL
   noted at Missile.cs:1179: not a failure to detonate, but a detonation
   displaced downrange.

2. `AllowSnapping` is false (Missile.cs:198, not overridden by ATGM), so the
   missile does NOT teleport onto the aim point at close range. If it were true
   the impact would be exactly P3-without-lead and `humvee-hitshape-ladder.py`
   would have been right.

DAMAGE MODEL -- exact, and 2-D
-------------------------------
  WorldUtils.cs:79-85   FindActorsInCircle ignores height entirely
  Rectangle.cs:109-116  DistanceFromEdge zeroes Z; altitude cannot make a miss
  Rectangle.cs:123-127  CenterProximityPercent = 100*(halfDiag-|v|)/halfDiag

so the whole hit test is the horizontal impact point against a rotated rectangle.

  TargetDamageWarhead.cs:44-77  applies iff DistanceFromEdge <= Spread (=1)
  DamageWarhead.cs:216-231      thickness 10 * ArmorDirectionPercent
  DamageWarhead.cs:127-131      TopAttack -> Distribution[3] = 80  =>  8
                                Penetration 100 > 8, so no reduction
  => damage = 10000 * CenterProximityPercent / 100

Humvee HP is 4000 (vehicles-america.yaml:57), so a landed missile kills only when
CenterProximityPercent >= 40, i.e. within 331 wdist of centre. An impact in the
far corners of the hitshape does real but NON-lethal damage. `Warhead@Spread`
contributes at most 2000*1/8 = 250 before falloff and is ignored here: it needs
16 near-misses to kill and cannot change any verdict below.

WHAT THIS DOES NOT MODEL -- read before trusting a number
----------------------------------------------------------
* The vertical channel is an ANALOGUE of the horizontal one, not a port of
  HomingInnerTick: vFacing is turned toward the elevation of the aim point at
  the same rate limit the engine uses (VerticalRateOfTurn .Facing = 6, boosted
  to 18 the same way hRot is). It is built this way so the missile ARRIVES,
  which is what the corpus measured; an imposed fixed descent rate instead
  produces spurious ground detonations and swamps the result. The terminal
  vFacing it settles at is checked against the audit's MEASURED -2..-15 band,
  and the starting altitude is swept.
* Terminal deceleration (Missile.cs:720, fires inside relTarHorDist ~343) is not
  modelled; speed is held at maxSpeed. This makes the clause-4 overshoot
  marginally PESSIMISTIC -- the real last-tick move can be ~270 rather than 300.
* Midcourse is skipped. The missile is seeded at TERMINAL_ENTRY on a converged
  pursuit course. TERMINAL_ENTRY is 3000 > the 1536 offset-freeze boundary
  (Missile.cs:1092), so the final offset re-rolls ARE inside the simulated window.
* The flyStraight latch is implemented but, as in the measured corpus, it
  essentially never fires at these ranges. It is here so the sim cannot silently
  diverge from the engine on a case the corpus does cover.

Every number this prints is SIMULATED. The one measured anchor it is checked
against is the corpus fact that shipped ATGMs essentially always detonate
(556 flights, zero survivals) -- see the `detonated %` column.

No game launch. Arithmetic over the shipped rules.
"""
import argparse
import math
import random

# ---------------------------------------------------------------- shipped facts

# weapons-missiles.yaml:2-32
INACCURACY = 512            # Absolute (Missile.cs:87 default), so range-independent
MAX_SPEED = 300             # Projectile Speed
CLOSE_ENOUGH = 298          # Missile.cs:203 default; ATGM does not override
RETARGET_TICKS = 5          # Missile.cs:96 default
OFFSET_FREEZE = 1536        # Missile.cs:1092, hardcoded
AIRBURST_ALT = 32
H_ROT_BASE = 5              # HorizontalRateOfTurn 20 raw -> .Facing 5
V_ROT_BASE = 6              # default 24 raw -> .Facing 6
TARGET_DAMAGE = 10000

# vehicles-america.yaml:56-78
HUMVEE_HP = 4000
HALF_W = 235                # cross-axis
HALF_L = 500                # long axis
HALF_DIAG = int(math.hypot(HALF_W, HALF_L))   # 552, matches WVec.HorizontalLength
HUMVEE_NOMINAL_SPEED = 150
HUMVEE_MEASURED_SPEED = 105  # javelin-terminal-geometry-run.md section 4, measured
HUMVEE_TURN = 4             # TurnSpeed 19 raw -> .Facing 4 (Mobile.cs:40, WAngle)

TERMINAL_ENTRY = 3000
TICK_CAP = 60


def loop_radius(speed, rot):
    """Missile.cs:368-374."""
    return speed * 6400 // (157 * rot)


def tick_facing(facing, desired, rot):
    """Util.cs:30-45, verbatim."""
    left = (facing - desired) & 0xFF
    if left < rot:
        return desired & 0xFF
    right = (desired - facing) & 0xFF
    if right < rot:
        return desired & 0xFF
    if right < left:
        return (facing + rot) & 0xFF
    return (facing - rot) & 0xFF


def facing_of(dx, dy):
    return int(round(math.atan2(dy, dx) * 128 / math.pi)) & 0xFF


def dir_of(facing):
    a = facing * math.pi / 128
    return math.cos(a), math.sin(a)


def pdf2(rng):
    """WDist.FromPDF(r, 2) -- WDist.cs:56-60. C# int division truncates to zero."""
    s = rng.randrange(-1024, 1024) + rng.randrange(-1024, 1024)
    return int(s / 2)


def sample_offset(rng):
    """Missile.cs:325 / 1098. Z is always zero (WVec.cs:105-108)."""
    return (int(pdf2(rng) * INACCURACY / 1024),
            int(pdf2(rng) * INACCURACY / 1024))


# ------------------------------------------------------------------ damage model

def resolve_damage(ix, iy, cx, cy, hv_facing):
    """Returns (damage, landed). Pure 2-D, per the file header."""
    dx, dy = ix - cx, iy - cy
    a = -hv_facing * math.pi / 128
    lx = dx * math.cos(a) - dy * math.sin(a)
    ly = dx * math.sin(a) + dy * math.cos(a)

    # Rectangle.cs:109-116
    ex = max(abs(lx) - HALF_W, 0.0)
    ey = max(abs(ly) - HALF_L, 0.0)
    if math.hypot(ex, ey) > 1.0:
        return 0, False

    # Rectangle.cs:123-127 -- magnitude only, so rotation drops out here
    prox = 100 * (HALF_DIAG - math.hypot(dx, dy)) / HALF_DIAG
    prox = max(0.0, prox)
    return TARGET_DAMAGE * prox / 100.0, True


# ------------------------------------------------------------------- the flight

def simulate_shot(rng, hv_speed, hv_heading, hv_turn_rate, approach_bearing,
                  reverse_at, alt0, segment=True, overshoot=True,
                  post_move=False):
    """One ATGM terminal engagement. Returns dict describing the outcome."""
    # Humvee starts at the origin; missile is seeded on a converged pursuit
    # course at TERMINAL_ENTRY along `approach_bearing`.
    cx = cy = 0.0
    heading = hv_heading
    ax, ay = dir_of(approach_bearing)
    mx, my = -ax * TERMINAL_ENTRY, -ay * TERMINAL_ENTRY
    alt = float(alt0)
    hfacing = approach_bearing
    vfacing = 0
    speed = MAX_SPEED

    offset = sample_offset(rng)
    last_cx, last_cy = cx, cy
    state_hitting = False
    fly_straight = False
    min_dist = float("inf")
    reversed_yet = False

    for tick in range(1, TICK_CAP + 1):
        # --- target moves first (its position is what Missile.cs samples) -----
        last_cx, last_cy = cx, cy
        if hv_turn_rate:
            heading = (heading + hv_turn_rate) & 0xFF
        if reverse_at is not None and not reversed_yet:
            if math.hypot(mx - cx, my - cy) <= reverse_at:
                heading = (heading + 128) & 0xFF
                reversed_yet = True
        hdx, hdy = dir_of(heading)
        cx += hdx * hv_speed
        cy += hdy * hv_speed

        # --- Missile.cs:1079-1089 -------------------------------------------
        tpx, tpy, tpz = cx, cy, AIRBURST_ALT
        tvx, tvy = cx - last_cx, cy - last_cy

        # --- Missile.cs:1092-1100, offset re-roll -----------------------------
        phys = math.sqrt((tpx - mx) ** 2 + (tpy - my) ** 2 + (tpz - alt) ** 2)
        if tick % RETARGET_TICKS == 0 and phys > OFFSET_FREEZE:
            offset = sample_offset(rng)

        # --- Missile.cs:1103-1106, lead + distances --------------------------
        hor = math.hypot(tpx - mx, tpy - my)
        ticks_to_reach = int(hor // speed)          # C# int division
        lead_x, lead_y = tvx * ticks_to_reach, tvy * ticks_to_reach

        p2 = (tpx + lead_x, tpy + lead_y, tpz)
        p3 = (p2[0] + offset[0], p2[1] + offset[1], p2[2])
        rel = math.sqrt((p3[0] - mx) ** 2 + (p3[1] - my) ** 2 + (p3[2] - alt) ** 2)
        rel_hor = math.hypot(p3[0] - mx, p3[1] - my)

        # --- Missile.cs:676, sticky Hitting ----------------------------------
        lr = loop_radius(speed, V_ROT_BASE)
        if rel_hor <= 3 * lr:
            state_hitting = True

        # --- Missile.cs:878-884, flyStraight latch ---------------------------
        cur = math.sqrt((tpx - mx) ** 2 + (tpy - my) ** 2 + (tpz - alt) ** 2)
        min_dist = min(min_dist, cur)
        if (not fly_straight and state_hitting
                and cur > min_dist + CLOSE_ENOUGH and cur > CLOSE_ENOUGH):
            fly_straight = True

        # --- Missile.cs:904-905 / 949-957, steering --------------------------
        h_rot, v_rot = H_ROT_BASE, V_ROT_BASE
        if state_hitting and rel_hor < 3 * lr:
            boost = min(3 * lr // max(int(rel_hor), 1), 3)
            h_rot = min(H_ROT_BASE * boost, 20)
            v_rot = min(V_ROT_BASE * boost, 20)
        if not fly_straight:
            vel_x = (p3[0] - mx) + tvx        # velVec = tarDistVec + predVel
            vel_y = (p3[1] - my) + tvy
            hfacing = tick_facing(hfacing, facing_of(vel_x, vel_y), h_rot)

            # Vertical analogue -- see the header. Signed facing in [-64, 64].
            want_v = facing_of(max(rel_hor, 1.0), p3[2] - alt)
            if want_v > 128:
                want_v -= 256
            cur_v = vfacing & 0xFF
            new_v = tick_facing(cur_v, want_v & 0xFF, v_rot)
            vfacing = new_v - 256 if new_v > 128 else new_v

        # --- Missile.cs:1130-1134, move (AllowSnapping is false) -------------
        lmx, lmy, lalt = mx, my, alt
        va = vfacing * math.pi / 128
        dx, dy = dir_of(hfacing)
        mx += dx * speed * math.cos(va)
        my += dy * speed * math.cos(va)
        alt += speed * math.sin(va)

        if alt < 0.0:
            return dict(reason="ground", ix=mx, iy=my, tick=tick,
                        cx=cx, cy=cy, hv_facing=heading, vf=vfacing)

        # --- Missile.cs:1163, endpoint clause (PRE-move dist, POST-move pos) --
        # `overshoot=False` is the ABLATION: detonate at the position that
        # actually satisfied the fuse rather than one tick further on.
        if post_move:
            # Self-consistent variant: test the fuse at the position the missile
            # actually reached, instead of the one it started the tick at.
            rel = math.sqrt((p3[0] - mx) ** 2 + (p3[1] - my) ** 2
                            + (p3[2] - alt) ** 2)
        if rel < CLOSE_ENOUGH:
            ex, ey = (mx, my) if overshoot else (lmx, lmy)
            return dict(reason="close_enough", ix=ex, iy=ey, tick=tick,
                        cx=cx, cy=cy, hv_facing=heading, vf=vfacing)

        # --- Missile.cs:1188-1214, swept-segment clause about P2 -------------
        sx, sy, sz = (mx - lmx, my - lmy, alt - lalt) if segment else (0, 0, 0)
        seg_sq = sx * sx + sy * sy + sz * sz
        if seg_sq > 0:
            tx, ty, tz = p2[0] - lmx, p2[1] - lmy, p2[2] - lalt
            dot = tx * sx + ty * sy + tz * sz
            t = max(0.0, min(1024.0, dot * 1024 / seg_sq))
            ccx = lmx + sx * t / 1024
            ccy = lmy + sy * t / 1024
            ccz = lalt + sz * t / 1024
            if ((p2[0] - ccx) ** 2 + (p2[1] - ccy) ** 2
                    + (p2[2] - ccz) ** 2) < CLOSE_ENOUGH ** 2:
                return dict(reason="segment_closest", ix=ccx, iy=ccy, tick=tick,
                            cx=cx, cy=cy, hv_facing=heading, vf=vfacing)

    return dict(reason="unterminated", ix=mx, iy=my, tick=TICK_CAP,
                cx=cx, cy=cy, hv_facing=heading, vf=vfacing)


# ------------------------------------------------------------------- the sweeps

_MEAS = HUMVEE_MEASURED_SPEED
CASES = [
    ("stationary",       dict(speed=0,     turn=0,           reverse=None)),
    (f"straight @{_MEAS}",
     dict(speed=_MEAS, turn=0, reverse=None)),
    (f"straight @{HUMVEE_NOMINAL_SPEED}",
     dict(speed=HUMVEE_NOMINAL_SPEED, turn=0, reverse=None)),
    (f"turning @{_MEAS}",
     dict(speed=_MEAS, turn=HUMVEE_TURN, reverse=None)),
    ("reverse @1500",    dict(speed=_MEAS, turn=0,           reverse=1500)),
    ("reverse @1000",    dict(speed=_MEAS, turn=0,           reverse=1000)),
]


def run_case(cfg, trials, alt0, seed, segment=True, overshoot=True,
             post_move=False):
    rng = random.Random(seed)
    landed = killed = detonated = 0
    dmg_total = 0.0
    miss_dists = []
    vfs = []
    reasons = {}
    # Sweep both the approach bearing and the humvee heading: the inaccuracy
    # cloud is square in WORLD axes while the hitshape is a rectangle in the
    # ACTOR frame, so neither angle averages out.
    per = max(1, trials // 64)
    for bi in range(8):
        for hi in range(8):
            bearing = bi * 32
            heading = hi * 32
            for _ in range(per):
                r = simulate_shot(rng, cfg["speed"], heading, cfg["turn"],
                                  bearing, cfg["reverse"], alt0,
                                  segment=segment, overshoot=overshoot,
                                  post_move=post_move)
                reasons[r["reason"]] = reasons.get(r["reason"], 0) + 1
                if r["reason"] != "unterminated":
                    detonated += 1
                vfs.append(r["vf"])
                d, hit = resolve_damage(r["ix"], r["iy"], r["cx"], r["cy"],
                                        r["hv_facing"])
                miss_dists.append(math.hypot(r["ix"] - r["cx"], r["iy"] - r["cy"]))
                if hit:
                    landed += 1
                    dmg_total += d
                    if d >= HUMVEE_HP:
                        killed += 1
    n = per * 64
    miss_dists.sort()
    vfs.sort()
    return dict(n=n, landed=landed / n, killed=killed / n,
                detonated=detonated / n,
                mean_dmg=dmg_total / n,
                median_miss=miss_dists[len(miss_dists) // 2],
                vf_lo=vfs[len(vfs) // 20], vf_hi=vfs[-len(vfs) // 20],
                reasons=reasons)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--trials", type=int, default=64_000)
    ap.add_argument("--alt", type=int, default=400,
                    help="terminal altitude at TERMINAL_ENTRY (audit measured 150-800)")
    ap.add_argument("--seed", type=int, default=20260821)
    args = ap.parse_args()

    print(__doc__.split("\n")[0])
    print(f"\nSIMULATED. {args.trials:,} shots/case, terminal altitude {args.alt} "
          f"at {TERMINAL_ENTRY} wdist out, seed {args.seed}.")
    print(f"Humvee {HUMVEE_HP} HP, hitshape {2*HALF_W}x{2*HALF_L}, "
          f"kill needs impact within {int(HALF_DIAG*0.6)} wdist of centre.\n")

    hdr = (f"{'target motion':<16} {'detonated':>10} {'landed':>8} {'killed':>8} "
           f"{'missiles/kill':>14} {'median miss':>12} {'mean dmg':>9}")
    print(hdr)
    print("-" * len(hdr))
    rows = []
    for name, cfg in CASES:
        r = run_case(cfg, args.trials, args.alt, args.seed)
        rows.append((name, r))
        mtk = 1 / r["killed"] if r["killed"] else float("inf")
        print(f"{name:<16} {100*r['detonated']:>9.1f}% {100*r['landed']:>7.1f}% "
              f"{100*r['killed']:>7.1f}% {mtk:>14.2f} {r['median_miss']:>12.0f} "
              f"{r['mean_dmg']:>9.0f}")

    print("\nWhy each missile ended (Missile.cs clause names):")
    for name, r in rows:
        tot = sum(r["reasons"].values())
        parts = ", ".join(f"{k} {100*v/tot:.0f}%"
                          for k, v in sorted(r["reasons"].items(),
                                             key=lambda kv: -kv[1]))
        print(f"  {name:<16} {parts}")

    print("\nMODEL CHECK -- terminal vFacing 5th..95th pct, against the audit's")
    print("MEASURED shipped band of -2..-15 facings:")
    for name, r in rows:
        print(f"  {name:<16} {r['vf_lo']:>4} .. {r['vf_hi']:>4}")

    print("\nABLATIONS -- kill %, to size the two fuse mechanisms directly.")
    print("  'no segment'  : clause 9 (Missile.cs:1188-1214) removed, so the missile")
    print("                  can only fuse on its own OFFSET aim point.")
    print("  'no overshoot': clause 4 detonates at the position that satisfied it,")
    print("                  undoing the one-tick displacement of Missile.cs:1103/1163.")
    print("  'post-move'   : clause 4 tested at the position the missile reached,")
    print("                  the self-consistent form of the same fix.")
    abl_hdr = (f"  {'target motion':<16} {'shipped':>9} {'no segment':>12} "
               f"{'no overshoot':>14} {'post-move':>11}")
    print(abl_hdr)
    for name, cfg in CASES:
        t = args.trials // 2
        a = run_case(cfg, t, args.alt, args.seed)
        b = run_case(cfg, t, args.alt, args.seed, segment=False)
        c = run_case(cfg, t, args.alt, args.seed, overshoot=False)
        d = run_case(cfg, t, args.alt, args.seed, post_move=True)
        print(f"  {name:<16} {100*a['killed']:>8.1f}% {100*b['killed']:>11.1f}% "
              f"{100*c['killed']:>13.1f}% {100*d['killed']:>10.1f}%")

    print("\nStarting-altitude sensitivity (kill %), audit measured dat 150-800:")
    print(f"  {'alt at 3000 out':<18} {'stationary':>11} {'straight @105':>14} "
          f"{'turning @105':>13}")
    for alt in (150, 300, 500, 800):
        a = run_case(CASES[0][1], args.trials // 4, alt, args.seed)
        b = run_case(CASES[1][1], args.trials // 4, alt, args.seed)
        c = run_case(CASES[3][1], args.trials // 4, alt, args.seed)
        print(f"  {alt:<18} {100*a['killed']:>10.1f}% {100*b['killed']:>13.1f}% "
              f"{100*c['killed']:>12.1f}%")


if __name__ == "__main__":
    main()
