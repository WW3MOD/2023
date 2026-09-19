#!/usr/bin/env python3
"""Measure, per map cell, how much of a nuclear scar actually reached the ground.

WHY THIS EXISTS
---------------
`test-field-swallows-nuke` produces three PNGs and grades none of them -- its own
README says so: "A PASS here means the frames are worth looking at". Judging them
by eye is how the defect this tool was written for survived a green run. A humvee
parked on scarred farmland left its whole cell showing bright unburnt wheat, and
the run that captured that passed.

So this turns "does that cell look scarred" into a number, for a named list of
cells, out of a screenshot the runner already wrote. No game launch, no engine.

WHAT IT MEASURES, AND WHY THAT AND NOT "DARKNESS"
------------------------------------------------
Not luminance. Three things in these frames are dark and only one of them is a
scar: the scar palette (near-neutral, near-black -- (32,28,28), (28,32,36),
(20,20,24) on temperate), the crop sprite's own soil between the wheat rows
(reddish brown, (113,44,36), luminance 63), and the vehicle's shadow. A luminance
threshold counts all three, and reports a cell holding a vehicle as DARKER than
the scarred farmland beside it -- which is backwards.

What separates the cases cleanly is the WHEAT: bright, strongly red-over-blue, and
present nowhere else on these maps. An unburnt field cell is dominated by it; a
scarred one keeps only what shows through the stipple gaps. So the metric is

    wheat% = bright wheat pixels / ground pixels

where GROUND excludes the vehicle sprite and its UI furniture (blue hull, yellow
health bar and selection diamond, dilated for the antialiased edge). That
exclusion is what makes an occupied cell comparable with an empty one at all --
a vehicle covers about half its cell, so any fraction taken over the whole cell
measures the vehicle's size as much as the scar's presence.

COMPARE WITHIN A BAND, NEVER ACROSS ONE
---------------------------------------
The five scar bands are annuli with deliberately different coverage (ScarCore
78-93% down to ScarRim 14-30%), so wheat% varies by band and a cell is only
comparable with cells in the SAME band. `Map.FindTilesInAnnulus` buckets a cell by
ceil(sqrt(dx^2+dy^2)) (MapGrid.cs:210); that is reproduced here, so --neighbours
separates same-band neighbours from the rest on its own.

Measured on run 260919_173731 (the pre-fix evidence), the four same-band
neighbours of 36,14 read 31.1 / 31.5 / 31.5 / 31.6 -- a 0.5-point spread -- while
36,14 itself read 51.4. That tightness is what makes a +/-5 point threshold mean
something.

THE GEOMETRY, AND THE TRAP IN IT
--------------------------------
Cell (cx,cy) maps to a screen square of side P centred on the camera cell:

    left = W/2 + (cx - camX - 0.5) * P        P = 24 * zoom * ui_scale

`ui_scale` is NOT 1 on this machine: Windows runs at 125%, so a Camera.Zoom of 2
renders 60 px per cell, not 48. Get P wrong and every number below measures the
wrong cell -- silently, and plausibly. --patch exists for exactly that: give it
the crop patch's cell rectangle and it checks the wheat really does land on those
cell boundaries before reporting anything.

Usage:
  python tools/impact-scar/scar_density.py SHOT.png --camera 39,15 --zoom 2 \
      --cells 36,14 --neighbours --gz 33,16 --patch 28,11,38,21
"""
import argparse
import sys

import numpy as np
from PIL import Image, ImageFilter

# Windows display scaling on this machine. Camera.Zoom 2 renders 60 px/cell, not 48.
DEFAULT_UI_SCALE = 1.25
CELL_PX = 24

# How far a cell may sit from its same-band neighbours' mean and still count as
# "scarred like them".
TOLERANCE = 5.0


def masks(a):
    """(wheat, actor) boolean masks over the whole frame."""
    r, g, b = a[..., 0], a[..., 1], a[..., 2]

    # Bright unburnt wheat. Strongly red-over-blue and mid-green: (231,146,40),
    # (215,121,16) and (239,174,85) are the three commonest colours of an untouched
    # v14 cell. The crop's own soil (113,44,36) fails the brightness test, and the
    # scar palette fails red-over-blue.
    wheat = (r >= 150) & (r - b >= 80) & (g >= 80) & (g <= 210)

    # The vehicle and its furniture. Blue-dominant is the hull -- no terrain, crop or
    # scar colour on these tilesets has B > R. The second clause is the yellow health
    # bar and the selection diamond.
    #
    # THE YELLOW CLAUSE IS TIGHT ON PURPOSE. Written loosely as g > 180 and b < 120 it
    # also matches the brightest wheat highlights, (239,174,85) and up, which are
    # exactly the pixels this is supposed to be counting. Requiring green almost as
    # high as red separates them: bar yellow is ~(255,220,0), wheat's own brightest is
    # (231,146,40) and (239,174,85), and neither survives g > 190.
    actor = (b > r + 10) | ((r > 190) & (g > 190) & (b < 70))

    # Dilate: sprite edges are antialiased against the ground and would otherwise be
    # counted as ground of an ambiguous colour.
    actor = np.asarray(Image.fromarray((actor * 255).astype(np.uint8))
                       .filter(ImageFilter.MaxFilter(7))) > 0
    return wheat, actor


def bucket(cx, cy, gz):
    """The engine's own annulus bucket, MapGrid.CreateTilesByDistance (MapGrid.cs:210)."""
    dx, dy = cx - gz[0], cy - gz[1]
    return int(np.ceil((dx * dx + dy * dy) ** 0.5))


