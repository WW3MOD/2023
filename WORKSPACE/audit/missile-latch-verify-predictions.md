# Verification run — predictions, recorded before any run

Written before the Hellfire rig was run even once, and before the isolation
build existed. Left exactly as written whether or not it held.

Verifier working from `1ec6f17c` (detached) at
`C:/Users/fredr/worktrees/ww3mod/missile-latch-verify`, pre-fix base `3f18551a`.

## Gap 1 — Hellfire

Hellfire: `Speed: 500`, `CloseEnough: 298` (engine default, not set on the
weapon), `HorizontalRateOfTurn: 60`, `TerrainHeightAware: true`, `Range 25c0`,
`MinRange 5c0`. MANPAD for comparison: `Speed 450`, `CloseEnough 192`,
`HorizontalRateOfTurn 20`.

**V1 — Hellfire latches pre-fix in the reversal lane.** `hf_air_reverse` shows
≥1 latch on the pre-fix build, and ≥75% of pre-fix latches with tick data fire
on a tick where physical separation shrank. This is the MANPAD signature
reproducing on a second weapon.

**V2 — Hellfire is nonetheless *less* exposed than MANPAD.** Its
`HorizontalRateOfTurn` is 60 against MANPAD's 20 and its `CloseEnough` is 298
against 192 — both make the latch predicate harder to trip. So the pre-fix
latch *rate* in `hf_air_reverse` comes in below MANPAD's 7/18. If Hellfire
latches at or above MANPAD's rate, my model of the mechanism is wrong.

**V3 — post-fix, zero closing latches.** Same as the author's P3: no Hellfire
latch on any physically-closing tick, in any lane, on the post-fix build.

**V4 — no lane regresses.** No Hellfire lane has a post-fix hit rate more than
5 points below its pre-fix hit rate. A larger drop blocks the merge.

**V5 — `Hellfire.strykershorad` behaves like its parent.** It inherits the
whole `Projectile` block bar `Speed` (400) and `MaximumLaunchSpeed`, so its
ground lanes should look like the ATGM ground lanes: near-zero latches both
builds, high hit rate both builds.

## Gap 2 — TerrainHeightAware

**V6 — the incline branch cannot fire in this mod, so the false positive is
structurally impossible.** `InclineLookahead` reads
`world.Map.Height[cell] * 512`. `Map.Height` is only populated when
`Grid.MaximumTerrainHeight > 0`; ww3mod's `MapGrid` (mod.yaml:320) does not set
it, so it takes the engine default of 0. Every one of the 171 `map.bin` files
in the repo carries `heightsOffset == 0`. Therefore `predClfHgt`, `predClfDist`
and `lastHt` are identically 0 on every map, and the climb branch
(`Missile.cs:669`, `TerrainHeightAware && diffClfMslHgt >= 0 && !allowPassBy`)
reduces to `pos.Z <= 0`, which is not "rising ground" at all.

Falsifiable form: **no traced missile with `TerrainHeightAware` ever records
`p.z <= 0` while in `homing` or `hitting` with a live target.** If one does,
the branch is reachable after all and I must chase it.

**V7 — a terrain lane cannot be built without a mod-wide change.** `MapGrid` is
read from the mod manifest (`Map.cs:400`, `modData.Manifest.Get<MapGrid>()`),
not from map rules, so no scenario-local override can turn heights on. I
predict I cannot construct the lane the brief asks for without editing
`mods/ww3mod/mod.yaml`, which is out of scope.

## Gap 3 — the hover shift

**V8 — the shift reproduces on the seed-only build.** A build carrying only the
`lastTargetPosition` constructor seed, run on `test-missile-latch-probe` at seed
20260813, puts `air_hover` at 3 latches and 83% hits — matching the full post-fix
build, not the pre-fix 0 latches / 100%. That is what "the trajectory change, not
the detector" predicts.

**V9 — and it reproduces per-missile, not just in aggregate.** The same missile
ids the author paired (the three that worsened to 227/231/197 and the three that
improved to 144/74/115) show those same closest-approach values on the seed-only
build. Aggregate agreement with three latches could be coincidence; six matching
per-missile closest approaches cannot.

If V8 fails — the seed-only build leaves `air_hover` at 0 latches — then the
detector change is doing something the author's analysis does not explain, and
that is a DO NOT MERGE finding.
