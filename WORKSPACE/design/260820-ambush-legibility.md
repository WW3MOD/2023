# Ambush legibility — what the player can see, and what they cannot

2026-08-20 · research only, no behaviour change · branch `wt/ambush-legibility` off `main @ 4bb3fae9`

Mockup: [`260820-ambush-legibility-mockup.html`](260820-ambush-legibility-mockup.html)

---

## 0. The recommendation in one paragraph

The user's complaint — *"it is hard to detect sometimes so I am not sure what is actually active in
game"* — is not a complaint about icons. Concealment in WW3MOD is driven by **five automatic
modifiers that the player never commands and the game never reports**: moving, firing, going prone,
digging in, and standing near cover. The player is not missing a *hidden/visible* light; they are
missing the *causal chain* that decides it. So I recommend **one new glyph, not six**: a white `!`
sharing the existing red `!` slot, meaning *this unit has stopped obeying you in order to stay
hidden*. Everything else — how concealed, and why — belongs in the selected-unit readout, where the
five modifiers can be shown as the ledger they actually are. Three options are costed in §5; the
recommendation is **Option B**.

Two defects found while researching, both feeding the same complaint:

- **The cover bonus is attached to burnt trees only.** `object-proximity` (+1..+3, the largest term
  in the stack) has exactly one granter repo-wide, and it is `^TreeHusk`. Standing in a *live* forest
  gives no concealment at all. §7.1.
- **At maximum concealment the gauge draws nothing**, and that state is reachable by exactly the two
  units a player ambushes with. §7.2.

---

## 1. Ground truth — what the game actually computes

Verified by reading the code, not the recon docs. Where a recon doc disagrees, the code wins; the
`260819` doc's §3.4 arithmetic table is explicitly marked `[NEEDS RUN]` by its own author and by a
comment in `infantry.yaml`.

### 1.1 `CurrentVisibility` is not "am I hidden"

`Detectable.CurrentVisibility` (`engine/OpenRA.Mods.Common/Traits/Modifiers/Detectable.cs:71`) is
**the vision strength an observer must still carry at my cell in order to see me.** Higher = harder
to see. It is a property of *the unit alone* and contains no observer information whatsoever.

It is recomputed every tick (`:86`), clamped to `[1, MapLayers.VisionLayers - 1]` = **`[1,10]`**
(`:90-93`, `MapLayers.cs:75` sets `VisionLayers = 11`), and on change it grants `visibility-<N>`
(`:170`). Reveal requires an observer strength **strictly greater** than the level
(`MapLayers.cs:579`).

> An earlier agent pass reported the clamp as `[1,9]` and concluded the top-end gap was unreachable.
> That is wrong: the ceiling is 10 and §7.2 shows it is reachable.

### 1.2 The five modifiers — the thing the player cannot see

All from `^DetectableInfantryStandard`, `mods/ww3mod/rules/ingame/infantry.yaml:703-732`:

| Input | Condition | Δ | Notes |
|---|---|---|---|
| Near cover ×1 | `object-proximity == 1` | **+1** | `TotalCap: 3` |
| Near cover ×2 | `object-proximity == 2` | **+2** | |
| Near cover ×3+ | `object-proximity >= 3` | **+3** | largest single term |
| Prone | `prone` | **+1** | automatic when stopped |
| Dug in | `dugin` | **+1** | automatic after ~12 s still |
| Firing | `firinganyweapon` | **−2** | `RevokeDelay: 12` ticks (`:723-727`) |
| Moving | `moving` | **−1** | |

Base `Detectable.Vision`: **3** for standard infantry (`:97`), **5** for Sniper (`:1542`) and Special
Forces (`:1988`). SF's suppressed rifle overrides firing to **−1** (`:1995`).

Not one of these is a player command. Not one is reported. The `260819` recon states the conclusion
plainly: *"Hiding is entirely automatic and entirely unreported, and the one stance the user reached
for actively works against it"* — Ambush stance changes **no** detectability term, and springing the
ambush fires, which costs −2 for 12 ticks.

### 1.3 Reachable range per unit

