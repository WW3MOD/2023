# tooltip-census — what every production tooltip's weapon heading actually says

```bash
python3 tools/tooltip-census/census.py --selftest   # run this first, always
python3 tools/tooltip-census/census.py
```

No build, no launch, ~1s.

## What it answers

`AmmoPoolInfo.ProvideTooltipDescription` heads each weapon section with either the pool's
authored `TooltipName:` or, failing that, a heading derived from the **weapon key** by
`FormatWeaponLabel`. So the headings a player reads are a property of the ruleset's identifiers,
and nothing in the tree prints them. This does: it resolves the actor rules the way the engine
does and runs the same algorithm.

Written 2026-09-21 to size the follow-up naming work for audit item I3 — which turned out to be
already closed (`5965d955`); no raw identifier reaches the screen. What remains is a *quality*
question about ~20 derived headings, which is what the second section of the output is for.

## Read the selftest line before you read anything else

The label algorithm is a Python port of the C# and is only worth what its agreement with the C#
is worth. `--selftest` replays **all 17 `FormatWeaponLabel` expectations** from
`engine/OpenRA.Test/OpenRA.Mods.Common/AmmoPoolTest.cs`. If it does not print
`selftest OK - 17/17`, the census below it is noise. If someone changes `FormatWeaponLabel` in
C#, this is what tells you the port has drifted — it will not notice on its own.

## The trap it already fell into

MiniYaml `Inherits:` **merges** a trait's child nodes key-by-key; it does not replace the trait.
An earlier version of this script replaced, and so reported a confident, plausible and entirely
fictional defect: `A10.Airstrike` drawing two identical `30MM A10 + HELLFIRE` subheads over
different ammo counts. The actor overrides one field (`Ammo:`) of an inherited `AmmoPool@1`, and
replacing erased the parent's `Name:` and `Armaments:` along with it.

The general form is worth carrying: **a bespoke YAML reader that gets inheritance wrong
manufactures defects in exactly the actors that override a single field** — which are the
interesting ones, which is why the output looks like a finding. Recorded in
`WORKSPACE/DISCOVERIES.md` 2026-09-21.

## What it does not do

- It does not know about map-level rules overlays or `scenarios.yaml`; it reads `mod.yaml`'s
  `Rules:` list only.
- It reports `Buildable` actors only — a pool on something you cannot order is not on a
  production tooltip.
- It says nothing about whether a heading is *good*. That is the reading you do with the output.
