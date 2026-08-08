# Post-fix supply-truck behaviour — is it still dithering, and why?

**Read against `main @ e79ddd97`** (`git status -sb`: `main...origin/main [ahead 22]`, 0 behind upstream;
working tree clean apart from the four known untracked paths). Static analysis of the merged code plus
mining of a **real post-merge play session log** (§4). **No autotest, batch or tournament was run.**
Read-only: no engine or YAML file was modified.

---

## 0. Verdict

**(c), with one correction that matters, plus a genuinely separate (b) that is the more damaging half.**

- **The primary loop is (c): the merged design working as specified, and it still looks like dithering.**
  The manager's reasoning is **confirmed** in mechanism. It is a limit cycle by construction. §1 establishes
  the period and amplitude.
- **Correction to the manager's framing: the retreat is TWO legs, not one.** The dwell and the leg model —
  the two mechanisms added to *bound* the retreat — compose to **double** it. Amplitude is ~23 cells, not
  ~12. §1.3.
- **(b) is real and separate: the pull side has no leash against a receding provider.** Out-of-ammo
  soldiers chase a truck that is 3× faster than they are, with no distance bound, no timeout, and no
  re-selection, while combat-inert. This is not something the evac fix caused, but the evac fix is what
  *triggers* it on every cycle. §2.
- **Not (a).** Nothing in the fix is failing. Every latch the review closed is still closed; I re-derived
  them and they hold. In particular the loop is now genuinely *closed* (§1.1), which is what the fix
  claimed and delivered.
- **A second, older loop (§2.2 of the 0807 recon) is still live and untouched by this merge** — and it is
  visually distinguishable from the evac loop. §3.

Two of the user's three observations are one mechanism. The truck leaving its aura mid-delivery is
*precisely* the event that flips a replenishing soldier back into `Approaching` and starts the chase.

---

## 1. The primary loop: period and amplitude

### 1.1 What the evac decision actually reduces to post-fix

`EvacuateWithDwell`'s entry test is `ShouldEvacuate(dangerAtTruck, dangerAtDestination, 60)`
(`SupplyLogisticsMath.cs:195`). **The destination term can never reach 60**, on either branch:

| Branch | `Gated` | `DestinationDanger(gated, danger)` | Can trip entry (≥60)? |
|---|---|---|---|
| Gate passed (`SupplyFollowerBotModule.cs:616-624`) | `true` | the reading, and the gate guaranteed it `< ReleaseLevel` = **45** | no |
| Relief valve (`:636-637`) | `false` | **0** by `DestinationDanger` (`SupplyLogisticsMath.cs:242`) | no |

So **the entry test is exactly `dangerAtTruck >= 60`, always.** This is worth stating plainly because it
is easy to read the `Gated` / `DestinationDanger` machinery as live control: with respect to the *evac
decision* it is now structurally dead. It is correct and defensive (it is what stops the latch coming
back), but it has no live effect. The gate at 45 is load-bearing on **selection** only.

The consequence is the generator of the cycle: **the module has no ability to refuse to set off toward a
hot cluster.** It can only abort once its own cell is hot. That is the relief valve's stated contract
("approach the least-bad one and abort on your own reading"), working as designed.

The loop *is* closed — retreating lowers `dangerAtTruck`, the only surviving term — which is exactly what
the fix set out to achieve. A closed loop settles only if it has a fixed point. This one does not: the
relief valve re-selects the same needy cluster as soon as the truck cools. **Closed, terminating per
excursion, and periodic.**

### 1.2 Speeds and geometry (all measured, not assumed)

