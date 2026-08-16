# Capture-ferry escort → POI defence team — scoping

**Read-only scoping. No engine or YAML change, no autotest run, no worktree.**

Researched against `main` @ **`fe692f17`** (`git status -sb`: `main...origin/main`, `git rev-list --count HEAD..@{u}` = 0 ⇒ not behind upstream; tree clean apart from untracked `.maestro/` scratch and four WORKSPACE artefacts). Every behavioural claim below carries a `file:line` read at that SHA.

---

## 0. Headline — the first half of the idea already shipped, eight days ago

The user's idea has two halves:

1. **Fill the ferry's empty seats with infantry.** — **Already built, enabled on both profiles, autotested.** `CaptureFerryEscortSeats: 3` at `mods/ww3mod/rules/ai/ai.yaml:1520` (`@poi`/stable) and `:1578` (`@experimental`). Implementation `MountedTransportBotModule.RecruitCaptureFerryEscorts` (`MountedTransportBotModule.cs:429-479`), called at `:404-407`. Shipped in `7286a15f` (merged `97289e48`, 2026-08-15), with scenario `tools/autotest/scenarios/test-ferry-fills-seats/` and a measured result in the commit body: `boarded=3`, `depart aboard=4 target=4 reason=Full`.
2. **On arrival, the escort becomes a defence team for the POI.** — **Not built as a directed behaviour**, but a general one exists that would very likely absorb them for free. See §3.

So the actionable scope is not "fill the seats". It is **"do the two existing halves actually compose, and if not, why not"** — a much smaller and much cheaper question than the brief assumed.

**But read §5 first.** The measured ferry rate is ~8%, and that number decides whether any of this is worth doing.

---

## 5. How often would this ever fire? *(answered first, because it is the gate)*

**Measured, not estimated: 1 ferry in 12 capture orders — ~8% — in one real match on 2026-08-11.**

`WORKSPACE/DISCOVERIES.md:4451-4453`:

> `CaptureCoordinatorBotModule`'s unconditional per-capture log yielded **12 orders: 11 `ferried=False`, 1 `ferried=True`** — and critically, the single `True` is the **6th** of the twelve, not the first.

The same entry (`:4462-4463`) draws the conclusion that matters here:

> the ~8% ferry rate quantifies the user's complaint rather than merely confirming it: **the feature is not broken-off, it is starved.**

Starved of what is established by elimination in that entry (`:4455-4459`): the `transportModuleResolved` one-shot latch (`CaptureCoordinatorBotModule.cs:1323-1328`) is **refuted** as the cause — a permanently-cached null cannot produce a `True` at position 6 — leaving **free-carrier availability** as the surviving explanation. `TryReserveCaptureFerry` requires a carrier that is untasked *and* physically empty (`MountedTransportBotModule.cs:356-360`); on a map where the frontline delivery loop is running, carriers are rarely both.

Gates that must all pass before a ferry exists at all:

| Gate | Where |
|---|---|
| `UseTransportForDistantCaptures: true` | `ai.yaml:186` (`@experimental.tecn`), `:1893` (`@stable.tecn`) — on for both |
| target ≥ `TransportCaptureMinDistanceCells: 12` | `CaptureCoordinatorBotModule.cs:1702` |
| a free **and empty** carrier exists | `MountedTransportBotModule.cs:356-360` |
| own Supply Route alive | `MountedTransportBotModule.cs:343-345` |
| a technician exists | `tecn` is procured reactively by `MaintainTecnFloor` (`ai.yaml:172-174`, `TecnFloorMax: 5`), not built proactively |

**Verdict on worth-building: NO, not as posed — and the reason is a decision the project already made.** `WORKSPACE/plans/260811_transport_doctrine.md:60` is explicit, in bold, about the sequencing:

> **`ferried=False` on a target ≥12 cells away is the entire item-35 diagnosis.** One live match answers whether the ferry is never attempted, attempted and refused, or attempted and succeeding but slower than walking. No instrumentation needed. **Do this before writing a line of item-35 code.**

That diagnosis has now been run once and returned ~8%. Enriching a path that fires roughly once per match, when the *same* document identifies carrier starvation as the reason it does not fire more, is optimising the payload of a delivery that mostly does not happen. **The higher-value work is raising the ferry rate** (carrier availability), not decorating the 8%.

