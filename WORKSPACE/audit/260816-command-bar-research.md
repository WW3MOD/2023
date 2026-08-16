# In-game chrome research — build categories, command-bar overflow, icon uniqueness

**Date:** 2026-08-16
**Ref:** `main @ 81e5a440` (working tree clean except pre-existing untracked audit docs)
**Status:** RESEARCH ONLY — nothing implemented, nothing committed. Every change below is a proposal awaiting goahead.
**Method:** static read of `mods/ww3mod/chrome/ingame-player.yaml`, `mods/ww3mod/chrome.yaml`, `mods/ww3mod/rules/player.yaml`, `mods/ww3mod/rules/ingame/structures*.yaml`, `mods/ww3mod/hotkeys.yaml`, `engine/OpenRA.Game/Graphics/ChromeProvider.cs`. **The game was not launched.**

---

## 1. Build menu categories — Buildings and Defence

### What exists

Five production tabs are declared in `mods/ww3mod/chrome/ingame-player.yaml` under `Container@PRODUCTION_TYPES` (lines 1338–1448), plus one already commented out:

| Tab widget | Line | Y | `ProductionGroup` | `Key` | Status |
|---|---|---|---|---|---|
| `@INFANTRY` | 1344 | 0 | `Infantry` | `ProductionTypeInfantry` | live |
| `@VEHICLE` | 1361 | 31 | `Vehicle` | `ProductionTypeVehicle` | live |
| `@AIRCRAFT` | 1379 | 62 | `Aircraft` | `ProductionTypeAircraft` | live |
| `@NAVAL` | 1397 | (93) | `Ship` | `ProductionTypeNaval` | **already commented out** |
| `@BUILDING` | 1413 | 93 | `Building` | `ProductionTypeBuilding` | live, permanently empty |
| `@DEFENSE` | 1431 | 124 | `Defense` | `ProductionTypeDefense` | live, permanently empty |

### Are they genuinely dead?

**Yes — verified, not assumed.** The queues themselves exist (`rules/player.yaml:18` `ClassicProductionQueue@Building`, `:30` `ClassicProductionQueue@Defense`), and actors *are* assigned to them. But **every single actor assigned to either queue is gated behind the prerequisite `~disabled`**, and **nothing in the mod provides a prerequisite named `disabled`**:

- `Queue: Building` — 4 live entries (`rules/ingame/structures.yaml:245, 365, 430, 498` — supply route, service depot, helipad, airfield). All four carry `Prerequisites: ~disabled`.
- `Queue: Defense` — 17 live entries in `rules/ingame/structures-defenses.yaml` (lines 91, 189, 276, 345, 374, 403, 466, 551, 583, 611, 696, 781, 823, 897, 1094, 1163) plus commented-out ones. **All** carry `Prerequisites: ~disabled`.
- `grep -rn "ProvidesPrerequisite.*disabled\|Prerequisites: disabled" mods/ww3mod/rules/` → **zero hits.** No actor, no upgrade, no map grants it.

So both tabs render permanently greyed for every player on every map, in every faction. This matches what you saw.

**The traps I checked and cleared:**

- **Pre-placed / capturable neutral structures.** These are placed by the map or spawned by rules; they do not need a `Buildable`/`Queue` entry to exist, and removing a *sidebar tab* does not touch actor definitions at all. `logisticscenter` capture, garrisonable buildings, and any map-placed structure are entirely unaffected — the tab is a filter over a production queue, not a registry of structures.
- **Nothing gets orphaned by removing the widgets.** A `ProductionTypeButton` is pure chrome; the queue it points at is defined independently in `player.yaml`.
- **The AI does reference the queues by name.** `rules/ai/ai.yaml:1623-1624` sets `BuildingQueues: Building` / `DefenseQueues: Defense`. This is why the recommendation below **removes only the two chrome widgets and leaves `player.yaml` untouched** — deleting the queues would strand those AI fields. The queues stay; they simply have no tab and no contents, exactly as they have no contents today.
- **Observer chrome is clean.** `chrome/ingame-observer.yaml` contains no `ProductionTypeButton` at all — no parallel edit needed.

