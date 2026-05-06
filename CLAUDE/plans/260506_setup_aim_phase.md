# Setup / Aim phase mechanics — design discussion

> Date: 2026-05-06. Triggered by drone-operator stuck bug + artillery firing-while-moving + cursor-blocked-while-paused. Two stabilization fixes shipped in commit `c0582a43`; this doc is the design brainstorm for the bigger mechanic.

## What we shipped today (already in main)

1. **`Mobile.cs` cursor truthfulness** — `BlockedCursor` no longer shown purely because Mobile is paused. Move orders queue regardless of pause and run once unpaused, so the old cursor was lying. Real unreachability is still flagged.
2. **`AttackBaseInfo.HoldFireWhileMoving`** (opt-in, default false) — gates `AttackBase.CanAttack` on `Mobile.IsMovingBetweenCells`. Closes the firing-mid-cell hole. Opted-in: Paladin, M270, HIMARS, giatsint, grad, tos, Iskander.

These don't add a "setup" or "aim wait" phase; they only stop the unit from firing while still rolling. The richer mechanic the user described follows.

---

## Issue 1 — Drone operator gets "stuck" (NOT shipped — needs decision)

### Root cause
`SmartMoveActivity` (in `engine/.../Activities/Move/SmartMoveActivity.cs`) wraps every player Move. Every `ScanInterval` (10 ticks) it scans for enemies in range. If a target is found and "not saturated" (`AverageDamagePercent < OverkillThreshold (=100)`), it cancels the inner Move and queues an Attack child.

For DR (drone operator) specifically:
- `AutoTarget: ScanRadius: 25`, default stance `FireAtWill`.
- Secondary armament `DroneJammer` has `ValidTargets: Drone`, `Range: 20c0`, `BurstWait: 1` — fires every tick.
- When an enemy drone is within 20c0 of a moving DR, SmartMove kicks in and cancels the move so DR can lase the drone.
- Because `BurstWait: 1`, the Attack activity stays in the "Attacking" state continuously while the drone is in range — the move never resumes until the drone dies/leaves.

User-visible symptom: "rejecting move orders, stuck in place, attack animation running" — exactly matches.

### Options (pick one or combine)

| # | Option | Recommendation | Why |
|:-:|---|:-:|---|
| **A** | Set `DR.AutoTarget.InitialStance: HoldFire`. User must manually engage drones. | 🤔 partial | Cheap, kills the bug, but loses auto-defense — drones become a hard-counter you must babysit. |
| **B** | Per-armament flag `Armament.NoSelfDefenseInterrupt` (default false). `SmartMoveActivity` skips targets that can only be hit by armaments with this flag set. DR sets it on DroneJammer. | ✅ **recommended** | Surgical, generalizes (e.g. Tunguska AA could opt in for ground targets), keeps auto-defense when stationary, doesn't break Move when only the support weapon is applicable. |
| **C** | `Armament.RequiresForceFire` (default false). When set, AutoTarget never picks the armament; only the player can target with it. | 🤔 alternative | Same effect as A but per-armament. DR loses auto-defense entirely. |
| **D** | Engagement-stance gate: `Defensive` engagement should not interrupt a player Move; only `Hunt` should. Today even `Defensive` interrupts via SmartMove. | ⚠️ broader | Affects every unit's behavior. Possibly the *right* fix, but risk of regressions on tanks/IFVs that should pause-and-shoot. Wants its own playtest pass. |
| **E** | Cap how often SmartMove can interrupt: minimum N ticks of Move between interrupts. | 😬 hacky | Treats the symptom not the cause; DR with a continuous laser would still lock up. |

**My recommendation:** B + take a separate look at D for v1.1. B is small, contained, and cleanly maps to "this weapon is a self-defense weapon, don't let it cancel my move." D is a stance-system redesign that deserves its own brainstorm.

---

## Issue 2 — Artillery / MLRS setup-and-aim phase (not shipped — design discussion)

What you described:
1. Stop completely
2. (Optional) wait — "setup" / unpack
3. Aim — turn turret/body
4. (Optional) wait — "settle aim"
5. Fire
6. Subsequent shots at same target: little or no aim wait
7. New target: re-aim wait

Mapping to existing engine:

| Phase | Existing field | Where | Notes |
|---|---|---|---|
| 1. Stop completely | `Mobile.PauseOnCondition: firing` + new `HoldFireWhileMoving` | Mobile + AttackBase | ✅ shipped |
| 2. Setup wait | **missing** | — | new field needed |
| 3. Aim (turret/body turn) | `Turreted.TurnSpeed`, `Mobile.TurnSpeed`, AlignBodyToTarget | Turreted, Mobile | ✅ exists |
| 4. Settle wait | `Armament.AimingDelay` | Armament | ✅ exists, default 15 |
| 5. Fire | `Armament.FireDelay` (per-shot launch delay) + CheckFire | Armament | ✅ exists |
| 6. Same-target burst | implicit — `AimingDelay` only resets on target change | Armament | ✅ already behaves this way |
| 7. New-target re-aim | `AimingDelay` reset in `Armament.CheckFire` line 339 | Armament | ✅ already behaves this way |

So we already have everything except phase 2 (the "unpack/setup" delay before aiming starts). And phase 4 already does the "settle" job.

### Proposal — add `SetupTicks` to AttackBase

