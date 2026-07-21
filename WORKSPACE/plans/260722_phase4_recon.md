# Phase-4 recon — bot consumption of the role model + full fog migration

**Status:** READ-ONLY design recon (implementation-ready). No code changed by this
pass. Researched against **main @ `ed483688`** (`git log --oneline -3`: ed483688
curation pass / 41a9c3d9 Phase-3 demo / 7f1138e3 Phase-3 executor merge). Tree
`ahead 171` of `origin/main`, `0` behind upstream — all cited SHAs are in main's
history (`git merge-base --is-ancestor 575e9c7d HEAD` ✓, resolver merged
`575e9c7d`, executor `a88ef596`, Phase-1 layers present).

**SPEC anchor:** `WORKSPACE/plans/260722_strategic_tactical_split_SPEC.md` §3c/§7
Phase-4 + the 2026-07-21 operations-layer amendment. **Constraints:**
`WORKSPACE/plans/260722_phase3_redteam.md` (B1/B3/N4). **Role design:**
`WORKSPACE/plans/260722_unit_role_resolver_DESIGN.md` §6 (consumer migration map —
authoritative; this doc re-verifies its citations against code and adds the fog
half). **Re-baseline protocol:** `WORKSPACE/ai-bench/{README,LADDER,SPEC}.md`.

Every claim below carries a `file:line` verified by reading the code at
`ed483688`, not summarised from doc memory. Paths are relative to repo root;
engine C# lives under `engine/OpenRA.Mods.Common/Traits/`.

---

## 0. What Phase 4 is, in one paragraph

SPEC §7 Phase 4: *"Squad FSMs delegate micro to L3 (stop re-issuing positioning);
InfluenceMap + ThreatMapManager rebuilt on per-player fog-respecting intel
(absorbs ladder cycle 5). Declared re-baseline event; expect an initial
bot-strength dip; opens a recon/scouting behavior cycle so bots buy back the lost
intel."* Plus the 2026-07-21 amendment: **a minimal scout link ships inside Phase 4
itself** (SPEC §7 Phase-4 doctrine-audit rider) — not deferred — so bots don't
"march into ambushes on decayed intel." The Phase-3 rider also lands the role
resolver (done, `575e9c7d`, inert); **Phase 4 is where consumers turn it on.**

Two workstreams, largely independent:
- **(A) Role-model consumption** — flip the per-module `UseUnitRoles` flags the
  design specified (§6) and rewrite each hard-coded type-list read as a
  `UnitRoleResolver` query. Mostly `@experimental`-gated ⇒ can keep `@stable`
  byte-identical. Mechanically safe.
- **(B) Full fog migration** — make bot strategic intel fog-respecting. This is
  the judgment-heavy, re-baseline-forcing half, because the omniscient grids are
  read by **shared** (all-profile) code, not just `@experimental`.

---

## 1. Role-model consumption surface

**Resolver API** (`Traits/World/UnitRoleResolver.cs:184-196`): `GetRole(Actor)` /
`GetRole(ActorInfo)` → `UnitRole`; `NamesWithRole(UnitRole)` →
`IReadOnlyCollection<string>`. Enum `UnitRole` (`:37-48`): `None, MainBattle,
IndirectFire, ShortRangeAD, Recon, TransportLift, CaptureSpecialist, Logistics,
AttackAir`. Acquire once in a consumer's init via
`world.WorldActor.Trait<UnitRoleResolver>()` (same pattern as the
`TraitOrDefault<InfluenceMap>` acquisitions at `PoiMap.cs:522`,
`LayeredDefenceBotModule.cs:138`). The trait is registered globally
(`world.yaml:339`) and inert until read (`UnitRoleResolver.cs:17-20`). The design's
governing rule (design §6, `architecture.md` `CohesionSwitchEnabled` precedent):
**every migrated module adds `UseUnitRoles = false` (code default = frozen
baseline); flip `true` only on `@experimental` YAML, priced before promotion.**

### 1a. Hard-coded classification sites (verified) and the replacing query

