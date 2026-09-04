# Buy-menu audit — production icons, autobuild, rank overlay

**Date:** 2026-09-04 · **Base:** `main @ d421e4ca` · **Branch:** `wt/buymenu-audit`
**Method:** read-only. No game launch, no screenshots, no YAML edits, no `--check-yaml`.
Pixel measurements come from decoding the shipped SHPs with
[`WORKSPACE/mockups/buymenu_shp_dump.py`](../mockups/buymenu_shp_dump.py), a port of the two
loaders the engine actually uses. Everything else is read from source.

**Mockup:** [`WORKSPACE/mockups/buymenu-icon-arrangements.html`](../mockups/buymenu-icon-arrangements.html)

Marked throughout: **[read]** = verified by reading the shipped code/art. **[derived]** =
arithmetic over values I read, not observed running. **[inferred]** = judgement.

---

## 1. The plain answer: how the fixed queue and autobuild combine

**There is no autobuild queue. There is one queue per tab, and autobuild is a flag on
individual entries in it.**

That single sentence is the whole model, and the UI never says it.

`ProductionItem` carries `public bool Infinite { get; set; }`
(`engine/OpenRA.Mods.Common/Traits/Player/ProductionQueue.cs:772`). Alt+click sets that flag on
the copies it queues (`ProductionPaletteWidget.cs:458` → `Order.StartProductionAutoFlag`,
`engine/OpenRA.Game/Network/Order.cs:299` → `ProductionQueue.cs:512-513`). A plain click queues
identical entries with the flag off. Both kinds sit in the same `List<ProductionItem> Queue`,
interleaved in click order. **[read]**

The only thing the flag does is this, in `EndProduction`
(`ProductionQueue.cs:645-649`):

```csharp
Queue.Remove(item);
if (item.Infinite)
    Queue.Add(new ProductionItem(this, item.Item, item.TotalCost, playerPower, item.OnComplete) { Infinite = true });
```

When a flagged entry completes, a fresh copy of it is appended **to the back of the queue**.
That is autobuild, entire. **[read]**

### Answers to the four questions as asked

**"What does queueing N normally do, and what does enabling autobuild for the same unit do at
the same time?"**
Queueing N appends N one-shot entries. Alt+clicking appends further entries that happen to
recycle. They are the same kind of object in the same list; the game draws no distinction
between them anywhere except at the moment one finishes. Ten manual Abrams plus an Alt+click
Abrams is a queue of eleven Abrams entries, ten of which evaporate on completion and one of
which comes back. **[read]**

**"What is the ordering/priority when the manual queue drains and autobuild takes over?"**
Nothing "takes over" — there is no handover. The queue is strict FIFO on `Queue[0]`
(`ProductionQueue.cs:344`, `TickInner` only ever ticks `Queue[0]`), and a recycled entry
re-enters at the **tail**, behind anything queued in the meantime. So autobuild has the *lowest*
priority of anything in the queue: it never jumps ahead, and any new manual click you make lands
in front of the next recycle. **[read]**

**"Does autobuild top up to a target count, maintain a ratio, or something else?"**
None of those. It is **not** a target count and **not** a ratio against your army. It is a fixed
number of recycling slots: the cycle size is simply *how many flagged entries of that type you
Alt+clicked*. Alt+click Abrams ×5 and Alt+click Bradley ×1 gives you a queue that produces 5
Abrams then 1 Bradley, forever, in that proportion — which is what commit `0174de66` means by
"ratio cycles". It is a ratio **between autobuilt types**, not a ratio to anything you own. It
never looks at how many you have on the field, and it never stops. **[read]**

**"What does the number on the icon count?"**
Bottom number = `icon.Queued.Count` (`ProductionPaletteWidget.cs:761`), i.e. every entry of that
type currently in the queue — manual and autobuild summed, in-flight included, completed
excluded. Top number (when shown) = how many entries of that type sit *consecutively at the head
of the queue* (`:782-790`). Neither is a built count, neither is a target. **[read]**

**"Can the two conflict, double-spend, or starve each other?"**
No double-spend: only `Queue[0]` draws cash, one entry at a time per tab
(`ProductionQueue.cs:344`, `ProductionItem.Tick` at `:823-826`). But yes to conflict, in three
concrete ways — §2.