One caveat stated plainly, because it cuts the other way: that measurement is **one match**, and the DISCOVERIES entry carrying it was explicitly **rejected during curation** (`:4449`) as "a measurement of one run". It is the best evidence in the repo and it is not strong evidence. If the ferry rate were raised to, say, 50% by fixing carrier starvation, the escort question becomes worth revisiting — which is why §7 sequences it behind that.

---

## 1. How is the ferry reserved today?

**Where a carrier task becomes a capture ferry.** `MountedTransportBotModule.TryReserveCaptureFerry(bot, capturer, target)` (`MountedTransportBotModule.cs:336`), called from `CaptureCoordinatorBotModule` — `IssueCaptureOrder` computes `var ferried = Info.UseTransportForDistantCaptures && TryFerryCapture(bot, capturer, target)` and issues the on-foot `CaptureActor` only when the ferry refused (per `260811_transport_doctrine.md:237`). The task is marked as a ferry by `CaptureTarget != null` (`:250`); `Capturer` (`:256`) holds the one passenger that may be handed `CaptureActor`.

**What sets the seat target.** `SeatTarget = 1` at creation (`:398`) — the technician — then `task.SeatTarget += escorts.Count` at `:407` after `RecruitCaptureFerryEscorts` returns the units actually told to board. The count is deliberately of *boarded* rather than *candidates*, so a refused order never leaves a phantom reservation the carrier waits on (`:420-422`).

**Is anything structural preventing extra passengers?** **No — and it has already been removed.** Before `7286a15f` the answer was "nobody asks them to". Now they are asked: `RecruitCaptureFerryEscorts` (`:429-479`) draws up to `min(CaptureFerryEscortSeats, MaxPassengersPerLoad - 1, cargoMaxWeight - 1)` (`:436-438`) from `PassengerTypes`, measured **from the carrier** rather than the SR (`:457`) because a ferry carrier is picked for proximity to the technician, not to the reserve bubble.

Two structural details worth carrying forward:

- **The escort orders are Protected, not Recurring** (`:462-472`). Marked `Recurring` they were suppressed by the arbitration gate's dwell rule for *every* candidate — measured `boarded=0` against `candidates=3`, "a fill that silently did nothing". A capture ferry is a one-shot that never re-offers, so a dropped order is a seat lost for the run.
- **`CarrierTask.Capturer` is what makes a mixed load safe.** The unload hand-back at `:733-740` issues `CaptureActor` to `task.Capturer` **and nothing else**. It used to iterate `ReservedPassengers`, which was only safe while the ferry was single-TECN. A rifleman handed `CaptureActor` would *neutralise* the building the ferry was sent to take — soldiers clear, only technicians own (`game-model.md`).

---

## 2. Does filling the seats re-open the closed bug?

**The user's reading is correct, and I tried to break it rather than confirm it.** Filling the seats does **not** re-open `09877fd5`.

**What `09877fd5` actually closed** (2026-08-08, "give an arriving unit a purpose, and a reason to garrison"):

> A bot's opening technicians walked into a rear civilian house and garrisoned it with no enemy anywhere on the map, then were unrecoverable for the match — no bot module can unload a garrison…

Two causes: `GarrisonBotModule`'s gate had no enemy/danger/belief/POI term at all, so it picked an arbitrary house on tick 1; and opening play had no owner, so `CaptureCoordinator` discarded the undispatched remainder of its scan "with no order, no claim and no log — manufacturing exactly the idle unclaimed unit that garrison then recruited."

**The invariant that ruling protects: a capture-layer unit (`tecn`) must not fall into a general pool where a transport/garrison module can grab and strand it.** `dd441876` (2026-07-20) built the directed reservation path — `TryReserveCaptureFerry` — precisely so the technician is *requested by name* rather than drawn from a pool.

**Why the user's idea runs the other way.** It does not add `tecn` to `PassengerTypes`; the escorts are ordinary soldiers boarded into seats the technician does not use. The technician's claim is untouched. This is stated as the design intent in the field docs themselves (`MountedTransportBotModule.cs:185-189`).

**The case against my own reading — three ways it could still bite, checked:**

