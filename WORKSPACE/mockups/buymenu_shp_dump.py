#!/usr/bin/env python3
"""Decode WW3MOD cameo SHPs to PNG, and measure where the baked caption sits.

Read-only helper for WORKSPACE/recon/buymenu-audit.md. Ports the two loaders the
engine actually uses -- engine/OpenRA.Mods.Cnc/SpriteLoaders/ShpTDLoader.cs and
engine/OpenRA.Mods.Common/SpriteLoaders/ShpTSLoader.cs -- so the pixels here are
the pixels the sidebar draws, modulo the palette caveat below.

PALETTE CAVEAT: the sidebar draws cameos through the `chrome` palette, which is
mods/ww3mod/rules/palettes.yaml:58 -> temperat.pal, and the canonical temperat.pal
lives inside a Blowfish-encrypted local.mix. We substitute the map-local copy at
engine/mods/ra/maps/chernobyl/temperat.pal (768 bytes, same 6-bit VGA layout).
Geometry is exact; individual hues may differ slightly from in-game.
"""

import os
import struct
import sys

from PIL import Image

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
PAL = os.path.join(REPO, "engine", "mods", "ra", "maps", "chernobyl", "temperat.pal")
ICONS = os.path.join(REPO, "mods", "ww3mod", "bits", "misc", "icons")
UI = os.path.join(REPO, "mods", "ww3mod", "bits", "misc", "ui")


def load_palette(path):
    raw = open(path, "rb").read()
    assert len(raw) == 768, len(raw)
    return [(round(raw[i * 3] * 255 / 63),
             round(raw[i * 3 + 1] * 255 / 63),
             round(raw[i * 3 + 2] * 255 / 63)) for i in range(256)]


# ---------------------------------------------------------------- LCW (Format80)
def lcw_decode(src, dest, off):
    di, n = 0, len(dest)
    while True:
        if off >= len(src) or di >= n:
            return di
        i = src[off]; off += 1
        if not i & 0x80:
            second = src[off]; off += 1
            count = ((i & 0x70) >> 4) + 3
            rpos = ((i & 0x0F) << 8) + second
            if di + count > n:
                return di
            s = di - rpos
            for k in range(count):
                dest[di + k] = dest[di - 1] if di - s == 1 else dest[s + k]
            di += count
        elif not i & 0x40:
            count = i & 0x3F
            if count == 0:
                return di
            dest[di:di + count] = src[off:off + count]
            off += count; di += count
        else:
            c3 = i & 0x3F
            if c3 == 0x3E:
                count = struct.unpack_from("<H", src, off)[0]; off += 2
                colour = src[off]; off += 1
                for _ in range(count):
                    if di >= n:
                        return di
                    dest[di] = colour; di += 1
            else:
                if c3 == 0x3F:
                    count = struct.unpack_from("<H", src, off)[0]; off += 2
                else:
                    count = c3 + 3
                si = struct.unpack_from("<H", src, off)[0]; off += 2
                for _ in range(count):
                    if di >= n:
                        return di
                    dest[di] = dest[si]; di += 1; si += 1


def xor_delta(src, dest, off):
    """Port of engine/OpenRA.Mods.Cnc/FileFormats/XORDeltaCompression.cs."""
    di = 0
    n = len(dest)
    while off < len(src) and di < n:
        b = src[off]; off += 1
        if b == 0:
            count = src[off]; off += 1
            val = src[off]; off += 1
            for _ in range(count):
                if di >= n:
                    return
                dest[di] ^= val; di += 1
        elif b & 0x80 == 0:
            for _ in range(b):
                if di >= n:
                    return
                dest[di] ^= src[off]; off += 1; di += 1
        elif b != 0x80:
            di += b & 0x7F
        else:
            w = struct.unpack_from("<H", src, off)[0]; off += 2
            if w == 0:
                return
            if w & 0x8000 == 0:
                di += w
            elif w & 0x4000 == 0:
                for _ in range(w & 0x3FFF):
                    if di >= n:
                        return
                    dest[di] ^= src[off]; off += 1; di += 1
            else:
                val = src[off]; off += 1
                for _ in range(w & 0x3FFF):
                    if di >= n:
                        return
                    dest[di] ^= val; di += 1


def rle_zeros(src, dest, di, limit):
    off = 0
    while off < len(src) and di < limit:
        cmd = src[off]; off += 1
        if cmd == 0:
            if off >= len(src):
                break
            count = src[off]; off += 1
            di += count
        else:
            dest[di] = cmd; di += 1


