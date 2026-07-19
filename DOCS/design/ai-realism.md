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

## Where the detail lives

The concrete pattern→behavior mapping — for each modern-warfare pattern: the
real-world observation, why it matters, the WW3MOD translation (naming the engine
systems: suppression, stances, `InfluenceMap`, `PoiMap`, the garrison module, the
call-in budget, the SR), effort estimate, and the watchability-vs-win-rate effect
— plus a ranked implementation order and honest format limits, is in:

**[`../../WORKSPACE/plans/260719_ai_realism_research.md`](../../WORKSPACE/plans/260719_ai_realism_research.md)**

That research doc is the working substrate; this file is the standing statement
of the goal.
