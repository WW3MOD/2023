"""Read RA .mix archives and ShpTD sprites in pure Python.

Exists so the impact-scar art generator can quantise against the real
temperat.pal and measure the stock crater/scorch art WITHOUT a built engine.
The house round-trip (tools/sprite/export.sh) needs engine/bin; this does not.

Ports, byte for byte:
  * PackageEntry.HashFilename(Classic)  engine/OpenRA.Mods.Cnc/FileSystem/PackageEntry.cs:59
  * MixFile header detect               engine/OpenRA.Mods.Cnc/FileSystem/MixFile.cs:42-53
  * LCWCompression.DecodeInto           engine/OpenRA.Mods.Cnc/FileFormats/LCWCompression.cs:34
  * ShpTDLoader frame headers           engine/OpenRA.Mods.Cnc/SpriteLoaders/ShpTDLoader.cs:169-270
"""
import struct


def hash_filename(name):
    """Classic (RA1/TD) mix filename hash. PackageEntry.cs:70-84."""
    name = name.upper()
    pad = (4 - len(name) % 4) % 4
    data = name.encode("ascii") + b"\0" * pad
    result = 0
    for i in range(0, len(data), 4):
        nxt = struct.unpack("<I", data[i:i + 4])[0]
        result = (((result << 1) & 0xFFFFFFFF) | (result >> 31)) + nxt
        result &= 0xFFFFFFFF
    return result


class MixFile:
    def __init__(self, path):
        self.raw = open(path, "rb").read()
        # MixFile.cs:42 - a non-zero first uint16 means the C&C (headerless) format.
        is_cnc = struct.unpack("<H", self.raw[0:2])[0] != 0
        if is_cnc:
            off = 0
        else:
            flags = struct.unpack("<H", self.raw[2:4])[0]
            if flags & 0x2:
                raise NotImplementedError(f"{path} is encrypted; not supported here")
            off = 4
        count, _datasize = struct.unpack("<HI", self.raw[off:off + 6])
        off += 6
        self.index = {}
        for _ in range(count):
            h, o, ln = struct.unpack("<IiI", self.raw[off:off + 12])
            self.index[h & 0xFFFFFFFF] = (o, ln)
            off += 12
        self.data_start = off

    def get(self, name):
        ent = self.index.get(hash_filename(name))
        if ent is None:
            return None
        o, ln = ent
        start = self.data_start + o
        return self.raw[start:start + ln]


def lcw_decode(src, out_len, src_offset=0):
    """Format80 / LCW. Mirrors LCWCompression.DecodeInto."""
    dest = bytearray(out_len)
    i = src_offset
    d = 0
    while True:
        b = src[i]; i += 1
        if not b & 0x80:
            # case 2: relative back-reference
            second = src[i]; i += 1
            count = ((b & 0x70) >> 4) + 3
            rpos = ((b & 0x0F) << 8) + second
            if d + count > out_len:
                return bytes(dest)
            s = d - rpos
            for k in range(count):
                dest[d + k] = dest[d - 1] if d - s == 1 else dest[s + k]
            d += count
        elif not b & 0x40:
            # case 1: literal run; count 0 terminates
            count = b & 0x3F
            if count == 0:
                return bytes(dest)
            dest[d:d + count] = src[i:i + count]
            i += count
            d += count
        else:
            c3 = b & 0x3F
            if c3 == 0x3E:
                # case 4: RLE fill
                count = struct.unpack("<H", src[i:i + 2])[0]; i += 2
                color = src[i]; i += 1
                for k in range(count):
                    dest[d + k] = color
                d += count
            else:
                # case 3 / case 5: absolute back-reference
                if c3 == 0x3F:
                    count = struct.unpack("<H", src[i:i + 2])[0]; i += 2
                else:
                    count = c3 + 3
                s = struct.unpack("<H", src[i:i + 2])[0]; i += 2
                for k in range(count):
                    dest[d + k] = dest[d - 1] if d - s == 1 else dest[s + k]
                d += count
        if d >= out_len:
            return bytes(dest)


def xor_delta_decode(src, dest, src_offset=0):
    """XORDeltaCompression.DecodeInto -- in-place XOR patch of `dest`.

    Ported from engine/OpenRA.Mods.Cnc/FileFormats/XORDeltaCompression.cs:20-79.
    """
    i = src_offset
    d = 0
    while True:
        b = src[i]; i += 1
        if not b & 0x80:
            count = b & 0x7F
            if count == 0:
                # case 6: XOR a repeated value
                count = src[i]; i += 1
                value = src[i]; i += 1
                for _ in range(count):
                    dest[d] ^= value; d += 1
            else:
                # case 5: XOR a literal run
                for _ in range(count):
                    dest[d] ^= src[i]; i += 1; d += 1
        else:
            count = b & 0x7F
            if count == 0:
                count = struct.unpack("<H", src[i:i + 2])[0]; i += 2
                if count == 0:
                    return d
                if not count & 0x8000:
                    d += count & 0x7FFF                      # case 2: skip
                elif not count & 0x4000:
                    for _ in range(count & 0x3FFF):          # case 3: XOR literals
                        dest[d] ^= src[i]; i += 1; d += 1
                else:
                    value = src[i]; i += 1                   # case 4: XOR repeated
                    for _ in range(count & 0x3FFF):
                        dest[d] ^= value; d += 1
            else:
                d += count                                   # case 1: skip


