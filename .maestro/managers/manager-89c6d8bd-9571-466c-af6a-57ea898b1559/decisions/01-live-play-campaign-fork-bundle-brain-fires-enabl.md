# Live-play campaign fork bundle + brain-fires enablement at merge

_Recorded 2026-08-04T06:07:14.507Z by be958765_

Decisions taken while converting five verified investigations into implementation lanes. Alternatives noted so future sessions don't relitigate.

**Brain-fires batch enablement (chosen: land flags OFF).** Options: (a) merge with PreparatoryFires/SuppressionCoordinatedAdvance ON + quantize band 25 as implemented; (b) merge flags OFF (`PreparatoryFires: false`, `SuppressionCoordinatedAdvance: false`, `AllocationScoreQuantizeBandPct: 0`) in @experimental, @stable untouched. Chose (b): the top live regression is axes that never close — adding new hold disciplines unpriced risks worsening it; reviewer found the shipped band-25 quantization defective (no-op on headline, amplifies near-ties, inflates worthless axes). Behaviorally-neutral merge also unblocks Waves A/B immediately. Sweep to price enablement is user-gated.

**OOA (Wave A).** F1: bot-module-only sweep in BotTick (alternative: engine-level AmmoPool activity change — rejected, human units must stay player-controlled). F2: fallback with no reachable rearm source = terminal evac + sell (record posted, conf 80). LC capture tier appended below money buildings in CaptureCoordinator behind CaptureSupplyDepots flag.

**SR-pooling (Wave B).** F(a): damper reshaped to fill-completion + max-evals cap; muster-anchor self-seed null-check. F(b): minimal SHORAD line-fold default-off now; full escort module deferred to PIPELINE. Flow shape: forward-assemble with capped wait at forward muster (record posted, conf 75) vs advance-singly.

**Supply-hunt.** T1 infantry auto-seek: INotifyIdle-driven only (no per-tick scans), leash 20 cells, stance-gate matrix (Resupply=Auto AND Engagement≠HoldPosition AND Fire≠Ambush), origin-return after replenish, master flag default-OFF with human default-ON flip as separate one-line commit pending the posted tactical-layer record (conf 85). T2 truck hunt = next wave.

**Composition.** Hybrid role-keyed counter matrix (enemy 3-way class > own role), YAML targets now / brain-fed later, CounterBiasMaxPct 200, value-weighted census in ActorID order, @stable frozen as control, mortar target 50‰. SelectDeficit uses least-over fallback (volume unchanged) rather than requiring positive deficit — deliberate deviation from investigator proposal, flagged for reviewer. Helis deferred by omission to idle-evac lane (enable-ai-any shared-block hazard).

**Idle-evac.** Demand-gated transport purchase mandatory, transports-first employment, ~900-tick idle threshold; lands after composition (UnitBuilder overlap).
