# AI v2 — Handoff (2026-05-13)

> Where we are mid-session. Two visible behaviours are still broken
> after multiple fix attempts; we now have concrete diagnostic evidence
> that says where to look next. This doc captures the full state so you
> (or a future agent) can pick up cold.

## Session goal

Build a doctrine-aware AI for WW3MOD that plays *believably* (real
tactics, not RTS-bot zergs). Doctrine source: [`doctrine.md`](doctrine.md).

## What's shipped this session (in order)

Each commit hash links to the actual change. All on `main` branch.

| Stage | Commit | What |
|---|---|---|
| Doctrine | `fafedcb9` | Defence-in-depth + 3:1 + honest fog roadmap |
| Doctrine clarification | `d4a32165` | Screen = sparse tripwire; main line carries the full combined-arms mix |
| Stage A.1+A.2 | `f8afecaf` | `InfluenceMap` world trait + frontline derivation math (`InfluenceMapMath`); 10 unit tests pass |
| Stage A.3 | `4c3e043d` | `FrontlineOverlay` trait — chat command `/frontline` toggles |
| Stage A.6 | `3e5f7a0a` | Demo + `DOCS/gameplay/ai-overlay.md` |
| Overlay polish | `6b03874b` | `FilledQuadAnnotationRenderable` (continuous orange band, not dots) + InfluenceMap.ContributionRadius 3→5 |
| Stage B.1 (initial) | (rolled into later commit) | Layered defence — every idle unit → nearest contested cell |
| B.1 revision | `0a462dbf` | Reserve-driven slot scoring (low friendly + low enemy density wins); on-line units stay put |
| Capture fixes | `9dbf9e75` | Excluded list + ammo-out skip + cover seeking (Tree/Rough/Field within 6 cells) |
| Supply truck restock | `ff535b52` | SupplyFollower skips trucks below RestockThreshold so built-in restock isn't cancelled |
| Stage B.4 | `7d5d4ea0` | `MountedTransportBotModule` — IFV/APC ferries infantry from SR reserve zone to thinnest frontline cell |
| Audit fixes | `d7d34d6d` | Gated legacy ground SquadManager + @captureenemystructures off v2; transport reservation handshake |
| Diagnostics | `b7ad8acb` | Activation chat lines + per-scan `Log.Write` + `Passenger.TargetLineColor = FFC850B4` |

## What's confirmed working (empirical evidence)

- **`InfluenceMap` + `FrontlineOverlay`** — visible orange band tracks
  forces correctly. User-confirmed across multiple screenshots. Doctrine
  perception is real.
- **`CaptureCoordinatorBotModule@v2.tecn`** — fires (TECN does walk to
  capture targets); income weighting is observable.
- **Capture rule fixes** (TECN-only neutrals, soldiers enemy-only) — 5
  unit tests pass; documented in `DOCS/gameplay/capturing.md`.
- **Supply truck restock fix** — visible in code; not playtested in
  isolation.
- **`MountedTransportBotModule@v2` IS LOADING.** The diagnostic
  `Log.Write` from commit `b7ad8acb` produces ~hundreds of lines in
  `~/Library/Application Support/OpenRA/Logs/debug.log`. Example:

```
[v2-transport] scan player=V2 AI (experimental) 1 carriers-total=1 carriers-candidate=0 tasks-active=0
[v2-transport] scan player=V2 AI (experimental) 2 carriers-total=3 carriers-candidate=0 tasks-active=2
```

This proves the YAML wiring + condition (`enable-ai-v2`) is correct
and the trait IS being instantiated for v2 players.

## What's NOT working — user playtest

User has run multiple demos / skirmishes (both 1×v2 vs normal and
2×v2 head-to-head). After the audit fixes (`d7d34d6d`) and
diagnostics (`b7ad8acb`):

### Bug 1 — TECN still gets order-overwritten

User: *"orders gets overwritten, dont see any attempts to load into
transports. while we are at it..."*

Despite gating `CaptureManagerBotModule@captureenemystructures` to
`enable-ai-legacy-only`, the user still observes TECN getting an
order *after* its capture order that cancels the capture.

Status: **WE DO NOT YET KNOW** which module is issuing the
overriding order. Hypotheses below.

### Bug 2 — No infantry loading into vehicles (carriers-candidate=0)

The diagnostic log is the smoking gun. Across 530 log lines, every
scan reports `carriers-candidate=0` while `carriers-total` is 1–3.
**Carriers are owned, but none qualify as candidates for new
transport tasks.**

Filter for carrier eligibility (`MountedTransportBotModule.cs`
around line 230):

