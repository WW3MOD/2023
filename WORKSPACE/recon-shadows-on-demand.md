# Recon: on-demand `shadows.bin` generation — ground truth, verdict, staged plan

**Date:** 2026-08-30 · **Against:** `main @ 627be5a4` (3 commits ahead of `origin/main`) · **Mode:** design & feasibility only, no engine/YAML changes.
**Method:** static read + four *measured* generation runs on map copies **outside the repo** (`/tmp`, deleted after). No game launch, no autotest, no tracked file touched (`git status --porcelain mods/ww3mod/maps/ tools/autotest/scenarios/` clean throughout).

---

## Verdict in one paragraph

**The user's plan is aimed at a problem that is real, but the premise underneath it is refuted, and the situation is the inverse of what was assumed.** `shadows.bin` is *not* "generated only when saving in the map editor" — **8 of the 10 shipped maps carry no `shadows.bin` at all and regenerate the entire cache from scratch on every single load**, single-threaded, inside the loading screen, at a measured **29–101 seconds** depending on map. "Ship WW3MOD without shadows for each map and generate on demand" is therefore **already what ships** — just with no cache, no progress bar, and no sync story. The genuinely valuable core of the proposal is the half the user framed as an afterthought ("if the shadows are not already cached and saved"): **persist the generated result**. Do that, plus parallelise the loop, and a 101-second stall becomes a ~13-second one-time cost — with **no lobby protocol, no handshake, and no new widgets required**. The elaborate multiplayer handshake the proposal centres on is solving a problem that mostly evaporates once the cache exists, and it is blocked outright by a constraint neither of us knew about (`ComputeUID` hashes `shadows.bin`, below) until that constraint is fixed first. **Recommendation: do Stages 1–3 (cache + parallelise + dedupe), which are small, high-value and low-risk; do NOT build the lobby handshake yet.** Separately and urgently: shadow generation is a live suspect for the unsolved 2026-08-16 two-machine desync, and there is a two-minute test that settles it.

---

## Part 1 — Ground truth

### Q1. What is in a `shadows.bin`, who writes it, and when

**It is a precomputed line-of-sight / concealment cache, not a rendering artifact.** Two layers:

| Layer | Type | Meaning |
|---|---|---|
| `Map.DensityLayer` | `CellLayer<byte>`, 1 byte/cell | Per-cell obstruction density, summed from every map actor carrying `IDensityInfo` (`Map.cs:252`, built `Map.cs:976-1002`) |
| `Map.ShadowLayer` | `MapShadowLayer`, 2 bytes per *ordered cell pair* | Per-pair `(groundShadow, airborneShadow)`, for every `to` cell in the radius-2..32 annulus around each `from` cell (`Map.cs:253`, built `Map.cs:1004-1010`) |

**Serialisation** (`Map.SaveShadowsBinaryData`, `Map.cs:946-974`): density bytes for all cells in `AllCells.MapCoords` order, then, per from-cell, one `ushort` per annulus cell — `(ground << 8) | airborne` (`Map.cs:967`). Read back symmetrically at `Map.cs:469-494`. Flat, uncompressed, no header, no version stamp, no checksum. **The format is positional** — it is valid only for the exact map dimensions and the exact annulus enumeration that wrote it, and carries nothing that would let a reader detect a mismatch.

**Writers — three, and the premise is refuted:**

1. `--regen-shadows` utility command (`UtilityCommands/RegenShadowsCommand.cs:34`).
2. Map save — `toPackage.Update("shadows.bin", SaveShadowsBinaryData())` (`Map.cs:880`), which is the editor-save path the user had in mind, and also `--refresh-map`.
3. **At runtime, on every map load, whenever the file is absent** — `Map.cs:500-508`:
   ```csharp
   // Fallback: if no shadows.bin was on disk, compute fresh from current rules.
   if (ShadowLayer == null || DensityLayer == null)
   {
       SetDensityLayer();
       SetShadowLayer();
   }
   ```
   This runs the *identical* `SetDensityLayer()` + `SetShadowLayer()` pair that `SaveShadowsBinaryData` runs (`Map.cs:948-949`) — same cost, same result — and then **throws it away when the map unloads.** Nothing writes it back.

