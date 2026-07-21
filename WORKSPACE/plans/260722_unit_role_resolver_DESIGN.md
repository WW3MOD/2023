# 260722 — Unit-Role Resolver: design

**Status:** IMPLEMENTED (data-only track) — `UnitRoleResolver` + `AIUnitRole` +
world registration + `AIUnitRole` overrides landed on branch `phase3-resolver`.
Cascade order and worked examples corrected per finding B3 (see §4 banner, §8).
Phase-3 rider of the ratified strategic/tactical split SPEC. Inert (no consumers).
**Mandate:** `DOCS/design/ai-realism.md` §4 (owner, 2026-07-22) — every unit has a
known role, from YAML-facing properties or derived by the engine from the unit's
stats; a one-time computation on first game load, cached; possibly a hybrid.
Roles drive doctrine (artillery at standoff providing suppressive/continuous
fires, not in the main line) and later AoE-aware cluster targeting.
**SPEC anchor:** `260722_strategic_tactical_split_SPEC.md` §7 Phase-3 amendment —
"the unit-role resolver ... behind its own flag, priced separately on the ladder;
the role resolver also cures the ai.yaml:349 artillery/SHORAD-as-mainline
conflation with no operations dependency."
**Architecture seed:** `260722_bot_brain_architecture.md` §4.5 (minimal role
model: one enum, one resolver, one override field). This design refines that
sketch; divergences are listed in §2.4.
**Researched against:** main @ `56e953b7`. All file:line citations verified at
that ref.

---

## 1. Problem — how unit categories are expressed today

There is no capability model. Every bot module answers "which units?" with
hand-maintained YAML type-name lists or bare trait-presence checks
(`260722_bot_brain_architecture.md` §1.5, re-verified):