### Knock-on: hotkeys

`mods/ww3mod/hotkeys.yaml:1-29` declares six production-tab hotkeys, **all six unbound** (the intended keys survive only as trailing comments):

| Hotkey | Line | Commented intent | Button state |
|---|---|---|---|
| `ProductionTypeBuilding` | 1 | `# E` | to be removed |
| `ProductionTypeDefense` | 6 | `# R` | to be removed |
| `ProductionTypeInfantry` | 11 | `# T` | live |
| `ProductionTypeVehicle` | 16 | `# Y` | live |
| `ProductionTypeAircraft` | 21 | `# U` | live |
| `ProductionTypeNaval` | 26 | `# I` | **already orphaned** (button commented out since before this work) |

Removing Building and Defense leaves three live tabs whose intended keys would be T / Y / U — an awkward, off-centre run that only makes sense as the tail of a six-key row that no longer exists. For a public release where a stranger reads the hotkey list cold, the three surviving tabs should get a contiguous, memorable block.

### RECOMMENDATION 1

Do all four, as one change:

1. **Delete** `ProductionTypeButton@BUILDING` (lines 1413–1430) and `ProductionTypeButton@DEFENSE` (lines 1431–1448) from `chrome/ingame-player.yaml`. Infantry / Vehicle / Aircraft keep their existing `Y: 0 / 31 / 62`; no repositioning needed. `Container@PRODUCTION_TYPES` `Height: 240` can stay as-is (it is a bounding box, not a drawn panel) or tighten to 90 — cosmetically identical, so leave it alone unless you want the tidier number.
2. **Leave `rules/player.yaml` alone.** The `Building` and `Defense` queues stay defined, because `rules/ai/ai.yaml:1623-1624` references them by name.
3. **Delete** the now-dead `ProductionTypeBuilding`, `ProductionTypeDefense` and (while we are here) the long-orphaned `ProductionTypeNaval` entries from `hotkeys.yaml`.
4. **Bind the three survivors** to a contiguous block: `Infantry: Q`, `Vehicle: W`, `Aircraft: E`. *(Needs a conflict check against existing Q/W/E bindings before implementing — flagged, not verified.)* If Q/W/E collide, `E / R / T` is the fallback and reuses two keys the file already wanted.

Risk: very low. Chrome-only plus hotkey declarations; no rules, no C#, no art. Verifiable with `make test` (YAML validation) and one look at the sidebar.

---

## 2. Command bar overflow — the arithmetic

### Layout model

The bottom bar is **six independently positioned `Background@*` panels** (the drawn chrome) with **five independently positioned button containers** (`Container@*`) floating on top of them. The two sets are *not* parented to each other — each carries its own absolute `X`. That decoupling is the whole bug: the button containers were shifted right to make room for `EVACUATE`, and **the panels behind them were never moved or resized.**

`CMD_BG_A` establishes the intended convention: panel `X: 5, Width: 290` = 5..295; its buttons run 14..286; inset **9 px left, 9 px right**. This matches `commandbar-background`'s `PanelRegion: 0, 0, 9, 9, 416, 26, 9, 9` (`chrome.yaml:155`) — a 9 px corner. Every panel is *supposed* to be `9 + content + 9`.

### Full button inventory

All 25 buttons of the bottom bar. `X abs` = container `X` + button `X`. Every button is `34 × 26`.