**So writer #3 answers Q3 as well: today, a map with no `shadows.bin` silently generates one in memory at every load.** No shadows are missing, nothing fails, nothing renders differently — the player just waits. That is why this has never been reported as a bug: it is indistinguishable from "the map is slow to load".

**Which maps actually ship a cache:**

| | Map | `MapSize` | `shadows.bin` |
|---|---|---|---|
| | `arena-tank-duel` | 66×34 | — |
| | `nuclear-winter-ww3` | 102×72 | — |
| | `polar-disorder-ww3` | 98×98 | — |
| ✅ | `river-zeta-ww3` | 98×82 | 36,871,948 B |
| | `seventh-woods-ww3` | 123×114 | — |
| | `shellmap-open-field` | 92×62 | — |
| | `siberian-pass-ww3` | 97×67 | — |
| | `twin-rivers-ww3` | 128×128 | — |
| ✅ | `woodland-warfare-ww3` | 98×98 | 45,527,532 B |
| | `x-lake-ww3` | 130×130 | — |

**Only 2 of 19 tracked `shadows.bin` files are shipped maps.** The other 17 are autotest scenario copies (`tools/autotest/scenarios/…`), which are duplicates of the same two maps' terrain — ten river-zeta-derived, seven woodland-derived, one `demo-heli-lanes`.

### Q2. How long generation takes, and what it scales with

**Derivation.** `SetShadowLayer` (`Map.cs:1004-1010`) walks every from-cell and calls `RecomputeShadowFrom` (`Map.cs:1126-1181`), which for each of ~3,204 annulus offsets runs a Bresenham walk (`CellLayer.TilesIntersectingLine`, `CellLayer.cs:160`) of up to 32 cells. Cost is therefore **linear in the number of stored (from, to) cell pairs**, and the pair count is fixed by geometry: the annulus offsets are `ceil(sqrt(du²+dv²)) ∈ [2,32]` (`MapShadowLayer.MinRange`/`MaxRange` = 2/32, `MapShadowLayer.cs:36-37`), clipped to `MapSize` by `FindTilesInAnnulus(..., allowOutsideBounds: true)` → `Tiles.Contains(t)` (`Map.cs:1916`).

**The model is verified exactly, not estimated.** Computing the clipped pair count analytically and predicting file size as `cells + 2×pairs` reproduces **all four on-disk files byte-for-byte**:

| Map | Predicted bytes | Actual bytes | |
|---|---|---|---|
| `arena-tank-duel` (bench) | 6,719,660 | 6,719,660 | ✅ exact |
| `siberian-pass-ww3` (bench) | 28,415,043 | 28,415,043 | ✅ exact |
| `river-zeta-ww3` | 36,871,948 | 36,871,948 | ✅ exact |
| `woodland-warfare-ww3` | 45,527,532 | 45,527,532 | ✅ exact |

**Two measured timings** (Release config — `Makefile:71` `CONFIGURATION ?= Release`; Apple Silicon, single-threaded, wall clock incl. ~1.6 s process+mod startup):

- `arena-tank-duel` 66×34 → **9.25 s**
- `siberian-pass-ww3` 97×67 → **34.04 s**

Fitting these two gives **`t ≈ 1.6 s + 2.29 µs × pairs`** (the implied 1.6 s intercept independently matches the ~1.1 s measured bare-utility startup, so the fit is not absorbing hidden per-map cost). Applied to the shipped set:

