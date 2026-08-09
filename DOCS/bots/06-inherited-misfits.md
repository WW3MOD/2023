# 06 — Inherited misfits: the consolidated, ranked audit

**Researched against `main` @ `dcc2f7c5`** (`git status -sb`: `main...origin/main [ahead 71]`; tree clean apart
from four known untracked scratch paths). Static read only — no build, no game run, no autotest.

> **Reconciled 2026-08-09 against `main @ 25a8aebd`.** A cross-document pass re-derived every headline
> claim, summary count and computed figure in this six-document set from the code, and corrected the
> loser of every contradiction in place. Corrections made here are marked at the point they occur.
> **Danger-field magnitudes are the one excluded class** — they are pending re-derivation on
> `auto/danger-scale` and are flagged wherever they appear; see
> [`04` §3.2](04-perception-and-fields.md).

**What this document is.** The four preceding documents each mapped one region of the bot and each found more
than expected. This one consolidates: **one ranked list of every misfit across all four plus the bug log**,
**the failure patterns behind them with the tell for each**, an argued **fix-first** order, and — because this
is not a hit piece — **what is genuinely good and why**.

**Who it is for.** You own the mod and you asked for this so you can *find the next one yourself*. §2 is the
part that does that. §1 is the part that tells you what to do this week.

**The framing everything is measured against.** WW3MOD is a **total conversion**
([`game-model.md`](../reference/game-model.md), [`supply-route.md`](../reference/supply-route.md)). There are
no factories and no tech tree. Units are called in as reinforcements from off-map reserves through the
**Supply Route** — a fixed, indestructible, non-buildable beachhead, one per player, more only by capture.
Costs are budget allocation, not manufacturing. **Every inherited assumption of production, base expansion,
base radius, rearm buildings or repair is by definition a misfit**, and two structural consequences follow that
sharpen almost everything below:

1. **Every unit in the game is born at the same few cells.** Every module that recruits "nearby idle units" is
   recruiting from one puddle, at one moment.
2. **Health and ammunition are one-way resources.** Nothing repairs and nothing rearms (§1 rank 2). Any
   inherited gate of the form "wait until you are healthy/full again" is unsatisfiable, not merely pessimistic.

---

## How to read this document

| Marker | Meaning |
|---|---|
| **[OpenRA]** | Inherited from `release-20230225` essentially unchanged. |
| **[MODIFIED]** | OpenRA structure, WW3MOD changed behaviour or added fields. |
| **[WW3MOD]** | Written for this mod. No OpenRA ancestor. |

Unmarked statements are **facts** with a `file:line`. Anything beginning **`ASSESSMENT:`** is my opinion and
you should argue with it freely.

**On verification.** The sources are four static-analysis passes, each of which flagged its own soft spots. I
re-opened the code for every claim that is load-bearing for the ranking, and **one of them did not survive**
(§5.1) — the single most-quoted number in the set is wrong, in a way that makes the underlying problem worse
rather than better. Claims I carried across without re-deriving are listed in §5.3. I have not laundered any
inference into a fact; where a source's caveat applied, it is carried.

---

## 1. The ranked misfit table

Ranked by **impact on your actual goals** — a bot that plays like a modern battlefield, and your ability to
find the next problem without a worker — not by how interesting the defect is.

**Cost key:** `XS` under an hour · `S` an afternoon · `M` a day or two · `L` a week+ or a design decision
first. **Status key:** `LIVE` affects shipped matches now · `LATENT` real but currently masked · `INERT` cannot
execute, costs attention rather than behaviour.

> ⚠️ **Standing warning on every danger-field magnitude in this document** — rank 1, rank 22, P1, P2, §3.1 and
> §5.1. All of them (`~6.8 × 10⁹`, `2,950`, `29.5×`, `751×`, `~22,000×`, `~900×`, `~130×`, the clamp to `1`)
> are **pending re-derivation** on `auto/danger-scale`, which is fixing `WeaponThroughput`'s arithmetic and
> **owns** the settled numbers. The *ranking* findings — that the field's ordering does not follow lethality,
> and that the heavy rows do not compute as published — survive; the *factors* do not. Do not quote a figure
> from this document to justify a threshold. See the warning at the head of
> [`04` §3.2](04-perception-and-fields.md).

