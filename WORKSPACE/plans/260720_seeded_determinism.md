# Seeded determinism for benchmark runs — design recon

**Date:** 2026-07-20
**Mode:** READ-ONLY recon (no code changed, game not run — another worker owns the run slot)
**Motivation:** The S1 benchmark (`tournament-s1-eco-river-zeta`, 7500-tick window) shows large
run-to-run variance — two N=10 batches of near-identical code measured in-window derrick-capture
**4/10 vs 9/10**. Suspected driver: bot decision RNG is not seeded per match, so a fixed
`Test.RandomSeed` gives a *sample*, not a *reproduction*. Goal: per-match reproducibility (same
seed → same match) so the roadmap's self-tuning phase (parameter search) has cheap, low-variance
evaluation.

---

## TL;DR

- **Root cause is a single line:** `World.cs:214` — `LocalRandom = new MersenneTwister()` is
  constructed **unseeded**, so it seeds from `Environment.TickCount` (wall clock). ~40 bot-decision
  call sites read `world.LocalRandom`; that is the dominant nondeterminism in a fixed-seed match.
- **The seed already flows end-to-end** — the tournament runner already passes a deterministic
  `Test.RandomSeed` per match, and it already reaches `SharedRandom` and `playerRandom`. It just
  never reaches `LocalRandom`. **No shell/YAML/env-var work is needed for the core fix.**
- **Recommended minimal mechanism:** seed `LocalRandom` from the same lobby `RandomSeed`,
  decorrelated by a fixed transform (≈2 LOC, 1 file), **plus** emit the seed into the verdict JSON
  (≈3 LOC, 1 file, bump `verdict_version` → 5). Then validate with the same-seed-twice protocol.
- **`OPENRA_SEED` env var is NOT recommended** — the existing `Test.RandomSeed` launch arg is the
  injection point and is already wired through `TestMode → Server → World`. Adding an env var would
  be a second, redundant path.

---

## 1. Root nondeterminism source(s)

### 1a. The seed flow, as it exists today (fixed-seed tournament match)

```
run-tournament.sh:282   MATCH_SEED = i*1000 + 17                 # deterministic per match index
run-tournament.sh:298   ./launch-game.sh ... Test.RandomSeed=$MATCH_SEED
TestMode.cs:96-98       Test.RandomSeed → RandomSeedOverride (int?)
Server.cs:310           randomSeed = RandomSeedOverride ?? (int)DateTime.Now.ToBinary()
Server.cs:332           LobbyInfo.GlobalSettings.RandomSeed = randomSeed
                         │
                         ├─ World.cs:213  SharedRandom  = new MersenneTwister(RandomSeed)   ✓ SEEDED
                         ├─ World.cs:237  playerRandom  = new MersenneTwister(RandomSeed)   ✓ SEEDED (faction/spawn)
                         └─ World.cs:214  LocalRandom   = new MersenneTwister()             ✗ UNSEEDED  ← ROOT CAUSE
```

`MersenneTwister()` (default ctor) chains to `this(Environment.TickCount)`
(`MersenneTwister.cs:25-26`). So `LocalRandom`'s sequence is a function of wall-clock milliseconds
at world-construction time — different every launch, even with an identical `Test.RandomSeed`.

### 1b. What reads `LocalRandom` (the bot-decision randomness)

~40 call sites in the WW3MOD bot modules read `world.LocalRandom`. A representative sample (all
verified):

- **Scan/reeval timing** (staggers *when* a module acts, so it perturbs the whole match phase):
  `LayeredDefenceBotModule.cs:137`, `SquadManagerBotModule.cs:190,193,194,195`,
  `PoiOffensiveBotModule.cs:148`, `PoiGarrisonBotModule.cs:132`, `CaptureCoordinatorBotModule.cs:156,157`,
  `CaptureManagerBotModule.cs:79`, `MountedTransportBotModule.cs:116`, `McvManagerBotModule.cs:93`,
  `HarvesterBotModule.cs:125`, `SupportPowerDecision.cs:119`, `BaseBuilderQueueManager.cs:103`.
- **Unit selection / call-in composition:** `UnitBuilderBotModule.cs:173,188`,
  `AdaptiveProductionBotModule.cs:150`, `McvManagerBotModule.cs:120`, `HarvesterBotModule.cs:166`.
- **Squad shaping & target choice:** `SquadManagerBotModule.cs:111,325,335,345,401`,
  `HelicopterSquadBotModule.cs:232,303,304`, `MinelayerBotModule.cs:97,147`,
  `Squad.cs:46` (`Random = World.LocalRandom` — aliases the world RNG, so seeding the world RNG
  covers squads too), `StateBase.cs:35`, `AirStates.cs:70,106,157`.