| Quantity | Value | Source |
|---|---|---|
| Scan interval | 150 ticks = **6.0 s** @ 25 tps | `ai.yaml:726` |
| TRUK speed | 75 → 75/1024 cells/tick → **~11 cells per scan** | `vehicles.yaml:538` |
| TRUK acceleration ramp | `7, 6, 5, 4, 3, 2, 1` — costs a fraction of a cell per leg | `vehicles.yaml:539` |
| `^Infantry` speed | 25 → **3.66 cells per scan** — the truck is **3.0× faster** | `infantry.yaml:46` |
| Evac entry threshold | 60 | `ai.yaml:749` |
| Release level | 60 − 15 = **45** | `ai.yaml:749/764`, `SupplyLogisticsMath.ReleaseLevel` |
| Retreat leg | 12 cells | `ai.yaml:750` |
| Dwell | 1 scan | `ai.yaml:765` |
| Leg-arrival tolerance | `RepathThresholdCells` = 3 cells | `SupplyFollowerBotModule.cs:862` |
| Truck supply aura | 5c0 radius, `RearmDelay` 6 ticks | `vehicles.yaml:544-545` |

The 60-contour sits at **roughly the believed enemy weapon radius**: per the 2026-08-07 contour entry in
`DISCOVERIES.md`, a rifle stamps intensity ≈1333 with a per-cell falloff of ~78–235, so 60 is within about
one cell of the envelope edge. "Danger ≥ 60" means "I have crossed into an enemy weapon envelope", not
"things are getting warm".

### 1.3 The cycle, scan by scan — and where the second leg comes from

Trace of `StepEvac` (`SupplyFollowerBotModule.cs:828-882`) at the shipped values:

| Scan | State | What happens |
|---|---|---|
| **N** | approaching, own cell crosses 60 | entry fires (undamped). `legDriven` is true via `!wasEvacuating` → **leg 1** ordered, 12 cells toward nearest SR. `hold := StepEvacDwell(0, true, 1) = 1`. |
| **N+1** | `heldBefore = 1` | `EvacuateWithDwell` returns true at `hold > 0` (`:201-202`) **whatever the danger reads**. Truck has covered ~11 of the 12 cells, so `(truck.Location - retreatCell).LengthSquared <= 9` → `legDriven` true → **leg 2 ordered**, another 12 cells. `hold := 0`. |
| **N+2** | `heldBefore = 0` | Entry: `dangerAtTruck >= 60`? No — ~23 cells rearward. Release: `dangerAtTruck >= 45`? Normally no → `evacState.Remove`, follow resumes. Leg 2 is abandoned ~11 of 12 cells in. |

**This is the correction to the manager's model.** The commit message and the in-code prose both describe
the retreat as bounded to one leg ("the dwell covers exactly one retreat leg"). It does not: the dwell
holds the branch true for scan N+1, and on that same scan the leg model observes leg 1 as *driven* and
issues leg 2. The dwell's purpose is to stop the branch being re-decided mid-leg; its side effect is to
guarantee a second leg. **Two mechanisms intended to bound the retreat compose to double it.**

Nothing here is a latch — if the truck is still over 45 at N+2 it simply legs again and cools further,
which terminates. But the *typical* amplitude is 2 legs, not 1.

### 1.4 Period and amplitude

- **Retreat: 2 scans, ≈23 cells** (12 driven + ~11 of the second leg).
- **Return: ≈23 cells at ~11 cells/scan = 2.1 scans → 3 scan boundaries.**
- **Cycle ≈ 5 scans ≈ 750 ticks ≈ 30 seconds, peak-to-peak ≈ 23 cells.**

Bounding the estimate for terrain, pathing detours and the acceleration ramp: **4–6 scans (24–36 s),
23–34 cells each way.** That is squarely "a few scans", as the manager predicted, and it is slow enough
to be unmistakable to a watching player — roughly two full out-and-back excursions per minute.

### 1.5 Does it ever sit inside its 5-cell aura?

**Yes — but only during the scan-phase overshoot, i.e. for part of one scan out of ~5.**

The abort is only *detected* at a scan boundary. Between the last scan that read cold and the scan that
reads ≥60, the truck keeps executing its last Move (toward the follow cell, next to the cluster) and can
travel up to ~11 cells past the contour. Writing `R` for the believed enemy weapon radius and `d` for the
distance from the enemy to our engaged units (`d < R`, since they are in contact), the truck aborts `R − d`
cells short of the units, and delivers iff **overshoot ≥ (R − d) − 5**.