| Unit | Moving + firing | Stopped, prone, dug in | + 3 cover |
|---|---|---|---|
| Rifleman (3) | 1 → ring 28c0 | 5 → ring 16c0 | 8 → ring 7c0 |
| Sniper (5) | 2 → ring 25c0 | 7 → ring 10c0 | **10 → nothing drawn** |
| Special Forces (5) | 3 → ring 22c0 | 7 → ring 10c0 | **10 → nothing drawn** |

Ladder from `infantry.yaml:751-840`; radius for `visibility-N` is the outer Range of the
`^StandardVision` band at Strength N+1.

---

## 2. What already renders — the slot map

The comment block at `defaults.yaml:765-808` is an accurate, load-bearing map of the space around a
selection box. Reproduced because any new mark must fit in it:

```
  Top centre     y=0 damage · y=-3 suppression · y=-5 critical · y=-10 and up CARGO (spreads sideways)
  TopLeft        WithSpriteControlGroupDecoration  (C# default, declares no Position in YAML)
  TopRight       holding-fire pip (orange)
  Top, margin.X +16   rank chevrons
  Bottom         ISelectionBar stack + ammo row
  Top, margin.X NEGATIVE = RIGHT of centre:
        -8,  0   spotted "!"   red FF4A3C          <- WithSpottedDecoration
        -8, -10  stance Fire         X hold-fire / A ambush
       -16, -10  stance Engagement   H hold-pos  / > hunt
```

Two facts that make the recommendation cheap:

- **`WithTextDecoration` is used nowhere else in the mod.** A text glyph is distinguishable from
  every existing pip *by medium alone*, before colour or position do any work.
- **`ValidRelationships` defaults to `Ally`** (`WithDecorationBase.cs:44`), so these marks draw only
  on your own units. An earlier agent pass reported "all ownership" — wrong.

Selected-only readouts already present: concealment gauge rings, suppression pips (10), defense pips
(5). Always-on: damage pips, rank chevrons, holding-fire pip, spotted `!`, stance glyphs.

**A soldier can already carry five marks at once.** This is the strongest argument against giving
each new state its own glyph.

---

## 3. The per-observer problem — and why it is already solved

Concealment is per-observer: hidden from one enemy, visible to another. A single icon is therefore a
simplification, and a wrong simplification lies.

**The answer is not to invent a rule. It is to inherit the shipped one.**
`WithSpottedDecoration.IsSpotted()` (`WithSpottedDecoration.cs:82-120`) already performs exactly this
reduction, and it is careful:

1. Observer set = enemies within `MaximumObserverRange` (32c0, sized to the largest vision band).
2. **Knowledge filter** — `observer.CanBeViewedByPlayer(viewer)`: an enemy that can see us but that
   *we* have not spotted does not light the mark. The trait's own comment explains why: a badge
   driven by true visibility alone *"would be a wallhack — it would announce 'someone you cannot see
   is watching you'."*
3. Reduction = **any**. One observer is enough.
4. Truth gate last, because false positives are worse than false negatives for a badge the player
   acts on.

So the answer to *"hidden from all, from the nearest, or from any?"* is: **none of those — hidden is
the exact complement of the shipped spotted rule.** Same observer set, same knowledge filter, same
`any` reduction, negated. If a "hidden" mark used a different observer set from the "spotted" mark,
the two could be simultaneously lit or simultaneously dark, and the player would be right not to
trust either.

**One addition is required to stop the complement being vacuous.** `!IsSpotted` is true for a soldier
alone in the backfield with no enemy for thirty cells. That is not *hiding*, it is *being
elsewhere* — and lighting a hiding mark there is pure noise. The mark must therefore mean:

> **at least one enemy I am aware of is within observer range, AND none of them can currently see
> me.**

That is one extra counter inside the loop that already runs. It costs nothing and it is the
difference between an icon that means something and an icon that is on all the time.

**Per-observer detail is not representable in one glyph and should not be attempted.** "Hidden from
the tank, seen by the scout" is a two-body fact; the glyph is a one-body surface. If that detail is
ever wanted it belongs to the gauge (draw the ring in the direction of the observer that defeats it)
or to a panel — never to a badge.

---

## 4. The state set — which ones earn a glyph

