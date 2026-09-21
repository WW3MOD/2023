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

THE GEOMETRY, AND WHY THE SCALE IS FITTED AND NOT ASSUMED
---------------------------------------------------------
Cell (cx,cy) maps to a screen square of side P centred on the camera cell:

    left = W/2 + (cx - camX - 0.5) * P        P = 24 * zoom * display_scale

`display_scale` IS A PROPERTY OF THE SESSION THAT RAN THE CAPTURE, NOT OF THIS
TOOL, and the first version of this file got that wrong. It hardcoded 1.25 from a
single observation -- a run captured while Windows was at 125% -- and every later
run on a 100% session was rejected by its own self-check. The two are not
distinguishable from the file: a 17:37 capture and a 21:06 capture of the SAME
scenario at the SAME 2160x1147 window rendered at 60 and 48 px per cell
respectively, and the 21:06 frames rendered at 48 whether the window was 1728x918
or 2160x1147. Window size does not tell you the scale; it only tells you how much
map is on screen.

So the scale is now MEASURED. --patch names the crop patch's cell rectangle, and
the tool tries each plausible display scale, predicts where that rectangle would
land, and keeps the SMALLEST one that captures at least 90% of the frame's wheat.
That separates cleanly rather than marginally: on the three frames this was built
against the right scale scores 0.997 and the next one down scores 0.598.

WHAT THE OLD CHECK GOT WRONG, BEYOND THE CONSTANT. It located the patch by its
FIRST bright-wheat pixel and compared that against the predicted corner. Even with
the right scale that is a fragile anchor, because the thing being measured --
how much the scar darkens the crop -- acts directly on it: darken the patch's
near edge enough and the check fails for the reason it exists to detect. The
replacement uses the wheat's DISTRIBUTION over the whole patch, which a darker
scar thins but does not move.

Usage:
  python tools/impact-scar/scar_density.py SHOT.png --camera 39,15 --zoom 2 \
      --cells 36,14 --neighbours --gz 33,16 --patch 28,11,38,21