| Module (file) | Site (verified) | Classifies | Replace with |
|---|---|---|---|
| **PoiOffensiveBotModule** | `IsEligibleCombatUnit` `:410-426`: gate `HasTraitInfo<AttackBaseInfo>()` + `!HasTraitInfo<AircraftInfo>()` + `!ExcludeUnitTypes.Contains(a.Info.Name)` (`:414-425`); `ExcludeUnitTypes` field `:82`; free-pool build `:403` | line-combat eligibility for offensive axes | `GetRole(a) == MainBattle` (design §6: IndirectFire stays eligible until the fires executor exists, else artillery orphans). Drops the `ExcludeUnitTypes` list. |
| **PoiGarrisonBotModule** | `ExcludeUnitTypes` `:89`; aircraft skip `:315`; `!Contains` `:317` | garrison free-pool | same filter as PoiOffensive (`role == MainBattle`) |
| **LayeredDefenceBotModule** | `ScreenUnitTypes` `:52`, `MainLineUnitTypes` `:56`, hard-coded `ExcludedActorTypes` `:90-97` (tecn/e6/truk/humvee/btr/bradley/bmp2/m113 — the copy-pasted list), eligibility `.Contains(name)` `:262/:265/:266` | screen vs main-line vs excluded — the **flagship defect** (ai.yaml:349 puts artillery+SHORAD+MANPADS on the line) | eligibility = `GetRole(a) == MainBattle` (artillery/SHORAD/MANPADS drop out by class); `ExcludedActorTypes` becomes `role != MainBattle`. Screen/line *partition* stays list-based in v1 (design §2.1) |
| **MountedTransportBotModule** | `CarrierTypes` `:44`, `PassengerTypes` `:47`; `.Contains` `:154/:374` (carrier), `:417` (passenger) | ferry carriers + passengers | carriers `role == TransportLift`; passengers `PassengerInfo` present ∧ `role ∈ {MainBattle, CaptureSpecialist}` (design §6). **Gated `enable-ai-experimental \|\| enable-ai-stable` (`ai.yaml:121`) — see §3 caution** |
| **CaptureCoordinatorBotModule** | `CapturingActorTypes` `:35`, `CapturableActorTypes` `:38`, `SupportingUnitTypes` `:79`; `.Contains` `:328/:359/:460` (capturable), `:722/:759` (capturer excl.), `:767` (supporter) | capturer / capture-target / escort | capturer `role == CaptureSpecialist`. `CapturableActorTypes` is a **target** list, not a unit role — leave it (design §2.1) |
| **CaptureManagerBotModule** (legacy) | `CapturingActorTypes` `:25`, `CapturableActorTypes` `:29`, `.Contains` `:141` | legacy capture | **do not migrate** — `enable-ai-legacy-only` (`ai.yaml:102`) is a frozen control |
| **ScoutBotModule** | `ScoutTypes` `:24`; `a.Info.Name == scoutType` `:135` | scouts | `role == Recon`. **`enable-ai-any` (`ai.yaml`) — shared across ALL profiles; migrate LAST, its flag flip is itself a priced change** (design §6) |
| **SupplyFollowerBotModule** | `SupplyTruckTypes` `:22`; `.Contains` `:95`, `!Contains` `:105` | supply trucks | `role == Logistics` ∧ `SupplyProvider` present. **`enable-ai-any` — same caution as Scout** |
| **SquadManagerBotModule** | `AirUnitsTypes` `:31`, `NavalUnitsTypes` `:25`, `ExcludeFromSquadsTypes` `:34`, `ConstructionYardTypes` `:43`; `.Contains` `:286` (air), `:294` (naval), `:387-388/:397` (rush excl.) | air/naval/excluded recruit filters | air `role == AttackAir`; excludes by `role ∉ combat set`. **Experimental twins only; legacy frozen** (design §6). Naval unused in WW3MOD (design §8 Q5) |
| **AdaptiveProductionBotModule** | `AntiAirUnits` `:36` (+ `AntiVehicleUnits` `:30`, `AntiInfantryUnits` `:33`); `.Contains` `:168` | production counter-pools | `AntiAirUnits` → `role == ShortRangeAD ∪ AA-infantry`. `AntiVehicle/AntiInfantry` need finer-than-role data — **not migrated in v1** (design §6) |
| **GarrisonBotModule** | `GarrisonActorTypes` `:22`; `.Contains` `:172` | garrison-eligible infantry | leave list in v1 (garrison eligibility ≈ `PassengerInfo` at `:175`, not a coarse role); low priority |