| # | State | Exists today? | Recommendation |
|---|---|---|---|
| 1 | Hidden from the enemies looking at me | derivable, not shown | **Do not glyph.** Absence of red `!` already carries it once the player learns the vocabulary. Show *degree* on the gauge instead. |
| 2 | Visible right now | **yes** — red `!` | Keep unchanged. |
| 3 | Stopped because continuing would reveal me | **no** (see §6) | **Glyph it — white `!`.** This is the one state whose absence actively confuses. |
| 4 | Holding fire deliberately (ambush) | **yes** — orange pip + `A` stance glyph | Already double-covered. Add nothing. |
| 5 | Ambush armed, targets acquired, waiting | **no** | **Do not glyph.** Merge into 3 — from the player's side "armed and waiting" and "stopped to stay hidden" are the same instruction: *leave them alone.* A separate glyph would split one decision across two symbols. |
| 6 | In cover / protected | partly — defense pips (selected only) | **Do not glyph.** It is a *degree*, and it is already a term in the gauge. Glyphing it would put a permanent icon on every soldier in a forest. |

**Six states, one new glyph.** The economy is the design. States 1, 4 and 6 are continuous or already
covered; states 3 and 5 collapse into a single player-facing meaning; state 2 ships.

The decisive test is *what does the player do differently?* Red `!` → react, you are seen. White `!`
→ do not re-order this unit, it is deliberately disobeying. States 1, 4, 6 change nothing about the
next click, so they are status, and status belongs in the panel.

### Why white `!` should mean "stopped", not "concealed"

The user's phrasing was *"blocked from acting normally, because they are hiding"* — a **disobedience
warning**, not a status light. That is the stronger reading and I recommend it:

- A unit silently not executing an order is the single most confusing event in an RTS. Nothing else
  on screen reports it.
- "I am concealed" is pleasant to know but the player already infers it from the absent red `!`.
- Sharing the red slot makes the pair legible: **the `!` lane means *your visibility needs your
  attention*; the colour says which way.**

Precedence, since both can be true in the same tick: **red wins.** If you are already seen, the fact
that you halted to avoid being seen is stale news.

---

## 5. Three options

### Option A — minimal, ships today, no dependency

Fix the gauge cliff (§7.2) and add **nothing else**. Concealment stays legible only while a unit is
selected.

*Cost:* one YAML tier + a sequence. *Buys:* removes a lie. *Leaves:* the user's actual complaint.

### Option B — recommended

1. **White `!`** at slot `-16,0` (free, beside the red `!`) or sharing `-8,0` with red precedence —
   the mockup shows both; sharing is my preference and is what the mockup's "recommended" row uses.
   Meaning per §3. Requires the halt state from §6.
2. **Fix the gauge top end** so maximum concealment draws a distinct ring instead of nothing.
3. **Concealment ledger on the selected-unit panel** — the five modifiers as signed rows, so
   *"moving −1, firing −2"* is visible and the player can see that stopping would help. This is the
   part that answers *"I am not sure what is actually active."*

*Cost:* one render trait mirroring `WithSpottedDecoration`, one gauge tier, one panel widget.
*Dependency:* §6.

### Option C — generalised disobedience mark

White `!` means *not executing your order*, whatever the cause — hiding, suppressed/pinned, no path,
out of ammo — with the cause named on hover. Strictly more useful and strictly more work, and it
needs a reason enum every halt site populates honestly. **Presented, not recommended, for now:** it
should be the shape the §6 state is built in, so Option B can grow into it without a rewrite.

### Rejected: readouts that are not on the unit

The brief asks these be costed.

- **Cursor** — carries one unit's worth of information at the moment you hover. Wrong channel for a
  state you need while *not* pointing at the unit.
- **Terrain tinting** — the honest version is per-observer and would have to redraw as enemies move;
  it also collides with the shroud, which already owns "what is visible" in this palette.
- **Squad panel** — right for the *ledger*, and that is Option B item 3.
- **Held-key overlay** — **worth building later.** A per-unit glyph does not scale to fifty selected
  soldiers, and the gauge already supports grouped rendering (`Type: concealment` →
  `RangeCircleGrouping`), so a squad sharing a tier draws **one** outline rather than fifty rings.
  That hook exists and is the natural home for a force-wide concealment survey. Out of scope here.

---

## 6. What I need from the halt-state worker

