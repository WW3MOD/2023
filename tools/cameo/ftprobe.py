#!/usr/bin/env python3
"""Rasterise a glyph through THE ENGINE'S OWN FreeType, with the engine's own flags.

    python tools/cameo/ftprobe.py                    # show what it found and prove it works
    python tools/cameo/ftprobe.py --font PATH --size 7 --text "T90"

WHY THIS EXISTS AND WHAT IT IS EVIDENCE FOR
--------------------------------------------
Every other offline measurement in tools/cameo goes through Pillow, which carries its OWN
statically-linked FreeType. That is the right tool for "roughly what will this look like", and
WORKSPACE/mockups/glyph_probe.py says so in its own header: a hinted stem could in principle
land one pixel differently from the game's freetype6.

For the caption font that caveat is not good enough, because the whole acceptance rule is a
per-pixel one -- every lit pixel must come back at coverage 255 -- and hinting is exactly what
decides it. So this module loads the SAME BINARY the engine binds to:

  engine/OpenRA.Platforms.Default/OpenRA.Platforms.Default.csproj:4
      <PackageReference Include="OpenRA-Freetype6" Version="1.0.11" />
  -> ~/.nuget/packages/openra-freetype6/1.0.11/native/win-x64/freetype6.dll
  -> FreeTypeFont.cs:39   [DllImport("freetype6")]

and makes the same four calls in the same order with the same arguments as
FreeTypeFont.CreateGlyph: FT_Init_FreeType, FT_New_Memory_Face, FT_Set_Pixel_Sizes(n, n),
FT_Load_Char(c, FT_LOAD_RENDER). The struct offsets below are copied from FreeType's own
constants in that file rather than re-derived, so a change there is a one-line change here.

WHAT IS STILL NOT PROVEN. This is the right library, the right version and the right flags, but
it is not the running game: the game also has a `deviceScale` (the UI scale) that multiplies the
pixel size, and it uploads the coverage byte to a texture. Both are reproduced arithmetically
rather than observed -- `--size` takes the already-multiplied number, and SpriteFont.cs:285-293
is a straight copy of the byte into all four channels with no further processing, which is read
and not run. A launch is still the only thing that proves the pixels on screen.
"""

import argparse
import ctypes
import glob
import os
import sys

FT_LOAD_RENDER = 0x04
OK = 0

# FreeTypeFont.cs:26-37, 64-bit column. Kept in this order and with these names so the two files
# can be diffed by eye.
FACE_GLYPH = 152          # offsetof(FT_FaceRec, glyph)
SLOT_METRICS = 48         # offsetof(FT_GlyphSlotRec, metrics)
SLOT_BITMAP = 152         # offsetof(FT_GlyphSlotRec, bitmap)
SLOT_BITMAP_LEFT = 192    # offsetof(FT_GlyphSlotRec, bitmap_left)
SLOT_BITMAP_TOP = 196     # offsetof(FT_GlyphSlotRec, bitmap_top)
METRICS_WIDTH = 0         # offsetof(FT_Glyph_Metrics, width)
METRICS_HEIGHT = 8        # offsetof(FT_Glyph_Metrics, height)
METRICS_ADVANCE = 32      # offsetof(FT_Glyph_Metrics, horiAdvance)
BITMAP_PITCH = 8          # offsetof(FT_Bitmap, pitch)
BITMAP_BUFFER = 16        # offsetof(FT_Bitmap, buffer)

PACKAGE = "openra-freetype6"


def find_library():
    """The freetype6 the engine would load, from the NuGet cache the build restores into.

    Not from engine/bin: this worktree has never been built, and the main checkout is off limits.
    The cache copy is the same file the build would place there.
    """
    if sys.platform == "win32":
        arch = "win-x64" if sys.maxsize > 2 ** 32 else "win-x86"
        names = [f"native/{arch}/freetype6.dll"]
    elif sys.platform == "darwin":
        names = ["native/osx/libfreetype.6.dylib", "native/osx-x64/libfreetype.6.dylib"]
    else:
        names = ["native/linux-x64/libfreetype.so.6"]

    roots = [os.environ.get("NUGET_PACKAGES"),
             os.path.join(os.path.expanduser("~"), ".nuget", "packages")]
    for root in roots:
        if not root:
            continue
        for version in sorted(glob.glob(os.path.join(root, PACKAGE, "*")), reverse=True):
            for name in names:
                path = os.path.join(version, *name.split("/"))
                if os.path.isfile(path):
                    return path
    return None


