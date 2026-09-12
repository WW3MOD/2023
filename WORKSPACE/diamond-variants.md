# One diamond, two channels — fourteen variants

**Ref: `main @ b1b90c4d`**, worktree `wt/diamond-variants`, branch `wt/diamond-variants`.
**Design + mockup only.** No rules YAML edited, no engine C# edited. Nothing launched, no screenshot
captured, no `--check-yaml`, no `make test` — all withheld from this job by the brief.

Artifact: [`WORKSPACE/mockups/diamond-variants.html`](mockups/diamond-variants.html), built by
[`diamond_variants_assets.py`](mockups/diamond_variants_assets.py). Evidence for every level count:
[`mockups/assets/_diamond-ladders.png`](mockups/assets/_diamond-ladders.png), built by
[`diamond_ladder_sheet.py`](mockups/diamond_ladder_sheet.py).

This replaces the layout half of [`indicator-layout-plan.md`](indicator-layout-plan.md), which now
carries a dated superseding banner naming exactly which of its sections survive.

---

## 1. The seam the whole request sits on

**Colour is free on text and fill is impossible on it. Fill is free on a sprite and colour is baked
into it.** The user's proposal — one glyph carrying colour *and* fill degree — crosses that seam, and
that single fact determines the cost of every variant here.

- The shipped diamond is **text**: `WithSpottedDecoration extends WithTextDecoration`, drawn through
  `UITextRenderable` in arbitrary RGB. That is why it can carry five authored colours for free. It is
  also why it **cannot be partially filled** — the shape is a font glyph and there is no clip path.
- A **sprite** decoration can be any shape, including a partial fill, but takes its colours from the
  palette baked into the SHP frame. Arbitrary RGB is not available.

So every variant on this page is a **sprite**, and its cost is: **one small C# trait that picks a
sequence each frame, plus `fill_steps × colour_bands` baked SHP frames.** The trait is the shape
`WithSpottedDecoration` already is — it computes its own appearance per frame — so this is a known
pattern, not new rendering work. `pip-suppression.shp` already ships ten baked colour frames, so the
art idiom exists too.

Three consequences worth stating:

1. **The frame count is the design budget, and it is a product.** 4 × 4 is sixteen frames and fine.
   5 × 10 is fifty, which is what row X costs — and row X resolves sixteen states. Counting frames is
   the same act as counting levels.
2. **The font constraint disappears.** `U+25C6`/`U+25C7` are absent from `FreeSansBold.ttf` and the
   mark uses `U+25CA`/`U+2666` because they are the only hollow/solid pair the font carries
   (`WithSpottedDecoration.cs:84-91`, proved in `diamond-pip-font-proof.html`). **Leaving the text
   path retires that constraint entirely** — a sprite diamond can have any silhouette, which is what
   makes rows V and W possible at all.
3. **The mark moves up about 5 px at the same authored margin.** Sprites centre on their origin
   (`WithDecoration.cs:106`); text ink hangs `GlyphFontSize / 2` = 5 px *below* it
   (`DecorationRowGeometry.cs:48-54`). Whoever implements this re-derives the margin through
   `DecorationRowGeometry`, and does not copy `0,-10` across.

---

## 2. What is real, and what is a document

| Channel | Status | Evidence |
|---|---|---|
| **Detection** | **Shipped and graded today.** `Detectable.CurrentVisibility` is live, `[Sync]`, recomputed every tick; `Detectability.Grade` inverts concealment into exposure and cuts five bands. | `DetectabilityGrade.cs:64-96`, `Detectable.cs:118-138` |
| **Impediment** | **Does not exist. Not one line is built.** Specified as `I = clamp(0,100, S+D)`; the spec's own staging plan lists the engine work as unstarted — a decay-exempt `ExternalCondition` source (~15 lines) and a new `GrantImpedimentFromHealth` trait (~60 lines). | [`impediment-spec.md`](impediment-spec.md) §1.1, §1.4, §7 |

