#!/usr/bin/env python3
"""Composition half of blendmock: lays the rendered panels out into the options
sheet with its captions. Kept separate only so the rendering code above stays
readable; there is nothing interesting here beyond text placement."""
import os

from PIL import Image, ImageDraw, ImageFont

BG = (18, 20, 16)
FG = (208, 203, 184)
DIM = (132, 129, 114)
HOT = (232, 206, 122)
RED = (222, 122, 96)


def font(size, bold=False):
    names = ("segoeuib.ttf", "arialbd.ttf") if bold else ("segoeui.ttf", "arial.ttf")
    for n in names:
        for d in ("C:/Windows/Fonts/", ""):
            try:
                return ImageFont.truetype(d + n, size)
            except OSError:
                continue
    return ImageFont.load_default()


def wrap(d, text, f, width):
    lines, cur = [], ""
    for w in text.split():
        t = (cur + " " + w).strip()
        if d.textlength(t, font=f) <= width:
            cur = t
        else:
            lines.append(cur)
            cur = w
    if cur:
        lines.append(cur)
    return lines


class Sheet:
    def __init__(self, width):
        self.w = width
        self.items = []
        self.h = 0

    def add(self, fn, height):
        self.items.append((self.h, fn))
        self.h += height

    def render(self):
        im = Image.new("RGB", (self.w, self.h), BG)
        d = ImageDraw.Draw(im)
        for y, fn in self.items:
            fn(im, d, y)
        return im


def build(fig0, panels_fig1, panels_fig2, rim_strip, detail_pair, panel_size, out_path):
    PW, PH = panel_size
    GUT, PAD = 26, 44
    sheet = Sheet(3 * PW + 2 * GUT + 2 * PAD)
    f_h1, f_h2, f_cap, f_body = font(34, True), font(21, True), font(17, True), font(15)

    def header(im, d, y):
        d.text((PAD, y + 26), "Blast scars: what is actually blocky, and which fixes pay for themselves",
               font=f_h1, fill=FG)
        d.text((PAD, y + 72),
               "NukeW76, 100 kt, on real temperate terrain: forest, a civilian building and a beach edge. "
               "The forest density is the point, not decoration - every tree cell is a cell the warhead skips.",
               font=f_body, fill=DIM)
        d.rectangle([PAD, y + 104, sheet.w - PAD, y + 150], fill=(46, 31, 27))
        d.text((PAD + 14, y + 114),
               "THESE ARE PYTHON COMPOSITES, NOT ENGINE SCREENSHOTS.  Every sprite is decoded from the "
               "files this branch ships; the layout is a hand port of the engine's arithmetic, cited "
               "line by line in blendmock.py. No game was launched.", font=f_body, fill=(240, 178, 152))
    sheet.add(header, 172)

    def band(title, sub):
        def fn(im, d, y):
            d.text((PAD, y + 28), title, font=f_h2, fill=HOT)
            for k, ln in enumerate(wrap(d, sub, f_body, sheet.w - 2 * PAD)):
                d.text((PAD, y + 58 + k * 20), ln, font=f_body, fill=DIM)
        sheet.add(fn, 108)

    def grid(panels, cols=3, caph=98):
        rows = (len(panels) + cols - 1) // cols

        def fn(im, d, y):
            for i, (img, title, body, flag) in enumerate(panels):
                cx = PAD + (i % cols) * (PW + GUT)
                cy = y + (i // cols) * (PH + caph + 16)
                im.paste(img.convert("RGB"), (cx, cy))
                d.rectangle([cx, cy, cx + PW - 1, cy + PH - 1], outline=(60, 64, 52))
                d.text((cx, cy + PH + 10), title, font=f_cap, fill=RED if flag else FG)
                for k, ln in enumerate(wrap(d, body, f_body, PW)):
                    d.text((cx, cy + PH + 33 + k * 19), ln, font=f_body, fill=DIM)
        sheet.add(fn, rows * (PH + caph + 16))

    def strip(items):
        dw, dh = items[0][0].size

        def fn(im, d, y):
            for k, (src, lab, flag) in enumerate(items):
                cx = PAD + k * (dw + GUT)
                im.paste(src.convert("RGB"), (cx, y))
                d.rectangle([cx, y, cx + dw - 1, y + dh - 1], outline=(60, 64, 52))
                for j, ln in enumerate(wrap(d, lab, f_cap, dw)):
                    d.text((cx, y + dh + 10 + j * 21), ln, font=f_cap, fill=RED if flag else FG)
        sheet.add(fn, dh + 58)

    band("0 - FIRST: which of these two is the screenshot?",
         "The five graded bands merged at 03:35 on 2026-09-08, hours before the report. If the screenshot came "
         "off a build older than that, the flat blocky disc on the left IS the bug, and it is already fixed. "
         "Everything below assumes the right-hand one; the left is here so that assumption can be checked.")
    strip(fig0)

    band("1 - The rendering: what the banding already fixed, and what it did not",
         "Measured on these renders, the radial gradient is ALREADY smooth - the banding work bought that. "
         "So panels 2-5 are honest about buying little. Every panel here keeps the shipped holes at the trees "
         "and the building, which is figure 2, and which on this terrain is the artefact that actually shows.")
    grid(panels_fig1)

    band("2 - The holes are not a rendering problem at all",
         "Nothing in figure 1 changes them. A tree cell or a building cell is skipped by the WARHEAD, before "
         "any of this, and the fix is a target-type list: not art, not a layer, not a shader.")
    grid(panels_fig2, caph=44)

    band("3 - The outer rim, at 4x",
         "The outer contour, where a hard staircase would show if there were one. There is not: the outermost "
         "band is only 14% covered, so it dissolves on its own. Per-cell alpha and the decal are both nearly "
         "indistinguishable from what already ships. This is the evidence for not building either of them.")
    strip(rim_strip)

    band("4 - The beach, at 6x",
         "The engine stores ONE terrain type per cell; the half-land-half-water that a beach tile SHOWS is "
         "baked into its 24px image and nothing reads it back. The mask on the right is recovered from that "
         "image by classifying pixels, not hand-authored.")
    strip(detail_pair)

    os.makedirs(os.path.dirname(out_path), exist_ok=True)
    sheet.render().save(out_path)
    return out_path