| # | Misfit | Where | Pattern | Status | Already in flight? | Cost |
|---|---|---|---|---|---|---|
| **1** | **Danger-field core arithmetic is wrong twice.** `throughput × durabilityWeight` overflows `int` for any high-throughput, high-HP contact, wraps negative and is clamped to the floor of **1** — so a believed MBT paints one cell at value 1. Independently, `WeaponThroughput` divides by `ReloadDelay` and never reads `BurstWait`, which this mod made mandatory — so relative ranking follows YAML style, not lethality. | `DangerFieldLayer.cs:170` (overflow), `:521-533` (cadence field) | P2, P1 | **LIVE** | **Overflow + derived unit: YES**, `auto/danger-scale`. **Cadence field: NO** — untouched, see §3.1 | S (done) / M (rest) |
| **2** | **Both aircraft rearm hosts are disabled, so `ReturnToBase` is a no-op and every "wait until healthy/full" gate is unsatisfiable.** A helicopter that takes one chip of damage is benched for the match on `@stable`. Each consequence was worked around with its own bypass flag rather than fixed. | `structures.yaml:432` (HPAD), `:500` (AFLD) both `~disabled`; `aircraft-america.yaml:219,376,498` still name them; `ReturnToBase.cs:106-108`; gates at `HelicopterStates.cs:374-375`, `HelicopterSquadBotModule.cs:1387-1394`; `ReEngageHealthPercent: 90` on both transports (`aircraft-america.yaml:9`, `aircraft-russia.yaml:9`) | **P8** | **LIVE** | no | M |
| **3** | **24 module cadences are per-call `--countdown` decrements, and several duration counters are denominated in module updates rather than world ticks.** Withholding a module tick stretches its interval by the withhold factor and silently drops its units out of the commitment ledger while the module still lists them. This is the structural blocker on the human-attention scheduler. | `ModularBot.cs:215-224` (the in-code warning); pattern e.g. `BotBlackboard.cs:100-108`; POI `ReevaluateInterval 100` vs `AxisCommitmentTicks 250` | **P9**, P6 | **LIVE** (as a blocker) | no | L |
| **4** | **Both transports are supply-driven in a game whose transport problem is demand-shaped, and the pickup bubble makes lift a one-shot.** A soldier more than 14 cells from its own SR can never be picked up again for the rest of the match, by either ferry. Neither ferry ever asks where a unit needs to go. | `MountedTransportBotModule.cs:58`, `ai.yaml:1124`, `:1154` (`ReserveZoneRadiusCells: 14`); same bubble on the heli lift | P1 | **LIVE** | no | L (missing layer, not a wiring job) |
| **5** | **Live air and helicopter states pick their targets omnisciently, then route to them fog-legally.** The bot flies at units it has never seen — the most player-perceptible form of cheating — and declines attacks because of AA it cannot legally know about. | `AirStates.cs:61-111`; `HelicopterStates.cs:323-336`, `:338-351`, `:403-427`, `:512-517`; `SquadManagerBotModule.cs:228-232` falls back to the omniscient set | P7 | **LIVE** | no | M |
| **6** | **A large, tunable-looking configuration surface that cannot move anything.** `BaseBuilderBotModule@normal`'s entire construction half: all 8 `BuildingFractions` targets carry `Prerequisites: ~disabled` and **nothing in the repo provides `disabled`** (verified). Plus four `PoiOffensive@experimental` levers set `false` over ~60 lines of shipped config, four inert `SquadManager` tunables, five unread `AIHelicopterRole` fields, one `TransportMissionSlots: 0` lane. | `ai.yaml:1195-1210` + `structures.yaml:432,500` / `structures-defenses.yaml:91,187,272,692,777,819`; `ai.yaml:353,519,531,595`; `ai.yaml:1244,1241,1242,1243` (+twins); `AIHelicopterRole.cs:25,37,40,43,46`; `HelicopterSquadBotModule.cs:120` | **P4** | **INERT** | no | S (delete) |
| **7** | **The benchmark control and the experiment run their modules in a different order, and nobody chose it.** `@experimental` blocks sit at `ai.yaml:111-1146`, the `@stable` twins at `:1587-1816`, with shared modules interleaved — so `PoiOffensive` ticks *before* Garrison and MountedTransport on one profile and *after* on the other. Before the order gate this resolved the same unit contest to different winners. Declaration order **is** the arbitration priority list. | `ai.yaml` block layout; `ModularBot.cs:112`, `:225`; `TraitDictionary.cs:150-155`; `ActorInfo.cs:104-142` | **P11** | **LIVE** | partly — the order gate (`ai.yaml:47-48`, `:52-53`, both profiles) damps but does not remove it | S |
| **8** | **`@stable`'s entire reactive counter-buy system is downstream of two humvees.** `AdaptiveProductionBotModule` early-returns below `MinEnemySightings` on blackboard counters that are **overwritten not accumulated**, **never decay**, and are **not fog-legal** — and only then runs its own correct fog-legal whole-map census. The accurate sensor cannot open the gate that guards it. | `AdaptiveProductionBotModule.cs:227-231`, `:255-256`, `:259`; `BotBlackboard.cs:246`; `ScoutBotModule.cs:237-291`, `:244`, `:257-258` | **P7** | **LIVE** | no — filed `discovered.md` 2026-08-09 `[med]` | S |
| **9** | **`GoalGuardLedger.Release` is keyed on the actor, not the objective.** A per-unit ambient `tacpos:` trait releasing "its own" claim deletes whichever claim the actor holds — including a `capture-escort:`. The order gate's rank ladder cannot help: rank is consulted at the order funnel, never at `Release`. | `PoiGoalGuard.cs:100`; writer `StancePositioningExecutor.cs:643` releasing from `:229,261,272,300,320` | P6 | **LIVE** | no — filed `discovered.md` 2026-08-09 `[med]` | S (but moves both profiles) |
| **10** | **Two generations of code share every player and neither reads the other.** 2026-03 support modules claim units in `BotBlackboard` and reason about space with the **omniscient, float-based** `ThreatMapManager`; the 2026-07 POI stack claims units in `PoiGoalGuard` and reasons with the fog-legal influence stack. `HelicopterSquadBotModule` writes the blackboard but never reads it. The older layer is where the shared `enable-ai-any` instances live — the modules with the weakest intel model are the ones with no twin protecting the benchmark. | `BotBlackboard.cs:196-218` vs `PoiGoalGuard.cs:39-117`; `world.yaml:283-286`; 0807 census §4.1 | **P7** | **LIVE** | no | L |
| **11** | **A second decision layer moves units without producing an order.** Five activity-layer traits queue `Activity` objects directly; two use the *cancelling* two-argument form and genuinely destroy in-flight work; **two are default-ON for human-owned units**. Invisible to the order gate, the lifecycle log, and any future attention scheduler placed at the order layer. | `ModularBot.cs:129-136`; `StancePositioningExecutor` + `AutoSeekSupplies` human grants at `rules/defaults.yaml` (`^Combatant`, `GrantConditionOnHumanOwner@tacpos`) and `infantry.yaml:221-222`; cancelling form `Actor.cs:381-387` | P7 | **LIVE** | no | L |
| **12** | **13 of 20 squad states, ~1,120 lines, cannot execute on either profile** — all five ground states, all four naval, all three protection, plus `HelicopterAttackRunState` (which is rank 14, counted once here), plus the 275-line fuzzy attack-or-flee evaluator. Ground combat is `PoiOffensiveBotModule`'s. Verified: `IgnoreGroundUnits: true` on all four instances, `NavalUnitsTypes`/`ProtectionTypes`/`ConstructionYardTypes` set on none. | `ai.yaml:1250,1341,1800,1813`; `SquadManagerBotModule.cs:328-336`; reachability table in [`05`](05-squads-and-combat-states.md) §2.7 | **P5** | **INERT** | no | S (delete/quarantine) |
| **13** | **`GarrisonBotModule@defenses` garrisons civilian houses on both profiles, for 45 s at a time, competing for the same idle infantry as four other modules** — named after defences that cannot be built, with `GarrisonActorTypes` unset so eligibility falls through to "anything with `PassengerInfo`". | `GarrisonBotModule.cs:18-23`, `:483-492`; `ai.yaml:760-775`, `MinGarrisonDwellTicks: 750` | P4, P1 | **LIVE** | no (the frozen-truck claim leak inside it is FIXED, `discovered.md` entry 22) | M (design question first) |
| **14** | **`HitAndRunCooldown` — the heli trait's signature doctrine knob — is unreachable on both shipped profiles, and counts squad updates rather than ticks (5× its stated duration).** Its consuming state is entered only under `if (!standoff)`, and `StandoffEngagement: true` ships on both. Configured on four airframes regardless. | `HelicopterStates.cs:565`, `:571`, `:685`, `:709`; `ai.yaml:1419`, `:1446`; `AIHelicopterRole.cs:33-34` | **P5**, P9 | **INERT** | no — filed `discovered.md` 2026-08-09 `[med]` | S |
| **15** | **Inherited retreat doctrine is "flee home to the beachhead" and "never retreat while near a building".** `RandomBuildingLocation` is on the live path for both air flee and heli withdraw fallback; `StateBase.ShouldFlee` cancels the entire flee decision if any own building is inside `DangerScanRadius` — which, in a mod whose buildings are mostly the SR, encodes *never retreat near home*. | `StateBase.cs:29-38`, `:83-104`, `:93-95`; `AirStates.cs:224`; `HelicopterStates.cs:822` | P1 | **LIVE** | no | M |
| **16** | **`EngineerRouteOpenBotModule` is enabled, fully implemented, and has no target on any shipped map.** It seeks `bridgehut` actors; **zero instances across all ten maps** (re-verified by grep over `mods/ww3mod/maps/`). | `ai.yaml:1084-1086`; `CrossingMap.cs:710-717`; `civilian.yaml:848,859` | **P5** | **INERT** | no | XS (say so in the config) |
| **17** | **`BotBlackboard`'s entire task-board API has zero callers.** `PostTask` / `ClaimTask` / `GetOpenTasks` / `UpdateTaskStatus` / `HasTaskNear` — **re-verified: no reference anywhere in `engine/` outside the file itself.** A half-built second coordination system sitting next to a live one, inviting someone to build on it. | `BotBlackboard.cs:137,145,160,170,184` | **P5** | **INERT** | no | XS |
| **18** | **An [OpenRA] smoothing constant silently shapes arbitration outcomes.** `MinOrderQuotientPerTick = 5` spreads a burst of 40 orders over 13 drain passes (~12 ticks, re-derived at `25a8aebd`). In OpenRA those were production and rally orders; here they are recruitment sweeps over the contested SR reserve, at the exact moment two modules are competing for it. | `ModularBot.cs:34`, `:253` | P1 | **LIVE** | indirectly — the gate perturbs the drain schedule (`ModularBot.cs:247-252`) | S (but re-benchmark) |
| **19** | **The order gate's objective-prefix → module rank table is hand-maintained and not re-read from the modules that emit those prefixes.** It fails open, so drift costs damping and not correctness — but drift is **silent** and nothing would tell you it had happened. | `OrderArbitrationMath.cs:206-226`, acknowledged at `:199-205` | P10 | **LATENT** | no | XS (a `make test` lint) |
| **20** | **`SupportPowerBotModule` is not instantiated while `MSLO` ships.** If a support power ever becomes reachable, nothing on the bot side will use it and the gap will not announce itself. | `structures-defenses.yaml:1077`; no trait declaration anywhere in `mods/` | P5 | **LATENT** | no | M |
| **21** | **Comments and curated docs assert facts owned by other files, and go stale when those files move.** `CaptureCoordinator`'s header still describes a legacy module gated to a condition granted to nobody; `LayeredDefence`'s header still says `SquadManagerBotModule` handles opening play; three `ai.yaml` comments claim byte-identity for gates `@stable` now sets; `influence-stack.md` carried sixteen stale code anchors — **fixed 2026-08-09**, values all still correct. And `HelicopterSquadBotModule.cs:403-406` claims the commitment ledger is "Resolved ONLY when CommitTransportPassengers is on" while `:496` resolves it unconditionally (filed `discovered.md` 2026-08-09 `[low, doc-in-code]`). | `CaptureCoordinatorBotModule.cs:18-19`; `LayeredDefenceBotModule.cs:28-29`; `discovered.md` 2026-08-04; `discovered.md` 2026-08-09 `[low, doc-in-doc]` | **P10** | **LIVE** (as misinformation) | no | S |
| **22** | **`ai.yaml:840-841` asserts the danger field "steps by tens-to-hundreds per cell", and `DOCS/bots/04` publishes a magnitude table that is unreachable for heavy contacts.** Both are the tree's most-quoted statements of this field's scale, and both are wrong — in *opposite* directions (§5.1). | `ai.yaml:840-841`; [`04`](04-perception-and-fields.md) §3.2 | P10 | **LIVE** (as misinformation) | partly — `auto/danger-scale` adds a `[danger] dist` log and a derived unit | XS (filed, §6) |