Every impediment value in the mockup is a number I chose to illustrate an encoding. The standing rule
(manager decision 08) is that you never draw a mark as a substitute for a mechanic that does not work.
This page is design exploration *ahead of* the mechanic rather than *instead of* it — but the
asymmetry is load-bearing and is stated on the page itself: **if any of these shipped before impediment
did, the colour channel would be reporting a quantity the simulation cannot supply.** Detection alone
is drawable today, which is the argument for row H.

**One more asymmetry, already known and still true:** a vehicle's concealment spans 3 of 9 levels and
it is *permanently solid* (`indicator-audit.md` §3.4), so four of the five detection grades never fire
on a vehicle. **Row F — binary fill — is the only row on the page that is honest on a vehicle today.**

---

## 3. The variants, and what each resolves

"Resolves" is what I could tell apart at **1×** in `_diamond-ladders.png`. Claimed is
fill steps × colour bands.

| | Encoding | Claimed | Resolves | Frames | Verdict |
|---|---|---|---|---|---|
| **X** | 10 hues × 5 fills | 50 | **~16** | 50 | **The control, drawn to fail.** Ten hues read as four. |
| **A** | bottom-up · 4 bands | 20 | ~16 | 20 | ⚠ merges High into Spotted |
| **B** | bottom-up · 3 bands | 12 | **12** | 12 | Claim = reality |
| **C** | centre-out · 4 bands | 16 | ~12 | 16 | Strongest first step, weakest last |
| **D** | hollow → ring → solid | 12 | **12** | 12 | Most robust at 1×; merge is deliberate |
| **E** | 9×13 · 5 bands | 25 | **~20** | 25 | Most states; ⚠ collides with the stance glyphs |
| **F** | binary fill · 4 bands | 8 | **8** | 8 | The only row honest on a vehicle |
| **G** | top-down · 4 bands | 16 | ~16 | 16 | A's pixels, opposite metaphor |
| **H** | inverted: colour = detection | 20 | ~16 | 20 | Puts the channel that exists on colour |
| **I** | lightness, not hue | 16 | ~16 | 16 | Legible, but no intrinsic bad end |
| **J** | A + outline weight | 32 | ~16 **+ 0** | 32 | The third channel resolves nothing |
| **K** | A, absent when unseen | 16 | ~16 | 16 | Cleanest screen; absence is ambiguous |
| **V** | split, two fills, no colour | 16 | ~9 | 16 | ⚠ unreadable at 1×; works from 2× |
| **W** | erosion, no colour | 16 | ~12 | 16 | Most legible single channel here |

**Shortlist, if one is wanted: D for robustness, E for capacity, F for honesty on vehicles.**
I am not choosing between them — that is the user's call and it turns on how much of the screen budget
the mark is allowed, which is not a question the pixels answer.

---

## 4. Four things the rendering settled that I had wrong

Corrections to my own captions, made after looking at the contact sheet. **Five of the fourteen
"resolves" figures moved after drawing, four of them upward.**

1. **Five detection grades do not fit four fill steps.** Any 4-step variant must merge two of
   Concealed / Low / Moderate / High / Spotted, and *which pair merges is a design decision nobody has
   taken.* Only D is honest about it, because it merges to three deliberately. Elsewhere the merge is
   an artefact of rounding — and in **A it lands on the worst possible pair, High with Spotted**, where
   Spotted is the only enemy-derived state on the scale.
2. **The fifth fill step never arrives, at any glyph size.** I expected E's 9×13 body to buy it. It
   does not: the rows a fifth step would use are the tip rows, and tips are thin at every size (at 9×13
   the top three rows are 1, 3 and 5 px — 9 px of 69). What the bigger glyph bought instead was **a
   fifth colour band.** *Fill is bounded by the silhouette and does not improve with area; hue is
   bounded by ink area and does.* The two channels have opposite appetites for size, which is not
   something I would have predicted from arithmetic.
3. **"Hollow" has less range than "full".** Every mark needs a 1 px dark keyline to survive textured
   ground — the sprite equivalent of `DrawTextWithContrast` — and on a 34 px glyph the ring plus
   keyline are most of the ink. An empty diamond reads as *a dark blob with a coloured rim*. The bottom
   of the fill ladder is compressed on every row.