--ui-scale overrides the fit for a frame that carries no crop patch to fit against.
"""
import argparse
import sys

import numpy as np
from PIL import Image, ImageFilter

CELL_PX = 24

# Display scales a session can plausibly have run at, ascending. Windows offers
# 100/125/150/175/200%; the rest are there so an unusual setting reports itself as a
# scale rather than as a mystery failure.
DISPLAY_SCALES = (1.0, 1.25, 1.5, 1.75, 2.0, 2.25, 2.5, 3.0)

# How much of the frame's wheat a candidate scale must place inside the patch to be
# accepted. Deliberately far below what the right scale achieves (0.997 on every frame
# this was built against) and far above what the next scale down achieves (0.598), so
# the margin absorbs a scar dark enough to erase whole cells of crop.
MIN_PATCH_RECALL = 0.90

# How far ABOVE its same-band neighbours' mean a cell may read and still count as
# "scarred like them".
#
# ONE-SIDED, AND THAT IS A CORRECTION RATHER THAN A LOOSENING. The defect this measures
# is farmland that stayed UNBURNT under a nuke: it makes a cell read too BRIGHT, never
# too dark. A two-sided band was the first version's mistake and it failed the fixed
# build -- cell 36,14 came out 12.2 points BELOW its neighbours once the scar was drawn
# on it, which reads as a regression and is not one.
#
# Two things push an occupied cell's number down and neither can push it up, so the low
# side carries no signal about this defect. Measured on frame B:
#   * The vehicle's SHADOW is dark, is not blue, and so is not excluded by the actor
#     mask -- it is counted as unburnt ground that holds no wheat. 8.6% of that cell's
#     ground is below luminance 22 against 3.4% and 5.4% on its two neighbours.
#   * The half of the cell that survives masking is its PERIPHERY, and the crop sprite's
#     wheat is not uniform -- the hull sits on the densest part of it. The offline
#     preview hits the same thing harder at 24 px a cell.
#
# A cell reading far BELOW its neighbours would be the other failure in the scenario's
# README -- a double composite making field cells darker than bare ground -- so it is
# reported as a note. It is not failed on, because nothing here can separate that from
# the two biases above, and a threshold picked to look decisive would be invented.
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
    def __init__(self, path, camera, px=None):
        a = np.asarray(Image.open(path).convert("RGB")).astype(int)
        self.h, self.w = a.shape[0], a.shape[1]
        self.wheat, self.actor = masks(a)
        self.cam = camera
        self.px = px

    def rect(self, patch, px):
        """The screen rectangle a cell rectangle would occupy at this scale."""
        px0, py0, px1, py1 = patch
        return (self.w / 2.0 + (px0 - self.cam[0] - 0.5) * px,
                self.h / 2.0 + (py0 - self.cam[1] - 0.5) * px,
                self.w / 2.0 + (px1 - self.cam[0] + 0.5) * px,
                self.h / 2.0 + (py1 - self.cam[1] + 0.5) * px)

    def fit_scale(self, patch, zoom, margin=6):
        """Measure px-per-cell by asking which display scale puts the frame's wheat
        inside the crop patch, and set self.px to it.

        THE SMALLEST ACCEPTABLE SCALE WINS, not the best-scoring one, and that is the
        whole trick. Recall alone is monotone in the wrong direction -- double the scale
        and the predicted rectangle swallows the screen, so it captures everything and
        scores 1.0. Requiring merely that a scale explains the wheat, and then taking
        the tightest such scale, gets the answer without needing a second criterion.

        `margin` forgives a few pixels of sprite overhang: ^CivField draws at
        RenderSprites.Scale 1.15, so the patch's art is slightly larger than its cells."""
        ys, xs = np.nonzero(self.wheat)
        if len(xs) == 0:
            raise SystemExit(
                "scale fit: this frame contains no bright wheat at all, so there is "
                "nothing to fit against. Pass --ui-scale explicitly, or check that this "
                "capture really is the one with the crop patch in it.")

        total = float(len(xs))
        table, chosen = [], None
        for scale in DISPLAY_SCALES:
            px = CELL_PX * zoom * scale
            x0, y0, x1, y1 = self.rect(patch, px)
            inside = ((xs >= x0 - margin) & (xs <= x1 + margin) &
                      (ys >= y0 - margin) & (ys <= y1 + margin)).sum()
            recall = inside / total
            table.append((scale, px, recall))
            if chosen is None and recall >= MIN_PATCH_RECALL:
                chosen = (scale, px, recall)

        summary = "  ".join(f"{s * 100:.0f}%:{r:.3f}" for s, _p, r in table)
        if chosen is None:
            raise SystemExit(
                "scale fit FAILED: no display scale places 90% of this frame's wheat "
                f"inside cells {patch[0]},{patch[1]}-{patch[2]},{patch[3]}. Recalls were "
                f"{summary}. That is not a scale problem -- --camera or --patch is wrong, "
                "or this is not the frame you think it is.")

        scale, px, recall = chosen
        self.px = px
        print(f"scale fit: {px:.1f} px/cell  (Camera.Zoom {zoom:g} at {scale * 100:.0f}% "
              f"display scale), {recall * 100:.1f}% of wheat inside the patch")
        print(f"           candidates {summary}")
        return px

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

    def check_patch(self, patch, max_empty_fraction=0.30):
        """Confirm the fitted geometry really does land on the patch's cells.

        BY DISTRIBUTION, NOT BY A CORNER. The previous version of this took the
        patch's first bright-wheat pixel and compared it with the predicted corner,
        which fails for the reason the tool exists: a scar that darkens the crop at the
        patch's near edge moves that first pixel, so the check breaks on exactly the
        change it is supposed to be measuring. What a darker scar does NOT do is move
        the crop -- so this asks whether every cell of the patch still holds some, and
        treats a cell that holds none as news rather than as a failure.

        The hard failure is reserved for the case the check is actually for: a
        transform so wrong that most of the patch has no crop in it at all."""
        px0, py0, px1, py1 = patch
        cells = [(cx, cy)
                 for cy in range(py0, py1 + 1)
                 for cx in range(px0, px1 + 1)]

        empty, offframe = [], 0
        for (cx, cy) in cells:
            x0, y0, x1, y1 = self.box(cx, cy)
            if x0 < 0 or y0 < 0 or x1 > self.w or y1 > self.h:
                offframe += 1
                continue
            if not self.wheat[y0:y1, x0:x1].any():
                empty.append((cx, cy))

        checked = len(cells) - offframe
        if checked == 0:
            raise SystemExit(
                f"patch check FAILED: none of the {len(cells)} patch cells is on this "
                "frame. --camera is wrong, or this capture is pointed somewhere else.")

        rx0, ry0, rx1, ry1 = self.rect(patch, self.px)
        print(f"patch check: cells {px0},{py0}-{px1},{py1} at "
              f"({rx0:.0f},{ry0:.0f})-({rx1:.0f},{ry1:.0f}); "
              f"{checked - len(empty)}/{checked} hold crop"
              + (f", {offframe} off-frame" if offframe else ""))

        if len(empty) > max_empty_fraction * checked:
            raise SystemExit(
                f"patch check FAILED: {len(empty)} of {checked} patch cells hold no crop "
                "at all. At that rate the cell grid is not on the patch, so every number "
                "below would be measuring the wrong cells. Check --camera and --patch "
                "against the scenario's Lua.")

        if empty:
            # Worth saying out loud: a cell of farmland with no crop pixel left is
            # either a very dark scar or a vehicle covering the whole cell, and both
            # are things the reader is here to find out about.
            shown = ", ".join(f"{cx},{cy}" for cx, cy in empty[:8])
            more = f" (+{len(empty) - 8} more)" if len(empty) > 8 else ""
            print(f"             note: {len(empty)} patch cell(s) hold no crop pixel: "
                  f"{shown}{more}")