1. *Could `RecruitCaptureFerryEscorts` pick up the technician?* No. `PassengerTypes` (`ai.yaml:1483`/`:1541`) does not contain any `tecn` variant, and the method also excludes the capturer explicitly (`:451`).
2. *Could the technician be recruited into a garrison **after** it dismounts and its capture commitment is released — the exact 09877fd5 shape?* **No, structurally.** `PoiGarrisonBotModule.IsEligibleCombatUnit` under `UseUnitRoles: true` (set on both twins, `ai.yaml:773`/`:2140`) returns only `MainBattle || IndirectFire` (`PoiGarrisonBotModule.cs:462-467`). `tecn` resolves to `CaptureSpecialist` (`UnitRoleResolver.cs:47`) — the same structural argument `09877fd5` used to refute its own recon. The name-list fallback also lists every `tecn` variant (`ai.yaml:766`) but is inert under `UseUnitRoles`.
3. *Does committing the escorts clobber the capturer's ledger key?* No — `CommitTaskPassengers` skips `task.Capturer` explicitly (`:309-311`), because `CaptureCoordinator` already holds it under `capture:<id>`; the mirror skip is in `ReleaseTaskPassengers` (`:324-326`).

**Conclusion: no conflict.** The one thing the brief asked me to flag if true — that this re-opens the closed bug — is **not** true. The `Capturer`/`ReservedPassengers` split is the mechanism that keeps the two apart, and it is already in the shipped code.

**What *is* a real, recorded cost, and is not the same bug:** `WORKSPACE/bugs/discovered.md:961-980` — ferry escorts and walking escorts are recruited independently, so a ferried capture now commits `CaptureFerryEscortSeats: 3` **and** a separately-recruited walking escort (`EscortSize: 2` / `ContestedEscortSize: 4`). No double-booking of individuals (the two sets are provably disjoint, `:971-973`), but total infantry per capture rose from ~2 to ~5. **Unmeasured.** See §4.

---

## 3. Is there an existing "defend this place" behaviour?

**Yes — `PoiGarrisonBotModule`, and it already does almost exactly what the user is describing.** It is enabled on both twins (`ai.yaml:752` `@experimental`, `:2127` `@stable`).

What it does: parks 1-3 units (`MinGarrison: 1`, `MaxGarrison: 3`) on each **held money POI**, sized by value + threat, capped at `MaxGarrisons: 4` concurrent POIs, re-evaluated every 100 ticks, committed in the shared ledger under `defend:<id>` so offense cannot scoop them.

**Three facts that together mean the composition may already be free:**

1. **A captured POI becomes a defend target automatically.** `PoiMap.Discover` is owner-agnostic and the comment at `PoiMap.cs:200-202` is explicit: *"the per-perspective owner/action/score is derived later in ScoreFor so a captured POI flips role (Capture → Defend) without a re-scan."* `GetDefendTargets` (`:399-446`) filters the same `candidates` list to actors we own with `IncomeWeights` value > 0. **The building the ferry was sent to capture is, the moment it is captured, a garrison target.**
2. **Garrison recruits nearest-first.** `PoiGarrisonBotModule.cs:362-366`: `free.OrderBy(u => (u.CenterPosition - g.PoiPos).LengthSquared).ThenBy(u => u.ActorID).Take(need)`. The escorts have just dismounted *at the POI*. They are the nearest free units in the world to it.
3. **The escorts are released into the free pool at exactly the right moment.** `ReleaseTaskPassengers(task)` fires on unload (`MountedTransportBotModule.cs:745`), with the comment that this is deliberate — *"Release their ledger claim so offense can recruit them straight away"*. Garrison's `BuildFreePool` (`:432-443`) requires only not-claimed, not-ledger-committed, and eligible — **it does not require `IsIdle`**, so a soldier still finishing its dismount is already recruitable.

**So the cheapest honest version is: verify, do not build.** Run one match with the ferry firing and read `[exp-garrison] garrison … poi=<the ferried target> units=N` against `[exp-transport]` unload for the same POI. If N ≥ 1 within ~100 ticks of the capture, the user's feature already exists as an emergent composition of two modules and the correct deliverable is a log line proving it, not code.

**What that cheapest version would NOT do**, stated plainly:

- **It is not a dedicated escort.** The garrison sizes itself from POI *value* (`ValuePerGarrisonUnit: 50`) and threat, not from "how many soldiers happened to arrive". A cheap derrick asks for `MinGarrison: 1`, so **two of the three escorts would be immediately re-freed** and walk off to the offensive anchor. To a player watching, one of three stays.
- **It is capped at four POIs.** With `MaxGarrisons: 4` already saturated by higher-scoring POIs, the freshly-captured one is dropped from `targets` entirely (`:270-271`) and gets nothing.
- **There is a timing race.** Garrison and `PoiOffensiveBotModule.StageFreePool` both run at `ReevaluateInterval: 100` and both pull from an uncommitted free pool. Whichever ticks first in the frame after unload claims the escorts. Nothing sequences them.
- **Not every escort is garrison-eligible.** This is the sharpest mismatch found. `RecruitCaptureFerryEscorts` draws from `PassengerTypes`, which includes `medi.*` (medics) and `aa.*` (`ai.yaml:1483`). Under `UseUnitRoles: true`, garrison admits only `MainBattle || IndirectFire`; `Logistics` and `ShortRangeAD` (`UnitRoleResolver.cs:44,48`) are refused. **The ferry can board three medics and the garrison layer will adopt none of them.** The two recruiters use different eligibility predicates and nobody has reconciled them.

A *directed* version — hand the escorts straight to a `defend:<poiId>` commitment on unload, bypassing the free-pool round-trip — is maybe 30 lines and removes the race, the cap and the value-sizing shed. It is genuinely small. It is also, per §5, decoration on an 8% path.

---

## 4. Where does the escort come from, and does it starve anything?

**Same pool the offensive layer draws from. The contest is real and already recorded.**

`PoiOffensiveBotModule.StageFreePool` (`:2272-2326`) walks `BuildFreePool()` toward the staging anchor; it is live on `@experimental` (`ForwardStagingEnabled: true`, `ai.yaml:647`). The census at `WORKSPACE/recon/260808-order-churn-census.md:235-241` records offense's axis pass and `StageFreePool` running at *the same* 100-tick eval, with staged units **not** ledger-committed (`:2202`) — which is precisely why they are yankable.

Three distinct claims on the same infantry, in the order they bite:

1. **Boarding.** Offense's `BuildFreePool` honours the ledger but **not** the bespoke `IsPassengerReserved` seam (`260802_squad_brain_design.md:42`), so it could pull a soldier off the ramp mid-board. This is why `7286a15f` turned on `CommitPassengers` for both twins — the commit body calls it "load-bearing rather than incidental", since waiting for a fuller load against a module that keeps removing the passengers is just a slower half-empty departure.
2. **Total spend per capture.** `discovered.md:961-980`: ~5 infantry per ferried capture instead of ~2, because ferry seats and walking escort are recruited independently. **Explicitly unmeasured** — "if the offense looks thin in a benchmark after this lands, this is the first thing to suspect. The cheap lever is `CaptureFerryEscortSeats`, which is per-profile and defaults to 0."
3. **After unload.** The escorts are released (`:745`) into the pool that offense and garrison both read. Undirected.

**A second worker is measuring this contest right now from another angle.** Nothing in this note should be treated as a measurement of it; §7 sequences behind that worker rather than duplicating it.

---

## 6. Terminology check

**"POI" is the correct word, and the `@poi` suffix is a naming coincidence you should not lean on in player-facing copy.**

- **POI is a formal enum.** `PoiKind` (`PoiMap.cs:38-43`): `IncomeStructure`, `UtilityStructure` (defined but unused), `SupplyRoute`. Discovery (`:203-228`) admits an actor if its lowercased name is in `Info.IncomeWeights` (`oilb`, `fcom`, `bio`, `miss`, `hosp`) **and** it has `CaptureManagerInfo` (`:223-224`), or if it is the Supply Route actor.
- **A capturable tech building IS a POI** — kind `IncomeStructure`. So "the POI the technician was sent to capture" is precisely correct in this codebase's vocabulary.
- **The Supply Route is also technically a POI** (kind `SupplyRoute`), discovered and scored as a deny/pressure target even though `SUPPLYROUTE` carries no `CaptureManager` (`:217-222`). So "POI" alone is not unambiguously "the money building" — if the copy needs to exclude the SR, say "money POI", which is the term `GetDefendTargets` itself uses (`:385-386`) and which `PoiGarrisonBotModule` is scoped to.
- **`@poi` is not a bot profile.** It is a YAML trait-instance suffix. The two actual profiles are `experimental` and `stable` (`ai.yaml:44-53`), gated by `enable-ai-experimental` / `enable-ai-stable`. `MountedTransportBotModule@poi` (`ai.yaml:1475`) carries `RequiresCondition: enable-ai-stable` — i.e. **the `@poi`-suffixed transport block IS the stable twin**, a historical naming artefact from `dd441876`'s split. Note the inconsistency: `PoiGarrisonBotModule` uses `@stable` (`:2127`) for the same role. Do not infer profile identity from the suffix; read the `RequiresCondition`.

