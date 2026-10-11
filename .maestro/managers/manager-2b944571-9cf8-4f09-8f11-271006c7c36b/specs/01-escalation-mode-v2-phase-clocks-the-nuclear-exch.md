# Escalation mode v2 — phase clocks, the nuclear exchange, and the lobby controls

_spec · status: draft · authored 2026-09-13T15:32:02.656Z_

# Escalation mode v2 — spec draft

Drafted 2026-09-13 against `main @ dcae209f` from the user's rulings in chat (this session) plus decisions 01–22 (manager-9d3ca782) where not superseded. **Draft** — four forks are open as an `ask_user_question`; marked ⟂ below.

## Phases (per match, wall clock unless stated)

| Phase | In-game name | Starts | Ends | Rules |
|---|---|---|---|---|
| No rush | DEFCON 3 — Positioning | 0:00 | **T1** (lobby: "No-rush period", minutes) | Border wall bisects the map; nothing crosses. Shipped. |
| Peace | DEFCON 2 — Weapons free | T1 | **event:** first enemy-caused destruction of anything | Units fire only on direct order. **Kept** (user: "who breaks the peace first"). Bots must fire eagerly here (wired last). Run-over counts today (`DefconCasualtyObserver.cs:63-78`) — accepted for now. |
| War | DEFCON 1 — Open war | first casualty | — | Shipped. |
| Nuclear | (readout: warhead ledger) | **T2** (lobby: "First warheads") ⟂ base = match start or DEFCON 1 | **T3** or apocalypse | Exchange model below. T2 may fall inside DEFCON 2 — a nuke is a direct order, its kill ends the peace. |
| Ending | Dead Hand | **T3** (lobby: "Time limit") if "Nuclear ending" on | — | Both sides get game-enders + 15 s to place them; Dead Hand places for anyone who does not. Nukes fly, game ends. **Same ending as the apocalypse below.** The 15 s window is unbuilt (prose only at `DoomsdayStrike.cs:46-52`). |

Timeline in the lobby is an *overview*; the user accepts it being approximate where a boundary is event-driven.

## The nuclear exchange (Escalation only; Skirmish keeps its unlock clock, untouched)

Sides: exactly two (decision 15). "The other side" is unambiguous.

- At T2 the lowest yield (1 kt) becomes available to **both** sides, free, on a **countdown** (regenerates after delay D; the same side may fire again without any retaliation).
- Firing yield Y at the other side arms them with **one warhead of Y+1** the first time; further Y hits while they already hold a Y+1 grant nothing larger ("only one more of the smaller").
- Every yield a side holds regenerates on its own countdown ("any yield at a later stage eventually resets").
- Rungs: 1 kt → 20 kt → 50 kt → 100 kt → game-enders (`NuclearReleaseLadder` bands; `NuclearYieldTons` is the authority).
- Game-enders unlocked for a side ⇒ ⟂ apocalypse immediately (both sides' 15 s window) vs the victim may hold/fire.
- The dominant side's correct play is restraint; the losing side's is to fire. Both retaliating spirals to the ending quickly. Bots fire only when losing.

⟂ Open: exact bookkeeping — **cooldown-unlock** (each unlocked tier is a cooldown power; parity by time) vs **stock ledger** (tit-for-tat counts: being hit by Y also grants +1 Y; the victim can burst-reply "as many small ones as were fired"). ⟂ Open: what arms the other side — any launch, or only a launch that damages them (decision 14's 10 % rule).

Nuclear Posture (decision 20, Limited/Flexible/Massive) survives as the knob on the regeneration delay D and per-tier countdowns.

## Lobby controls (dropdowns are the source of truth; the timeline becomes read-only overview)

Game mode · No-rush period (min) · First warheads (min) · Time limit (min / none) · Nuclear ending (on/off) · Nuclear posture · [Skirmish only] Nuclear unlock interval · Highest yield.

Consistency rules: T1 < T2 < T3 (when T3 set). Growing an earlier window shifts later ones; a window that would collapse to zero is flagged, never silently clamped. The word DEFCON does not appear in the lobby (decision 18). `MarkAsPlaceholder` flips off with this work. `defcon-pace` (slow/standard/fast) is replaced by a minutes value; `nuclear-ceiling` is dropped (the exchange has no host ceiling; game-enders are the top).

## In-game feedback (required by the user)

Phase strip + banner ship; add: a sound/notification on each transition; a **warhead ledger** visible to both sides (what each side holds and its countdowns) — countability is the mechanic; a clear "you have been armed with X" moment; the 15 s Dead Hand targeting UI.

## Out of scope for the first cut

Bot wiring (last). Partial MIRV (game-enders fire everything). 3+ sides.
