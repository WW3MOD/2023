# RECON — Lever 1: contested-capture prioritisation design (260729)

Read against `main` @ **541be058**. Read-only code trace + design. No gameplay
runs, builds, or edits to `.cs`/rules YAML were performed. This turns lever 1 of
`WORKSPACE/recon/260729-exp-deficit-attribution.md` §6 ("contested-capture
prioritisation in the strategic layer") from a one-liner into a decision-ready
design. Direction remains the user's call; §5 is the agent's recommendation.

Register: impersonal. Every claim is tagged **MEASURED** (off a surviving ladder
artifact, via the attribution doc), **CODE-READ** (traced to file:line at 541be058),
or **HYPOTHESIS** (a reading the evidence suggests but does not prove).

Pre-read: `supply-route.md`, `game-model.md`, the attribution recon, and
`influence-stack.md` (invariants). The SR framing trap below is why the first two matter.

---

## 0. Headline

**CODE-READ — the two capture modules are configured near-identically, so the
measured 1.9× contested-capture deficit almost certainly does NOT originate in
capture *ordering*.** `CaptureCoordinatorBotModule@experimental.tecn`
(`ai.yaml:131-185`) and `@stable.tecn` (`ai.yaml:830-858`) share every scoring,
escort, defense, and floor knob. During the re-baseline that measured the deficit,
the one experimental-only *ordering* lever — `StrategicCaptureRepointEnabled`
(item 24) — was held **OFF** (the very flip sitting uncommitted in the shared tree,
`ai.yaml:180` true→false; and `PoiMap.GetCaptureTargets` damps high-threat targets
**identically** for both profiles, `PoiMap.cs:495-497`). Ordering parity therefore
held during the deficit.

**HYPOTHESIS — the deficit lives in capture *execution* (who wins the hold), not
selection.** Two code-read execution differences survive as candidates: (a) fixed
`EscortSize: 2` (`ai.yaml:158/:849`) is not contest-aware, so a contested derrick
gets the same two escorts as an uncontested one — the module *damps* contested
targets in the unused legacy path (`SafetyMultiplierHostile: 10`,
`CaptureCoordinatorBotModule.cs:778-784`) and does nothing extra in the live PoiMap
path; (b) experimental **ferries** distant captures (`UseTransportForDistantCaptures:
true`, `ai.yaml:172`) while stable walks them — an extra reserve/drive/unload/re-issue
chain (`CaptureCoordinatorBotModule.cs:683-697`) with more failure points on a
contested approach. Both are experimental-only and were **live** during the measured
deficit.

**Consequence for lever 1:** a pure "boost the contested target's ordering score"
change may not move the number, because ordering was already at parity. The
options in §4 therefore target the **hold** (escort sizing, defense pre-summon,
re-dispatch-on-loss), and §5 recommends landing lever-4 diagnostic logging first to
confirm the failure is "held shorter" vs "captured later" before spending a
gameplay lever.

---

## 1. Framing correction — the capturable is an income derrick, NOT a Supply Route

**CODE-READ + doc.** The attribution doc and the lever menu say "contested SR/tecn
seeds." Mechanically the contested, income-producing capturable is a **neutral
income structure** — `oilb`/`bio`/`fcom` (derricks with `CashTrickler`), listed in
`CapturableActorTypes: oilb,bio,miss,fcom,hosp,logisticscenter` (`ai.yaml:140/:833`).
The **Supply Route is not capturable at all**: `SUPPLYROUTE` carries no
`Capturable`/`CaptureManager` (`supply-route.md:65-72`), and `PoiMap.TryScore`
routes an SR to `Pressure`/`DenyCapture` with no `CaptureManager` for a TECN to
enter (`PoiMap.cs:467-474`). The `capture_income` that decides the ladder
(attribution §2.2, a held asset accrues $33k–$64k) is **derrick `CashTrickler`
income**, not anything SR-related.

This matters for the design: "contested-capture prioritisation" = **race for and
hold a neutral derrick that both bots want**, resolved by TECN dispatch + escort +
defense against re-capture/destruction. It is NOT about SR placement, SR capture, or
SR contestation. (The recurring `supply-route.md` trap: do not design an
"expand-by-capturing-SR" behaviour here — that is not what generates the income
gap.)

---

## 2. The current path — how @experimental decides to contest/capture

### 2.1 Module + gates (CODE-READ)
The live capture brain is `CaptureCoordinatorBotModule@experimental.tecn`
(`ai.yaml:131`, `RequiresCondition: enable-ai-experimental`). Engine class
`CaptureCoordinatorBotModule.cs`. The legacy `CaptureManagerBotModule@tecn`
(`ai.yaml:101`) is gated `enable-ai-legacy-only` and does not fire for either
profile. `@stable` runs the byte-frozen twin `@stable.tecn` (`ai.yaml:830`).

### 2.2 Target ordering (CODE-READ)
Per scan (`ScanInterval: 75`), `QueueCaptureOrders` (`:308`) resolves `PoiMap`
(`:382-386`) and, because it is always present, delegates to
`QueueCaptureOrdersFromPoiMap` (`:604-652`). Ordering comes from
`OrderedCaptureTargets` (`:557-571`) → `PoiMap.GetCaptureTargets(player)`
(`PoiMap.cs:266-269`), which filters `GetScoredPois` to `Capture`/`DenyCapture`
actions. The per-target score is `value × distFactor × threatFactor × ownershipMul`
(`PoiMap.cs:508-513`), where `value` = the derrick's income weight
(`IncomeWeights` OILB 50 / FCOM 100 / BIO 150, `ai.yaml:146-151/:837-842`) and
`threatFactor` **damps** high enemy-influence targets (`PoiScoring.ThreatFactor`,
`PoiMap.cs:496-497`).

- **Ordering parity, the load-bearing fact.** With `StrategicCaptureRepointEnabled:
  false` (deficit condition), `OrderedCaptureTargets` returns
  `poiMap.GetCaptureTargets(player)` **verbatim** (`:566-568`) — the same omniscient
  `threatFactor` damp both profiles use. When ON (`ai.yaml:180`, uncommitted flip
  restores false), it instead asks for a threat-neutral base and re-applies a
  believed-danger **damp** (`RescaleCaptureByBelievedDanger`, `:578-597`) —
  *still a damp*, i.e. it steers capturers **away** from danger, not toward
  contested targets. Neither mode boosts a contested derrick.
- The unused legacy `ScoreTarget` (`:760-788`) has a `SafetyMultiplier{Safe/Mild/
  Hostile}` (`:778-784`) that would damp contested targets — but it only runs when
  `PoiMap == null`, which never happens in ladder play. So the safety damp is inert
  today; noted because it shows the module's design instinct is to **avoid**
  contest, not win it.

### 2.3 Dispatch, commitment, escort (CODE-READ)
`QueueCaptureOrdersFromPoiMap` walks the ranking and assigns the **nearest** free,
uncommitted, able TECN to each target (`:631-651`), then `IssueCaptureOrder`
(`:656-679`): queues `CaptureActor` (or a ferry, §2.5), commits the TECN in the
shared `PoiGoalGuard` ledger (`:666-667`, `DefaultCommitmentTicks: 600`,
`ai.yaml:126`) so its order is not overwritten mid-walk, and fires
`DispatchEscort` (`:796-812`). Escort = `FindIdleSupportersNear(…, Info.EscortSize,
…)` (`:801`) → up to **`EscortSize: 2`** (`ai.yaml:158/:849`) idle armed friendlies
within `SupportRecruitRadiusCells: 40`, `AttackMove`d to the target. **`EscortSize`
is a fixed constant — the same 2 whether the derrick is uncontested or under enemy
assault.**

### 2.4 Hold / defense pass (CODE-READ)
`QueueDefenseOrders` (`:818-869`, every `DefenseScanInterval: 150`): for each owned
capturable, if enemy sell-value within `DefenseEnemyScanRadiusCells: 12` exceeds
`DefenseEnemyValueThreshold: 200` **and** exceeds friendly value within
`DefenseFriendlyScanRadiusCells: 6`, summon `DefenseSummonCount: 3` defenders. This
is the only "hold the derrick" mechanism, and its parameters are **identical**
across `@experimental`/`@stable` (`ai.yaml:160-164/:851-855`). It is **reactive**
(fires after the enemy value is already on-site) and value-gated, so a light,
persistent re-capture probe below $200 never triggers it.

### 2.5 Experimental-only deltas vs @stable (CODE-READ — this is the whole difference)
Diffing the two YAML blocks, `@experimental.tecn` carries three fields `@stable.tecn`
lacks:

| Flag (`ai.yaml`) | Effect | Live during deficit? |
|---|---|---|
| `UseUnitRoles: true` (:136) | Rebuild capturer pool from `UnitRole.CaptureSpecialist` instead of the name list. Comment (`:134-135`, `CaptureCoordinatorBotModule.cs:112-116`) states **same TECN set for the current roster** → behaviourally inert today. | Yes, but inert |
| `UseTransportForDistantCaptures: true` (:172, `TransportCaptureMinDistanceCells: 12`) | For targets ≥12 cells, request a mounted ferry from the experimental `MountedTransportBotModule` twin instead of walking (`CaptureCoordinatorBotModule.cs:662/:683-697`). Extra reserve→drive→unload→re-issue chain. | **Yes — active** |
| `StrategicCaptureRepointEnabled` block (:180-185) | Believed-danger **damp** on ordering (§2.2). | **No — forced OFF** for the re-baseline |

All other knobs (income weights, distance half-life, escort size, defense params,
`TecnFloor: 1`) are equal. `TecnFloor: 1` (`:168/:858`) keeps ≥1 capturer
alive-or-pending via production pull (`MaintainTecnFloor`, `:477-502`) — parity, so
not a differentiator.

**Net:** during the measured deficit the only *active* experimental-only capture
difference was **ferrying distant captures**. Everything else was either inert
(`UseUnitRoles`) or off (`StrategicCaptureRepointEnabled`).

---

## 3. Why @stable wins the contest — hypotheses (labelled)

- **MEASURED (attribution §2.4):** on contested seeds 6017/8017, stable extracts
  ~1.9× experimental's derrick `capture_income`; a stable-vs-stable control on the
  same seeds splits ~1.00× — so the map does not favour a slot; it is a treatment
  effect on the experimental profile.
- **MEASURED/INFERRED (attribution §5, one-sided):** on seed 6017 the `[exp-capture]`
  markers show stable at `committed=2` by tick ~1047 vs experimental `committed=1`
  by ~1146 — stable commits **more capturers, earlier**. One-sided instrumentation
  (only the experimental trait logs), single seed.
- **HYPOTHESIS H1 (hold, not race):** experimental captures at parity but **holds
  shorter** — its fixed 2-escort dispatch (§2.3) plus reactive $200-gated defense
  (§2.4) lets stable re-capture/deny the derrick, or item-26/28 forest combat
  (attribution §3, untestable) trades the escort away. Predicts: capture ticks
  ~equal, income-per-held-tick lower.
- **HYPOTHESIS H2 (race, ferry fragility):** `UseTransportForDistantCaptures`
  arrives the TECN **later** or **loses it** on contested seeds (carrier intercepted,
  extra hops) — the §5 timing gap. Predicts: experimental capture tick strictly
  later; disabling ferry closes part of the gap. Distinguishable from H1 only with
  two-sided capture-tick logging.
- **HYPOTHESIS H3 (supporting army divergence):** the experimental-only Stage E/F
  layers (`StrategicRepointEnabled` offense ON, ground danger-nav) route the general
  army's `Secure` axis off the contested derrick differently than stable's frozen
  offense, so fewer bodies are near the derrick to protect/deny. Predicts: fewer
  friendly units within the derrick neighbourhood at capture time. Outside the
  capture module; would need a strategic-layer, not capture-layer, fix.

H1 and H2 are the code-supported, in-scope-for-lever-1 candidates. H3 is plausible
but points at the offense layer (a different lever). Lever-4 logging (§4.D) is the
cheap disambiguator.

---

## 4. Design options

All options are gated so the `@stable` twin and Normal/Rush/Turtle stay
byte-identical. Gating is easy here: `CaptureCoordinatorBotModule@experimental.tecn`
is a **separate trait instance** from `@stable.tecn`, so a new field defaulting
off/inert on the engine class, set only on the experimental block, is sufficient
(the "per-profile instance" pattern, `influence-stack.md` Invariants). No new
`SharedRandom` draws are introduced by any option (the existing `TraitEnabled`
stagger uses unsynced `world.LocalRandom`, `CaptureCoordinatorBotModule.cs:242-243`
— cosmetic, sim-legal, unchanged).

### Option A — contest-aware escort sizing + defense pre-summon (size **S**)
**Targets H1 (hold).** Make escort/defense contest-aware instead of the flat 2/3.
When a capture target reads **contested** — believed-enemy or gray control in its
neighbourhood via `ControlField.OwnerAt` / a believed-danger reading via
`DangerFieldLayer.GroundDanger(player, cell)` (both already resolved for
`@experimental`) — dispatch a larger escort (`ContestedEscortSize`, e.g. 4) and let
the defense pass pre-summon *before* the enemy value crosses $200 (lower the
threshold for contested-and-recently-captured derricks).
- **Files/traits:** `CaptureCoordinatorBotModule.cs` (`DispatchEscort`/
  `QueueDefenseOrders` read a contest flag; new `Info` fields
  `ContestedEscortSize`, `ContestedDefenseEnemyValueThreshold`), `ai.yaml`
  experimental block only.
- **Size:** S — two new fields, one contest predicate, reuse of the already-resolved
  `dangerField`/control field.
- **Invariant risk:** low. Zero RNG; reads existing fields; default new fields to the
  current constants (4→2, threshold unchanged) so an un-tuned build is byte-identical
  to today and `@stable` never sees the fields.
- **A/B measure:** re-run the paired `rebase_armA/armB_{s1,s2}` rungs; primary metric
  = contested-seed (6017/8017) `capture_income` ratio Stable÷Exp, target ↓ from ~1.9×
  toward the ~1.0× control (attribution §2.4); guardrail = no-capture seeds' combat
  net-swing not worse (SPEC §6.4 composite gate, median-with-margin §6).
- **Logging-first?** Helpful but not required — A is cheap enough to A/B directly;
  logging (§4.D) confirms it works via the "held longer" signal.

### Option B — contested re-dispatch-on-loss + target stickiness (size **M**)
**Targets H1 (hold) + the §5 "stable re-commits" reading.** Today a derrick that
flips back to neutral (re-captured/denied) re-enters the ranking like any fresh
target and competes on income-weight order; a TECN killed en route frees its
commitment (`ReconcileGuardCommitments`, `:718-758`) but nothing prioritises
**re-taking a derrick we just lost**. Add: (1) a short-lived "recently ours / just
lost" boost so a contested derrick we held is re-dispatched **first** next scan; (2)
shorten `captureScanCountdown` on a *capturer death that was committed to a
contested target* (the killed-handler already zeroes it for any capturer death,
`:924-938` — narrow/strengthen for contested).
- **Files/traits:** `CaptureCoordinatorBotModule.cs` (a small per-target "lost-tick"
  memory + ordering nudge in `OrderedCaptureTargets`; refine the killed-handler),
  `ai.yaml` experimental block (`RecaptureBoostTicks`, `RecaptureBoostMultiplier`).
- **Size:** M — new per-target state + an ordering post-multiply; must not perturb
  `PoiScoring.CompareForOrder` tie-breaks (reuse the same comparator, `:594-595`).
- **Invariant risk:** low–med. Zero RNG; the state is experimental-instance-local.
  Risk is subtle ordering churn — mitigate by applying the boost as a bounded
  multiplier and keeping the deterministic comparator.
- **A/B measure:** same rungs; metric = contested-seed income ratio **and** number of
  distinct capture events per contested derrick (needs §4.D logging to read cleanly).
- **Logging-first?** **Yes** — B's mechanism (re-take faster) is only verifiable with
  two-sided capture-tick + income-timeseries logging; ship §4.D first.

### Option C — contested-capture race speed: earlier commit + ferry policy (size **M**)
**Targets H2 (race) + the §5 timing gap.** Two sub-levers: (1) on a **contested**
target, drop the effective scan latency (fire a capture dispatch immediately rather
than waiting up to `ScanInterval: 75`, mirroring the death-triggered
`captureScanCountdown = 0`), and (2) make ferrying contest-aware — either prefer a
ferry to arrive a contested derrick *earlier*, or **fall back to on-foot** when the
carrier route is itself contested (test whether ferrying is helping or hurting, H2).
- **Files/traits:** `CaptureCoordinatorBotModule.cs` (contest-triggered scan reset;
  a `FerryContestPolicy` gate around `TryFerryCapture`, `:683-697`), `ai.yaml`
  experimental block.
- **Size:** M — touches the ferry handoff and the scan cadence; interacts with the
  `MountedTransportBotModule` twin (verify no double-commit against the goal-guard).
- **Invariant risk:** med. Zero RNG, but the ferry path crosses module boundaries
  (`TryReserveCaptureFerry`) — highest integration surface of the three; regression
  risk to the existing distant-capture behaviour.
- **A/B measure:** same rungs; primary = experimental capture **tick** on 6017/8017
  (must move earlier) and the income ratio; A/B **ferry-on vs ferry-off** as its own
  arm to settle whether `UseTransportForDistantCaptures` is a net negative.
- **Logging-first?** **Yes, mandatory** — C is a race-timing fix and cannot be
  evaluated without two-sided capture-tick logging (§4.D); shipping C blind risks
  "fixed" being unmeasurable.

### 4.D — Lever-4 diagnostic logging (de-risking pre-step, size **S**, not a gameplay change)
From attribution §3: add (1) **two-sided capture instrumentation** — commit tick,
capturer count, target derrick id for **both** bots (today only the experimental
trait logs, `[exp-capture]`), and (2) **capture-income-per-tick timeseries** per
player. This is the single artifact that separates H1 ("held shorter") from H2
("captured later"), which in turn selects between Option A/B (hold) and Option C
(race). Cost: logging only + one gated S2 rung. It touches diagnostics, not
behaviour, so it is invariant-safe and can land independently.

---

## 5. Recommendation (agent's opinion — direction is the user's call)

**Ship 4.D logging first, then Option A, holding B/C in reserve.**

1. **4.D (logging) — do this first, unconditionally.** It is cheap, invariant-safe,
   and it is the only thing that tells us whether the deficit is "held shorter" (→ A/B)
   or "captured later" (→ C). Committing a gameplay lever before this is guessing
   between H1 and H2. It also finally makes the `[exp-capture]` race legible on the
   stable side (attribution §5 is one-sided today).
2. **Option A (contest-aware escort/defense) — best first gameplay lever.** Smallest
   surface, lowest invariant risk, and it attacks the most defensible hypothesis
   (H1): the module currently gives a contested derrick the *same* two escorts and a
   *reactive* $200-gated defense as an uncontested one, and its only contest-sensitive
   code (`SafetyMultiplier*`) is dead. Making the hold contest-aware is the most
   direct closing of a 1.9× **hold** gap, and it reuses fields already resolved for
   `@experimental`.
3. **Option B (re-dispatch/stickiness) — strong second, gated on 4.D.** If the
   timeseries shows experimental captures then *loses* the derrick repeatedly, B is
   the targeted fix. Held second only because its payoff and even its verification
   depend on 4.D landing.
4. **Option C (race/ferry) — last, and partly a *diagnostic* of ferrying itself.**
   Highest integration risk and it presumes H2, which the one-sided §5 timing read
   under-supports. Its most valuable early form is the **ferry-on/ferry-off A/B**:
   `UseTransportForDistantCaptures` is the only active experimental-only capture
   difference from stable during the deficit, so proving whether it helps or hurts is
   worthwhile regardless of which lever ships.

**Caveat carried from §0:** if 4.D + a ferry-off arm show capture ticks and holds at
parity with stable, the deficit is H3 (supporting-army divergence from Stage E/F),
and lever 1 is the wrong tool — the fix would move to the offense/`PoiOffensive`
layer, not the capture module. That outcome is itself a valuable narrowing.

---

## 6. Invariant checklist (binds any option chosen)

- **Zero `SharedRandom`/`LocalRandom` (synced) draws added.** None of A/B/C draws
  synced RNG; the existing `TraitEnabled` stagger (`:242-243`) uses unsynced
  `world.LocalRandom` and is untouched (`influence-stack.md` Invariants).
- **Byte-identity for `@stable`/Normal/Rush/Turtle.** Every new field defaults to the
  current constant on the engine class and is set only on
  `CaptureCoordinatorBotModule@experimental.tecn`. Per-profile-instance gating (not
  a shared module) → the default-off-flag pattern suffices; no double-gate needed
  (contrast `SupplyFollowerBotModule`, `influence-stack.md` Invariants).
- **Deterministic ordering preserved.** Any ordering nudge (B) reuses
  `PoiScoring.CompareForOrder` (`PoiMap.cs:442`, `CaptureCoordinatorBotModule.cs:594`)
  so seed→result determinism and paired-seed variance reduction (SPEC §6.4) hold.
- **SR-trap avoided.** No option treats the SR as capturable/buildable; the target
  set stays the neutral income derricks (§1).

---

## 7. Reference map

| Claim | Source |
|---|---|
| Exp capture module + flags | `ai.yaml:131-185` |
| Stable capture twin (identical knobs) | `ai.yaml:830-858` |
| Live PoiMap dispatch path | `CaptureCoordinatorBotModule.cs:604-652` |
| Capture ordering (verbatim / damp) | `:557-597`; `PoiMap.cs:266-269/:495-497` |
| Fixed escort = 2 | `:796-812`, `ai.yaml:158/:849` |
| Reactive defense pass | `:818-869`, `ai.yaml:160-164` |
| Ferry (experimental-only, active) | `:662/:683-697`, `ai.yaml:172` |
| SR not capturable | `supply-route.md:65-72`, `PoiMap.cs:467-474` |
| 1.9× contested gap / control ~1.0× | attribution §2.4 |
| One-sided capture-timing read | attribution §5 |
| Invariants (RNG / byte-identity / gating) | `influence-stack.md` §Invariants |
| A/B rungs + decision rule | `WORKSPACE/ai-bench/` `rebase_*` dirs; SPEC §6.4/§6 |

Instrument note: the deficit was measured with capture repoint OFF; the uncommitted
`ai.yaml:180/:306` true→false flip in the shared tree is that same OFF condition and
belongs to another worker — untouched here. This recon read `main` @ **541be058**.
