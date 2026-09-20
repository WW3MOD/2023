#!/usr/bin/env python3
"""nuke-perf -- read one run of demo-nuke-perf and say what the detonation cost.

Two instruments, two files, and they measure DIFFERENT THINGS. Read README.md before
trusting either.

  perf.log          long-tick attributions: which trait or effect exceeded
                    Debug.LongTickThresholdMs, at which tick, for how long.
                    Written only when Debug.EnableSimulationPerfLogging is true.

  <prefix>*.csv     one file per PerfHistory item (tick_time, tick_actors,
                    terrain_lighting, render, ...), one row per tick.
                    Written only when Launch.Benchmark=<prefix> was passed, and
                    only from World.Dispose -- i.e. only on a CLEAN exit.

Neither file is produced by a plain run. Both are overwritten on every launch.

Usage:
    ./analyse.py summary                       # newest run in the OpenRA support dir
    ./analyse.py summary --logs <dir>          # an archived copy
    ./analyse.py compare --before A --after B  # two archived runs, side by side
"""

from __future__ import annotations

import argparse
import csv
import os
import re
import statistics
import sys
from dataclasses import dataclass, field
from pathlib import Path

# ---------------------------------------------------------------- the pinned schedule
#
# These MUST match demo-nuke-perf.lua. They are duplicated rather than re-derived because
# the Lua is also free to not fire at all, and a rig that infers its own window from a run
# that produced no detonation reports the quiet tail as the detonation. analyse.py prefers
# the ticks it reads out of lua.log and falls back to these.
FIRE_TICK = 60
FIRST_IMPACT = 246
LAST_IMPACT_SALVO = 306
END_TICK = 1000

# Ticks of quiet before the order, used as the baseline the detonation is measured against.
BASELINE_WINDOW = (10, 55)

# PerfTimer.cs:27 -- "{0,6:0} ms [{1}] {2}: {3}", optionally followed by " [GC g0/g1/g2]".
# The ms column is an INTEGER format, so anything under 0.5 ms prints as 0 and a threshold
# below 1 produces many rows reading "0 ms". That is the engine's formatting, not a bug in
# this parse, and it is why the threshold guidance in README.md is 1 rather than 0.
#
# The \s* inside the brackets is DEFENSIVE, not observed: {1} is Game.LocalTick with no
# padding format today, so the tick is never space-padded. It is written tolerantly because
# the cost of being wrong is asymmetric -- a regex that stops matching reports a detonation
# that cost nothing, which is indistinguishable from the fix having worked.
LONG_TICK = re.compile(
    r"^\s*(?P<ms>\d+) ms \[\s*(?P<tick>\d+)\s*\] (?P<kind>\w+): (?P<name>.+?)"
    r"(?: \[GC (?P<g0>\d+)/(?P<g1>\d+)/(?P<g2>\d+)\])?$"
)

NUKEPERF = re.compile(r"NUKEPERF (?P<rest>.*)$")


def support_logs_dir() -> Path:
    """Where the engine writes its logs on this platform (Log.cs:128, Platform.SupportDir)."""
    if sys.platform.startswith("win"):
        appdata = os.environ.get("APPDATA")
        if appdata:
            return Path(appdata) / "OpenRA" / "Logs"
        return Path.home() / "Documents" / "OpenRA" / "Logs"
    if sys.platform == "darwin":
        return Path.home() / "Library" / "Application Support" / "OpenRA" / "Logs"
    return Path.home() / ".config" / "openra" / "Logs"


def newest(paths: list[Path]) -> Path | None:
    live = [p for p in paths if p.exists()]
    return max(live, key=lambda p: p.stat().st_mtime) if live else None


def resolve_channel(logs: Path, base: str) -> Path | None:
    """Log.AddChannel falls through to <name>.1, .2 ... when the file is LOCKED by a
    still-running instance (Log.cs:145-160, the IOException catch). So the NEWEST matching
    file, not the bare name, is the one this run wrote. A stale bare name is the trap: it
    reads as a successful run of the wrong game."""
    return newest(sorted(logs.glob(base)) + sorted(logs.glob(base + ".*")))


# ------------------------------------------------------------------------------- models

@dataclass
class Attribution:
    kind: str
    name: str
    hits: int = 0
    total_ms: float = 0.0
    max_ms: float = 0.0
    first_tick: int | None = None
    last_tick: int | None = None
    gc_hits: int = 0


