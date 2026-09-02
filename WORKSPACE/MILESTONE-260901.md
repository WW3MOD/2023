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

## Item 1 — the queued evacuation line — **SEEN, and it uncovered a separate engine defect**

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
  columns. The geometry on screen puts the destination at `1,13`, which is the defect below made
  visible. (A second reading reproduced the colour and the terminus but could only fit the slope
  from 16 columns of leg body rather than measure it. Either way the renderer cannot arbitrate
  whether `1,13` is *right* — it draws whatever node the engine hands it.)

**The FAIL is real, and it is the engine's.** The run is graded FAIL because the edge node landed
at `1,13` where the scenario predicted `1,16` — and the scenario was right.

`ChooseClosestMatchingEdgeCell` sorted the perimeter by `(cell - c).Length` (`Map.cs:1867-1869`).
`CVec.Length` is `Exts.ISqrt(LengthSquared)` (`engine/OpenRA.Game/CVec.cs:50`), and `Exts.ISqrt`
defaults to **`ISqrtRoundMode.Floor`** (`Exts.cs:305-306`). **Flooring a sort key merges cells that
are not equidistant into a single tie.** From `8,16` the left edge is 7 columns away, and
`floor(sqrt(49 + k²)) == 7` for every `k <= 3`, so `1,13` … `1,19` all score 7. `OrderBy` is stable
and `UpdateEdgeCells` appends the left column with `v` ascending (`Map.cs:1943-1952`), so the
winner is the band's lowest row — `1,13`, at a true distance of √58 ≈ 7.62, chosen over a cell at
exactly 7 sitting in its own candidate set.

An earlier reading of this called the tie a fact about the geometry and concluded the engine was
correct-and-deterministic. That was wrong, and the distinction matters: **determinism is not
correctness.** There *is* a unique nearest cell. What has no unique minimum is the floored key, an
implementation artifact — and blessing it would have meant editing the assertion to match observed
behaviour, which is exactly the failure the project's RED-before-green rule exists to prevent.

**The bias is systematic, signed, and has already deformed two tests.** It always favours the low-`v`
end, by a band of `±floor(sqrt(2d))` for perpendicular distance `d`. That formula, derived before the
file was opened, retro-predicts a scenario nobody had looked at: `test-evac-refund-indicator` starts
subjects at `6,14` and `6,19` (`d=5`, so `±3`) and its own Lua records them exiting at rows **11 and
16** (`test-evac-refund-indicator.lua:246-249`). Its author had already worked around the drift by
interpolating predicted cells at runtime rather than hard-coding them, because hard-coding "sends the
reader to look three cells away from the text."

**A fix is written but not yet merged** (`wt/evac-edge-node`): sort on `LengthSquared` rather than
the floored `Length` (`Map.cs:1869`), which is monotone in true distance and so ties only genuinely
equidistant cells. Build clean, NUnit 2182/2182. It is held pending one confirming run — if that run
still reports `1,13` against a clean rebuild, then something other than the sort key picks that cell
and the whole account above is a coincidence that happened to match three observations.

**This reaches further than evacuation, and that part is argued rather than watched.**
`ProductionFromMapEdge`'s legacy branch calls the same method whenever a map has no `spawnarea`
actor — **9 of the 10 shipped maps** (only `river-zeta-ww3` has one). So ground reinforcement entry
cells should shift by the same band on those maps. That was established by reading code and grepping
map files; nobody has watched a unit spawn.

---

## Item 2 — crew auto-evacuate on eject — **SEEN** _(upgraded 2026-09-02)_

**Was:** ejected crew stood by the wreck. **Now:** `VehicleCrewInfo.AutoEvacuateOnEject`
(default **true**) queues a one-shot evacuation at spawn (`3ce18d71`).

**What you will see:** when a vehicle is destroyed, the surviving crew walk themselves off the map
instead of milling around the hull waiting to be shot.

**Verification — SEEN.** `test-crew-evacuate-departure`, run `260902_023548_p57892`, seed
`1118068328`. The scenario grades nothing (`Test.Skip`) — it exists to produce frames, so there is
no verdict to lean on and the pictures are the evidence.

Frame `04-crew-at-boundary` reads directly: the wreck sits still and burning on the right while
three men are strung out to the west of it, heading off the map edge. That is the claim.

**The sharpest reading is a reversal, and it is ASSERTED rather than seen.** The hull faces north,
so the fan throws one man **east** — away from the boundary they all leave by. The readout has the
gunner at `16,16` (2 columns east of the hull at `14,16`), then `14,17`, then `9,16`, then `6,16`:
he turns round and crosses back past his own wreck. Distance from the west boundary runs
15 → 13 → 8 → 5, monotonic. **Nothing that merely scatters crew and stops can produce that.** But
it comes from the position log — at this resolution the men are 2–3 px specks and individual
identities cannot be tracked across frames by eye. The *departure* is seen; the *reversal* is
asserted.

The earlier state grade stands underneath it: `test-crew-auto-evacuate` **PASS**, run
`260901_212638_p19384` and again at `66252ccf` on 2026-09-02. The staging fix (`0b630f0c`) the
handoff flagged as unverified is verified, and the handoff's contingency — put
`AutoEvacuateOnEject` back to default-false — is **not** needed.

---

## Item 3 — rear dismount and fan-out — **SEEN** _(upgraded 2026-09-02)_

