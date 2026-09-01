# Milestone — evacuation, crew, fog and minimap feedback (items 1–6)

**Stamp: `main @ 9ef205c5`, 2026-09-01.** All six are merged and pushed.

This note exists because the previous session refused to write it. At that point four of the six
had never had the game launched at them, and "milestone reached" over a build-verified code read
would have been a claim nobody had earned. They have now been looked at. What follows separates
what was **seen on screen** from what was only **read in code**, and does not smooth the two
together.

**Verification key — this is the point of the document, not decoration:**

| | Meaning |
|---|---|
| **SEEN** | A frame was captured from a running game and read. Pixels, not inference. |
| **ASSERTED** | The engine's own state was queried in a scripted run and graded. Strong for geometry, blind to rendering. |
| **READ** | Code reading, clean build, green NUnit. **Nobody has watched it happen.** |

---

## The one thing that changed since the handoff

**Item 5 shipped at `FogDarkness: 1.85`. It is now `1.4`** (`f16cdb66`,
`mods/ww3mod/rules/world.yaml:243`). The instrument confirmed 1.85 was genuinely in force and
behaving exactly as modelled — and that same measurement is what condemned it. At 1.85 fogged
ground renders at ~6.4% of lit brightness, which on screen was indistinguishable from unexplored
shroud, defeating the entire point of the setting. If you were expecting the number in the handoff
table, that is why it moved.

---

## Item 1 — the queued evacuation line — **SEEN, with one open thread that is almost certainly the test's fault**

**Was:** `RotateToEdge` resolved its destination in `OnFirstRun`, which does not run until the
activity becomes current. `edgeCell` is the only input to `TargetLineNodes`, so a *queued*
evacuation had no destination and drew no line at all until the unit reached the waypoint before
it. That is the reported "it only shows up at the last waypoint".

**Now:** resolution moved into a pure static called from both constructors (`94ca9e0f`).

**What you will see:** select a unit, shift-click three or four waypoints, then shift-E. The
evacuation leg — amber/gold, distinct from the white move legs — draws all the way to the map edge
**immediately**, while the unit is still crossing its first leg. Before, that stretch of the frame
was empty.

**Verification — SEEN.** Run `260901_232601_p30661`, seed `-1457687476`. The node chain was
`[18,16  28,16  38,16  1,13]` — four nodes, the fourth an edge cell, captured while the tank still
stood at `9,16` on leg one. That is the feature, and it is the thing that could not happen before.

I also read the capture's pixels rather than trusting the verdict line, because the rendering half
of this scenario is graded by nothing — `4a2844e7` is explicit that a PASS certifies the node chain
and **not** that the line renders. It renders:

- A single continuous leg runs from the west boundary east-south-east across the frame.
- Its west terminus carries a node marker measuring **(192,155,69)** against **(192,157,68)**
  predicted for the evac colour `ARGB 180,255,200,80` composited over the terrain measured beside
  it. It is the amber leg, not a white move leg.
- Its slope is **+0.082** at a cell pitch of 48 device px — i.e. three rows over thirty-seven
  columns. The geometry on screen independently says the destination is `1,13`.

**The open thread, and why it is not a product defect.** The run is graded FAIL because the edge
node landed at `1,13` where the scenario predicted `1,16`. The scenario's arithmetic assumed a
unique nearest perimeter cell. It is not unique: `CVec.Length` is an **integer** square root
(`CVec.cs:50`), so from `8,16` every west-edge cell from `1,13` to `1,19` has length exactly 7.
`ChooseClosestMatchingEdgeCell` is `OrderBy(...).FirstOrDefault(match)` (`Map.cs:1867-1869`),
`OrderBy` is a stable sort, and `UpdateEdgeCells` walks the west edge from `Bounds.Top` downward
(`Map.cs:1943-1947`). So the first cell in the tie is `1,13`, and `1,13` is what the engine is
supposed to return.

**Read that way, the run is a pass with a wrong yardstick.** The discriminator this scenario was
built around still resolved correctly: a destination committed at issue time points **west**, one
committed on arrival would have pointed **north** to `38,1`. It pointed west. The scenario's
expected cell needs correcting to `1,13`; no engine change is indicated.

