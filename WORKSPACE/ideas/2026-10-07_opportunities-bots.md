# Opportunities — BOT / AI COMPETENCE (scout pass 2026-10-07)

_Read at `main @ c276679c`, worktree `wt/scout-ai`. **Read-only scout: no build, no launch, no autotest,
no lint was run.** Every claim below was read in source at that ref; where a claim rests on someone else's
read I say so. "Hypothesis" means not observed in a match._

**Angle:** visibly-stupid bot behaviour a watching player would screenshot, and cheap incremental fixes to
`@experimental` that `@stable` may inherit (CLAUDE.md policy). The bar is the binding release ruling
`PIPELINE.md:38-43`: *visible stupidity is HIGH; deep architecture is not release-gating; cheap incremental
improvement is in scope.*

**Not re-proposed (queued or ruled):** item 18 "should I attack?" layer, 34/35 transports, 40 danger scale,
43 re-baseline, 44 AA arithmetic, 45 missiles, 76 paused-armament lockout, 79/81 contestation, 82 longest-
armament standoff, 84 seek gate, 85 lead-hold, 87 scorer; closed 56/63/64/66/86. The close-in ratchet was
**measured and lost** (`ai.yaml:937-973`, `1452a82f`), so nothing here re-arms it. Ejected-crew loss
(0902-loss-mining §1.4) is superseded by `VehicleCrewInfo.AutoEvacuateOnEject` (`PoiOffensiveBotModule.cs:211-214`).

**Evidence base used.** `WORKSPACE/analysis/0902-loss-mining.md` (the only per-unit-type loss data in the
repo; n=2 valid matches, treat as directional); `benchmarks/260923-rebaseline.md` (40/40 matches end on
`time_limit`, **0 SR overruns**, `:64-65`; parity within its own noise band); the 2026-09-03 ratchet sweep
recorded in `ai.yaml:950-966` (100% of ~1300 axis retires are `reason=dropped`; force inside the 10-cell
ring 1.3%); `bugs/discovered.md` and `DISCOVERIES.md` entries cited per item.

## Ranking

Ranked by **(how visible in one match) × (confidence the defect is real) ÷ (cost to fix and verify)**.

| # | Title | Visible? | Confidence | Cost | Moves `@stable`? |
|---|---|---|---|---|---|
| 1 | Sticky-target selection keeps only ONE of several marginal axes — static bug in the churn lever | indirect, high | **code-confirmed defect** | S | yes (shared method) |
| 2 | A dry soldier whose walk to the truck ends short freezes for the rest of the match | **very** | code-confirmed latch | S | yes — and humans |
| 3 | A retreating squad is dissolved mid-withdrawal and its units are marched straight back in | high | code-confirmed path; frequency hypothesis | S | yes |
| 4 | The bot never comes home when its own Supply Route is being contested | **very** (it is how you beat it) | code-confirmed absence | M | yes, if not gated |
| 5 | America's Apache can never form a pair: held forever when rich, sent alone when poor | high | code-confirmed | S | no (flag is exp-only) |
| 6 | Artillery is recruited as line infantry by the garrison and ambush modules | high | code-confirmed eligibility; frequency hypothesis | S | yes |
| 7 | Scout and transport helicopters never re-check danger after take-off | high | code-confirmed; link to losses is hypothesis | M | yes |
| 8 | The counter-battery radar is bought, deployed, and read by nothing | medium | code-confirmed | S–M | no (module exp-only) |
| 9 | The scout picker overflows: unexplored map edges rank LAST, scouts sweep column by column | low–medium | **code-confirmed defect** | S | yes |
| 10 | A bot engineer follows a moving tank across the map instead of repairing the parked one | medium | code-read; recorded 09-22 | S | no (module exp-only) |
| 11 | The bot never buys or fires the conventional strike powers a human can | medium | gap, not a defect; availability per mode unverified | M–L | design call |

---

## 1. Sticky-target selection keeps only one of several marginal axes

