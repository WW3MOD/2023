# smudge-gate

Static guard that a nuclear scar can actually land where a player will look. No build, no
game launch, about a second. Runs as part of `.\make.ps1 test`, beside `nav-guard` and
`lua-gate`.

```
python tools/smudge-gate/smudge_gate.py check      # 0 clean, 2 on a finding
python tools/smudge-gate/smudge_gate.py selftest   # proves the detectors still fire
```

## The failure class it exists for

`TerrainTypeInfo.AcceptsSmudgeType` (`engine/OpenRA.Game/Map/TerrainInfo.cs:63`) is a
`HashSet<string>` that **defaults to empty, and empty means "accepts nothing"**.
`LeaveSmudgeWarhead.cs:73` asks each cell's terrain for the first accepted type matching the
warhead and silently drops the cell when there is none.

So the acceptance list is an **opt-in allowlist, repeated per terrain type per tileset**, and
a terrain type that simply never mentions a smudge type rejects it. There is no error, no
warning, and nothing in any log — the scar just is not there.

That shape has a bad interaction with adding smudge types. The nuclear scar work introduced
five at once (`ScarCore`, `ScarCrater`, `ScarChar`, `ScarBurn`, `ScarRim`), each of which had
to be added to every terrain type in every tileset by hand. It was missed twice, and both
times the symptom was only ever visible in a screenshot:

- **Beaches**, fixed 2026-09-09 morning. A blast disc stopped a full cell short of the water,
  leaving a bright sand band between the black scar and the shoreline.
- **Rock and Cliffs**, fixed 2026-09-09 evening. Every boulder and cliff line sat in an
  unburnt halo while the ground around it charred. The user's words were "some kind of
  protective aura, which doesn't make sense". On the high-yield demo map that was 2417 of
  16384 cells; across all shipped maps and scenarios, `TEMPERAT/Rock` alone is 17,891 cells.

Neither was a code defect and neither could fail a build. Both were data omissions in a list
whose default is silence.

## What it checks

1. **No partial families.** A terrain type accepting some of the scar family but not all of
   it. That is almost always a forgotten edit — it is exactly the state the tree passes
   through while somebody fixes one tileset at a time, which is when it is most likely to be
   committed by accident.
2. **No silent holes in terrain that is actually used.** Every terrain type appearing on a
   shipped map or an autotest scenario must either accept the full scar family or be named in
   `DELIBERATELY_UNSCARRED` with a reason. Note the direction — a **new** terrain type fails
   until somebody makes a decision about it. That is the point, not a nuisance.

The scar family is **derived from `world.yaml`'s `SmudgeLayer` declarations**, not hard-coded
here. A sixth scar band added tomorrow is covered by check 1 the moment it is declared, with
no edit to this tool.

## What is deliberately unscarred

`Water`, `River`, `RiverShallow`. A smudge is an opaque full-cell ground decal and there is no
sub-cell land/water information anywhere in the engine, which is precisely why `SmudgeLayer`
carries `ShoreFadeCells` — the blast ramps down as it approaches these rather than drawing
onto them. Adding a fourth entry to that dict is a design decision and should read like one:
the value is the reason, and it is there to be quoted back at whoever added it.

`Tree` and `Field` exist as terrain types in the tilesets but appear on **no** shipped map or
scenario — they are Red Alert leftovers, and the crop fields a player sees are actors
(`v17`, `v16`, `rice`) standing on ordinary `Clear`. The gate only inspects terrain in use, so
it says nothing about them; if a map ever paints one, the gate will start demanding a decision.

## The selftest, and why it is not decoration

`selftest` fails if `TEMPERAT/Rock` accepts no scar type — i.e. if the tree has regressed to
the pre-2026-09-09 state — and it also asserts that `Clear` is not exempt, since exempting it
would quietly disable the hole detector for the most common terrain in the game. A gate nobody
has watched go red is not known to work; both detectors were confirmed firing on injected
faults before this was wired into `make.ps1`.
