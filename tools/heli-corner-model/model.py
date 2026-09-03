#!/usr/bin/env python3
"""Tick-accurate model of the CanSlide (helicopter) movement path, used to derive the pass thresholds in
tools/autotest/scenarios/test-heli-corner-flow WITHOUT spending a run slot.

WHY THIS EXISTS. The corner-release feature (Aircraft.WaypointReleaseAggression, Fly.cs CanSlide branch,
AircraftCornerMath) is derived geometry. The unit tests pin the release DISTANCE, but the thing the
scenario asserts on -- how far past the outbound leg's line the airframe ends up -- is the result of ~35
ticks of integer simulation and cannot be read off a formula. A threshold guessed at instead of derived is
how the first draft of that scenario ended up failing its own criterion at the shipped default, one sample
away from passing, having never been run.

WHAT IS REPLICATED, and it is replicated rather than approximated:

  * WAngle.Cos / Sin / ArcTan against the ENGINE'S OWN TABLES, extracted verbatim into _tables.py. ArcTan
    is a 256-entry linear search, so a heading is quantised to 1/1024 of a circle and is NOT atan2. A float
    atan2 substituted here shifts which tick the release fires on, which moves the answer by a whole
    sample -- this is the single most important thing in this file to get right.
  * Exts.ISqrt, the two-bits-at-a-time integer square root, NOT math.sqrt.
  * C# integer division, which TRUNCATES TOWARD ZERO where Python's // floors. Every division on the
    simulated path goes through cdiv().
  * The one genuine float in the engine path, (float)Info.Speed / horizontalSpeed at Aircraft.cs:507,
    modelled in numpy.float32 because C# `float` is single precision.
  * Semi-implicit Euler ordering: Aircraft.Tick adds the acceleration and THEN moves by the new velocity.

WHAT IS NOT, and therefore what a number out of this file cannot settle: altitude and pitch (the vertical
axis does not couple back into the horizontal one), Repulse() (no second actor in the modelled scenario --
note that with one it adds a further FlyStep AFTER the velocity step and the airframe can then pass a
waypoint it would otherwise have released before), fog, and anything an order gives that the scenario's
Lua Move does not. This models one airframe flying an L in an empty world, which is exactly what the
scenario stages and nothing more.

Usage:
    python3 model.py            # the scenario's geometry, both arms, plus the aggression sweep
    python3 model.py --sweep    # aggression sweep only, machine-readable
"""

import sys
from _tables import COSINE_TABLE, TAN_TABLE

try:
    import numpy as np
    F32 = np.float32
except ImportError:                                             # pragma: no cover
    F32 = float

CELL = 1024


def cdiv(a, b):
    """C# integer division: truncates toward zero. Python's // floors, which differs on negatives."""
    q = abs(a) // abs(b)
    return q if (a < 0) == (b < 0) else -q


def isqrt(n):
    """Exts.ISqrt(uint, Floor) -- the engine's integer sqrt, two bits at a time."""
    if n < 0:
        raise ValueError(n)
    divisor = 1 << 30
    root = 0
    remainder = n
    while divisor > n:
        divisor >>= 2
    while divisor != 0:
        if root + divisor <= remainder:
            remainder -= root + divisor
            root += 2 * divisor
        root >>= 1
        divisor >>= 2
    return root


def wangle(a):
    """WAngle constructor: a % 1024, forced non-negative."""
    a %= 1024
    return a + 1024 if a < 0 else a


def wcos(a):
    a = wangle(a)
    if a <= 256:
        return COSINE_TABLE[a]
    if a <= 512:
        return -COSINE_TABLE[512 - a]
    return -wcos(a - 512)


def wsin(a):
    return wcos(wangle(a - 256))