@dataclass
class Run:
    label: str
    logs: Path
    series: dict[str, dict[int, float]] = field(default_factory=dict)
    attributions: dict[tuple[str, str], Attribution] = field(default_factory=dict)
    per_tick_attributed: dict[int, float] = field(default_factory=dict)
    markers: list[str] = field(default_factory=list)
    saw_marker: bool = False
    fired: bool = False
    arm: str = "?"
    order_tick: int | None = None
    first_impact: int | None = None
    last_impact: int | None = None
    end_tick: int | None = None
    threshold_note: str = ""


def load_lua_markers(run: Run) -> None:
    path = resolve_channel(run.logs, "lua.log")
    if path is None:
        return
    for line in path.read_text(encoding="utf-8", errors="replace").splitlines():
        m = NUKEPERF.search(line)
        if not m:
            continue
        rest = m.group("rest").strip()
        run.markers.append(rest)
        run.saw_marker = True
        if rest.startswith("loaded "):
            for tok in rest.split():
                if tok.startswith("arm="):
                    run.arm = tok[4:]
        elif rest.startswith("order "):
            run.fired = True
            for tok in rest.split():
                if tok.startswith("tick="):
                    run.order_tick = int(tok[5:])
                elif tok.startswith("arm="):
                    run.arm = tok[4:]
        elif rest.startswith("expect first_impact"):
            ticks = [int(t[5:]) for t in rest.split() if t.startswith("tick=")]
            if len(ticks) == 2:
                run.first_impact, run.last_impact = ticks
        elif rest.startswith("endtick "):
            # The scenario moves its own end tick to keep a full measurement window
            # after a LATE order, so the window must be read rather than assumed.
            for tok in rest.split():
                if tok.startswith("tick="):
                    run.end_tick = int(tok[5:])
        elif rest.startswith("NOT-FIRED"):
            run.fired = False


def load_perf_log(run: Run) -> None:
    path = resolve_channel(run.logs, "perf.log")
    if path is None:
        run.threshold_note = "no perf.log found at all"
        return
    saw_long_tick = False
    for line in path.read_text(encoding="utf-8", errors="replace").splitlines():
        m = LONG_TICK.match(line)
        if not m:
            continue
        saw_long_tick = True
        ms = float(m.group("ms"))
        tick = int(m.group("tick"))
        key = (m.group("kind"), m.group("name"))
        a = run.attributions.setdefault(key, Attribution(*key))
        a.hits += 1
        a.total_ms += ms
        a.max_ms = max(a.max_ms, ms)
        a.first_tick = tick if a.first_tick is None else min(a.first_tick, tick)
        a.last_tick = tick if a.last_tick is None else max(a.last_tick, tick)
        if m.group("g0") is not None:
            a.gc_hits += 1
        run.per_tick_attributed[tick] = run.per_tick_attributed.get(tick, 0.0) + ms
    if not saw_long_tick:
        run.threshold_note = (
            "perf.log holds load timings only, no long-tick rows -- "
            "Debug.EnableSimulationPerfLogging was NOT set, or nothing ever exceeded "
            "Debug.LongTickThresholdMs"
        )


def load_benchmark(run: Run, prefix: str) -> None:
    for path in sorted(run.logs.glob(prefix + "*.csv")):
        name = path.stem[len(prefix):]
        rows: dict[int, float] = {}
        with path.open(newline="", encoding="utf-8", errors="replace") as fh:
            reader = csv.reader(fh)
            if next(reader, None) is None:
                continue
            for row in reader:
                if len(row) < 2:
                    continue
                try:
                    rows[int(row[0])] = float(row[1])
                except ValueError:
                    continue
        if rows:
            run.series[name] = rows


def load_run(logs: Path, prefix: str, label: str) -> Run:
    run = Run(label=label, logs=logs)
    load_lua_markers(run)
    load_perf_log(run)
    load_benchmark(run, prefix)
    return run


# ----------------------------------------------------------------------------- reporting

def window(series: dict[int, float], lo: int, hi: int) -> list[float]:
    return [v for t, v in series.items() if lo <= t <= hi]


def stats(vals: list[float]) -> tuple[float, float, float, float]:
    if not vals:
        return (0.0, 0.0, 0.0, 0.0)
    ordered = sorted(vals)
    p50 = statistics.median(ordered)
    idx = min(len(ordered) - 1, int(round(0.95 * (len(ordered) - 1))))
    return (p50, ordered[idx], ordered[-1], sum(ordered))


