# DEFCON is three rungs starting at 3, and nuclear weapons exist only inside the mode

_Recorded 2026-09-09T09:53:32.782Z by ffb08fdc_

User's ruling, 2026-09-09. **Supersedes decision 03's five-rung ladder.**

## Ladder 1 — DEFCON, now THREE rungs

- **Start at DEFCON 3, and 3 is the ceiling.** Their reasoning verbatim: *"that is the current real world status, so it makes no sense to start lower (higher defcon level) than that."* There is no 5 and no 4. The host may start at 3, 2 or 1.
- **DEFCON 3 — positioning.** Hard wall on the dividing line, neither side may cross. Build up and take ground on your own half.
- **DEFCON 2 — free to strike, but only by DIRECT ORDER.** Either side may attack anything; **units do not fire autonomously**. Every shot is one the player gave.
- **DEFCON 2 → 1 is triggered by ONE casualty.** *"If even one singe enemy life is taken we go into defcon 1 and the regular game begins."*
- **DEFCON 1 — open war**, autonomous fire returns, the regular game.

**Why DEFCON 2 is the good idea here:** no unit shoots because it happened to see something, so the first casualty is always somebody's decision — and it immediately ends the phase, which is what gives that decision weight.

## Ladder 2 — nuclear release

- **Tsar Bomba is OUT of normal play.** *"mostly a gimmick… We keep it in code but it should be disabled and cannot be used in game for now (keep it for sandbox)."* Possibly a custom campaign later. Not on the ladder at any ceiling setting.
- **Game-enders are anything above ~200 kt.**
- Ladder as drawn: HOLD → 1 kt → 20 kt → 50–100 kt → 200 kt+.

## The three forks, answered

- **Shared ladder** — but *"exact mechanics needs to be discussed further"*. Open sub-question put back to them: whether firing should push the firer up faster than the opponent, which would make going first costly rather than free.
- **Hard wall**, no crossing at the early stage. This is the expensive half of the feature: authored per-map dividing lines across ten shipped maps plus a runtime blocking layer.
- **Checkboxes gone, the ladder decides.** *"we might add in other options later when we see how it turns out. But I think I want nukes to only be part of this escallation mode."* That last clause is stronger than the fork asked: nuclear weapons do not exist outside DEFCON Escalation and Sandbox at all.

## Lobby direction

*"I would like to try to make the lobby options a bit simplified. Even though I like the fine grain controls, it gets pretty messy… if we design it quite simply at first, then we can add finer controls later."*

Delivered `WORKSPACE/mockups/lobby-settings-v3.html`, drawn against a live screenshot of the shipped lobby (`manual_lobby_260909_115104`) so the chrome is real. Five blocks — Match, Nuclear, Economy, Battlefield, Developer — with every shipped setting present. Two controls (Nuclear Release, Ceiling) replace four nuclear checkboxes plus the Doomsday Clock. Sandbox becomes a Game Mode value rather than a checkbox; Skirmish is Game Mode → Skirmish, so neither needs its own control.

## Still open, and asked

1. **The DEFCON 2 casualty rule** — does a non-combat death (crash, crush, friendly fire) trip it, and does a destroyed *structure* count or only a unit? The one rule in the design that cannot be guessed.
2. **What "shared" means mechanically** when a player fires.
3. **Where the dividing line comes from** — authored per-map, ten maps, not started, and the largest single piece of work in the feature.