| Map | Cells | Pairs | Cache size | **Generation time** | Ships cache? |
|---|---|---|---|---|---|
| `arena-tank-duel` | 2,244 | 3.36 M | 6.7 MB | **9.3 s** *(measured)* | no |
| `shellmap-open-field` | 5,704 | 12.1 M | 24.2 MB | **29.2 s** | no |
| `siberian-pass-ww3` | 6,499 | 14.2 M | 28.4 MB | **34.0 s** *(measured)* | no |
| `nuclear-winter-ww3` | 7,344 | 16.5 M | 33.0 MB | **39.3 s** | no |
| `river-zeta-ww3` | 8,036 | 18.4 M | 36.9 MB | 43.7 s | ✅ yes |
| `woodland-warfare-ww3` | 9,604 | 22.8 M | 45.5 MB | 53.6 s | ✅ yes |
| `polar-disorder-ww3` | 9,604 | 22.8 M | 45.5 MB | **53.6 s** | no |
| `seventh-woods-ww3` | 14,022 | 35.1 M | 70.3 MB | **81.9 s** | no |
| `twin-rivers-ww3` | 16,384 | 41.9 M | 83.8 MB | **97.3 s** | no |
| `x-lake-ww3` | 16,900 | 43.3 M | 86.7 MB | **100.7 s** | no |

Bold = paid **on every load, today**. Independent cross-check: `RESUME-260816.md:21` records x-lake's in-memory shadow layer at 103 MiB post-optimisation, consistent with it being the largest by a wide margin.

