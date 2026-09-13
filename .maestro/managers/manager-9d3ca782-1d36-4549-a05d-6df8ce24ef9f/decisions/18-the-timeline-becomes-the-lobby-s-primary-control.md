# The timeline becomes the lobby's primary control, and DEFCON leaves the lobby entirely

_Recorded 2026-09-10T20:09:51.622Z by ffb08fdc_

Ruled by the user. Recorded now, before the supporting research lands, because it carries a hazard that could invalidate it and a future session must see the hazard next to the ruling rather than discover it separately.

## The ruling

Option 3 from `lobby-final.html` — "Drag the match" — becomes the settings section's primary control: one horizontal bar representing the match, with draggable markers for when the no-rush period ends and when the first warheads arrive.

And: **the word DEFCON does not appear in the lobby at all.** It is an in-game concept only. The user's reasoning is that the slider explains itself better than the label does — a host does not need to know the mechanic is called DEFCON to understand a marker that says when the line lifts.

That second half is the more interesting ruling. DEFCON has been the mod's name for its no-rush timer through this entire design thread, and the panel has carried it in every previous mockup. Dropping it from the lobby means the lobby describes **what happens** and the game describes **what it is called** — which is the right division, and it removes the one piece of jargon a new player met before the match started.

## The hazard, which is real and unresolved at time of writing

**A draggable timeline may not be expressible as a lobby option at all.**

`DefconEscalation.cs:200-215` states that every control in that panel is a dropdown because `LobbyBooleanOption` is the only type that produces a checkbox, and that **there is no integer option type in this engine**. If that holds generally, then the lobby's option system can express dropdowns and checkboxes and nothing else — and a timeline with draggable markers is neither.

The likely shape of a solution, if one exists: a **custom widget** that renders the timeline and writes into ordinary *enumerated string* options underneath (`"10"`, `"20"`, `"30"`…). The synced lobby state stays a set of string options, so replays, server validation and clients that never draw the widget all keep working; the timeline is purely a nicer way to set values the engine already understands. That would also mean the timeline degrades gracefully — the same match is configurable without it.

A research worker is confirming this. Until it reports, **the direction is ruled but not yet known to be buildable**, and the fallback is the tab-1 dropdown panel already drawn and already agreed.

## Scope of the mockup this produces

The user asked for the settings section **as a whole**, in the real lobby's own styling, ordered sensibly — explicitly not the nuclear settings with everything else bolted on afterwards. Two constraints on that:

- The mod's lobby already has a **locked visual design** that shipped: `WORKSPACE/lobby/mockups/full-page-realistic.html`, with a documented palette at `mods/ww3mod/chrome/_lobby-palette.yaml`. The new mockup must be drawn in that vocabulary — 4-column grid of 60 px option tiles, uppercase 10 px labels over 16 px values, pure grayscale, 1 px bevel pairs — rather than inventing a look.
- A 2026-08-19 UX review found that **22 of the 34 designed lobby options were never implemented and are silently hidden**, leaving 12 real ones. So "all the options that should be in the lobby" is a question with a documented history, and the mockup should distinguish what exists from what is proposed rather than drawing them alike.

## Why this is being built to be screenshot-compared

The user's stated reason: *"you have had issues working with the UI so we need a clear mockup so that you can iterate and screenshot and compare until it looks the same."* That is now mechanically possible — `tools/autotest/screenshot-lobby.sh` launches into the skirmish lobby unattended, captures at a chosen window size, and quits cleanly without a pkill. Drawing the mockup at **1440×900** matches the locked design's stage, so the mockup and the capture are directly comparable rather than merely similar.
