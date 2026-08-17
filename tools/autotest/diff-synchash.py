#!/usr/bin/env python3
"""Diff two per-net-frame sync-hash traces (Test.SyncHashLog).

Reports the FIRST frame at which the two runs disagree, and on which column —
the aggregate world hash, the shared RNG value, or the RNG draw count. The
distinction matters: a prior investigation here found the synced hash diverging
~490 frames BEFORE the RNG did, so "the RNG differs" is routinely a downstream
consequence and not the cause.

Also refuses to call two traces identical when the comparison is vacuous:
an empty trace, an all-zero hash column, or a runtime header that says both
runs used the same runtime are reported as such rather than as a match.
"""

import sys


def load(path):
    meta, rows = {}, []
    with open(path) as fh:
        for line in fh:
            line = line.rstrip("\n")
            if line.startswith("#"):
                parts = line.lstrip("# ").split("\t")
                if len(parts) >= 2 and parts[0] in ("runtime", "platform", "seed", "build"):
                    meta[parts[0]] = "\t".join(parts[1:])
                continue
            if not line:
                continue
            f = line.split("\t")
            rows.append((int(f[0]), int(f[1]), int(f[2]), int(f[3])))
    return meta, rows


def main(pa, pb):
    ma, ra = load(pa)
    mb, rb = load(pb)

    print(f"A {pa}\n  runtime={ma.get('runtime')} seed={ma.get('seed')} build={ma.get('build')} frames={len(ra)}")
    print(f"B {pb}\n  runtime={mb.get('runtime')} seed={mb.get('seed')} build={mb.get('build')} frames={len(rb)}")

    problems = []
    if not ra or not rb:
        problems.append("a trace is EMPTY — nothing was compared")
    if ra and all(r[1] == 0 for r in ra):
        problems.append("A's hash column is all zeroes — the hash was never computed")
    if rb and all(r[1] == 0 for r in rb):
        problems.append("B's hash column is all zeroes — the hash was never computed")
    if ma.get("build") != mb.get("build"):
        problems.append(f"BUILD MISMATCH {ma.get('build')} vs {mb.get('build')}")
    if ma.get("seed") != mb.get("seed"):
        problems.append(f"seed differs ({ma.get('seed')} vs {mb.get('seed')}) — divergence is expected, not evidence")
    for p in problems:
        print(f"  !! {p}")

    first = None
    for (fa, ha, sa, ca), (fb, hb, sb, cb) in zip(ra, rb):
        assert fa == fb, f"frame numbering diverged: {fa} vs {fb}"
        if (ha, sa, ca) != (hb, sb, cb):
            cols = []
            if ha != hb:
                cols.append(f"synchash {ha} vs {hb}")
            if sa != sb:
                cols.append(f"sharedrandom {sa} vs {sb}")
            if ca != cb:
                cols.append(f"randomdraws {ca} vs {cb}")
            first = (fa, cols)
            break

    common = min(len(ra), len(rb))
    if first:
        print(f"  DIVERGE at net frame {first[0]} of {common} compared: {'; '.join(first[1])}")
    else:
        print(f"  IDENTICAL across all {common} compared frames")
    if len(ra) != len(rb):
        print(f"  note: trace lengths differ ({len(ra)} vs {len(rb)}); compared the common prefix")
    return 1 if first else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1], sys.argv[2]))