---

## 2. The three real conflicts

### 2.1 One right-click on a mixed stack deletes your manual orders too

`CancelProductionInner` (`ProductionQueue.cs:610-635`): if **any** entry of the clicked type
carries `Infinite`, it strips the flag from every entry of that type, then refunds and removes
**every queued entry of that type** except the one in flight. It does not check which ones were
flagged. **[read]**

So: queue 3 Abrams normally, then Alt+click one more. Right-click the Abrams icon once — you
intended to cancel the autobuild, and you have cancelled all four. The three manual ones are
refunded and gone. Nothing warns you, and the badge showed one number the whole time so there
was nothing to warn *about* from the player's point of view.

The commit that wrote this (`0174de66`) describes it as matching "the user's *if it is currently
under construction it gets reduced to that one*" intent. That intent was about the autobuild
copies. The code applies it to the type. **[read + inferred]**

### 2.2 The tab's autobuild silently rewrites the icons' autobuild

Two independent autobuild scopes exist and they are wired to the same flag:

- Per-icon: Alt+click an icon → `Infinite` on those entries (`ProductionPaletteWidget.cs:458`).
- Per-tab: Alt+click a category button → `ToggleRepeatProduction` (`ClassicProductionLogic.cs:49-53`).

`ToggleRepeatProduction` (`ProductionQueue.cs:536-543`) does `foreach (var item in Queue)
item.Infinite = RepeatMode;` — **every entry in the tab, of every type**. And `BeginProduction`
(`:655-657`) does `if (RepeatMode) item.Infinite = true;` for every subsequently queued item.
**[read]**

Consequences, all silent:
- Turning tab-autobuild **on** converts your existing one-shot orders into recycling ones.
- Turning it **off** again strips per-icon autobuild you had set deliberately.
- While it is on, a plain left-click is an autobuild order. The click looks identical.

The only signal is a 3 px lime stripe on the 28×28 tab button
(`ProductionTypeButtonWidget.cs:76-78`).

### 2.3 An unaffordable autobuild entry at the head stalls the entire tab, forever

`ProductionItem.Tick` returns without progressing if `TakeCash` fails (`ProductionQueue.cs:825`),
and `TickInner` only ever ticks `Queue[0]`. A manual order you cannot afford is a mistake you
notice and cancel. An autobuild entry re-enters the queue by itself and will sit at the head
draining nothing and blocking everything behind it whenever you are broke. Because it re-adds
itself on completion, this state recurs on its own. **[read]** *(Not new to WW3MOD — the head-only
tick is stock — but autobuild is what makes it self-renewing.)*

---

## 3. What the UI communicates versus what is true

This is the actual bug, stated as a table.

| The UI shows | A player reasonably reads it as | What is true |
|---|---|---|
| One number, bottom-right, e.g. **5** | "5 units are coming" | 5 entries queued, of which an unknown split is one-shot and an unknown split recycles forever |
| That number in **lime green** | "these 5 are the autobuild ones" | *at least one* of the 5 is flagged; the other four may all be manual (`:841`, `anyInfinite`) |
| A **lime stripe** down the icon's left edge | "autobuild is on for this unit" | correct, but it is the same boolean as the green number — two pixels of chrome for one bit |
| A **lime stripe** on the tab button | "this category has autobuild" | it also means *every future click in this tab is an autobuild click* |
| The number counting **down** 5→4→3→2 | "getting through the queue" | true until it reaches the autobuild cycle size, then it stops falling and the same number now means "recycling forever" — the digit changes meaning without changing appearance |
| Two numbers stacked top-right | *(no available reading)* | top = consecutive-at-head run, bottom = total. Documented nowhere. |
| Nothing at all | "no modifiers here" | Alt = autobuild, Shift = ×5, Ctrl = jump the queue, Ctrl+Alt = select-by-type. See §3.1 |

### 3.1 Zero of the icon's modifiers are documented in game

Grepping `mods/ww3mod/chrome/` and `ProductionTooltipLogic.cs` for `Alt+click` / `auto-build` /
`Ctrl+click` returns exactly three hits, all of them the **category tab** tooltip
(`ingame-player.yaml:1208`, `:1226`, `:1244`), which describes the *tab's* modifiers. **[read]**