| # | Container (`X`, `Width`) | Button | `X` rel | **X abs** | ends | Panel behind | Icon collection | Icon name |
|---|---|---|---|---|---|---|---|---|
| 1 | `COMMAND_BAR` (14, 460) | `@ATTACK_MOVE` | 0 | 14 | 48 | `CMD_BG_A` | `command-icons` | `attack-move` |
| 2 | ″ | `@FORCE_MOVE` | 34 | 48 | 82 | `CMD_BG_A` | `command-icons` | `force-move` |
| 3 | ″ | `@FORCE_ATTACK` | 68 | 82 | 116 | `CMD_BG_A` | `command-icons` | `force-attack` |
| 4 | ″ | `@GUARD` | 102 | 116 | 150 | `CMD_BG_A` | `command-icons` | `guard` |
| 5 | ″ | `@DEPLOY` | 136 | 150 | 184 | `CMD_BG_A` | `command-icons` | `deploy` |
| 6 | ″ | `@SCATTER` | 170 | 184 | 218 | `CMD_BG_A` | `command-icons` | `scatter` |
| 7 | ″ | `@STOP` | 204 | 218 | 252 | `CMD_BG_A` | `command-icons` | `stop` |
| 8 | ″ | `@QUEUE_ORDERS` | 238 | 252 | 286 | `CMD_BG_A` | `command-icons` | `queue-orders` |
| 9 | ″ | `@RESUPPLY` | 290 | 304 | 338 | `CMD_BG_B` | `command-icons` | `resupply` |
| 10 | ″ | `@PATROL` | 324 | 338 | 372 | `CMD_BG_B` | `command-icons` | `guard` |
| 11 | ″ | `@TAKE_COVER` | 358 | 372 | 406 | `CMD_BG_B` | `command-icons` | `deploy` |
| 12 | ″ | `@AUTO_ENTER` | 392 | 406 | 440 | `CMD_BG_B` | `command-icons` | `guard` |
| 13 | ″ | **`@EVACUATE`** | 426 | 440 | **474** | `CMD_BG_B` (**overflows**) | `stance-icons` | `defend` |
| 14 | `STANCE_BAR` (492, 102) | `@STANCE_FIREATWILL` | 0 | 492 | 526 | `FIRE_BG` | `stance-icons` | `attack-anything` |
| 15 | ″ | `@STANCE_AMBUSH` | 34 | 526 | 560 | `FIRE_BG` | `stance-icons` | `defend` |
| 16 | ″ | `@STANCE_HOLDFIRE` | 68 | 560 | **594** | `FIRE_BG` (**overflows**) | `stance-icons` | `hold-fire` |
| 17 | `ENGAGEMENT_STANCE_BAR` (612, 102) | `@ENGAGEMENT_HUNT` | 0 | 612 | 646 | `ENGAGE_BG` | `stance-icons` | `attack-anything` |
| 18 | ″ | `@ENGAGEMENT_DEFENSIVE` | 34 | 646 | 680 | `ENGAGE_BG` | `stance-icons` | `defend` |
| 19 | ″ | `@ENGAGEMENT_HOLDPOSITION` | 68 | 680 | **714** | `ENGAGE_BG` (**overflows**) | `stance-icons` | `hold-fire` |
| 20 | `COHESION_BAR` (732, 102) | `@COHESION_TIGHT` | 0 | 732 | 766 | `COHESION_BG` | `stance-icons` | `attack-anything` |
| 21 | ″ | `@COHESION_LOOSE` | 34 | 766 | 800 | `COHESION_BG` | `stance-icons` | `defend` |
| 22 | ″ | `@COHESION_SPREAD` | 68 | 800 | **834** | `COHESION_BG` (**overflows**) | `stance-icons` | `hold-fire` |
| 23 | `RESUPPLY_BEHAVIOR_BAR` (852, 102) | `@RESUPPLY_HOLD` | 0 | 852 | 886 | `RESUPPLY_BG` | `stance-icons` | `hold-fire` |
| 24 | ″ | `@RESUPPLY_AUTO` | 34 | 886 | 920 | `RESUPPLY_BG` | `stance-icons` | `attack-anything` |
| 25 | ″ | `@RESUPPLY_EVACUATE` | 68 | 920 | **954** | `RESUPPLY_BG` (**overflows**) | `stance-icons` | `defend` |

### The overflow, numerically

