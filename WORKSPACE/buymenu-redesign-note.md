# Buy-menu redesign — five architectures, and the one I would ship

**Date:** 2026-09-04 · **Base:** `main @ 2c8488ef` · **Branch:** `wt/buymenu-redesign`
**Mockup:** [`WORKSPACE/mockups/buymenu-redesign.html`](mockups/buymenu-redesign.html)
**Method:** read-only. No game launch, no screenshots, no YAML edits, no `--check-yaml`, no engine
code. Pixels come from decoding the shipped art with
[`buymenu_shp_dump.py`](mockups/buymenu_shp_dump.py) and
[`buymenu_redesign_assets.py`](mockups/buymenu_redesign_assets.py).

Marked: **[read]** = verified against shipped code/art. **[derived]** = arithmetic over read values.
**[inferred]** = judgement.

---

## 0. Two premises in the brief are wrong, and both change the design

### 0.1 The dead gutter and the right margin are not free — they are the frame

The brief said the ~6 px at x 36–41 and the ~7 px at the right edge are "real, currently unused,
and cost nothing to claim". They are neither unused nor free.

`Container@PALETTE_FOREGROUND` is declared **after** `ProductionPalette@PRODUCTION_PALETTE`
(`mods/ww3mod/chrome/ingame-player.yaml:1177` then `:1186-1195`), so its cloned `ROW_TEMPLATE`
draws `background-iconrow` **over** the icons, once per row. Decoding that region — `sidebar.png`
at `0, 116, 238, 47`, named at `chrome.yaml:32` — gives, per column: **[read]**

| columns | alpha | what it is |
|---|---|---|
| x 0–40 | fully opaque, all 47 rows | brushed-metal panel behind the tab column, with a 3 px dark bevel at x 38–40 |
| x 41–102, 104–165, 167–228 | transparent except a hairline at y 46 | the three icon cut-outs |
| x 103, x 166 | fully opaque | 1 px dividers, drawn over each icon's last pixel column |
| x 229–237 | fully opaque, all 47 rows | the sidebar's right bevel |

So the "gutter" is the metal panel plus its bevel, and the "right margin" is the right bevel.
Claiming either means re-cutting `sidebar.png`.

**And re-cutting is not enough.** Both strips sit *outside* the three columns. A mark drawn there
belongs to a whole row of three different units and cannot say which one it means. **A per-icon
status rail in the gutter is not buildable at any price** — that is a geometry fact, not a cost.
It is why every option below either takes a column, grows the row, or leaves the grid entirely.

One thing that came out *cheaper* than expected: NATO and BRICS both point `background-iconrow` at
the same region of the same file (`chrome.yaml:32` and `:90`), so any frame re-cut is one edit, not
two. **[read]**

### 0.2 The badge cannot say "↻"

My first draft folded the autobuild stripe into the badge as a lime `2↻`. I checked the font
instead of assuming: **`FreeSansBold.ttf` has no U+21BB**, exactly as it has no U+221E — which is
the documented reason the stripe exists as a primitive in the first place
(`ProductionPaletteWidget.cs:138-140`). And there is no fallback: `SymbolsFont = "Symbols"`
(`:75`) is resolved with `TryGetValue` (`:213`), and `mods/ww3mod/mod.yaml` declares no such font,
so `symbolFont` is null. **[read]**

Present and usable at TinyBold: `+` `×` `•` `↑` `♦`. Absent: `↻` `∞` `▲` `◆`. **[read]**

The badge therefore reads **`3`(white)`+2`(lime)** — 18 px at 10 pt, comfortably inside the cell.
That is a better outcome anyway: it is the first time the mixed manual/autobuild stack is visible
at all, which is precisely the warning the audit says is missing before one right-click refunds all
five (§2.1).

### 0.3 What I did not disprove

The audit's numbers all held on re-check: the caption band at cell rows 38–45, the 62×46 cell, the
three-column pitch, the four spare tab slots, `iconchevrons.shp` being byte-identical to stock with
14×10 / 14×14 / 14×18 ink, and `ProductionIconOverlayManager` being inert. I did not re-derive
them; I spot-checked the ones the design leans on. **[read]**

