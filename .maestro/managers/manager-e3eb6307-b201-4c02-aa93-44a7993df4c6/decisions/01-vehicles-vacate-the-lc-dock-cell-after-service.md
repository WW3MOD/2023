# Vehicles vacate the LC dock cell after service

_Recorded 2026-09-05T06:13:35.462Z by 0d084dc5_

Context: the 2x2 Logistics Centre has ONE stoppable dock cell (under the crane) and no queue/reservation. `test-depot-vacate-phantom`'s header documents that the old 3x3 "shove off the transit-only centre cell" was load-bearing — it kept the dock free for the next vehicle. With a stayable dock, a player-ordered vehicle left parked after service makes the next one wait forever (MoveOnto NoPath + cell-equality arrival).

Options: (A) explicit vacate to the nearest free cell adjacent to the footprint once Resupply finishes all service types; (B) port DockHost/DockClientManager queueing (large refactor, out of scope); (C) loosen arrival to "near the dock" (defeats "stop under the crane").

Picked A. Cheapest, deterministic, preserves the user's ask (vehicle IS under the crane while served) and the old observable behaviour (serviced vehicles clear the pad). The phantom scenario is rewritten to this contract: Tank serviced → vacates without an order → Tank2 docks on the same cell.

Also decided without asking: the RepairsUnits zero-step engine bug (HpPerStep 0, PercentageStep unread — repair never healed anything) is fixed on this branch. It changes @stable; the next benchmark baseline must be re-taken knowingly.
