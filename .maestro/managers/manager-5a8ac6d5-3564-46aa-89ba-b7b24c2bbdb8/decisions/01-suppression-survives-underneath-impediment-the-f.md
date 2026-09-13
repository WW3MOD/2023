# Suppression survives underneath impediment; the fuller collapse is rejected as costed

_Recorded 2026-09-11T23:08:46.191Z by d994e671_

**User ruling, 2026-09-12, on question `fbGd2ZjxFKaQmwNFtjKW2`.** Chosen: *keep suppression underneath*. Verbatim: *"Yes correct, I was over zealous in my persuit. Do whatever is smartest."*

**The fork.** Ruling 1 of this strand said impediment *replaces* the existing ladder. The spec came back recommending it replace the **ladder** but not the **gates**, which is less unification than "one quantity" implies. That gap was put to the user rather than resolved silently.

**Options and why this one.**
- *Keep suppression underneath* (taken): `suppressed` keeps meaning exactly what it means today — incoming fire, warhead-granted, decaying. `impediment` is a new derived total of suppression plus a permanent damage floor. One number to display and act on; one number underneath still answering "is this unit being shot at right now".
- *Collapse fully* (rejected, but costed rather than dismissed — it lives at spec §6.5): seven sites read the raw suppression count as a threshold meaning "under fire" — three in bot code deciding whether a squad is pinned, four YAML gates. Folding damage into that number makes a soldier at 91% HP read as suppressed **forever**: he never took another round, so nothing decays, and his weapon stays gated off with a full magazine. Not a tuning problem — a unit that stops working. Undoing it means re-pointing all seven at a separate recently-took-fire signal, each pinned by an NUnit scan of the shipped rules.

**The root difficulty, worth carrying into every later design here:** the damage half is permanent until repaired and the suppression half decays. Two quantities behaving differently inside one number is what makes this hard, and **any design in this area must state which parts of the game read the decaying thing and which read the total.**

**Consequence for staging:** Stage 4 is purely additive, per the reclassification of the three `Modifier: 0` gates verified earlier today. The rejected option is not dead — it is written up and costed, and remains available if the simpler end state is ever judged worth the re-pointing work.

**The ruling also redirected the strand.** The user asked to stop staging mechanics and produce a final indicator layout — clean, intuitive, everything deliberately positioned — delivered as an HTML rendering, one row per style, variants side by side with short explanations, and *"mainly I want to see renderings of what the design will be in game."* That is now the active deliverable.
