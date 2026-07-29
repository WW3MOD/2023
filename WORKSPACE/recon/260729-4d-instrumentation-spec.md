# SPEC — Option 4.D: two-sided capture metrics + income timeseries (260729)

Read against `main` @ **d80b750b**. Read-only code trace; **no** gameplay runs,
builds, or edits to `.cs`/rules YAML were performed. This turns option **4.D** of
`WORKSPACE/recon/260729-lever1-capture-contest-design.md` (§4.D, size **S**,
non-gameplay) into an implementation-ready spec: a fresh implementer can execute it
without re-deriving anything.

**Purpose (from the design recon §3/§5):** 4.D is the de-risking instrument that
discriminates the surviving deficit hypotheses before any gameplay lever is spent —
**H1 "exp held derricks shorter"** (→ fix is escort/defense, Options A/B) vs
**H2 "exp captured later"** (→ fix is race speed/ferry, Option C) vs **H3
"supporting-army divergence"** (→ out of lever-1 scope, offense layer). It is
logging-only: no gameplay divergence, byte-identical simulation (§5 below states how).

Register: impersonal. Every claim is tagged **MEASURED** (off a prior artifact),
**CODE-READ** (traced to file:line at d80b750b), or **HYPOTHESIS** (a reading the
evidence suggests but does not prove).

Pre-read for the implementer: `influence-stack.md` §Invariants (zero-RNG /
byte-identity), and the design recon §2–§3 (why the completion event, not the
dispatch marker, is the missing signal).

---

## 0. Headline

**CODE-READ — the missing signal is capture *completion* + income *over time*, not
capture *intent*.** Today the only capture instrumentation is
`CaptureCoordinatorBotModule`'s `[exp-capture]` debug markers
(`CaptureCoordinatorBotModule.cs:328/:360/:612/:674/:731/:753/:934`), which record
**dispatch intent** (pre-scan, issue, commitment-released, tecn-killed) keyed by
`player=`. There is **no** log at the tick a derrick's ownership actually flips, and
**no** income-vs-time series. So neither "who captured first" nor "who held longer"
is currently recoverable from a match. 4.D adds exactly those two.

**CODE-READ — the correct sink is `BotVsBotMatchWatcher`, not the debug log.** The
per-match structured artifact the harness collects is `result.json`
(`run-test.sh:558` copies `${RESULT_FILE}` → the per-match `${SCREENSHOT_DIR}`),
built by `BotVsBotMatchWatcher.SerializeVerdict` (`BotVsBotMatchWatcher.cs:299-369`).
The aggregators already read `players[].stats.*` out of it
(`tools/autotest/parse-s1-batch.py:77-110`). The watcher **already** ticks every
frame as a declared read-only observer and integrates gross income
(`BotVsBotMatchWatcher.cs:226-288`). Extending that observer is the whole job —
`debug.log` is a single rolling file (`run-test.sh:219-227`) that is **not** archived
per-match except on a hang, so it is unfit for a batch timeseries.

**Consequence:** 4.D is entirely additive fields on `result.json` plus a small
poll-based observer, all inside the tournament assembly that only exists in
bot-vs-bot test worlds. No shared engine trait, no YAML, no gameplay path is touched.

---

## 1. What "capture" and "income" mean here (CODE-READ)

- **The capturable is a neutral income derrick, not the SR.** The contested,
  income-producing POIs are `oilb`/`bio`/`fcom` (the `CapturableActorTypes` list,
  `ai.yaml:140`), each carrying a `CashTrickler` (`structures-neutral.yaml:19/:51/:83`,
  OILB `Amount: 50`). The Supply Route is **not** capturable and generates none of
  this income (design recon §1). 4.D must therefore key on **CashTrickler-bearing
  actors**, which is exactly the income-POI set.
- **Income is a registered rate, not a per-tick grant.** `CashTrickler` does not add
  cash directly; on add/owner-change it registers an `IncomeEntry` with the unified
  economy (`CashTrickler.cs:71-76/:118-129/:127`), tagged with the actor's `ActorType`
  (e.g. `"oilb"`). `PlayerResources` exposes `IncomeEntries` (`PlayerResources.cs:145`),
  `TotalBuildingIncome`, and `TotalIncome` (`PlayerResources.cs:148/:150`). The unified
  tick pays `TotalBuildingIncome` once per `PassiveIncomeInterval`. **So per-POI income
  attribution = sum the `IncomeEntries` whose `ActorType` is a derrick type; total-POI
  income rate = `TotalBuildingIncome`.** Both are pure reads.
