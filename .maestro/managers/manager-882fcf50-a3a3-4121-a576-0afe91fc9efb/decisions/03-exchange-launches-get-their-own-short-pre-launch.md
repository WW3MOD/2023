# Exchange launches get their own short pre-launch countdown; the cascade floor drops from 800 to 350 ticks

_Recorded 2026-09-20T12:07:19.660Z by 60a95888_

**Problem.** The rebuilt Final Exchange anchors every warhead at `max(trigger's first impact, close + FinalExchangeFlightTicks)`. With the game-enders' `MissileDelay 500` (30 s pre-launch countdown) inside the floor, the shipped floor had to be 800 ticks, putting the first impact ~63 s after the trigger fires (35 s today) — 48 s of dead time after a 15 s window.

**Options.** (a) Keep 800 — warning time; (b) lower `MissileDelay` on the game-ender powers — rebalances them in every ordinary match; (c) a `FinalExchangeMissileDelay` (100) that the cascade substitutes ONLY for game-ender launches inside an open exchange, with the floor at 350 (covers the slowest arc on the largest map, B83 on x-lake = 292, plus margin).

**Picked (c).** Warning time is a balance property of a weapon in play; inside the exchange production is halted, stats frozen, map revealed and annihilation follows — there is no play left to warn. The substitution is gated by the same predicate the cascade hook already uses (`SalvoInProgress && NuclearGameEnders.Is(info)`), so `MissileStrikeArrivalTest` (ruleset-only) cannot see it. Timeline on river-zeta (N=3): window closes 15 s, first missile visible 8.4 s later, first impact 36 s, last 40.5 s, verdict 47.7 s.

**Mandatory companions.** `FinalExchangeCascadeTest.TheShippedFloorCoversTheSlowestGameEnderOnTheLargestMap` was over-constrained (`>= MissileDelay + slowestFlight`) and becomes `>= FinalExchangeMissileDelay + slowestArc`; the sweep's natural-impact construction uses the short delay; `world.yaml` comment states the real rule; the two scenarios' `MissileDelay` overrides are inert inside the exchange and go.
