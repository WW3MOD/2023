# Unit indicator layout — the final plan

**Ref: `main @ d8ce0db3`**, worktree `wt/indicator-layout`, branch `wt/indicator-layout`.
**Design + mockup only.** No rules YAML edited, no engine C# edited. Nothing launched, no screenshot
captured, no `--check-yaml`, no `make test` — all withheld from this job by the brief.

Companion artifact: [`WORKSPACE/mockups/indicator-layout.html`](mockups/indicator-layout.html), built by
[`WORKSPACE/mockups/indicator_layout_assets.py`](mockups/indicator_layout_assets.py).
Inventory not re-derived here: [`indicator-audit.md`](indicator-audit.md) is the source of truth for
what is drawn today, and [`indicator-mechanics.md`](indicator-mechanics.md) for what a glyph can carry.
The quantity the display reads is [`impediment-spec.md`](impediment-spec.md) §1.1.

---

## 0. What is new in this document, and what is only rearranged

Five things here are **measured, not inherited**, and three of them correct a figure the existing
documents state:

| # | Finding | Corrects |
|---|---|---|
| 1 | **The diamonds rasterise at 6×9 px**, not 5.0×7.7 (hollow) / 5.8×8.0 (solid). Hollow `U+25CA`, solid `U+2666` and white `U+2662` all share an identical 6×9 bbox at `TinyBold` size 10; only the *advance* differs (5.0 vs 6.0 vs 6.0). | [`indicator-mechanics.md`](indicator-mechanics.md) Q9, which derived from outlines and flagged ±1px hinting error as honest-gap #1. **Gap closed, and the real number is at the top of its error bar.** |
| 2 | **The diamond↔stance clearance is 1px, not ~2px.** `A` inks 8px wide centred at x+8, so it occupies cx+4…cx+12 against the diamond's cx−3…cx+3. | `defaults.yaml:941-943`, *"only by about 2px, so widening the glyph is not free."* It is tighter than that: widening the glyph by one pixel makes them touch. |
| 3 | **The vehicle damage bar is 17×5 against a 29×24 tank on a 24×24 cell.** An Abrams at its shipped `RenderSprites: Scale` of 1.25 is 29 px wide, so the bar spans 59% of the hull and overhangs the *cell*. On the 24×24-canvas vehicles (humvee) it overhangs the hull outright. | Nothing states this. It is the mechanical reason the bar reads as chrome bolted on rather than as part of the unit. |
| 4 | **A rifleman is 7×8 px, and the diamond glyph is 6×9 — the mark is TALLER THAN THE MAN.** `^E3` ships `Scale: 0.65` over an 11×12 ink box. The 6×6 damage dot is then 86% of his width. | Nothing states this, and it reframes the problem: on infantry this is not decoration *over* a unit, it is a column of chrome roughly three times the soldier's height with a 7×8 man at the bottom. Any layout that keeps stacking marks above infantry is fighting arithmetic. |
| 5 | **The palette proxy is exact for decorations.** `engine/mods/ra/maps/chernobyl/temperat.pal` reproduces four values the tree states independently: `pip-suppression` f0 `#FFD77D` and f9 `#9A2800` (`WithGarrisonDecoration.cs:59`), and the damage ramps `#FFFF55`/`#FF8A00`/`#FF0000` (`infantry.yaml:713-715`) and `#650000` (`defaults.yaml:185`, "101,0,0"). | Upgrades the standing "hues are a shade approximate" caveat: for **decorations** they are not approximate at all. |

Everything else is rearrangement of what the two audits already established.

---

## 1. The three gates, and how this plan stays inside them

**Gate 1 — decision 08: do not redraw a mark whose underlying mechanic is broken.**
Applied as a hard filter. Four marks fail it and are therefore **not drawn in any row of the mockup**,
with the cell left empty rather than filled with a lie:

- **The Fire-axis stance glyph's *implication*.** The glyph is an accurate report of `AutoTarget.Stance`;
  what is wrong is that a human's gold `A` promises a coordinated hold-and-spring that stages 2–4 gate
  behind `enable-ambush-tactics`, which no human opt-in path grants (`PIPELINE.md:618`). Redesigning
  that glyph is decision 08 committed twice, same subject, same user. **Stance is carried in this plan
  as a neutral "you set this deliberately" mark and nothing more** — it must not imply concealment.
- **Defense pips.** `^DefensePips` is inherited by nothing and the `defense` condition is granted
  nowhere (`infantry.yaml:671`). **Delete.**
- **The tier-10 concealment ring.** `visibility-10` can never be granted. Keep the YAML (it is a
  deliberate superset, `infantry.yaml:814-819`), draw nothing.
- **A fine-grained detectability gauge on vehicles.** Vehicle concealment spans 1–3 of 9
  ([`indicator-audit.md`](indicator-audit.md) §3.4). A ten-step readout there would be a readout of a
  three-step quantity. Making it meaningful needs more `DetectableAddativeModifier`s — a *mechanics*
  change inside the standing user gate, not a display change.

**Gate 2 — no readout for the vehicle turret lock.** User ruling 2026-09-03, recorded at
`vehicles.yaml:293-302`. `27210e9c` hung a red `X` there and it was reverted *after* the argument for it
had been made. The comment is explicit that the open question is "a QUIETER treatment, not this one
again". **No row in this plan puts any mark on the 50% turret-lock crossing.** The `badly damaged` cell
in the mockup is captioned to say so, because the absence is deliberate and should not read as an
oversight.

**Gate 3 — suppression survives underneath impediment.** Ruled by the user. `suppressed` keeps its
meaning: warhead-granted, decaying, "under fire right now". `impediment` is the derived total
`I = clamp(0,100, S + D)` ([`impediment-spec.md`](impediment-spec.md) §1.1). §4 below is the required
statement of which marks read which.

---

## 2. Disposition of every mark in the audit

All 24 entries in [`indicator-audit.md`](indicator-audit.md) §1, plus the two the audit lists as
already-off. **Positions are given for the recommended row (A · Base ring); §6 gives the fallback.**

| # | Mark | Verdict | Where it goes |
|---|---|---|---|
| 1 | Selection box | **KEEP unchanged** | Bounds rect, selected only. Note it is suppressed on infantry (`ShowNever: true`, `infantry.yaml:56-57`) — see §5. |
| 2 | Health bar | **LEAVE OFF** | Switched off by the user himself (`SelectionBarsAnnotationRenderable.cs:168-181`). Re-enabling is a product decision, not part of this. |
| 3 | Extra bars (EMP, capture) | **KEEP unchanged** | Bottom edge, `ISelectionBar` stack. Out of scope. |
| 4 | Detectability diamond | **CHANGE — becomes the base ring** | Ground ellipse at the unit's feet: colour = grade, weight = grade, dashed while Concealed/Low. Removes it from the crowded top row entirely. |
| 5 | Stance — Fire (`X`/`A`) | **CHANGE — move, do not redesign** | Bottom-left of bounds, selected-or-modifier only. Glyph and colour **unchanged** (gate 1). |
| 6 | Stance — Engagement (`H`/`>`) | **CHANGE — move** | Same slot, second position. |
| 7 | Suppression pips (10 tiers) | **DELETE as ten pips; FOLD into the impediment arc** | Ten colours of one 6×3 glyph whose adjacent tiers are not separable (`architecture.md:491`). The arc carries the same quantity with real resolution. |
| 8 | Damage pip — vehicle (17×5 bar) | **CHANGE — shrink and move below** | Three ticks under the ring. The 17px bar is wider than a bradley (finding 3). |
| 9 | Damage pip — infantry (6×6 dot) | **CHANGE — same treatment** | Removes it from the diamond's warm ramp, which is collision 1 in the audit. |
| 10 | Rank chevrons | **KEEP, re-anchor** | Top-right of bounds. Currently `Margin: 16,0` = 16px **left** (X is negated). |
| 11 | Cargo pips | **KEEP, re-anchor** | Above the unit — the slot the diamond vacates. Resolves audit collision 3 (they share an anchor today). |
| 12 | Ammo-empty pips | **KEEP unchanged** | Bottom, blinking. Already out of the contested zone. |
| 13 | Ammo pips (per-pool) | **KEEP unchanged** | Per rule. |
| 14 | Holding-fire pip | **KEEP unchanged** | `TopRight`. It is the shipped home for "declining a shot on purpose" and the diamond's own design record points at it (`diamond-pip-design-260903.md:131-139`). |
| 15 | Control-group number | **KEEP unchanged** | `TopLeft`, selected only. |
| 16 | Class pictogram | **KEEP, re-anchor** | Currently `Top 0,6` — **exactly overlapping** the "selected" pip (audit collision 4). Move to `0,8`. |
| 17 | "Selected" pip | **DELETE** | It is redundant with the selection box on every chassis that has one, and on infantry it exists only because the box is switched off. Its slot is the class pictogram's. One of the two must move regardless; deleting the weaker signal is cheaper than re-siting both. |
| 18 | Evacuating pip | **KEEP unchanged** | `TopRight`. |
| 19 | Ammo-replenishing pip | **KEEP unchanged** | `Bottom 0,-8`. |
| 20 | Concealment range circles | **KEEP, but see the conflict** | Selected-only ground circles. §5 records that this is the one real objection to row A. |
| 21 | Weapon range circles | **KEEP unchanged** | Selected / shift. |
| 22 | Target line | **KEEP unchanged** | Selected, on order. |
| 23 | Defense pips | **DELETE** | Dead: inherited by nothing, condition granted nowhere. Gate 1. |
| 24 | X-ray occlusion ghost | **KEEP unchanged** | The one existing precedent for modifying the sprite; row D would extend it. |