| Panel | `X` | `Width` | spans | Content on it | Content spans | Left inset | Right inset | Verdict |
|---|---|---|---|---|---|---|---|---|
| `CMD_BG_A` | 5 | 290 | 5..295 | buttons 1–8 | 14..286 | 9 | **9** | correct |
| `CMD_BG_B` | 295 | **154** | 295..449 | buttons 9–13 | 304..**474** | 9 | **−25** | **25 px overflow** |
| `FIRE_BG` | 449 | 120 | 449..569 | `STANCE_BAR` | 492..**594** | **43** | **−25** | 34 px too far left |
| `ENGAGE_BG` | 569 | 120 | 569..689 | engagement bar | 612..**714** | **43** | **−25** | 34 px too far left |
| `COHESION_BG` | 689 | 120 | 689..809 | cohesion bar | 732..**834** | **43** | **−25** | 34 px too far left |
| `RESUPPLY_BG` | 809 | 120 | 809..929 | resupply bar | 852..**954** | **43** | **−25** | 34 px too far left |

**The headline figure: content right edge = 954 px; chrome right edge = 929 px. Overflow = 25 px, on five separate panels.**

The root cause is exact and unambiguous:

- `CMD_BG_B` was sized for **four** buttons: `9 + (4 × 34) + 9 = 154`. That is its literal current width.
- `EVACUATE` made it **five**: it now needs `9 + (5 × 34) + 9 = 188`.
- **Deficit = 188 − 154 = 34 px — exactly one button pitch.**
- The four downstream panels are each correctly sized (`9 + 102 + 9 = 120` for a 3-button bar) but each sits **exactly 34 px to the left** of where its bar now is. Their bars sit at `panel_X + 43` where the convention demands `panel_X + 9`; `43 − 9 = 34`.

So one button was added, every *button container* after it was pushed right by one button pitch, and **not one of the six background panels was touched.** The visible symptom is 25 px of button hanging off the right end of each panel; the layout debt is a uniform 34 px.

Note the `COMMAND_BAR` container itself is fine: `Width: 460`, and `EVACUATE` ends at exactly 426 + 34 = 460. The container is not the thing overflowing — the drawn panel behind it is.

### Options

| Option | Change | Cost | Tradeoff |
|---|---|---|---|
| **A. Fix the panels** | `CMD_BG_B` `Width: 154 → 188`; `FIRE_BG` `X: 449 → 483`; `ENGAGE_BG` `569 → 603`; `COHESION_BG` `689 → 723`; `RESUPPLY_BG` `809 → 843` | **5 single-number edits** | Restores the 9 px inset everywhere; panels stay contiguous (5..295, 295..483, 483..603, 603..723, 723..843, 843..963). Bar grows 929 → **963 px**. No button moves, so muscle memory is untouched. |
| B. Shrink the buttons | 34 → 30 px wide, reflow all 25 | ~25 edits | Everything gets smaller and slightly harder to hit; the 24×24 icons no longer centre cleanly in a 30 px button; every X in the file changes. Buys 100 px nobody needs. |
| C. Wrap to a second row | New `Y` band above the current one | Large | Doubles the bar's vertical footprint over the play area — bad on a 768-tall screen, and this is a first-impression surface. |
| D. Move something out | e.g. push `RESUPPLY_BEHAVIOR_BAR` into a fly-out | Medium + design | Hides a WW3MOD-specific mechanic (ammo/evac doctrine) that a new player most needs to discover. Wrong direction for a public release. |

### Resolution dependence

The command bar's `X` values are absolute from the left edge; only `Y` is window-relative (`WINDOW_HEIGHT - HEIGHT - 5/14`). So the bar's width is **fixed in UI units** and does not adapt.

- **No sidebar collision.** I checked this rather than assuming it: the sidebar is top-right only — `SIDEBAR_BACKGROUND_TOP` is `Y: 10, Height: 262` and `SIDEBAR_MONEYBIN` ends at Y = 300 (`ingame-player.yaml:1147-1152, 1481-1486`). The command bar sits at `WINDOW_HEIGHT − 49`. They never meet vertically at any supported resolution. **There is no horizontal-overlap problem, at any width.**
- **The real limit is raw UI width.** After Option A the bar needs **963 UI px**. The engine's default `WindowedSize` is `1024 × 768` (`engine/OpenRA.Game/Settings.cs:204`), so a stranger on the out-of-the-box window has **61 px of headroom** — it fits, but only just. Today's 929 px has 95 px.
- **UI scale is the actual stranger-risk.** `Settings.UIScale` (`Settings.cs:208`) divides effective UI width by the scale factor. Worked examples after Option A:

