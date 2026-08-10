# Stage F balance-of-power fix: neighborhood control read over narrative-only qualification

_Recorded 2026-07-24T09:31:44.715Z by ee31feaf_

Reviewer 694aa737 (MERGE-WITH-FIXES) found the boost half of `BalanceOfPowerFactor` unreachable for enemy targets: every seen enemy Attack/Pressure target is a structure with CaptureManagerInfo/SupplyProviderInfo → IsSiteAnchor → its own cell anchor-floored to ≈−800 (ControlField.cs:137-143, 469-501), so `ScoreAt(targetCell)` < −GrayBand always → damp ×60 even for the textbook "encircled isolated enemy derrick" case. Emergent behavior = "prefer nearby neutral income + avoid danger", not the advertised "press enemy weakness".

Options considered:
(a) Narrative-only qualification — cheapest, ships as-is, but PIPELINE item 9's perceived promise ("offense presses where the enemy is weak") would be knowingly unfulfilled, and the lever's asymmetry would harden into the doctrine docs.
(b) Neighborhood read — `BalanceOfPowerFactor` samples the SURROUNDING believed control (fixed ring around the target cell, excluding the anchor-floored target cell itself) so an enemy structure sitting in ours-painted territory reads positive → boost, one deep in enemy paint reads negative → damp. A behavior change, but the declared re-baseline has NOT run yet, so it is exactly the cheap moment to change behavior.

Picked (b) + narrative alignment. Rationale: (b) is what the design (§3.3, item 9's perceived line) actually mandated; the reviewer itself flagged (b) as "arguably the better doctrine"; the anchor floor is a deliberate feature of the target's own cell, not of its surroundings, so reading the neighborhood is semantically correct, not a workaround. Fix routed back to implementer a26cf63e per pipeline precedent (one new fix commit, no amend), manager diff-inspection on delivery.