def windows_for(run: Run) -> tuple[tuple[int, int], tuple[int, int]]:
    first = run.first_impact or FIRST_IMPACT
    # END TICK FROM THE RUN, not from the constant: the scenario extends its own run when the
    # order lands late, so a late shot still gets a full measurement window. Assuming the
    # constant would clip that window and under-report the detonation.
    last = run.end_tick or END_TICK
    # The detonation window runs from the first impact to the END OF THE RUN, not to the
    # last impact: the longest thing a warhead starts (ShockwaveEffect, ~400 ticks) outlives
    # the impacts by a wide margin, and clipping at the last impact would measure the flash
    # and miss the sweep that is the point of the exercise.
    return BASELINE_WINDOW, (first, last)


def report(run: Run, top: int) -> None:
    print(f"== {run.label}")
    print(f"   logs: {run.logs}")
    if not run.saw_marker:
        print("   !! no NUKEPERF markers in lua.log -- the scenario script did not run.")
        print("      Check the LuaScript: Scripts: declaration, and run `.\\make.ps1 lua-gate`.")
    for m in run.markers:
        print(f"   lua: {m}")
    if run.saw_marker and not run.fired:
        print("   !! THE SALVO DID NOT FIRE. Every number below is about an empty map.")
    if run.threshold_note:
        print(f"   note: {run.threshold_note}")

    # THE ARM, ON ITS OWN LINE AND NOT ONLY INSIDE THE MARKER DUMP. The two arms of this rig
    # share their schedule by design, so nothing else in this report distinguishes an `exchange`
    # run from a `salvo` one -- same ticks, same warhead count, same window. A compare that
    # silently put two runs of the SAME arm side by side would report a delta of zero and read
    # as "the variant bought nothing", which is the one wrong answer this rig can give quietly.
    print(f"   arm: {run.arm}")

    base, det = windows_for(run)
    print(f"   baseline ticks {base[0]}-{base[1]}    detonation ticks {det[0]}-{det[1]}")
    print()

    if not run.series:
        print("   !! no benchmark CSVs found. Launch.Benchmark=<prefix> was not passed, or the")
        print("      run did not exit cleanly -- the CSVs are written from World.Dispose only,")
        print("      so a watchdog kill produces none.")
    else:
        print("   per-tick series (ms)          "
              "baseline p50/p95/max          detonation p50/p95/max        det total")
        for name in sorted(run.series):
            b = stats(window(run.series[name], *base))
            d = stats(window(run.series[name], *det))
            if b[3] == 0 and d[3] == 0:
                continue
            print(f"   {name:<26} {b[0]:8.2f} {b[1]:7.2f} {b[2]:8.2f}    "
                  f"{d[0]:8.2f} {d[1]:7.2f} {d[2]:8.2f}   {d[3]:10.1f}")
        if "terrain_lighting" not in run.series:
            print()
            print("   note: no terrain_lighting series. EXPECTED under --hidden:")
            print("         PerfHistory.Sampling is assigned in RenderTick (Game.cs:879) and")
            print("         --hidden suspends rendering (Sdl2PlatformWindow.cs:372-373), so")
            print("         TintAt takes its unsampled path. tick_time is still valid there --")
            print("         and is in fact the HONEST figure, because the sampling costs ~15x")
            print("         what it measures on that path (TerrainLighting.cs:269-279).")
    print()

    if not run.attributions:
        print("   !! no long-tick attributions. Pass Debug.EnableSimulationPerfLogging=true and")
        print("      Debug.LongTickThresholdMs=<n>.")
        return

    print(f"   long-tick attributions, top {top} by total ms "
          f"(only ticks over the threshold appear at all)")
    print("   kind     name                                hits   total ms    max ms   "
          "ticks            GC")
    for a in sorted(run.attributions.values(), key=lambda x: -x.total_ms)[:top]:
        span = f"{a.first_tick}-{a.last_tick}"
        print(f"   {a.kind:<8} {a.name:<34} {a.hits:5d} {a.total_ms:10.0f} "
              f"{a.max_ms:9.0f}   {span:<16} {a.gc_hits:4d}")

    att_det = [v for t, v in run.per_tick_attributed.items() if det[0] <= t <= det[1]]
    att_base = [v for t, v in run.per_tick_attributed.items() if base[0] <= t <= base[1]]
    print()
    print(f"   attributed ms summed per tick: baseline {sum(att_base):.0f} ms over "
          f"{len(att_base)} ticks, detonation {sum(att_det):.0f} ms over {len(att_det)} ticks")
    print("   (a LOWER BOUND: anything under the threshold is invisible to it)")