```csharp
.Where(a => a.Owner == player && !a.IsDead && a.IsInWorld && a.IsIdle
    && Info.CarrierTypes.Contains(a.Info.Name.ToLowerInvariant())
    && !carrierTasks.ContainsKey(a)
    && a.Info.HasTraitInfo<CargoInfo>())
```

`tasks-active` is sometimes ≥1, sometimes 0 — but candidate is *always*
0 even when `total > tasks-active`, so there are carriers that aren't
in tasks but still fail the filter. Most likely failure modes:

1. **`a.IsIdle == false`** — the carrier has an active activity from
   somewhere (AutoTarget engaging an enemy in range? leftover Move
   from earlier? Some other module we missed gating?).
2. **Name-case mismatch** — `actor.Info.Name.ToLowerInvariant()` vs the
   YAML `CarrierTypes` set, where the set stores values exactly as
   written. The YAML uses `bradley, bmp2, m113` (lowercase). The actor
   `.Name` for the YAML-defined `bradley:` actor *should* be exactly
   `"bradley"` — but I haven't confirmed.
3. **`CargoInfo` trait missing** — verified present in
   `mods/ww3mod/rules/ingame/vehicles-america.yaml:265` (M113) and
   `:414` (Bradley) and `vehicles-russia.yaml:103` (BMP-2). So this
   isn't it. (But fresh-built carriers might have a brief window where
   trait isn't yet attached? Unlikely.)

The simple way to narrow this down: log WHY each carrier fails the
filter, not just the count. Concrete next step in the debug plan below.

## Why my prior fixes weren't sufficient

Going through the audit findings + what I did:

| Finding | Fix attempted | Why insufficient |
|---|---|---|
| Legacy SquadManager grabs Bradleys | Gated `@america.normal`/`@russia.normal` to `enable-ai-legacy-only && enable-ai-player && player.<faction>` | Did fix the SquadManager source. But there's ANOTHER source of orders making carriers non-idle. |
| `@captureenemystructures` competing for TECN | Gated to `enable-ai-legacy-only`; added `logisticscenter` to v2 CaptureCoordinator | User still reports TECN overwrites. Either gating didn't take effect, OR there's a third source. |
| MountedTransport required IsIdle on passengers | Removed IsIdle requirement; reservation handshake | Doesn't help if carriers themselves are never IsIdle. Passenger filter is moot when no carrier is eligible. |

## The most likely remaining culprit

**Carriers are never idle.** Once produced and walked from map-edge to
SR rally point, the Bradley enters AutoTarget Defensive stance + scans
for enemies. If an enemy is anywhere in `AttackScanRadius` (32 cells
for `^Combatant`), the Bradley starts an Attack activity → `IsIdle = false`.

WW3MOD's `^Combatant` (from `mods/ww3mod/rules/world.yaml` or similar
common rules) sets `InitialEngagementStanceAI: Defensive` and
`ScanRadius: 30`. A v2 Bradley at the rally point with enemy scouts
visible 25 cells away will be in attack mode, never idle, never a
transport candidate.

**Confirm by:** logging each carrier's `IsIdle` + current activity name
inside the candidate filter. If `IsIdle=false` for all carriers, we've
found it.

**Fix paths** (any/all):

1. Relax the MountedTransport filter — accept carriers even when
   they have an Attack activity, as long as they're empty and not in
   a task. The new `EnterTransport`/`Move` order with `queued=false`
   will cancel the Attack.
2. Force carriers into `HoldFire` or a lower engagement stance until
   they reach the front (would require either YAML config on the
   carriers or runtime stance manipulation per carrier).
3. **Better:** the user's actual ask is "infantry rides forward". Make
   carriers AUTOMATICALLY pick up infantry whenever they're at the SR
   rally area, regardless of state. Then drive forward.

## Hypothesis for TECN order-overwriting

If carriers aren't idle (above hypothesis), what about TECN?

TECN doesn't have AttackBase by default (no weapon) — but it DOES
have a small SMG weapon? Let me check. Actually `^TECN` inherits
`^ArmedCivilian` which means it has SOME armament. So TECN
auto-engages enemies in range → not idle → CaptureCoordinator might
see it as not idle and... wait, CaptureCoordinator only ACTS when
TECN is idle. So not-idle TECN wouldn't get a new order.

UNLESS the order chain is: TECN gets capture order → starts walking
→ AutoTarget detects enemy in range → TECN tries to fire → the
CaptureActor activity gets interrupted by AutoFire? In OpenRA, an
AttackBase target acquisition could enqueue an Attack activity that
PREEMPTS the move. The CaptureActor (which inherits Enter, which
inherits Activity) might get cancelled or paused.