**Today.** The bot runs up to `MaxAxes: 4` attack axes (`PoiOffensiveBotModule.cs:66`). Every reevaluation it
re-ranks targets and keeps the top k. `SelectStickyTargets` (`:2563-2595`) is the hysteresis that should stop
an axis being thrown away because its target slipped just outside top-k: if the existing target is within
`ReassignScoreThresholdPct` (30, `:92`; `ai.yaml:384` / `:3209`) of the cutoff, it is swapped back in.
**But the swap always writes the same slot** — `top[top.Count - 1] = existing` (`:2589`). With two axes both
marginally out, the second retention **overwrites the first one it just retained**, and that first axis is
then retired `"dropped"` at `:1857-1864`, its units freed. Only one marginal axis can ever be kept per pass.
The comment says *"drop the weakest newcomer"*; the code drops whatever sits in the last slot, which after
the first swap is the sticky target itself.

What a watching player sees: groups that form, walk ~50 cells toward an objective and dissolve mid-approach,
then reform on something else — the "army drifts around and never arrives" look. This is the lever the
09-03 sweep named as **"THE REAL LEVER … Deliberately NOT implemented"** (`ai.yaml:965-966`) after measuring
100% `reason=dropped` retires (`:957-963`); the slot bug is a concrete, cheap part of that lever nobody had
recorded.

**Premise check.** `git log -S SelectStickyTargets` / `ReassignScoreThresholdPct`: no change since the
09-03 ratchet commits (agent read, path-limited `1452a82f..HEAD` diff). Not in PIPELINE (`grep -i sticki`
returns nothing in `PIPELINE.md` or `pipeline/items/`). Recorded only in `closed-items.md:1343,1399`
(as open churn) and `ai.yaml:965`.

**Why #1.** It is the only proposal here that is (a) a provable code defect, (b) on the path that the one
real measurement in the repo identifies as the biggest open behaviour problem, and (c) verifiable without a
launch.

