# Crash sweep — absent-key, optional-trait and lazy-asset throws

> **Scope:** hunt for crash-to-desktop defects ahead of public release, generalising from two crashes found on 2026-08-16.
> **Read-only.** No production code changed, no build, no game launch, no autotest.
> **Against `main` @ `fc62f3b4`**, working tree clean, in sync with `origin/main`.
> Line numbers are current at that SHA.

---

## 0. The brief's premise is half wrong

The sweep was commissioned on the theory that both 2026-08-16 crashes were "the same shape: a
lookup by a key that can be absent". **They are opposite failure modes**, and the distinction
changes what you should sweep for.

| | Veteran crash (`8c05b2ff`) | `ClientInSlot` crash |
|---|---|---|
| Exception | `KeyNotFoundException` | `InvalidOperationException` |
| Cause | key **absent** | key **duplicated** |
| Trigger | `Rules.Actors["PILOT"]` — YAML capitalised, dict keys lowercased at load | two clients in one slot |

`SingleOrDefault` **returns null on zero matches and throws only on two or more**. So the
`ClientInSlot` crash cannot be an absent-key bug; it is a duplicate-key bug. Sweeping only for
absent keys would have missed it entirely.

**The shape they actually share** is more useful than either: *a runtime lookup whose invariant is
established somewhere far away and never re-checked at the lookup site.* For the veteran crash the
invariant was "this name exists in `Rules.Actors`", established in YAML. For `ClientInSlot` it was
"at most one client per slot", established in `SetupShellmapBots`. Both lookup sites assumed a
guarantee made by code they do not reference.

That reframing predicts a different third class than the brief does — not more `.First()` calls,
but **more non-idempotent setup routines that can run twice**. `SetupShellmapBots`
(`engine/OpenRA.Game/Game.cs:592-628`) shows the tell precisely, and it is visible *within a single
loop*: `lobbyInfo.Slots[slotKey] = ...` (:603, indexer — idempotent) sits eight lines above
`lobbyInfo.Clients.Add(...)` (:613, append — not idempotent). Re-running overwrites the slot but
appends a second client to it. **When auditing a setup routine, diff its writes for that
overwrite-vs-append asymmetry.**

---

## 1. Severity calibration — an exception in tick really is crash-to-desktop

Worth establishing before ranking anything, because it justifies weighting tick-reachable throws
above everything else.

`Game.LogicTick()` (`engine/OpenRA.Game/Game.cs:822`, called from the run loop at `:1023`) has
**no `try`/`catch`**. The only handler is `AppDomain.CurrentDomain.UnhandledException` in the two
launchers (`OpenRA.Launcher/Program.cs:28`, `OpenRA.WindowsLauncher/Program.cs:67`), which routes to
`ExceptionHandler.HandleFatalError` — that writes an `exception-*.log` and stderr, and **does not
recover**. So any throw inside `World.Tick` terminates the process.

Confirmed by reading. A tick-reachable throw is a hard CTD, not a caught error dialog.

---

## 2. Predictions registered before verifying — and how they came out

Recorded so the wrong ones are visible rather than quietly dropped.

| # | Prediction | Outcome |
|---|---|---|
| 1 | The two-crashes-one-shape premise is wrong; `ClientInSlot` is a duplicate-key crash | **RIGHT** — see §0 |
| 2 | Capitalised YAML actor names are a wide-open crash class, since lint lowercases before checking | **WRONG** — see §3. The most useful negative result in this sweep |
| 3 | Most `.First()`/`.Single()` hits will be vendored upstream and uninteresting | Broadly right |

---

## 3. WRONG PREDICTION, and why it matters: capitalised actor names are NOT an open crash class

I expected this to be the headline. It is not, and the manager should **not** spend effort on it.

The reasoning that made it look alarming is real as far as it goes:

- `CheckActorReferences.cs:70` lowercases a value **before** its `ContainsKey` test (an upstream
  workaround for OpenRA #4124). So lint declares a capitalised `[ActorReference]` value valid.
- `Ruleset` lowercases every actor name at load, and `ActorInfoDictionary` uses the default
  ordinal comparer. So a capitalised name genuinely misses at runtime.
- **101 capitalised `[ActorReference]` values are present in shipped mod YAML** across 44 distinct
  fields — husks (`Actor: HIND.Husk`), pilots (`PilotActor: PILOT`), missiles
  (`Actors: IskanderMissile`), `Mine: MINV`, and the `SupportActors` starting-unit lists.

**But the consumers normalise, so none of the 101 can crash.** `Actor.cs:163` does
`name = name.ToLowerInvariant()` *before* the `ContainsKey`/lookup pair, so **everything routed
through `World.CreateActor` is safe** — which is every husk, pilot, missile and starting unit in
that list. Spot-checked the non-`CreateActor` consumers too: `EjectOnDeath.cs:64,90` and
`AirstrikePower.cs:74` both `.ToLowerInvariant()` explicitly.

The residual risk is only a consumer that indexes `Rules.Actors[...]` with a YAML-supplied name
*without* going through actor creation — which is exactly what the veteran crash was
(`PlayerStatistics` built an `ArmyUnit` directly). I enumerated those consumers; see §4.

**Contradicts the reconciliation audit.** `260816-bug-reconciliation.md` §4 lists
`AirstrikePower.cs:104` as a latent defect that "still passes `info.UnitType` un-lowercased into
`CreateActor`". Both halves are now wrong: `:104` does pass it un-lowercased, but `CreateActor`
lowercases internally, and the *other* consumer at `:74` lowercases explicitly. That entry should be
struck.

**The veteran fix is durable, not a point patch.** `PlayerStatistics.cs:295` normalises once into a
single `actorName` field consumed by all seven downstream sites (`:307,319,351,359,371,385,400,411`),
with a comment naming the lint gap. A new call site would have to go out of its way to reintroduce it.

---

## 4. Verified clean — negative results worth recording

Recorded so nobody re-derives them. Each was checked against code, not assumed.

- **Influence-stack round-robin schedulers.** `DangerFieldLayer.cs:467`, `ControlField.cs:558` and
  `BeliefStore.cs:185` all do `cursor = (cursor + 1) % participants.Count` inside a tick — a
  `DivideByZeroException` if the participant list is ever empty, which a human-vs-human game could
  plausibly produce. **All three carry `if (participants.Count == 0) return;` on the immediately
  preceding line.** Correctly guarded.
- **YAML-configurable divisors.** `InfluenceMap.CellSize` (default 2) / `ValueDivisor` (100),
  `DangerFieldLayerInfo.HealthDivisor` (1000) / `CostDivisor` (5000) are divisors reachable in tick.
  All defaults non-zero; no shipped YAML sets any of them to 0 (`world.yaml:284,291` set `CellSize`
  to 8 and 2). `DangerFieldLayer.cs:998` guards `shotsPerBurst` with `burst > 0 ? burst : 1`.
  Latent for a modder or a map-rules override only — **not** reachable in the shipped game.
- **`DropsSupplyCache`.** `:129` calls `self.Trait<SupplyProvider>()`, which throws when absent, and
  `:134`/`:152` then null-check the result — a guard that can never fire. Harmless, because
  `DropsSupplyCacheInfo : Requires<SupplyProviderInfo>` makes the trait's presence a load-time
  constraint. The `Rules.Actors[Info.SupplyCacheActor]` lookup at `:176` is safe both ways: the
  default and the only YAML value (`vehicles.yaml:593`) are lowercase `supplycache`.
  **`Requires<T>` is what makes `Trait<T>()` safe** — worth knowing before flagging any
  `Trait<T>()` call as a null-deref risk.
- **`Map.cs:1230`** `Rules.Actors[actor.Value.Value]` is pre-filtered at `:1223` to names already
  present in `Rules.Actors`, so it cannot miss. It is also a map-editor path.
- **`LobbyPresetLogic.cs`** (WW3MOD-authored) contains no `Slots[...]`, `Players[...]`, `.First(`,
  `.Single(`, `.Max(` or `.Last(` call at all.

---

## 5. Confirmed still-live from the prior audit

- **`CreatesShroud` → `NotImplementedException`.** Re-verified at `fc62f3b4`:
  `AffectsMapLayer.cs:201` is `public virtual MapLayers.Type Type => throw new
  NotImplementedException();`, `CreatesShroud.cs` has **no `Type` member**, and `CreatesShroud`
  appears in **no** mod YAML. Still latent, still a one-line fix, still fires the instant anyone
  adds a jammer/smoke/stealth actor or a map author adds the trait via map rules.

---

## 6. Method, and where it is weak

**Authorship filter.** `git ls-tree` at the vendoring squash `7362fbc6` vs `HEAD` gives 523 engine
`.cs` files that did not exist upstream. Mapping each to its introducing commit and subtracting
`git rev-list c5bb5ece ^7362fbc6` (the later "apply release-20250330" re-merge) splits them
**249 upstream re-merge / 164 genuinely WW3MOD-authored** (413 after excluding `OpenRA.Test`). File
list at `/tmp/ww3_authored.txt` during the sweep; regenerate rather than trust it.

**The trap this avoids:** filtering by file is useless here — WW3MOD has touched ~1,840 engine `.cs`
files — and filtering by "file is new since `7362fbc6`" is *actively misleading*, because 60% of the
new files came from the upstream re-merge, not from WW3MOD. Both filters must be applied.

**Where this sweep is weak:**
- Static only. Nothing here was reproduced in a running game; no run was permitted.
- The `[ActorReference]` field list was harvested by regex over `public readonly` declarations, so
  any field declared in another form was not scanned.
- §3 clears the 101 capitalised values against the consumers I checked. I did not exhaustively
  enumerate every consumer of every one of the 44 fields — I checked the actor-creation path plus
  the direct `Rules.Actors[...]` sites. A consumer that *compares* `a.Info.Name == Info.SomeField`
  against a capitalised value would silently never match — not a crash, but a real silent bug class
  this sweep did not chase.
