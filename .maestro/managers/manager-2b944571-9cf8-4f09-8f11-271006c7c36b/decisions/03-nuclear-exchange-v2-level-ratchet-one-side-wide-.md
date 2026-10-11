# Nuclear exchange v2: level ratchet + one side-wide cooldown, retaliation window removed

_Recorded 2026-09-14T23:43:59.159Z by 7a1529e6_

User feedback 2026-09-15 after a real match: too many nukes flying, no time to build forces; wants one nuke per team at a time, bands gated by the team's level, a general cooldown after any launch, and "using nukes escalates the enemy's level" kept.

Options considered:
1. Keep decision 01 (parity + retaliation window + per-band regen) and just lengthen every timer. Rejected: N bands × N timers still allows several nukes per side per minute at any posture; the window adds bursts on top.
2. Windowed escalation (enemy gets b+1 only for a window) + one side cooldown. Rejected: keeps two clocks per row in the ledger and the reply-or-lose pressure the user found absurd; the window was the main source of "nukes flying around".
3. **Chosen: permanent level ratchet + one side-wide cooldown.** Enemy level := max(L, b+1) forever; firer unchanged; side cooldown per band fired (3/4/5/6 min placeholders × posture); END = national ender at level 5, one shot, opens the final exchange. Simplest ledger (level + one clock), strongest anti-spam (one shot per side per cooldown), self-limiting escalation (aggressor arms the defender, never itself).

Consequences: Retaliation window lobby option removed; NuclearArmedBanner becomes a level-raised notice; NuclearBotModule loses its window-reply logic; bd1d4f89's band-level regen is subsumed by the side cooldown. Skirmish untouched. Spec: specs/02 (this manager). Companion lobby change: hide Skirmish-only controls in Escalation and Escalation-only controls in Skirmish.