**Cheapest verification — static, no launch.** Extract the selection into a pure function
(`PoiOffenseMath.SelectSticky(IReadOnlyList<(uint id,long score)> ranked, IReadOnlyCollection<uint> axisTargets, int k, int thresholdPct)`)
and NUnit it. **Pass bar:** ranked `[A100, B90, C85, D80, E78]`, k=3, existing axes on `{D, E}`, threshold 30
(`ScoreBeatsByThreshold` is `candidate*100 > current*(100+pct)`, `PoiOffensiveBotModule.cs:5834-5835`).
Today: D retained into slot 3 (85 does not beat 80 by 30%), then E retained into **the same slot**, so the
result is `{A, B, E}` and D's axis is retired. Fixed: D evicts C, E evicts the next-lowest newcomer B (90 does
not beat 78 by 30%) → `{A, D, E}`. **RED first:** the current algorithm transliterated into the test must
return `{A, B, E}` and the assertion message must name D as the evicted retained id. *Design point for the
worker to settle, not guess:* whether a retention may ever evict the rank-1 target — recommend "never", as a
guard that keeps hysteresis from freezing the bot onto stale targets. Then optionally one benchmark-style read: count `retire reason=dropped`
per match before/after on two seeds (manager's call; not needed to merge a correctness fix).

**Dispatch-ready brief.**
- *Files:* `engine/OpenRA.Mods.Common/Traits/BotModules/PoiOffensiveBotModule.cs:2563-2595`;
  `PoiOffenseMath` (pure-math sibling, where `ScoreBeatsByThreshold` already lives); new test in
  `engine/OpenRA.Test/OpenRA.Mods.Common/PoiOffenseTest.cs` (or a new `StickySelectionTest.cs`).
- *Approach:* build the result as "top-k newcomers ∪ retained", evicting the **lowest-scored non-retained**
  entry for each retention (never a retained one), stop when no newcomer is left to evict. Keep iteration
  order over `axes` and the tie-break deterministic (list order, then ActorID) — **zero RNG**, integer compares
  only, per `influence-stack.md:127`.
- *Acceptance:* NUnit red→green as above; `make check` clean (RCS gate, CLAUDE.md); `dotnet test` green.
  Do not tune `ReassignScoreThresholdPct` in the same commit — that would be a second unmeasured change
  (memory: "never make a second unmeasured behavioural change").
- *Read first:* `ai.yaml:937-973` (why the ratchet lost and what telemetry to keep);
  `DOCS/reference/influence-stack.md` §Invariants.
- *Risks:* **moves `@stable`** — the method is shared and both twins run it with MaxAxes 4. It is a bug fix,
  so per CLAUDE.md it flows through; say so in the commit so the next re-baseline is taken knowingly. Behavioural
  risk: more axes survive → force spread thinner across targets. That is what the hysteresis was always
  supposed to do, but it should be watched in the next benchmark, not assumed neutral.

---

## 2. A dry soldier whose walk to the truck ends short freezes for the rest of the match

**Today.** `SeekSupplyProvider` is the activity a dry soldier runs to walk to a supply truck. Out of range,
it queues one `MoveWithinRange` and sets `moveQueued = true` (`Activities/SeekSupplyProvider.cs:256-262`).
`moveQueued` is cleared **only** on the in-range path (`:246-249`), on a *different* retarget (`:216-224`)
and on `BeginReturn` (`:284`). If the child move simply **ends** without reaching range (blocked path, truck
re-parked, crowd at the truck), `ChildActivity` becomes null, `moveQueued` stays true, the retarget every
`RetargetInterval` finds the **same** truck so nothing resets, and the activity returns `false` forever.
The soldier is not idle, so no auto-seek retries; he is dry, so the recruit gates skip him.

What a watching player sees: exactly the release audit's own example — *"soldiers standing around out of
ammo"* (`PIPELINE.md:41`) — a rifleman frozen a few cells short of a parked supply truck for the rest of
the game.

**Premise check.** Recorded `bugs/discovered.md:2603` (2026-08-11); `0d7fb847` the same day fixed a sibling
defect and the entry says the latch survives it. I re-read `:172-262` at HEAD: the latch is intact. Not
item 84 (that is a *should-I-seek* gate, a different decision); no other queue item.

**Cheapest verification — one autotest.** `test-dry-infantry-reseeks-after-short-walk`: one dry rifleman
(ammo 0) at A; one parked allied supply truck at B, 12 cells away through a 1-cell gap; a neutral blocker
actor parked in the gap at t0, removed by Lua at t200 (`Actor.Destroy`). The walk ends short against the
blocker. **Pass:** rifleman's ammo > 0 by t900. **Fail text:** `"rifleman still dry at t900; dist-to-truck=N"`.
**RED first:** run against HEAD and require that exact fail text (the activity never re-plans after t200).
Verify map connectivity per `DOCS/recipes/AUTOTEST.md` §"Verify before you ask for a slot" and run
`lua-gate`.

**Dispatch-ready brief.**
- *Files:* `engine/OpenRA.Mods.Common/Activities/SeekSupplyProvider.cs`; the new scenario under
  `tools/autotest/scenarios/`.
- *Approach:* at the out-of-range branch, treat `moveQueued && ChildActivity == null` as "the walk ended
  short": clear the latch and re-plan, with a small retry budget (e.g. 3) after which `BeginReturn` — so an
  unreachable truck cannot produce an infinite re-path loop (the engine never reports `MoveResult` failure,
  `bugs/discovered.md:2688`, so a budget is the only termination guarantee).
- *Acceptance:* scenario RED on HEAD with the exact message, GREEN with the fix; one run of the existing
  `test-supply-safe-front-keeps-cargo` as a regression read (item 56/84's instrument).
- *Read first:* `pipeline/items/84-seek-gate-hold-when-truck-closing.md` (adjacent gate, do not merge the two);
  `DOCS/reference/economy.md`.
- *Risks:* **this is an engine activity, not a bot module — it moves human players' soldiers and both bots
  equally.** That is the right outcome for a freeze, but it is a player-visible change and should be said in
  the commit. No RNG, no sync-state read outside the activity, so no desync surface beyond the activity
  itself (which is already synced).

---

## 3. A retreating squad is dissolved mid-withdrawal and marched straight back in

**Today.** When an axis is losing (believed enemy ≥ `RetreatForceRatioPct` 200% of own for
`RetreatSustainEvals` 2, `ai.yaml:762-770`), `CombatRetreatMath.Step` puts it in Retreating and it is ordered
back to the rally cell, its own SR (`PoiOffensiveBotModule.cs:5061-5118`, `RallyCell :5050`). But retreat is a
**state on the axis**, and the retire step at `:1857-1864` has **no exemption for it**: if the target falls out
of `SelectStickyTargets`, the axis is released `"dropped"` and its units go into `free`. Worse, the axis count
is recomputed from **current** force size every pass (`DesiredAxisCount(totalOffensive, …)`, `:1851`), so the
casualties that triggered the retreat also shrink k — the losing axis is the likeliest to be cut. The freed
units are then handed to `StageFreePool`, which walks them **forward** to the staging anchor (agent read of
`:2047`, `:3014-3228`).

What a watching player sees: a squad breaks off a lost fight, turns for home, then turns round and walks back
into the same guns — the classic "AI feeds units one at a time" screenshot. (Only `under-min` is retreat-
exempt, `:2013-2014`.)

**Premise check.** Retire reasons are exactly three (`"no-targets" :1797`, `"dropped" :1862`, `"under-min"
:2016`); no `-S` change to the `"dropped"` site since 2026-08-01. The 0902 report's "the bot never breaks off
for danger" is a misread (retreat is the state, `[exp-retreat]` the log) — this proposal is the narrower,
real defect inside that misread. **Hypothesis, unmeasured:** how often a Retreating axis is the one dropped.
A log join (`[exp-retreat] state=Retreating` followed within one eval by `retire reason=dropped` on the same
axis id) over any preserved `match_*_debug.log` would confirm it without a run.

**Cheapest verification.** First the free one: grep the preserved debug logs for that join (static). Then
NUnit: extract the retire predicate (`ShouldRetire(axisInFinal, axisRetreating)`) and pin that a retreating
axis survives a drop until it reaches `RetreatSafeDistanceCells`. Optional autotest: an axis forced into
retreat on a map where its target simultaneously loses value (enemy steals it); **pass** = no
`retire reason=dropped` on that axis while `state=Retreating`, and its units' max distance from own SR is
monotone non-increasing for 300 ticks.

**Dispatch-ready brief.**
- *Files:* `PoiOffensiveBotModule.cs:1847-1866` (selection + retire), `:5061-5118` (retreat states).
- *Approach:* a retreating axis keeps its slot until it reaches the rally (then it may be retired normally),
  and is excluded from the `k` budget so it cannot evict a healthy axis. Gate behind a new Info flag
  `KeepRetreatingAxes` (C# default `false`, per the shared-trait rule in `architecture.md` §"Adding a
  behavioural field…"), set `true` on `@experimental`; the manager decides whether `@stable` takes it after one
  read — or set both and say so (CLAUDE.md: improvements flow).
- *Acceptance:* NUnit red→green; the log join shows zero retreating-then-dropped events in one bot-vs-bot
  match (manager-sanctioned run).
- *Read first:* `ai.yaml:796-822` (retreat damper and re-advance dwell) — the damper's arm (a) dwell is the
  thing that should govern re-advance, not the retire path.
- *Risks:* holds slots longer → fewer concurrent axes while one retreats; interacts with #1 (do #1 first and
  measure them separately, never in one arm).

---

## 4. The bot never comes home when its own Supply Route is being contested

**Today.** Contestation is the mod's win condition (`DOCS/reference/supply-route.md:93-100`; in 1v1, passive
and defeat land in the same tick, `:100`). Yet **no bot module reacts to its own SR's bar.** `PoiMap` drops
the own SR from defend targets with *"handled elsewhere"* (`World/PoiMap.cs:467-471`) and *"SR defence is out
of scope (decision #2)"* (`:420-421`) — and there is no elsewhere. The only bot read of `ControlBarFraction` is
the nuclear module (`NuclearBotModule.cs:623-634`, used at `:346`). `SquadManagerBotModule.ProtectOwn` is inert
(`ProtectionTypes` empty by default, `:62`, set nowhere in `mods/`). The one partial response is a
**purchase**: `AdaptiveProductionBotModule.TrySupplyRouteDefense` (`:373`) calls in a matched counter when
believed contacts sit near the SR — new units walk in from the edge, but the field army is never recalled.

What a watching player sees: you park a few tanks in the bot's beachhead circle while its army fights 30 cells
away at a derrick; trickle reinforcements die one by one at the edge; the bot loses without its main force ever
turning round. That is the most screenshot-able failure a stranger can produce, because it is *how you win*.

**Premise check.** `git log -S` "ControlBarFraction" in BotModules: only the nuclear module. Item 18
(`PIPELINE.md:619`) is a *parked future* endgame layer about *attacking*; item 79 changes the mechanic, not
the bot. Not queued. Benchmarks can't see this: bot-vs-bot has **0 overruns in 40 matches**
(`260923-rebaseline.md:64-65`), i.e. neither bot ever threatens the other's SR — so a human is the only
opponent who exposes it.

**Cheapest verification — one autotest.** `test-bot-recalls-to-contested-sr`: bot (`@experimental`) with 6
units already committed to a POI 30 cells from its SR; scripted enemy force of ~2× bot-SR-zone value placed
inside the bot's 10-cell circle at t100 and set to hold. **Pass:** ≥ 3 bot field units inside its own circle
before the control bar falls below 50% (read via the existing contestation Lua getters, or `Player.WinState`
per `supply-route.md:105` since TestMode suppresses the end screen). **Fail text:**
`"bar at N% and 0 field units recalled"`. RED first on HEAD.

**Dispatch-ready brief.**
- *Files:* `PoiOffensiveBotModule.cs` (new defend branch alongside `UpdateRetreatStates`), or a small new
  `SrDefenceMath` pure class + call site; reads `SupplyRouteContestation.ControlBarFraction`
  (`SupplyRouteContestation.cs:220-226`, public) via `world.ActorsHavingTrait<SupplyRouteContestation>()`
  filtered by owner (`supply-route.md:278`).
- *Approach:* when own bar < `SrRecallBarPct` (e.g. 70) **and** believed enemy value in the circle ≥ own value
  there, re-target the nearest axis (or the free pool, then the nearest axis) to an AttackMove on the own SR
  cell; release when the bar recovers above a hysteresis band. Bar reads are **not** fog-cheating (it is your
  own building's state); target the SR cell, not enemy positions, so no omniscient read is introduced.
  Flag default off in C#, on for `@experimental`.
- *Acceptance:* scenario RED→GREEN; one bot-vs-bot match to confirm it does not thrash when *both* SRs are
  quiet (expected: never fires, since bot-vs-bot never contests).
- *Read first:* `DOCS/reference/supply-route.md` (whole — the recurring trap; SR is never destroyed, only
  contested; contesting ≠ capturing); `DOCS/reference/game-model.md`.
- *Risks:* zero RNG (integer thresholds). Pulling the army home cedes POIs — the hysteresis band and the
  value comparison are what stop a single scout from recalling everything. This is a design-flavoured change
  on the central mechanic; the threshold is the user's to sanity-check, but the *absence* of any reaction is a
  defect under the "credible / not embarrassing" bar.

---

## 5. America's Apache can never form a pair

**Today.** `HelicopterSquadBotModule@experimental` wants `AttackSquadSize: 2` + up to `AttackSquadSizeBonus: 1`
(`ai.yaml:2875-2877`; draw at `HelicopterSquadBotModule.cs:862`). Below preferred size it launches partial only
when `AllowSoloAttackHeli` and spendable cash < `PairUpIncomeThreshold: 6000` (`ai.yaml:2882-2884`,
`:868-877`), else **returns and waits**. But America's heli lane caps `heli: 1, littlebird: 1`
(`ai.yaml:2796-2798`, 2026-08-30) and the littlebird is `Role: Scout`, not an attack heli
(`ingame/aircraft-america.yaml`, `AIHelicopterRole: Role: Scout`; the HELI is `AttackHeavy`). So America has
**at most one attack airframe**, `ready ≤ 1 < preferred ≥ 2`, always.

What a watching player sees: a rich US bot whose Apache hovers at its staging point for the whole match
waiting for a wingman that can never be bought; a poor one sends it in alone. 0902 loss-mining §1.3 recorded
the "bought singly, dies every time" half (n small).

**Premise check.** `git log -S PairUpIncomeThreshold`: `0cb7c808`, `3318d5c7` only; the cap commit
`79e2ce90` (08-30) did not revisit pairing. No queue item (grep `PairUp`/`lone heli` empty in PIPELINE).

**Cheapest verification — static + NUnit.** `HeliPackageMath.ShouldLaunchPartial` is already pure and pinned;
add a case. Fix shape: clamp `preferredSize` to the **attainable** count (sum of `UnitLimits` over attack-role
heli types for this player). **Pass bar (NUnit):** limit 1, preferred 2, cash 9000 → launch size 1.
RED first with the current function.

**Dispatch-ready brief.**
- *Files:* `HelicopterSquadBotModule.cs:852-890`; `HeliPackageMath` + its test.
- *Approach:* compute `attainable` once per launch pass from the player's attack-heli `UnitLimits`; use
  `min(preferredSize, attainable)`. **Keep the `LocalRandom.Next` draw exactly where it is with the same
  arguments** (`:860-862` comment) so the frozen path stays byte-identical. Alternative is pure YAML (raise
  `heli: 2` for America) but that reverses a deliberate 08-30 budget ruling — the user's call, so prefer the
  clamp.
- *Acceptance:* NUnit red→green; `make check`.
- *Risks:* `@stable` omits `AllowSoloAttackHeli` (`:2879-2881`) → if the clamp lives inside the
  `AllowSoloAttackHeli` branch, `@stable` is untouched; if outside, it moves `@stable` (an improvement — say so).
  A solo Apache going in more often may *raise* air losses; pair with #7.

---

## 6. Artillery is recruited as line infantry by the garrison and ambush modules

**Today.** The offense module carefully holds artillery back at weapon standoff behind a screen (agent read,
`PoiOffensiveBotModule.cs:3889-3933`, `:4318-4400`). But `PoiGarrisonBotModule` and `LaneAmbushBotModule`
both admit `UnitRole.IndirectFire` alongside MainBattle (`PoiGarrisonBotModule.cs:459-466`,
`LaneAmbushBotModule.cs:772-776`), choose recruits by distance only, and garrison AttackMoves them **onto the
POI cell** (agent read, `PoiGarrisonBotModule.cs:514`).

What a watching player sees: an M109 / Giatsint parked on a contested derrick, or posted as a forward lane
picket, dying to the first rifle squad.

**Premise check.** Eligibility dates from `0c05dbcd` (07-22) and `d0992098` (07-25), before the fires-standoff
work; the code comment says *"design §6"* but the fires work since then assumes artillery stays rear. No queue
item. **Hypothesis:** frequency — depends on how often artillery is the nearest eligible unit.

**Cheapest verification — static.** Remove `IndirectFire` from both predicates; extend the existing role-
eligibility NUnit (if none, add one per module asserting an `IndirectFire` actor is rejected). Optional
autotest: a bot with one M109 and one rifle squad equidistant from a garrison POI; **pass** = the rifle squad
is recruited, the M109 never enters the POI cell over 600 ticks.

**Dispatch-ready brief.**
- *Files:* the two predicates above; tests.
- *Approach:* drop `IndirectFire` from both role sets (or add an Info flag `AdmitIndirectFire`, C# default
  `true` to keep the pre-change path, set `false` on both twins).
- *Acceptance:* NUnit; `make check`.
- *Risks:* moves `@stable` (both modules have `@stable` twins, `ai.yaml:3359`, `:3385`) — an improvement,
  disclose. Garrison may now find fewer recruits and hold POIs thinner; that is the honest trade.

---

## 7. Scout and transport helicopters never re-check danger after take-off

**Today.** Recon-heli targets are danger-checked **once** at dispatch (`HelicopterSquadBotModule.cs:1022-1030`),
then a bare `Move` is issued (`:966`) and the scout leaves the managed pool (`:969`). Transports likewise get a
bare `Move` to the drop zone (`:1333`) with no in-flight abort. Unscouted ground reads air danger 0, so the
dispatch-time gate passes on exactly the cells where the AA is.

What a watching player sees: a Little Bird flies straight at an enemy point and keeps flying into the AA after
the first missile is already in the air. 0902 §1.3: littlebird 2/2 lost, halo 1/1 lost — **hypothesis** that
this is the mechanism; no kill attribution exists in any artifact.

**Premise check.** Gate introduced in `907640d1`, dispatch-only; no later in-flight check (`git log -S
ReconSafetyMath.Acceptable`, agent read). Attack helis already have an in-flight withdraw
(`AirDangerSpikeUnits: 25`, `ai.yaml:2898`; `HelicopterStates.cs:219-226`) — this proposal extends the same
predicate to the two roles that lack it. Not item 44 (AA damage arithmetic) or 81.

**Cheapest verification — one autotest.** `test-bot-scout-heli-aborts-into-aa`: bot littlebird dispatched to a
recon cell; a hidden enemy AA unit (not yet believed) sits on the path at 60% of the leg. **Pass:** the heli is
alive at t+600 and its max approach to the AA is ≥ AA range − 2 cells after first detection. **Fail text:**
`"scout heli died / closed to N cells after AA contact"`. RED first.

**Dispatch-ready brief.**
- *Files:* `HelicopterSquadBotModule.cs` (scout dispatch ~`:940-970`, transport ~`:1320-1340`); reuse the
  attack-squad spike predicate rather than writing a second one (memory: *never duplicate subtle logic*).
- *Approach:* keep a small "in flight" list for dispatched scouts/transports; each tick-batch, if
  `AirDanger` at the heli's cell exceeds the spike threshold, issue a `Move` back toward own SR (scout) or abort
  the drop (transport — note the three-leg chain at `:1328-1333` is all-or-nothing; cancel the whole chain).
- *Acceptance:* scenario RED→GREEN; `BotOrderedMutationTest` stays green (all mutations via orders — the
  saved-game leak class, HOTBOARD).
- *Risks:* the air channel is item 40's scale problem — the spike threshold may be mis-scaled (`influence-
  stack.md:61`: never re-scale air via the ground reference). Moves `@stable` if done in shared code.

---

## 8. The counter-battery radar is bought, deployed, and read by nothing

**Today.** `CounterBatteryRadarBotModule` parks and deploys an MSAR; its own header says the payoff is that
a firing enemy gun becomes a belief contact that *"feeds ContinuousBombardment … This module delivers the
coverage and deliberately builds no consumer"* (`CounterBatteryRadarBotModule.cs:14-17`). But continuous
bombardment admits **only static contacts** (`PoiOffensiveBotModule.cs:4612-4613`), and `IsStatic` is false for
anything with `Mobile` (`World/BeliefStore.cs:273-278`) — i.e. every artillery piece. The radar's whole output is
filtered out at the one consumer it names.

What a watching player sees: the bot spends money on a radar truck, drives it out, deploys it — and enemy
artillery keeps firing unanswered.

**Premise check.** `55453fec` (09-02), `c6634d24` (08-03); no queue item. Module is `@experimental`-only
(`ai.yaml:1160-1161`; `@stable` twin `:3516` exists — check its config before asserting byte-identity).

**Cheapest verification — NUnit + one autotest.** Pure predicate `BombardEligible(contact)` = static OR
(IndirectFire role AND seen within N ticks); NUnit both branches. Autotest: enemy M109 firing from fog inside
bot radar cover, bot has one artillery piece in range. **Pass:** bot artillery fires on the M109's cell within
400 ticks of the first enemy shot.

**Dispatch-ready brief.**
- *Files:* `PoiOffensiveBotModule.cs:4600-4730` (bombard target set); BeliefStore query (contact age is
  already tracked — check before adding).
- *Approach:* admit recently-refreshed mobile contacts whose type resolves to `IndirectFire`, at reduced
  confidence. Fog-legal by construction (belief store only). Zero RNG.
- *Risks:* shooting at a gun that has since moved wastes ammo — the recency window is the knob. Do not touch
  `IsStatic` itself (decay semantics depend on it).

---

## 9. The scout picker overflows: unexplored map edges rank last

**Today.** `ScoutBotModule.FindScoutTarget` scores `age + edgeBonus` (`ScoutBotModule.cs:222`, bonus 500 at
`:220`). `ThreatMapManager.GetExplorationAge` returns **`int.MaxValue`** for a never-visited square (`:314-323`).
`int.MaxValue + 500` wraps negative (no `CheckForOverflowUnderflow` in any engine csproj or
`Directory.Build.props`), so every **unexplored edge square scores below everything** and the comment's
"bonus for likely enemy approach routes" is inverted. Every unexplored interior square ties at `int.MaxValue`,
and the strict `>` picks the first in `gx, gy` order — scouts sweep the map column by column from x=0. The
heli recon path met the identical problem and fixed it with a clamp, `ScoutReconMath.MaxTrackedAge`
(`HelicopterSquadBotModule.cs:2015-2040`); the ground scout never got it. Separately the scout uses a bare
`Move` with no danger read (`:150`) — a humvee/BTR scouts straight into the enemy beachhead.

What a watching player sees: the bot's scout humvee driving a mechanical stripe pattern from one side of the
map, never checking the flanks first, then dying at the enemy SR.

**Premise check.** Last touched `22bb6e29` (bounds clamp); overflow never recorded. Moderate payoff: scouting
feeds `AdaptiveProduction`'s counter-buy tallies (`AdaptiveProductionBotModule.cs:238-240`; `bugs/discovered.md:2818`).

**Cheapest verification — static/NUnit.** Route the score through `ScoutReconMath.Score` (already pure,
pinned by `ScoutReconMathTest.cs`). NUnit: unvisited edge square must outrank unvisited interior square.
RED first with a transliteration of the current expression.

**Dispatch-ready brief.**
- *Files:* `ScoutBotModule.cs:195-232`; `ScoutReconMathTest.cs`.
- *Approach:* reuse `ScoutReconMath.Score(age, isEdge, false, dist)` — one implementation, two callers. Do not
  add danger-aware scouting in the same commit.
- *Risks:* `ScoutBotModule` is `enable-ai-any` (`ai.yaml:1591-1606`) → **moves `@stable`**; disclose. Note
  `FindRandomFarCell` draws `SharedRandom` (`:258-259`) — untouched by this fix, but do not add draws.

---

## 10. A bot engineer follows a moving tank across the map

**Today.** `EngineerOperatorBotModule.FindRepairTarget` picks the **nearest** damaged friendly within
`RepairMaxDistanceCells`, with no test for whether the target is stationary or whether the engineer is gaining
on it (`EngineerOperatorBotModule.cs:496-529`). Recorded `bugs/discovered.md:8` (2026-09-22): an engineer trails
a damaged tank 12–20 cells behind while a parked casualty near the SR waits. (The separate "parks ON the
casualty" bug is fixed, `5f8fed03`.)

**Cheapest verification — one autotest.** Two damaged friendlies: tank A moving away along a road, tank B
parked 8 cells from the engineer. **Pass:** B's HP rises within 300 ticks.

**Dispatch-ready brief.** Prefer targets whose `Mobile` is idle / not moving, or abandon a target whose distance
has not shrunk across two `OrderSettleTicks` windows (`:74`); deterministic tie-break unchanged. Module is
`@experimental`-only (`ai.yaml:1433`; check the `@stable` block at `:3559` before claiming byte-identity).

---

## 11. The bot never buys or fires the conventional strike powers a human can

**Today.** Kinzhal / GBU-57 are purchasable support powers gated by `powers.russia` / `powers.america`
(`player.yaml:138-163`, `:275-284`). No bot lists the support-power queue (`UnitQueues` are Vehicle/Infantry/
Aircraft only, e.g. `ai-russia.yaml:20`) and `SupportPowerBotModule` is instantiated nowhere
(`ai.yaml:3032-3035`). Only `NuclearBotModule` fires powers, and only nuclear ones.

**Hypothesis to confirm first:** that these strikes are actually buyable in the default lobby mode (DEFCON
Skirmish vs Escalation gating, `HOTBOARD`/item 89) — if they are Escalation-only, this shrinks to the
Escalation case. **This is a gap, not stupidity**, ranked last for that reason; scope it as its own item
(purchase decision = budget allocation, target = highest-value believed static cluster, reusing
`NuclearBotModule`'s target picker rather than a second one).

---

## Considered and rejected

- **Logistics Centres never captured** (`bugs/discovered.md:2404`): real, but the delisting from
  `PoiMap.IncomeWeights` (`world.yaml:362-367`) was a deliberate 2026-07-19 fix (`2d5433a`, "a $0 building
  outbids real income POIs"). Re-adding needs a value model for a non-income structure — a design call, not a
  cheap fix.
- **`Mobile.MoveResult` is never assigned** (`bugs/discovered.md:2688`): the root of several "spins forever"
  shapes, but an engine-wide change with per-caller workarounds already in place; not cheap or incremental.
- **Combat-engineer `e6` never bought** (`bugs/discovered.md:2327`): true, but a composition-weight question
  for the composition owner, low visibility.
- **Escort claim dropped by stance code** (`PoiGoalGuard.cs:100`, `bugs/discovered.md:2852`): reachability
  never measured; leave for a recon pass.

## What this pass did not verify

- **No match was watched or run.** Every "what a player sees" line is inferred from code. Frequencies for #3,
  #6, #7 are hypotheses; #7's link to the 0902 aircraft losses rests on n=3 airframes.
- Several citations (#3's `StageFreePool` lines, #6's `:514`, #7's `:1022-1030`, #8's `:4600-4730`) come from
  sub-agent reads that I spot-checked at the decision lines only, not the surrounding ranges.
- I did not check whether `@stable`'s `CounterBatteryRadar` / `EngineerOperator` twins (`ai.yaml:3516`,
  `:3559`) are configured identically to `@experimental`, so the "Moves `@stable`?" column for #8/#10 is
  provisional.
- #11's availability in Skirmish was not checked.
- Line numbers in `ai.yaml` drift weekly (HOTBOARD repeatedly notes it); grep the key, not the line.