class Frame:
    def __init__(self, path, camera, px):
        a = np.asarray(Image.open(path).convert("RGB")).astype(int)
        self.h, self.w = a.shape[0], a.shape[1]
        self.wheat, self.actor = masks(a)
        self.cam = camera
        self.px = px

    def box(self, cx, cy):
        x = self.w / 2.0 + (cx - self.cam[0] - 0.5) * self.px
        y = self.h / 2.0 + (cy - self.cam[1] - 0.5) * self.px
        return (int(round(x)), int(round(y)),
                int(round(x + self.px)), int(round(y + self.px)))

    def measure(self, cx, cy):
        x0, y0, x1, y1 = self.box(cx, cy)
        if x0 < 0 or y0 < 0 or x1 > self.w or y1 > self.h:
            return None
        ground = ~self.actor[y0:y1, x0:x1]
        w = (self.wheat[y0:y1, x0:x1] & ground).sum()
        n = ground.sum()
        return (100.0 * w / n if n else 0.0), int(n), int((~ground).sum())

    def check_patch(self, patch, tol=8):
        """Fail loudly if the geometry is wrong. The search window is the patch's own
        rows and columns plus a small margin, so a vehicle or a fire elsewhere in the
        frame cannot widen the box and hide a misalignment."""
        px0, py0, px1, py1 = patch
        ex0, ey0 = self.box(px0, py0)[0], self.box(px0, py0)[1]
        ex1, ey1 = self.box(px1, py1)[2], self.box(px1, py1)[3]
        wx0, wy0 = max(ex0 - 20, 0), max(ey0 - 20, 0)
        sub = self.wheat[wy0:ey1 + 20, wx0:ex1 + 20]
        ys, xs = np.nonzero(sub)
        if len(xs) == 0:
            raise SystemExit(
                "patch check: no wheat anywhere near the expected rectangle. Either "
                "--camera/--zoom/--ui-scale are wrong, or this frame holds no crop field.")

        gx0, gy0 = xs.min() + wx0, ys.min() + wy0
        off = (int(abs(gx0 - ex0)), int(abs(gy0 - ey0)))
        print(f"patch check: {self.px:.1f} px/cell; patch expected at "
              f"({ex0},{ey0})-({ex1},{ey1}), wheat starts at ({gx0},{gy0}), "
              f"offset {off[0]},{off[1]} px")
        if max(off) > tol:
            raise SystemExit(
                f"patch check FAILED: off by {off} px, tolerance {tol}. Every number "
                "below would be measuring the wrong cells. Check --zoom and --ui-scale "
                "against the scenario's Lua.")


def parse_cell(s):
    x, y = s.split(",")
    return int(x), int(y)


def report(f, cx, cy, gz, neighbours):
    """Returns True when the cell is within tolerance of its same-band neighbours,
    False when it is outside, None when there is nothing to compare against."""
    got = f.measure(cx, cy)
    if got is None:
        print(f"cell {cx},{cy}: off-frame")
        return None

    wf, ground, hidden = got
    tag = f" bucket {bucket(cx, cy, gz)}" if gz else ""
    print(f"\ncell {cx},{cy}{tag}: wheat {wf:5.1f}%  (ground {ground} px, actor {hidden} px)")
    if not neighbours:
        return None

    same, other = [], []
    for dy in (-1, 0, 1):
        for dx in (-1, 0, 1):
            if dx == 0 and dy == 0:
                continue
            n = f.measure(cx + dx, cy + dy)
            if n is None:
                continue
            entry = (cx + dx, cy + dy, n[0])
            if gz and bucket(cx + dx, cy + dy, gz) == bucket(cx, cy, gz):
                same.append(entry)
            else:
                other.append(entry)

    for label, group in (("same band", same), ("other bands", other)):
        if not group:
            continue
        vals = [v for _x, _y, v in group]
        print(f"  {label:<12} " + "  ".join(f"{x},{y}={v:.1f}%" for x, y, v in group))
        print(f"  {'':<12} mean {np.mean(vals):.1f}%  spread {min(vals):.1f}-{max(vals):.1f}%")

    if not same:
        print("  --> no same-band neighbour on this frame; nothing to compare against")
        return None

    mean = float(np.mean([v for _x, _y, v in same]))
    delta = wf - mean
    # bool(): numpy comparisons yield np.bool_, and `np.bool_(False) is False`
    # is False -- the caller's identity test would silently never fire.
    ok = bool(abs(delta) <= TOLERANCE)
    print(f"  --> {cx},{cy} is {delta:+.1f} points from its same-band mean "
          f"({mean:.1f}%): {'WITHIN' if ok else 'OUTSIDE'} +/-{TOLERANCE:.0f}")
    return ok


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("shot")
    ap.add_argument("--camera", required=True, help="camera cell, e.g. 39,15")
    ap.add_argument("--zoom", type=float, required=True)
    ap.add_argument("--ui-scale", type=float, default=DEFAULT_UI_SCALE)
    ap.add_argument("--cells", required=True, help="semicolon-separated cells to report")
    ap.add_argument("--neighbours", action="store_true",
                    help="also report the 8 neighbours, split by annulus bucket")
    ap.add_argument("--gz", help="ground zero cell; needed for bucket grouping")
    ap.add_argument("--patch", help="crop patch cell rect x0,y0,x1,y1 -- geometry self-check")
    args = ap.parse_args()

    f = Frame(args.shot, parse_cell(args.camera), CELL_PX * args.zoom * args.ui_scale)

    if args.patch:
        f.check_patch(tuple(int(v) for v in args.patch.split(",")))

    gz = parse_cell(args.gz) if args.gz else None
    verdicts = [report(f, cx, cy, gz, args.neighbours)
                for (cx, cy) in (parse_cell(c) for c in args.cells.split(";"))]

    return 2 if any(v is False for v in verdicts) else 0


if __name__ == "__main__":
    sys.exit(main())