The icon's own modifiers are undocumented anywhere the player can see:

| Gesture | Effect | Source |
|---|---|---|
| LMB | queue 1 | `ProductionPaletteWidget.cs:589` |
| Shift+LMB | queue 5 | `:589` |
| Alt+LMB | queue 1 **as autobuild** | `:458` |
| Shift+Alt+LMB | queue 5 as autobuild | `:589` + `:458` |
| **Ctrl+LMB** | **queue-jump: insert at position 1, ahead of everything but the in-flight item** | `:463` (`queued = !Ctrl`) → `ProductionQueue.cs:659-661` |
| Ctrl+Alt+LMB | select your units of this type (not production at all) | `:585-586` |
| RMB | pause, or cancel/exit-autobuild | `:471-495` |
| Ctrl+RMB | cancel up to the whole queue length of this type | `:592` |
| MMB | cancel every copy incl. in-flight | `:598` |

Ctrl+LMB is the one I would flag hardest: it is the *only* way to express "I need this now",
it is exactly what a confused player is looking for, and nothing hints at it.

### 3.2 On the infantry tab, two of the indicators are structurally wrong

Infantry is the only queue on `ClassicParallelProductionQueue` (`player.yaml:57`). That class
overrides `IsProducing` to `Queue.Contains(item)` — **true for everything in the queue**
(`ClassicParallelProductionQueue.cs:143-146`). The widget's two derived states both read it:

- `waiting = !IsProducing(first) && !first.Done` (`ProductionPaletteWidget.cs:777`) is therefore
  **always false on the infantry tab**. The gold "queued but not being worked on" colour and the
  lone-item "1" badge (`showBottom = total > 1 || waiting`, `:825`) can never appear there. A
  single queued infantryman shows no count at all; a single queued vehicle behind another type
  shows a gold 1. Same situation, two different displays, by accident. **[read]**
- Every queued infantry icon draws a running clock (`:742`), which is defensible for a parallel
  queue — but see the next point for what the clock *says*.

**The infantry countdown under-reports by exactly 2× whenever two or more infantry types are
queued. [derived]** With `ParallelPenaltyBuildTimeMultipliers: 100` (`player.yaml:69`, a
one-element array), `TickInner` (`ClassicParallelProductionQueue.cs:110-117`) advances the queue
on every *second* game tick, and rotates types after each advance (`:125-130`). With *n* distinct
types queued, a given entry therefore advances once per `2n` ticks. `RemainingTimeActual`
(`:230-239`) reports `remaining × n × arr[0]/100` = `remaining × n`. Displayed `n`, actual `2n`:
the clock always says half the truth. The 2 comes from the array being flat at 100 — stock
OpenRA's default array gives a different, also-wrong ratio, so this is an engine inaccuracy the
mod's tuning simplified rather than introduced.

---

## 4. Question 2 — the `ProductionIconOverlayManager` lead

**Verdict: the trait is the wrong vehicle and is currently inert. The art it points at is
exactly right and should be used directly.** Both halves matter.

### 4.1 It draws nothing today

`ProductionIconOverlayManager` is declared at `mods/ww3mod/rules/player.yaml:233-236`
(`Type: Veterancy`, `Image: iconchevrons`, `Sequence: veteran`). It registers a TechTree watcher
for every actor carrying `WithProductionIconOverlayInfo` with a matching type
(`ProductionIconOverlayManager.cs:75-79`), and `IsOverlayActive` returns whatever the resulting
`overlayActive` dictionary says, defaulting to `false` (`:92-98`).

`grep -rn "WithProductionIconOverlay" mods/` returns **nothing**. No actor in the mod carries the
trait. The dictionary is therefore never populated, `IsOverlayActive` is false for every actor,
and the loop at `ProductionPaletteWidget.cs:733` never draws. The manager loads a sprite in its
constructor and does nothing else. **[read]**

### 4.2 Its shape cannot express banked ranks

Even wired up, the trait is structurally unable to do this job:

- **One sprite, fixed at construction.** `Image` + `Sequence` are single scalars and the sprite is
  resolved once (`:66-68`); `Sequence` is documented "cannot be animated" (`:32`). One manager can
  show one picture.
- **Binary, not a count.** The interface is `bool IsOverlayActive(ActorInfo)`. There is no channel
  for "two of these" or "tier 2".
- **Driven by TechTree prerequisites, which are player-global flags** — not per-actor-type
  integers. Banked rank is `RankAccumulation.StockOf(actorName, tier)`
  (`RankAccumulation.cs:375`), a per-actor-type, per-tier counter that changes on a timer. Driving
  the overlay from it would mean synthesising a prerequisite per (actor × tier) — roughly 44
  buildables × 3 tiers ≈ 130 fake prerequisites, granted and revoked as stock ticks — to carry
  information the widget can already read directly with one method call.
- **Placement is not yours.** `Offset(iconSize)` is hardcoded to `(sprite - icon) / 2`
  (`:85-90`), which lands the sprite at the icon's top-left corner. There is no offset field.

Three tiers with counts would need three managers, three `WithProductionIconOverlay` traits per
actor, and the prerequisite scaffolding above — and would still not render a digit. **Kill the
lead.** **[read + inferred]**

### 4.3 But the art is real, ships, and is the right size

`mods/ww3mod/bits/misc/ui/iconchevrons.shp` exists, is 527 bytes, and is **byte-identical**
(md5 `8d7c5986ca47388cafb5b82a0a16ffcd`) to `engine/mods/ra/bits/iconchevrons.shp`. It is a
SHP(TD), 4 frames on a 15×20 canvas. Decoded ink extents: **[read]**

| frame | content | ink size |
|---|---|---|
| 0 | one chevron | 14 × 10 |
| 1 | two chevrons | 14 × 14 |
| 2 | three chevrons | 14 × 18 |
| 3 | star | 15 × 16 |

Gold, black-outlined, pre-drawn rank insignia — legible at 1× in a way a 6×3 polyline is not.
The `iconchevrons` sequence already exists (`sequences-misc.yaml:539-541`) with one `veteran:`
entry at frame 0; adding `veteran1/2/3` with `Start: 0/1/2` is a four-line sequences edit.

The right move is therefore: **load the sprite in `ProductionPaletteWidget` the way `clock` and
`cantBuild` already are** (`ProductionPaletteWidget.cs:206-207`) and draw it from
`DrawAccumulatedRanks`, reading `StockOf` as it does now — bypassing
`ProductionIconOverlayManager` entirely. The manager itself should either get a
`WithProductionIconOverlay` consumer or be deleted from `player.yaml`; right now it is dead
declaration. **[inferred]**

Note frame 3, the star, is unreachable for this purpose: purchase stock caps at 3 tiers
(`RankAccumulation.cs:304`, `Caps = {3, 2, 1}`) and `MaxPurchasableRank = 3` (`:59`).

### 4.4 The collision, measured

**Cameo art is 64×48 for 204 of the 260 shipped icons and 60×48 for 40 more** (16 outliers at
32×24 / 24×24 are not cameos). The cell is `IconSize: 62, 46` with `IconSpriteOffset: -1, -1`
(`ingame-player.yaml:1180-1182`), and `DrawSpriteCentered` subtracts half the sprite
(`WidgetUtils.cs:86-89`), so a 64-wide cameo is drawn from cell x −2 and a 60-wide one from cell
x 0. **Two cameo widths against one cell width is itself a small defect:** 64-wide art bleeds 2 px
left over the 1 px gutter into its neighbour, 60-wide art leaves 2 px bare at the right. **[read]**

**The name band.** I decoded ten shipped cameos and measured the caption rows directly (see
`WORKSPACE/mockups/assets/_caption-zoom.png`, produced by the dump script). Every one of them
puts its baked all-caps caption in the same place:

- **glyph ink: art rows 41–46**, with the 1 px drop shadow reaching row 47
- several cameos (e.g. `littlebirdicon`) additionally paint a solid black plate across the full
  width from art row 40 to 47
- captions are centred and run nearly the full width — `a10icon`'s "ATTACK AIRCRAFT" reaches both
  edges