| Monitor | UIScale | Effective UI width | 963 px bar |
|---|---|---|---|
| 1920×1080 | 1.0 | 1920 | fits easily |
| 2560×1440 | 2.0 | 1280 | fits |
| 1920×1080 | 1.5 | 1280 | fits |
| 1920×1080 | **2.0** | **960** | **clipped by 3 px** (today: fits with 31 px) |
| 1600×900 | 2.0 | 800 | badly clipped — **already broken today** |

  So Option A takes 1920@2× from "just fits" to "3 px of the last button clipped", and does not change the already-broken sub-900 cases. This is worth a follow-up (making the bar centre or right-anchor, or scale-aware), but it is a **separate** piece of work from the panel misalignment and should not be bundled into it.

### RECOMMENDATION 2

**Take Option A.** Five single-number edits in `chrome/ingame-player.yaml`:

```
Background@CMD_BG_B      Width: 154 -> 188
Background@FIRE_BG       X:     449 -> 483
Background@ENGAGE_BG     X:     569 -> 603
Background@COHESION_BG   X:     689 -> 723
Background@RESUPPLY_BG   X:     809 -> 843
```

It is the minimal change that is also the *correct* change: it restores the 9 px inset convention the file already establishes, rather than papering over the symptom. No button moves, so nothing the user has learned to click changes position. Reject B (churn for no benefit), C (steals play area), and D (hides a signature mechanic from new players).

Raise **separately**, do not bundle: the bar is not resolution-adaptive, and at UIScale 2 on a 1080p monitor the right end clips. Worth its own ticket before a public release.

**Screenshot:** not required for this diagnosis — the arithmetic above is complete and self-verifying. A single in-game capture is worth scheduling **after** implementation as confirmation, at the manager's convenience and not while the user is playing.

---

## 3. Icon uniqueness

### Where the art actually lives

- All bottom-bar icons come from `mods/ww3mod/uibits/glyphs.png`, declared via `^Glyphs` (`chrome.yaml:10-13`), which also names `glyphs-2x.png` and `glyphs-3x.png`.
- **Format: `glyphs.png` is 256 × 256, 8-bit, PNG colour-type 6 — straight RGBA with a real alpha channel. It is NOT indexed and NOT palettised.** (2x = 512×512, 3x = 1024×1024.) This is the single most important fact for your question below: there is **no palette constraint** on chrome art. It is not a SHP, it does not go through the RA palette pipeline, it is an ordinary RGBA PNG.
- Icons are addressed as sub-rectangles of that one sheet. `command-icons` regions are **24 × 24** (`chrome.yaml:246-266`); `stance-icons` regions are **16 × 16** (`chrome.yaml:222-236`).
- The engine picks 2x/3x by DPI and **falls back gracefully** — `ChromeProvider.cs:115-122` only uses `Image3x`/`Image2x` `if (!string.IsNullOrEmpty(...))`. A new collection may ship 1x only; it will be upscaled (softer at high DPI) rather than failing.

### Duplicate map — which buttons share art

There are **25 buttons** drawing on only **11 distinct sprite regions**.

