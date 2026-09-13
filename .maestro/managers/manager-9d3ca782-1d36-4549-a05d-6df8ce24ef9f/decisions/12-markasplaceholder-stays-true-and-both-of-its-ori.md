# MarkAsPlaceholder stays True, and both of its original reasons are gone

_Recorded 2026-09-10T10:11:10.361Z by ffb08fdc_

**The flag.** `DefconEscalationInfo.MarkAsPlaceholder = true` dims all four DEFCON dropdowns in the lobby and appends *"Not yet implemented — visual placeholder for a future feature."* to their tooltips.

**Both reasons it was set are now gone.** Two workers had independently declined to flip it, correctly, on two grounds: the mode had no in-game readout, and the wall was inert on every shipped map. As of `1aae0e0f` the readout ships and is verified in a live match; as of `ed27965d` the wall divides all ten maps and is verified by audit. Neither argument survives.

**It stays set anyway, on a different argument.** Two things the flag dims are still genuinely unsettled:

1. **The three pace durations are self-declared `UNTUNED PLACEHOLDER`s.** `SlowTicks` / `StandardTicks` / `FastTicks` are round numbers chosen so the phase is long enough to deploy from the Supply Route and short enough to sit through. Nobody has played the mode. The Escalation Pace dropdown therefore offers three choices whose difference nobody has felt.
2. **The ladder's opening rung is still an open question with the user** (`WORKSPACE/pipeline` / the ladder track).

Flipping the flag advertises the mode as finished while its clocks are guesses. **The mode being visible is not the same as the mode being tuned**, and the flag is currently the only thing telling a host the difference.

**Decision: leave `MarkAsPlaceholder: True`, and flip it in the same change that tunes the pace durations.** That is the wall worker's recommendation and it is right.

**What was changed instead.** The flag's `[Desc]` claimed the mode "changes nothing a player can see". That was false in both directions — the mode silently changed two major rules while claiming to change nothing, and now it visibly changes several — so the description was rewritten on `wt/defcon-wall-live` without touching the flag. The audit that found the false description (`WORKSPACE/reports/mode-audit-260910.md`) called this out as the worst single item in the whole survey: **a live feature wearing an inert label** is more dangerous than an unfinished one wearing an honest label.

**What would move this.** One session of the user actually playing DEFCON Escalation, at each pace, saying whether 5:00 at DEFCON 3 feels right. That is the only input the tuning needs and it cannot be derived.