This is speculation — needs verification.

**Diagnostic to add:** log the current activity name on TECN every
N ticks. If we see TECN flipping between `CaptureActor` and `Attack`
activities, this is the answer.

## Adjacent observation: dual v2 in user testing

The latest debug.log shows two `V2 AI (experimental)` players. So the
user has been testing with v2 vs v2 (not the `demo-layered-defence`
v2-vs-normal). That's fine — actually MORE useful since both sides
exercise the v2 modules. Note this for future test interpretation.

## Concrete next debug steps

1. **Add per-carrier diagnostic** in `MountedTransportBotModule.cs`'s
   `TryAssignNewTasks`. For each carrier the bot owns, log:
   - Name, location, `IsIdle`, `currentActivity?.GetType().Name`,
     `Cargo.PassengerCount`, `carrierTasks.ContainsKey(carrier)`.
   - Example output: `[v2-transport] carrier bradley@27,15 idle=false activity=AttackFrontal pax=0 task=no`.
   - Will conclusively answer "why carriers-candidate=0".

2. **Add per-TECN diagnostic** in `CaptureCoordinatorBotModule.cs` at
   the start of `BotTick`. Log each TECN's name, location, idle state,
   activity, last order issued. Track activity transitions tick-over-
   tick — if we see Capture → Attack → idle → Capture, the AutoTarget
   theory is correct.

3. **If carriers are stuck in Attack activities:** test fix path 1
   (relax IsIdle requirement). One-line code change:
   ```csharp
   .Where(a => a.Owner == player && !a.IsDead && a.IsInWorld
       // && a.IsIdle    ← remove this gate
       && Info.CarrierTypes.Contains(a.Info.Name.ToLowerInvariant())
       && !carrierTasks.ContainsKey(a)
       && a.Info.HasTraitInfo<CargoInfo>()
       && a.Trait<Cargo>().IsEmpty())  // ← add this so we only grab empty carriers
   ```
   `EnterTransport`/`Move` with `queued=false` will cancel the
   AutoTarget Attack activity.

4. **If TECN is being yanked out of Capture by AutoTarget:** set TECN's
   `AutoTarget.InitialStance` to `HoldFire` so they only fire on
   explicit order. The capture is the order; AutoFire won't interrupt
   if HoldFire.

5. **Audit YAML gating one more time.** I gated
   `@captureenemystructures` to `enable-ai-legacy-only` in commit
   `d7d34d6d`. The DLL on disk (`engine/bin/OpenRA.Mods.Common.dll`,
   timestamped 2026-05-13 16:28) is newer than the commit time (15:29)
   AND newer than the diagnostics commit (16:15). So the change IS in
   the loaded code. *But the user observes TECN thrashing.* So either
   the diagnostic theory above (AutoTarget interrupts capture) is the
   real cause, OR there's another order source we haven't gated.

## Wishlist / discovered work

These are user-flagged items still outstanding (priority ordering in
`playtest_260513.md`):

- **P5 — Vehicles as mobile fire support.** Beyond transport: tanks/
  IFVs should reposition to the threatened sector, not just sit at
  the standoff. Phase C territory.
- **P6 — Active rearm/retreat.** Empty-ammo units actively retreat to
  supply/LC instead of just skipping in LayeredDefence.
- **P7 — Phase D offensive 3:1.** Bot attacks when conditions align.
  Major scope.
- **Multi-frontline support.** InfluenceMap currently treats
  contested zone as one. Multi-axis FFA/team play not handled.
- **Carrier fire-support fallback.** Idle empty carriers with no
  passengers should fall back to LayeredDefence positioning.
- **B.3** — main-line overlapping fields of fire.

## Files touched this session

Engine:
- `engine/OpenRA.Mods.Common/Traits/World/InfluenceMap.cs`
- `engine/OpenRA.Mods.Common/Traits/World/FrontlineOverlay.cs`
- `engine/OpenRA.Mods.Common/Graphics/FilledQuadAnnotationRenderable.cs`
- `engine/OpenRA.Mods.Common/Traits/BotModules/LayeredDefenceBotModule.cs`
- `engine/OpenRA.Mods.Common/Traits/BotModules/MountedTransportBotModule.cs`
- `engine/OpenRA.Mods.Common/Traits/BotModules/CaptureCoordinatorBotModule.cs`
- `engine/OpenRA.Mods.Common/Traits/BotModules/SupplyFollowerBotModule.cs`
- `engine/OpenRA.Test/OpenRA.Mods.Common/InfluenceMapMathTest.cs`

