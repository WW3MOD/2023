# Skirmish buys its nuclear weapons on a clock; Escalation is handed them and never sells them

_Recorded 2026-09-10T19:47:11.239Z by ffb08fdc_

Ruled by the user, recorded because it is the line that finally separates the two modes cleanly and it settles several earlier open threads at once.

## The ruling, verbatim in substance

Skirmish may have nuclear weapons for players who want a nuclear skirmish game. They are **built/purchased**, gated behind time intervals — low yield purchasable after ten minutes, the next tier after twenty, and so on. **No free nukes in Skirmish, and no escalation per used nuke at all in Skirmish.** Also locked in the same message: **Escalation requires two teams**, confirming decision 15.

## Why this is the right seam

Each mode now has one coherent answer to "what is a nuclear weapon here", and they are opposites:

- **Skirmish** — a nuke is a *weapon you buy*. It behaves like any other RTS superweapon: gated behind a clock so nobody opens with one, priced, and consequence-free beyond the crater. This is the familiar thing, and it is what the audience asking for "nuclear skirmish" actually wants.
- **Escalation** — a nuke is a *consequence you are handed*. Nothing is purchasable; every warhead in the match exists because somebody decided to fire one. Firing arms the side you hit.

The modes stop being two configurations of one system and become two different games that share a map and a no-rush timer. That is why the lobby panel could finally be drawn: there is no shared nuclear section to reconcile, because the two halves have nothing in common.

## What it settles that was previously open

- **Buying in Escalation** is now clearly wrong rather than merely off-by-default. The audience it would serve has Skirmish. Countability — both sides knowing exactly what is on the map — is what makes the decision to fire a real one, and a purchase channel destroys it. Posted to the user as a question rather than assumed, but the recommendation is no.
- **The Skirmish unlock schedule mirrors the old ladder's yields** — 1 kt, 20 kt, 50 kt, 100 kt — so the tier list already exists in `NuclearReleaseLadder`'s band table and does not need inventing. What differs is that crossing a tier makes a weapon *purchasable* rather than *granted*.
- **Two settings for Skirmish, one for Escalation.** Skirmish takes a highest-yield cap and an unlock interval; Escalation takes the time the first warheads arrive. Four new controls in the whole panel counting Doomsday's length.

## Consequence for what ships today

The design deletes two of the four placeholder-dimmed dropdowns currently in the lobby. **Escalation Pace** was a scale factor on the DEFCON 3 hold that no player could interpret, and it is replaced by the DEFCON hold field being editable directly. **Nuclear Ceiling** only ever meant something in the pressure-driven ladder that the exchange model replaced; its role in Skirmish is taken by Highest yield, and in Escalation nothing caps the ladder except the game-enders at the top.

`MarkAsPlaceholder` can come off the survivors once the panel is built and the DEFCON hold durations are tuned — which still needs the user to play the mode, and remains the one input that cannot be derived.

## Open, and asked rather than assumed

The interval defaults. Skirmish's tiers are drawn at ten minutes, which puts the 100 kt tier at 40:00 — most of a match — so if Skirmish nukes are meant to be a mid-game tool the interval wants to be five to seven minutes. Escalation's single setting is drawn at the user's own 30 minutes. Both posted to the user; neither is a code question.
