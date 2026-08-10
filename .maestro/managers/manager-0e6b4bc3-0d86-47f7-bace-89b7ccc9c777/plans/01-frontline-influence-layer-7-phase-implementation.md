# Frontline influence layer — 7-phase implementation plan

_plan · status: active · authored 2026-08-03T01:29:20.050Z_

Source research: WORKSPACE/research/frontline-influence.md @ b2054d16 (authoritative detail — bars, file:line causes). Architecture: Option A on-ramped by Option C slices.

- **Phase 0 — CrossingMap** (build+NUnit only): per-locomotor connected components, crossing cells (bridges incl. destroyed/repairable via LegacyBridgeHut/Bridge), amphibious-crossable edges; computed at map load; @stable never builds it. Bar: River-Zeta fixture — land components=2, 2 central crossings enumerated, flank destroyed-bridges flagged repairable, amphibious component=1.
- **Phase 1 — Reachability-gated + amphibious-typed targeting** (single autotest): PoiReachabilityFactor in PoiMap scoring (default inert, StrategicRepointEnabled gating shape); amphibious units assigned to water-only targets. Bar: amphibious IFVs cross to a far-bank POI within T ticks; land axes never sent to unreachable POIs; flag off ⇒ byte-identical.
- **Phase 2 — Free-pool forward staging + rally advance** (single autotest): uncommitted units move to a forward staging point behind the frontier (FrontierStandoffMath/GroundDangerNav reuse); muster advances with the frontier. Bar: idle-unit median distance from SR ≥Y; SR-radius congestion count below threshold (measure with UnitLifecycleLogger JSONL).
- **Phase 3 — Retreat-oscillation damper** (single autotest): hysteresis + min dwell on retreat FSM, effective min-axis-strength before retreat. Bar: SR-bubble re-entries per axis below threshold; genuinely-losing axes still withdraw.
- **Phase 4 — Frontline strength profile** (build+NUnit): per-frontier-sector believed own-vs-enemy strength + avenue mapping (Phase-0 crossings → sectors) on ControlField's existing cadence.
- **Phase 5 — Man-the-line + weakest-point attack** (benchmark ladder, USER-GATED): defend spread across avenues; attack vectors biased to weakest sector; posture holds where strong.
- **Phase 6 — Engineer route-opening** (single autotest → ladder): weak flank sector + repairable crossing ⇒ send e6 to LegacyBridgeHut with screen, or commit amphibious pool.

Execution: Phases 0+1 one worker (B-consumes-A, worktree frontline-p01); Phases 2+3 one worker in parallel (worktree frontline-p23, different code regions); each through adversarial review before merge. 4 next, 5/6 after Brain progress + user grants.
