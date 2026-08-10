# Ambush forks A-D resolved on user delegation

_Recorded 2026-07-24T23:56:37.841Z by ee31feaf_

User asked "Do you think it will work? If so, queue it up" and explicitly delegated ("forced to trust you"). Agent verdict: yes — design is engine-verified (plans/260722_ambush_undetected_design.md), core primitives already exist (idle ambush pre-aim + group volley, AutoTarget.cs:511-580; O(1) visibility test, MapLayers.cs:571-577).

Fork picks (plan §7), recorded in PIPELINE item 8 @ 6813b94b:
- **A (prone):** cosmetic only on infantry ambushers. Plan's cheapest pick was "skip"; cosmetic chosen because it makes the ambush READ in-game (user values realism/immersion) and keeps the ProneDamageModifiers benefit, with no false concealment claim. Real prone-concealment (a new detection-modifier mechanic) explicitly parked for the upcoming stealth-mechanics discussion — user asked for the strands to stay separated.
- **B (scope):** halt-before-contact on attack-move + auto-move only; plain Move always obeyed (per plan recommendation — silent disobedience of explicit orders rejected).
- **C (timing):** peak-density initiation default; rear-arc shots only when free via L-shape geometry. User's "spring past peak for rear shots" intuition rejected per plan §3.3 — the AT Specialist's ATGM pauses at suppressed>=10, so late spring silences exactly the unit the tactic needs.
- **D (audience):** human-settable + bot behind the same default-off gate from day one (matches item 8's pre-existing framing; user wants to feel it in skirmish).

Alternatives and rationale live in the plan §7. User may override any pick before dispatch; item 8 runs after items 5 (in flight) and 6.
