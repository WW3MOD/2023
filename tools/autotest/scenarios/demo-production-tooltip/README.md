# demo-production-tooltip — capture request

Staged by `wt/tooltip-legibility` against `main @ d69e6883`. **The worker did not run this.**
Captures are serialised through the manager; this file is the request.

## Run lines

```bash
./tools/autotest/run-demo.sh demo-production-tooltip --hidden --size 1600x900
./tools/autotest/run-demo.sh demo-production-tooltip --hidden --size 1280x720
```

Two resolutions because the defect is a **relationship between two widgets**, not a property of
one. `TooltipContainerWidget.GetAnchoredPosition` (`:154`) places the panel at
`anchor.X - tooltipWidth - AnchorGap` and clamps only vertically, while the panel's width is
fixed (`ProductionTooltipLayout`). How much of the sidebar it covers therefore changes with the
window, and 1280x720 is where it covers most.

There is **no `result.json`** — this is a demo, it reaches no verdict by design (`DEMO.md`).
The frames are the output; find them under the run directory's `screenshots/`, and read the
`note` recorded with each.

## Frames

Three, one per subject, in this order. Every one must show the tooltip **open and overlapping
the sidebar** — the fix does not move the panel, and a frame where it has moved clear of the
sidebar is not photographing the reported defect at all.

| # | Label | Subject | What must be visible |
|---|---|---|---|
| 1 | `01-abrams` | `abrams` — one pool, every actor-wide stat | Panel interior **solid**: no sidebar cameo, portrait or row frame legible through it anywhere behind the text. Heading `TANK ROUND ABRAMS` (word-split, **not** `TankRound.Abrams`). Rows: `AMMO 40 rounds`, a `REFILL` rate, `FULL REFILL` total in amber. |
| 2 | `02-rifleman-two-pools` | `E3` — **the original 2026-08-30 repro** | Two weapon sections, `5.56MM DMR` and `RPG`, each with its own `AMMO`/`REFILL`; a visibly wider gap where the weapons band ends and `ARMOUR`/`HEALTH`/`SPEED` begin; one `FULL REFILL`. The tallest infantry panel — interior solid for its **whole height**, including the bottom rows furthest from the anchor. |
| 3 | `03-conscript-single-pool` | `E1` — one pool, shortest panel | One section `5.56MM E3`, `AMMO 100 rounds`, `REFILL`, **and a `FULL REFILL` total even though this unit has only one pool** (that row was gated on having two until `0d7663ab`). Interior solid. |

### What makes a frame a NO-RESULT

Not a failure of the fix — a failure of the capture, and the run should be repeated rather than
reported on.

- **Sidebar empty / no production icons.** The queues are only `Enabled` while the player owns a
  producer, which in WW3MOD is the Supply Route and nothing else. `OwnSR` in `map.yaml` is what
  supplies that; without it every frame is a bare sidebar and no tooltip.
- **No panel drawn at all, but text present.** That is the missing-collection signature, not a
  transparency problem: a `Background:` naming a collection the mod does not declare draws
  **nothing**, silently (`ChromeProvider.TryGetPanelImages` returns null, `WidgetUtils.cs:93-95`
  skips). Check `tooltip-panel` is declared in `mods/ww3mod/chrome.yaml`.
- **No tooltip at all.** The hover was not armed. `lua.log` carries one line per subject —
  `demo-production-tooltip: hover <type> armed=true|false`. A `false` means no enabled queue
  offered that type.
- **A blank/black frame.** Told by **file size, not by the image**: ~59 KB is a black frame, a
  real one is megabytes (`SCREENSHOT.md`). Re-shoot before concluding anything.
- **The tooltip does not overlap the sidebar.** Then the frame cannot answer the question in
  either direction.

### What these frames deliberately do NOT settle

The **6px frame ring** around the panel is still translucent (alpha 159 black, with the bevel
highlight on its outer row) and is meant to be — replacing it would flatten the bevel every other
panel in the mod has. Only the interior was made opaque. A reviewer who sees a faint edge band is
seeing the intended result, not a partial fix.

Nor do they say anything about **fog or ally-only decorations**: an autotest capture runs with
`world.RenderPlayer = null` (`TestModeLogic.cs:31`), so the map behind the panel is drawn without
fog and with every `ValidRelationships` gate off. That affects what is *behind* the tooltip, never
whether the tooltip transmits it — but do not read anything else off these frames.
