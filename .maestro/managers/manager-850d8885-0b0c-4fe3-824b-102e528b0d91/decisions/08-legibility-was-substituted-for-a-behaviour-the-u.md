# Legibility was substituted for a behaviour the user asked for

_Recorded 2026-08-19T22:16:24.188Z by 17dc66e4_

## What happened

The user asked, in substance: *"it is really hard to stay hidden, and in ambush stance that should take care of itself."*

The agent researched it, established that **no stance touches detectability** and that Ambush actually disables the only automatic take-cover, and then shipped the **concealment gauge** — a ring showing how far away a soldier can be seen. That work is good and correct. **It is not what was asked for.**

The user's line was a statement of DESIRED behaviour. It was processed as a claim about CURRENT behaviour, refuted, and answered with visibility into the absence.

The "legibility first" ruling licensed this and is not to blame. That ruling was about ordering — make existing mechanics visible *before* changing them — not about replacing a requested mechanic with a readout of its absence. The agent read a sequencing rule as a scope rule.

## The same substitution happened three times

1. **Ambush should hide.** → reported "no stance touches detectability".
2. **A running soldier who spots an enemy should stop and take cover.** → reported "no react-to-contact behaviour exists anywhere".
3. **A white "!" for a soldier held up because he is hiding.** → reported "the state it would report does not exist". That is the reason to BUILD the state, not to drop the indicator.

Each report was factually correct and each closed a request the user had opened.

## And one active loss

**The TAKE_COVER button was deleted the same day** (dead at three levels, so removing it was defensible cleanup). But its own backlog entry listed *"implement take-cover as a real orderable behaviour"* as one of three live options, explicitly flagged as a decision for the user. The agent took the tidy-up option without asking. If the user wants a manual take-cover, its natural home was removed hours before they said so.

## The rule to carry

**When the user describes behaviour the game does not have, that is a feature request, not a misconception to correct.** Verifying it is absent is the FIRST step, not the answer. The report should end with "so here is what building it would take", not with "so the complaint resolves".

Corollary for dead UI: **before deleting a control, check whether the thing it was meant to do is something the user has asked for.** A dead button for a wanted feature is a stub, not litter.

## Status

Four design questions posted (`YYCzbpE7-gBGnWGLw73V1`): what "hidden" means mechanically, what breaks it, whether it requires standing still or cover, and whether a manual take-cover order should exist alongside the stance. **The user has said explicitly: keep asking, implement nothing until they say ready.** No worker is to touch this.