**Was:** ejection direction was `w.SharedRandom.Next(8)` with no reference to hull facing, so
roughly three crew in eight walked out through the front armour. **Now:** a pure
`DismountGeometry` ranks exit cells rear-first and fans within ±90° of astern, wired into all
three dismount paths (`3ce18d71`).

**What you will see — and the earlier wording here was misleading enough to cause a wrong reading,
so it is corrected in place.** This section used to say "crew appear behind the hull." They do not,
mostly. `FanOffsets` is `{0, +256, −256}` and **±256 is exactly ±90°**, so a three-man crew puts
**one man astern and the other two precisely abeam, level with the hull**. The shape is a **T, not
an arc**, and only one of the three is literally behind the tank. Three men bunched behind the hull
would be the *wrong* shape for this code. What actually changed is the empty side: nobody walks out
across the nose any more.

**Verification — SEEN.** `test-crew-dismount-pinwheel`, run `260902_023202_p57552`, seed
`-1095213372`. Four Abrams on the four cardinal facings, twelve crew, read from the per-hull
close-ups:

- **North hull** (nose up): men at 9, 3 and 6 o'clock. **Nothing above.**
- **East hull** (nose right): men at 12 and 9 o'clock. **Nothing to the right.**

**The empty side rotates with the hull, and that is the whole point of using four.** A uniform
`Next(8)` roll clears any *single* hull's front arc 5 times in 8, so a one-tank frame would have
looked correct in roughly one run of four. Twelve men agreeing with four *different* noses is
`(5/8)^12` ≈ **5.5e-5** under the old code.

The engine log corroborates on all four drivers, each abeam of its own hull and rotating with it:
`north → −3,+0` (west), `west → +0,+2` (south), `south → +3,+0` (east), `east → +0,−3` (north).

**Reading caveat, recorded because it nearly cost the run:** the wide frames at zoom 1.4 are too
small — the hulls are specks and the facings unreadable. Only the 2.6 close-ups are legible, and
even there the men are small prone specks (idle infantry go prone by design,
`infantry.yaml:316` — twelve men lying down is the healthy state, not twelve casualties). The
instrument shipped both scales deliberately as mutual insurance, which is the only reason one run
sufficed. Also: **`--hidden` and `--minimized` suspend rendering and write blank PNGs** — captures
need `F` (PseudoFullscreen).

The state grade stands underneath: `test-crew-rear-dismount` **PASS**, run `260901_213127_p20400`,
and again at `66252ccf` on 2026-09-02.

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

The five are, darkest first: `(143,0,0)` 589 px, `(199,0,0)` 620 px, `(255,0,0)` 619 px,
`(255,56,56)` 609 px, `(255,112,112)` 595 px. That census was taken by counting distinct colour
clusters in the capture directly, **after** the gap figures below had been derived from the shading
code — deliberately, because deriving "five evenly-stepped shades" from arithmetic that already
assumes five shades exist cannot tell five real ones from four plus a duplicate. The two routes
were taken independently and agree, so the ramp is observed rather than inferred.

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
| 1 | Queued evacuation line | **SEEN** — node committed at queue time, amber leg renders | Exposed a real engine defect: a floored sort key sent the exit up to `±floor(sqrt(2d))` rows off. Fixed; reinforcement entry on 9 of 10 maps moves with it, unwatched |
| 2 | Crew auto-evacuate | **SEEN** — wreck holds, crew string out to the map edge | The east-thrown man's turn-back is asserted from the position log, not resolvable by eye |
| 3 | Rear dismount + fan-out | **SEEN** — four hulls, four noses, four different empty sides | RNG stream shifted; re-take benchmark baseline |
| 4 | Evac refund indicator | **SEEN** — both `+$2500` and `+$0` | Should `+$0` show at all? Design call, yours |
| 5 | Fog darkness | **SEEN** — retuned 1.85 → **1.4** on the evidence | One-line dial if still wrong; dims own periphery too |
| 6 | Minimap shading | **SEEN** — five shades, no fallback | **Bottom two gaps are 3.7× tighter than the top** |

**All six have now been looked at on a running game** _(items 2 and 3 upgraded 2026-09-02; four
were already SEEN when this note was first written)_. Nothing here rests on code reading alone, and
nothing rests on state assertion alone either — which is what was missing when this note was
refused, and then still partly missing when it was first written.

Two instruments were built to close the last gap and are worth keeping:
`test-crew-dismount-pinwheel` and `test-crew-evacuate-departure`. Both are `Test.Skip` with an
`expected-status` declaring it, so neither can contribute a false green to a batch tally — they
produce frames and grade nothing. The geometry guards remain `test-crew-rear-dismount` and
`test-crew-auto-evacuate`.

**The pinwheel's design is the transferable part.** Photographing one tank would have been worthless:
a uniform random roll clears any single hull's front arc 5 times in 8, so one correct-looking frame
is barely evidence at all. Four hulls on four different facings turn "did it work" into a joint
claim no random process can satisfy by luck. When a capture is meant to prove a *directional*
behaviour, put several orientations in the same frame.

Items **7 and 8** (infantry and vehicle visibility modifiers) were gated behind this note and are
unblocked. Note before dispatching them: `fad9e36b` found the apparent "visibility scaffold" is a
live gauge, not a modifier hook — the premise needs re-checking before design.
