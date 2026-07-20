# Loop Doctrine — action types & cadence for autonomous bot development

Standing routine for the AI-improvement loop. Each action type has a trigger, an output, and a size. The loop's default rhythm is **RECON → IMPLEMENT+VERIFY → merge**, punctuated by the maintenance actions when their triggers fire. This document is durable policy; the dated state lives in `REVIEW.md` and `reports/`.

## Action types

| Action | What | Trigger | Output |
|---|---|---|---|
| **RECON** | Read-only design study: trace the target behavior to code (file:line), propose minimal mechanism + structural alternative + risks | Before every behavior cycle | Implement-ready plan in `WORKSPACE/plans/` |
| **IMPLEMENT+VERIFY** | One behavior change, on a branch, compile + unit tests, then hidden-window benchmark vs the scenario bar | An implement-ready plan exists and the run slot is free | Branch merged to main on pass; findings logged |
| **BASELINE** | N≥10 statistical run of the current bot on the active scenario (primary + mirror) | After any scorer/map/win-rule change; every ~5 behavior cycles; before promoting Stable | `runs/` analysis doc + updated LADDER numbers |
| **CALIBRATE** | Control-vs-control run to measure map/side fairness | New scenario added; fairness in doubt | Fairness verdict in `runs/`; mirror policy set |
| **RETHINK** | Architecture review: is the current structure (bot modules, scoring, mission flow) still the right vehicle, or is the next gain structural? Explicitly weighs radical options against incremental ones | Every ~5 cycles; OR 2 consecutive cycles fail their bar; OR a recon flags "patching is the wrong move" | Decision doc: continue incremental / schedule structural cycle |
| **CURATE** | Verify unpromoted `DISCOVERIES.md` entries against code; promote to `DOCS/reference/`; reject freely | ~10 unpromoted entries; end of a work batch | Updated reference docs, entries tagged |
| **PROMOTE** | Snapshot Experimental → Stable (SPEC §13) so the ladder gets a stronger control | Experimental beats Stable convincingly on the ladder (post-BASELINE) | New Stable roster + LADDER note |
| **EXPAND** | Add the next ladder rung (new scenario) when the top rung stops discriminating | Current scenario's bar passed reliably; behaviors outgrow what it measures | New scenario + CALIBRATE + bar definition |
| **REPORT** | Wide-but-short system report for the user: state, numbers, roadmap | Major milestone; user request; every ~10 cycles | `reports/` doc, presented as artifact |

## Cadence rules

- **One behavior change per IMPLEMENT cycle.** Attribution dies when two changes share a batch.
- **RECON is cheap, run it eagerly** — it parallelizes with anything (read-only) and its plan docs survive re-routing (e.g. the SR-contestation plan parked when baseline routed cycle 1 to capture-reliability).
- **The run slot is singular.** One game process machine-wide; builds and batches never overlap (host memory pressure incident, 2026-07-20). Max 2 concurrent workers while either is heavy.
- **Eager merge on assumed improvement**; the benchmark catches regressions at the next BASELINE. Unverified behavior changes stay on their branch until their own verify run.
- **Radical-change mandate (user, 2026-07-20):** aim beyond patching the inherited OpenRA botmodule pattern. Every RETHINK must seriously cost at least one structural option; every RECON includes a "structural option" section. Incrementalism is a choice to be justified, not a default.
- **Bars are ratified, not improvised.** A degenerate or outgrown pass bar gets a recommended replacement flagged in REVIEW + an assumption-question to the user; the loop proceeds on the recommendation.

## Escalation triggers (interrupt the rhythm)

- Benchmark stops discriminating (control ≈ experimental for 2 baselines) → EXPAND or RETHINK.
- A fix needs unseeded-randomness determinism → schedule the seeded-LocalRandom backlog item before more statistics.
- Code-vs-doc contradiction found → doc fix in the same work item (knowledge-bank rule).
- Host resource pressure → throttle first, ask questions later.