def parse_cell(s):
    x, y = s.split(",")
    return int(x), int(y)


def report(f, cx, cy, gz, neighbours):
    """Returns True when the cell is not brighter than its same-band neighbours by more
    than TOLERANCE, False when it is, None when there is nothing to compare against."""
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
    ok = bool(delta <= TOLERANCE)
    verdict = "PASS" if ok else "FAIL -- reads as unburnt farmland"
    print(f"  --> {cx},{cy} is {delta:+.1f} points from its same-band mean "
          f"({mean:.1f}%); bar is at most +{TOLERANCE:.0f}: {verdict}")

    if delta < -TOLERANCE:
        print(f"             note: it reads {-delta:.1f} points BELOW its neighbours. On a cell "
              "holding a vehicle that is expected -- the hull's shadow counts as ground "
              "and the hull covers the crop's densest part -- and it is not failed on. If "
              "this cell holds no vehicle, suspect a double composite instead.")

    return ok


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("shot")
    ap.add_argument("--camera", required=True, help="camera cell, e.g. 39,15")
    ap.add_argument("--zoom", type=float, required=True)
    ap.add_argument("--ui-scale", type=float, default=None,
                    help="display scale of the session that captured the frame. Omit to "
                         "measure it from --patch, which is what you want: it is a "
                         "property of that session and not of this machine.")
    ap.add_argument("--cells", required=True, help="semicolon-separated cells to report")
    ap.add_argument("--neighbours", action="store_true",
                    help="also report the 8 neighbours, split by annulus bucket")
    ap.add_argument("--gz", help="ground zero cell; needed for bucket grouping")
    ap.add_argument("--patch", help="crop patch cell rect x0,y0,x1,y1 -- geometry self-check")
    args = ap.parse_args()

    patch = tuple(int(v) for v in args.patch.split(",")) if args.patch else None
    f = Frame(args.shot, parse_cell(args.camera))

    if args.ui_scale is not None:
        f.px = CELL_PX * args.zoom * args.ui_scale
        print(f"scale given: {f.px:.1f} px/cell (Camera.Zoom {args.zoom:g} at "
              f"{args.ui_scale * 100:.0f}% display scale), not measured")
    elif patch:
        f.fit_scale(patch, args.zoom)
    else:
        raise SystemExit(
            "no --patch to measure the display scale against, and no --ui-scale given. "
            "The scale is a property of the session that captured the frame -- the same "
            "scenario at the same window size has rendered at both 48 and 60 px/cell -- "
            "so it cannot be assumed. Pass one or the other.")

    if patch:
        f.check_patch(patch)

    gz = parse_cell(args.gz) if args.gz else None
    verdicts = [report(f, cx, cy, gz, args.neighbours)
                for (cx, cy) in (parse_cell(c) for c in args.cells.split(";"))]

    return 2 if any(v is False for v in verdicts) else 0


if __name__ == "__main__":
    sys.exit(main())
