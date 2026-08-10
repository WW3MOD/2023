# Split-plan forks ratified: default-ON micro, full fog migration, global cohesion fix

_Recorded 2026-07-21T12:55:04.604Z by ee31feaf_

User answered the three strategic/tactical-split forks (2026-07-21):

1. **Cohesion fix scope → global bug fix + deliberate re-baseline** (matched agent default). Bound ComputeBoxSlots extent + regroup for everyone; ladder re-baselines; folds into queued dispersion re-verify.
2. **Human default autonomy → Default ON** (agent had recommended decide-after-playtest). Rationale: the per-unit-type stance-default mechanism (Ctrl-Alt-click → UnitDefaultsManager, AutoTarget.cs:358-388) already lets players change defaults per type, so reasonable authored defaults + easy opt-out covers the risk. Alternatives (decide-later, default-off) rejected by user.
3. **Bot fog policy → FULL migration now** (agent had recommended hybrid). InfluenceMap + ThreatMapManager become fog-respecting as part of split-plan Phase 4, absorbing ladder cycle 5. Accepted consequences: initial bot-strength dip, benchmark-wide re-baseline, recon/scouting becomes a needed bot behavior cycle. Alternatives (hybrid-first, status quo) rejected by user.

Clarifications also locked into spec: L3 may act in-transit as stance-conditioned detours (never cancels orders); hold-Space intel overlay (BoP green/red wash, computed grayzone, GPS-dot sightings reusing OpenRA.Mods.Cnc GpsDot/GpsWatcher/GpsDotEffect — confirmed in-repo), dev always-on switch.

Spec: .maestro/managers/manager-0e6b4bc3.../specs/01-strategic-tactical-split-... (still draft; awaiting overall go to start Phase 0).
