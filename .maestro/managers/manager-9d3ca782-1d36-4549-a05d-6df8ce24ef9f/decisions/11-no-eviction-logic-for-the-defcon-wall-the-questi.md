# No eviction logic for the DEFCON wall: the question collapsed rather than being answered

_Recorded 2026-09-10T09:40:49.748Z by ffb08fdc_

**The question.** A ground unit is standing in the DEFCON 3 wall's column at the instant it goes up — shove it, kill it, or tolerate it? Raised by the wall worker on `wt/defcon-wall @ 1a3d9b72`, backlogged as `887117e3`, and put to the user on 2026-09-10 because destroying a player's units at a scripted transition is a ruling rather than an implementation detail. `GroundLevelBridge` kills invalid actors in its footprint, so there was engine precedent for the harsh option.

**The user picked none of the three.** Verbatim:

> "I dont really get the issue here. We start at DEFCON 3 at most, so it is either active from the start or if we chose to start in defcon 2/1 it is not active at all. So no units should be able to end up trapped there, unless there are units inserted on the map that it loads with. But that is a later problem, or something you can solve, but it is an edge case."

**They are right, and the code says so.** `DefconWallInfo.ActiveLevels = { 3 }` (`DefconWall.cs:85`); `Apply()` is called from `Created` and from the level-change path, and sets `active` only when the current level is in `ActiveLevels`. The escalation ladder only ever DESCENDS — 3 → 2 on a clock, 2 → 1 on a casualty, terminal — and `StartAtDefault` is bounded above by `DefconEscalationState.Ceiling`, which is 3. So the wall is either standing at tick 0 or it never stands at all. **There is no moment at which it rises under a unit.**

**Decision: build no eviction logic.** No shove, no kill, no tolerate-with-machinery. The three options were answers to a situation that cannot occur.

**What survives.** Map-authored actors already standing in the band at load — a real case, and the user classed it as an edge case to solve cheaply or defer. And the wall coming DOWN at 3 → 2 rewrites the column back to passable, which strands nobody.

**The related unverified worry is also resolved by the same argument.** The backlog item flagged that units already pathing THROUGH those cells when the wall goes up were unstudied, and guessed that might be the more disruptive half. It cannot happen either, for the same reason.

**The lesson worth keeping.** The question was well-formed, had three genuinely different answers, cited precedent, and was still the wrong question — because nobody checked whether the triggering event could occur before designing responses to it. The worker that raised it was right to refuse to decide, and wrong about what it was refusing. **Before ruling on what to do when X happens, establish that X can happen.**