This is exactly the residual recorded at `DISCOVERIES.md:13`, and **what the user describes is consistent
with it** — the entry already predicts "rifle-vs-rifle at ~10 cells: delivers in most scan phases". What
the user's report adds is the part that entry did not cover: the *duty cycle*. Even in the phases where
delivery happens, the truck is inside the aura for a fraction of one scan and then leaves for ~4. With
`RearmDelay: 6`, 60 ticks in aura is ~10 batches — real, but bursty, phase-dependent, and immediately
undone by the departure (§2).

**I could not establish the delivered-rounds-per-minute figure.** It depends on `R`, `d` and the scan
phase, none of which the module measures and none of which are determinable statically.

### 1.6 The Stage-E detour is structurally inert on exactly the path that needs it

`DangerFieldRouting: true` (`ai.yaml:732`) with `GroundDangerSafeThreshold: 15`, so one might expect the
two-leg lateral detour (`SupplyFollowerBotModule.cs:445-461`) to be a third source of visible weaving. It
is not, on the relief-valve path, and the reason is structural:

`PathMaxGroundDanger` samples **both endpoints** (`GroundDangerNav.cs:64` and `i <= steps` at `:65`). A
detour is only returned when `worst < direct` **strictly** (`:132`), where `worst = max(PMD(from, wp),
PMD(wp, to))` — and both terms include `to`. So if the destination is itself the hottest point on the
route, **no waypoint can strictly improve, and `DetourWaypoint` returns null.** On the relief-valve path
the follow cell *is* the hottest point by construction (it is the least-bad *needy* cluster, sitting in a
firefight). The truck therefore takes a single direct Move into the danger, and its only protection is the
undamped evac entry — drive in, then abort.

The complement is also true and is worth recording as the design's actual shape: when the destination is
**cool** (gate passed) and only the corridor is hot, the detour *does* fire and does route around it. So
the two mechanisms are complementary rather than redundant. **Consequence for the report: the weaving the
user sees is the evac branch, not the detour.** The detour is inert precisely when the truck is being sent
somewhere dangerous.

---

## 2. The second observation — the pull side against a receding provider

**This is the more damaging finding, and it is a separate defect from the oscillation.**

### 2.1 What `AutoSeekSupplies` does when its provider recedes: chases, essentially without bound

The chain, verified end to end:

1. **The 20-cell leash is applied at SELECTION ONLY** — `SupplyHuntMath.WithinLeash` is called inside
   `FindNearestUsableProvider` (`AutoSeekSupplies.cs:152`) and nowhere else.
2. **`CanServe` has no distance term at all** (`AutoSeekSupplies.cs:177-217`). It is deliberately the one
   eligibility test, re-asked every tick by the activity (`SeekSuppliesAndReturn.cs:86-89`) — so a
   provider that dies, drains, pauses or starts restocking releases the soldier immediately. **A provider
   that merely drives away does not.** The trait's own doc comment says as much
   (`AutoSeekSupplies.cs:42-43`); the 0807 census recorded it too.
3. **The approach is an actor-tracking move.** `MoveWithinRange(Target.FromActor(provider), 5c0)`
   (`SeekSuppliesAndReturn.cs:138`). `MoveWithinRange.ShouldStop` is *only* "am I at correct range"
   (`MoveWithinRange.cs:38-43`), and `MoveAdjacentTo.Tick` cancels and re-queues the inner move **every
   time the target's cell changes** (`MoveAdjacentTo.cs:95-100`). It ends on arrival-in-range, or on the
   target becoming invalid — not on the target running away.
4. **The soldier cannot re-decide.** It is on an activity, so it is not idle, so `INotifyIdle.TickIdle`
   never fires and it never re-scans for a nearer provider (`AutoSeekSupplies.cs:91`). It is locked to
   this one truck for the whole errand.
5. **It cannot win the race.** Infantry 25 vs TRUK 75 — the truck opens the gap by ~7.3 cells per scan
   during a retreat.