def arctan(y, x):
    """WAngle.ArcTan(y, x, stride=1) -- a 256-entry linear search over TanTable, not atan2."""
    if y == 0:
        return wangle(0 if x >= 0 else 512)
    if x == 0:
        return wangle((1 if y > 0 else -1) * 256)
    ay, ax = abs(y), abs(x)
    best_val, best_angle = None, 0
    for i in range(256):
        val = abs(1024 * ay - ax * TAN_TABLE[i])
        if best_val is None or val < best_val:
            best_val, best_angle = val, i
    if x < 0 < y:
        best_angle = 512 - best_angle
    elif x < 0 and y < 0:
        best_angle = 512 + best_angle
    elif x > 0 > y:
        best_angle = 1024 - best_angle
    return wangle(best_angle)


def yaw(vx, vy):
    """WVec.Yaw -- ArcTan(-Y, X) - 256. OpenRA defines north as -y."""
    if vx == 0 and vy == 0:
        return 0
    return wangle(arctan(-vy, vx) - 256)


def vlen(x, y):
    return isqrt(x * x + y * y)


def angle_diff(a, b):
    """WAngle.AngleDiff -- unsigned, wraps the short way, in [0, 512]."""
    d = abs(wangle(a) - wangle(b))
    return 1024 - d if d > 512 else d


