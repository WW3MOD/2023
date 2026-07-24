# Design goal — Realistic, doctrine-grounded bot behavior

> Status: **primary AI goal** (stated by the project owner, 2026-07-19).
> Applies to the **Experimental AI** (`enable-ai-v2`); Normal / Rush / Turtle
> remain the untouched A/B control.

## The goal

WW3MOD bots should play **realistically**. A bot-vs-bot match should feel like
**watching a real modern battlefield**, to the extent the RTS format allows.
The north star is **real-world land warfare from current and recent conflicts**
— the Russo-Ukraine war above all — read through professional military analysis,
**explicitly not movie or game tropes**. When an RTS convention and modern
doctrine disagree, doctrine is the target.

This is one of the project's **primary AI goals**, alongside (and treated as
aligned with) competitive strength — see the rationale below.

## Why (rationale)

- **Immersion / watchability.** The intended experience of WW3MOD is a modern
  war, not a Red Alert reskin. Bots that see before they shoot, disperse under
  observation and mass only at the decisive point, kill mostly with fires, defend
  in depth with reserves, and fight for the enemy's logistics *read* like a real
  battlefield. A death-ball marching at the enemy flag does not.
- **Competitive strength.** Modern doctrine exists because it wins. The same
  behaviors that make a match look real — recon-strike targeting, force
  preservation, defense in depth, interdicting the enemy's sustainment — are also
  what raise the AI's win-rate. **Immersion and win-rate are treated as the same
  axis, not a trade-off.** Where a purely "cinematic" behavior would hurt play, it
  is out of scope; realism here means *doctrinally effective*, not theatrical.

## North-star sources

Professional analysis of the Russo-Ukraine war and other recent conflicts, and
Western service doctrine:

- **RUSI** — tactical developments and offensive-operations lessons.
- **ISW / CEPA / CSIS / Hudson / Modern War Institute** — kill-chain, drone,
  logistics, and command analysis.
- **U.S. Army / TRADOC doctrine** — FM 3-0-style operations concepts
  (reconnaissance-strike, defense in depth, mission command, sustainment).

Full sourcing lives in the research doc.

## The WW3MOD grain (hard filters)

Any realism translation must respect two constraints (details in
[`../reference/game-model.md`](../reference/game-model.md) and
[`../reference/supply-route.md`](../reference/supply-route.md)):

1. **SR call-in economy, not manufacturing.** No factories; units are called in
   from off-map reserves, walk/fly in from the map edge, and cost budget.
   Fittingly, this makes the real war's central lesson — **logistics is the
   center of gravity** — literal: the Supply Route link, income POIs, and the
   vulnerable reinforcement lane are the decisive objectives.
2. **The RTS format caps realism.** No operational depth on a tactical map, no
   rout model (suppression is the only "morale"), casualties are HP. Where a
   doctrine concept doesn't survive these limits, the research doc says so rather
   than forcing it.

## Long-term vision (user-authored, 2026-07-20)

> **Source:** captured live from the project owner while spectating an
> Experimental-vs-Experimental match, 2026-07-20. This is the owner's statement of
> where the AI should go — the north star for the strategic layer — recorded
> faithfully; light editorial structure only. It sits **above** any specific
> benchmark cycle: cycles are the means, this is the end.

### 1. Territorial-control map layer (the centerpiece)

A **fog-of-war-respecting map layer** that classifies territory as **safe /
grayzone / enemy**. It is the AI's running model of who controls what, and it
drives the whole game.

- **Initial assumption:** at game start the bot treats **its own half of the map
  as safe** — a reasonable prior in a 2-player game — until proven otherwise.
- **Intelligence-driven, no cheating:** the layer updates from **actual
  intelligence** (spotting enemies, contact, losses), respecting fog — **no seeing
  through fog**. Territory is reclassified only as the bot actually learns.
- **Safe → capture + fortify:** the bot reads safe territory as *"territory to
  capture and to set up defensive positions in."* Safe ground is to be occupied
  and held, not just passed through.
- **Runs the entire game:** if the enemy retreats from or is destroyed in an area,
  that area becomes **safer**, and the bot **advances into it** — the standing
  principle is to **always push where the enemy is comparatively weak**.
- **Balance-of-power reading:** the same layer, read as a balance of power, drives
  **repositioning and reinforcing weak spots** — strength is shifted to where the
  line is thin or threatened.
- **End state — a defended front, stepping forward:** forces end up **spread along
  the entire line of combat** so that *every part of the front is defended* —
  **most important sectors first**, but eventually **at least some soldiers along
  the whole front** — and the front **steps forward wherever it is safe to do so**.
  Not a death-ball; a held, advancing line.

### 2. Early-game economy sensibilities

Behavior early in the game should match how a thinking human opens, not a
build-order script.

- **No supply trucks while every unit has full ammo.** A truck bought at the start
  just **sits as a target** — no human plays that way. Simple rule for now (don't
  buy resupply while ammo is full); **smarter foresight later** (buy against
  *anticipated* need, not current emptiness).
- **AA proportionate to the actual threat.** A **couple of AA infantry** are
  already dangerous to any helicopter; fielding **multiple SHORAD/Tunguska at the
  start is overbuild**. Scale AA to the air threat that actually exists.