**Net: 3 deletions (suppression pip row, "selected" pip, defense pips), 7 moves, 1 mark changing kind
(diamond → ring), 13 untouched.**

---

## 3. Per-signal at-rest visibility — the table the brief asks for

The user picked all three non-permanent options and said *"keep all these doors open and ask yourself
what should be visible and when."* So this is per-signal, justified individually, not one global rule.

Ordering constraint, from the user and inverting the obvious one: **detectability most visible; health
possibly behind a modifier; stance possibly behind one too.**

| Signal | When it shows | Why this and not something else |
|---|---|---|
| **Detectability** | **Always** | The user's explicit ranking — *"Detectability most I think."* It is also the only signal that is *actionable while you are not looking at the unit*: you move a unit because it is exposed. Putting it behind any gate defeats the whole ordering. |
| **Impediment (total)** | **Always, but only when non-zero** | An undamaged, unsuppressed unit draws nothing. That is what keeps the resting screen clean, and it is most of the screen most of the time (mockup column 1). A mark that is present on every unit always is a mark that says nothing. |
| **Suppression (the decaying half)** | **Always while changing, then decays away on its own** | This is the one signal whose *own mechanic* is a timer, so "only while changing" is not a UI rule imposed on it — it is the quantity. It needs no gate because it removes itself. |
| **Health** | **On hover, on selection, or while it is changing** | Behind a gate per the user's steer, with one carve-out: a *transition* between damage bands flashes briefly even unselected, because the moment a unit crosses 50% is the moment the player must react. **Caveat: hover-only decorations are not free** — the rollover path at `SelectionDecorationsBase.cs:104-107` drives *bars*, not `IDecoration`s, so hover-gating a decoration is a small C# change. Costed in §7. |
| **Stance** | **On selection or on modifier only** | Stance is a thing *you set*, so you already know it; you need it back when auditing a group, which is when you have them selected. This also quietly fixes the gate-1 problem: a glyph the player only sees when they go looking cannot mislead a passer-by about concealment. |
| **Rank** | **Always** | Cheap, small, top-right, and it is the one purely-positive signal on the unit. No reason to hide it. |
| **Cargo** | **Always while loaded** | It changes what the unit *is*, not how it is doing. Unchanged from today. |
| **Ammo-empty** | **Always, blinking** | Unchanged. A unit that cannot shoot is an emergency. |
| **Holding fire** | **Always, 15-tick linger** | Unchanged. Its whole value is catching a moment you were not watching for. |
| **Control group** | **Selected only** | Unchanged. |
| **Concealment range circles** | **Selected only** | Unchanged. |
| **Turret lock (50%)** | **Never** | Gate 2. |

