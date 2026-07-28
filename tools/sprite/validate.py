#!/usr/bin/env python3
"""Validate PNG frames before --shp import.

Usage: validate.py <temperat.pal> <frame0.png> [frame1.png ...]

Hard-fails (exit 1) on the first violation of:
  1. Indexed8 (PNG colour-type 3, bit-depth 8)
  2. all frames identical W x H
  3. palette == temperat.pal, index 0 (transparent) exempt

temperat.pal is 6-bit VGA (0-63); the PNG PLTE the exporter emits is 8-bit
(value * 255 / 63). We scale the .pal up and allow +/-1 rounding slack.
"""
import struct
import sys


def png_chunks(data):
    if data[:8] != b"\x89PNG\r\n\x1a\n":
        raise ValueError("not a PNG")
    i, out = 8, {}
    while i < len(data):
        (ln,) = struct.unpack(">I", data[i:i + 4])
        typ = data[i + 4:i + 8].decode("ascii")
        out.setdefault(typ, data[i + 8:i + 8 + ln])
        i += 12 + ln
    return out


def load_pal_scaled(path):
    raw = open(path, "rb").read()
    if len(raw) != 768:
        raise ValueError(f"{path}: expected 768-byte palette, got {len(raw)}")
    # 6-bit VGA -> 8-bit
    return [round(b * 255 / 63) for b in raw]


def main(argv):
    if len(argv) < 3:
        print("usage: validate.py <temperat.pal> <frame.png> [...]", file=sys.stderr)
        return 2

    pal = load_pal_scaled(argv[1])
    frames = argv[2:]
    size = None
    errors = []

    for path in frames:
        c = png_chunks(open(path, "rb").read())
        w, h, bitdepth, colortype = struct.unpack(">IIBB", c["IHDR"][:10])

        if colortype != 3 or bitdepth != 8:
            errors.append(
                f"{path}: not Indexed8 (colour-type={colortype}, bit-depth={bitdepth}); "
                f"re-save in indexed / 8-bit palette mode")
            continue

        if size is None:
            size = (w, h)
        elif (w, h) != size:
            errors.append(f"{path}: size {w}x{h} != {size[0]}x{size[1]} (frames must match)")

        plte = c.get("PLTE", b"")
        for idx in range(1, len(plte) // 3):  # index 0 exempt (transparent)
            r, g, b = plte[idx * 3:idx * 3 + 3]
            pr, pg, pb = pal[idx * 3], pal[idx * 3 + 1], pal[idx * 3 + 2]
            if abs(r - pr) > 1 or abs(g - pg) > 1 or abs(b - pb) > 1:
                errors.append(
                    f"{path}: palette drift at index {idx}: PNG {(r, g, b)} vs "
                    f"temperat.pal {(pr, pg, pb)}; the editor re-quantised the palette")
                break

    if errors:
        print("[validate] FAIL:", file=sys.stderr)
        for e in errors:
            print("  - " + e, file=sys.stderr)
        return 1

    print(f"[validate] OK: {len(frames)} frame(s), {size[0]}x{size[1]}, Indexed8, palette matches temperat.pal")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
