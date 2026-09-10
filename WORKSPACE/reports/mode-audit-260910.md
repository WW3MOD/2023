# What each game mode actually does — audit, 2026-09-10

Read-only static audit against `main @ 53f337a6`. Nothing was launched, built or linted; every
claim is a code read. Inferences are labelled as such.

**Headline.** DEFCON Escalation is not an unfinished feature sitting inert — it is a **live
feature wearing an "inert" label**. Selecting it silently changes two major rules (units stop
firing autonomously; every nuclear weapon disappears for 15+ minutes) while all four of its
dropdowns render dimmed with "Not yet implemented — visual placeholder for a future feature."
Separately, **Doomsday ships enabled with its clock defaulting to "No limit"**, which is the one
combination in which it cannot fire at all.

## 1. DEFCON Escalation — live rules, zero feedback, labelled not-implemented

- State machine complete and correct: opens at Start At, clock to DEFCON 2 (Standard 5000 ticks
  = 5:00 at 16.67 tps), holds at 2 until any actor is destroyed by enemy action
  (`DefconCasualtyObserver.cs:79-96`), then DEFCON 1, terminal.
- **Hold-fire at DEFCON 2 is live and unconditional** — `DefconFireDiscipline.HoldsFire` read at
  six sites (`AutoTarget.cs:668,1018,1229,1403`, `AttackFollow.cs:210,234`,
  `GarrisonManager.cs:961,1313`), with running engagements cancelled once on entry.
- **Inert:** `defcon-1/2/3` conditions have **zero consumers** repo-wide;
  `DefconEscalation.PermitsNuclearYield` is dead code and is the only route into
  `NuclearReleaseLadder.Permits`, so the Tsar gate inside it is unreachable (the shipped gate is
  the YAML condition); `TicksUntilNextLevel`, `NuclearPressure`, `TicksUntilNuclearRelease` are
  `[Sync]` only — hashed, never displayed.
- **`DefconWall` is inert on all 10 shipped maps** (`world.yaml:774`, `Start`/`End` both `0,0`),
  so DEFCON 3's entire stated meaning — "the border is closed" — does not exist in a real match.
- **Could a player tell? No.** Four `Log.Write("debug", …)` calls and not one
  `TextNotificationsManager`, `PlayNotification`, `FlashTarget` or widget reference across seven
  DEFCON files. No chrome file mentions DEFCON. `Widgets/**/*Defcon*` returns nothing. And the
  in-game Game Info → options tab is pinned to `CATEGORY_FILTER: Common`
  (`ingame-info-lobby-options.yaml:30`) while every DEFCON id is Advanced, so a player cannot even
  look up mid-match which mode they are in.

## 2. Nuclear ceiling / release ladder — fully built, gates real content, unexplained

Complete and tested (15 NUnit cases). In Escalation the ladder is shut until DEFCON 1 **and** a
further 10000 ticks (10:00). Band conditions are consumed by **13 real powers**. The only feedback
is cameos appearing and vanishing: a player sees every nuclear power missing for at least 15
minutes with no countdown and no indication a ladder exists.

`nuclear-ceiling` is missing from `LobbyOptionsLogic.OptionSection`, which is why it falls into the
implicit "Other" section while its three siblings sit in "Game Rules".

## 3. Doomsday — enabled by default, clock off by default, so it cannot fire

When it fires it is finished and spectacular (statistics freeze, tactical wave, 40-tick pause,
strategic wave, everything destroyed, verdict from the frozen score, its own spoken lines). But
`TimeLimitDefault = 0` and ww3mod never overrides it, and `TimeLimitManager.Tick` early-returns on
`TimeLimit <= 0`. `CountdownLabel`/`CountdownText` are never set, so the dedicated countdown is dead
code here; a host who sets a clock gets the generic unlabelled `GAME_TIMER` counting down with a
tooltip that never mentions Doomsday.

## 4. Sandbox (the Game Mode value) — does nothing, and shares a name

Pins the level and never escalates; ladder fully released. Net effect versus Skirmish: nothing —
except that at Start At 2 the hold-fire rule keys on level alone, so **units never fire
autonomously for the whole match**. Not the same feature as the "Sandbox: All Support Powers"
checkbox.

## 5-8. What is genuinely finished

- **Skirmish** — a strict no-op, pinned by tests. No gap. This is what the user has been playing.
- **Sandbox: All Support Powers** — works. `SandboxStandoffPercent = 100` is the identity by the
  user's 2026-09-08 ruling; do not "fix" it.
- **Tactical / Strategic / Nuclear Arsenal checkboxes** — work, fail-safe polarity, two autotests.
- **Supply Route contestation** — the counterexample and the bar to clear: control bar, defeat bar,
  colour transitions, building flashes, radar pings, speech, five distinct text lines.

## 9. Two master switches that control nothing

"Powers Enabled" and "Friendly Fire" come from `LobbyDummyOptions`, which stamps `Placeholder` on
everything it yields. Zero consumers outside the lobby UI. "Powers Enabled" reads as a master
toggle over the entire support-power system and governs nothing.

## Watch

- **Not verified by running anything.** Section membership, dimming and the Game Rules / Other
  split are read from `LobbyOptionsLogic`; row order within a section is inferred from its sort.
- **"A player cannot tell" is an argument from absence** — strong (zero notification/widget calls
  across seven files, zero DEFCON references in 16 chrome files) but not the same as watching a
  match. The settling run: an Escalation scenario with `StartAtDefault: 1`,
  `NuclearReleaseDelayTicks: 60`, asserting whether anything appears when the ladder opens.
- **`DOCS/reference/game-model.md` documents none of these modes** — it is 66 lines on how WW3MOD
  differs from Red Alert and contains no occurrence of DEFCON, Doomsday, Skirmish or Sandbox. The
  design of record is the decision log plus the mockups. No disagreement to reconcile, only a gap.