This matches the generator that produces new cameos: `tools/cameo/convert.py:133-147` places the
glyph body at `y0 = h - 2 - GLYPH_H` = row 41 on a 48-row canvas, 5 rows tall, plus a shadow.
That file's docstring says the geometry was "measured from the shipped US cameos, not invented",
and my independent decode agrees. **[read]**

**In cell coordinates the name band is rows 38–45 — the bottom 8 of 46, full width.**

**Where the rank chevrons land.** `DrawAccumulatedRanks` (`ProductionPaletteWidget.cs:860-899`):

- `baseY = pos.Y + IconSize.Y - RankBottomMargin` = cell y **44** (`:871`)
- a chevron is `RankChevronHeight = 3` tall with a 1 px shadow → cell y **41–45**
- tiers stack upward at `RankChevronPitch = 4`, so a tier-3 entry occupies cell y **33–45**
- the count digit sits at `baseY - Measure(text).Y + 1` = cell y 35, ink to y 44 (`:889-891`)
- entries run left from `RankLeftMargin = 4` at 18 px each (6 chevron + 2 gap + 6 digit + 4 entry
  gap, TinyBold digit advance measured at 6 px from `engine/mods/common/FreeSansBold.ttf` @ 10)

**Worst case — the full 3/2/1 the caps allow — the rank strip occupies cell x 4–54 (81 % of the
width) and cell y 33–45. The name band is cell y 38–45. They overlap over the strip's entire
lower half, across the whole caption.** **[derived from read values]**

Commit `908a2719` asserts "Bottom-left is the only corner of this button nothing else claims"
and enumerates the badge, the centre text and the auto stripe. It does not mention the baked
caption, because the caption is in the art rather than in the widget. That omission is the
collision the user is reporting.

### 4.5 Where there is genuinely free space

**Inside the 62×46 cell: none.** The cameos are full-bleed photography with a 1 px bevel and a
caption baked into the bottom 8 rows. Every overlay the widget draws is drawn *on top of* picture.
There is no unused region — only regions that are less informative.

Outside the cell there is almost nothing either: `IconMargin: 1, 1` (`ingame-player.yaml:1181`),
3 columns × 63 px pitch from palette X 42 inside a 238-wide container leaves ~7 px at the right
edge and a ~6 px dead gutter at x 36–41 between the tab column and the first icon. **[read]**

So the honest options are (a) occlude art behind a deliberate plate, (b) enlarge the cell, (c)
re-bake the cameos to reserve a strip, or (d) move information off the icon into the tooltip.
The mockup shows (a) in two flavours and a minimum-change variant. **[inferred]**

---

## 5. Question 3 — everything drawn on or around an icon

Cell = 62 × 46. Coordinates below are cell-relative. "Stock" = present in upstream OpenRA;
"WW3MOD" = added or materially reshaped here, per
`git log --follow -- engine/OpenRA.Mods.Common/Widgets/ProductionPaletteWidget.cs`.