- **Placement / rally:** `BaseBuilderBotModule.cs:116,216`, `GarrisonBotModule.cs:79`,
  `ScoutBotModule.cs:82`, `SupportPowerBotModule.cs:171`, `BaseBuilderQueueManager.cs:149,211,243,340,345,348`.

A couple of bot sites already use the seeded `SharedRandom`: `ScoutBotModule.cs:214-215` (random
scout target cell) and `ThreatMapManager.cs:71` (update countdown). Those are already deterministic;
they are the exception.

### 1c. What is already deterministic under a fixed seed

- **Combat/sim RNG** — weapon inaccuracy, miss rolls, burst randomization — all read
  `SharedRandom`: `Armament.cs:513,536,567,654`. Deterministic once `RandomSeed` is fixed.
- **Faction & spawn-point assignment** — `playerRandom` seeded from `RandomSeed` (`World.cs:237`).
- **Lockstep sim** — OpenRA's netcode guarantees tick-for-tick determinism given identical order
  streams and a shared seed. This is the property netplay relies on.

### 1d. Deliberately-out-of-scope RNG (do NOT seed these)

- `Game.CosmeticRandom` (`Game.cs:52`, `new()` unseeded, commented `// not synced`) drives
  rendering/sound/menu/editor only — tesla arcs, smudge smoke, idle anims, sound-clip choice,
  bot-name/skin pick in the lobby. None of it is on the sim tick path that produces the verdict.
  ~50 call sites; seeding it would be pure overscope and would not change any match outcome.
- Other unseeded `new MersenneTwister()`: `DefaultTerrain.cs:174` / `DefaultTileCache.cs:48`
  (tile-variant render cache), `LobbyCommands.cs:1330,1334` (server map pick),
  `DownloadPackageLogic.cs:309` (mirror pick). None sim-tick.

**Conclusion:** in a single-client `Test.Mode` bot-vs-bot match with a fixed `Test.RandomSeed`, the
**only** simulation-affecting nondeterminism is the unseeded `LocalRandom`. Seed it and the match
becomes reproducible (subject to the residual risks in §5).

---

## 2. Where a fixed seed can be injected (pipeline audit)

There is **already** a fully-wired injection point — no new mechanism required:

| Layer | Hook | Status |
|---|---|---|
| Shell | `run-tournament.sh:282,298` passes `Test.RandomSeed=$((i*1000+17))` | **exists** |
| Launch arg | `TestMode.cs:96-98` parses `Test.RandomSeed` → `RandomSeedOverride` | **exists** |
| Server | `Server.cs:310,332` `RandomSeedOverride ?? DateTime.Now → GlobalSettings.RandomSeed` | **exists** |
| Sim seed | `World.cs:213` `SharedRandom`, `:237` `playerRandom` | **exists, seeded** |
| **Bot seed** | `World.cs:214` `LocalRandom` | **MISSING — this is the gap** |

`run-test.sh` (single-test flow) also honors `Test.RandomSeed` via the same `TestMode` path, so a
one-off reproducible match is available for debugging without the batch runner. No Lua/map-option
plumbing is needed.

---

## 3. Verdict-JSON seed recording gap

`BotVsBotMatchWatcher.cs` documents a `"seed": <int>` field in its verdict-shape header comment
(`:21`), but `SerializeVerdict` (`:287-356`) **does not emit it**. `batch.meta.json`
(`run-tournament.sh:179-189`) records only `seeds_requested`, not the per-match seed value (it is
recoverable as `i*1000+17`, but not stamped). So today a verdict JSON cannot be traced back to the
seed that produced it except by index arithmetic. This is the cheap "increment (a)" — make the
verdict self-describing.

The watcher can read the authoritative seed directly: `World` exposes
`public Session LobbyInfo => OrderManager.LobbyInfo` (`World.cs:48`), so
`world.LobbyInfo.GlobalSettings.RandomSeed` is the actual seed used (works whether it came from
`Test.RandomSeed` or the `DateTime.Now` fallback). Prefer this over `TestMode.RandomSeedOverride`
(which is null on the non-deterministic path).

---

## 4. Increment cost table