- **Spread out and capture fast, in small groups.** Early urgency is to **disperse
  and grab ground quickly** rather than assembling one armada at the Supply Route.
  Forming up is fine, but **movement can happen in smaller packets** — especially
  early, when units are few.

### 3. Mounted infantry doctrine

Riding vehicles is a doctrine, weighed against its risk, not a default.

- **Technicians ride to distant captures first.** Getting technicians to far-off
  capture objectives by vehicle is the **first-priority** use of mounted movement.
- **Soldiers ride with context-appropriate dismount tactics** (a later layer):
  **dismount far from the enemy** when the ride is just about *reaching the front
  to hold/defend*, and **dismount closer** when it is *assault transport* into
  contact.
- **Always weigh the shared-fate risk:** one missile can kill the **vehicle + the
  squad riding it** together. Mounted movement trades speed for concentration of
  risk, and the AI should price that in.

### 4. Unit-role model + role-driven behaviors (user-authored, 2026-07-22)

> **Source:** the user, answering the operations-layer adoption question,
> 2026-07-22. Recorded faithfully; light structure only. Converges with the
> architecture doc's role resolver (`WORKSPACE/plans/260722_bot_brain_architecture.md`)
> and the fires/artillery behavior cycle the user already adopted.

- **Every unit should have a known role.** Either **YAML-facing properties** set
  per unit, or a role **derived from the unit's stats by the engine** — a
  **one-time computation on first game load, cached**, so role info is readily
  available thereafter. Possibly a **hybrid**: engine derivation plus YAML flags
  where derivation isn't enough.
- **Roles drive doctrine, not just grouping.** From such a calculation the AI
  should determine that **artillery belongs far away**, providing **suppressive
  effects during an assault** or **continuous bombardment** — not standing in the
  line (today `ai.yaml` lumps artillery and SHORAD in with tanks as
  "main line" units; the role model cures that).
- **AoE-aware target selection.** From a weapon's area-of-effect damage, a unit
  should learn to **prioritize formations/clusters of units rather than simply
  the closest target**.
- **Build it where both humans and bots benefit.** The AoE/cluster prioritization
  in particular is a candidate for the **autotargeter itself** (the shared L3
  layer), so human-owned artillery gets smarter target choice too — consistent
  with the split plan's principle that shared unit traits serve both sides.

*Roadmap mapping (agent, same date): the role resolver is already a Phase-3
rider of the split SPEC (derive-from-traits + YAML `AiUnitRole` override — the
user's hybrid, with load-time caching as the implementation shape); artillery
standoff/suppressive-fires doctrine lands in the adopted fires cycle, which
consumes the role model; AoE cluster targeting in AutoTarget is queued as its
own shared-trait work item — default-off, benchmark-priced, and a Phase-3-class
re-baseline if shipped to everyone, per the split SPEC's governance rules.*

### 5. Fires economics — differentiated employment + ammo expected-value (user-authored, 2026-07-24)

> **Source:** the user, reviewing the shipped fires-standoff doctrine,
> 2026-07-24. Recorded faithfully; light structure only.

- **Tube artillery and rocket artillery play differently, and the bots should
  learn that.** Tube artillery (Paladin, Giatsint) can be utilized against
  single units — though ideally against groups. Firing rocket artillery
  (Grad, TOS, M270) against single soldiers **is not worth it**.
- **The general rule — ammunition expected value:** when firing *any* weapon,
  **the cost of the ammunition should be less than the projected damage to the
  enemy** — or else the player is just wasting money firing. This applies to
  every weapon, not just fires.
- **Ballistic missile launchers (Iskander, HIMARS) are special cases, not
  artillery.** The AI does not currently field them (correct). If support is
  ever added, it must be **when-warranted only** — these units are massive
  liabilities due to their price and volatility, so the AI should not use them
  like regular artillery.
- **Improvement path:** this can start as a quick fix (a cheap fire-worthiness
  gate) or a proper implementation (a real EV model), and either way it is
  something to **continuously try to improve** — a standing north-star item,
  not a one-shot feature.

*Roadmap mapping (agent, same date): the aim-at-clumps half is already queued
as the AoE-aware cluster-targeting item (`WORKSPACE/PIPELINE.md` item 14, shared
AutoTarget layer per §4); the fire-worthiness half — the ammo-EV gate and
tube-vs-rocket differentiation — is queued as its own PIPELINE item beside it.
Ammo costs live in the economy model (`DOCS/reference/economy.md`); the role
model (§4) gives the resolver hooks to tell tube from rocket from ballistic.*

## Where the detail lives

The concrete pattern→behavior mapping — for each modern-warfare pattern: the
real-world observation, why it matters, the WW3MOD translation (naming the engine
systems: suppression, stances, `InfluenceMap`, `PoiMap`, the garrison module, the
call-in budget, the SR), effort estimate, and the watchability-vs-win-rate effect
— plus a ranked implementation order and honest format limits, is in:

**[`../../WORKSPACE/plans/260719_ai_realism_research.md`](../../WORKSPACE/plans/260719_ai_realism_research.md)**

That research doc is the working substrate; this file is the standing statement
of the goal.