---

## Item 2 — crew auto-evacuate on eject — **ASSERTED**

**Was:** ejected crew stood by the wreck. **Now:** `VehicleCrewInfo.AutoEvacuateOnEject`
(default **true**) queues a one-shot evacuation at spawn (`3ce18d71`).

**What you will see:** when a vehicle is destroyed, the surviving crew walk themselves off the map
instead of milling around the hull waiting to be shot.

**Verification — ASSERTED.** `test-crew-auto-evacuate` **PASS**, run `260901_212638_p19384`. This
scenario was RED in the previous session; the staging fix (`0b630f0c`) that the handoff flagged as
unverified is now verified, and the one-shot semantics are sound. The handoff's contingency — put
`AutoEvacuateOnEject` back to default-false — is **not** needed.

No frame was read for this one. The assertion covers where the crew go, which is the whole claim.

---

## Item 3 — rear dismount and fan-out — **ASSERTED**

**Was:** ejection direction was `w.SharedRandom.Next(8)` with no reference to hull facing, so
roughly three crew in eight walked out through the front armour. **Now:** a pure
`DismountGeometry` ranks exit cells rear-first and fans within ±90° of astern, wired into all
three dismount paths (`3ce18d71`).

**What you will see:** crew appear behind the hull, spread rather than stacked, and stop stepping
out into whatever was shooting at them.

**Verification — ASSERTED.** `test-crew-rear-dismount` **PASS**, run `260901_213127_p20400`. Also
previously RED for staging reasons, now genuinely green.

**Carry this one into your next benchmark:** three `SharedRandom.Next` calls became deterministic
fan indices, so the shared RNG stream shifts. Replays and benchmark runs diverge from anything
recorded before `3ce18d71` for that reason alone. `@stable` bots now self-evacuate crew too — an
intended improvement, but the baseline must be re-taken knowingly.

---

## Item 4 — evacuation refund indicator — **SEEN**

**Was:** the refund text was suppressed for every evacuation that **succeeded**. Fog and shroud
both answer "hidden" for out-of-bounds positions (`MapLayers.cs:504-505`, `:576-577`), and a
completed evacuation always ends out of bounds — so the indicator was reliably invisible in exactly
the case it was written for.

**Now:** the position is clamped into `Map.Bounds`, the visibility gate is bypassed, and the rise
is lengthened 1.8 s → ~4.5 s (`adfb0f2f`, merged `94ca9e0f`).

**What you will see:** evacuate a unit and `+$2500` floats up just inside the map boundary, legible
for about four and a half seconds. Evacuate a nearly-dead one and it reads `+$0` rather than
nothing at all.

**Verification — SEEN.** Run `260901_225727_p29763`. Both ticks render: `+$2500` at x 943–1007 /
y 309–326 and `+$0` at x 960–991 / y 895–912, both colour `(68,136,255)`, both on the clamped
column x≈976 where unclamped would have put them at 784. So the clamp holds, the visibility bypass
holds under a live `RenderPlayer`, and the zero-refund arm draws.

This one was first reported as **not rendering at all**, twice, and that was wrong both times. The
cause was two clocks — the scenario counted its own poll iterations from 1 while the screenshot
stamped `World.WorldTick`, putting every reported sale 28 ticks adrift and making a live text look
expired. Both are now on `DateTime.GameTime` and the verdict states each text's age at the shutter
outright (`3c751652`).

**Left open on purpose:** whether a zero refund should display at all is a design question, not a
bug. It currently does.

---

## Item 5 — fog darkness — **SEEN**

**Was:** a hardcoded per-layer vertex alpha in `ShroudRenderer.Alpha()`. **Now:** a `FogDarkness`
Info field, default `1f` = engine baseline, with the mod's value in
`mods/ww3mod/rules/world.yaml:243` (`1250d51a`, retuned `f16cdb66`).

**What you will see:** fogged ground is markedly darker than before — about **15%** of lit
brightness against ~30% at baseline — while still legible as terrain shape rather than a black
hole. Fully visible ground draws no fog layer at all and is untouched.