### 1b. What is NOT a role migration (leave as-is)

- **Trait-presence checks that are not roster classification** stay: `ThreatMapManager`
  militariness (`HasTraitInfo<AttackBaseInfo>()`/`AutoTargetInfo`/`ProductionInfo`…,
  `ThreatMapManager.cs:105-110`), `HelicopterSquadBotModule` selecting by
  `AIHelicopterRole` trait presence (`:153`) — the resolver *maps* `AIHelicopterRole`
  (`UnitRoleResolver.cs:262-272`) rather than replacing its consumer (design §2.2/2.3).
- `CapturableActorTypes` (capture **targets**), `ConstructionYardTypes`,
  `SupplyRouteTypes`, `NavalProductionTypes` — targets/structures, not unit roles.
- Legacy/`@stable` twins and building/production-queue types (design §6 last row).

### 1c. Seed `AIUnitRole` overrides before flipping any consumer

Finding B3 (`260722_phase3_redteam.md`) and the resolver header
(`UnitRoleResolver.cs:23-26`) fixed the cascade so armed IFVs/AA-vehicles are not
mis-bucketed as carriers, but the design (§6.2) still calls for explicit overrides
on the ~ambiguous hulls. These are **YAML annotations** (`AIUnitRoleInfo`,
`UnitRoleResolver.cs:52-58`), inert until a consumer reads them, so they can land
first with zero behaviour risk: seed `e6: Logistics`, and decide `bradley/bmp2`
(MainBattle vs TransportLift) *with* the LayeredDefence/PoiOffensive consumers.
`strykershorad` needs **no** override (AD precedes Cargo in the cascade,
`UnitRoleResolver.cs:296` before `:311`). **Recommend an NUnit assertion pinning
the full classification table before flipping any consumer** — the resolver already
has one (`OpenRA.Test/OpenRA.Mods.Common/UnitRoleResolverTest.cs`); extend it with
every consumer-relevant hull so a future YAML edit that silently re-buckets a unit
fails a cheap test, not a benchmark.

---

## 2. Fog migration surface

### 2.0 What "full fog migration" means (SPEC §3c, quoted)

> *"**FULL FOG MIGRATION** … Bot strategic grids become fog-respecting as part of
> this project, sourced from the same per-player intel substrate as 3a — bots and
> humans reason on identical information rights at every layer. This absorbs ladder
> cycle 5 into Phase 4… Consequences accepted: bots initially get *weaker* (they
> lose free intel) and every benchmark baseline shifts."*

**Interpretation (stated explicitly, since the target substrate matters):** the
per-player intel substrate "3a" already exists and is the migration target —
`SightingThreatLayer` (`Traits/World/SightingThreatLayer.cs`, registered
`world.yaml:325`), built strictly from own vision + `FrozenActorLayer`
(`SightingThreatLayer.cs:189-237`), per-player (`fields` keyed by Player, `:110`).
Its query API (`:286-338`): `ThreatIntensity(player, cell)`,
`ThreatDirection(player, cell)`, `FriendlyIntensity(player, cell)`,
`ActiveCells(player)`.