One correction to a number I nearly used myself: **banked rank counts are not bounded by the 3/2/1
caps.** `StockOf` returns `Total(tier)` = `Stock + BonusStock` (`RankAccumulation.cs:211`), and
`BonusStock` is incremented by `CreditWhole` with no cap check (`:245`) whenever a veteran comes
home alive. Any design showing a rank count must budget for two digits. **[read]**

---

## 1. The reframe, corrected

The brief's instinct was right and its geography was wrong. The icon *is* the wrong surface to
accumulate state on — 62×46, full-bleed, with the bottom 8 rows printed into the art. But there is
no free margin to move state into. The honest set of destinations is:

1. **Less of the icon** — draw only what is actionable now (costs information, not pixels).
2. **A column of the grid** — free of art changes, costs a third of the grid.
3. **A new row** — free of art changes, costs a row of vertical space.
4. **A taller row** — costs a chrome re-cut, costs about one visible row.
5. **Off the surface** — tooltip and tab buttons; costs glanceability.

The five options are exactly those five. They are not five arrangements; they are five different
answers to "where does this live".

---

## 2. The options

Every one of them assumes two changes that are independent of the architecture and that I would
make regardless:

- **Delete the lime left stripe.** It and the lime badge colour encode the same single bit,
  `anyInfinite` (`:793` and `:841`). Replace both with the split badge `3+2`.
- **Load `iconchevrons.shp` in the widget** the way `clock` and `cantBuild` already are
  (`:206-207`), and draw from it instead of the 6×3 polyline. Four lines in
  `sequences-misc.yaml:539` to expose frames 1 and 2. `ProductionIconOverlayManager` stays killed.

### A — Quiet grid · *ship this*

**Moves:** per-tier rank counts and the queue's internals leave the icon for the tooltip. What
remains on the cameo is two marks: the split badge top-right, and **one** chevron sprite top-left
for the highest held tier — the user's own proposal — suppressed while READY / ON HOLD / the
countdown is showing.
**Costs:** one file. ~40 lines in `ProductionPaletteWidget.cs`, plus the 4-line sequences edit. No
art, no chrome, no new widget, no layout change. **[inferred]**
**Gives up:** "I am three deep in rank-1s" at a glance.
**Caption band:** survives — nothing is drawn below cell row 12.

### B — Info column

**Moves:** everything except the art and the clock into a 62 px text panel that takes column 3;
the grid drops to 2 columns.
**Costs:** a new panel widget and `Columns: 2`. **No art change** — the panel sits in the frame's
existing third cut-out. **[read + inferred]**
**Gives up:** a third of the grid. Infantry is the largest tab a player actually sees (15
buildables, `rules/ingame/infantry.yaml`): 5 rows at 3 columns, **8 rows at 2**, against about 5
rows of sidebar. It also asks the player to learn a lane→icon mapping, because the panel serves
two icons. The unambiguous variant — a narrow panel beside each icon — does not fit 238 px without
recutting the frame. **[derived]**
**Caption band:** survives; nothing is drawn on the art at all.

### C — Queue strip

**Moves:** the entire queue out of the grid. The grid answers "what can I buy" and carries only art,
the clock, and the one chevron. A strip row at the top of the palette answers "what is happening":
the real FIFO in order — head with its clock, second, then the tail summarised as text, with the
lime stripe on each recycling *entry*, where it finally means something exact.
**Costs:** highest. A new widget reading `AllQueued()` positionally, ~150–200 lines. No art change
— it reuses the 62×46 cell and the shipped row frame. **[inferred]**
**Gives up:** one row of vertical space, and per-icon "how many of these are coming".
**Caption band:** survives.
**Worth noting:** this is the only option that shows what the engine actually has. There is one
FIFO list and autobuild is a flag on entries in it (audit §1); every per-type aggregate the current
UI draws is a lossy summary of that list. C is also the only option that would have made §2.1
(one right-click refunds your manual orders too) visible before it bit someone.

### D — Header band

