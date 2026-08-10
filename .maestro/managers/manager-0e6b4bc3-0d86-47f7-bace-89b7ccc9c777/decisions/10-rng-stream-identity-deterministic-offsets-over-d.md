# RNG-stream identity: deterministic offsets over declared re-baseline; territory baseline feeds ground channel only

_Recorded 2026-07-22T02:10:33.125Z by ee31feaf_

Context: Stage C review (893d5e07) adjudicated that the merged A/B substrate (BeliefStore.cs:165, DangerFieldLayer.cs:283) each draw SharedRandom once in WorldLoaded, unconditionally in every match — advancing the synced stream by 2 draws vs pre-A/B, so post-A/B @stable/control games are not byte-reproducible against the 40800107 re-price or ladder baselines.

Decision 1: convert both draws to distinct deterministic stagger offsets (e.g. 0 and UpdateInterval/3; ControlField already at UpdateInterval/2+1). Alternative rejected: declared benchmark re-baseline — costs a ~43-match batch, and the offset conversion achieves full stream identity for free with no behavioral downside (the draws only staggered recompute phase). Consistent with the philosophy Stage C already adopted for ControlField.

Decision 2: ProjectTerritoryBaseline contribution feeds the GROUND channel only (was both). Rationale: air danger's contract is "can hit an airborne target" (§2B); a ground-gun-derived envelope in the air channel paints AA-free rear areas as air-dangerous, defeating the heli-safety consumer (§1.4 flagship defect). If an air baseline proves wanted, Stage D derives it from believed anti-air envelopes. Design doc to be annotated when Stage D is briefed.

Both changes ordered as a follow-up commit on stage-c-control-overlay before merge, plus a DISCOVERIES.md entry recording the RNG-stream insight (2-draw shift, offset fix, why future drift must not be misattributed to gameplay changes).
