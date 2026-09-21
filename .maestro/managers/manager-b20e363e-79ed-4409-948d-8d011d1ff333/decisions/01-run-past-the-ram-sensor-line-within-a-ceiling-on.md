# Run past the RAM sensor line, within a ceiling, on the user's instruction

_Recorded 2026-09-19T15:41:17.105Z by 928f56a1_

**Context.** Every `get_machine_load` reading during the 2026-09-19 backlog drain said `critical, headroom_agents 0` (1.01–1.18× committed), including with ZERO workers of this manager's running. Following the sensor literally throttled the drain to one worker at a time; the user asked for five and a full budget burn.

**User's ruling (request ZE4yMxvHr3LSNXOACdRvs, verbatim):** "I think (NOT SURE) it is more efficient to run a bit beyond the RAM limit, just not too much? Seems to me you are too careful now, not allowing it to go beyond at all. Since we added those instructions/guards the total pace of token burning has gone down a lot. So try to find the optimal balance of 'overloading' the ram without freezing the computer up."

**Options considered.** (a) Keep obeying `headroom_agents` → serial, contradicts the mandate. (b) Ignore the sensor entirely → the 2026-08 crash risk the guards exist for. (c) Offload to the MBP → user did not pick it; needs a push-policy bend.

**Decision: (b) bounded.** For this manager: fill up to the hard cap of 5 workers while the committed/physical ratio is ≤ 1.20× (the level at which 4 workers + 2 concurrent Release/Debug builds ran on 2026-09-19 with no incident); do not START a worker or a game launch above 1.20×; above 1.30× archive idle children first and hold. Re-read the ratio before each fan-out or launch, not on a schedule. `verdict`/`headroom_agents` remain informational; the ratio in `detail` is the number acted on. If the machine ever pages hard (a launch or build stalls > 2× its normal time), drop the ceiling to 1.10× and record it here.

**Why 1.20×.** It is observed, not derived: the highest reading this session under real load, with the builds finishing normally and the game launching normally afterwards. Anything higher is untested.