| # | Element | Means | Where (cell coords) | Size | Movable? | Origin |
|---|---|---|---|---|---|---|
| 1 | Cameo art | the unit | x −2…61 (64-wide) or 0…59 (60-wide), y −2…45 | 64×48 / 60×48 | only by changing `IconSize`/`IconSpriteOffset` | stock mechanism, WW3MOD art (`:729`) |
| 2 | **Baked name** | unit name | **y 38–45, full width** | ~8 rows | **no** — it is pixels in the .shp | WW3MOD (`tools/cameo/convert.py:133`) |
| 3 | Bevel | frame | 1 px border of the art | 1 px | no | WW3MOD art (`convert.py:116-126`) |
| 4 | Clock sprite | build progress of the in-flight entry | full cell, centred | 62×46 | no (sprite is cell-sized) | stock (`:745-757`) |
| 5 | Darken sprite | not currently buildable | full cell, centred | 62×46 | no | stock (`:753-754`) |
| 6 | READY / ON HOLD | done / paused | centred, y ≈19–27 | 35 px / 46 px wide | yes, widget field | stock (`:802-812`) |
| 7 | mm:ss | time remaining | centred, y ≈19–27 | ~21 px | yes | stock (`:814-818`) |
| 8 | **Autobuild stripe** | ≥1 entry of this type recycles | x 0–2, y 0–45 | 3 × 46 | yes, `AutoStripeWidth` (`:142`) | **WW3MOD** (`:793-800`) |
| 9 | **Badge — "now"** | consecutive run of this type at queue head | right-anchored x 59, y 1 | 6 px/digit, 10 px line | yes (`:220-223`) | **WW3MOD** (`:826-829`) |
| 10 | **Badge — "total"** | all entries of this type queued | right-anchored x 59, y 10 (or 1) | as above | yes | **WW3MOD** (`:833-841`) |
| 11 | **Rank chevrons** | banked free ranks per tier | x 4→54, y 33–45 | 6×3 polyline + 6 px digit each | yes, 8 widget fields (`:153-162`) | **WW3MOD** (`:860-899`) |
| 12 | Icon-overlay sprite | *(nothing — inert, §4.1)* | would be top-left ~16×12 | — | no | stock, unused (`:733`) |
| 13 | Row background / foreground | sidebar chrome | behind and over each 47 px row | 190×47 / 238×47 | yes, chrome | WW3MOD chrome (`ingame-player.yaml:1158-1195`) |
| 14 | Hotkey | Production01–24 bound | **not drawn on the icon at all** | — | — | stock binding (`:1183-1184`), shown only in the tooltip |
| 15 | Cost | credits | **not on the icon** — tooltip only | — | — | stock (`ProductionTooltipLogic.cs:154`) |
| 16 | Category tab stripe | tab-wide autobuild on | 3 px left edge of the 28×28 tab | 3×28 | yes | **WW3MOD** (`ProductionTypeButtonWidget.cs:76-78`) |

### Drift from stock, and whether it still earns its place

Four elements are WW3MOD additions. My read on each:

- **#8 autobuild stripe — earns it, but is redundant with #10.** The stripe and the green total
  encode the same single bit (`anyInfinite`). One of them is spare. It exists as a primitive
  rather than an `∞` glyph for a good reason (FreeSansBold at TinyBold has no such glyph — it
  rendered as a missing-glyph box; `:138-140`), and that reasoning still holds.
