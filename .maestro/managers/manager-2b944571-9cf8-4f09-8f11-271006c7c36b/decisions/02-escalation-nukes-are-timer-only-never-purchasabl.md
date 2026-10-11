# Escalation nukes are timer-only, never purchasable; Skirmish sells them with a checkbox per tier

_Recorded 2026-09-13T18:01:27.290Z by 7a1529e6_

Ruled by the user on question `UiMJYz9xFUHYcDc-2YiVW` (free-text, none of the three drawn options): *"In escalation it is only on the timer etc, in Skirmish mode they can be purchased but we can disable purchasing for various levels too, like disable all high yield, or low yield etc, there can be 4 checkboxes, one for each tier. But in escalation mode we only control the timing, nothing is purchasable."*

## What it means

- **Escalation:** money never touches a nuke. Every band a side holds (permanent level, and the window grant) is a free power. Permanent bands regenerate on a **timer** per band (untuned placeholders to start; Nuclear Posture scales them — it becomes a live lever). The window grant is ready the instant the window opens, one shot, and vanishes with the window. The shipped `RequiresPurchase: True` on all thirteen nuclear powers must be bypassed in Escalation (mode-conditional in engine code, not thirteen yaml forks).
- **Skirmish:** purchase economy unchanged, gated by the unlock clock — but the single "Highest yield" cap dropdown becomes **four per-tier checkboxes** (1 kt / 20 kt / 50 kt / 100 kt purchasable). Must be reconciled with the pre-existing `tactical-nuke` / `high-yield-nuke` booleans in the Arsenal section so there is one set of controls.

## Why this was a fork at all

The engine has no nuclear cooldown: the worker found every nuclear power is a bought, banked shot, so "free on a countdown" had no home. The user's answer resolves it by making the two modes different economies on purpose — the seam decision 16 already drew (Skirmish buys, Escalation is handed) — and by declining the hybrid.

## Consequence

The interim implementation on `wt/nuclear-exchange` (permanent parity = permission to buy; window = one free banked shot) is a stepping stone, not the design; a follow-up replaces purchase with regeneration in Escalation.