The white `!` cannot ship without a real "stopped because hiding" state. Today the nearest thing is
`haltedForAmbush` in `AttackMoveActivity`, and it is **AI-only** — gated behind
`enable-ambush-tactics`, granted only to units posted by `LaneAmbushBotModule`. A human-ordered
soldier can never enter it.

Precise asks:

1. **Human-reachable.** A unit under a player's attack-move must be able to enter the state.
2. **Readable from the render path** — a public property on a trait or activity, in the shape of
   `Detectable.CurrentVisibility`. A render trait must be able to ask "are you halted for
   concealment?" without a sim call.
3. **NOT a granted condition.** This is load-bearing: the PITFALL at `Detectable.cs:160-162` records
   that a condition token is an allocation handle, not gameplay state, and that driving visibility
   marks from conditions is *"the shape of two shipped desyncs in this repo."*
4. **Carry a reason**, even if only one value exists at first — so Option C is a widening, not a
   rewrite.
5. **Latch semantics stated.** `haltedForAmbush` today does not resume until the order is re-issued.
   Whether the mark clears on its own decides whether white `!` is a transient blink or a standing
   flag, and the mockup assumes **standing flag**.

---

## 7. Defects found while researching

### 7.1 The concealment cover bonus only comes from *burnt* trees

`object-proximity` has exactly one granter repo-wide: `ProximityExternalCondition@ObjectProximity` on
`^TreeHusk` (`mods/ww3mod/rules/husks/husks.yaml:118-121`), inherited by the 21 `*.Husk` tree actors.
`mods/ww3mod/rules/ingame/decoration.yaml`, which defines the **living** trees `T01`–`T17`,
`TC01`–`TC05`, contains **zero** `ProximityExternalCondition`.

So hiding in a live forest grants **+0** concealment. The +1..+3 arrives only after the forest burns
down. Live trees do carry `Density` (`decoration.yaml:101`), but that feeds `DensityModifiesDamage`
— a *damage* reduction, a different system with a different meaning.

This is very likely inverted by accident, and it is a strong candidate for the root of the user's
complaint: the largest term in the concealment stack is unobtainable in the terrain built for it.
Recorded in `WORKSPACE/bugs/discovered.md`. **Not fixed here** — this branch changes no behaviour.

### 7.2 Maximum concealment draws nothing, on exactly the ambush units

`visibility-10` is reachable: `5 (Sniper/SF) + 3 (cover) + 1 (prone) + 1 (dug in) = 10`. No
`^StandardVision` band carries strength 11, so the unit is genuinely undetectable by standard vision
— and the gauge, by design, draws **no circle** (`infantry.yaml:746-747`).

The behaviour is correct; the *readout* fails. "Perfectly hidden" and "feature not working" render
identically, and it happens precisely when the player has done everything right, with the units they
ambush with. A distinct treatment — dashed ring, or a filled faint disc at the innermost radius —
would cost one tier. Note this is currently only reachable *next to burnt husks* because of §7.1; if
§7.1 is fixed so live trees grant cover, **this becomes common** rather than rare. Fix 7.2 before or
with 7.1.

---

## 8. How this would be verified

Per `DOCS/recipes/SCREENSHOT.md`, a visual change like this is verified by scripted capture rather
than by argument: `TestHarness.Screenshot(label, note)` at beats in an autotest scenario, PNGs landing
in the run directory alongside the verdict, then read back multimodally. Evaluation is semantic —
presence/absence of a mark, colour, which unit it belongs to — not pixel-exact. One caveat matters
here: captures taken inside autotests run with **no render player**, so fog- and
visibility-dependent marks cannot be trusted from that path, and the red/white `!` are exactly that
class. This proposal therefore needs an **external** capture with a real player, or a state-query
assertion, and a prior pass already found glyph placement is the failure mode to watch: lateral
offsets of 13 and 21 px read as *orphaned* from their soldier and were halved to 8 and 16
(`defaults.yaml:803-807`). Any new glyph must be checked at that scale, in a crowd, not in isolation.

---

## 9. Open questions for the user

1. **Does white `!` mean "stopped" (recommended) or "concealed"?** The mockup shows both.
2. **Share the red slot, or take the free one beside it?** Sharing reads as one axis; separate slots
   let both show at once.
3. **Is §7.1 a bug or intended?** If live trees are *meant* to give no concealment, the whole cover
   term is dead and the ledger in Option B should stop showing it.