---

## 4. Which marks read the decaying quantity, and which read the total

This is the central difficulty the brief names: one number containing two things that behave
differently. The plan's answer is that **the total and the decaying half are drawn on the same mark, in
two tones, rather than as two marks.**

| Mark | Reads | Rationale |
|---|---|---|
| **The impediment arc / bar — its length** | **The TOTAL, `I = S + D`** | This is the quantity that predicts what the unit can currently do: speed, vision, burst, accuracy, turret traverse. It is what a player asking "can this unit still fight" wants. |
| **The impediment arc / bar — its leading pale segment** | **The DECAYING half, `S` alone** | So "is this unit being shot at right now" remains answerable at a glance, as the brief requires. The segment shrinks visibly as suppression decays onto the permanent floor. |
| **The permanent floor (the boundary between the two tones)** | **`D` alone, derived from HP** | The player can read repair progress off the same mark: repairing lowers the floor. |
| **Health ticks** | **HP, not impediment** | Deliberately kept separate. Impediment is *not* a health readout — [`impediment-spec.md`](impediment-spec.md) §2.5 keeps the damage pips untouched for exactly this reason, and with the health bar off they are the mod's only health indicator. |
| **The base ring colour** | **Neither — `Detectable.CurrentVisibility`** | Independent axis. |
| **Sprite desaturation (row D only)** | **The TOTAL** | Same quantity as the arc, different channel. |

**The honest weakness in this scheme**, stated plainly: the two-tone bar asks the player to read a
*boundary* rather than a *length*, and boundaries are harder to see at 3px tall than lengths are. If
one tone has to go, keep the total and lose the decaying segment — and then accept that "under fire
right now" needs its own mark somewhere else, most likely a brief flash rather than a persistent one.
I have not resolved which is better and I do not think it can be resolved without seeing it move.

---

## 5. What I rendered that changed my mind

**Row C · corner badges is unworkable for infantry, and I only found out by drawing it.**
Infantry carry `SelectionDecorations: ShowNever: true` (`infantry.yaml:56-57`) — the only such line in
the mod — so an infantryman has **no selection box at all**. A design language whose entire premise is
"pin marks to the corners of the box" has no corners to pin to on half the roster. The mockup shows
that cell **empty with the reason written in it**, rather than quietly substituting a different
treatment and letting the row look complete. That is the finding the brief said was worth more than a
complete-looking row, and it is why row C should not be chosen.