> **AMBIGUITY / GAP I am flagging.** `SightingThreatLayer` is an *intensity* field
> (armed-only, `FreshWeight 100` / `FrozenWeight 60`, decaying — `:57-66`), **not a
> $-cost-weighted influence grid**. The omniscient grids it must replace are
> value-weighted: `InfluenceMap` sums `GetSellValue()/ValueDivisor`
> (`InfluenceMap.cs:104-115`); `ThreatMapManager` sums `ValuedInfo.Cost ×
> healthRatio` (`ThreatMapManager.cs:94-124`). So "source from the same substrate"
> is **not a drop-in**: either (a) extend `SightingThreatLayer` to carry a
> value/cost channel (fog-correct, per-player), or (b) leave the grids' *shape* but
> gate their enemy contributions by the viewer's visibility. The SPEC does not
> resolve this; **my recommendation is (a)** — one fog-correct value-weighted
> per-player field feeding both grids and the L3 executor — because (b) still
> requires per-viewer enemy layers (see §2.1) and leaves two parallel omniscient
> scanners. This is the single biggest design decision Phase 4 must make and should
> be ratified before coding.

### 2.1 Omniscient reads, enumerated (file:line) with fog-correct replacement

**Grid builders (the roots):**

| Read (file:line) | Why omniscient | Per-player? | Fog-correct replacement |
|---|---|---|---|
| `InfluenceMap.Recompute` `InfluenceMap.cs:92` (`foreach actor in world.Actors`), reads `actor.Location` `:114`, `GetSellValue()` `:104` | scans every actor, no shroud/frozen check | layers keyed **by owner** (`:58`) — each owner's own true positions | The build isn't the leak; the **query** is: `GetEnemyInfluence(perspective)` `:156-166` sums enemy owners' *ground-truth* layers. Fix: build enemy influence **per viewer** from that viewer's visible + frozen enemies (mirror `SightingThreatLayer.InjectSightings:189-237`), or add a value channel there and derive influence from it. |
| `ThreatMapManager.RecalculateThreatMap` `ThreatMapManager.cs:89` (`world.Actors`), `actor.Location` `:99`, health `:113` | scans every actor; grid is **shared/global**, not per-player (fields `:45-48` are single `float[,]`; the `:43` "per-player" comment is **stale/wrong**) | **NO** | rebuild per-perspective from the fog-correct value field; every query below then reads the per-player grid instead of re-scanning |
| `ThreatMapManager` query rescans: `GetPlayerMilitaryValue` `:175`, `GetThreat` `:204`, `FindWeakestEnemyCell` `:250`, `FindAttackTargets` `:363` — each `world.FindActorsInCircle(...)` with only a relationship filter, no shroud | per-query omniscient re-scan | takes `perspective` but ignores its fog | replace with reads of the per-player grid (or `SightingThreatLayer.ThreatIntensity`) |
| `PoiMap.Discover` `PoiMap.cs:206` (`world.Actors`) | discovers every POI regardless of fog | candidates owner-agnostic (`:173`) | POI *existence* is arguably fair (map objectives) but **enemy-owned POIs & their defenders** leak; gate discovery of enemy actors by visibility, keep neutral/own |
| `PoiMap.SampleThreat` fallback `:493` (`world.FindActorsInCircle`) | counts enemies with no shroud check | — | when `InfluenceMap` present it reads the enemy layer (`:483-487`) → inherits the InfluenceMap fix; the `:493` fallback needs the same fog gate |
| `PoiMap.FindOwnSupplyRoute` `:503` (`world.Actors`, `Owner == perspective`) | scans all actors but only for **own** SR | — | **legal** (you always know your own SR) — no change needed; flagged only to pre-empt a false positive |

**Squad-FSM + module target/threat acquisition (all `world.FindActorsInCircle`
with a relationship filter, no shroud — omniscient):**

- `SquadManagerBotModule`: `FindClosestEnemy` `:210`, radius scan `:216`;
  `FindNewUnits` `ActorsHavingTrait<IPositionable>` `:276` (own units — legal);
  rush scan `:396`; protection scan `:425`.
- `GroundStates`: idle-enemy scan `:50`; attack-move enemy scan `:165`; regroup
  threat scan `:318`; retreat destination via
  `ThreatMapManager.FindSafestRetreatCell(perspective, …)` `:270-275` (omniscient
  through the grid).
- `StateBase`: `ShouldFlee` danger scan `:89`; `RandomBuildingLocation`
  `ActorsHavingTrait<Building>` (own — legal) `:32`.
