"""Minimum distance from any DEFCON border cell to any spawn point, per shipped map.

Static: no build, no launch. Reuses tools/nav-guard's own map loader and its mirror of
DefconWall.BuildRegion, so RegionTerrainTypes (river-zeta's Water/River/Bridge) is counted
as well as the authored RegionCells -- an audit over the authored list alone would miss
every water cell, and river-zeta is the one map that has them.

Answers the question FIX 4 asks: does the HOME starting-units annulus (OuterSupportRadius 7
for the motorized and air packages) reach the border on any shipped map? If not, wall-filtering
the home package moves nothing on any map a bot plays, and neither profile's opening changes.
"""
import math
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "tools", "nav-guard"))

import defcon_wall_audit as dwa        # noqa: E402
import modload                          # noqa: E402
import nav_guard                        # noqa: E402

OUTER = 7        # largest OuterSupportRadius in world.yaml (motorized, air)


rules = modload.load_mod(nav_guard.MOD_DIR)
maps = [modload.load_map(p) for p in modload.discover_maps(nav_guard.MOD_DIR)]

print(f"{'map':<24} {'spawns':>6} {'border':>7} {'terrain?':>9} {'min d':>6}  verdict")
print("-" * 76)

worst = None
for game_map in sorted(maps, key=lambda m: m.name):
    border, terrain, cells = dwa.region_from_map(game_map)
    spawns = dwa.spawns_of(game_map)

    if not border:
        print(f"{game_map.name:<24} {len(spawns):>6} {'-':>7} {'-':>9} {'-':>6}"
              f"  no region; derived bisector")
        continue

    best = min(math.hypot(c[0] - s[0], c[1] - s[1]) for c in border for s in spawns)
    reach = "HOME PACKAGE REACHES IT" if best <= OUTER else "clear"
    print(f"{game_map.name:<24} {len(spawns):>6} {len(border):>7} "
          f"{('yes' if terrain else 'no'):>9} {best:>6.1f}  {reach}")

    if worst is None or best < worst[1]:
        worst = (game_map.name, best)

print()
print(f"Closest border cell to any spawn, across every shipped map: {worst[1]:.1f} cells ({worst[0]}).")
print(f"Home annulus outer radius is {OUTER}, so the home package's reach stops "
      f"{worst[1] - OUTER:.1f} cells short of the nearest border.")