| Module | Type-list fields (C#) | YAML values | Gate |
|---|---|---|---|
| LayeredDefenceBotModule | `ScreenUnitTypes` (:52), `MainLineUnitTypes` (:56), `ExcludedActorTypes` (:90-97) | ai.yaml:346, **:349**, :350 (stable twin :778-780) | enable-ai-experimental |
| PoiOffensiveBotModule | `ExcludeUnitTypes` (:82); eligibility = `IPositionableInfo` + `AttackBaseInfo` (`IsEligibleCombatUnit` :410-426) | ai.yaml:192 (stable :730) | enable-ai-experimental |
| PoiGarrisonBotModule | `ExcludeUnitTypes` | ai.yaml:233 (stable :749) | enable-ai-experimental |
| CaptureCoordinatorBotModule | `CapturingActorTypes` (:35), `CapturableActorTypes` (:38), `SupportingUnitTypes` (:79) | ai.yaml:132-133 (stable :691-694) | enable-ai-experimental |
| CaptureManagerBotModule (legacy) | `CapturingActorTypes` (:25), `CapturableActorTypes` (:29) | ai.yaml:101-104, :413-416 | enable-ai-legacy-only |
| SquadManagerBotModule | `AirUnitsTypes` (:31), `NavalUnitsTypes` (:25), `ExcludeFromSquadsTypes` (:34), `ConstructionYardTypes` (:43) | ai.yaml:533/:599 (air), :557/:617 (excl.); ai-america.yaml:58/:96/:147, ai-russia.yaml:55/:93/:145 | legacy + experimental twins |
| MountedTransportBotModule | `CarrierTypes` (:44), `PassengerTypes` (:47), `SupplyRouteTypes` (:70) | ai.yaml:376/:395 (stable :369) | enable-ai-stable / -experimental |
| AdaptiveProductionBotModule | `AntiVehicleUnits` (:30), `AntiInfantryUnits` (:33), `AntiAirUnits` (:36) | ai.yaml:289-316 | legacy + experimental |
| ScoutBotModule | `ScoutTypes` (:24) | ai.yaml:248 (humvee), :256 (btr) | enable-ai-any |
| SupplyFollowerBotModule | `SupplyTruckTypes` (:22) | ai.yaml:273 | enable-ai-any |
| GarrisonBotModule | `GarrisonActorTypes` (:22) | unset in ai.yaml | enable-ai-any |
| HelicopterSquadBotModule | none — selects by `AIHelicopterRole` trait presence (:153) | — | legacy + experimental twins |
| UnitBuilder / BaseBuilder / McvManager | production-queue and building types | ai.yaml:421-490, :515-577 | various |

The flagship defect: **`MainLineUnitTypes` (ai.yaml:349)** lists `m109, paladin,
grad, tos, m270` (tube/rocket artillery) and `strykershorad, tunguska` (SHORAD)
and `aa.america, aa.russia` (MANPADS) alongside tanks and heavy infantry. The
defence module slots artillery and AD assets into a front-line standoff slot as
if they were tanks; the offense module marches them in assault axes. Secondary
costs: the same exclusion set (`tecn, e6, truk, bradley, bmp2, m113`) is
copy-pasted across at least seven YAML locations (ai.yaml:192, :233, :350, :557,
:617, :730, :749, :780+), and every new unit is invisible to the AI until
someone remembers every list (silent-rot, architecture doc §1.5).

## 2. Role taxonomy

A single closed enum. Each role earns its place by a distinct doctrinal
behavior at least one consumer needs; anything without a consumer is deferred
(§7).

```csharp
public enum UnitRole
{
    None,               // buildings, husks, dummies, unclassifiable
    MainBattle,         // holds/advances the line (tanks, line infantry, ATGM teams)
    IndirectFire,       // standoff fires: suppressive during assault, continuous bombardment
    ShortRangeAD,       // air-defence overwatch of the force; threat-proportionate purchase
    Recon,              // ahead of the force; screens; Phase-4 stale-intel tasking
    TransportLift,      // ferries infantry; mounted-doctrine executor owns them
    CaptureSpecialist,  // neutral-tech capture tasking + ferry priority (tecn)
    Logistics,          // resupply/repair/medical: follows the force, never line duty
    AttackAir           // sortie-cycle air assets, owned by the air squad modules
}
```

Doctrinal justification per role:

- **MainBattle** — the only role LayeredDefence may put on the line and the
  default body of an assault axis. Exists so everything below can be *excluded*
  from line duty by classification instead of by seven copy-pasted lists.
- **IndirectFire** — the mandate's headline: artillery belongs far away,
  providing suppressive effects during an assault or continuous bombardment.
  The adopted fires behavior cycle consumes this role; LayeredDefence and
  PoiOffensive stop treating these units as line combatants.
- **ShortRangeAD** — AD assets overwatch the main body rather than stand in it;
  the owner's economy note (ai-realism.md §2, "AA proportionate to the actual
  threat") needs the AD population to be countable as a class.
- **Recon** — screens ahead; the Phase-4 scout link ("cheapest fast unit
  periodically tasked toward stale high-value cells", SPEC §7 Phase 4) needs a
  queryable recon class instead of `ScoutTypes: humvee`.
- **TransportLift** — MountedTransportBotModule's carrier pool. Keeping
  carriers out of line duty is already load-bearing today (PITFALL at
  LayeredDefenceBotModule.cs:86-89: LayeredDefence pulling carriers forward
  starves the transport module forever).
- **CaptureSpecialist** — mounted doctrine gives technicians first priority on
  vehicle ferries (ai-realism.md §3); capture dispatch keys on this class.
- **Logistics** — supply trucks, repair engineers, medics. Excluded from every
  combat pool today by name; later the follow-the-force doctrine attaches here.
- **AttackAir** — absorbs `AirUnitsTypes`; marks air assets out of every ground
  pool. Fine-grained air behavior stays on `AIHelicopterRole` (§2.3).

### 2.1 What is deliberately NOT a role

- **Screen vs main line** (LayeredDefence's two lists) is a *positioning*
  partition inside MainBattle, not a capability class — both lists are line
  combatants. The resolver cures the eligibility defect (non-MainBattle units
  drop out of both lists); the screen/line partition itself stays as-is in v1
  (§6.1, open question §8).
- **AntiArmor** (in the architecture doc's sketch) is dropped for v1: no
  shipped behavior consumes it (axis composition is an operations-layer,
  Phase-5 concern), and `at`/ATGM teams behave as line infantry under every
  current consumer. The closed enum makes adding it cheap when a consumer
  exists.
- **Capturable buildings** (`CapturableActorTypes`) are targets, not unit
  capabilities — out of scope.

### 2.2 Prior art built on, not duplicated

- **`AIHelicopterRole`** (engine/OpenRA.Mods.Common/Traits/Air/AIHelicopterRole.cs:16-49):
  an existing enum-typed, YAML-facing role trait (`Scout | AttackLight |
  AttackHeavy | Transport`) with per-role tuning fields, consumed by
  HelicopterSquadBotModule via trait query (:153, :229). This is exactly the
  target pattern, scoped to helicopters. The resolver *maps* it (§4 rule 2)
  rather than replacing it: `AIHelicopterRole` keeps its tuning fields and its
  consumer untouched.
- **`PassengerInfo.GarrisonRole`** (engine/OpenRA.Mods.Common/Traits/Passenger.cs:54):
  a string role hint ("General, MachineGunner, AntiTank, ...") for garrison
  targeting — second precedent for per-actor role metadata, untouched.
- **`TargetableInfo.TargetTypes` / `WeaponInfo.ValidTargets`**
  (Targetable.cs:23; engine/OpenRA.Game/GameRules/WeaponInfo.cs:121-122):
  the engine's existing pseudo-classification (Air/Ground/Vehicle/...). Used as
  a derivation *signal* (§3), not extended — target types answer "what can hit
  this", not "what is this for".
- **`^Template` inheritance does NOT survive to runtime.** MiniYaml `Inherits:`
  is flattened before `ActorInfo` construction (ActorInfo.cs:38-52 receives
  already-merged trait nodes); a resolver cannot ask "does this actor inherit
  ^Artillery". Derivation must read the flattened trait list only. Templates
  remain useful on the *authoring* side: an `AIUnitRole` override placed on a
  shared template annotates every inheritor in one line.

### 2.3 Relationship to `AIHelicopterRole`

Coarse role (this enum) answers "which pool does this unit belong to";
`AIHelicopterRole` keeps answering "how does the heli squad fly it". The trait
class is `AIHelicopterRole`/`AIHelicopterRoleInfo`, but its `Role` field's enum
type is spelled **`HelicopterAIRole`** (AIHelicopterRole.cs:16) — the resolver
switches on that name. Mapping: `Scout → Recon`, `AttackLight/AttackHeavy →
AttackAir`, `Transport → TransportLift`. No consumer of `AIHelicopterRole` changes.

### 2.4 Divergences from the architecture-doc sketch (§4.5)

Sketch: `Recon | MainLine | AntiArmor | Fires | AirDefence | Transport |
Capture`. Changes here: renamed for self-documentation (MainBattle,
IndirectFire, ShortRangeAD, TransportLift, CaptureSpecialist); **added
Logistics** (every exclusion list in §1 contains `truk`/`e6`, and
SupplyFollower/medic behaviors need the class); **added AttackAir** (absorbs
`AirUnitsTypes`, closes the taxonomy over the whole roster); **dropped
AntiArmor** (§2.1); **added None** (explicit unclassified value — total
function over all ActorInfos, no null handling at call sites).

## 3. Derivation signals (verified inventory)

All available on `ActorInfo` at load time via `TraitInfoOrDefault<T>()` /
`TraitInfos<T>()`; weapon data resolved because `ArmamentInfo.RulesetLoaded`
caches `WeaponInfo` from the weapon name during Ruleset construction
(engine/OpenRA.Mods.Common/Traits/Armament.cs:114-131).

| Signal | Where | Role evidence in WW3MOD data |
|---|---|---|
| `AircraftInfo` | Traits/Air/Aircraft.cs:22 | air vs ground split |
| `AIHelicopterRoleInfo.Role` | Air/AIHelicopterRole.cs:22 | authored heli roles |
| `AttackBaseInfo.Armaments` | Traits/Attack/AttackBase.cs:14-17 | armed vs unarmed |
| `WeaponInfo.Range` / `MinRange` | GameRules/WeaponInfo.cs:76-77 | arty 28c0-40c0 w/ MinRange 5c0-12c0 (ArtilleryRound.Paladin 40c0/10c0, GradRockets 40c0/12c0, M270Rockets 40c0/12c0, TosRockets 28c0/5c0); direct fire ≤ ~25c0-30c0, MinRange absent or ≤1c512 (e2 GrenadeLauncher) |
| `WeaponInfo.ValidTargets` | WeaponInfo.cs:121-122 | AA weapons (Stinger.quad, 9M311, 30mm.Tunguska.AA) are the only ground-unit weapons with `Air`; no false positives found |
| `SpreadDamageWarhead.Spread` | Warheads/SpreadDamageWarhead.cs:25 | ALL artillery warheads have Spread (64-100) — but tank rounds also carry Spread 64, so AoE is a *cluster-targeting* signal (queued item), not a role discriminator |
| `MobileInfo.Speed` / `Locomotor` | Traits/Mobile.cs:31,41 | scouts 110-150 (btr 110, humvee 150) vs MBTs 90-100 — separator exists but is brittle (grad = 110); used only as a last-resort tiebreak, see §4 rule 8 |
| `CargoInfo.MaxWeight/Types` | Traits/Cargo.cs:24-33 | bradley/bmp2/m113 carriers |
| `PassengerInfo` | Traits/Passenger.cs:22 | ride-eligible infantry |
| `CapturesInfo.CaptureTypes` | Traits/Captures.cs:21-25 | tecn inherits `^CapturesNeutralBuildings` (`CaptureTypes: building-neutral`, `ConsumedByCapture: true`, infantry.yaml:897-906, inherited :2164); combat infantry carry `^CapturesOccupiedBuildings` (`building-occupied`, :885-896) — capture-type value, not trait presence, is the discriminator |
| `SupplyProvider` | on TRUK, vehicles.yaml:541-548 | logistics (unarmed, TotalSupply/Range/RestockActors) |
| medic heal | heal-type armament, ally-targeted (infantry.yaml ~:2127-2157) | lethal-vs-support armament split |
| `AmmoPoolInfo` | Traits/AmmoPool.cs:20-35 | not a role signal; stays a runtime *state* filter (`SkipOutOfAmmoUnits`, LayeredDefenceBotModule.cs:102) |
| `DetectCloakedInfo.Range` | Traits/DetectCloaked.cs:18-23 | weak — e6 (repair) has `DetectCloaked@Mine`; not used in v1 |
| `GrantConditionOnBotOwner` | Conditions/GrantConditionOnBotOwner.cs:46 | not a derivation signal; the per-unit bot-gating idiom available to consumers (conventions.md) |

## 4. Derivation rules — deterministic priority order

First match wins. Evaluated per `ActorInfo` over the flattened trait list.
Thresholds are YAML fields on the resolver (§6 governance), defaults below.

> **CASCADE ORDER CORRECTED (finding B3, `260722_phase3_redteam.md`).** The
> original draft tested `TransportLift` (Cargo) *before* ShortRangeAD /
> IndirectFire / Recon. Against the real YAML every fast-light and mobile-AD
> hull also carries `Cargo` (humvee 8, btr 8, strykershorad 9), so that order
> collapsed them all into TransportLift — ground Recon became empty and
> ShortRangeAD lost its only mobile American member. The reordered cascade below
> tests all combat/specialist roles first and reaches Cargo→TransportLift only
> as the fall-through for genuine carriers. Implemented in
> `UnitRoleResolver.Classify`.

1. **Explicit override.** `AIUnitRoleInfo` present → its `Role`. Absolute;
   hybrid model per the mandate.
2. **Air.** `AircraftInfo` present → map `AIHelicopterRoleInfo.Role` (enum type
   `HelicopterAIRole`, §2.3) if present; else armed → `AttackAir`; else
   `CargoInfo` → `TransportLift`; else `None`.
3. **CaptureSpecialist.** Any `CapturesInfo` whose `CaptureTypes` contains
   `building-neutral` (`NeutralCaptureType`, YAML-tunable). Ordered before
   Logistics so the tecn does not fall into the support bucket, and before the
   combat roles so a capturer that also has a token weapon is still a capturer.
4. **Logistics.** `SupplyProvider` present; OR at least one armament exists and
   **no** armament targets enemies (heal/repair-only — medi's `Heal` armament
   is `TargetRelationships: Ally`). Covers truk (supply) and medi (heal-only).
   The engineer **e6 carries a lethal MP5 (targets enemies), so derivation does
   NOT catch it — it is pinned `Logistics` by an `AIUnitRole` override** (§6.2).
   Note: this rule was narrowed from the original draft, which also swept in any
   "unarmed with Passenger/Mobile" actor — that mislabels radar/minelayer/MCV
   hulls (MSAR/MNLY/LCCV) as Logistics; they now fall through to None (§7).
5. **ShortRangeAD.** `MobileInfo` present AND any armament whose
   `WeaponInfo.ValidTargets` contains **`Air`** (not `Helicopter` — machine guns
   list `Helicopter` and must not count; only Stinger/9M311/MANPAD-class weapons
   list `Air`). The Mobile guard keeps air-defence *structures* out of the
   maneuver taxonomy (they fall to None). Catches strykershorad (Stinger.quad),
   tunguska (9M311), MANPAD infantry — even though strykershorad also has Cargo.
6. **IndirectFire.** Any armament whose weapon has `MinRange ≥ IndirectMinRange`
   (default `4c0`) OR `Range ≥ IndirectRangeFloor` (default `35c0`). `^ArtilleryRound`
   is `Range 40c0 / MinRange 10c0` (weapons-ballistics.yaml:613-614), inherited
   unchanged by Paladin/Giatsint; grad/m270 `40c0/12c0`; tos `28c0/5c0` (MinRange
   clause alone); mt's `60mm_Mortar` `25c0/8c0`. Rejects e2's grenade launcher
   (MinRange 1c512) and all direct-fire tank guns (`^TankRound` 24c0/MinRange 1c512).
7. **Recon.** `MobileInfo.Speed ≥ ReconSpeedFloor` (default `110`) AND armed
   with at least one weapon AND **every** weapon `Range ≤ ReconMaxWeaponRange`
   (default `16c0`). Catches humvee (150, MG 15c0) and btr (110, MG 16c0). grad
   (speed 110) is already IndirectFire by rule 6, so rule order — not the speed
   margin — is what keeps it out of Recon. Still the most brittle predicate;
   `AIUnitRole: Recon` overrides remain the belt-and-braces for new units (§8 Q3).
8. **TransportLift.** `CargoInfo` with `MaxWeight > 0`, reached only after every
   combat/specialist role above declined. This is the finding-B3 fix: carriers
   are the fall-through, not the pre-empt. Catches m113 (Cargo 12, slow, MG-only);
   the armed IFVs bradley/bmp2 are pinned `MainBattle` by override (§6.2) so they
   join the line of battle rather than the ferry pool.
9. **MainBattle.** Armed (`ArmamentInfo` present) + `MobileInfo` present. Tank
   hulls (abrams/t90 — direct-fire main gun) land here.
10. **None.** Everything else (buildings, husks, dummy actors, unarmed
    non-mobile support such as MSAR/MNLY/LCCV and air-defence structures).

Determinism: the computation is a pure function of the ruleset — no randomness,
no per-player state, no tick-time input — so it is identical on every client by
construction (SPEC §5 satisfied trivially). Iteration order of
`Rules.Actors` never affects any per-actor result.

## 5. Caching shape

**Site: a data-only world trait, computed once per world in `IWorldLoaded`.**

```csharp
[TraitLocation(SystemActors.World)]
public class UnitRoleResolverInfo : TraitInfo
{
    public readonly WDist IndirectMinRange = WDist.FromCells(4);
    public readonly WDist IndirectRangeFloor = new WDist(35 * 1024);
    public readonly int ReconSpeedFloor = 110;
    public readonly WDist ReconMaxWeaponRange = new WDist(16 * 1024);
    public readonly string NeutralCaptureType = "building-neutral";
    // Create() => new UnitRoleResolver(this)
}

public class UnitRoleResolver : IWorldLoaded
{
    // WorldLoaded: one pass over w.Map.Rules.Actors -> roles[actorInfo.Name]
    public UnitRole GetRole(ActorInfo info);          // O(1); None for unknown
    public UnitRole GetRole(Actor a);                 // GetRole(a.Info)
    public IReadOnlyCollection<string> NamesWithRole(UnitRole role);
}
```

The override trait (§6.2):

```csharp
[Desc("Overrides the AI role derived by UnitRoleResolver for this actor.")]
public class AIUnitRoleInfo : TraitInfo
{
    public readonly UnitRole Role = UnitRole.None;
}
```

Why `IWorldLoaded`, not `IRulesetLoaded`:

- **Ordering safety.** The Ruleset constructor invokes `RulesetLoaded` per
  actor per trait info (engine/OpenRA.Game/GameRules/Ruleset.cs:49-62) with no
  cross-actor ordering guarantee; a resolver running there could read an
  `ArmamentInfo` whose `WeaponInfo` is not yet resolved (Armament.cs:114-131).
  At `WorldLoaded` the ruleset is fully constructed — every weapon reference is
  resolved.
- **Map-rule correctness.** Maps may carry rule overrides
  (`Map.RuleDefinitions`, Map.cs:176); a per-mod cache would be stale on such
  maps. Per-world computation is always computed against the rules actually in
  effect.
- **Established pattern.** World traits building lookups at load are the house
  idiom (BuildingInfluence's CellLayer, Buildings/BuildingInfluence.cs:24-39;
  PoiMap/InfluenceMap). One pass over a few hundred ActorInfos is microseconds;
  the owner's "one-time computation on first game load, cached" is satisfied —
  computed once at load, O(1) dictionary reads thereafter, never recomputed
  during play.

Consumers resolve it once in their own init (`world.WorldActor.Trait<UnitRoleResolver>()`)
— the same acquisition pattern modules already use for PoiMap/InfluenceMap.

Validation: `Role`-typed YAML fields get **parse-time validation for free** —
FieldLoader rejects an unknown enum value as a load error, so no dedicated lint
pass is needed for role names. A lint pass (`ILintRulesPass`, pattern:
Lint/CheckActorReferences.cs:26-36) is worth adding only for *semantic* checks:
warn when an `AIUnitRole` override equals the derived role (dead YAML), and
optionally report the full derived-role table under `make test` for eyeball
review.

## 6. Consumer migration map

Governing rule (architecture.md:314-316, the `CohesionSwitchEnabled`
precedent): **any Info field added to a trait shared across profiles must
default to frozen-baseline behavior and be opted in per-profile via YAML.**
Concretely:

- The `UnitRoleResolver` world trait is pure data — it issues no orders, ticks
  nothing, and mutates no sim state. Registering it is behavior-inert for every
  profile; @stable stays byte-identical.
- Each migrated module gets `UseUnitRoles = false` (code default = frozen
  baseline). With it false, the existing type-list path runs untouched. It is
  flipped `true` only on `@experimental` YAML instances, after the change is
  priced on the ladder. `@stable`/`@normal`/legacy YAML and behavior stay
  byte-identical until a deliberate, declared promotion (SPEC §6).
- `AIUnitRole` YAML annotations on unit actors are read by nobody until a
  consumer opts in — adding them is likewise inert.

| Consumer (list) | Role-based read | Tier |
|---|---|---|
| LayeredDefence `ScreenUnitTypes`/`MainLineUnitTypes` (ai.yaml:346/:349) | Eligibility filter: only `MainBattle` may enter either layer — artillery, SHORAD, MANPADS drop out of :349 (the defect cure). Screen/line partition unchanged in v1 (§2.1). IndirectFire units freed for the fires cycle's standoff executor | experimental |
| LayeredDefence `ExcludedActorTypes` (:350) | Derived: exclude role ∉ {MainBattle} — subsumes tecn/e6/truk/scouts/carriers by classification | experimental |
| PoiOffensive `ExcludeUnitTypes` (ai.yaml:192) | Free-pool filter: role == `MainBattle` (excludes CaptureSpecialist/Logistics/TransportLift by class; IndirectFire excluded from *axis line-up* once the fires executor exists to receive them — until then IndirectFire stays eligible to avoid orphaning artillery) | experimental |
| PoiGarrison `ExcludeUnitTypes` (:233) | Same filter as PoiOffensive | experimental |
| CaptureCoordinator `CapturingActorTypes` (:132) | role == `CaptureSpecialist` | experimental |
| MountedTransport `CarrierTypes`/`PassengerTypes` (:376/:395) | Carriers: role == `TransportLift`; passengers: `PassengerInfo` present ∧ role ∈ {MainBattle, CaptureSpecialist} | experimental + stable twin (stable stays on lists) |
| SquadManager `AirUnitsTypes` (:533/:599) / `ExcludeFromSquadsTypes` (:557/:617) | role == `AttackAir` for recruit; excludes by role ∉ combat set | experimental twins only; legacy frozen |
| Scout `ScoutTypes` (:248/:256) | role == `Recon` | enable-ai-any — migrate LAST; shared across all profiles, so its `UseUnitRoles` flip is itself a priced change |
| SupplyFollower `SupplyTruckTypes` (:273) | role == `Logistics` ∧ `SupplyProvider` present | enable-ai-any — same caution as Scout |
| AdaptiveProduction `AntiAirUnits` (:289-316) | role == `ShortRangeAD` ∪ AA infantry. `AntiVehicleUnits`/`AntiInfantryUnits` need finer-than-role data — **not migrated** in v1 | partial, experimental |
| CaptureManager (legacy), BaseBuilder, McvManager, UnitBuilder | **Not migrated** — legacy profiles are frozen controls; building/production types are not unit roles | — |
| HelicopterSquad | Already role-driven via `AIHelicopterRole` — no change | — |

Migration order inside Phase 3: resolver + `AIUnitRole` trait + lint report
land first (inert); LayeredDefence eligibility is the first behavioral opt-in
(it is the named defect and the cheapest to price); the fires cycle then
consumes `IndirectFire`; remaining experimental modules follow; `enable-ai-any`
modules (Scout, SupplyFollower) last, each as its own priced change.

## 7. Non-goals (first cut)

- **AoE-aware cluster targeting** (mandate bullet 3) — a SEPARATE queued work
  item on the shared AutoTarget path (default-off, benchmark-priced,
  re-baseline-class if shipped to everyone, per SPEC §6). This design only
  guarantees the signal (`SpreadDamageWarhead.Spread`) is inventoried.
- **Dismount tactics / mounted-infantry context** (ai-realism.md §3) — later
  layer; TransportLift only marks the pool.
- **AntiArmor role** and axis composition by role — operations-layer (Phase-5)
  consumers; enum extension when needed.
- **Production steering by role** (role-aware `UnitsToBuild`) — separate
  concern; the demand-queue pattern (architecture.md:298-312) is the seam.
- **Screen/line partition redesign** inside MainBattle (§8 Q2).
- **Migrating frozen profiles** (legacy, @stable) — by definition out of scope
  until promotion events.

## 8. Open questions

1. **Medic placement.** `medi` is in `ScreenUnitTypes` today (ai.yaml:346) but
   derives as `Logistics`. Under the migrated filter it leaves the screen.
   Options: accept (medics rejoin via a later Logistics-follow behavior);
   or annotate `AIUnitRole: MainBattle` on medi as a transitional pin. Leaning:
   accept, and let the fires/support cycle bring medics back deliberately —
   but this changes experimental behavior and should be called out when priced.
2. **Mortar teams (`mt`).** CORRECTION: the earlier draft claimed mt derives
   `MainBattle` (weapon range ~12c0, MinRange below threshold). Verified wrong —
   mt's `60mm_Mortar` is `Range 25c0 / MinRange 8c0` (infantry.yaml:1508,
   weapons-ballistics.yaml:522-527), so it **derives `IndirectFire`** by rule 6
   (MinRange 8c0 ≥ 4c0), with no override. This matches its doctrine (light
   indirect fire) and is the correct outcome; no open question remains.
3. **Recon predicate brittleness.** Rule 7 rests on a speed threshold with a
   narrow margin (btr 110 vs grad 110). After the finding-B3 reorder, grad is
   caught by IndirectFire (rule 6) *before* Recon, so rule order — not the speed
   margin — is what separates them, and humvee/btr derive `Recon` correctly
   without overrides. The margin is still thin for future units; ship
   `AIUnitRole: Recon` overrides if a new fast-light unit needs pinning — the
   derivation rule remains as the safety net for new units.
4. **`SupportingUnitTypes`** (CaptureCoordinator :79, escort pool) — escorts
   are "any idle friendly" when unset; should escort selection prefer
   MainBattle by role, or is that an operations-layer composition question?
5. **Naval.** `NavalUnitsTypes` (SquadManagerBotModule.cs:25) is unused in
   WW3MOD ai.yaml; taxonomy has no naval role. Assumed dead — confirm before
   treating the enum as roster-complete.
6. **Where the resolver trait registers.** `world.yaml` (all profiles, inert)
   vs ai.yaml world block. Leaning `world.yaml` next to other world traits —
   data-only, and consumers in any profile can find it; but ai.yaml keeps AI
   machinery in one place. Cosmetic; decide at implementation.

## 9. SPEC consistency check

- SPEC §7 Phase-3 amendment language ("derive-from-traits + YAML `AiUnitRole`
  override ... behind its own flag, priced separately") — this design matches:
  hybrid derivation+override (§4 rule 1), per-module `UseUnitRoles` flags (§6),
  ladder pricing per migration step. The trait is spelled `AIUnitRole` here for
  consistency with the in-repo `AIHelicopterRole` naming.
- The `ai.yaml:349` citation in SPEC/architecture doc resolves to
  `mods/ww3mod/rules/ai/ai.yaml:349` and still holds at `56e953b7`.
- Architecture doc §1.5 cites `MainLineUnitTypes` at LayeredDefenceBotModule.cs:56
  (field) — matches; some earlier notes cite :54-59, which is the field plus its
  Desc block. No substantive inconsistency found in either document.
- One correction to the architecture doc's sketch (§4.5): it derives Capture
  from "`CaptureManager` + capture types" — trait *presence* is insufficient in
  WW3MOD because line infantry also carry `Captures@OCCUPIED`
  (infantry.yaml:885-896); the discriminator must be the `building-neutral`
  capture type (§4 rule 3).