- **Ownership flip is observable three ways** (any one suffices; §3 picks one):
  `CaptureManager.INotifyCapture.OnCapture(self, captor, oldOwner, newOwner, …)`
  (`CaptureManager.cs:169`); `CashTrickler.INotifyOwnerChanged.OnOwnerChanged(self,
  oldOwner, newOwner)` (`CashTrickler.cs:71`) — guaranteed present on every income POI;
  or **polling `actor.Owner`** each tick from the watcher (no trait edit). 4.D uses
  polling (§3, isolation rationale).

---

## 2. Exact metrics to emit

All values are **per tracked player** (the `state.OriginalSrOwner.Keys` set the
watcher already iterates, `BotVsBotMatchWatcher.cs:251/:274`). "POI" = a
CashTrickler-bearing neutral income structure.

### 2.1 Capture-event stream (tick-resolved)
One record per ownership transition of a POI, appended in tick order:

| Field | Source | Notes |
|---|---|---|
| `tick` | `world.WorldTick` | integer sim tick of the flip |
| `poi_id` | `actor.ActorID` (uint) | stable per-actor id |
| `poi_type` | `actor.Info.Name` | `oilb`/`bio`/`fcom` |
| `old_owner` | `oldOwner.ClientIndex` (−1 if Neutral) | prior holder |
| `new_owner` | `newOwner.ClientIndex` | new holder |
| `event` | derived | `capture` (old = Neutral), `steal` (old = a tracked bot ≠ new), `recapture` (new = a bot that previously owned this poi_id), `destroyed` (POI left world while owned — see §2.3) |

This is the **two-sided** artifact the recon calls for: it records **both** bots'
transitions from a single player-agnostic observer, replacing the one-sided
`[exp-capture]` dispatch view.

### 2.2 Income timeseries (rate + cumulative, sampled)
One sample every `SampleInterval` ticks (default **25** = 1 s, matching the watcher's
`EvaluationInterval`), per player:

| Field | Source | Notes |
|---|---|---|
| `tick` | `world.WorldTick` | sample tick |
| `poi_income_rate` | Σ `IncomeEntries[i].AmountPerInterval` over derrick `ActorType`s | instantaneous per-interval POI income |
| `poi_income_gross` | new per-POI integrator (§3.2) | cumulative POI-only gross to this tick |
| `poi_count` | count of POIs currently owned | contextual denominator |

`poi_income_gross` is a POI-filtered twin of the existing whole-economy
`GrossCaptureIncomeFor` (`MatchTypes.cs:56`); it isolates derrick income from any
other `TotalBuildingIncome` source so the ladder metric is attributable.

### 2.3 Per-side scalar rollups (aggregator-ready, in `stats{}`)
Computed once at match end from the stream above and emitted as scalar fields
alongside `capture_income_gross` (`BotVsBotMatchWatcher.cs:361`):

- `time_to_first_capture_tick` — tick of this player's first `capture`/`steal`/`recapture`
  event, or `-1` if none. **The H2 discriminator.**
- `hold_ticks` — Σ over POIs of (ticks this player owned each POI). **The H1
  discriminator.** Computed by walking each POI's ownership timeline; the final open
  interval closes at `duration_ticks`.
- `captures_count`, `steals_count`, `recaptures_count`, `losses_count` — event tallies
  (a `steal` where `old_owner` = this player counts as its `loss`). **Distinguishes
  Option B's "loses then re-takes" churn.**
- `poi_income_gross` — cumulative POI-only gross income (§2.2). Parity check against
  the whole-economy `capture_income_gross` already present.

**Destruction handling (`event=destroyed`):** a POI can be blown up rather than
re-captured (OILB `Explodes`, `structures-neutral.yaml:25`; `SpawnActorOnDeath`
`:30`). When a tracked POI leaves the poll set while owned by a bot, emit a
`destroyed` event and close that owner's hold interval at the current tick. This
keeps `hold_ticks` honest (a destroyed derrick stops accruing hold, matching the loss
of its income).