**The only bound is `MaxApproachAttempts = 3` (`SeekSuppliesAndReturn.cs:42`, `:153-154`), and it counts
approach CHILDREN, not distance or time.** Each attempt is an unbounded chase. What actually consumes the
counter is aura entry/exit flips: `SupplyHuntMath.NextState` sends `Replenishing → Approaching` when the
soldier "drifts back out of the aura", the state change cancels the child (`:120`), and the next
`Approaching` tick increments `approachAttempts` (`:141`). So the soldier gets roughly **three aura
crossings and then walks home**, possibly still empty. The `MaxStalledTicks = 300` guard does not help —
it only runs in `Replenishing`, never during an approach.

Net: two parties re-deciding on independent cadences, exactly as the brief suspected — but the failure is
not symmetric. **The truck never considers the soldier at all** (the follow target is a cluster centroid,
and the Tier-2 hunt is skipped whenever a cluster exists), while the soldier considers only the truck and
cannot let go.

### 2.2 Does the merged fix make it better or worse?

Honestly: **mixed, and I cannot settle the delivery balance statically.**

- **Before the fix**, the retreat was monotonic to the SR and the truck then *parked* there. A parked
  provider is the easy case for `MoveWithinRange` — it terminates. A chasing soldier that survived its 3
  attempts would actually arrive and refill. The failure was "no supply at the front", not "no supply".
- **After the fix**, the truck reverses every ~2 scans and never parks. The soldier now gets brief aura
  crossings as the truck comes back *through* it (combined closing speed ~0.098 cells/tick across a
  10-cell aura transit ≈ 100 ticks ≈ 16 batches), which may well be *more* total delivery. But the
  crossings burn the 3-attempt budget quickly, and the soldier spends the entire errand combat-inert
  (`AutoSeekSupplies.cs:25-28` — no `AutoTarget` idle scan, no retaliation).

So: **plausibly neutral-to-better for rounds delivered; clearly worse for the visible symptom and for how
long an out-of-ammo soldier is out of the fight.** That matches what the user reported watching.

---

## 3. The other loop that is still live (untouched by this merge)

§2.2 of the 0807 recon — the `residueUnusable` / `DropsSupplyCache` collision — **was not addressed by
`e79ddd97` and is still present verbatim**:

`ResidueVerdict` re-latches `residueUnusable` every 7 ticks and can flip **both ways**
(`SupplyProvider.cs:290-295`, `:944`) → `CountsAsEmpty` (`:153`) → `IsLowOnSupply`
(`SupplyFollowerBotModule.cs:909-915`) drops the truck from the roster and releases its claim → the truck
idles → `DropsSupplyCache.ITick` (`:210-216`) queues `RotateToEdge` (`:246`).

**This one is visually distinguishable, and the user can tell them apart without any instrumentation:**

| | Evac loop (§1) | Residue loop (§3) |
|---|---|---|
| Rearward heading | toward the player's **Supply Route** | toward the nearest **map edge** |
| Truck supply bar | amber | **red** (`SupplyProvider.cs:850` colours it red on unusable residue) |
| Truck supply level | any | below 50, or holding only unaffordable residue |
| Ends in | return to the front | sold at the map edge |

Note the coupling to §2: a starving soldier walking into range is exactly what flips the residue verdict
back to *usable*, so the pull side drives this loop too. The user's description ("ordered forward and then
back again", with soldiers seeking the truck) is most consistent with the **evac** loop, but if the truck
they watched was heading for a map edge with a red bar, it is this one instead and §1 does not apply.

---

## 4. Logs — they exist, and they contain nothing about trucks