**Composition of the list.** Re-tallied from the Status column at `25a8aebd`: of 22 rows, **15 are LIVE**
(1, 2, 3, 4, 5, 7, 8, 9, 10, 11, 13, 15, 18, 21, 22), **5 are INERT-but-attention-costing** (6, 12, 14, 16,
17), and **2 are LATENT** (19, 20). *(An earlier draft summarised this as "13 / 6 / 3", which does not match
the table it summarises.)* By provenance the split is not what "we inherited bad bots" would predict: rows 1,
4, 8, 9, 10, 11, 13, **17** are **[WW3MOD]** originals, rows 2, 5, 6, 12, 15, 18 are inherited or
inherited-shaped. *(Row 17, `BotBlackboard`'s task board, was listed as inherited in an earlier draft; the file
was added `2026-03-21` and has no OpenRA ancestor — [`02` §6.2](02-lifecycle-and-arbitration.md) marks it
**[WW3MOD]** and is right.)* **The inherited pieces are mostly fine in isolation and misfit at the seams; the
mod's own pieces misfit at scale.**

---

## 2. The failure patterns, and the tell for each

This is the section worth keeping. Each pattern has a **tell** — the thing to grep for, or the question to
ask — so you can find the next instance without a worker.

The brief named eight. I have kept them, moved one instance between them, and added three (**P9**, **P10**,
**P11**) that the four documents demonstrate but did not name.

> **Relationship to [`README` §5](README.md), settled by the 2026-08-09 reconciliation pass.** The two sections
> overlap deliberately and both survive: §5 there is the *worked-example* teaching version (read once), this is
> the *field guide* (keep open). **This numbering is canonical** and `README` §5's headings now carry it —
> note the mapping is not the identity (its Pattern 4 is **P6** here, its Pattern 5 is **P4** here). Four
> patterns here — **P5**, **P7**, **P8**, **P11** — have no worked example there because that document covers
> them in narrative rather than as patterns; **P10** appears there as an operational habit in its §5.8. In the
> other direction, `README` §5's Pattern 7 (*an availability check written as "has the data arrived yet?" is
> false over exactly the warm-up window you care about*, from [`04` §8.3](04-perception-and-fields.md)) has
> **no P-number here**. It is a real pattern; it is simply not in this list.

---

### P1 — Constants that were never rescaled when the game was

An absolute number chosen against Red Alert's magnitudes, still being compared against a field the total
conversion moved by two to five orders of magnitude. Nothing is wrong in either file; the two were simply never
introduced to each other.

**Instances.** `EvacDangerThreshold: 60` against a field whose measured median at evac entry is **66,834**
(`ai.yaml:830`, `SupplyFollowerBotModule.cs:91`, measured in
[`260809-truck-loop-from-live-log.md`](../../WORKSPACE/recon/260809-truck-loop-from-live-log.md) §1) · the
durability weight documented as "~1.0×" at `DangerFieldLayer.cs:167-168` delivering **29.5×** on a 28,000 HP
tank and **751×** on a 75,000 HP structure · `EvacReleaseHysteresis: 15`, a Schmitt-trigger band narrower than
one per-cell step of the field it damps · **13 of the 26 configured perception-field thresholds**
([`04`](04-perception-and-fields.md) §5 — re-tallied; the earlier "14 of 26" did not match that table) · the transport's 14-cell pickup bubble, a base-radius number in a game
with no base radius · `MinOrderQuotientPerTick = 5`.

**The tell.** *A mid-range integer compared against a value derived from the ruleset.* Every surviving
threshold on the danger field is a `0` or a `1` — used as a boolean — and every one that fails is a mid-range
number someone chose to mean "a moderate amount". **The field cannot express "a moderate amount."** Before
writing any such comparison, compute the per-cell step in the regime you care about; if your band is narrower
than one step, use a *time* bound instead. Every anti-oscillation fix in this tree that actually worked
(`MinGarrisonDwellTicks: 750`, `EvacDwellScans`, `RetreatDamperMath`) is temporal for exactly this reason.

---

### P2 — Formulas still reading the old game's fields

Worse than P1, because the failure is not a scale offset you can retune away — it is a **ranking inversion**.
Two things sorted by the formula come out in the wrong order.