- `AirStates` `:97`, `NavyStates` `:74`, `HelicopterStates` `:158/:211/:280` — same
  omniscient circle scans; `HelicopterStates` also reads `ThreatMapManager`
  `:203/:449`.
- `CaptureCoordinatorBotModule`: legacy fallback `GetActorsThatCanBeOrderedByPlayer`
  `:784` (omniscient), but `GetVisibleActorsBelongingToPlayer` `:778` uses
  `CanBeViewedByPlayer` (**already fog-correct** — the model to copy); capture-safety
  scan `:644`, defense scans `:692/:707/:719`, escort recruit `:753`.
- `PoiOffensiveBotModule.BuildFreePool` `:403` and `LayeredDefenceBotModule` `:173`/`:254`
  scan `world.Actors` for **own** units (`Owner == player`) — **legal** (own-unit
  enumeration), not a fog leak; listed to bound the surface.

The fog-correct replacement for enemy-target scans is `SightingThreatLayer` +
`FrozenActorLayer` reads (the `CaptureCoordinator.cs:778` `CanBeViewedByPlayer`
pattern and `SightingThreatLayer.cs:207/215/225` are the two in-repo templates).

### 2.2 Shared vs @experimental-only — the re-baseline classifier

This is the load-bearing distinction. **A change is `@stable`-byte-identical only
if every consumer of the changed code is `@experimental`-gated.** Consumer gates
(`ai.yaml` `RequiresCondition`, condition semantics `ai.yaml:7-24`):

- **`InfluenceMap`** consumers: `PoiMap` (read by `@experimental`/`@stable` capture
  + `@experimental` offense/garrison), `LayeredDefenceBotModule`
  (`enable-ai-experimental`, `ai.yaml:181`), `MountedTransportBotModule`
  (`enable-ai-experimental \|\| enable-ai-stable`, `ai.yaml:121`),
  `FrontlineOverlay` (render-only). ⇒ **touches `@experimental` AND `@stable`
  (via MountedTransport + the promoted `@stable` capture path). NOT
  control-affecting** (Normal/Rush/Turtle/legacy never read it — `PoiMap.cs:26-27`
  header + `world.yaml:294-295`). **Changing InfluenceMap re-prices `@stable`.**
- **`ThreatMapManager`** consumers: `SupplyFollowerBotModule` (`enable-ai-any`,
  `SupplyFollowerBotModule.cs:70`), `GarrisonBotModule` (`enable-ai-any`,
  `:71`), `ScoutBotModule` (`enable-ai-any`, `:73`), `SquadManagerBotModule`
  (`:332`), `HelicopterSquadBotModule` (`:103`), and the **squad FSM**
  (`GroundStates:270/327`, `HelicopterStates:203/449`). `enable-ai-any` = *normal,
  rush, turtle, experimental, stable* (`ai.yaml:7`), and the squad FSM runs for
  every bot. ⇒ **🚨 SHARED ACROSS ALL PROFILES INCLUDING THE FROZEN CONTROLS.
  Any change to `ThreatMapManager` changes Normal/Rush/Turtle behavior — it is NOT
  byte-identical and it violates the ai-bench "controls stay byte-identical"
  invariant (`ai-bench/README.md:36`) unless done as a declared ship-to-everyone
  re-baseline (Phase-0 cohesion-cap precedent, `ai-bench/LADDER.md:87-95`).**
- **`SightingThreatLayer`** (the target substrate): currently **zero consumers**
  (`SightingThreatLayer.cs:35`) — adding value-weight or new readers is inert until
  a consumer flips on.

### 2.3 The uncomfortable finding

The SPEC says "InfluenceMap **and** ThreatMapManager rebuilt on fog-respecting
intel." InfluenceMap can be migrated inside the `@experimental`/`@stable` envelope
(re-prices `@stable`, a declared re-baseline — acceptable). **ThreatMapManager
cannot** be touched without moving the Normal/Rush/Turtle controls, because
`enable-ai-any` modules and the shared squad FSM read it. Options, to be ratified:

1. **Repoint, don't rebuild.** Migrate only `@experimental`/`@stable` consumers off
   `ThreatMapManager` onto the fog-correct field; leave `ThreatMapManager` itself
   untouched (controls keep reading the omniscient version → byte-identical). The
   omniscient grid persists only for the frozen controls. **Keeps the invariant;
   preferred.**
2. **Ship-to-everyone re-baseline.** Make `ThreatMapManager` fog-respecting for all;
   accept that Normal/Rush/Turtle change and re-baseline them (Phase-0 precedent).
   Larger blast radius, weakens the controls' role as a *fixed* yardstick.

I recommend **(1)**: it satisfies "identical information rights for bots and
humans" for the *living* AI (`@experimental`, and `@stable` as its frozen twin)
without disturbing the Normal control that exists precisely to be an unchanging
ruler. The SPEC's "full migration" intent is met for every profile that is
actually being improved.

---

## 3. Ordering + risk

**Guiding rule:** land inert/annotation work first, then `@experimental`-only
behavior, then the one declared `@stable` re-baseline, and keep the
`enable-ai-any`/control paths for last (or never — see §2.3 option 1).

**Phase 4a — role annotations + tests (mechanically safe, inert).**
Seed `AIUnitRole` overrides (§1c); extend `UnitRoleResolverTest.cs` to pin the full
consumer-relevant table. Zero behavior change, `@stable` byte-identical (design §6,
N2 `260722_phase3_redteam.md:308-317` — no RNG, no trait-order change).

**Phase 4b — role consumption, `@experimental`-only (safe, priced per module).**
Add `UseUnitRoles=false` to each module in §1a; flip `true` on `@experimental` YAML
only. Order by design §6: LayeredDefence eligibility first (the named ai.yaml:349
defect, cheapest to price) → PoiOffensive/PoiGarrison free-pool → SquadManager air
recruit → CaptureCoordinator capturer → AdaptiveProduction AA. **Each is one
`@experimental` pricing run (S1+S2).** `@stable` untouched because its twins keep
`UseUnitRoles=false`. **Defer `enable-ai-any` modules (Scout, SupplyFollower) — their
flip changes controls; treat like a re-baseline (design §6).**

**Phase 4c — fog migration, InfluenceMap path (`@experimental`+`@stable`, declared
re-baseline).** Decide §2.0 (recommend: add a value channel to `SightingThreatLayer`
or a per-viewer enemy-influence build in `InfluenceMap.GetEnemyInfluence:156`).
Re-prices `@stable` (via MountedTransport + promoted capture). **Judgment-heavy:**
scoring semantics shift when enemy intel decays; expect the SPEC's "initial dip."

**Phase 4d — squad-FSM delegation + ThreatMapManager repoint (judgment-heavy).**
Squad FSM stops re-issuing positioning (SPEC §7) and reads the fog-correct field
instead of `ThreatMapManager` for `@experimental`/`@stable`. Per §2.3 option 1,
leave `ThreatMapManager` itself for the controls. This is where the L2→L3
hand-off (SPEC §2 contract) actually bites — the executor (Phase 2/3, merged) must
own arrived-unit micro while the FSM commands intent; watch the 75-tick re-fire vs
ledger (`StateBase.ExcludeTacticallyCommitted`, design context).

**Phase 4e — minimal scout link (SPEC-mandated inside Phase 4).** Cheapest
`role == Recon` unit periodically tasked toward stale high-value cells of the
per-player intel field. Natural fit: `SightingThreatLayer.ActiveCells(player)` +
staleness, or `ThreatMapManager.GetExplorationAge` (`:333`, already present).
Gated `@experimental`.

**Mechanically safe vs judgment-heavy:** 4a/4b safe (flag-gated, list→query is
behavior-preserving *if* the resolver table matches the old lists — that's what the
NUnit pin guarantees). 4c/4d judgment-heavy (intel semantics + L2/L3 arbitration).
4e judgment-heavy (new behavior, needs its own pricing).