**Where they are.** `Log.cs:128` writes to `Platform.SupportDir + "Logs"`; on Windows `Platform.cs:174`
resolves that to **`C:\Users\fredr\AppData\Roaming\OpenRA\Logs\`**. No launch script passes
`-Engine.SupportDir`, and the `<EngineDir>\Support\` override path (`Platform.cs:214-216`) does not exist
on this machine. *Incidental doc bug: `launch-game.cmd:46` and `ww3-dev.ps1:148` both point at
`Documents\OpenRA\Logs`, which does not exist — the ww3-dev log-cleanup step is a silent no-op.*

**A real post-merge session exists.** `debug.log`, 1,497,745 bytes, 12,246 lines, modified
**2026-08-08 03:29** (session 03:09–03:29, ~14,356 ticks), map "River Zeta WW3", **Stable AI 0802 vs
Experimental AI**. It ran against `OpenRA.Mods.Common.dll` dated 2026-08-07 17:13 — the post-merge build.
Also present: `perf.log`, `server.log`, `traitreport.log` (08-06), and 12 `exception-*.log` (newest
08-03, none from this session). `tools/autotest/tournament-results/` holds only 08-02 runs — pre-merge.

**Mining result: no truck evidence, and none is possible at this build.** Counts in `debug.log`:
`SupplyFollower` **0**, `dwell` **0**, `truk` 2 (both scenario spawn lines, `:40` and `:81`). The 2,325
`[exp-transport]` lines are `MountedTransportBotModule` — IFV carriers, not supply trucks
(`MountedTransportBotModule.cs:520/524/578`). The 1,141 `supply` hits are `supplyroute@x,y` target names
inside `[exp-terr]` / `[exp-ambush]` / `[exp-offense]`. The 2 `evac` hits are `[exp-ooa] ... evac=1`
(out-of-ammo sweep), not truck evac.

**Root cause of the gap, verified by grep:** `SupplyFollowerBotModule.cs`, `SupplyLogisticsMath.cs`,
`AutoSeekSupplies.cs`, `SupplyProvider.cs` and `SeekSupplyProvider.cs` contain **zero** occurrences of
`Log.Write`, `BotDebug`, `TextNotifications` or `Console.WriteLine`. **The entire supply/evac path is
instrumentation-free.** No setting turns on logs that do not exist.

**What would be needed.** `Debug.BotDebug` in `%APPDATA%\OpenRA\settings.yaml` (default false,
`Settings.cs:161`) would **not** help: it gates `AIUtils.BotDebug` (`AIUtils.cs:92-96`), which routes to
in-game chat rather than a file, and no supply-path code calls it. Getting evidence requires adding
`Log.Write("debug", ...)` to `SupplyFollowerBotModule.BotTick` / `StepEvac` first — minimally: tick,
truck ActorID, branch taken, `dangerAtTruck`, `hold`, and the ordered cell. That would let a single
ordinary play session settle §1.4 and §1.5 empirically instead of by derivation.

---

## 5. Drop-and-leave: assessment

**The manager's core claim is correct and I verified the mechanism: a stationary destination dissolves
all four problems at once** — abort geometry, scan-phase overshoot, weapon envelopes, and the
recede-while-being-approached defect of §2. The last one is the strongest argument and it is worth being
explicit about *why*: against a static actor, `MoveWithinRange.ShouldStop` becomes reachable, so the
approach child terminates, `MaxApproachAttempts` stops being consumed by aura-exit flips, and the soldier
gets a real `Replenishing` dwell bounded by `MaxStalledTicks` instead of a stern chase.

**Buildable on top of `SupplyFollowerBotModule` without a rewrite: yes.**

- **The order is already bot-issuable with no new plumbing.** `DropsSupplyCache` exposes it as a bare
  self-targeted order — `new Order("DropSupplyCache", self, queued)` (`:291`, `:298`), resolved at
  `:129-131`. A bot module can issue `bot.QueueOrder(new Order("DropSupplyCache", truck, false))`
  directly; no targeter, no cursor, no new activity. `CanDropCache()` (`:75-83`) requires
  `CurrentSupply > 0` and a cell holding nothing but self or another cache.
- **The cache is complete**: 750 supply, 4c0 aura, per-instance quantity, merge-on-drop, sprite tiers,
  capturable, `RemoveBelowSupply: 1` (`misc.yaml:370-427`).
- **The pull side already works against it** — `AutoSeekSupplies` skips only docking-gated providers
  (`:201-202`), and the cache has no `DockedCondition`.

**The two decisions it needs:**

1. **WHERE — mostly solved; `ForwardStagingMath.StagingCell` is genuinely reusable.** It is engine-free
   `Func`-driven integer math (`ForwardStagingMath.cs:89-93`): steepest descent on
   `ControlField.FrontierDistanceAt` with a `dangerAt(n) > threshold` guard that closes off forward
   neighbours, so the walk **halts behind the defended line** — exactly the requirement, and it is the
   same primitive `PoiOffensiveBotModule.ResolveStagingAnchor` already consumes. Use a larger standoff than
   the offense module's. **Better than the 0807 recon knew: `SupplyFollowerBotModule` already resolves
   `ControlField` (`:240`)**, for `GroundDangerAt`'s de-aliasing — the trait handle is in hand, so this is
   not even a new lookup.
   *Hard constraint, unchanged:* `ControlField` exists only for `InfluenceStack.Participates` players. On
   Normal/Rush/Turtle/legacy the field is flat, `StagingCell` returns the start unchanged, and the truck
   would drop its cache at the Supply Route. Drop-and-leave is @experimental + @stable only unless a
   fallback anchor is designed.

2. **WHEN — unsolved, and this is the real cost. The census did not cover it.** The drop is all-or-nothing:
   `DropSupplyCacheHere` calls `SetSupply(0)` (`DropsSupplyCache.cs:85-125`). The moment it lands,
   `CountsAsEmpty` is true → `IsLowOnSupply` → `SupplyFollowerBotModule` releases the truck **and its
   blackboard claim** (`:262-271`) → the truck idles → `DropsSupplyCache.ITick` drives it to the **map
   edge and sells it** (`:210-216`, `:246`). **That is the default outcome of dropping a cache, and it is
   almost certainly not what is wanted.** So drop-and-leave requires deciding the truck's post-drop fate,
   and the obvious answer is blocked: `TryRestock` is gated on `ShouldSelfRestock()`, which returns false
   under `ResupplyBehavior.Evacuate`, which is TRUK's AI default (`vehicles.yaml:516`). The options are an
   explicit bot-issued `Restock` order, or flipping `InitialResupplyBehaviorAI` — either is a real
   decision with knock-on effects, not a detail.
   Secondary: the cache has `Health: 5000`, `Armor: Light`, `Targetable: Ground, Structure` and **no**
   `NoAutoTarget` (`misc.yaml:382-387`) — enemies shoot it unaided. A cache at a proper standoff is fine;
   one dropped too far forward is a gift.

**Overall: the design is right and the build is small, but "where" is the easy half and "when" is the
half that is genuinely open.** Budget the post-drop truck lifecycle as part of the work, not as follow-up.

### A cheaper stopgap, offered as a lever and explicitly NOT verified

Because the entry test now reduces to `dangerAtTruck >= 60` alone (§1.1) and the amplitude is dominated by
the second leg (§1.3), the *visible* symptom could be roughly halved by config alone:

- **`EvacDwellScans: 0`** removes the held scan, so leg 2 is never issued and the truck re-decides at N+1
  against the release level. Amplitude ~23 → ~12 cells, period ~30 s → ~15 s. **Tradeoff:** the dwell is
  one of the two damping mechanisms; without it the branch can flip back to follow mid-leg, and readings
  parked near 45 may chatter. The leg model alone still prevents the receding-target restart that was the
  pre-fix failure.
- **Lowering `EvacRetreatCells`** cuts amplitude proportionally without touching the branch logic.

Neither removes the limit cycle — it is structural, and only a stationary destination (or refusing to
select hot clusters at all, which reintroduces park-and-starve) removes it. **Both are untested; they need
a run before anyone believes the numbers.**

---

## 6. Things I could not establish

- **Delivered rounds per minute, before or after the fix.** It depends on `R`, `d` and the scan phase; the
  module measures none of them, and the logs carry no supply telemetry (§4).
- **Whether the user's truck was on the evac loop (§1) or the residue loop (§3).** The description fits §1
  better, but §3 is live and untouched. The bar colour / heading test in §3 settles it by eye.
- **How often the relief valve fires versus the gate passing, in a real match.** The de-aliasing entry
  argues the valve is the ordinary in-contact path; I did not measure the pass rate, and it decides
  whether §1 is the common case or an occasional one.
- **The net delivery effect of the fix on the pull side** (§2.2) — the two mechanisms push in opposite
  directions and only a run separates them.
- **Whether `approachAttempts` typically exhausts before or after a soldier refills.** The counter's
  advance depends on aura-crossing frequency, which depends on the same unmeasured geometry.
