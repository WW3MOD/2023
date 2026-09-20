#!/usr/bin/env python3
"""Self-test for analyse.py.

The point of this file is narrow and worth stating: analyse.py's whole job is to read two
log formats it cannot produce itself, and a parser that silently matches nothing looks
EXACTLY like a run in which the detonation was cheap. Every launch slot this rig consumes
is expensive and serial, so the parse is proven here, against synthetic logs written in
the engine's exact format, before anyone spends a run on it.

The long-tick format is transcribed from PerfTimer.cs:27
    static readonly string FormatStringLongTick = "{0,6:0} ms {1}[{2}]: {3}" ...
    -> "{ms,6:0} ms [{LocalTick}] {name}: {label}"  (+ " [GC g0/g1/g2]")
and the benchmark CSV header from Benchmark.cs Write() -- "tick,time [ms]".

    ./selftest.py          # prints ok/FAIL per case, exit 1 on any failure
"""

from __future__ import annotations

import shutil
import sys
import tempfile
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import analyse  # noqa: E402

FAILURES: list[str] = []


def check(name: str, cond: bool, detail: str = "") -> None:
    if cond:
        print(f"  ok    {name}")
    else:
        print(f"  FAIL  {name}  {detail}")
        FAILURES.append(name)


def write_run(root: Path, *, fired: bool = True, benchmark: bool = True,
              long_ticks: bool = True, prefix: str = "nukeperf-") -> Path:
    root.mkdir(parents=True, exist_ok=True)

    lua = [
        "NUKEPERF loaded arm=salvo firetick=60 endtick=1000",
    ]
    if fired:
        lua += [
            "NUKEPERF order tick=60 arm=salvo warheads=6 groundzero=64,64",
            "NUKEPERF expect first_impact tick=246 last_impact tick=306",
            "NUKEPERF endtick tick=1000",
        ]
    else:
        lua += ["NUKEPERF NOT-FIRED status=refused magazine=refused -- every number "
                "from this run is a number about an empty map"]
    lua += ["NUKEPERF end tick=1000 fired=" + ("true" if fired else "false")]
    (root / "lua.log").write_text("\n".join(lua) + "\n", encoding="utf-8")

    perf = [
        "          SpriteFonts",
        "     2 ms |   Precache common|FreeSans.ttf 10px",
        "    63 ms --------------------------------------------------",
    ]
    if long_ticks:
        perf += [
            "    41 ms [250] Trait: LightEventManager",
            "    38 ms [251] Trait: LightEventManager [GC 1/0/0]",
            "    12 ms [252] Effect: ShockwaveEffect",
            "     7 ms [252] Effect: ThermalRadiationEffect",
            "    44 ms [300] Trait: LightEventManager",
            "     3 ms [ 20] Effect: ShockwaveEffect",
        ]
    (root / "perf.log").write_text("\n".join(perf) + "\n", encoding="utf-8")

    if benchmark:
        def csv(name: str, rows: list[tuple[int, float]]) -> None:
            body = "tick,time [ms]\n" + "\n".join(f"{t},{v}" for t, v in rows) + "\n"
            (root / f"{prefix}{name}.csv").write_text(body, encoding="utf-8")

        # 10..55 quiet at ~5 ms; 246..1000 loaded at ~60 ms with one 200 ms spike.
        quiet = [(t, 5.0) for t in range(1, 60)]
        loud = [(t, 60.0) for t in range(246, 1001)]
        loud[4] = (250, 200.0)
        csv("tick_time", quiet + loud)
        csv("tick_actors", [(t, 1.0) for t in range(1, 1001)])
        csv("terrain_lighting", [(t, 0.0) for t in range(1, 246)]
            + [(t, 30.0) for t in range(246, 1001)])
    return root