def compare(before: Run, after: Run, top: int) -> None:
    print("== compare")
    print(f"   before: {before.label}   arm={before.arm}")
    print(f"   after:  {after.label}   arm={after.arm}")
    for run, tag in ((before, "before"), (after, "after")):
        if run.saw_marker and not run.fired:
            print(f"   !! {tag} DID NOT FIRE -- the comparison is meaningless.")

    # SAID OUT LOUD BECAUSE THE TWO MISREADINGS ARE OPPOSITE AND BOTH SILENT. Comparing two runs
    # of the SAME arm across a code change is the ordinary before/after and is correct. Comparing
    # `salvo` against `exchange` is the PAYLOAD A/B and is also correct. What is never a result is
    # a pair where one side's arm is unknown, or where an arm difference is read as a code
    # difference -- the arms share a tick schedule, so nothing else in this report would show it.
    if before.arm != after.arm:
        print(f"   note: ARMS DIFFER ({before.arm} -> {after.arm}). This measures the PAYLOAD, "
              "not a code change.")
    if "?" in (before.arm, after.arm):
        print("   !! at least one side has no arm marker -- its lua.log is missing or predates "
              "the arm being recorded. Do not read the delta as either kind of comparison.")
    print()
    _, det = windows_for(after)

    names = sorted(set(before.series) | set(after.series))
    if names:
        print(f"   per-tick series, detonation window ticks {det[0]}-{det[1]} (ms)")
        print("   series                       before p50 /     max     "
              "after p50 /     max       delta p50")
        for name in names:
            b = stats(window(before.series.get(name, {}), *det))
            a = stats(window(after.series.get(name, {}), *det))
            if b[3] == 0 and a[3] == 0:
                continue
            delta = a[0] - b[0]
            pct = (100.0 * delta / b[0]) if b[0] else float("nan")
            print(f"   {name:<26} {b[0]:10.2f} {b[2]:9.2f}  "
                  f"{a[0]:10.2f} {a[2]:9.2f}  {delta:+9.2f} {pct:+8.1f}%")
        print()

    keys = set(before.attributions) | set(after.attributions)
    if keys:
        print("   long-tick attributions, total ms over the whole run")
        print("   kind     name                                 before      after      delta")
        rows = []
        for k in keys:
            b = before.attributions.get(k)
            a = after.attributions.get(k)
            rows.append((k, b.total_ms if b else 0.0, a.total_ms if a else 0.0))
        rows.sort(key=lambda r: -max(r[1], r[2]))
        for (kind, name), bt, at in rows[:top]:
            print(f"   {kind:<8} {name:<34} {bt:10.0f} {at:10.0f} {at - bt:+10.0f}")


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(
        description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="cmd", required=True)

    def common(p):
        p.add_argument("--prefix", default="nukeperf-",
                       help="Launch.Benchmark prefix used for the run (default: nukeperf-)")
        p.add_argument("--top", type=int, default=20)

    s = sub.add_parser("summary", help="report one run")
    s.add_argument("--logs", type=Path, default=None,
                   help="log directory (default: the OpenRA support dir for this platform)")
    common(s)

    c = sub.add_parser("compare", help="report two archived runs side by side")
    c.add_argument("--before", type=Path, required=True)
    c.add_argument("--after", type=Path, required=True)
    common(c)

    args = ap.parse_args(argv)

    if args.cmd == "summary":
        logs = args.logs or support_logs_dir()
        if not logs.is_dir():
            print(f"no such log directory: {logs}", file=sys.stderr)
            return 3
        report(load_run(logs, args.prefix, str(logs)), args.top)
        return 0

    for p in (args.before, args.after):
        if not p.is_dir():
            print(f"no such log directory: {p}", file=sys.stderr)
            return 3
    compare(load_run(args.before, args.prefix, str(args.before)),
            load_run(args.after, args.prefix, str(args.after)),
            args.top)
    return 0


if __name__ == "__main__":
    sys.exit(main())
