# FIRST WARHEADS is an offset from the line lifting, not a match-clock time

_Recorded 2026-09-10T21:50:07.936Z by ffb08fdc_

Ruled by the user, 2026-09-10, closing the one number the previous generation's handoff flagged as unconfirmed.

## The ruling

The lobby timeline's second marker (FIRST WARHEADS) is **relative to the first** (THE LINE LIFTS), not absolute on the match clock. Dragging the no-rush marker carries the warhead marker with it. What the host sets with the second marker is the **gap**.

## Why this was open at all

The user had ruled "first warheads 10 minutes in" without saying ten minutes from what, and the two readings are not cosmetically different — they change what happens to the second marker when the first one moves, and whether a host can reach a state where warheads arrive before the no-rush period ends.

## What the answer costs

Nothing. `NuclearReleaseDelayTicks` already defaults to 10000 ticks, which at this engine's rate is exactly 10 minutes measured from DEFCON 1 — so the relative reading is what the shipped code does today. The alternative would have needed a rebind and a clamp.

## Consequence worth recording, because it is an absence

Under the relative reading the two markers **cannot cross into an incoherent order**, so the widget needs no clamp guarding that case. The implementer was told explicitly not to add one: a clamp implies the state it guards against is reachable, and a future reader finding it would spend real time working out when. This is the kind of thing that gets added defensively during a later refactor and then can never be removed, because nobody can prove it is dead.

## Corroboration, arrived at independently

The mockup drew the second caption as `+10:00` with a plus sign before the question was asked — a separate route to the same reading, and the plus is now load-bearing copy rather than decoration: it is what tells the host the number is a gap. Keep it.
