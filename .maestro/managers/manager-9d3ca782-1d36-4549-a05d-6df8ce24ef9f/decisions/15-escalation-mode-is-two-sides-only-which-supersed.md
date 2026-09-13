# Escalation mode is two sides only, which supersedes the per-pair machinery of decisions 13 and 14

_Recorded 2026-09-10T19:38:44.063Z by ffb08fdc_

Recommended in response to the user's question, and it narrows two earlier decisions rather than extending them. Anyone reading decision 13 or 14 should read this one immediately after: the per-pair ladder they specify is correct engineering for a problem this decision removes.

## The question that broke it

The user asked: in a free-for-all, if P1 fires at P2 and P2's ceiling rises, **can P2 then fire that warhead at P3, who has been entirely peaceful?** And if not, how is that enforced?

Decision 13 said "ceiling per side-pair, stock per player" and never said what stops a warhead earned in one relationship being used in another. That is a genuine gap in the specification, not a detail — the user's question found it.

## Why per-pair cannot be rescued

The obvious patch is a targeting restriction: a warhead may not be aimed at a side whose pair-ceiling is below that yield. Two objections, and the second is fatal.

**It is unintuitive.** The player owns a missile and the game refuses to let them point it somewhere. Every refusal needs an explanation, and the explanation is a rule about a relationship the player is not currently thinking about.

**Blast does not respect ownership.** A 20 kt aimed legally at P1 will damage P3's units standing nearby. Either that spill counts — in which case a peaceful side has just been hit by a yield it never escalated to, which is the exact abuse the restriction existed to prevent — or it does not count, in which case the restriction is cosmetic and can be defeated by aiming next door. **Per-pair cannot deliver its own promise.** It is not a UI problem; the physics of an area weapon do not have a per-relationship channel.

Per-side ceilings have the same abuse without even the pretence: escalation becomes global proliferation and the peaceful player is fair game.

## The decision

**Escalation mode requires exactly two sides.** Any number of players, assigned to two teams. FFA and three-or-more-team lobbies do not offer Escalation.

Enforced in the lobby, not in the mechanic: the mode is unavailable, or refuses to start, unless the players resolve into two teams.

## Why this is the right scope rather than a retreat

- **The fiction already says so.** The mod ships exactly two factions — `world.yaml:257-275` has two `Faction@` blocks and `RandomFactionMembers: america, russia`. World War 3 is a two-bloc war. A six-way nuclear free-for-all was never the story this mod tells.
- **Every open problem in the thread disappears at once**: attribution beyond "did the enemy side take damage", targeting restrictions, the collusion vector, the innocent third party, and the N² display. Not mitigated — gone.
- **The mechanic is unchanged.** Everything the user designed for 1v1 works identically at 2v2, 3v3 and 5v5, because there is one ladder in all of them. Decision 14's damage attribution still stands and gets simpler: one hostile side, one 10% test.
- **It is a lobby constraint, which is the cheapest kind of rule to build and the easiest to explain.** "Escalation needs two sides" is a sentence; the targeting-restriction alternative is a system.

## What free-for-all players get instead

Skirmish, with the nuclear weapons setting at their chosen level, and Doomsday switched on if they want an ending. That is nuclear weapons and a nuclear apocalypse in a free-for-all — everything except the ladder. **The ladder is a two-bloc mechanic**, and saying so out loud is more honest than shipping one that pretends to work with six.

## The alternative if the user wants FFA anyway

Allow it with **per-side ceilings and no pretence of protection**: escalation is proliferation, what you hold you may use on anyone, and a peaceful player being annihilated with a warhead from someone else's war is simply what proliferation means. Thematically dark and defensible, mechanically trivial, and it needs no enforcement at all. Recorded as the fallback rather than the recommendation because it makes the mode's worst experience — being destroyed by a war you stayed out of — reachable by accident rather than by anyone's decision.
