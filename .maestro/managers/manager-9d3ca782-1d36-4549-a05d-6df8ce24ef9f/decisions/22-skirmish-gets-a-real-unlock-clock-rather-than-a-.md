# Skirmish gets a real unlock clock rather than a trimmed timeline

_Recorded 2026-09-10T23:01:00.268Z by ffb08fdc_

Ruled by the user, 2026-09-11, on question `Kl8qTg9Q11WKyL2DuMEha`.

## The finding that forced the question

The timeline shipped at `3a1780bc` draws three bands — `NO RUSH`, `CONVENTIONAL`, `NUCLEAR WEAPONS PURCHASABLE` from ten minutes. In **Skirmish, the default mode, all three are false**, and in two opposite directions:

- The first two name the DEFCON clock, and `DefconEscalation.cs:49-52` says in its own words that Skirmish is a strict no-op. That clock never runs.
- The amber band understates rather than overstates: every buy-tier nuclear power is gated on `Prerequisites: powers.america` / `powers.russia` (`player.yaml:208-234`), a **faction** gate with no time component anywhere in the tree. Nukes are purchasable from the first second.

The bands were not invented by the implementer — the approved mockup draws them, including `NO RUSH` in its own Skirmish tab. They depict decision 16's design, which is written throughout in the future tense and was never built.

## The options and the ruling

Three were offered: draw only what is true today (agent's recommendation, 62), build the unlock clock so the bands become true (45), ship the bands as drawn (12). **The user chose to build the clock.**

Worth recording that the agent's recommendation was not taken, and why the user's choice is better than the reasoning behind the recommendation: the agent optimised for unblocking a push, treating the bands as the thing to fix. The user treated the *absent feature* as the thing to fix. The bar was already an accurate picture of the game that was designed — the game had simply not caught up to it.

## What this commits to

Decision 16's Skirmish schedule: rungs becoming purchasable on an interval, with two lobby options (unlock interval, highest-yield cap). Default interval **10 minutes**, matching what the bar already draws. Decision 16's open question — whether 5–7 minutes plays better, since 10 puts the top rung at 40:00 — **stays open and is the user's**; the implementer was explicitly told not to pre-empt it.

## A trap recorded for whoever picks this up next

`powers.america` is provided by a **single** `ProvidesPrerequisite@PowersAmerica` block for the whole faction. Gating that block on a condition locks or unlocks every rung at once, which is not a ladder. Per-rung gating needs per-rung prerequisites.

## And a correction to two documents at once

Both decision 16 (1/20/50/100 kt) and the 2026-09-10 handoff (5000 conventional/1/10/50/100) state rung tables that **disagree with the tree**. Read at `86547ce1`: America ships 0.3 / 50 / 100 kt (B61-12 low, B61-12 max, W76-1) — three rungs; Russia ships 1 / 10 / 50 kt plus Kalibr — four. The factions have different rung counts and that asymmetry is real. **Two documents agreeing with each other is not evidence, and here they did not even agree.**

---

## CORRECTED 2026-09-11, same day, by the implementer — both claims above are wrong

**The rung table.** Read with `NuclearYieldTons` as the authority: **both factions have four nuclear buy rungs and they are symmetric** — 1 / 20 / 50 / 100 kt bands, one rung per band per side. There is no asymmetry to preserve.

The rung I missed is **`MissileStrikePower@B61Mid`, B61-12 at 10 kt** (`NuclearYieldTons: 10000`, `Prerequisites: powers.america`), and the reason I missed it is worth more than the number: **it is the one nuclear power defined in `player.yaml` (`:611-617`) rather than `nuclear-arsenal.yaml`.** I surveyed the arsenal file, and America duly appeared to skip 10 kt. A survey of "the file where these live" silently excludes whatever does not live there, and reports the gap as a property of the game.

**And the two documents were never in conflict.** Decision 16's 1/20/50/100 describes the **bands**; the handoff's 1/10/50/100 describes Russia's **weapons**. Both are correct about different things. I read a contradiction into them, made that the headline of this section, and used it to justify distrusting both — when the actual error was mine and neither document needed correcting. **Two documents that appear to disagree may be answering different questions; check what each is counting before concluding either is wrong.**

**The trap recorded above does not bind, either.** It is true that `powers.america` has a single provider — but per-rung gating **already existed under a different name**: every nuclear power carries `RequiresCondition: !nuke-arsenal-disabled && nuclear-release-<band>`, and `GrantConditionOnNuclearRelease` was granting all bands on the first tick outside Escalation. So the work added no prerequisite and no per-tier provider blocks; it changed what that one branch reads. I had established the mechanism confidently enough to tell the implementer not to re-derive it, and the mechanism I established was the wrong one — the ladder was already in the tree, one grep away, under a name I did not think to look for.