**Recommended:** `AttackBaseInfo.SetupTicks` (default 0) — ticks the unit must spend stationary AND in range AND with target acquired before it can begin aiming. While in setup, the unit is frozen (no movement, no turret turn, no fire). Setup resets if the unit moves.

```yaml
AttackTurreted:
    HoldFireWhileMoving: True   # already shipped
    SetupTicks: 50              # 2s "deploy" before turret starts turning
    SetupCondition: deploying   # optional, for animation hooks
Armament@1:
    AimingDelay: 35             # 1.4s aim settle after turret faces target (existing field)
```

Implementation sketch (in `AttackBase`):
- Track `setupStartedTick` and `lastSetupTarget`.
- In `CanAttack`: if `Info.SetupTicks > 0`:
  - If unit is moving → reset setup, return false.
  - If target changed → reset setup, return false.
  - If `WorldTick - setupStartedTick < SetupTicks` → return false.
- Optional: grant `Info.SetupCondition` while in setup so YAML can hook a sprite/animation.

The aim-settle phase (step 4) is already covered by `AimingDelay`, which is per-armament and resets on target change. Recommend bumping defaults on artillery/MLRS armaments (currently default 15, only Bradley TOW @ 50 is set).

### Alternatives I'm less keen on

- **Combined into a single `AimingDelay`**: increase `AimingDelay` to cover both setup + aim settle. ⚠️ Loses the distinction — setup should NOT reset between shots at the same target, but aim settle should. They're different things.
- **Per-armament `SetupTicks`**: ⚠️ overkill — setup is a per-unit property (the chassis deploys), not per-weapon.
- **`UnpackOnDeploy` trait**: 😬 too heavyweight — would require a deploy order / mechanic. The user already said reinforcements come pre-deployed; we just want time-gating.

---

## Issue 3 — Proactive: other things found while looking

### 3a. ⚠️ `WAngle` convention contradiction in DISCOVERIES.md vs memory
- `CLAUDE/DISCOVERIES.md:28` says `0=North, 256=East, 512=South, 768=West` (clockwise).
- `CLAUDE.md` and `memory/feedback_facings.md` say WAngle is counterclockwise — `256=West, 768=East`.
- Engine code (`WAngle.Yaw`, drone's `targetingVector`) uses counterclockwise. The MCP tool entry in DISCOVERIES is wrong — leftover from an early misunderstanding.
- 💡 Suggest fixing DISCOVERIES.md or marking that entry deprecated. Not done in this turn — wanted to flag first.

### 3b. ⚠️ `AttackTurreted` commented-out `# StopAndWait: 100` on Paladin
- `vehicles-america.yaml:622` has a leftover comment hinting at a previous attempt at this exact feature.
- Same comment on giatsint (`vehicles-russia.yaml:459`).
- Suggests this design has been thought about before. Worth searching git log for the abandoned implementation if the user wants context.

### 3c. ⚠️ `GrantConditionOnPreparingAttackInfo` has dead fields
- Fields `PreparingRevokeDelay`, `AttackingRevokeDelay`, `RevokeOnNewTarget`, `RevokeAll` are declared but the trait body is mostly commented-out scaffolding from a half-finished rework. Active path uses only `RevokeDelay`.
- BMP-2 sets `PreparingRevokeDelay: 50`, `AttackingRevokeDelay: 0` thinking they matter — they don't.
- 💡 Either implement the split-condition feature or strip the dead fields + YAML. Same shape as the `ReloadAmmoPool.FullReloadTicks` discovery from 2026-03-23.

### 3d. ⚠️ `Mobile.PauseOnCondition: firing` does NOT halt mid-cell
- `MovePart.Tick` does **not** check `IsTraitPaused` — only the cell-boundary in `OnComplete` does. So even with the condition set, a unit fires its first shot mid-cell. This was the actual root cause of "fire before fully stopping". My `HoldFireWhileMoving` flag closes the fire side; the unit still finishes the in-flight cell visually (reasonable — full mid-cell halt would look jerky for vehicles).
- Worth knowing for future debugging — `PauseOnCondition` is a "no new transitions" gate, not a "stop now" gate.

### 3e. 💡 `IndirectFire` is a marker trait used by FiringLOS to skip LOS checks, but `AllowIndirectFire` on `Armament` is documented as `// TODO FF, Unimplemented` (`Armament.cs:79`)
- The whole "this armament can/can't shoot indirect" toggle exists in the schema but is wired to nothing. Currently if `IndirectFire` is on the actor, ALL its armaments bypass LOS. DR has it (so DroneJammer also bypasses LOS — probably fine since it's a drone-jammer laser? or maybe not). Worth a sanity check.

### 3f. ⚠️ `AutoTarget` on DR is set explicitly; faction files for DR don't override
- `infantry-america.yaml:115` and `infantry-russia.yaml:115` are just `# Drone Operator` comment markers — no actual unit override, which means DR uses the same template both sides. Probably intentional but worth confirming.

---

## What to do next

**Decisions I need from you before implementing more:**

1. Drone-operator stuck — pick option **A**, **B**, **C**, or **D** above (I recommend B).
2. Setup phase — implement `SetupTicks` as proposed? Defaults for artillery/MLRS?
3. Should I also bump artillery armament `AimingDelay` from 15 → ~35 as part of the same pass?
4. Should I touch any of the proactive findings (3a, 3c) now, or file them?

Once you decide, the engine bits are small (~30 lines for SetupTicks, ~15 lines for the SmartMove gate). YAML pass is mechanical.