---

## 3. Emission points and mechanism (CODE-READ anchors)

All new code lives in the tournament assembly
(`engine/OpenRA.Mods.Common/Traits/World/BotVsBotMatchWatcher.cs` and
`engine/OpenRA.Mods.Common/Tournament/MatchTypes.cs`). No file outside this assembly
is edited.

### 3.1 Capture-event stream — poll in the watcher tick
**Anchor:** `BotVsBotMatchWatcher.cs:241` (the `AccumulateGrossIncome()` call inside
`ITick.Tick`, which already runs every frame before the countdown gate).

Add a sibling read-only pass, `PollPoiOwnership()`, called on the same line-241 cadence:

1. **First-tick seed** (reuse the `srDiscoveryDone` first-tick path,
   `BotVsBotMatchWatcher.cs:231-236`): enumerate `world.ActorsWithTrait<CashTrickler>()`,
   record each `ActorID → Owner` into a new `state.PoiOwner` dict, and each
   `ActorID → poi_type` into `state.PoiType`.
2. **Each tick:** for every currently-live CashTrickler actor, compare `actor.Owner`
   to `state.PoiOwner[id]`. On a change, classify the event (§2.1), append a
   `PoiCaptureEvent` to `state.CaptureEvents`, and update `state.PoiOwner[id]`. For
   any id in `state.PoiOwner` no longer live, emit `destroyed` (if last owner was a
   bot) and drop it.

Polling — not the `INotifyCapture`/`INotifyOwnerChanged` hooks — is chosen so **zero
shared engine traits are edited**; the watcher already owns a per-tick read budget and
is declared a pure observer (`BotVsBotMatchWatcher.cs:270-271`). Exact-tick fidelity is
preserved because the poll runs every tick (line 241 is pre-gate). *(Alternative, if
event-driven is later preferred: a tiny `INotifyCapture` helper trait on `^TechBuilding`
forwarding to the watcher — anchored at `CaptureManager.cs:169` — but that edits a
shared actor template and is unnecessary here.)*

### 3.2 Income timeseries — extend the existing integrator pass
**Anchor:** `BotVsBotMatchWatcher.cs:272-288` (`AccumulateGrossIncome`) and
`MatchTypes.cs:75-92` (`GrossIncomeIntegrator`).

1. Add a POI-filtered integrator per player, `state.PoiIncome[p]` (a second
   `GrossIncomeIntegrator`), fed each tick with `poiIncomeRate` = Σ over
   `pr.IncomeEntries` where `ActorType ∈ {oilb,bio,fcom}` of `AmountPerInterval`
   (reads only; `PlayerResources.cs:145`). This mirrors the existing whole-economy
   `integrator.Tick(pr.TotalBuildingIncome, …)` at `BotVsBotMatchWatcher.cs:286`.
2. Every `SampleInterval` ticks, append `{tick, poiIncomeRate, PoiIncome[p].Value,
   poiCount}` to a new `state.IncomeSamples[p]` list.

`GrossIncomeIntegrator` is reused verbatim (`MatchTypes.cs:85-91`); its own doc-comment
already certifies it "cannot affect determinism or the experiment"
(`MatchTypes.cs:67-69`).

### 3.3 Serialize into `result.json`
**Anchor:** `BotVsBotMatchWatcher.cs:347-362` (the `stats{}` block) and `:367` (the
top-level object close).

- **Scalars (§2.3):** insert new keys into `stats{}` immediately after
  `capture_income_gross` (`:361`) — additive, schema-stable, exactly the pattern the
  v3 comment describes (`:359-361`). Bump `verdict_version` 5→6 (`:305`).
- **Arrays (§2.1/§2.2):** add two top-level fields before the `:367` close —
  `"capture_events":[…]` and, per player, an `"income_samples":[…]` inside each
  `players[]` element. Manual JSON append via the existing `sb`/`Escape` helpers
  (`:303/:371`); no new serializer dependency.

The harness change is **zero**: `result.json` is already copied per-match
(`run-test.sh:558`) and already parsed by the aggregators.

---

## 4. Post-processing (aggregator) and decision rule

