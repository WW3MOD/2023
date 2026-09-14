# The true shipped glyph, adapted to carry two channels

**Ref: `main @ dcae209f`**, worktree `wt/diamond-true-glyph`, branch `wt/diamond-true-glyph`.
`git status -sb` clean against `origin/main`, `git rev-list --count HEAD..@{u}` = 0 at branch time.
**Design + mockup only.** No rules YAML edited, no engine C# edited. Nothing launched, no screenshot
captured, no `--check-yaml`, no `make test` — all withheld from this job by the brief.

Artifact: [`WORKSPACE/mockups/diamond-true-glyph.html`](mockups/diamond-true-glyph.html), built by
[`diamond_true_glyph_assets.py`](mockups/diamond_true_glyph_assets.py). Glyph bitmaps and coverage
bytes: [`glyph_probe.py`](mockups/glyph_probe.py). Evidence for every level count:
[`mockups/assets/_true-glyph-proof.png`](mockups/assets/_true-glyph-proof.png) and
[`_true-glyph-ladders.png`](mockups/assets/_true-glyph-ladders.png), built by
[`diamond_true_glyph_sheet.py`](mockups/diamond_true_glyph_sheet.py).

This supersedes **§3 only** of [`diamond-variants.md`](diamond-variants.md), which now carries a dated
banner. Its §1, §2 and §4 survive, one item of §4 corrected.

---

## 1. The headline: the true glyph survives being filled, and the silhouette was never the problem

**Yes — `U+2666` fills cleanly at 6×9 and keeps its diamond identity from full down to roughly the
halfway step.** No variant on the page had to widen a tip to make the fill work.

But the premise the job was dispatched on is wrong, and that is the more useful finding. The brief
held that the previous attempt drew a different silhouette. **It did not.** Its hand-rasterised L1 ball
and the shipped `U+2666` have the byte-identical row profile and the identical ink count:

```
previous hand-rasterised 6×9 : [2, 2, 4, 6, 6, 6, 4, 2, 2]  ink = 34 px
SHIPPED U+2666          6×9 : [2, 2, 4, 6, 6, 6, 4, 2, 2]  ink = 34 px
SHIPPED U+25CA          6×9 : [2, 3, 5, 4, 5, 4, 4, 3, 2]  ink = 32 px
```

The footprint was already exactly right. **What differs is the ink treatment**, in two ways that
compound:

1. **Coverage.** `FT_LOAD_RENDER` (`FreeTypeFont.cs:85`) returns an 8-bit *antialiased* bitmap, and
   `SpriteFont.cs:285-293` copies that coverage byte into R, G, B **and** A. Of the shipped ♦'s 34
   inked pixels **only 16 reach ≥78% coverage**; the tip pixels run as low as 13/255. The previous
   pass drew every mask pixel at **flat alpha 255**. The fade at the points *is* what reads as a
   point — remove it and 34 px of full-opacity ink is a lozenge-shaped blob.
2. **Contrast.** The previous pass added a hard opaque `rgba(0,0,0,200)` keyline on all eight
   neighbours. **The engine does not do that.** `UITextRenderable.Render` (`:57`) calls
   `DrawTextWithContrast(…, 1)`, which draws a **greyscale dilation** of the glyph's own alpha through
   `CreateCircularWeightMap(1)` — a soft halo, corner weight **0.60**, one pixel up-left
   (`SpriteFont.cs:302-417`, `:87`). A constant-alpha ring thickens every edge equally; a dilation of a
   soft edge stays soft where the glyph is soft.

So `diamond-variants.md` §4.4 — *"at 6×9 my diamond does not read as a diamond … this may be my
rasteriser rather than the size"* — was right to suspect the rasteriser and wrong about which part of
it. **The size was fine and the shape was fine. The opacity and the keyline were not.**

**What does not survive is the empty end of the ladder.** At fill step 0 a masked ♦ is a dark lump
inside a black halo with no diamond identity at all — visible in rows T1, T4, T6, T7 and T8 of the
proof sheet. **Row T3 is the only one that solves it, and it solves it by doing what the game already
does: using the real `U+25CA` lozenge for the bottom of the ladder.**

---

## 2. The two glyphs are not one shape filled and unfilled

Nobody had checked this. **They are different outlines.** Both rasterise to 6×9, but **3 pixels of the
lozenge lie outside the diamond suit**: `U+25CA` is a thin-stroked rotated square that bulges wider at
the waist rows, `U+2666` is a solid body with 2 px tips. They are characters from different Unicode
blocks, chosen for availability rather than for being a pair.

**"The same shape as the current one" is therefore ambiguous, and I resolved it explicitly rather than
averaging: the silhouette everywhere on the page is `U+2666`.** Three reasons, in weight order:

1. It is the only one of the two with an interior for a fill to march up.
2. It is **already the shipped silhouette for three of the five grades** — `SolidFromGrade: Moderate`
   (`defaults.yaml:958`) means Moderate, High and Spotted all draw ♦ today; only Concealed and Low
   draw ◊.