| Sprite region (sheet coords) | Size | Used by | Count |
|---|---|---|---|
| `command-icons/attack-move` (0, 207) | 24² | `@ATTACK_MOVE` | 1 ✓ unique |
| `command-icons/force-move` (25, 207) | 24² | `@FORCE_MOVE` | 1 ✓ unique |
| `command-icons/force-attack` (50, 207) | 24² | `@FORCE_ATTACK` | 1 ✓ unique |
| `command-icons/scatter` (125, 207) | 24² | `@SCATTER` | 1 ✓ unique |
| `command-icons/stop` (150, 207) | 24² | `@STOP` | 1 ✓ unique |
| `command-icons/queue-orders` (175, 207) | 24² | `@QUEUE_ORDERS` | 1 ✓ unique |
| **`command-icons/guard`** (75, 207) | 24² | `@GUARD`, `@PATROL`, `@AUTO_ENTER` | **3** |
| **`command-icons/deploy`** (100, 207) | 24² | `@DEPLOY`, **`@RESUPPLY`**, `@TAKE_COVER` | **3** |
| **`stance-icons/attack-anything`** (0, 119) | 16² | `@STANCE_FIREATWILL`, `@ENGAGEMENT_HUNT`, `@COHESION_TIGHT`, `@RESUPPLY_AUTO` | **4** |
| **`stance-icons/hold-fire`** (51, 119) | 16² | `@STANCE_HOLDFIRE`, `@ENGAGEMENT_HOLDPOSITION`, `@COHESION_SPREAD`, `@RESUPPLY_HOLD` | **4** |
| **`stance-icons/defend`** (17, 119) | 16² | `@EVACUATE` (command bar), `@STANCE_AMBUSH`, `@ENGAGEMENT_DEFENSIVE`, `@COHESION_LOOSE`, `@RESUPPLY_EVACUATE` | **5** |

**Totals: 6 buttons have unique art. 19 of 25 buttons share art with at least one other button. To make all 25 distinct you need 14 new icons.**

Two extra findings worth having:

1. **`resupply` is a literal alias, not just a lookalike.** `chrome.yaml:259-260` reads `resupply: 100, 207, 24, 24 # TODO` — byte-identical coordinates to `deploy` on line 257, with the previous author's own TODO marker attached. This one is already flagged in-tree as unfinished.
2. **`@EVACUATE` is visually inconsistent with its own row, independent of duplication.** It is the only button in `COMMAND_BAR` that pulls from `stance-icons` — so its icon is **16 × 16 at offset (9, 5)** while all twelve of its neighbours are **24 × 24 at offset (5, 1)**. Even given unique art, it will read as visibly smaller and off-grid until it moves to a 24 × 24 `command-icons` region. Any new Evacuate icon should be drawn at 24 × 24 and the button's `Image@ICON` changed to `ImageCollection: command-icons`, `X: 5`, `Y: 1`.
3. **`stance-icons/return-fire` (34, 119) exists and is unused** — one ready-made 16×16 sprite already on the sheet, if you want a quick single-button de-duplication before anything else lands.

### Sheet capacity

I mapped every `^Glyphs` region (152 parsed) against the 256×256 sheet. It is close to full:

- The 24×24 command band (`y = 207..231` and `232..256`) has usable free space only from `x ≈ 200` to the right edge — room for about **2 more 24 × 24 icons**, not 14.
- The only other meaningful gap is **rows 186–206** (21 px tall, full 256 width) — too short for 24 px icons; it would hold roughly 15 icons at 16 × 16.
- `y = 232..256` reaches the exact bottom edge of the image. There is no room to extend downward without resizing the PNG.

So 14 new 24×24 icons do not fit the existing sheet. The clean route is a **new collection with its own `Image:`** — e.g. `ww3-command-icons.png` — rather than growing `glyphs.png` and reflowing 152 existing regions. A chrome collection may declare its own `Image:` / `Image2x:` / `Image3x:` instead of inheriting `^Glyphs`, so this is additive and touches no existing region.

### Your direct question: what can be produced here, honestly

**What is genuinely feasible.** The pipeline imposes almost no barrier. These are plain RGBA PNGs with alpha, no palette, no SHP conversion, no indexed-colour constraint, no engine-side import step — just a file in `uibits/` and a block of `name: x, y, w, h` region lines in `chrome.yaml`. That means a **programmatically generated placeholder set is entirely practical**, and I can state the exact shape of it:

