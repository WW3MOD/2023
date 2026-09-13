# Nuclear Posture survives the cull as a word, not a number

_Recorded 2026-09-10T21:20:38.941Z by ffb08fdc_

Decided by the agent on the user's explicit delegation: *"Either is fine by me, you decide."* The choice was to drop the per-step grant control entirely, or keep it as a qualitative scale.

## Kept, and renamed

The control was drawn as `ESCALATION GRANTS: 1 warhead per step`. It becomes **`NUCLEAR POSTURE`** with three values — **Limited / Flexible / Massive** — after the three Cold War doctrines (*Limited War*, *Flexible Response*, *Massive Retaliation*), which belong in the tooltip rather than on the control. "Massive" reads correctly to a player who has never met the term; the doctrine names reward one who has.

## Why keep it when Game-enders and Reply Window were just cut

The test that separates them is **whether the host has an opinion worth expressing**.

- *Reply window* was always 15 seconds — a control that could hold exactly one value.
- *Game-enders* was arithmetic: one per ~1340 cells of map, split between sides. Offering it invited a host to override a calculation the engine already does correctly.
- *Nuclear Posture* is the only remaining control over the **shape** of the nuclear phase. The timeline says **when** the war goes nuclear; this says **how hard**. Those are genuinely different questions, and the difference between a trickle and a flood is larger than most of the options that survived the cull.

## Why a word rather than a number — this is the load-bearing part

The user's own framing is the argument: *"At some steps you might get more than one, maybe some steps grants you a mixed bag of small and large."*

A number promises a precision the design has not earned. It fixes the payout to a per-step constant at exactly the moment the design wants freedom to make step 2 grant two warheads, step 3 grant a small and a large together, and step 4 grant nothing but a raised ceiling.

**A named level is an indirection with a real payoff:** the schedule underneath can be retuned from play without touching the lobby, the option's values, or the wire format. That last point matters concretely — lobby options are enumerated strings validated by `Values.ContainsKey`, and the stored value is wire-visible in saved settings and replays. `"flexible"` stays valid across every future retuning of what flexible *means*; `"1"` does not survive the day the payout stops being one.

## Cost, and the exit

One enumerated string option — the only kind the engine supports, so nothing new is required. If it proves to be one knob too many once the mode is actually played, deleting it is a single line and no data migration, because nothing else keys off it.

## Left open deliberately

What each level actually grants. That is a tuning question answerable only by playing the mode, and it is precisely the question the indirection exists to defer.