**Moves:** every overlay off the picture into a 12 px band above it. The art is never occluded by
anything but the clock.
**Costs:** chrome art. `background-iconrow` and `background-iconbg` recut 47 → 58 px, plus
`IconMargin`. One edit covers both factions. **[derived]**
**Gives up:** about one visible row (250 / 58 = 4.3 against 5.3 today). The band is 62 px wide and
gets tight at worst case — the mockup shows it. **[derived]**
**Caption band:** survives trivially.

### E — Off-surface

**Moves:** everything. Totals go on the tab buttons; per-type detail goes in the tooltip, which was
just widened and has room.
**Costs:** lowest. A badge on `ProductionTypeButtonWidget` plus rows in `ProductionTooltipLogic`.
**Gives up:** glanceability, which is most of what a buy menu is for.
**Caption band:** survives.
**Verdict:** I would not ship it. It is in the mockup because it is the honest floor of the design
space, and because a real chunk of what the current icon draws genuinely belongs there.

---

## 3. Your two ideas, judged

### "Only the chevron for the highest one" — yes, and it is what the engine already does

`RankAccrual.HighestHeldTier` is documented as *"the rank a purchase would consume"*
(`RankAccumulation.cs:141-152`), and `Spend` only ever touches that tier (`:225-236`). Showing one
tier is not a simplification of the display; it is the display finally matching the rule. It costs
one 14×18 sprite instead of a 51×13 strip, which clears the caption band by 19 rows. **[read]**

**What is lost, and where it should go.** You lose the depth reading — three rank-1s versus one.
I would not put that back on the icon: it is stock management, not a purchase decision, and the
purchase only ever spends the top tier. Put the per-tier breakdown in the tooltip. Keep **one**
digit on the icon — the count at the highest tier, and only when it is above 1 — and budget two
digits for it, per §0.3. **[inferred]**

### "The green bar as a corner icon" — exactly one corner is free, and the chevron needs it

Of the four 16×12 corners of the cell: bottom-left and bottom-right are 8 of 12 rows caption;
top-right is the queue badge. That leaves top-left — and top-left is where the chevron has to go,
because it is the only corner tall enough for a 14×18 tier-3 sprite. **The two ideas want the same
pixels.** **[derived]**

The way out is not to relocate the green bar but to **delete** it. It encodes one bit that the
badge already carries. Fold it in as the lime half of `3+2`, and the corner frees itself for the
chevron. That is option A, and it is the change I would make first whichever architecture you pick.

---

## 4. Recommendation

**Ship A. Then decide C separately, on its own merits.**

A is the whole of the user's own instinct, made consistent: one chevron, one badge, one file, no
art, no layout change, and it clears the caption band with room to spare. It fixes the reported
collision and removes a redundant element while it is in there.

C is the one that fixes the *model* problem rather than the *pixel* problem — the UI presents a
FIFO list as a set of per-type aggregates, and every confusion in audit §3 follows from that. But
it is a new widget and a row of screen, and it is worth its own decision rather than being smuggled
in behind a chevron fix. **A and C compose**: A's grid is exactly the catalogue C wants.

I would not build B or D. B trades a third of the grid for legibility the tooltip can supply for
free, and D pays a chrome re-cut for the same. Both are in the mockup so the tradeoff is visible
rather than asserted.

---

## 5. What I could not verify

- **No running game.** Every geometry number is computed from source constants and decoded art.
- **Palette.** Same caveat as the audit: `temperat.pal` is substituted from the map-local copy, so
  hues are approximate and geometry is exact.
- **Font metrics.** Text widths in the mockup come from the browser rasterising the real
  `FreeSansBold.ttf` at 10 px; the engine's `SpriteFont` may place ink a pixel differently.
- **The clock.** No `clock.shp` ships outside the encrypted mix, so the mockup's build wipe is a
  CSS approximation. Its extent — the full cell — is right; its shape may not be.
- **Costs in §2 are estimates**, not measured against an implementation.
- I did not check whether any of the five options breaks the observer/spectator production widgets,
  which draw through the same widget class.