YAML:
- `mods/ww3mod/rules/ai/ai.yaml`
- `mods/ww3mod/rules/ai/ai-america.yaml` (squad manager gating)
- `mods/ww3mod/rules/ai/ai-russia.yaml` (squad manager gating)
- `mods/ww3mod/rules/ingame/infantry.yaml` (Passenger line colour, capture rules)
- `mods/ww3mod/rules/world.yaml` (InfluenceMap + FrontlineOverlay registration)

Docs:
- `WORKSPACE/ai/doctrine.md`
- `WORKSPACE/ai/stage_a_frontline_perception.md`
- `WORKSPACE/ai/stage_b_layered_defence.md`
- `WORKSPACE/ai/stage_b4_mounted_transport.md`
- `WORKSPACE/ai/playtest_260513.md`
- `WORKSPACE/ai/handoff_260513.md` (this doc)
- `DOCS/gameplay/ai-overlay.md`
- `DOCS/gameplay/capturing.md` (resolved decisions)
- `DOCS/recipes/DOCUMENT.md`

Scenarios (demos + tournament):
- `tools/autotest/scenarios/demo-frontline-overlay/`
- `tools/autotest/scenarios/demo-layered-defence/`
- `tools/autotest/scenarios/demo-v2-capture-coordinator/`
- `tools/autotest/scenarios/tournament-capture-arena-2p/`
- `tools/autotest/scenarios/tournament-capture-arena-mirror-2p/`
- `tools/autotest/scenarios/tournament-v2-vs-normal-2p/`
- `tools/autotest/scenarios/tournament-v2-vs-normal-mirror-2p/`
- `tools/autotest/scenarios/test-capture-rules/`

## How to verify when picking back up

```bash
# 1. Confirm binaries are current.
ls -la engine/bin/OpenRA.Mods.Common.dll
git log --oneline -5

# 2. Launch a v2 vs v2 (or v2 vs normal) skirmish or demo.
./tools/autotest/run-demo.sh demo-layered-defence

# 3. In the running game, type /frontline to toggle the overlay.
#    Confirm the orange band tracks contact.

# 4. After ~30 sim-seconds, check the debug log:
tail -50 "$HOME/Library/Application Support/OpenRA/Logs/debug.log" | grep v2-

# 5. If you see "carriers-candidate=0" repeatedly with carriers-total>0,
#    the next step is the per-carrier diagnostic in §"Concrete next debug steps".
```

## Status of doctrine roadmap

- Phase A — Frontline perception ✓ shipped
- Phase B.1 — Layered defence (reserve-driven) ✓ shipped
- Phase B.2 — Cover (treeline) ✓ shipped (visible if maps have trees
  near the front)
- Phase B.3 — Overlapping fields of fire — not started
- Phase B.4 — Mounted transport ✓ shipped + **IsIdle-fix shipped
  2026-05-14**; awaiting playtest confirmation
- Phase C — Reserve pressure response — not started
- Phase D — Offensive 3:1 — not started
- Phase E — Personality differentiation — not started
- Phase F — Honest fog — not started

## IsIdle-fix shipped (2026-05-14)

Root cause was three-fold, not two:

1. **LayeredDefence pulled carriers forward.** Its `ExcludedActorTypes`
   default didn't include `bradley/bmp2/m113`. Fresh carriers got
   AttackMove orders → walked to the front → engaged via AutoTarget →
   `IsIdle = false` forever. Fix: added carriers to the exclusion set.
2. **MountedTransport required `IsIdle`** — even after fix 1, a carrier
   sitting at the SR rally could enter Attack against a distant scout
   in scan range, again failing the candidate filter. Fix: dropped
   `IsIdle`, added `cargo.IsEmpty()` instead.
3. **Loading didn't pin the carrier.** Without an order to the carrier
   itself, AutoTarget kept yanking it into chases — passengers would
   walk up but the carrier kept moving. Fix: send `Stop` order to the
   carrier when starting a Loading task. Cancels the Attack activity
   and parks the carrier so passengers actually catch up to board.

Plus a per-carrier diagnostic in `TryAssignNewTasks` that logs WHY
each owned carrier is or isn't a candidate — so the next time
`carriers-candidate=0` reappears, the cause is named in the log.

Next time picking this up: run a v2-vs-v2 skirmish, watch chat for
`[v2-transport]` lines, tail `debug.log` for per-carrier rows
showing `→ OK` instead of `→ skip`, then look for infantry actually
loading into Bradleys/M113s/BMP-2s and getting ferried forward.