**Scaling is ~quadratic in linear map dimension** up to the point where the map exceeds the 65-cell annulus diameter, then linear in area. It is **independent of tree count** — the walk runs over every pair regardless of whether any density is present. (`demo-heli-lanes`'s 25.5 MB file is stored *sparse* on disk — 8 KB allocated — because it is nearly all zeros; that is a filesystem artifact, not a smaller computation.)

### Q3. What happens today if a map has no `shadows.bin`

**Answered above and it is the cheapest, most important fact in this document: it silently regenerates in memory at load and discards the result.** `Map.cs:500-508`. Nothing fails, nothing renders differently. "Shipping without shadows" therefore requires **zero** engine work — it is the status quo for 80% of the map roster. The entire cost of the user's proposal is in the *caching* and the *sync*, not in the "ship without" part.

### Q4. Determinism, and whether the simulation reads shadows

**Shadows are simulation inputs, not presentation. This is not close.** Three consumers:

- **Vision attenuation** — `MapLayers.cs:363-365` reads `map.ShadowLayer[selfLocation, puv]` and subtracts it from the visibility strength band. This is the vision/shroud system (`Shroud.cs → MapLayers.cs`, per `architecture.md`).
- **Firing LOS** — `WeaponInfo.cs:148`: *"Maximum shadow value (0-255) from the ShadowLayer that still allows this weapon to fire."* A shadow delta flips whether a weapon may fire at all. `DISCOVERIES.md:5948` records the exact arithmetic: `airborneShadow = ceil(ΣD/5)` vs MANPAD's default `ClearSightThreshold` 5 → blocks at ΣD ≥ 26.
- **Damage reduction / cover** — `DensityModifiesDamage` samples the frozen `DensityLayer` (`Infantry/DensityModifiesDamage.cs:72-87`).

The in-tree test file states the bar explicitly (`MapShadowLayerTest.cs:20-25`): *"Shadow feeds vision attenuation (MapLayers) and firing LOS (FiringLOS), both of which are simulation inputs, so the bar is not 'close enough' — every query must return exactly what the nested CellLayer returned, **on every machine**."*

**So: any per-player divergence in generated shadows is a desync, full stop.**

**Is the generator deterministic?** Structurally, mostly yes — but with a float caveat that is *not* fully closed:

- Enumeration order is fixed and RNG-free: `Grid.TilesByDistance` is precomputed immutable data (`Map.cs:1911-1919`), Bresenham is pure integer (`CellLayer.cs:160-190`), `CenterOfCell` is pure integer (`Map.cs:1423-1426`).
- **But `RecomputeShadowFrom` uses `float`** at `Map.cs:1163` (`t = dot / (float)deltaLengthSquared`), `:1165` (`z_los = z_a * (1 - t)`), `:1169` (`totalAirborne += DensityLayer[tile] / 5f`), and `Math.Ceiling` at `:1177`. These are IEEE-754 basic ops in a fixed accumulation order, so they *should* be bit-identical — but ECMA-335 permits higher intermediate precision, and this has **never been tested across two physical machines**.
- Minor: `dH` (`Map.cs:1147`) is computed and never used — dead.
- **Latent trap:** `deltaLengthSquared` is `int` and peaks at ~1.07 × 10⁹ for `MaxRange = 32`. The overflow ceiling is `MaxRange = 45`; raising the constant past that silently wraps negative.

> ### ⚠️ Shadow generation is an untested suspect for the unsolved 2026-08-16 two-machine desync
>
> `DISCOVERIES.md:2927-2979` records that .NET 6/8/10 produce a **byte-identical** 12-minute simulation — but explicitly: *"Both runs were the same machine, so same CPU, same OS, same libm — a cross-machine difference is untested and the 2026-08-16 desync was two physical machines."* That desync appeared at world tick 3792, not tick 0.
>
> Per-machine float shadow generation is **exactly** that shape: 8 of 10 maps compute shadows independently on each machine at load; the resulting bytes are never sync-hashed (`Sync.cs:71` cannot admit a float at all), so a divergence would stay invisible until the first vision/LOS query near foliage diverged — i.e. at first contact, some thousands of ticks in.
>
> **Two-minute test that settles it, and it needs no code:** run `./utility.sh --regen-shadows` on the same map on both machines and `shasum` the output. Identical ⇒ shadows cleared, and the hypothesis dies cheaply. Different ⇒ the desync is found, and it also means **the user's per-player-generation design is unsafe as proposed** and must be replaced by host-authoritative distribution. **Do this before building anything.**

---

## Part 2 — The constraint that reshapes the design

### `ComputeUID` hashes every `.bin` file — including `shadows.bin`

`Map.cs:291-318`:
```csharp
foreach (var filename in contents)
    if (filename.EndsWith(".yaml", …) ||
        filename.EndsWith(".bin",  …) ||   // ← map.bin AND shadows.bin
        filename.EndsWith(".lua",  …) || (format >= 12 && filename == "map.png"))
        streams.Add(package.GetStream(filename));
return CryptoUtil.SHA1Hash(merged);
```

Consequences, and they are decisive:

1. **A map with a `shadows.bin` and the same map without one are, to the engine, two different maps.** Different UID ⇒ different lobby identity.
2. **The user's design as literally stated cannot work.** "Each player generates the shadows if they don't have it already" — if the generated file is written into the map package, the generating player's map UID *changes underneath them*, mid-lobby, diverging from everyone else's. The lobby matches on `GlobalSettings.Map = map.Uid` (`LobbyCommands.cs:588`).
3. **The cache must therefore live OUTSIDE the map package** — a support-dir cache keyed by map UID.
4. **This is circular until fixed.** While `shadows.bin` is inside the package, the UID depends on the cache and the cache is keyed on the UID. **Removing `shadows.bin` from the package breaks the circularity and is a prerequisite for everything else.** It also permanently removes a whole class of "why do these two installs disagree about this map" bug.
5. Removing the two shipped caches **changes those two maps' UIDs once**. Cost: replays bound to the old UID no longer resolve, and the maps look "new" to the map cache. I found **no hardcoded 40-hex map UIDs** in `tools/autotest/` or `mods/ww3mod/` (the 40-hex strings there are `git_sha` fields in `batch.meta.json`), so nothing in-repo breaks.

### What the 19 files actually cost in git

| Measure | Value |
|---|---|
| Distinct `shadows.bin` blob versions in history | **26** |
| Logical (uncompressed) total | **718.3 MB** |
| **Actual packfile cost** | **243.8 MB** |
| Total `.git` | **644 MB** |

**`shadows.bin` is ~38% of the repository.** Two things follow that matter for expectation-setting:

- **gzip is not a lever.** Yes, `shadows.bin` gzips to 40.5% (measured: 36.9 MB → 14.9 MB) — but **git already zlib-compresses blobs**, which is exactly why 718 MB logical occupies 244 MB on disk. Compressing the files before committing would save close to nothing and would break the map format for no gain.
- **gitignore-going-forward reclaims exactly zero of the 243.8 MB.** History rewrite is off the table (correctly — hundreds of `file:line`/SHA references point into existing history). What gitignoring buys is **stopping the bleed**: 26 versions have accumulated at ~9.4 MB average packfile cost each, and *every future editor save or `--regen-shadows` of any map permanently adds another ~9–25 MB*. That is the real win, and it should be described as "the repo stops growing at this rate", never as "the repo shrinks".

---

## Part 3 — Verdict on the user's plan, piece by piece

| User's proposal | Verdict |
|---|---|
| "The shadows should not be committed probably" | **Agree, with one carve-out.** Stop committing them. But the two shipped caches are currently *earning their keep* (44 s and 54 s of load time). Don't delete them until the replacement cache exists — otherwise those two maps get *slower*. |
| "they are only generated when saving the map in the map editor" | **Refuted.** Also generated by `--regen-shadows`, and — critically — **in memory on every load of the 8 uncached maps** (`Map.cs:500-508`). |
| "ship WW3MOD without shadows for each map" | **Already true for 8 of 10 maps, and it costs 29–101 s per load.** Zero engine work; zero benefit without caching. |
| "generate the shadows in the lobby when a map is selected, if not already cached" | **This is the good idea, and the "if not already cached" clause is the whole value.** But generate at *load*, not at map-select — see below. |
| "needs to sync between players … each player generates if they don't have it" | **Unsafe as stated, and blocked by `ComputeUID`.** Safe only if (a) the cache moves out of the map package, and (b) cross-machine determinism is proven by the shasum test. If that test fails, per-player generation must be abandoned for host-authoritative distribution. |
| "the game cannot be started during generation; a loading bar per player" | **Correct requirement, wrong priority.** Once cached, this fires once per player per map ever — and with parallelisation it is ~13 s, which is inside the range players already tolerate from a loading screen. Build it *last*, if at all. |

**The central mis-framing:** the proposal treats generation as a new cost to be introduced and then managed with a protocol. It is an *existing, unmanaged, recurring* cost. Caching removes it recurring; parallelising makes the first payment cheap. The protocol manages what is left, which is small.

---

## Part 4 — Recommended staged plan

Stages 1–3 are the recommendation. Stage 0 gates everything. Stages 4–5 are optional and I would not start them yet.

### Stage 0 — Settle determinism (size: **minutes**, risk: **none**, value: **very high**)
Run `--regen-shadows` on one map on both the user's machines; `shasum` both outputs. Also worth diffing a fresh regen of `river-zeta-ww3` against its committed `shadows.bin` — if those differ, **the shipped cache is already stale relative to current code**, which is a live correctness bug independent of this whole feature (`Map.cs:1172-1175` warns about exactly this) and would mean a cached player and an uncached player already disagree.
**Gate:** if outputs differ across machines, stop and redesign around host-authoritative distribution.

### Stage 1 — Persist the fallback to an out-of-package cache (size: **S**, risk: **low**)
At `Map.cs:500-508`, before computing, probe a cache file; after computing, write it.

- **Location:** `Platform.SupportDir` (`Platform.cs:151`), e.g. `SupportDir/Cache/shadows/<key>.bin`. **Not** in the map package — see Part 2.
- **Key — and this must not be filename-based.** Key on `SHA1(mapUid ‖ densityRulesHash ‖ SHADOW_ALGO_VERSION)`:
  - `mapUid` covers map.yaml/map.bin/lua/png, so any terrain or actor edit invalidates automatically — this is what makes a stale cache impossible rather than merely unlikely. *(Valid only once `shadows.bin` is out of the package; before that the key is circular.)*
  - `densityRulesHash` over every `IDensityInfo` value in `Rules.Actors` — because `SetDensityLayer` reads the rules (`Map.cs:979-999`), a rules-only edit changes the output with the map bytes unchanged.
  - `SHADOW_ALGO_VERSION`, a hand-bumped const — the standing trap at `Map.cs:1172-1175` is precisely that editing the curve silently keeps old values. A version const converts that from a silent-wrong into an automatic-rebuild, and is the single highest-value line in this plan.
- Add a length check + the key in a small header; treat any mismatch as a miss, never as an error.
- **Buys:** every map after the first load. x-lake 101 s → ~0.4 s (an 87 MB sequential read).

### Stage 2 — Parallelise generation (size: **S**, risk: **low**)
`SetShadowLayer` (`Map.cs:1004-1010`) is **embarrassingly parallel** and I verified the preconditions: each iteration writes only `ShadowLayer[fromUV, *]` — a disjoint slot range per from-cell (`MapShadowLayer.cs:71-77,134`) — reads a `DensityLayer` that is fully populated beforehand, and both enumerators are pure over immutable data (`Map.cs:1911-1919`, `CellLayer.cs:160-190`). `Parallel.ForEach` over from-cells is safe **and bit-identical regardless of scheduling**, because no accumulation crosses a from-cell boundary.
**Buys:** ~8× on a modern desktop. x-lake's worst case 101 s → **~13 s**. Combined with Stage 1, the worst first-load in the game becomes ~13 s once, ever.

### Stage 3 — Stop the bleed, and dedupe (size: **S**, risk: **low**)
- Add `shadows.bin` to `.gitignore`.
- `git rm --cached` the **17 autotest copies** (`tools/autotest/scenarios/…`). These are duplicates of two maps' terrain, are never shipped to players, and are regenerated by Stage 1 on first run. This is the safe 80% of the win.
- **Handle the 2 shipped maps last and deliberately**, since removing them changes their UID (Part 2 §5) and slows their first load until Stage 1 lands. Sequence: Stage 1 → then remove.
- Because the Stage 1 cache is keyed on content rather than path, **river-zeta's cache is computed once and shared by the map and all ten river-zeta-derived autotest scenarios** — 17 files collapse to 2 entries. This is a real speedup for repeated autotest runs, not just a disk saving.
- **Say plainly in the commit message:** this stops future growth; it does not shrink the existing 244 MB.

### Stage 4 — Lobby gate (size: **M**, risk: **medium**) — *not recommended yet*
Only worth building if Stage 0 proves determinism **and** post-Stage-2 timings still feel bad. Prior art carries most of it:
- **Readiness:** `Session.ClientState` (`Session.cs:122`) already has `NotReady`; the server already refuses commands from non-ready clients (`LobbyCommands.cs:222-231`). Hold a generating client in `NotReady` and reject its `state ready`. **No new gating concept.**
- **Progress:** `ProgressBarWidget` (`Widgets/ProgressBarWidget.cs`) already exists and is already wired to a per-client percentage for map download (`MapPreviewLogic.cs:170-187`, `GetPercentage`/`IsIndeterminate`). **A per-player bar needs no new widget type** — it is an existing widget in the existing player-row layout, fed by a new lobby command broadcasting a percentage. This is the cheap part.
- **Host changes map mid-generation:** already free — `LobbyCommands.cs:601-611` resets **every** client to `ClientState.Invalid` on map change. Cancel in-flight generation on that signal.
- **Joins mid-generation, slow or failing players:** a joiner arrives `NotReady` (`LobbyCommands.cs:534`) and generates like everyone else. A failed generation must fall back to *in-memory* generation, never to "play without shadows" — that would be a guaranteed desync.
- **Single-player / skirmish:** no peers; Stage 1+2 alone cover it. Do not route SP through a handshake.
- **Replays and saved games:** the cache is derived from map content + rules + algorithm version, all of which the replay already pins. Provided `SHADOW_ALGO_VERSION` is bumped whenever the curve changes, an old replay regenerates the same bytes. **Without that const, replays silently re-render and re-simulate differently after any curve edit** — another reason it is the highest-value line in Stage 1.

### Stage 5 — Shrink the format (size: **M**, risk: **medium**) — *optional*
The airborne channel is `ceil(ΣD/5)` and the ground channel saturates; both are heavily zero-dominated on sparse maps (hence `demo-heli-lanes` storing sparse). RLE or a zero-block index would cut cache size several-fold. **Only worth it for disk/RAM, not for the repo** (Stage 3 already removes it from git), and it touches a format with no version field. Defer.

---

## Part 5 — Alternatives considered and rejected

| Alternative | Why rejected |
|---|---|
| **Compress the committed caches** | Git already zlib-compresses; measured 40.5% gzip ratio is *already reflected* in the 244 MB. Saves ~nothing, breaks the format. |
| **Rewrite history to purge the blobs** | Explicitly off the table, and correctly — hundreds of `file:line`/SHA references across `DOCS/` and `WORKSPACE/` point into existing history. |
| **Ship caches for shipped maps only, generate for user maps** | This is *almost* the status quo (2 of 10) and it is the worst of both worlds: it is exactly the mixed cached/uncached population that makes stale-vs-fresh divergence possible (`DISCOVERIES.md:6969` names "baseline divergence" as the real lockstep risk). Stage 1 makes every player uniformly cache-backed instead. |
| **Generate at map-select in the lobby, as proposed** | Wasteful and worse UX: players browse maps. Generating on select burns 100 s on maps nobody plays. Generate at **load**, cache, and the second load is free. |
| **Host generates and ships `shadows.bin` to clients** | 87 MB over the wire vs ~13 s of local CPU. Only correct if Stage 0 proves *non*-determinism — at which point it becomes mandatory rather than merely expensive. |
| **Make the shadow layer dynamic instead** | Different feature, explicitly disabled 260503 for mid-game lag (`Building.cs:372-397`, `Map.cs:1012-1016`). Out of scope. |
| **Do nothing** | Genuinely defensible for the *repo* complaint alone (gitignore-only is 10 minutes). **Not** defensible once the 101-second-per-load finding is on the table — that is a shipping-quality bug affecting 8 of 10 maps, and it was invisible precisely because it looks like a slow loading screen. |

---

## Watch

- **The 2.29 µs/pair constant is from two runs on one Apple Silicon machine, Release, no repeats.** The *pair counts* and *file sizes* are exact (verified byte-for-byte on four maps) and I'd defend those anywhere; the *seconds* carry maybe ±30% across hardware. The 8× parallel speedup is a textbook estimate, not measured — memory bandwidth on an 87 MB write-out could easily make it 5×.
- **I did not verify that x-lake, twin-rivers and seventh-woods actually load** — I never launched the game (correctly, per brief). I inferred their generation cost from a verified size model, not from a stopwatch on a real load. **If I'm wrong about anything important, I'd bet on this**: there may be a map-load path I did not find that avoids the full `Map` constructor for some maps, or an existing cache layer above it, which would make the 101 s figure never actually materialise. I looked for one and found none — `Map.cs:500-508` is unconditional — but "I found no bypass" is weaker than "I watched it load".
- **The desync hypothesis is a hypothesis.** It fits the evidence unusually well (per-machine float generation, untested cross-machine, fault at tick 3792 rather than tick 0), but I have not run the shasum test and I could easily be pattern-matching. Treat Stage 0 as the thing that decides it, not this document.
- **I did not confirm the two shipped caches are current.** If `river-zeta-ww3/shadows.bin` was baked before the `ForestGroundShadow` superlinear-knee change (`db9545a1`, per `DISCOVERIES.md:5908`), then a cached and an uncached player **already** disagree today. That would be a live bug that this document's Stage 0 would surface as a side effect. I flagged it rather than tested it because testing means overwriting a tracked file in a shared checkout.
- **Float determinism: I reasoned structurally, I did not prove it.** "IEEE-754 basic ops in fixed order should be bit-identical" is true in practice on SSE2/NEON but ECMA-335 permits higher intermediate precision, and `Map.cs:1163-1169` is exactly where that would bite.
- **Unrelated but worth knowing:** the machine's boot volume is at **97%, ~975 MB free**. My benchmark was 6.6 MB and is deleted, so this is pre-existing — but it caused a genuine `ENOSPC` mid-session and truncated one benchmark write to 0 bytes. Something else is filling that disk.