4. ⚠ **At 6×9 my diamond does not read as a diamond** — drawn as a solid shape with a keyline it is a
   rounded octagon; only E's 9×13 body has an unmistakable diamond silhouette. **This may be my
   rasteriser rather than the size**: the shipped mark is the font glyph `◊`, whose tips are thinner
   than a filled shape can have, and FreeType's hinting is not reproduced here. It is the one shape
   claim on the page I would not defend without a capture.

---

## 5. What "the diamond only" displaces — surfaced, not decided

Fourteen other marks are drawn over a unit today ([`indicator-audit.md`](indicator-audit.md) §1). A
single-glyph ruling does not fold them in; it evicts them. **No row in the mockup draws any of them**,
which makes each row a fair test of the diamond and an unfair picture of the finished screen.

| Folds into the diamond | Does not, and needs a ruling |
|---|---|
| **Suppression pips** (10 tiers) — the decaying half of impediment by construction, and the original ten-colours-of-one-glyph problem. | **Stance glyphs `X A H >`** — four discrete values on a third axis. Also the marks row E collides with. **This is the likeliest reason to want a second mark at all.** |
| **Damage pip** — *partly.* The permanent floor `D = k × (100 − HP%)` is literally derived from HP. But impediment ≠ health, and with the health bar off these pips are the mod's **only** health readout. | **Cargo pips** — say what the unit *is*. Share the diamond's anchor today. **Ammo-empty** — an emergency, not a degree. **Rank** — the one purely positive signal. **Holding-fire / evacuating / replenishing** — discrete events. |

**The open question for the user, stated as a question:** the stance glyphs are the one thing that
genuinely cannot fold in. Either they go (and the player loses the readout of a setting they chose),
or "the diamond only" means "one *state* mark only" and stance survives beside it. **I have not decided
this and no variant assumes an answer** — but if the answer is that stance survives, row E's 9×13 body
is ruled out on clearance grounds (`defaults.yaml:941-943` records ~1 px of headroom), and that
changes the shortlist.

---

## 6. Where I am guessing

1. **Nothing has been seen in the running game.** Every level count is my eye on a browser composite.
   The counts are reproducible — regenerate the contact sheet and look — but they are judgement, not
   measurement, and a different pair of eyes will move some by one.
2. **The terrain palette is a proxy.** `temperat.pal` is inside a Blowfish-encrypted `local.mix`, so
   ground renders through `plains.pal`; true in-game ground is somewhat more olive. Since the question
   these marks must survive is **contrast against ground**, every contrast verdict here is provisional.
   Decoration hues themselves are exact — that proxy was verified against four independently stated
   values — but the ground under them is not.
3. **The 6×9 silhouette claim** (§4.4) may be an artefact of my rasteriser rather than of the size.
4. **Anything that moves cannot be settled here.** A pulse on Spotted, a blink while suppression
   decays, a flash on a band crossing — all plausible, none drawable in a still. I have deliberately
   proposed none, because proposing one I cannot show is how a mockup acquires a claim nobody checked.
5. **The distribution is still unmeasured** — `impediment-spec.md` §5.3 records that nobody knows how
   often a unit sits in each decile, and it is the evidence whose absence caused the original failure.
   **Every count in §3 is an upper bound on legibility, not a statement that the levels will be used.**
   A four-band encoding is worthless if 90% of units sit in band one.

---

## 7. The one capture that would settle the most

**Not run — the brief withholds launching. Handed up as a request.**

```
./tools/autotest/run-test.sh --hidden <a scenario with mixed infantry and vehicles>
```

What would count as the answer: one frame at default zoom **and** one zoomed well out, containing an
undamaged stationary infantry squad, the same squad at ~40% health, a stationary vehicle and a moving
vehicle, on **grass and dirt in the same frame**. It settles the three things this page cannot:
whether a 6×9 sprite diamond reads as a diamond at all (§4.4); whether the keyline is enough contrast
against real ground rather than a proxy palette (§6.2); and whether four colour bands stay four once
the ground under them varies.

⚠ `test-visual-gauge-truth` and `test-visual-concealment-gauge` **should not** be used without fixing
their calibration first — both omit `prone`, which is granted on `!moving`, so both are one tier low
(`discovered.md:1008-1032`).