**Instances.** `DangerFieldLayer.WeaponThroughput` divides by `ReloadDelay` and never reads `BurstWait`
(`:521-533`). WW3MOD made `BurstWait` mandatory — `Armament.cs:128-129` throws for a weapon that omits it —
and demoted `ReloadDelay` to the post-magazine pause applied only when non-zero. **Re-counted at `25a8aebd`:
14 `ReloadDelay` declarations against 87 *live* `BurstWait` declarations** across `mods/ww3mod/rules/weapons/`,
so most weapons take the `≤ 0 → 1` substitution and are modelled as firing their entire burst damage every
tick. *(This number has been quoted three ways. `BurstWait:` occurs as a key 90 times, 3 of them commented
out ⇒ 87 live; 92 counts lines that merely mention it. The earlier "92" here and the "90" in
[`04` §3.2](04-perception-and-fields.md) were both counting artefacts.)*

The second instance is the same expression's arithmetic: `intensity = throughput * durabilityWeight /
DurabilityBase * confidencePercent / 100` (`DangerFieldLayer.cs:170`) evaluates the first multiply in `int`.
For a main battle tank that product is ~6.8 × 10⁹, which wraps negative, falls through the `intensity < 1`
guard at `:171-172`, and is clamped to **1**. See §5.1 — this is the claim that changed under verification.

**The tell.** *A formula that reads a ruleset field, in a mod that changed what that field means.* The specific
grep is cheap: for any engine expression consuming a YAML key, check whether the mod still populates that key.
`ReloadDelay` at 14 declarations against 92 for its replacement is the signature — **a field that has become
rare in the data is a field the code should probably no longer be reading.** A second tell: any `int`
expression multiplying two ruleset-derived quantities, in a mod whose ruleset values run to 10⁵.

---

### P3 — Tests written at toy magnitudes

A feature ships with tests, the tests pass for the feature's entire life, and the defect they were supposed to
catch lives underneath them the whole time — because the fixtures used values four to five orders of magnitude
below the real ones.

**Instance.** The `int` overflow above survived the danger field's whole life. It was found only when a worker
wrote a regression test **at real WW3MOD magnitudes** (`auto/danger-scale`, `DangerFieldKernelTest.cs`); the
pre-existing kernel coverage never multiplied numbers large enough to wrap.

**The tell.** *Open the fixtures and compare their magnitudes to the ruleset's.* If a test for damage,
health, cost, throughput or danger uses values like `100` and `50` while the mod ships `23000` and `28000`,
that test is pinning a shape, not a behaviour. This one is trivially auditable and I would spend an hour on it
across `OpenRA.Test/` before anything else in this list, because it tells you which *other* pins are decorative.

---

### P4 — Configuration wired to nothing

A block of YAML that reads exactly like a tuning surface and cannot move anything. The cost is not CPU. The
cost is **your attention**, and — because a knob that looks live invites tuning — it is precisely the mechanism
that manufactures P1 bugs.

**Instances.** `BaseBuilderBotModule@normal`'s construction half: eight `BuildingFractions` entries whose every
target carries `Prerequisites: ~disabled`, with `NewProductionCashThreshold: 5000`, `MinBaseRadius`,
`PlaceDefenseTowardsEnemyChance: 80` and the rest alongside them (`ai.yaml:1195-1210`; targets at
`structures.yaml:432,500` and `structures-defenses.yaml:91,187,272,692,777,819`; **nothing in `mods/` provides
`disabled`** — re-verified) · four `PoiOffensive@experimental` levers set `false` with ~60 lines of tested
config below them (`ai.yaml:353,519,531,595`) · four `SquadManagerBotModule` tunables read only by unreachable
code (`AttackScanRadius`, `SquadSize`, `SquadSizeRandomBonus`, `RushInterval`) · **five `AIHelicopterRoleInfo`
fields with zero readers anywhere in `engine/`** — re-verified by grep — configured per airframe under names
that promise exactly what a tuner would reach for ("how close does the Apache engage", "does the Hind avoid
AA") · `TransportMissionSlots` defaulting to 0, which makes `@stable`'s entire lift lane unreachable
(`HelicopterSquadBotModule.cs:120`, set only at `ai.yaml:1545`).

**The tell.** Two greps, both cheap and both worth automating. **(a)** For every `public readonly` Info field,
count references outside its own declaring file; zero means inert. **(b)** For every actor name appearing in a
bot config list, check its `Prerequisites` for `~disabled` and check whether anything provides the token. A
`make test` lint over (a) alone would have caught five of the instances above.

---

### P5 — Machinery that never executes

Whole subsystems that compile, read as live, and cannot run. Distinct from P4 in that it is *code* rather than
config, and the reason is usually one unset name list or one flag on the far side of the file.

**Instances.** 13 of 20 squad states — 987 lines of whole dead files plus ~130 lines of dead
`SquadManagerBotModule` members — including the 275-line `AttackOrFleeFuzzy`; dead because
`IgnoreGroundUnits: true` on all four managers and `NavalUnitsTypes`/`ProtectionTypes` set nowhere (all
re-verified) · seven module entries that appear in `ai.yaml` and do nothing ([`03`](03-module-catalogue.md)
§2.1) · five module classes never instantiated at all · `BotBlackboard`'s five-method task-board API with
**zero callers** (re-verified) · `HelicopterAttackRunState` and the whole hit-and-run mechanic, gated out by
`StandoffEngagement: true` on both profiles · `EngineerRouteOpenBotModule`, whose target actor has **zero
instances across all ten shipped maps** (re-verified) · `StateBase.ExcludeTacticallyCommitted`, a correct and
important guard whose three call sites are all unreachable.

**The tell.** *An empty name list is a silent off switch.* `Info.SomeTypes.Contains(x)` against a
never-populated `SomeTypes` is always false and reads as a working filter. Grep every `*Types`/`*Names` Info
field for whether the mod sets it. The second tell is **the boolean on the far side of the file**: a state
entered only under `if (!flag)` where `flag: true` ships 900 lines away in the YAML. Neither is visible from
the code you are reading, which is exactly why both survive review.

---

### P6 — Memory purged by the very event it guards against

The diagnosis the 0808 churn census named **eligibility-coupled amnesia**, and the most predictive idea in
this whole set. **28 distinct anti-churn dampers** already existed
([`260808-order-churn-census.md`](../../WORKSPACE/recon/260808-order-churn-census.md) appendix — count carried
from the census, not recounted here). 27 of the 28 are private to the module that wrote them, and each is
deliberately purged the moment the unit leaves that module's eligibility set — with a *correct* local reason.
But eligibility is computed from things that flicker: danger reads, POI visibility under fog, residue verdicts,
TTLs, and `IsIdle`. **The dedup memory is destroyed by the same event that triggers the re-issue.** You do not
need two modules fighting; one module with a flickering predicate produces the whole wiggle by itself.

**Instances.** The 27 private dampers · `GoalGuardLedger.Release` keyed on the actor rather than the objective
(`PoiGoalGuard.cs:100`) · `Commit` with a different objective silently destroying the incumbent (`:68-76`) ·
the ledger claim refresh living only inside each module's own periodic eval, which is what makes P9 dangerous.

**The tell.** *Ask what destroys the record, not what writes it.* For any dedup, cooldown, "last destination"
or "already handled" memory: is its lifetime owned by the **decider** or by the **subject**? If a module's own
roster rebuild can reach it, it will be erased at the worst possible moment. This is also why the order gate
works where a *eighth* per-module dedup would have failed — its standing records are player-owned and
module-unreachable (`OrderArbitrationMath.cs:368-372`). That lifetime property, not the dwell window, is the
gate's most valuable feature.

---

### P7 — Two generations of code sharing a player without talking

Not a design; a seam. Each generation is internally coherent and neither is aware of the other.

**Instances.** Two claim registries — `BotBlackboard` (4 modules, one of them write-only) and `PoiGoalGuard`
(11 modules, several read-only or write-only) — **neither reads the other**, though the honouring sets
overlap rather than being disjoint (`HelicopterSquadBotModule.cs:496` and `CaptureCoordinatorBotModule.cs:518`
resolve the ledger unconditionally on both profiles; the POI stack never touches the blackboard — see
[`03` §E2](03-module-catalogue.md), corrected 2026-08-09) · two
spatial models, the **omniscient float-based** `ThreatMapManager` and the fog-legal influence stack, with every
consumer of the former being a 2026-03-generation module · `AdaptiveProductionBotModule` owning a correct
fog-legal census and gating it behind a two-scout, never-decaying, non-fog-legal counter (rank 8) · live
squad states picking targets omnisciently and then routing to them fog-legally (rank 5) · the order layer and
the activity layer, where half the deciders produce no artefact at all (rank 11).

**The tell.** *Count the registries.* If a system has two answers to "who owns this unit" or "what is at this
cell", they will diverge, and the divergence will look like a behaviour bug rather than an architecture bug.
The sharper form of the question: **which generation do the `enable-ai-any` shared instances belong to?** Here
the answer is the older one — so the modules with the weakest intel model are also the ones with no `@stable`
twin protecting the benchmark.

---

### P8 — Disabled dependencies silently degrading a system

Something is turned off for good design reasons in one file; a system elsewhere depends on it, degrades to a
no-op, and each downstream consequence gets its own local bypass instead of the root being addressed.

**Instance, and it is the cleanest in the codebase.** `HPAD` and `AFLD` both carry `Prerequisites: ~disabled`
(`structures.yaml:432`, `:500`) — correct under the game model. The aircraft still declare them as rearm hosts
(`aircraft-america.yaml:219,376,498`; `aircraft-russia.yaml:224,392,530,625`). `Aircraft.ResolveOrder` accepts
`ReturnToBase` because `RearmActors.Count != 0`, the activity finds no resupplier, and falls to
`QueueChild(new FlyIdle(...)); return true` (`ReturnToBase.cs:106-108`) — **the aircraft idles where it
stands, never rearms, never repairs.** Three consequences, each patched separately: the ammo branches in
`AirStates` became dead code inside a live state; `SquadHasAmmo` reports "no ammo" at full ammo, routed around
by `SkipRearmReadyCheck` on both profiles; and the health gates became unsatisfiable, since with no repair
host **health only ever decreases** — `ReEngageHealthPercent: 90` on both transport helis (verified,
`aircraft-america.yaml:9`, `aircraft-russia.yaml:9`) means one chip of damage benches an airframe for the
match, reclaimable only by `@experimental`-only evacuation.

**The tell.** *Grep `~disabled` and then grep for who still names those actors.* More generally: when you
disable a thing, the systems that depended on it do not fail loudly — they *degrade*, and the degradation gets
absorbed as a series of unrelated-looking workarounds. **Three bypass flags around one predicate is the
signature.** If you find yourself adding a `SkipXCheck`, ask what made X unsatisfiable.

---

### P9 — [added] Counters denominated in the caller's cadence, not the world clock

A counter named `...Ticks`, documented in seconds, incremented once per call to a method that is itself on an
interval. Its real duration is the stated one multiplied by the caller's period, and nothing in the file
containing it reveals the multiplier.

**The brief grouped the ~270× comment under P1. I think it belongs here, and the separation matters** — P1 is
a magnitude mismatch you fix by rescaling a constant; P9 is a *unit* mismatch you fix by rescaling or by
changing what the counter counts, and it has a completely different tell.

**Instances.** `AIHelicopterRole.HitAndRunCooldown`, `[Desc]` "ticks of engagement", incremented once per
`Squad.Update()` at `SquadUpdateInterval = 5` — the Apache's `200` is 1000 world ticks ≈ **60 s**, not 12 s ·
`GroundUnitsRegroupState.MaxRegroupTicks = 750` with the comment "~12.5 seconds", incremented once per
`AttackForceInterval = 75` — **≈ 56 minutes**, off by ~270× · `PoiOffensiveBotModule`'s force-preservation
budgets counted in **evals**, not ticks · and the general case, **24 module cadences expressed as per-call
`--countdown` decrements**, so a module's "interval" is measured in *calls* and its wall-clock period stretches
by whatever factor it is withheld (`ModularBot.cs:215-224`).

That general case is why this pattern is rank 3 rather than a curiosity. A `--countdown` and a
`WorldTick % N` are identical **only while the module is called every tick** — which was true in OpenRA,
where nothing ever withheld a module tick, and is exactly what the human-attention scheduler this project wants
would stop being true. Worse than the stretched interval: **the only place a module's ledger claim is refreshed
is inside its own periodic eval**, so withholding a module past its TTL expires its claims while the module
still believes it owns the units. At the POI modules' 250/100 TTL-to-interval ratio, withhold more than 60% of
the time and units start leaking silently.

**The tell.** *For every `int somethingTicks`, find the `++` and ask what calls that method.* If the increment
is inside a method reached from a countdown, an interval, or a scan, the name is lying. Two sanity checks that
cost nothing: does the neighbouring constant's implied duration look deliberate at the *multiplied* value?
(`stuckTicks > 200` → ≈60 s and `withdrawTicks < 75` → ≈22 s both do, which is what identifies `HitAndRunCooldown`
as the outlier rather than the code.) And: **converting these 24 countdowns to tick stamps is mechanical and
boring, and it turns "you cannot schedule this bot" into "you can schedule this bot"** — which is why I rank it
above most behavioural work.

---

### P10 — [added] Comments and docs asserting facts owned by another file

A statement about what another module does, what another profile sets, or where another symbol lives. It is
correct when written and there is no mechanism that fails when it stops being correct. The definition it
describes can be **widened out from under every comment that depends on it**, all at once.

**Instances.** `InfluenceStack.Participates` was widened on 2026-08-02 to include `@stable` and every human;
three `ai.yaml` comments still claim "Default OFF on the @stable twin = byte-identical" for gates the `@stable`
block now sets (`discovered.md` 2026-08-04) · `CaptureCoordinatorBotModule.cs:18-19` still describes coexisting
with a legacy module gated to `enable-ai-legacy-only`, a condition granted to nobody, for a module not declared
at all · `LayeredDefenceBotModule.cs:28-29` still says `SquadManagerBotModule` handles opening play, which
stopped being true when `IgnoreGroundUnits` shipped · `influence-stack.md` carries six stale `ControlField.cs`
line anchors and one stale `GarrisonBotModule` anchor, values all still correct · `ai.yaml:840-841`'s
"tens-to-hundreds per cell" · the order gate's rank table, which is a hand-maintained mapping from objective
prefixes to module names **not re-read from the modules that emit them** (`OrderArbitrationMath.cs:206-226`) ·
and, newly, [`04`](04-perception-and-fields.md)'s own magnitude table (§5.1).

**The tell.** *When a comment asserts byte-identity, inheritance, or what another profile does, re-derive the
gate rather than trusting the sentence.* A quick discriminator that works: check the comment's commit date
against the date the referenced definition last changed — for this repo, `2026-08-02` is the watershed for
anything claiming `@stable` byte-identity. The structural fix is to make the assertion executable: the rank
table wants a `make test` lint reconciling it against the modules' own `*ObjectiveKey` helpers, which is cheap
and would close a whole class.

---

### P11 — [added] Ordering and adjacency as undeclared semantics

The position of a block in a file is load-bearing, and nothing at the site says so. This is the inverse of P4:
there, config that looks live is inert; here, file layout that looks cosmetic is decisive.

**Instances.** **Until 2026-08-08, when two modules wanted the same unit, the winner was whichever is declared
later in `ai.yaml`** — not priority, not urgency, nothing semantic — via `TraitsImplementing` order →
`TraitDictionary` construction order → `ActorInfo.TraitsInConstructOrder` → YAML declaration order, with FIFO
order drain making the later module's order land last. Documented nowhere in code until the order gate ·
consequently, **the two profiles run their modules in a different relative order** because `@experimental`
modules were appended next to their shared cousins while the `@stable` twins were appended as a block at the
bottom (rank 7) · `CohesionSlotMemory` **must** be declared before `StancePositioningExecutor` in
`rules/defaults.yaml`, and the reason is a comment · and the project's own standing rule that **blank lines
between MiniYaml top-level entries are significant** — adjacent entries silently merge.

**The tell.** *If moving a block would change behaviour, the file is a program.* Ask of any config file: is
there an ordering here, and is it stated? Where the answer is "yes, and no", the fix is usually not to remove
the ordering — it is to make it a stated rule, which is exactly what the order gate did for arbitration and
what mirroring the twin block order would do for rank 7. **Where two blocks are meant to be a controlled
comparison, declaration order is part of the configuration.**

---

## 3. What I would fix first, argued

**The constraint I am weighting hardest.** You have been shown several fixes that changed nothing you could
see. That is not bad luck — it is a predictable consequence of fixing things whose failure mode is *invisible
by construction*: an inert lever, a suppressed order in a log you cannot enable, a threshold whose effect is
"a slightly different set of cells". So I am ranking by **user-visible benefit per unit of risk**, and I am
explicitly demoting items whose payoff you would have to take on trust.

**Already in flight — do not double-count.** `auto/danger-scale` (one commit, `3a7a10a3`, under review) fixes
the `int` overflow at three sites, introduces a derived `ReferenceIntensity` unit with 13 renamed thresholds
across 7 modules, fixes the stuck frontier descent that the live log caught at cell `33,31`, and adds three
observability channels (`[danger] dist`, unconditional `[ordgate]`, `[supply] evac-leg`). It states plainly
that it moves `@stable` and that the benchmark baseline must be re-taken. **Rank 1's overflow half and the
whole of the threshold-scale problem are that branch's; do not re-plan them.**

### 3.1 First: finish rank 1 — the cadence field the branch did not touch

**Why first.** Perception is upstream of every strategic decision in the bot. Attack-axis scoring, capture
ordering, garrison sizing, heli target safety, truck evacuation, route detours — all read this field. Getting
it wrong does not degrade the bot slightly; it makes the bot *afraid of the wrong things*, which is the exact
opposite of "plays like a modern battlefield".

**What is left after the branch, precisely.** The branch fixes *magnitude* (overflow) and *scale-dependence*
(the derived unit). It does **not** fix *relative ranking* — `WeaponThroughput` still divides by `ReloadDelay`
and still never reads `BurstWait` (verified: the branch's diff touches `Compute`, adds `ReferenceIntensity`,
and leaves `:521-533` alone, acknowledging the problem in a new comment). **No unit denominator can correct a
ranking inversion**, because the inversion is in the ordering of contacts, not in the scale of the answer.

**Where I differ from the bug log.** The `[high]` entry of 2026-08-09 advises fixing the formula *first*, or
"the retune is fitted to the broken field". I think that is right about a *hand-tuned* retune and wrong about
this branch specifically: `ReferenceIntensity` is a median over the ruleset's own types, so it re-derives
itself when throughput changes — that is the branch's explicit design claim and it holds. So the order does not
matter for the thresholds. It matters for the **benchmark**: fixing throughput will move the field again and
require a *second* baseline re-take. If you want one re-take rather than two, land them together.

**Visible payoff.** High and specific. Today the field ranks a BMP2 above an Abrams by a factor of ~22,000
(§5.1) and an AT specialist ~900× above a machine gunner. After both fixes the bot should visibly stop walking
armour into armour and stop treating an infantry AT team as the most dangerous object on the map.

**Cost/risk.** M. It changes every cell value in the field, hence every threshold-derived behaviour, on both
profiles. Wants the branch's new `[danger] dist` log to validate against rather than reasoning.

### 3.2 Second: rank 2 — decide what "damaged" means, once

**Why here.** This is the highest **visible** payoff per unit of risk in the whole list, and it needs no new
subsystem. Today, on the benchmark control, a helicopter that takes one chip of damage parks for the rest of
the match — and the mod has no repair and no rearm, so this is not a rare edge case, it is the steady state.
You would see the difference in the first game.

**What to do, and it is a design decision rather than a patch.** Health and ammunition are **one-way
resources**, so the correct doctrine is *use it or bank it* — which is exactly what the evacuation work already
reinvented, `@experimental`-only. Replace the inherited
`ReloadsAutomatically`/`HasFullAmmo`/`ReEngageHealthPercent` triad on the launch path with a single "is this
airframe still worth committing?" predicate, and delete the three bypass flags that exist only to route around
it. **Leaving three unsatisfiable gates in place and bypassing each with its own flag is why the heli module
has 50+ Info fields**; this is the fix that stops that count growing.

**Cost/risk.** M, contained to the heli/air launch path. Moves `@stable` — deliberately, and for the better.

### 3.3 Third: rank 6 and rank 12 — delete the lies

**Why this high, given it changes no behaviour at all.** Because your stated goal is to *spot design problems
yourself*, and this list's single largest tax on that is that the bot **looks four times bigger and four times
more capable than it is**. Six of 22 rows are inert. A tuner reading `ai.yaml` today sees a base-building
surface, an engagement-range knob, a hit-and-run cooldown and a squad-size threshold, and every one of them is
a dead end. Worse, per P4, a knob that looks live is how a P1 bug gets made in the first place.

**Do it in this order,** cheapest and least arguable first: delete the five unread `AIHelicopterRole` fields
and their YAML (XS, zero risk) → strip `BaseBuilderBotModule@normal`'s construction config, keeping the trait
for its live `SetRallyPoint` half (S, zero behavioural risk — the fractions are already unreachable) → annotate
or remove the four `PoiOffensive` false levers → quarantine the 12 dead squad states to a `_unused/` folder
with a README rather than deleting, so the next engine merge stays mechanical. **One caveat that is real:**
removing `RushInterval` changes a `World.LocalRandom` draw and breaks byte-identity against the recorded
baseline, so that one is a deliberate, separately-measured act.

**Cost/risk.** S total. The only risk is deleting something that is inert *today* for a reason you want to
reverse later — which is why the squad states get quarantined rather than deleted.

### 3.4 Fourth: rank 3 — convert the 24 countdowns to tick stamps

**Why not higher, despite being the largest structural item.** Because on its own it changes nothing you can
see. It is an *enabler*: it converts "you cannot schedule this bot" into "you can", and the human-attention
model is the biggest realism idea on your roadmap. I put it fourth rather than first precisely because of the
"fixes that changed nothing visible" constraint — but I would not put it lower, because every month it waits is
a month of new modules adding countdown #25.

**Note what the in-code warning does and does not say.** `ModularBot.cs:215-224` argues against *gating before
converting* — the coverage of a 25-site conversion is not statically verifiable, so a scheduler bolted on today
would silently leak ledger claims. It does not argue against converting. Do the conversion; keep the gate off
until it is done.

### 3.5 Fifth: rank 5 — make heli and air target selection fog-legal

**Why it earns a place above several bigger items.** It is the only entry on this list a human opponent can
*perceive directly*. A bot that flies at a unit it has never seen reads as cheating in a way that a suboptimal
route never does — and `IsTargetTooHot` is the worst of them, because the bot declines to attack on the basis
of AA it cannot legally know about, which makes it look *cowardly* for reasons the player cannot observe. The
routing next door is already fog-legal, so this is finishing a half-conversion rather than starting one, and
the belief-side reads it needs already exist.

### 3.6 Explicitly deferred, with reasons

- **Rank 4, transports.** The biggest doctrine gap in the list, and I am still deferring it: per-unit
  destinations exist only as private fields on `Axis`/`Garrison` with no accessor, and the shared ledger stores
  an objective string with **no position**. That is a missing layer, not a wiring job — it is a design task,
  and it should follow rank 1 so that "where does this unit need to go" is being asked of a field that ranks
  threats correctly.
- **Rank 7, profile order asymmetry.** Cheap and worth doing, but it invalidates the benchmark baseline, so
  bundle it with the next deliberate re-take rather than spending one on it.
- **Rank 9, ledger `Release`.** Small and correct, but it changes ledger behaviour on both profiles, so it
  wants a benchmark rather than a drive-by.
- **Rank 13, house garrisoning.** Blocked on a design question only you can answer: *is occupying civilian
  buildings doctrine WW3MOD wants?* The module's name has been quietly answering "yes" since March.

---

## 4. What is genuinely good — and why it is the template

This project's own work is strong, and the contrast with the inherited material is the most useful thing in
this document, because **the good parts are good for reasons you can copy.**

**The extracted pure decision-math classes — the healthiest thing in the codebase.** 28 engine-free static
classes (`AmmoEvacMath`, `EscortSizingMath`, `ForwardStagingMath`, `OrderArbitrationMath`, `RetreatDamperMath`,
`TransportEmploymentMath`, …), NUnit-pinned, holding the decisions rather than the plumbing. **Note the
contrast that proves the point: not one inherited module has a `*Math` partner.** This is what makes WW3MOD's
modules portable to a future brain and the inherited ones not. It is also what made the overflow findable — the
bug was caught by writing a test at real magnitudes against a pure function, which is only possible because the
function is pure. **Template rule: when you extract something from a state machine or a module, extract it as a
pure static with a pin, not as a private helper.**

**The helicopter module — the mod's best subsystem.** ~3,000 lines across
`HelicopterSquadBotModule` + `HelicopterStates`, wholly WW3MOD, the only state machine that consumes the
influence stack, and the only unit pool in the bot that genuinely **rotates** — helis return to the idle pool
when their squad dissolves, where a fixed-wing aircraft is in its squad forever. It has real problems (§1
ranks 2, 5, 14) but they are *its* problems, or inherited ones it is routing around, not design errors. It is
also the only module that consistently does the hard thing correctly: `StageIdleHelicopters` checks
`QueueOrder`'s return value before advancing its state, with a comment explaining why
(`HelicopterSquadBotModule.cs:716-721`) — the exact discipline the FSM sites structurally cannot manage.

**The fog-legal belief stack — a construction property, not a policy.** `BeliefStore` has no code path that
reads an enemy actor the player cannot see: live sightings go through `CanBeViewedByPlayer` (the same test the
renderer uses) and remembered structures through the player's own frozen-actor layer. **A human and a bot with
identical vision get identical beliefs.** The static/mobile confidence split is genuinely good design, the
"verified-clear removes rather than decays" rule is the right call, and the whole stack holds **zero RNG draws**
with fixed deterministic stagger offsets — an invariant that was violated once, caught, and is now documented
with its reason. The `ControlField`'s scale is the proof that the danger field's problem is not "thresholds are
hard": `GrayBand 150` against `MaxScore 1000` and `PresenceGain 250` were **chosen together**, depend on
nothing in the ruleset, and so nothing about them broke when the ruleset was rescaled. **Template rule: give a
field a designed, bounded scale, or give its consumers only booleans and ratios.**

**The recent censuses and this documentation set.** The order-source census, the churn census, the transport
census, the unit-purpose census, the live-log recon and this six-document set are the reason this audit could be
written in a day. Three specific habits are worth protecting: **they cite `file:line` and say which commit they
read**; **they retract** — the live-log recon publicly withdrew a wrong `grep`-under-scanning diagnosis and
re-measured against the settled file, and a `discovered.md` entry publicly retracted a multiplayer-divergence
claim so the next reader would not "fix" a non-bug; and **they state what they could not verify**, which is why
§5.1 below was catchable at all.

**The order gate, for one property that is easy to miss.** Not the dwell window — the fact that its standing
records are **owned by the player and unreachable from any module**. That single lifetime property is what
breaks eligibility-coupled amnesia, and it is why an eighth per-module dedup would have failed where this
succeeded. Its fail-open design (an unknown prefix, an unattributed order, an unrecognised module all admit) is
also the right call: **table rot degrades to "no suppression", never to "this module silently cannot give
orders."**

**`LaneAmbushBotModule`'s header** is the best-written in the repo: it names three carried observations
including why the `^AutoTargetGround` family is auto-excluded. That is what a module header should do.

---

## 5. Disagreements, corrections, and what I could not verify

### 5.1 The one source claim that did not survive verification

**[`04-perception-and-fields.md`](04-perception-and-fields.md) §3.2 publishes a magnitude table that is
unreachable for its heavy-weapon rows, and its headline sentence is false as executed.** This is the most-quoted
table in the set and it will be used to justify future numbers, so it needs stating plainly.

Doc 04 computes, for a believed Abrams, `intensity = 67,850,000` and an outermost-ring value of `2,423,214`,
and builds its headline on it: *"the outermost, faintest ring of a single believed Abrams reads 2,423,214."*
That is the value the formula produces **in exact arithmetic**. The code at `DangerFieldLayer.cs:170` evaluates
it in `int`, left to right, and `throughput * durabilityWeight` = 2,300,000 × 2,950 ≈ **6.8 × 10⁹** exceeds
`int.MaxValue`. It wraps negative, falls through the `intensity < 1` guard at `:171-172`, and is clamped to the
floor of **1**. A believed Abrams paints exactly one cell, at value 1. I verified the types (`DangerKernelFacts`
and `DangerKernelParams` are all `int`) and the expression at `main @ dcc2f7c5`; `auto/danger-scale` found this
independently by writing a regression test at real magnitudes and fixes it with `long` + saturation.

**Which of doc 04's four rows are affected:** rifleman (162,626), BMP2 (2,197,440) and ATGM (151,200,000) all
compute below the wrap and their published intensities stand. **Only the Abrams row overflows** — and it is the
row the headline rests on.

**Why this makes the problem worse rather than better, and in a different direction.** Doc 04 concludes the
field over-ranks heavy weapons — *"an Abrams is 3,100× more dangerous than a BMP2, when the true ratio is under
2×"*. As executed the Abrams reads **1** and the BMP2 reads **21,974**, so the field ranks the **BMP2 about
22,000× above the Abrams**. The inversion is real, larger, and points the opposite way. Anyone reading doc 04's
table — or the `[high]` bug-log entry, which reproduces its ratios — to choose a threshold or predict a
behaviour will get the sign wrong. **My reading: doc 04's §5 threshold verdicts still stand** (the thresholds
are unjustifiable either way), **and its §3.2 ratios do not, for the heavy-vehicle class.** Filed, §6.

### 5.2 Where sources disagree, and my reading

**(a) Does the order gate fix the declaration-order problem?** [`03`](03-module-catalogue.md) §2.3 presents the
gate as damping it; [`02`](02-lifecycle-and-arbitration.md) §5.2 audits itself and says **"predicate (a) is not
what damps the user's churn"** — it only adds anything for three modules that do not already consult the ledger
when building their pool, and **only on `@stable`**. **Doc 02's is the more careful reading and I take it.**
What actually damps the observed churn is predicate (b), the dwell, at four call sites. The declaration-order
*asymmetry between profiles* (rank 7) is untouched by either predicate.

**(b) Is `@stable` "the same bot with fewer switches"?** Doc 03 §0 says yes and backs it with a mechanical
twin-diff finding zero divergence on any shared key — then doc 03 §2.3 shows the two profiles tick their
modules in a **different relative order**. Both are doc 03's, and they are in tension: identical key-values
under a different arbitration order is not the same bot. **My reading: the key-set claim is sound and the
"same bot" summary over-reaches**; treat declaration order as an unmirrored configuration difference between
control and experiment. **Settled 2026-08-09: doc 03 §0 now carries that qualification explicitly.**

**(c) Sequencing the throughput fix against the threshold retune.** The `[high]` bug-log entry says fix the
formula first or the retune is fitted to a broken field. `auto/danger-scale` did it the other way. **I side
with the branch on the mechanism and with the bug log on the consequence** — see §3.1: the derived unit
self-re-derives, so the thresholds survive, but the benchmark does not, and doing them separately costs two
baseline re-takes instead of one.

**(d) Whether the ledger-participation flag split is a mistake.** Doc 02 §6.2 calls the `@experimental`-only
`goalGuard` resolution on three modules "leftovers" of a retired policy and a mistake, because the control ends
up with different *arbitration semantics* from the experiment. Doc 04 §8.4 argues the general case the other
way — `@stable` inherits improvements, so `Participates` is usually the right gate. **These are compatible and
both right**: doc 04's rule is about new behavioural reads, doc 02's complaint is about arbitration semantics
specifically, where a control that arbitrates differently makes A/B attribution *harder*. I agree with doc 02
for this case.

### 5.3 What I could not verify, carried forward with its caveat

- **Doc 04's sustained-output ratios (4.5× / 7.8× / 200× / 130×)** depend on that author's model of the WW3MOD
  firing cycle from `Armament.UpdateBurst`/`UpdateMagazine`. **The fact** that `WeaponThroughput` never reads
  `BurstWait` needs no model and is direct at `:521-533` — I re-read it. The ratios I did not re-derive.
- **The `auto/danger-scale` commit reports the rifleman's clean intensity as "~1,212"; doc 04 computes 1,626**
  for `5.56mm.AR` on `AR`. I did not resolve the discrepancy — it is plausibly a different carrier or a
  `Burst` reading, and nothing in this document's ranking depends on it. Flagging it so nobody treats either
  number as settled.
- **The "28 distinct anti-churn dampers" count** is carried from the 0808 churn census appendix; I confirmed the
  census asserts it and did not recount the 28.
- **Doc 03's mechanical twin-diff** ("zero divergence on any shared key" across nine twinned pairs) I did not
  re-run. Its method is stated and reproducible.
- **Doc 05's dead-line arithmetic** I did not recount at the time; I did independently re-verify the
  *reachability* claims it rests on (`IgnoreGroundUnits` on all four instances, `NavalUnitsTypes` /
  `ProtectionTypes` / `ConstructionYardTypes` set on no `SquadManagerBotModule`). **Recounted 2026-08-09:**
  the four dead state files total **987** lines (`GroundStates` 382 + `NavyStates` 251 + `ProtectionStates` 79
  + `AttackOrFleeFuzzy` 275), plus ~130 lines of dead `SquadManagerBotModule` members ⇒ **≈1,120**, not the
  "~1,050" both docs were quoting.
- **Doc 02's "~57 `IsIdle`-gated recruitment filters"** — carried from the 0807 census, not recounted.
- **Nothing here was run.** No build, no game, no autotest. Every behavioural statement is static reasoning
  over code plus the one live-log recon, which is the only observed data in the entire document set.

---

## 6. Filed this pass

Nothing was fixed; this was an audit. One new entry added to
[`WORKSPACE/bugs/discovered.md`](../../WORKSPACE/bugs/discovered.md), 2026-08-09:

> **`[med, doc-in-doc]`** `DOCS/bots/04-perception-and-fields.md` §3.2's danger-intensity table is unreachable
> for its heavy-weapon rows because of the `int` overflow at `DangerFieldLayer.cs:170`, and the ranking
> inversion it describes points the **opposite way** from the executed field. Also records that
> `auto/danger-scale` fixes the overflow and the threshold unit but **not** `WeaponThroughput`'s
> `ReloadDelay`/`BurstWait` defect, so the `[high]` entry of the same date must not be closed when that branch
> merges.

Everything else in §1 was already on the record — in `discovered.md`, in the four sibling documents, or in the
`auto/danger-scale` branch.

---

## See also

- [`02-lifecycle-and-arbitration.md`](02-lifecycle-and-arbitration.md) — tick path, cadences, the two order
  layers, four ownership mechanisms, the order gate.
- [`03-module-catalogue.md`](03-module-catalogue.md) — every module, what it claims, what is inert.
- [`04-perception-and-fields.md`](04-perception-and-fields.md) — the four fields and every threshold against
  them. **Read §3.2 with §5.1 above.**
- [`05-squads-and-combat-states.md`](05-squads-and-combat-states.md) — the squad layer and which **thirteen**
  of its twenty states are dead.
- [`game-model.md`](../reference/game-model.md), [`supply-route.md`](../reference/supply-route.md) — the
  yardstick every row in §1 is measured against.
- [`260809-truck-loop-from-live-log.md`](../../WORKSPACE/recon/260809-truck-loop-from-live-log.md) — the only
  observed evidence in this document set.
