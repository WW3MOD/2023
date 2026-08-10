# Influence stack replaces Phase-4 value-channel patch — full-map commander's-view layers

_Recorded 2026-07-21T22:54:11.167Z by ee31feaf_

**Context.** Phase-4 recon (0e5dbc99) posed a gating decision: patch a value channel onto the Phase-1 armed-only SightingThreatLayer, or rebuild toward per-viewer influence. The user then delivered a much more ambitious vision in live discussion (2026-07-22): full-map ownership (Voronoi seed, persistence, verified-clear grayzone), commander's-belief semantics (lost-visual contacts assumed in place), threat-weighted per-unit auras (radius←weapon range, density←lethality×durability), a dedicated anti-air danger channel to fix helicopter suicides (flagship defect: helis overfly enemies firing opportunistically instead of standing off at missile range), danger-gradient rear-lateral routing for high-value units, and assumed threat projection through fog (enemy arty envelope from believed-enemy territory).

**Decision.** Build the full influence stack — belief store (A), per-domain danger fields (B), control field (C), overlay (D-component) — instead of the value-channel patch. Staged 0/A–F per WORKSPACE/plans/260722_influence_stack_design.md @ 4c3ea1a5. Stage 0 = heli standoff micro, layer-independent quick win. Phase-4 role consumption unchanged, still first. Repoint-don't-rebuild holds: controls/@stable never touch the stack; byte-identity preserved. Stage F (strategic repoint) carries the declared re-baseline and revives the parked terr-bias (exp-terr-bias @ ccd12c98) on the substrate it actually needed.

**Alternatives rejected.** (1) Thin value-channel patch — cannot express full-map ownership, belief persistence, or the air channel; would be rebuilt anyway. (2) Live-field (no persistence) — flickers, violates commander's-view semantics the user specified. (3) Terrain-aware propagation in v1 — deferred to v2 on cost.

**Same window:** Phase 3 closed on re-price 40800107 (S1 non-regression, @stable byte-identical all 10 seeds; S2 re-engaged: eng median 675→1700, swing −350→0, 4017 re-engages; no PROPOSED bar flips — Experimental ≈ Stable on force efficiency, off the validity floor). Pricing worker archived.
