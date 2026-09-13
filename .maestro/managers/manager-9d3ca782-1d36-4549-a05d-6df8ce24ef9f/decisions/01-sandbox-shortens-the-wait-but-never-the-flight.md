# Sandbox shortens the wait but never the flight

_Recorded 2026-09-08T09:32:04.261Z by ffb08fdc_

User's call, 2026-09-08, in answer to a direct question.

**Taken:** leave the missile's approach at full length in Sandbox. `SandboxStandoffPercent` stays at its shipped `100` in `PowersLobbyOptions.cs:241`, which `MissileStrikePower.cs:283` treats as an early-return identity — so the shortening path remains a branch that has never executed in any build.

**Rejected:** halving the standoff to 50 (about 31 s of flight becomes about 15 on a large map), and collapsing it near-fully so the missile appears close overhead.

**Why it matters for future sessions:** the boundary Sandbox is allowed to cross is *dead air*, not *effect*. Purchase build duration (30–270 s) and `MissileDelay` were both removed because during them the missile does not exist and nothing is drawn. Flight is the effect the user judges — plasma streak, arrival angle, shake onset — so shortening it would trade the thing being evaluated for the agent's own iteration speed. Do not re-propose the lever as a convenience; if it is ever turned on, it needs a fresh reason and the user's word again.