- A single `ww3-command-icons.png` at 256 × 256 RGBA, generated with Python + Pillow (or raw `zlib`/`struct` if Pillow is unavailable — I verified I can already parse these PNGs by hand, so writing one is within reach either way).
- 14+ cells at 24 × 24 on a 25 px pitch, each with a **distinct geometric glyph** — chevrons, arrows, brackets, circles, crosses, arcs, dots — drawn in a flat light grey to match the existing chrome's visual weight, on full transparency.
- Matching `-disabled` variants (the same glyph at reduced alpha) on a second row, since every existing command icon has one and the widget looks them up by the `-disabled` suffix.
- Optionally the 2x/3x sheets by integer nearest-neighbour upscale; or skip them, because `ChromeProvider.cs:115-122` falls back to the 1x image cleanly.
- Plus the `chrome.yaml` collection block and the ~14 `ImageName:` changes in `ingame-player.yaml`.

**What I cannot deliver, stated plainly.** I can produce icons that are *unique, correctly sized, correctly aligned and stylistically neutral*. I **cannot** produce icons that are *good* — pixel art at 24 × 24 that reads instantly, matches the hand-drawn character of the existing `attack-move` / `scatter` / `stop` glyphs, and communicates "patrol" versus "auto-enter" versus "take cover" at a glance. Programmatic shapes will look like programmatic shapes: recognisably placeholder, obviously not by the same hand as the existing set. For a **public release to strangers**, placeholder-looking icons in the most-used UI element may read worse than honest duplicates, because duplicates look like a design choice while mismatched auto-generated glyphs look unfinished.

So the honest framing is: **the duplicate table above is the real deliverable** — it tells you that you need 14 icons and exactly which buttons they are for. Generated placeholders are a legitimate *interim* option if you want to unblock and iterate, and I can build the full pipeline for them, but they are not a substitute for your art on a release surface.

### RECOMMENDATION 3

Split into three decisions, in this order:

1. **Free.** Point `@EVACUATE` at a 24 × 24 `command-icons` region with `X: 5, Y: 1` so it stops being the odd one out in its row, regardless of what art lands there. Do this whenever section 2 is implemented — same file, same area.
2. **Cheap.** Assign the unused `stance-icons/return-fire` sprite to one of the five `defend` users (`@COHESION_LOOSE` is the least semantically loaded) to shave one duplicate at zero art cost.
3. **The decision I need from you.** Choose one:
   - **(a) Ship the duplicates for v1** and treat the 14 icons as a post-release art task, using the table above as your work list. *My pick* — duplicated-but-consistent art reads as deliberate; auto-generated placeholder art reads as unfinished, and this is the surface strangers judge first.
   - **(b) Generate a placeholder set now.** I will build `ww3-command-icons.png` (256², RGBA, 24 × 24 cells, distinct geometric glyphs + `-disabled` variants), the `chrome.yaml` collection block, and the `ImageName:` rewiring. Every button becomes unique and correctly sized; every new icon looks visibly like a placeholder until you replace it — which you can do by overwriting one PNG, with no YAML changes needed.
   - **(c) Hybrid** — generate placeholders only for the worst offenders (`@PATROL`, `@AUTO_ENTER`, `@TAKE_COVER`, `@RESUPPLY`, `@EVACUATE`: the 5 command-bar buttons where duplication is most confusing), leave the four 3-button stance rows sharing art, since within those rows the shared glyphs at least map consistently onto "aggressive / middle / passive" and read as a deliberate system.

---

## Summary of proposed changes (none implemented)

| # | File | Change | Risk |
|---|---|---|---|
| 1 | `chrome/ingame-player.yaml` | delete 2 `ProductionTypeButton` blocks (lines 1413–1448) | very low |
| 1 | `hotkeys.yaml` | delete 3 dead hotkey decls; bind 3 survivors to Q/W/E | low — needs conflict check |
| 1 | `rules/player.yaml` | **no change** (AI references the queues) | — |
| 2 | `chrome/ingame-player.yaml` | 5 numeric edits to `Background@*` X/Width | very low |
| 3 | `chrome/ingame-player.yaml` | `@EVACUATE` icon → `command-icons` 24×24 @ (5,1) | very low |
| 3 | new `uibits/*.png` + `chrome.yaml` | **only if option (b)/(c) chosen** | low, additive |

Verification for all of the above: `make test` (YAML validation) plus one in-game screenshot of the bottom bar and sidebar, scheduled when the user is not playing.