3. The lozenge's 1–2 px stroke is almost entirely partial coverage — **only 4 of its 32 pixels reach
   ≥78%** — so "filling inside it" means inventing interior pixels it does not have, which is drawing
   a new shape, which is the thing this job exists to stop.

The lozenge is not discarded. **Row T3 keeps it as the unfilled state, exactly as it ships**, and it
is the row I would take forward on that basis.

**This may well be the second half of why the current mark reads inconsistently**, as the brief
suspected: the mark does not fill and unfill, it *swaps between two differently-shaped characters* at
the Moderate boundary. That is a silhouette change mid-scale, not a fill change.

### A PITFALL re-confirmed, and a trap in confirming it

`WithSpottedDecoration.cs:84-91` says `U+25C6`/`U+25C7` are absent from `FreeSansBold.ttf`. **They
are, and the comment is correct.** But the check has to be against `.notdef`, not against "did
anything rasterise": FreeSansBold's `.notdef` is a **3×7 hollow rectangle carrying 16 px of ink**, so
an unmapped codepoint renders *something*. Both diamonds come out **byte-identical to `U+E000`,
`U+0870` and `U+4E00`** — all `.notdef`.

My first probe asked "did anything rasterise?", got 3×7 for both, and briefly concluded a curated
comment was wrong. It was not. `glyph_probe.py` now carries the check the right way round, with the
reason in a comment, because the wrong version of this test is the natural one to write.

---

## 3. The variants, and what each resolves

Eight, all preserving the shipped silhouette. **"Resolves" is what I could tell apart at 1×** in
`_true-glyph-ladders.png` and `_true-glyph-proof.png` — read off the render, not computed.

| | Encoding | Halo | Claimed | Resolves | Verdict |
|---|---|---|---|---|---|
| **T1** | fill=detection (4) · colour=impediment (4) | engine, `-1,-1` | 16 | **16** | The faithful reference. Step 0 is a dark lump with no diamond identity. |
| **T2** | fill=impediment (4) · **colour=detection (5)** | engine, `-1,-1` | 20 | **12** | ⭐ **The only variant fully drawable today.** Five colours at 6×; yellow/amber/orange collapse to two at 1×. |
| **T3** | fill=detection (4) · colour=impediment (4), **`U+25CA` at step 0** | engine, `-1,-1` | 16 | **16** | ⭐ **Best low end on the page.** The one row whose empty state is still a diamond. |
| **T4** | T1 with **no halo** | none | 16 | **12** | Step 0 nearly invisible on grass. **Settles it: the halo is load-bearing.** |
| **T5** | T1, halo displaced **down-right** | `+1,+1` | 16 | 16 | Reads as a black slab *beside* the mark, not a shadow under it. Rejected on looking. |
| **T6** | T1, halo in the **mark's own hue** | own hue | 16 | 16 | Hue survives at the silhouette edge where black throws it away. The runner-up keyline. |
| **T7** | fill=detection (**2**) · colour=impediment (4) | engine, `-1,-1` | 8 | **8** | Claim equals reality. **The only row honest on a vehicle.** |
| **T8** | fill=detection (5) · colour=impediment (5), **no merge** | engine, `-1,-1` | 25 | **16** | Both fifth steps collapse — see §5. |

**Shortlist: T3 for the silhouette, T2 for honesty about what exists today, T7 for honesty on a
vehicle.** I am not choosing between them; that turns on whether impediment is going to be built,
which is not a question the pixels answer.

---

## 4. The keyline, treated as a variable and solved

The brief asked for this not to be inherited as a constant. Four treatments were drawn (T1, T4, T5,
T6) and the render decides between them:

- **The engine's own dilation (T1/T2/T3/T7/T8) is the right default.** It is what ships, it is soft
  where the glyph is soft, and porting it removes a whole class of "my keyline vs their contrast"
  error. `circular_weight_map(1)` is asserted against the values `SpriteFont.cs:308-312` documents, so
  a mistranscription cannot pass silently.
- **No halo (T4) fails**, and fails *specifically at low fill*. At full fill T4 reads fine; at step 0
  and step 1 the mark nearly disappears into grass. **The halo earns its darkening at the empty end of
  the ladder, which is exactly where the previous pass complained about compression.**
- **The down-right shadow (T5) fails on looking.** Displacing the dilation instead of centring it puts
  a hard black mass to one side; it reads as two objects.
- **Own-hue (T6) is the viable alternative.** It keeps the band colour legible at the edge, where a
  black ring throws it away. It is slightly weaker against dark ground, which is the trade.

---

## 5. Two findings from last round, re-tested on the true glyph

Both hold, and one is now stronger.

1. **Five detection grades still do not fit four fill steps.** Where a 4-step fill carries detection I
   merge **Low into Moderate** — two adjacent, posture-only grades. **I deliberately did not merge
   High with Spotted**: Spotted is the only enemy-derived grade on the scale and the one state players
   already recognise. The merge is declared on the page and in the code (`DET_MERGE_4`), not left to
   rounding.