| Increment | What | Files / LOC | Risk | Value |
|---|---|---|---|---|
| **(a) Record seed in verdict** | Emit `"seed"` (+ `"local_random_seed"` if (b) decorrelates) in `SerializeVerdict`; bump `verdict_version` → 5 | `BotVsBotMatchWatcher.cs`, ~3 LOC | Trivial (additive field) | Provenance now; enables replay-by-seed once (b) lands |
| **(b) Seed `LocalRandom`** | `World.cs:214` → seed from `RandomSeed` via a fixed decorrelating transform | `World.cs`, ~2 LOC | **Low–Med** | THE fix: same seed → same bot decisions → same match |
| **(c) Full replay determinism** | No new code beyond (a)+(b) — a *validation* task: run same seed twice, diff verdicts; hunt any residual nondeterminism | 0 LOC; test time | Med (may surface pathfinding-thread or watchdog issues) | Confidence that (b) actually reproduces |

Recommended bundle: **(a) + (b), then validate via (c)'s protocol.** They are complementary and
touch two small, independent files.

---

## 5. Is full determinism realistically achievable?

Mostly yes, with named caveats to confirm empirically in §7:

1. **Lockstep sim is deterministic** given a fixed shared seed and identical order stream — this is
   OpenRA's netplay contract. `Test.Mode` runs a single local client, so the order stream is
   whatever the bots issue, which becomes deterministic once (b) removes the `LocalRandom` wobble.
2. **Wall-clock watchdog (real risk):** `run-tournament.sh` kills a match at `MAX_WALL_SECS`
   (`:323-333`). A deterministic match ends at the same *tick* every time, but the watchdog is
   *wall-clock*. On a loaded machine one run could be killed before natural end while another
   reaches it → different verdicts. **For reproduction, the match must reach its natural end
   (SR-capture or `TimeLimitTicks`), not the watchdog.** Give reproduction runs generous
   `--max-wall-secs`.
3. **Async pathfinding (verify):** stock OpenRA computes some paths off-thread but applies results
   deterministically on the sim thread; WW3MOD modified movement (`SmartMove`, `Aircraft`,
   `CohesionMoveModifier`). Flag for the §7 diff — if two same-seed runs diverge *after* (b), this
   is the first suspect.
4. **`LocalRandom` is "local" (non-synced) by OpenRA design.** Seeding it is safe and correct for a
   single-client benchmark, but it does **not** make bot behavior synced across a *multiplayer*
   match (each client keeps its own `LocalRandom`). That is out of scope and unaffected — call it
   out so nobody reads this as an MP-desync fix.
5. **Decorrelation:** seeding `LocalRandom` with the *same* int as `SharedRandom` makes the two MT
   streams emit identical sequences, coupling bot decisions to combat rolls. Harmless but sloppy —
   derive `LocalRandom`'s seed with a fixed transform (e.g. `unchecked(RandomSeed * 6364136223846793005L + 1442695040888963407L)`
   truncated to `int`, or a plain `RandomSeed ^ constant`) so the streams are independent yet still
   a pure function of `RandomSeed`.

---

## 6. Implementation checklist

**(b) Seed `LocalRandom` — `engine/OpenRA.Game/World.cs:214`**
- [ ] Replace `LocalRandom = new MersenneTwister();` with a seeded construction derived from
      `orderManager.LobbyInfo.GlobalSettings.RandomSeed` via a fixed decorrelating transform
      (see §5.5). Compute the derived seed into a local first for clarity.
- [ ] Leave `SharedRandom` (`:213`) and `playerRandom` (`:237`) untouched.
- [ ] One-line comment: *why* it is now seeded (reproducible bot decisions for benchmarking) and
      that it is derived-not-equal to the shared seed to avoid stream correlation.
- [ ] Update the `architecture.md:291-293` "Bot decisions are not seed-reproducible" note — it
      becomes *stale-and-wrong* the moment (b) lands. Reword to: bot decisions ARE reproducible
      when a fixed seed is supplied; describe the derived-seed transform. (Fixing a
      verifiably-wrong curated statement is allowed by the knowledge-bank rules.)

**(a) Record seed in verdict — `engine/OpenRA.Mods.Common/Traits/World/BotVsBotMatchWatcher.cs`**
- [ ] In `SerializeVerdict` (`:287-356`) add `"seed":<int>` from
      `world.LobbyInfo.GlobalSettings.RandomSeed`. `SerializeVerdict` is `static` and has no
      `world` — thread the seed in (capture it in `WorldLoaded` into a field, or pass it to
      `WriteVerdictAndExit`/`SerializeVerdict`). Simplest: store `capturedSeed` in `WorldLoaded`.
- [ ] Optionally also emit `"local_random_seed":<int>` (the derived value) so a debugger can
      reconstruct the exact bot stream.
- [ ] Bump `"verdict_version"` `4 → 5` (`:293`) and add a `v5` line to the header changelog
      (`:38-52`) describing the additive `seed` field.
