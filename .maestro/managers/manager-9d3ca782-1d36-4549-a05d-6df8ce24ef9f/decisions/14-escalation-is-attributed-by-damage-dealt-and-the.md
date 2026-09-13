# Escalation is attributed by damage dealt, and the apocalypse is one room with two doors

_Recorded 2026-09-10T19:24:41.464Z by ffb08fdc_

Recommended, not yet ruled. Companion to decision 13, which settled that a ladder exists per pair of sides; this settles how a launch is attributed to a side at all, and how the two paths to the ending unify.

## Attribution: by damage, not by aim point

The user asked how the game "calculates who was targeted" with more than two sides, and what happens when two players are hit at once.

**Aim points are a poor witness.** They land on neutral ground, on a unit that has moved, on the seam between two territories; a MIRV has six of them and they can land on three different sides. Any rule phrased in terms of intent has to answer a question the engine cannot observe.

**The rule: every hostile SIDE that takes at least 10% of a detonation's damage climbs one step on its ladder with the firer.** A relative floor rather than an absolute one, so it never needs per-yield tuning — 10% of a 1 kt and 10% of a 750 kt are both "meaningfully hit".

Consequences, all of which are improvements rather than costs:

- **Simultaneity stops being a case.** Two sides in the blast means two ladders climb. The question dissolves instead of needing an answer, which is the signal the rule is the right shape.
- **The aim point becomes a diplomatic act.** Dropping a warhead on the seam between two enemies escalates with both — deliberately, or by a misjudgement the player has to live with.
- **The demonstration shot exists without being designed.** A warhead into empty ground damages nobody, so it escalates nothing: you have spent a nuke purely to prove you have one. That is a real Cold War move and it falls out of the rule for free.
- **Escalation is caused by killing people, not by owning weapons**, which is the morally exact version and reads correctly in the event log.

The launcher's own side and its allies are never armed by its launch, whatever they absorb.

The 10% floor is the one tunable number and wants a play test, not an argument. Too low and stray splash on a scout drags a third party into a war; too high and a real glancing blow escalates nothing.

## The ending: one implementation, two doors

The user ruled that Doomsday should be available to any match as a maximum length, that it should grant every player the game-enders and the 15-second window, and that the current Dead Hand automatic strike can be removed in favour of manual targeting.

Accepted, and it collapses two endings into one. **Door 1** is a player firing a game-ender — the decision is theirs, which is the moral of Escalation. **Door 2** is a Doomsday clock expiring — the decision is the host's, made in the lobby an hour earlier. Both open into the same room: every surviving side armed, fifteen seconds, targets clicked, everything landing together.

This does not reintroduce the objection of decision 13's rejected section. That objection was to a Doomsday clock running *inside* Escalation by default as a second racing clock, which would steal the ending's authorship from the players. As an opt-in session cap that is **off by default in Escalation**, it steals nothing: a default Escalation match still ends only when a player decides it does, and a host who sets a one-hour cap has knowingly chosen otherwise.

**The one thing manual targeting loses.** The automatic salvo guaranteed the map was destroyed. Manual targeting does not: an AFK player, a dead player, or one who picks two targets out of three leaves warheads unfired and the map partly standing, which undercuts the ending badly. Keep the guarantee by reusing what exists — when the fifteen seconds expire, every unspent warhead fires on its own using the `DoomsdayStrike` cities-and-outliers aim-point pass already in the tree. Agency where a player wants it; apocalypse regardless.

Code consequence: `DoomsdayStrike` today runs a clock, fires a salvo, and resolves a winner from the frozen score. The clock becomes a lobby setting usable by any mode, the salvo becomes the leftover-filler behind the manual window, and the winner resolution differs by door — Skirmish-Doomsday keeps its winner, an Escalation apocalypse records everyone as Lost.

## Also recorded: the stalemate answer, from the user

A stalemate in which neither side can win conventionally and neither will fire needs no engine rule. A player who has lost patience fires — "I either win or we all die at this point" — and the mechanic supplies its own escape hatch. Nothing to build.