**Verification — SEEN, and the setting was changed as a result.** Measured on river-zeta-ww3 with
`Test.KeepRenderPlayer=true`, Starting Units "None" and Explored Map on, which makes the entire map
explored-but-fogged with no lit patches to dodge. A linearised transmission ladder matched the
prediction at mean error **0.045**, against **0.215** for the baseline of 1 — the model is right and
the field is genuinely in force. Three-way A/B at 1 / 1.4 / 1.85 gave ~30% / ~15% / ~6.4%, and 1.85
was rejected on sight for reading as pure black.

**This is a tuning dial, not a mechanism.** If it is still wrong for you it is one YAML line and no
rebuild — rules load at runtime. Lower is lighter: `1.25`→~17.5%, `1`→~30%.

**The caveat that will bite first:** `^StandardVision` is a falloff, not a switch, so bands 2–9 are
your *own* sight periphery and get dimmed along with genuine fog. If your own surroundings feel
hard to read, that is this, and the fix is a lower number — not a different mechanism.

---

## Item 6 — minimap player shading — **SEEN, and the main open caveat in this milestone**

**Was:** the relationship-colour mode already shipped (Ctrl+Comma, settings checkbox). What was
missing was per-player shading — every enemy was one flat red, so four enemies were one red smear.
**Now:** `RelationshipShade` varies HSL lightness only, preserving hue and saturation exactly, so
shading can never move a player between bands (`e8398bdc`).

**What you will see:** in a game with several enemies, each draws as a distinguishable step of the
band's red rather than all sharing one.

**Verification — SEEN.** Run `260901_225047_p29299`. All five shades render, 589–620 px each. No
rainbow fallback, and no sixth red — so the mode took, and the shading is doing what it says.

### The caveat — the shades are not evenly separated, and the bottom of the ramp is where it hurts

Adjacent-pair luminance gaps measure **44.1 / 44.1 / 11.9 / 11.9**. The bottom three shades are
**3.7× less separated** than the top three, and that is arithmetic rather than bad luck. For a
fully-saturated hue, HSL lightness 0.5 is a hinge: above it a lightness step moves green and blue
(luma weights 0.7152 and 0.0722, so ≈0.787 of the step), below it only red moves (weight 0.2126).
A 0.11 step therefore yields ΔLuma ≈ 44.2 above the hinge and ≈ 11.9 below it — a ratio of 3.70,
which is what was measured. The five-shade ramp centres on 0.5, so two of its four gaps land on the
wrong side.

**What this means for you:** with a large enemy count, expect the two darkest enemies to be the
hard pair. This is open, not fixed. It is also cheap to fix if it bothers you — the ramp could bias
its centre above 0.5, or vary saturation as well as lightness, both local to
`RelationshipShade.Shade` (`engine/OpenRA.Game/Primitives/RelationshipShade.cs:39-60`).

---

## Summary

| # | Item | Verified | Open |
|---|---|---|---|
| 1 | Queued evacuation line | **SEEN** — node committed at queue time, amber leg renders | Scenario expects the wrong cell; engine looks correct |
| 2 | Crew auto-evacuate | **ASSERTED** — PASS | RNG stream shifted; re-take benchmark baseline |
| 3 | Rear dismount + fan-out | **ASSERTED** — PASS | as above |
| 4 | Evac refund indicator | **SEEN** — both `+$2500` and `+$0` | Should `+$0` show at all? Design call, yours |
| 5 | Fog darkness | **SEEN** — retuned 1.85 → **1.4** on the evidence | One-line dial if still wrong; dims own periphery too |
| 6 | Minimap shading | **SEEN** — five shades, no fallback | **Bottom two gaps are 3.7× tighter than the top** |

**Four of six were looked at on a running game; two were graded by state assertion and never
watched.** Nothing here rests on code reading alone any more, which is what was missing when this
note was refused.

Items **7 and 8** (infantry and vehicle visibility modifiers) were gated behind this note and are
unblocked. Note before dispatching them: `fad9e36b` found the apparent "visibility scaffold" is a
live gauge, not a modifier hook — the premise needs re-checking before design.