**Anchor for the aggregator edit:** `tools/autotest/parse-s1-batch.py:77-110` (already
digs `players[].stats.capture_income_gross`); extend the same row-reader to pull the
new scalars, and add a small pass over `capture_events` / `income_samples`.

### 4.1 Computed per contested seed (6017/8017 from the recon), per side
- **median `time_to_first_capture_tick`** (across the paired rungs) — exp vs stable.
- **total `hold_ticks`** → hold-seconds = `hold_ticks / 25`.
- **`poi_income_gross`** — exp vs stable, and its ratio Stable ÷ Exp (compare to the
  measured **~1.9×** contested gap and the **~1.00×** stable-vs-stable control, design
  recon §3 MEASURED).
- **`losses_count` / `recaptures_count`** — churn on contested derricks.

### 4.2 Decision rule (maps result → hypothesis → next lever)

| Observation (exp vs stable, contested seeds) | Selected hypothesis | Next lever |
|---|---|---|
| `time_to_first_capture` **later** for exp (and ferry-off arm closes it) | **H2 — captured later** | **Option C** (race/ferry), design recon §4 C |
| `time_to_first_capture` ≈ parity, but exp `hold_ticks` **lower** and `poi_income_gross` **lower** | **H1 — held shorter** | **Option A** (contest-aware escort/defense); **Option B** if `losses_count`/`recaptures_count` on contested derricks is elevated (loses-then-retakes) |
| `time_to_first_capture` ≈ parity **and** `hold_ticks` ≈ parity **and** `poi_income_gross` ≈ parity, yet exp still loses the ladder | **H3 — supporting-army divergence** | **Out of lever-1 scope** → Stage E/F offense layer (`PoiOffensiveBotModule`), design recon §5 caveat |

This is precisely the discriminator the design recon §3/§5 says must land before any
gameplay lever is committed. **HYPOTHESIS:** the recon's one-sided §5 read (stable
`committed=2` by ~tick 1047 vs exp `committed=1` by ~1146) points weakly at H2, but
that is dispatch-intent, not completion; 4.D's completion stream is what confirms or
refutes it two-sided.

---

## 5. Invariant compliance — how 4.D stays byte-identical (CODE-READ)

Binds to `influence-stack.md` §Invariants. Each mechanism is **log/observe-only**;
none reads its own output back into sim state.

- **Zero synced-RNG draws added.** No `SharedRandom`/synced `LocalRandom` is touched.
  The poll (§3.1) and the integrators (§3.2) do arithmetic on reads only.
- **Pure reads, no sim writes.** The watcher writes exclusively to its own
  `MatchTrackingState` (new dicts/lists) and to the `result.json` string. It reads
  `actor.Owner`, `pr.IncomeEntries`, `pr.TotalBuildingIncome`, `world.WorldTick` —
  all already read by the existing observer (`BotVsBotMatchWatcher.cs:272-288`). No
  actor/player/trait state is mutated. The reads never flow back into any order,
  score, or RNG.
- **No gameplay trait or YAML edited.** All code is in the tournament assembly; the
  contest capture path (`CaptureCoordinatorBotModule`, `ai.yaml`) is untouched, so
  `@experimental`/`@stable`/Normal/Rush/Turtle remain byte-identical. In particular
  the `ai.yaml:180` repoint flip sitting uncommitted in the shared tree is **not**
  touched by 4.D.
- **Observer exists only in test worlds.** `BotVsBotMatchWatcher` is a world trait on
  the bot-vs-bot tournament world; normal play never instantiates it, so 4.D adds
  nothing to a shipped match. (This is why the poll approach is preferred over an
  `INotifyCapture` helper on `^TechBuilding`, which *would* exist in normal play.)
- **Logging order is sim-irrelevant.** Even if `ActorsWithTrait` iteration order were
  unstable, it changes only the *order of appended log records*, never a sim decision;
  determinism of the simulation is independent of the observer entirely.

---

## 6. Effort estimate + risk notes

**Size: S** (matches the design recon §4.D label). No gameplay change, no YAML, no new
test scenario required — the metric rides existing tournament rungs.

**Work items (all in the tournament assembly + one Python aggregator):**
1. `MatchTypes.cs` — add `PoiOwner`, `PoiType`, `CaptureEvents`, `PoiIncome`,
   `IncomeSamples` to `MatchTrackingState` (`:35-57`); a `PoiCaptureEvent` struct and
   an `IncomeSample` struct. (~30 lines.)