class FreeType:
    """FreeTypeFont.cs, ported. One face, opened once, glyphs rendered one at a time."""

    def __init__(self, ttf_path, library_path=None):
        library_path = library_path or find_library()
        if not library_path:
            raise RuntimeError(
                f"no {PACKAGE} in the NuGet cache. `dotnet restore WW3MOD.sln` puts it there; "
                "until then only the Pillow measurement is available.")
        self.library_path = library_path
        self.lib = ctypes.CDLL(library_path)
        self.lib.FT_Init_FreeType.argtypes = [ctypes.POINTER(ctypes.c_void_p)]
        self.lib.FT_New_Memory_Face.argtypes = [ctypes.c_void_p, ctypes.c_void_p, ctypes.c_long,
                                                ctypes.c_long, ctypes.POINTER(ctypes.c_void_p)]
        self.lib.FT_Set_Pixel_Sizes.argtypes = [ctypes.c_void_p, ctypes.c_uint, ctypes.c_uint]
        self.lib.FT_Load_Char.argtypes = [ctypes.c_void_p, ctypes.c_ulong, ctypes.c_int]

        lib = ctypes.c_void_p()
        if self.lib.FT_Init_FreeType(ctypes.byref(lib)) != OK:
            raise RuntimeError("FT_Init_FreeType failed")
        self.library = lib

        with open(ttf_path, "rb") as f:
            self.data = f.read()
        self.buf = ctypes.create_string_buffer(self.data, len(self.data))
        face = ctypes.c_void_p()
        if self.lib.FT_New_Memory_Face(self.library, self.buf, len(self.data), 0,
                                       ctypes.byref(face)) != OK:
            raise RuntimeError(f"FT_New_Memory_Face failed for {ttf_path}")
        self.face = face.value

    def _long(self, addr):
        return ctypes.c_long.from_address(addr).value

    def _int(self, addr):
        return ctypes.c_int.from_address(addr).value

    def _ptr(self, addr):
        return ctypes.c_void_p.from_address(addr).value

    def glyph(self, char, size, device_scale=1.0):
        """(width, height, advance, left, top, coverage_bytes) -- CreateGlyph's FontGlyph.

        Returns None where CreateGlyph would return EmptyGlyph.
        """
        scaled = int(size * device_scale)
        if self.lib.FT_Set_Pixel_Sizes(self.face, scaled, scaled) != OK:
            return None
        if self.lib.FT_Load_Char(self.face, ord(char), FT_LOAD_RENDER) != OK:
            return None

        slot = self._ptr(self.face + FACE_GLYPH)
        metrics = slot + SLOT_METRICS
        # 26.6 fixed point, truncated by >> 6 exactly as FreeTypeFont.cs:105-106 does.
        w = self._long(metrics + METRICS_WIDTH) >> 6
        h = self._long(metrics + METRICS_HEIGHT) >> 6
        advance = self._long(metrics + METRICS_ADVANCE) >> 6
        left = self._int(slot + SLOT_BITMAP_LEFT)
        top = self._int(slot + SLOT_BITMAP_TOP)

        bitmap = slot + SLOT_BITMAP
        pitch = self._int(bitmap + BITMAP_PITCH)
        buffer_ = self._ptr(bitmap + BITMAP_BUFFER)

        data = bytearray(w * h)
        if w and h and buffer_:
            row = buffer_
            k = 0
            for _ in range(h):
                ctypes.memmove((ctypes.c_char * w).from_buffer(data, k), row, w)
                k += w
                row += pitch
        return (w, h, advance, left, top, bytes(data))

    def line(self, text, size, device_scale=1.0):
        """Every glyph of `text`, and the summed advance the widget would measure.

        SpriteFont.LineWidth sums Advance and divides by deviceScale; SpriteFont.Measure then
        takes the ceiling. Reproduced here so a width printed by this module is the width
        CameoCaptionCache.Fit compares against the slot budget.
        """
        out, width = [], 0
        for c in text:
            g = self.glyph(c, size, device_scale)
            out.append((c, g))
            width += g[2] if g else 0
        import math
        return out, math.ceil(width / device_scale)


def coverage(ft, text, size, device_scale=1.0):
    """Every non-zero coverage byte, flattened -- the thing the acceptance rule is about."""
    vals = []
    for _, g in ft.line(text, size, device_scale)[0]:
        if g:
            vals.extend(v for v in g[5] if v)
    return vals


def art(ft, text, size, device_scale=1.0):
    """The rendered line as rows of characters, glyphs placed the way SpriteFont.DrawText does.

    Baseline at row `size`; a glyph's top-left goes at (pen + left, baseline - top). That is
    DrawText's `screen + g.Offset` with Offset = (bitmapLeft, -bitmapTop).
    """
    glyphs, width = ft.line(text, size, device_scale)
    height = int(size * device_scale) + 2
    grid = [[0] * (width + 2) for _ in range(height)]
    pen, baseline = 0, int(size * device_scale)
    for _, g in glyphs:
        if g:
            w, h, adv, left, top, data = g
            for j in range(h):
                for i in range(w):
                    y, x = baseline - top + j, pen + left + i
                    if 0 <= y < height and 0 <= x < len(grid[0]):
                        grid[y][x] = max(grid[y][x], data[j * w + i])
            pen += adv
    return ["".join("#" if v == 255 else "+" if v else "." for v in row) for row in grid]


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    root = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
    ap.add_argument("--font", default=os.path.join(root, "mods", "ww3mod", "WW3Caption.ttf"))
    ap.add_argument("--size", type=int, default=7)
    ap.add_argument("--text", default="T90 MIG-29 1.2 MT")
    args = ap.parse_args()

    ft = FreeType(args.font)
    print("freetype6:", ft.library_path)
    print("font:     ", os.path.relpath(args.font, root), "at", args.size, "px")
    cov = coverage(ft, args.text, args.size)
    grey = sorted({v for v in cov if v != 255})
    print("coverage: ", f"{len(cov)} lit pixels, ALL 255" if not grey
          else f"{len(cov)} lit pixels, NOT 1-BIT: {grey[:12]}")
    for row in art(ft, args.text, args.size):
        print("  " + row)
    return 0


if __name__ == "__main__":
    sys.exit(main())