def read_shp(data):
    """ShpTD -> (width, height, [frame bytes]).

    Handles all three frame formats: LCW (0x80) standalone, XORPrev (0x20)
    delta against frame i-1, and XORLCW (0x40) delta against the frame whose
    file offset equals RefOffset. ShpTDLoader.cs:224-280.
    """
    count, _a, _b, w, h, _c, _d = struct.unpack("<HHHHHHH", data[0:14])
    hdrs = []
    p = 14
    for _ in range(count):
        v, ref_off, _ref_fmt = struct.unpack("<IHH", data[p:p + 8])
        hdrs.append({"off": v & 0x00FFFFFF, "fmt": (v >> 24) & 0xFF,
                     "ref": ref_off, "data": None})
        p += 8

    by_off = {h_["off"]: h_ for h_ in hdrs}
    for idx, h_ in enumerate(hdrs):
        if h_["fmt"] == 0x20:
            h_["refimg"] = hdrs[idx - 1]
        elif h_["fmt"] == 0x40:
            if h_["ref"] not in by_off:
                raise ValueError(f"frame {idx} reference 0x{h_['ref']:x} is not a frame offset")
            h_["refimg"] = by_off[h_["ref"]]
        else:
            h_["refimg"] = None

    def decomp(h_, depth=0):
        if h_["data"] is not None:
            return h_["data"]
        if depth > count:
            raise ValueError("XOR header loop")
        if h_["fmt"] == 0x80:
            h_["data"] = bytearray(lcw_decode(data, w * h, h_["off"]))
        elif h_["fmt"] in (0x20, 0x40):
            base = decomp(h_["refimg"], depth + 1)
            buf = bytearray(base)
            xor_delta_decode(data, buf, h_["off"])
            h_["data"] = buf
        else:
            raise ValueError(f"unknown frame format 0x{h_['fmt']:02x}")
        return h_["data"]

    return w, h, [bytes(decomp(h_)) for h_ in hdrs]


def read_pal(data):
    """768-byte 6-bit VGA palette -> list of 256 (r,g,b) 8-bit tuples."""
    if len(data) != 768:
        raise ValueError(f"expected 768-byte palette, got {len(data)}")
    return [(round(data[i * 3] * 255 / 63),
             round(data[i * 3 + 1] * 255 / 63),
             round(data[i * 3 + 2] * 255 / 63)) for i in range(256)]


def lcw_encode(frame):
    """Minimal, correct LCW (Format80) encoder.

    Emits only two of the five commands the decoder understands:
      * case 4 (0xFE + uint16 count + byte)  -- runs of one value, >= 5 long
      * case 1 (0x80|n + n bytes)            -- literal runs, n in 1..63
    then the 0x80 terminator.

    Both are self-contained, so nothing here can emit a back-reference with a
    bad offset -- the one way a hand-rolled LCW stream corrupts silently.
    Craters are mostly one transparent value, so case 4 does the real work and
    the output lands within a few percent of the engine's own encoder.
    """
    out = bytearray()
    lit = bytearray()

    def flush():
        i = 0
        while i < len(lit):
            n = min(63, len(lit) - i)
            out.append(0x80 | n)
            out.extend(lit[i:i + n])
            i += n
        lit.clear()

    i = 0
    n = len(frame)
    while i < n:
        j = i
        while j < n and frame[j] == frame[i]:
            j += 1
        run = j - i
        if run >= 5:
            flush()
            while run > 0:
                take = min(run, 0x3FFF)
                out.append(0xFE)
                out.extend(struct.pack("<H", take))
                out.append(frame[i])
                run -= take
            i = j
        else:
            lit.extend(frame[i:j])
            i = j

    flush()
    out.append(0x80)
    return bytes(out)


def write_shp(path, width, height, frames):
    """ShpTD with every frame stored as standalone LCW.

    Byte-for-byte the layout ShpTDSprite.Write produces
    (engine/OpenRA.Mods.Cnc/SpriteLoaders/ShpTDLoader.cs:289-322): a 14-byte
    header, one 8-byte ImageHeader per frame plus an EOF and an all-zeroes
    header, then the frame payloads.
    """
    comp = [lcw_encode(bytes(f)) for f in frames]
    data_offset = 14 + (len(comp) + 2) * 8

    out = bytearray()
    out += struct.pack("<HHHHHHH", len(comp), 0, 0, width, height, 0, 0)
    for c in comp:
        out += struct.pack("<IHH", (data_offset & 0xFFFFFF) | (0x80 << 24), 0, 0)
        data_offset += len(c)
    out += struct.pack("<IHH", data_offset & 0xFFFFFF, 0, 0)   # EOF header
    out += struct.pack("<IHH", 0, 0, 0)                        # all-zeroes header
    for c in comp:
        out += c

    with open(path, "wb") as f:
        f.write(out)
    return len(out)
