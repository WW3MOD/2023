# DEFCON is two sequential ladders, not one ladder plus a counter

_Recorded 2026-09-08T13:29:26.701Z by ffb08fdc_

User's design, given 2026-09-08, and it SUPERSEDES part of Decision 02. Recording verbatim in substance because it is the first complete statement of the mode.

**Ladder 1 — DEFCON, about permission to fight at all.**
- Host picks the **starting level**; you need not start at 5.
- **DEFCON 5**: no hostilities whatsoever. Each team is confined to its own side of the map and physically cannot cross a dividing line. The line should follow natural terrain where the map offers it — the river on river-zeta is the named example. This is a build-up and positioning phase.
- **DEFCON 4**: similar; possibly some attacks permitted. Undecided.
- **DEFCON 3**: continues in that vein. Undecided.
- **DEFCON 2**: a first strike of some kind becomes possible, and making it is what takes the game to 1.
- **DEFCON 1**: full war, all bets off. **Explicitly NOT "launch the nukes"** — it means war is imminent or has broken out, nothing more.

**Ladder 2 — Nuclear escalation, a separate scale that begins after DEFCON 1.**
- Lowest rung: nobody may use nuclear weapons at all.
- Then 1 kt becomes available; then tactical 20 kt; then 50 kt; then 100 kt; then the game-enders.
- **The twist: using a nuclear weapon escalates toward the next rung faster.** Using the small ones is what unlocks the big ones.

**What this changes about Decision 02.** That decision said DEFCON was "a pure wall clock 5→1 that ordinary fighting does not touch". The user's design is a HYBRID: the upper rungs are scheduled, but 2→1 is driven by a player ACTION (the first strike). Do not carry the pure-wall-clock framing forward without re-checking it against this.

**The expensive part, and it is not the ladder.** The DEFCON 5 map divider is authored per-map data, not a rule: every map needs a front line, ideally following terrain. Ten shipped maps. Open: what enforces it (impassable overlay toggled at runtime vs. a soft refusal), and how a runtime-mutable blocking layer interacts with the nav-guard baseline and pathfinding. Scope this before committing to the feature.

**Open questions I raised back and which are NOT answered yet:**
1. Is the nuclear ladder SHARED (one global rung, so your first strike arms the enemy's bigger weapons) or per-player? Shared is doctrine-correct and is the whole dilemma; per-player is fairer and loses the point. Recommended: shared.
2. Do rungs 5/4/3 each move a distinct lever, or are they three flavours of waiting? Three levers exist: where you may go, what you may shoot, what you may buy. A rung that moves none of them is dead air.
3. "Start at DEFCON 1" IS conventional mode — so the compatibility escape hatch is a starting-level value, not a separate mode entry.

Lobby reorganisation was raised in the same breath and is a live sub-thread: the current grouping is Rules / Support powers / Match with display orders scattered 1..105, and the incoherence is that the four nuclear checkboxes sit under "Support powers" when they are escalation policy, while the Doomsday Clock sits under "Match" as a time limit.