def main() -> int:
    tmp = Path(tempfile.mkdtemp(prefix="nukeperf-selftest-"))
    try:
        # ---- the happy path parses ------------------------------------------------
        good = write_run(tmp / "good")
        run = analyse.load_run(good, "nukeperf-", "good")

        check("lua markers are picked up", run.saw_marker and run.fired)
        check("order tick is read from lua.log", run.order_tick == 60,
              f"got {run.order_tick}")
        check("impact ticks are read from lua.log",
              (run.first_impact, run.last_impact) == (246, 306),
              f"got {(run.first_impact, run.last_impact)}")

        key = ("Trait", "LightEventManager")
        check("long-tick rows parse", key in run.attributions,
              f"keys={sorted(run.attributions)}")
        if key in run.attributions:
            a = run.attributions[key]
            check("long-tick totals add up", a.hits == 3 and a.total_ms == 123.0,
                  f"hits={a.hits} total={a.total_ms}")
            check("the GC marker suffix does not break the name match", a.gc_hits == 1,
                  f"gc_hits={a.gc_hits}")
            check("tick span is tracked", (a.first_tick, a.last_tick) == (250, 300),
                  f"got {(a.first_tick, a.last_tick)}")

        # Defensive, not observed: PerfTimer.cs:27 formats the tick with no padding, so
        # "[ 20]" cannot occur today. Pinned anyway because the cost of the regex quietly
        # ceasing to match is a detonation that reports as free.
        check("a space-padded tick number still parses",
              ("Effect", "ShockwaveEffect") in run.attributions
              and run.attributions[("Effect", "ShockwaveEffect")].hits == 2,
              "the '[ 20]' row")

        check("load-time PerfTimer rows are NOT mistaken for long ticks",
              all(k[0] in ("Trait", "Effect") for k in run.attributions),
              f"keys={sorted(run.attributions)}")

        check("benchmark CSVs load", set(run.series) ==
              {"tick_time", "tick_actors", "terrain_lighting"},
              f"got {sorted(run.series)}")

        base, det = analyse.windows_for(run)
        check("detonation window starts at the reported first impact", det[0] == 246,
              f"got {det}")
        check("detonation window runs to the end of the run, not the last impact",
              det[1] == analyse.END_TICK, f"got {det}")
        check("the end tick is read from lua.log", run.end_tick == 1000,
              f"got {run.end_tick}")

        # A LATE order must not be windowed against the default end tick.
        late = tmp / "late"
        write_run(late)
        lua = (late / "lua.log").read_text(encoding="utf-8")
        lua = lua.replace("tick=246 last_impact tick=306", "tick=500 last_impact tick=560")
        lua = lua.replace("endtick tick=1000", "endtick tick=1254")
        (late / "lua.log").write_text(lua, encoding="utf-8")
        run_late = analyse.load_run(late, "nukeperf-", "late")
        _, det_late = analyse.windows_for(run_late)
        check("a late order widens the window instead of being clipped",
              det_late == (500, 1254), f"got {det_late}")

        b = analyse.stats(analyse.window(run.series["tick_time"], *base))
        d = analyse.stats(analyse.window(run.series["tick_time"], *det))
        check("baseline p50 is the quiet value", b[0] == 5.0, f"got {b[0]}")
        check("detonation p50 is the loaded value", d[0] == 60.0, f"got {d[0]}")
        check("detonation max picks up the spike", d[2] == 200.0, f"got {d[2]}")

        # ---- the failure modes are DIAGNOSED, not silently reported as cheap ------
        empty = write_run(tmp / "nofire", fired=False)
        run2 = analyse.load_run(empty, "nukeperf-", "nofire")
        check("a run that never fired is flagged", run2.saw_marker and not run2.fired)

        bare = write_run(tmp / "bare", benchmark=False, long_ticks=False)
        run3 = analyse.load_run(bare, "nukeperf-", "bare")
        check("missing benchmark CSVs leave series empty", run3.series == {})
        check("a perf.log with no long ticks is called out",
              "long timings only" in run3.threshold_note
              or "no long-tick rows" in run3.threshold_note,
              f"note={run3.threshold_note!r}")

        # ---- the locked-file fallthrough is followed ------------------------------
        # Log.AddChannel writes perf.log.1 when perf.log is held open by another instance.
        rot = tmp / "rotated"
        write_run(rot)
        shutil.move(str(rot / "perf.log"), str(rot / "perf.log.1"))
        (rot / "perf.log").write_text("          stale header only\n", encoding="utf-8")
        # Make the rotated file unambiguously newer than the stale bare name.
        import os
        import time
        now = time.time()
        os.utime(rot / "perf.log", (now - 100, now - 100))
        os.utime(rot / "perf.log.1", (now, now))
        run4 = analyse.load_run(rot, "nukeperf-", "rotated")
        check("the NEWEST perf.log* is read, not the stale bare name",
              ("Trait", "LightEventManager") in run4.attributions,
              f"keys={sorted(run4.attributions)}")

        # ---- the two report paths at least run over real data --------------------
        analyse.report(run, top=5)
        analyse.compare(run2, run, top=5)
        check("report() and compare() run without raising", True)
    finally:
        shutil.rmtree(tmp, ignore_errors=True)

    print()
    if FAILURES:
        print(f"FAILED: {len(FAILURES)} -- {', '.join(FAILURES)}")
        return 1
    print("OK -- analyse.py parses the engine's formats")
    return 0


if __name__ == "__main__":
    sys.exit(main())