# ---------------------------------------------------------------- SHP readers
def read_shp(path):
    """-> (width, height, [frame index-arrays over the full canvas])."""
    d = open(path, "rb").read()
    if struct.unpack_from("<H", d, 0)[0] == 0:      # SHP(TS)
        _, w, h, n = struct.unpack_from("<HHHH", d, 0)
        frames = []
        for i in range(n):
            base = 8 + i * 24
            fx, fy, fw, fh = struct.unpack_from("<HHHH", d, base)
            fmt = d[base + 8]
            foff = struct.unpack_from("<I", d, base + 20)[0]
            canvas = bytearray(w * h)
            if foff and fw and fh:
                dw = fw + (fw % 2)
                buf = bytearray(dw * (fh + fh % 2))
                p = foff
                if fmt == 3:
                    for j in range(fh):
                        ln = struct.unpack_from("<H", d, p)[0] - 2; p += 2
                        rle_zeros(d[p:p + ln], buf, dw * j, len(buf)); p += ln
                else:
                    ln = fw
                    if fmt == 2:
                        ln = struct.unpack_from("<H", d, p)[0] - 2; p += 2
                    for j in range(fh):
                        buf[dw * j:dw * j + ln] = d[p:p + ln]; p += ln
                for j in range(fh):
                    for k in range(fw):
                        yy, xx = fy + j, fx + k
                        if 0 <= yy < h and 0 <= xx < w:
                            canvas[yy * w + xx] = buf[dw * j + k]
            frames.append(bytes(canvas))
        return w, h, frames

    n, _, _, w, h, _ = struct.unpack_from("<HHHHHI", d, 0)   # SHP(TD)
    heads = []
    for i in range(n):
        v, roff, rfmt = struct.unpack_from("<IHH", d, 14 + i * 8)
        heads.append({"i": i, "off": v & 0xFFFFFF, "fmt": v >> 24,
                      "roff": roff, "rfmt": rfmt, "data": None})
    body_at = 14 + (n + 2) * 8
    body = d[body_at:]
    by_off = {hh["off"]: hh for hh in heads}

    def decomp(hh, depth=0):
        if hh["data"] is not None or depth > n:
            return
        buf = bytearray(w * h)
        if hh["fmt"] == 0x80:
            lcw_decode(body, buf, hh["off"] - body_at)
        else:
            ref = heads[hh["i"] - 1] if hh["fmt"] == 0x20 else by_off[hh["roff"]]
            decomp(ref, depth + 1)
            buf[:] = ref["data"]
            xor_delta(body, buf, hh["off"] - body_at)
        hh["data"] = bytes(buf)

    for hh in heads:
        decomp(hh)
    return w, h, [hh["data"] for hh in heads]


def to_png(w, h, idx, pal, transparent=(0,), shadow=()):
    im = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    px = im.load()
    for y in range(h):
        for x in range(w):
            v = idx[y * w + x]
            if v in transparent:
                continue
            if v in shadow:
                px[x, y] = (0, 0, 0, 140)
            else:
                r, g, b = pal[v]
                px[x, y] = (r, g, b, 255)
    return im


def trimmed_bounds(w, h, idx):
    """What ShpTDLoader.TrimmedFrame reports as Sprite.Size: bbox of non-zero
    indices, grown 1px each side where possible, then padded to even."""
    top, bottom, left, right = h - 1, 0, w - 1, 0
    any_px = False
    for y in range(h):
        for x in range(w):
            if idx[y * w + x]:
                any_px = True
                top = min(top, y); bottom = max(bottom, y)
                left = min(left, x); right = max(right, x)
    if not any_px:
        return (0, 0, 0, 0)
    if left > 0: left -= 1
    if top > 0: top -= 1
    if right < w - 1: right += 1
    if bottom < h - 1: bottom += 1
    tw, th = right - left + 1, bottom - top + 1
    return (left, top, tw + tw % 2, th + th % 2)


def caption_rows(w, h, idx, pal):
    """Rows carrying near-white pixels -- the baked caption and the bevel."""
    out = []
    for y in range(h):
        n = 0
        for x in range(w):
            v = idx[y * w + x]
            if v == 0:
                continue
            r, g, b = pal[v]
            if r > 200 and g > 200 and b > 200:
                n += 1
        if n:
            out.append((y, n))
    return out


def find(name):
    for base in (ICONS, UI):
        p = os.path.join(base, name + ".shp")
        if os.path.isfile(p):
            return p
    return None


def main(argv):
    pal = load_palette(PAL)
    names = argv[1:] or ["e1americaicon", "3tnkicon", "a10icon", "iconchevrons"]
    outdir = os.path.join(os.path.dirname(__file__), "assets")
    os.makedirs(outdir, exist_ok=True)
    for name in names:
        path = find(name)
        if path is None:
            print(f"MISSING {name}")
            continue
        w, h, frames = read_shp(path)
        print(f"{name:24s} {w}x{h} frames={len(frames)} "
              f"trimmed0={trimmed_bounds(w, h, frames[0])}")
        for i, f in enumerate(frames):
            im = to_png(w, h, f, pal, transparent=(0,), shadow=(3,))
            suffix = "" if len(frames) == 1 else f"-{i}"
            im.save(os.path.join(outdir, f"{name}{suffix}.png"))
        rows = caption_rows(w, h, frames[0], pal)
        print("   near-white rows (y:count):",
              " ".join(f"{y}:{c}" for y, c in rows))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