- **#9 / #10 the two-number badge — the "total" earns it; the "now" does not.** "Total" is the
  only queue feedback there is. "Now" answers a question nobody asks ("how many of these are
  contiguous at the head?"), appears only in the narrow case `0 < nowCount < total` (`:824`), is
  meaningless on the infantry tab where the head rotates every advance
  (`ClassicParallelProductionQueue.cs:125-130`), and costs the top-right corner 10 more pixels of
  height. **It is also the wrong second number.** The second number a player actually needs is
  the manual/autobuild split, which nothing currently shows.
- **#11 rank chevrons — the information earns it, this rendering does not.** 6×3 px at a 1.25 px
  stroke is below legibility, and it is drawn over the one part of the icon that already has type
  in it. It also shows all three tiers when only the highest can be spent next
  (`RankAccrual.HighestHeldTier`, `RankAccumulation.cs:142-152`) — three entries' worth of width
  for one entry's worth of decision.
- **#16 tab stripe — earns it, and is under-explained.** It marks a mode that silently changes
  what every subsequent click in that tab does (§2.2); 3 px is thin for that.

---

## 6. Question 4 — what a new menu would cost the layout

### 6.1 A Powers tab: the slot is free, the queue is not the hard part

**The tab column has spare room and the top-left activation surface already exists.** **[read]**

- `Container@PRODUCTION_TYPES` is `Width: 29, Height: 240` at Y 2, with 28×28 buttons on a 31 px
  pitch (`ingame-player.yaml:1196-1253`). Occupied Y: 0, 31, 62. **Seven** slots fit inside 240
  (last at Y 186); three are used. Naval is commented out at Y 93 and Building/Defense
  deliberately have no tab (`:1270-1273`). **A Powers tab is a free insert. It costs the layout
  nothing.**
- The brief's framing of "a seventh queue tab" overstates it: six queues are *declared*
  (`player.yaml:23-93`) but only **three** are reachable in the sidebar. A Powers tab would be the
  **fourth visible tab**, not the seventh.
- The activation half is already built: `Container@SUPPORT_POWERS` sits at X 10, Y 10 with a
  `SupportPowers@SUPPORT_PALETTE` widget on the same `IconSize: 62, 46` and 6 hotkeys
  (`ingame-player.yaml:16-39`), and `SupportPowerManager:` is live at `player.yaml:110`. Only the
  ~15 `AirstrikePower`/`ParatroopersPower` blocks are commented out (`player.yaml:131`–`:600`).

The cost is not layout, it is model: a `ProductionQueue` produces **actors**, and a support power
is not an actor. Buying a power through the buy menu means either (a) a producible dummy actor
that grants the power on completion, or (b) a new queue-like trait. That is a design question for
whoever owns the powers item, not a buy-menu one. **[inferred]**

One thing the buy menu *would* need: powers have no cameo captions baked by
`tools/cameo/convert.py`'s roster, and the support palette's own overlay chrome
(`background-supportoverlay`, `ingame-player.yaml:31-39`) is drawn at 62×46 — so the two surfaces
already agree on icon size, and a power icon could move between them unchanged.

### 6.2 Pre-loaded transports: the engine can express it, the icon cannot

**The production path already has the hook.** `ProductionQueue.CreateProductionInits`
(`ProductionQueue.cs:727-741`) builds the `TypeDictionary` handed to `Production.Produce`, and
already injects a `VeterancyLevelInit` there. `CargoInit : ValueActorInit<string[]>` exists
(`Cargo.cs:1290`) and `Cargo` also honours an `InitialUnits` list (`:54`, `:326`). Spawning a
Humvee with a fireteam inside is an init added at that one call site. **[read]**

**What the buy menu cannot currently express** is *which* preset. A `ProductionIcon` is 1:1 with an
`ActorInfo` (`ProductionPaletteWidget.cs:687-700`, keyed off `AllBuildables`), so today "Humvee"
and "Humvee + fireteam" would have to be two separate buildable actors with two separate cameos —
which multiplies the grid: 23 vehicles × presets. At 3 columns and `MinimumRows: 4`, the vehicle
tab is already scrolling. **[read + inferred]**

The cheaper shapes, both of which need a decision *before* layout is fixed:
- a **modifier on the existing icon** (a fourth gesture on an already-overloaded, undocumented set
  — see §3.1), or
- a **sub-panel** opened from the icon, like the existing `unload-menu.yaml` /
  `garrison-panel.yaml` precedent, which would need somewhere to anchor.

Either way the buy menu needs at least one more piece of per-icon state to display ("this order
carries passengers"), and there is nowhere on the icon left to put it (§4.5). **This is the
strongest argument in the whole audit for deciding contents before arrangement.**

---

## 7. Where I disagree with the brief

- **"`ProductionIconOverlayManager` is the answer."** It is not, and it is inert today (§4.1–4.2).
  The *art* it names is the answer; the trait is not. Killed as briefed.
- **"a seventh queue tab."** Three tabs are visible, not six; a Powers tab is the fourth, and four
  more slots remain after it (§6.1).
- **"the new rank indicators sit on top of the name band."** Confirmed, and worse than stated: at
  the full 3/2/1 the strip covers 81 % of the icon's width and reaches 5 rows above the caption.

## 8. What I could not verify

- **No running game.** Every geometry number is computed from source constants and decoded art,
  not observed. In particular I have not seen the 2× infantry clock error (§3.2) on screen.
- **Palette.** Cameos render through `chrome` → `temperat.pal` (`palettes.yaml:58-62`), which
  lives inside a Blowfish-encrypted `local.mix`. The dump script substitutes the map-local
  `engine/mods/ra/maps/chernobyl/temperat.pal`. **Geometry is exact; hues in the mockup may be
  slightly off in-game.**
- **Glyph ink rows.** TinyBold text positions are from `SpriteFont.Measure` returning
  `rows * size` = 10 (`SpriteFont.cs:230-246`) plus FreeSansBold bbox measured with Pillow. The
  engine's own rasteriser may place ink a pixel differently.
- I did not audit the observer/spectator production widgets, or `SupportPowersWidget`'s own
  overlay set.
