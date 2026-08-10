# POI plan approved: Path A on v2, SR deny-only by design, fully score-floating axes

_Recorded 2026-07-19T10:19:13.357Z by ee31feaf_

User resolved the three forks on `WORKSPACE/plans/260719_experimental_ai_poi_strategy.md`:

1. **Architecture — Path A**: bolt the POI system onto the live v2 bot, plus the ~120-line per-unit goal-guard (fixes TECN `IsIdle` re-issue thrashing at the root; ports into v3 later). Path B (v3-brain-first) rejected for now because the live Experimental AI must improve immediately.
2. **Neutral SR capture — deny-only is PERMANENT design, not a deferral.** SR flipping to Neutral is intentional realism: neutralizing symbolizes cutting the enemy's reinforcement route; the capturer can never reinforce through it. No capture-to-own-side engine change, ever. AI behavior wanted: attack SR → turn neutral → hold with a *small garrison* so the defeated player's ALLIES cannot recapture it (recapture brings the defeated player back into team games).
3. **Spread offense — fully score-floating axes.** No dedicated enemy-base axis; every target including the enemy base competes on POI score. User explicitly accepts risk of passive/imperfect games early — priority is building the foundation for a genuinely decision-making AI, tuned over time.