2. `BotVsBotMatchWatcher.cs` — `PollPoiOwnership()` + first-tick seed (`:231-241`);
   POI income sampling in/next to `AccumulateGrossIncome` (`:272-288`); serialize new
   scalars (`:361`) + arrays (`:367`); `verdict_version` 5→6 (`:305`). (~70 lines.)
3. `tools/autotest/parse-s1-batch.py` — read new scalars + reduce arrays into the §4.1
   table + §4.2 rule. (~40 lines.)
4. One gated **S2** rung to produce the artifact (design recon §4.D "one gated S2
   rung"). **Requires the user's explicit go-ahead to run** (CLAUDE.md: no autonomous
   multi-test runs).

**Risks (labelled):**
- **HYPOTHESIS (low) — timeseries bloat in `result.json`.** At 25-tick sampling a
  ~4-min match yields a few hundred samples/player; JSON stays well under a MB.
  Mitigation if needed: raise `SampleInterval`, or gate the arrays behind an
  `EmitTimeseries` flag defaulting on only for S2 rungs. Scalars (§2.3) are tiny and
  unconditional.
- **CODE-READ (low) — `IncomeEntry.ActorType` is the derrick-type key.** Per-POI
  attribution (§3.2) filters on `ActorType ∈ {oilb,bio,fcom}`; if a future income
  structure is added to `CapturableActorTypes` (`ai.yaml:140`) without updating the
  filter, its income silently drops from `poi_income_gross`. Mitigation: source the
  filter set from the same list, or from "has CashTrickler", rather than a hardcoded
  triple.
- **HYPOTHESIS (low–med) — destruction vs recapture ambiguity.** A derrick blown up
  (`Explodes`/`SpawnActorOnDeath`, `structures-neutral.yaml:25-32`) leaves the poll set
  the same tick it would on a re-capture; §2.3 disambiguates by "left world while
  owned" → `destroyed`. If the husk (`OILB.Husk`) briefly retains a CashTrickler this
  could misclassify; the implementer must confirm the husk carries **no** CashTrickler
  (spot-check `OILB.Husk` in `structures-neutral.yaml`) so it correctly drops from the
  poll set.

**Single biggest risk:** **CODE-READ — misattributing income if the per-POI filter
drifts from the capturable set.** `poi_income_gross` is the load-bearing ladder metric
(§4.1); if its `ActorType` filter and `CapturableActorTypes` (`ai.yaml:140`) ever
disagree, the "held shorter vs captured later" verdict is computed on an incomplete
income figure and could point at the wrong lever. Bind the filter to a single source
of truth (the CashTrickler presence check) to close this.

---

## 7. Reference map

| Claim | Source (@ d80b750b) |
|---|---|
| Existing one-sided dispatch markers | `CaptureCoordinatorBotModule.cs:328/:360/:612/:674/:731/:753/:934` |
| Result sink + serializer | `BotVsBotMatchWatcher.cs:299-369` |
| Per-tick observer + gross integrator | `BotVsBotMatchWatcher.cs:226-288` |
| `stats{}` insertion point | `BotVsBotMatchWatcher.cs:347-362` (after `:361`) |
| Match state to extend | `MatchTypes.cs:35-57`; integrator `:75-92` |
| Income model (registered rate) | `CashTrickler.cs:71-76/:118-129`; `PlayerResources.cs:118-150/:321` |
| Ownership-flip hooks (event-driven alt) | `CaptureManager.cs:169`; `CashTrickler.cs:71` |
| Derrick set / income weights | `ai.yaml:140`; `world.yaml:310-311`; `structures-neutral.yaml:1-33` |
| Harness collects result.json | `run-test.sh:558` |
| Aggregator reads stats | `tools/autotest/parse-s1-batch.py:77-110` |
| Hypotheses H1/H2/H3 + 1.9×/1.0× | design recon `260729-lever1-capture-contest-design.md` §3 |
| Invariants (RNG / byte-identity) | `influence-stack.md` §Invariants |

Read `main` @ **d80b750b**. Another worker's A/B run may hold uncommitted state in the
shared tree (e.g. `ai.yaml`); this spec touched nothing but its own new file.