**Existing autotests over touched paths** (scenario dirs under
`tools/autotest/scenarios/`): `test-experimental-poi-capture`,
`test-experimental-poi-harness`, `test-experimental-poi-observe` (PoiMap →
InfluenceMap → CaptureCoordinator — the InfluenceMap fog path), `test-heli-squad-forms`
(HelicopterSquad → ThreatMapManager), `test-stance-positioning` (the L3 executor
4d must not fight), `demo-layered-defence` (LayeredDefence eligibility, 4b),
`tournament-capture-arena-2p` + `-mirror` (capture ladder). Unit: the resolver's
own `UnitRoleResolverTest.cs`.

**New tests needed:**
- NUnit classification-table pin extended per §1c (before 4b).
- Fog-honesty autotest: bot with a scripted blind spot must **not** target/score an
  enemy it cannot legally see (the whole point of 4c/4d) — assert no order toward a
  fully-fogged enemy cluster.
- L2/L3 non-conflict autotest (4d): squad FSM + executor on the same units — assert
  the FSM doesn't re-issue a positioning order the executor owns (ledger honored).
- Scout-link autotest (4e): assert the recon unit visits a stale high-intel cell.

---

## 4. Re-baseline plan (post-Phase-4)

**Governing docs:** `ai-bench/{README,LADDER,SPEC}.md`. Phase 4 is a **declared
re-baseline event** (SPEC §3c/§7); the mechanism and precedent are the Phase-0
cohesion-cap re-baseline (`ai-bench/LADDER.md:87-119`) and the regime re-baseline
(`:54-84`), both of which re-ran the full batch after a change that shipped to
frozen controls.

**Current regime (`LADDER.md:19-84`, must be preserved):** Motorized start both
sides, **same-faction US-US** (both `america`), primary opponent **`@stable`**;
Normal demoted to a single sanity floor. Per-index seeds `i*1000+17`, deterministic
replay (`LADDER.md:196-208`), even=primary / odd=mirror.

**Matchups + counts to re-run after Phase 4 lands (per LADDER registry `:586-595`):**

| Scenario | Matchup | N | Purpose |
|---|---|---|---|
| `tournament-s1-eco-river-zeta` (+ `-mirror`) | Exp vs `@stable` | **10** (5 primary + 5 mirror) | economy: capture-income after role-consumption + fog intel |
| `tournament-s2-combat-river-zeta` (+ `-mirror`) | Exp vs `@stable` | **10** | force efficiency (net swing) under fog-honest targeting |
| `tournament-s1-eco-cal-nn` | **Stable-vs-Stable** | **10** | side/spawn bias re-cal — **mandatory** because 4c/4d moved `@stable` |
| `tournament-s2-combat-river-zeta-cal-nn` | **Stable-vs-Stable** | **10** | side-fairness + min-engagement floor re-cal at 720s |
| `tournament-s1-eco-floor-vs-normal` | Exp vs `@normal` | **3** (SPEC floor N) | "has Exp regressed below the frozen control?" sanity — NOT part of composite gate |
| S3 win-rate (TBD map, `LADDER.md:519-534`) | Exp vs `@stable` | **20** | only at S3 standup |

**Why the `-cal-nn` (Stable-vs-Stable) re-runs are non-optional:** Phase 4c/4d
change `@stable` (MountedTransport + promoted capture read the migrated InfluenceMap;
squad-FSM repoint if `@stable` is included). The moment `@stable` moves, the frozen
control's own baseline and the map-bias calibration are stale (`LADDER.md:88-95`
is the exact precedent — "the `60b93501` regime numbers above are void and
re-measured here"). Run the CALIBRATE **before** trusting any Exp-vs-Stable bar.

