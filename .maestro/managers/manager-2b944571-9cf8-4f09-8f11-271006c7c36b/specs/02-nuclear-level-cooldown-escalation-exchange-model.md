# Nuclear Level & Cooldown — Escalation exchange model v2

_spec · status: active · authored 2026-09-14T23:43:50.821Z_

# Nuclear Level & Cooldown — Escalation exchange model v2

Supersedes decision 01 (parity + retaliation window) and the band-level regeneration of bd1d4f89. Motivation (user, 2026-09-15, after a real match): too many nukes in flight, no chance to build forces; each team should fire one nuke at a time, from the bands its level allows, with a general cooldown before ANY nuke can fire again. The core idea is unchanged: **using nukes escalates the enemy's level.**

## Terms
- **Band** (rung): 1 = 1 kt, 2 = 20 kt, 3 = 50 kt, 4 = 100 kt, 5 = END (national game-ender). `NuclearReleaseLadder.RungForYield` is unchanged.
- **Side** = lobby team (NuclearExchange's side key). Teammates share everything below.
- **Level** L(side) ∈ {0 (Hold), 1..5}. Replaces PermanentLevel + WindowLevel; there is no window.
- **Side cooldown** — one countdown per side. While > 0, NO nuclear power of any player on that side is fireable.

## Rules
1. **Release.** When the release gate opens (unchanged: first-warheads clock after DEFCON 1), every side gets L = 1 and cooldown 0.
2. **Availability.** A side may fire any band b ≤ L(side), and only while its cooldown is 0. Every nuclear power on the side reads Ready iff (band ≤ L and cooldown == 0); the others read Disabled/hidden as today for unreleased bands. One activation = one shot (a multi-warhead salvo is one activation).
3. **Firing.** When side A fires band b:
   - A's cooldown := CooldownTicks[b] × posture (Limited 150 %, Flexible 100 %, Massive 60 %). Defaults (USER-RULED 2026-09-15, "nukes become rare punctuation; conventional play dominates"): 1 kt 5000, 20 kt 7000, 50 kt 9000, 100 kt 12000 ticks = 5/7/9/12 min at 16.67 tps (60 ms timestep — NOT 25 tps). Reuse the existing per-band Info fields, renamed to cooldowns. Still lobby-scaled by posture. END has no cooldown (the game ends).
   - Every ENEMY side B: L(B) := max(L(B), min(b + 1, 5)). Permanent — levels never fall. A's own level is unchanged by its own fire.
   - Allies of A are unaffected (they share A's side).
4. **END.** At L = 5 the side's NATIONAL game-ender (B83 for america, Sarmat for russia; the unattributed 6 Mt stays sandbox-only — user ruling 2026-09-14) becomes fireable under the same cooldown rule. Firing it calls `DoomsdayStrike.BeginFinalExchange(trigger)` exactly as today (path b). Dead Hand / time-limit path (a) unchanged.
5. **Escalation is asymmetric by design**: the aggressor hands the defender a bigger weapon and never itself. A side that never fires keeps its enemy at L = 1 forever. Being hit by a 100 kt gives END; a side ahead on score may then end the match — this is the deterrent, accepted.
6. **Nothing nuclear is purchasable in Escalation** (decision 02, unchanged). Skirmish is untouched by this spec.

## Removed
- The retaliation window: `WindowLevel`, `WindowTicksRemaining`, `TickWindows`, the `RetaliationWindow` lobby option and dropdown, "one band up for a window", "firing at/above your window closes it", `GrantRetryTicks` window-grant machinery insofar as it is window-specific. `NuclearArmedBannerWidget`'s copy changes from "20 kt available for 1:00 — reply or hold" to a level-raised notice ("ESCALATED — 20 kt now available"), shown on every level rise of the local side.
- Per-band regeneration timers as independent clocks (`PutBandOnRegen` per band). `EscalationRegenTicks` becomes the side cooldown lookup.

## Kept
- Ledger (YOU / ENEMY rows): boxes ≤ L lit; ONE cooldown clock per row (drawn in the value slot: `READY` or `mm:ss`); END box lit at L = 5. RELEASE banner unchanged. Transients on level rises.
- `NuclearBotModule`: fires only when losing (thresholds/streak unchanged); picks the highest band ≤ L it can aim well (existing scoring); obeys Ready. Window-reply logic deleted. `MayStartApocalypse` governs END as before.
- `SupportPowerManager` purchase bypass shape (`RequiresPurchase && regen < 0`), `-Vaporizable:` on SUPPLYROUTE, faction lock, `NuclearGameEnders.ArmableBy`.

## Abuse review
- Spam: impossible — one shot per side per cooldown (≥ 3 min at Flexible).
- Self-escalation: impossible — only enemies climb.
- Alt-account/teammate double fire: cooldown is per SIDE, so a team of two still fires once per cooldown.
- Denial: a side may refuse to fire; then the enemy is stuck at 1 kt. Intended.
- Cheap 1 kt to bait a 20 kt reply: the reply costs the replier a 4-min cooldown and hands the baiter 50 kt. Self-limiting.

## Lobby (companion change)
- Escalation section: Game mode, Opening phase, No-rush, First warheads, Nuclear posture. Retaliation window REMOVED.
- Skirmish-only controls (unlock interval, four tier checkboxes, tactical-nuke, high-yield-nuke, nuclear-arsenal) hidden while mode = Escalation; Escalation-only controls (no-rush, first warheads, posture) hidden while mode = Skirmish. `powers-sandbox` stays visible (debug) but its description says it is sandbox-only.

## Verification (end to end)
- NUnit: `NuclearExchangeState` pure rules (release → L1; fire b → enemy max(L, b+1) capped 5; own L unchanged; cooldown set; teammates share; cooldown blocks; END at 5).
- Scenarios (GREEN + RED each): `test-nuclear-exchange` rewritten for v2 (ladder climb by alternating fire; assert levels, cooldown blocks the second shot, enemy escalated, END reached after a 100 kt, game-ender fires → final exchange); `test-nuclear-band-regen` → `test-nuclear-side-cooldown` (teammate blocked by side cooldown); `test-nuclear-ender-window` → END via level; `test-bot-nuclear` (bot fires when losing at highest band ≤ L, respects cooldown); `demo-defcon-readout` frames updated; `test-escalation-full-match` readings updated.
- A full-match smoke run and a `demo-defcon-readout` capture presented to the user before handover.