2. **The fifth fill step still never arrives — now confirmed on the real glyph.** T8 exists to test
   it. The ink-fraction ladder picks rows `[0, 4, 5, 6, 9]`, so the extra step is a **one-row**
   difference (14 px → 20 px → 26 px of 34), and at 1× it collapses. The previous round attributed
   this to tips being thin at every size; on the true glyph the cause is the same and the outcome is
   identical. **T8's fifth *colour* band collapses too**, so a 5×5 claim renders as 16.
3. **On vehicles the quantity underneath is degenerate**, unchanged: concealment spans 3 of 9 levels
   and is permanently solid, so four of five grades never fire. **T7 is the only honest encoding
   there.** Noted, not drawn around — no silhouette can fix a flat input.

---

## 6. At rest and on Shift — shown, not decided

Per the user ruling of 2026-09-14, the page shows both columns: a diamond-only at-rest scene and a
Shift-held scene with the damage pip, suppression pips, rank chevron and **both stance glyphs drawn
exactly as they ship** — same glyphs, same colours, same font, not restyled, because the ruling
explicitly defers their redesign. The eviction question is therefore visibly answered: **nothing is
evicted.**

**No cost is quoted for the Shift reveal, and this is deliberate.** The layout plan priced "behind
selection" as free YAML via `RequiresSelection: true`, but a held modifier is a different mechanism
and the input path has not been read. **Whether this is YAML, a small trait, or input-layer work is
unscoped.**

---

## 7. Which channel is real, labelled on the page itself

| Channel | Status |
|---|---|
| **Detection** | **Shipped and graded today.** `Detectable.CurrentVisibility` is live, `[Sync]`, recomputed every tick; `Detectability.Grade` cuts five bands. |
| **Impediment** | **Does not exist. Not one line is built** — `impediment-spec.md` §1.1 specifies `I = clamp(0,100, S+D)`; §1.4 and §7 list the engine work as unstarted. |

Every impediment value on the page is a number chosen to illustrate an encoding, and the page says so
in a banner. Manager decision 08 forbids drawing a mark as a substitute for a mechanic that does not
work; this is exploration *ahead of* the mechanic, which is allowed **only** while it says so. **T2 is
the one variant that would be honest if it shipped tomorrow.**

---

## 8. Where I am guessing

1. **Nothing has been seen in the running game.** Every level count is my eye on a contact sheet.
2. **The ground is a real tile through a proxy palette.** The tile is the genuine `clear1.tem` (found
   this pass in an unencrypted `temperat.mix`); the palette is still `plains.pal`, because
   `temperat.pal` lives in a Blowfish-encrypted `local.mix`. True ground is more olive. **Every
   contrast verdict is provisional.**
3. **PIL ships its own FreeType.** Hinting matches in kind (`FT_LOAD_DEFAULT` both sides) and the ink
   box comes out 6×9 — the size the tree reports independently — but a hinted stem could in principle
   land a pixel differently from the game's `freetype6`.
4. **`deviceScale` is assumed 1.0.** Every number in `DecorationRowGeometry` assumes it — `GlyphFontSize`
   is a bare 10 with no scale term. At non-100% UI scale the glyph is re-rasterised and these bitmaps
   are not what is drawn. **I did not read where UI scale is set or what its default is.**
5. **`GetContrastColor` picking `000000` is DERIVED, not observed.** All five shipped colours have HSL
   lightness 0.52–0.62, comfortably over the 0.33 threshold at `SpriteFont.cs:421`, so every grade
   takes `TextContrastColorDark`. I did not confirm `Color.GetBrightness` is HSL lightness rather than
   luma; at these values either reading gives the same answer.
6. **Nothing that moves can be settled in a still.** No pulse, blink or flash is proposed, because
   proposing one I cannot show is how a mockup acquires a claim nobody checked.

---

## 9. The one capture that would settle the most

**Not run — the brief withholds launching. Handed up as a request.**

```
./tools/autotest/run-test.sh --hidden <a scenario with mixed infantry and vehicles>
```

What would count as the answer: one frame at default zoom **and** one zoomed well out, containing a
stationary infantry squad, a stationary vehicle and a moving vehicle, on **grass and dirt in the same
frame**. It settles the three things this page cannot: whether the true glyph's antialiased tips
survive the real renderer at real zoom; whether the dilated halo is enough contrast against real
ground rather than a proxy palette; and whether four colour bands stay four once the ground varies.

⚠ `test-visual-gauge-truth` and `test-visual-concealment-gauge` **should not** be used without fixing
their calibration first — both omit `prone`, which is granted on `!moving`, so both read one tier low
(`discovered.md:1008-1032`).

**No YAML was touched, so there is nothing for `--check-yaml` to say about this branch.** The only
files added are three Python scripts, one HTML page, two PNG contact sheets and two Markdown files.
