# The casualty rule counts buildings; the nuclear ladder is one shared counter

_Recorded 2026-09-09T11:24:32.329Z by ffb08fdc_

Both answered by the user on 2026-09-09, closing the two rules decision 04 recorded as still open.

## DEFCON 2 → 1: anything destroyed by enemy action, buildings included

Not units only. Accidents and friendly fire still do not count. The user's reasoning against the units-only reading is the hole it leaves: with structures exempt, a player could spend the whole phase flattening buildings without consequence and hold the match in its opening indefinitely. Counting structures closes that at the cost of a demolished empty shed ending the phase like a killed crew, which was the argued drawback and was accepted.

Implementation is three clauses: attacker non-null and not the victim itself, which cleanly excludes every self-inflicted death; attacker is an enemy, which has to be asked for because nothing in the damage record flags friendly fire; and no filter at all on what the victim is.

**The rule asks for one thing the code cannot yet deliver, and this is recorded so nobody later reads the gap as a bug.** "Accidents do not count" ought to exclude being run over, and it cannot: crushing kills with the crushing locomotor's damage types, that field defaults empty, and no locomotor in this mod sets it — so a crush arrives naming an enemy vehicle as the attacker with no damage types, indistinguishable from a weapon that declares none. **As shipped, being run over by an enemy vehicle will end DEFCON 2.** The predicate is written so excluding it later is one added clause. Giving the ground locomotors a crush damage type is a separate change with its own blast radius, since death animations and husk selection read the same field.

## The nuclear ladder is a single shared counter

Every detonation adds to one pressure value, doubling per use, and both sides always read the same rung. Whoever fires, both climb together and are permitted exactly the same yields.

The rejected alternative is worth keeping because it can still be tried later without redoing this work: firing releasing the OPPONENT one rung further than yourself, so escalating hands them a bigger weapon than the one just used. That is the only version where opening with a nuke carries a downside, which is what a deterrent is — and the accepted version's known cost is exactly that going first is free, and may reward opening with a nuke rather than deterring it. One number on the HUD instead of two was the deciding factor, consistent with the user's standing preference for simplifying first and adding fine control after seeing how it plays.