- [ ] Confirm the aggregator (`tools/autotest/aggregate-tournament.sh`) tolerates the new field
      (additive JSON keys should be ignored by a well-formed reader — verify it does not assume a
      fixed key set).

**No changes required** to `run-tournament.sh`, `run-test.sh`, scenario YAML, or any env var — the
seed already flows. (Optional nicety, separate follow-up: stamp `MATCH_SEED` into
`batch.meta.json` for human readability.)

---

## 7. Validation protocol

Gated behind explicit user goahead (autotest run-slot rule). Two-run determinism check:

1. Build once. Run the **same** scenario with the **same** `Test.RandomSeed` **twice**, with a
   generous `--max-wall-secs` so neither run is watchdog-killed:
   `run-test.sh` twice (or `run-tournament.sh <scen> --seeds 1` twice), same seed both times.
2. Diff the two verdict JSONs. **Pass = byte-identical** on `duration_ticks`, `winner_client_index`,
   `win_reason`, and every player's `score_total` / `stats`. (Timestamps differ — compare the
   embedded verdict `notes`, not the outer `TestMode` wrapper.)
3. **Negative control:** run the same scenario with a *different* seed → verdict should differ
   (confirms the seed actually drives outcome, i.e. we did not accidentally make matches
   seed-invariant).
4. If step 2 diverges despite (b): the sim itself has a residual nondeterminism. Bisect by tick —
   the periodic score log (`BotVsBotMatchWatcher.cs:243-247`) already prints `tick=… scores=…` to
   the `.watcher.log`; find the first tick where the two runs' scores differ and inspect what acted
   there. Prime suspects, in order: async pathfinding (§5.3), any remaining `Environment.TickCount`/
   `DateTime`/`Guid` on the tick path, thread-pool ordering.
5. Only after step 2 passes twice on ≥2 distinct seeds is "full replay determinism" claimed.

**Statistical note:** even with reproducibility, keep N>1 for *evaluating a code change* — one seed
is one battlefield, and you want mean-over-seeds, not a single sample. What (b) buys is that a fixed
small seed-set (e.g. 5 seeds) yields a *stable* mean run-to-run, collapsing the 4/10-vs-9/10 wobble
into a repeatable number — the precondition for parameter search.

---

## 8. Structural option — deterministic fast-forward harness for parameter search

Per loop doctrine, the recon names one structural payoff. Seeding `LocalRandom` turns each
`(params, seed)` pair into a pure function returning a verdict, which unlocks a **headless
parameter-sweep harness**:

- **Shape:** for a parameter grid point `P` (e.g. a squad-size or reeval-interval knob exposed via
  YAML or a `Test.*` override), run the fixed seed-set `S = {s1..s5}`, collect verdicts, reduce to
  a scalar objective (e.g. in-window derrick-capture rate or mean SR-capture margin). Because each
  `(P, s)` is reproducible, the harness can **cache** results and never re-run an evaluated point —
  the search is resumable and its cost is exactly `|grid| × |S|` matches, known up front.
- **Why it needs (b):** without seeded `LocalRandom`, each `(P, s)` is noisy, so a search would need
  large N per point to see through variance, and could never cache (a re-run gives a different
  number). Determinism is the enabling primitive.
- **Cheapest first cut:** a shell/Python driver over the existing `run-tournament.sh` (already
  emits per-seed verdicts + aggregate), sweeping one knob, with results keyed by
  `(git_sha, param_value, seed)`. No engine work beyond (a)+(b). This is the concrete on-ramp to the
  roadmap's self-tuning phase.

---

## References

- `engine/OpenRA.Game/World.cs:48,213-214,237`
- `engine/OpenRA.Game/Support/MersenneTwister.cs:25-26`
- `engine/OpenRA.Game/Server/Server.cs:310,332`
- `engine/OpenRA.Game/TestMode.cs:68-72,96-98`
- `engine/OpenRA.Game/Game.cs:52,244`
- `engine/OpenRA.Mods.Common/Traits/World/BotVsBotMatchWatcher.cs:21,38-52,243-247,287-356`
- `engine/OpenRA.Mods.Common/Traits/Armament.cs:513,536,567,654`
- Bot `LocalRandom` sites: `SquadManagerBotModule.cs`, `UnitBuilderBotModule.cs`,
  `PoiOffensiveBotModule.cs`, `CaptureCoordinatorBotModule.cs`, `LayeredDefenceBotModule.cs`,
  `HelicopterSquadBotModule.cs`, `BaseBuilderQueueManager.cs`, `Squad.cs:46`, et al. (see §1b)
- `tools/autotest/run-tournament.sh:282,298,323-333`
- `DOCS/reference/architecture.md:291-293` (existing note — becomes stale when (b) lands)