def release_distance(speed, max_accel, deflection, aggression, leg_length):
    """AircraftCornerMath.ReleaseDistance, verbatim."""
    if speed <= 0:
        return 0
    sin_half = wsin(deflection // 2)
    geometric = cdiv(speed * speed * sin_half * aggression, max_accel * 1024 * 100)
    cap = cdiv(leg_length, 2)
    if geometric > cap:
        geometric = cap
    floor = speed + max_accel
    return geometric if geometric > floor else floor


def accel_to_waypoint(wp, pos, vel, speed_cap, max_accel, stop_at_waypoint):
    """Aircraft.CalculateAccelerationToWaypoint, verbatim including the (int)Math.Sqrt double."""
    dx, dy = wp[0] - pos[0], wp[1] - pos[1]
    distance = vlen(dx, dy)
    if distance == 0:
        return (0, 0)
    dirx, diry = cdiv(dx * 1024, distance), cdiv(dy * 1024, distance)
    dvx, dvy = cdiv(dirx * speed_cap, 1024), cdiv(diry * speed_cap, 1024)

    if stop_at_waypoint:
        ideal = int((2.0 * max_accel * distance) ** 0.5)          # (int)Math.Sqrt(...)
        if ideal < max_accel:
            dvx, dvy = 0, 0
        else:
            s = min(ideal, speed_cap)
            dvx, dvy = cdiv(dirx * s, 1024), cdiv(diry * s, 1024)

    ddx, ddy = dvx - vel[0], dvy - vel[1]
    dd_len = vlen(ddx, ddy)

    if dd_len <= max_accel and (dvx, dvy) != (0, 0):
        ds = vlen(dvx, dvy)
        if ds > 0:
            return (cdiv(dvx * max_accel, ds), cdiv(dvy * max_accel, ds))
        return (0, 0)

    if dd_len == 0:
        return (0, 0)

    mag = min(max_accel, dd_len)
    return (cdiv(ddx * mag, dd_len), cdiv(ddy * mag, dd_len))


class Leg:
    """One Fly activity on the CanSlide path."""

    def __init__(self, target, nxt=None):
        self.target = target
        self.next = nxt
        self.leg_length = -1


class Sim:
    def __init__(self, start, legs, speed=245, max_accel=10, aggression=100, max_deflection=384):
        self.pos = list(start)
        self.vel = [0, 0]
        self.legs = legs
        self.i = 0
        self.speed_cap = speed
        self.max_accel = max_accel
        self.aggression = aggression
        self.max_deflection = max_deflection
        self.track = []
        self.releases = []

    def early_release(self, leg, speed, inbound_yaw):
        if self.aggression <= 0 or leg.next is None:
            return 0
        nxt = leg.next.target
        ox, oy = nxt[0] - leg.target[0], nxt[1] - leg.target[1]
        if ox == 0 and oy == 0:
            return 0
        deflection = angle_diff(inbound_yaw, yaw(ox, oy))
        if deflection > self.max_deflection:
            return 0
        return release_distance(speed, self.max_accel, deflection, self.aggression, leg.leg_length)

    def tick(self):
        """One Actor.Tick: run the activity chain to a fixed point, then apply Aircraft.Tick."""
        accel = (0, 0)
        while self.i < len(self.legs):
            leg = self.legs[self.i]
            done, accel = self.fly_tick(leg)
            if not done:
                break
            self.i += 1                                          # RunActivity chains in the SAME tick
        if self.i >= len(self.legs):
            return False

        # Aircraft.Tick
        if accel != (0, 0):
            self.vel[0] += accel[0]
            self.vel[1] += accel[1]
            hs = vlen(self.vel[0], self.vel[1])
            if hs > self.speed_cap:
                ratio = F32(self.speed_cap) / F32(hs)
                self.vel = [int(F32(self.vel[0]) * ratio), int(F32(self.vel[1]) * ratio)]
        elif self.vel != [0, 0]:
            cs = vlen(self.vel[0], self.vel[1])
            if cs <= self.max_accel:
                self.vel = [0, 0]
            else:
                self.vel = [self.vel[0] - cdiv(self.vel[0] * self.max_accel, cs),
                            self.vel[1] - cdiv(self.vel[1] * self.max_accel, cs)]

        if self.vel != [0, 0]:
            self.pos[0] += self.vel[0]
            self.pos[1] += self.vel[1]

        self.track.append((tuple(self.pos), vlen(self.vel[0], self.vel[1]), self.i))
        return True

    def fly_tick(self, leg):
        """Fly.Tick, CanSlide branch. Returns (activity_finished, requested_acceleration)."""
        dx, dy = leg.target[0] - self.pos[0], leg.target[1] - self.pos[1]
        speed = vlen(self.vel[0], self.vel[1])
        dist = vlen(dx, dy)

        if leg.leg_length < 0:
            leg.leg_length = dist

        # Precise arrival
        if speed <= self.max_accel and dist <= self.max_accel * 3:
            self.pos = [leg.target[0], leg.target[1]]
            self.vel = [0, 0]
            return True, (0, 0)

        desired_facing = yaw(dx, dy) if (dx or dy) else 0
        inbound = yaw(self.vel[0], self.vel[1]) if speed > 0 else desired_facing
        rel = self.early_release(leg, speed, inbound)
        if rel > 0 and dist <= rel:
            self.releases.append((tuple(self.pos), dist, speed, rel))
            return True, (0, 0)

        accel = accel_to_waypoint(leg.target, self.pos, self.vel,
                                  self.speed_cap, self.max_accel, rel == 0)

        # Overshoot prediction
        pvx, pvy = self.vel[0] + accel[0], self.vel[1] + accel[1]
        ps = vlen(pvx, pvy)
        if ps > self.speed_cap:
            pvx, pvy = cdiv(pvx * self.speed_cap, ps), cdiv(pvy * self.speed_cap, ps)
        if dx * dx + dy * dy < pvx * pvx + pvy * pvy:
            if speed <= self.max_accel * 2:
                self.pos = [leg.target[0], leg.target[1]]
                self.vel = [0, 0]
                return True, (0, 0)
            if speed > 0:
                accel = (cdiv(self.vel[0] * -self.max_accel, speed),
                         cdiv(self.vel[1] * -self.max_accel, speed))
        return False, accel


def centre(cx, cy):
    return (1024 * cx + 512, 1024 * cy + 512)


def run_l(start_cell, corner_cell, end_cell, aggression, **kw):
    """The scenario's shape: one intermediate waypoint at a 90-degree corner, then a terminal one."""
    start, corner, end = centre(*start_cell), centre(*corner_cell), centre(*end_cell)
    terminal = Leg(end)
    legs = [Leg(corner, terminal), terminal]
    sim = Sim(start, legs, aggression=aggression, **kw)
    for _ in range(4000):
        if not sim.tick():
            break
    return sim, corner, end


def measure(sim, corner, end, window=8 * CELL):
    """The scenario's own metrics, computed the way its Lua computes them."""
    min_speed = None
    max_east = 0                     # signed, east of the outbound line -- the Lua's `past`
    max_abs_off = 0                  # unsigned distance from the outbound line, after the corner
    closest = None
    in_window = False
    window_ticks = 0
    off_at_close = None

    for (pos, speed, _) in sim.track:
        to_corner = vlen(pos[0] - corner[0], pos[1] - corner[1])
        if closest is None or to_corner < closest:
            closest = to_corner
        if not in_window and to_corner <= window:
            in_window = True
        if in_window:
            window_ticks += 1
            if min_speed is None or speed < min_speed:
                min_speed = speed
            past = pos[0] - corner[0]
            if past > max_east:
                max_east = past
            if pos[1] < corner[1]:                      # past the corner, on the outbound leg
                max_abs_off = max(max_abs_off, abs(pos[0] - corner[0]))
            if corner[1] - pos[1] >= window:
                off_at_close = pos[0] - corner[0]
                break

    terminal_err = vlen(sim.pos[0] - end[0], sim.pos[1] - end[1])
    return dict(min_speed=min_speed, max_east=max_east, max_abs_off=max_abs_off,
                closest=closest, window_ticks=window_ticks, off_at_close=off_at_close,
                terminal_err=terminal_err, ticks=len(sim.track))


LANE_ON = ((6, 28), (26, 28), (26, 6))
LANE_OFF = ((36, 28), (56, 28), (56, 6))


def main():
    sweep_only = '--sweep' in sys.argv

    if not sweep_only:
        print('=== scenario geometry: 20 cells east, 90-degree corner, 22 cells north ===')
        print('    HELI Speed 245, MaxAcceleration 10 (trait default)\n')
        for label, lane, agg in (('LaneOn  (treatment, aggression 100)', LANE_ON, 100),
                                 ('LaneOff (control,   aggression 0)  ', LANE_OFF, 0)):
            sim, corner, end = run_l(*lane, aggression=agg)
            m = measure(sim, corner, end)
            print(f'  {label}')
            print(f'      minSpeed={m["min_speed"]}  maxEast={m["max_east"]}  '
                  f'maxAbsOffAfterCorner={m["max_abs_off"]}  offAtClose={m["off_at_close"]}')
            print(f'      closestApproachToWaypoint={m["closest"]}  windowTicks={m["window_ticks"]}  '
                  f'terminalError={m["terminal_err"]}  totalTicks={m["ticks"]}')
            if sim.releases:
                pos, dist, speed, rel = sim.releases[0]
                print(f'      released {dist} WDist short (computed {rel}) at speed {speed}')
            else:
                print('      no early release (flew to the waypoint and stopped)')
            print()

    print('=== aggression sweep, LaneOn geometry ===')
    print('  agg   minSpeed   maxEast  maxAbsOff  offAtClose  releaseAt   termErr')
    for agg in [0, 25, 50, 75, 90, 95, 100, 105, 110, 120, 125, 150, 175, 200, 250, 300]:
        sim, corner, end = run_l(*LANE_ON, aggression=agg)
        m = measure(sim, corner, end)
        rel = sim.releases[0][3] if sim.releases else 0
        print(f'  {agg:4}   {str(m["min_speed"]):>8}  {m["max_east"]:>8}  {m["max_abs_off"]:>9}  '
              f'{str(m["off_at_close"]):>10}  {rel:>9}   {m["terminal_err"]:>7}')


if __name__ == '__main__':
    main()