---

## 7. Proposed implementation shape

Sequenced so the cheap disproof comes before the code. **Steps 1-2 may close the whole item without writing engine code.**

| # | Step | Size | Risk |
|---|---|---|---|
| **1** | **Do not build yet — raise the ferry rate first.** Confirm §5's ~8% against a second match, and pursue carrier starvation (the surviving hypothesis) as the actual item. Everything below is decoration until a ferry fires more than once a match. | 1 run + read | none |
| **2** | **Verify the free composition.** One match with the ferry firing; grep `[exp-garrison] garrison … poi=` for the ferried target within ~100 ticks of the `[exp-transport]` unload. If the escorts are already adopted, close the item and record it. | 1 run + read | none |
| **3** | **Reconcile the two eligibility predicates** *(only if step 2 shows non-adoption)*. `RecruitCaptureFerryEscorts` draws from `PassengerTypes` (includes medics/AA); garrison admits only `MainBattle`/`IndirectFire`. Filter escort candidates through `UnitRoleResolver` so the ferry boards soldiers the garrison will actually take. Behavioural ⇒ default-off field per `@stable` policy. | ~20 lines | low |
| **4** | **Directed hand-off on unload.** In the `Unloading` branch (`MountedTransportBotModule.cs:720-745`), for a ferry task, commit the escorts under `defend:<captureTargetId>` instead of plain-releasing them. Removes the free-pool race, the `MaxGarrisons` cap and the value-sizing shed. Needs `PoiGarrisonBotModule` to adopt a pre-committed garrison rather than treating the key as foreign. | ~30-40 lines, 2 files | **the risky step — see below** |
| **5** | **Bound the spend.** Couple ferry seats and walking escort in `IssueCaptureOrder` so a ferried capture does not commit both (`discovered.md:961-980`). Depends on the other worker's measurement. | small, design-gated | med |

**The risky step is 4**, and the risk is not the code — it is the ledger. Committing escorts under a `defend:` key that `MountedTransportBotModule` invents means two modules now write the same namespace, and `PoiGarrisonBotModule.PruneGarrisons`/`RetireAll` own the release side for that namespace. A `defend:` commitment made by a module that has no garrison record to prune it is exactly the shape of the "unrecoverable unit" class `09877fd5` closed — not the same bug, but the same failure mode: a claim whose owner never releases it. If step 4 is taken, the commitment must be adopted into a real `Garrison` record on the garrison module's next tick, or given a TTL that guarantees release; the ledger's `Prune` liveness check (`PoiGarrisonBotModule.cs:257`) only sweeps dead/foreign actors, not orphaned keys on live ones.

---

## What I could not determine from reading alone

- **Whether the composition in §3 actually happens.** Every link is verified statically; the emergent behaviour is not. It depends on tick ordering between two modules on the same 100-tick cadence, which static reading cannot settle.
- **What the escorts actually are.** `RecruitCaptureFerryEscorts` takes the three nearest `PassengerTypes` units to the carrier. Whether that is typically riflemen or typically medics is a property of what is standing near a carrier at capture time — unknowable without a log.
- **Whether §5's 8% generalises.** One match, and the DISCOVERIES entry carrying it was rejected in curation as too narrow to trust without re-verification.
- **How many money POIs a real map carries**, hence how often `MaxGarrisons: 4` is already saturated when a capture lands. I did not count `oilb`/`fcom`/`bio`/`miss`/`hosp` per map.
- **The offense-starvation magnitude** (§4 claim 2) — deliberately left to the worker measuring it.