**Row A's real objection is not clutter, it is that the ground plane is already occupied.**
The concealment range circles are ground circles, selected-only, grouped by `Type: concealment`
(`infantry.yaml:832-932`). Row A puts a second ring language on the same plane. On an unselected unit
there is no conflict; on a *selected* one there are now two concentric grey/coloured rings meaning
different things. This is survivable (the range circle is 3px wide at `Alpha: 25` and very faint, and
sits cells away rather than at the unit's feet) but it is real, and it is the thing I would look at
first in a capture.

**The `♦A>` row reads as one text run.** At 1× the three glyphs sit 1px and 4px apart on a single
baseline in three warm colours, and the eye groups them as a word. This is visible in the mockup's row 0
and it is, I think, a large part of *"it blends together too much"* — the collision is as much
typographic as spatial.

---

## 6. Recommendation, and the fallback

**Recommended: Row A · base ring, with row B as the fallback.**

Row A is recommended because it is the only language that actually *empties* the contested zone above
the unit rather than tidying it, and because it puts the user's top-ranked signal (detectability) on the
largest, most-always-visible surface without adding a single floating glyph. Its cost is mostly YAML plus
one small trait for the arc.

**Row B · tight column is the fallback** and is the cheaper, duller answer: keep marks above the unit,
but collapse to exactly two and take health out of the warm ramp. If row A's ground-plane conflict turns
out to be worse in motion than it looks in a still, B is what to fall back to, and nothing in the
disposition table in §2 changes except the anchors.

**Row D · sprite-carried is the one I would most like to be right and least trust.** It matches the
user's ordering better than anything else — detectability on the whole silhouette is as visible as a
signal can get — and it satisfies *"the sprite itself may carry state"*. But tinting is precedented
(`WithColoredOverlay@EMP`, `XRayOverlayAlpha`, `WithAlphaCondition`) while **outlining is not: no outline
shader path was found in the audit and I did not audit it either** ([`indicator-audit.md`](indicator-audit.md)
gap #4). It is costed unknown-medium, and it is one step from the over-doing-it the user warned against.

---

## 7. Cost, per piece

| Piece | Cost | Basis |
|---|---|---|
| Move any mark to a new anchor | **Free — YAML** | Six anchors × signed margin. Mind the X-negation and re-derive through `DecorationRowGeometry`. |
| Delete defense pips, "selected" pip, suppression pip row | **Free — YAML** | Deletions only. |
| Retune / recolour the diamond | **Free — YAML** | Every knob is mirrored in `defaults.yaml:948-964`. |
| Stance and health behind selection | **Free — YAML** (`RequiresSelection: true`) | — |
| Health/stance behind **hover** | **Small C#** | The rollover path drives bars, not decorations (`SelectionDecorationsBase.cs:104-107`). |
| The impediment arc (row A) | **Small C#** — a new `WithDecorationBase` subclass emitting a ground-plane arc | `WithRangeCircle` is the nearest existing thing but draws a full ring at a fixed radius, not a partial arc at the unit's footprint. |
| The two-tone impediment bar (row B) | **Small C#** | One trait reading two counts. `WithSpottedDecoration extends WithTextDecoration` is the working pattern for a decoration that computes its own appearance. |
| Fade/shrink an unselected mark | **Small C#** | `UISpriteRenderable` already takes scale and alpha; `WithDecoration` just never passes them (`:106`). Cargo pips and `WithGarrisonDecoration` are the working precedents. |
| Sprite outline (row D) | **Unknown-medium** | No outline shader path audited. **This is a costing gap, not a costing.** |
| Any change to what the diamond *means* on vehicles | **Not a display change** | Mechanics, inside the standing user gate. |

---

## 8. What I deliberately left out

- **A "being shot at" mark separate from the impediment bar.** Folded into the bar as a second tone
  instead. If the two-tone boundary proves illegible (§4), this comes back as a separate flash.
- **Any mark on the 50% turret lock.** Gate 2.
- **Any redesign of the stance glyph vocabulary.** The four Latin letters are questionable
  ([`indicator-audit.md`](indicator-audit.md) Appendix A calls the vocabulary, not the implementation,
  the throwaway part) — but changing them is adjacent enough to the ambush-implication problem that I
  would rather move them than restyle them. They are `Info` string fields; changing them later is free.
- **The 12-frame `pip-visibility.shp` ladder.** It is in the tree and unreferenced, and it looks like a
  free graduated readout. It is not: [`indicator-mechanics.md`](indicator-mechanics.md) Q10 decoded it as
  one blank frame, six identical shapes differing only in hue, one white and four halo variants — a
  colour ramp, not a fill ramp. Using it would reintroduce exactly the "ten colours of one glyph"
  problem that sinks the suppression pip.
- **Re-enabling the health bar.** Explicitly a product decision the user already took.
- **Anything touching stances, ambush, concealment or cover mechanically.** Standing gate,
  `PIPELINE.md:580`.

---

## 9. Where I am guessing

1. **Nothing has been seen in the running game.** Every position here is arithmetic over margins and
   decoded sprite sizes, composited in a browser. That is one step better than the two audits (which are
   arithmetic alone) and one step worse than a screenshot. The mockup is a *prediction* of the frame.
2. **The terrain is a real tile, but the palette under it is a proxy.** The ground in the mockup is
   `clear1.tem` (template 255, `tilesets/temperat.yaml:100`), decoded from the *unencrypted*
   `temperat.mix` in the local OpenRA content directory — the tile binaries are not in this repo, but
   that mix is readable, so no flat fill was needed. What remains uncertain is the **palette**: the
   canonical `temperat.pal` is inside a Blowfish-encrypted `local.mix`, so terrain is rendered through
   `plains.pal`. The chernobyl `temperat.pal` is a deliberately darkened wasteland palette and reads
   too dark; the true in-game ground is somewhere between the two. Since the question these marks have
   to survive is *contrast against ground*, a systematic shift in ground brightness is the one proxy
   error that could change a verdict. **Judge contrast in the capture (§10), not here.**
3. **The distribution question is still open and I depend on it.** [`impediment-spec.md`](impediment-spec.md)
   §5.3: nobody has measured how often a unit sits in each decile. **The part of this plan that depends
   on it is the arc's resolution** — how many visually distinct steps the arc should have. If units
   cluster at 0–20 and 80–100 with an empty middle, a continuous arc is the wrong shape and banded steps
   are right. I have drawn it continuous because that is what the quantity is, not because the
   distribution supports it.
4. **Vehicle `k = 0.6`** ([`impediment-spec.md`](impediment-spec.md) §6.4, "the weakest number in this
   document") sets where the whole vehicle impediment distribution sits, and therefore how much of the
   arc a vehicle ever uses. If `k` moves, the vehicle cells in the mockup are wrong.
5. **Row A's ground-plane conflict with the range circles is judged, not tested** (§5).
6. **Z-order between overlapping marks remains undetermined** — `TraitsImplementing<IDecoration>()`
   order was not traced by the audit and not by me. Every row here reduces overlap, which reduces
   exposure to it, but none of them proves it away.
7. **The facings are derived, not guessed — but only after guessing wrong first.** Frame 0 is NORTH and
   the index advances **counter-clockwise** (`WVec.cs:66-76` defines north as −y), so south-east on a
   32-facing classic ring is hull frame **19** with turret frame **51**; infantry `stand` is 8 facings
   and south-east is frame **5**. I originally picked frame 12 off a contact sheet by eye and it was
   wrong. Two related traps corrected in the same pass, both of which would have put fake pixels in
   front of the user: the Abrams draws **`abrams-correction.shp`**, not `abrams.shp` (the sequence node
   sets `idle: abrams-correction`), and the turret is a **separate frame ring** — a tank rendered from
   the hull ring alone is missing its gun. ⚠️ **Incidental bug found on the way:** `bradley`'s sequence
   is `idle: 1tnk`, and `1tnk.shp` is absent from this repo, so the actor has no drawable sprite.
   Filed separately; it is not an indicator problem.

---

## 10. The capture that would settle the most

**Not run — the brief withholds launching. Handed up as a request.**

One screenshot, at default zoom **and** one zoomed well out, of a mixed group on **grass and on dirt in
the same frame**: an undamaged stationary infantry squad; the same squad at ~40% health; a stationary
vehicle; a moving vehicle; one unit in Ambush and one in Hunt; one unit selected; one transport loaded.

It answers, in a single frame, the four things arithmetic and a flat-fill mockup cannot:
whether the 6×9 diamond is legible at 10px against textured ground; whether `ECC73C` / `F0B232` /
`F09425` are separable from each other and from the damage ramp; whether the 1px stance clearance holds
once FreeType has hinted the glyphs; and whether the marks read as belonging to their unit in a crowd,
which is the failure the 13/21→8/16 re-margining in `6cb66e28` was already once trying to fix.

⚠️ `test-visual-gauge-truth` and `test-visual-concealment-gauge` **should not** be used for this
without fixing their calibration first — both omit `prone`, which is granted on `!moving`, so both are
one tier low (`discovered.md:1008-1032`).
