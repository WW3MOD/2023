# Everything but the diamond hides at rest; Shift reveals all

_Recorded 2026-09-14T08:50:41.282Z by d994e671_

**User ruling, 2026-09-14, on question `_mgNDQCQH2AEgZ0uiLdeb`.** Neither offered option picked. Verbatim: *"Hide everything else for now unless shift is held - shift reveals all. We will redesign the stances, but for now keep them same and just hidden unless shift is held."*

**This supersedes the question as posed.** The fork offered was *stance survives beside the diamond* versus *stance is evicted*. The user took neither and changed the axis: **nothing is evicted, everything except the diamond is hidden at rest, and a held Shift reveals the lot.** The diamond is the only mark on screen in normal play; every other mark still exists and is one keypress away.

**Consequences that change earlier conclusions:**

1. **The clearance argument against the 9×13 body is weaker than stated.** It was ruled out because the stance glyphs sit in roughly 1 px of headroom (`defaults.yaml:941-943`). At rest that collision no longer exists, because stance is not drawn. It returns only while Shift is held — so the question becomes whether a momentary revealed-state collision is acceptable, which is a much smaller objection than a permanent one.
2. **The per-signal at-rest visibility table in `indicator-layout-plan.md` §3 is superseded.** That table reasoned signal by signal about always / hover / selection / modifier. The ruling replaces it wholesale with one rule: diamond always, everything else on Shift.
3. **Stance is explicitly deferred, not settled.** *"We will redesign the stances"* — keep the current four glyphs and vocabulary unchanged for now. Do not restyle them, and do not treat the deferral as licence to design them.

**Cost is not yet scoped and must not be assumed free.** The layout plan's §7 costing put *"stance and health behind selection"* at free-in-YAML via `RequiresSelection: true`, but **behind a held modifier key is a different mechanism** — the same table put the hover variant at small C#, because the rollover path drives bars rather than decorations. Whether a Shift-held reveal is YAML, a small trait, or input-layer work is an open question; nobody has read that path. Do not repeat the free-in-YAML figure for it.

**One risk worth recording now.** A mark that is invisible at rest cannot be noticed by a player who does not know to hold Shift. The diamond carries the load in normal play, and everything hidden behind Shift is discoverable only if the player is told. That is a product question the user has effectively already taken — but it should be re-raised if anything load-bearing (ammo-empty, in particular, which the audit calls an emergency rather than a degree) ends up hidden behind it.