**Sequencing (per `ai-bench/SPEC.md` promotion policy §13, referenced
`LADDER.md:370-384`):** (1) land Phase 4 on `@experimental` only, price 4b modules
one at a time vs the *current* `@stable`; (2) when the InfluenceMap fog path (4c)
is ready, run it as its own declared re-baseline — re-cal Stable-vs-Stable, then
Exp-vs-Stable S1+S2; (3) expect and **document the SPEC-accepted initial
bot-strength dip** (`ai-bench/README.md:35` "improve the AI, never shorten the
yardstick" still binds — the dip is a real strength change, not an instrument
change, so it is logged, not masked); (4) the scout link (4e) is the lever that
buys the intel back — price it against the post-fog baseline, not the pre-fog one.
**No batch/tournament run without explicit user goahead** (standing rule,
`CLAUDE.md`; SPEC §6 governance).

**Composite gate unchanged (`LADDER.md:556-580`):** one commit passes S1 ∧ S2 ∧ S3
re-verified together. Phase 4 does not clear the rung by itself; it resets the bars
the rung is measured against.

---

## 5. Biggest findings / risks (summary)

1. **ThreatMapManager is shared across ALL bot profiles** (via `enable-ai-any`
   Scout/SupplyFollower/Garrison + the squad FSM, `ThreatMapManager.cs` consumers
   `SupplyFollowerBotModule.cs:70`/`ScoutBotModule.cs:73`/`GroundStates.cs:270`).
   The SPEC's "rebuild ThreatMapManager fog-respecting" **cannot** be done
   byte-identically for the Normal control — **recommend repoint-don't-rebuild
   (§2.3 option 1)** so the frozen yardstick stays fixed.
2. **The intel substrate is not a drop-in** (§2.0): `SightingThreatLayer` is an
   armed-only *intensity* field; the grids it must replace are *value/cost*
   weighted. Phase 4 must first decide value-channel-on-SightingThreatLayer vs
   per-viewer-InfluenceMap. **This is the gating design decision; ratify before
   coding.**
3. **InfluenceMap fog migration re-prices `@stable`** (not just `@experimental`) via
   `MountedTransportBotModule` (`ai.yaml:121`) and the promoted `@stable` capture
   path — so the `-cal-nn` Stable-vs-Stable re-baseline is mandatory, not optional.
4. **Role consumption is the safe, high-value half** and cures the flagship
   `ai.yaml:349` artillery/SHORAD-on-the-line defect (LayeredDefence `:52/:56/:90-97`)
   entirely inside `@experimental` — do it first, and pin the classification table
   with an NUnit test before flipping any consumer (the list→query swap is only
   behavior-preserving if the resolver reproduces the old lists).
5. **L2/L3 arbitration is the real behavioral risk in 4d** — the squad FSM's
   75-tick re-fire vs the merged Phase-2/3 executor's ledger claims. The executor
   already ships the seam (`AdjustmentState`/`tacpos:` ledger, N4); Phase 4 is its
   first real consumer, so the deferred event-bus (`260722_phase3_redteam.md:285-294`,
   S7) may finally need pricing here.

## 6. Contradictions with / gaps in the SPEC

- **SPEC §3c vs ai-bench invariant.** "Full migration" of ThreatMapManager
  literally read means changing the shared grid the Normal control depends on,
  which collides with `ai-bench/README.md:36` ("controls stay byte-identical").
  The SPEC's "declared re-baseline" language covers `@stable`, but Normal/Rush/Turtle
  are a *separate* fixed yardstick; the SPEC never addresses them. **Interpretation:
  migrate the living AI (`@experimental`+`@stable`), leave the omniscient grid for
  the pure controls (§2.3 option 1).** Flagging for user ratification.
- **SPEC §3c "sourced from the same per-player intel substrate as 3a"** presumes 3a
  can source value-weighted strategic scoring. It cannot as built
  (`SightingThreatLayer.cs:57-74` — armed-only intensity, no cost). Gap, not a
  contradiction; resolved by the §2.0 value-channel decision.
- **`ThreatMapManager.cs:43` comment ("Per-player threat layers: player index ->
  grid") is factually wrong** — the grids are single shared `float[,]` (`:45-48`).
  Not a SPEC issue, but it would mislead an implementer who trusts the comment; fix
  it in the same pass (per `CLAUDE.md` "fix verifiably-wrong statements on sight").
