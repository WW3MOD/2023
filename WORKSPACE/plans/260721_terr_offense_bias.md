# RECON / Implement-ready plan — Territorial balance-of-power offense bias (cycle 1, slice 1)

**Date:** 2026-07-21
**Mode:** READ-ONLY recon → implement-ready plan. No code changed, nothing built, no game run.
**Researched against:** `main` @ `5c07d1a8` (HEAD; `git status` clean bar untracked `.maestro/`,
`.claude/scheduled_tasks.lock`, `tools/autotest/__pycache__/`). Code cited by file:line below.
**Authority for scope:** `WORKSPACE/ai-bench/reports/260721_rethink2.md` (RETHINK #2, commit
5c07d1a8) — cycle 1 = "the smallest behavior-bearing slice of the territorial layer: a
balance-of-power offense bias that reads the existing `InfluenceMap`." Loop rules:
`WORKSPACE/ai-bench/DOCTRINE.md`. North Star: `DOCS/design/ai-realism.md` §1.

---

## 0. One-paragraph summary

Add an `@experimental`-only, default-frozen **balance-of-power rescale** to
`PoiOffensiveBotModule` that reads the already-computed `InfluenceMap` friendly *and*
enemy grids, and biases the offensive axis ranking toward enemy/neutral targets that sit
on a **contact cell where we hold local superiority** (advance the front where safe),
while **damping** targets deep in enemy-dominated cells (stop lunging into strength — the
exact S2 re-baseline failure mode) and **leaving empty-ground targets untouched** (the
frontline guard against a degenerate economy grab). The mechanism is a near-verbatim clone
of the existing `RescaleSrPressure` pattern (`PoiOffensiveBotModule.cs:340-358`): one pure
math function, one rescale method, five new Info fields set only on the `@experimental`
YAML profile, plus `[exp-terr]` telemetry. `@stable`/Normal/Rush/Turtle stay byte-identical.

---

## 1. Why this mechanism (grounding in the data + the code)

- **The new input the offense has never consumed is *friendly* influence.** Today the
  offensive score keys on enemy influence only (`ScoredPoi.EnemyInfluence`, sampled by
  `PoiMap.SampleThreat`, `PoiMap.cs:481-498`) via `ThreatFactor` (safe ×100 / mild ×40 /
  hostile ×10, `PoiScoring.cs`/`PoiMap.cs:542-550`). A target with **high enemy AND high
  friendly** influence (a front where we are locally strong) gets the *same* ×10 hostile
  damp as a target with high enemy and **no** friendly presence. `InfluenceMap` already
  exposes `GetFriendlyInfluence(perspective)` (`InfluenceMap.cs:143-153`) — **it is never
  read by the offense.** Consuming it is a genuinely new lever, not a re-weight of the old
  axes (RETHINK §1).
- **It targets the two stuck bars.** The re-baseline (`runs/260721_regime_rebaseline.md`)
  found Exp ≈ Stable: S2 swing edge −$350, and same-faction play collapsing into a passive
  economy race (engagement −5–6×, 3/10 zero-combat). "Push where the enemy is comparatively
  weak; the front steps forward where safe" converts the stalemate into contact-seeking
  advance — precisely what S2 net swing + the engagement floor reward (RETHINK §1, Exec).
- **It directly attacks the observed loss mode.** The re-baseline's worst Exp cells (seeds
  6017 swing −4200, 8017 −4950) were "Exp pushed in and got out-traded badly while Stable
  defended." The damp-into-strength half of this bias is the direct antidote.
- **It is the North Star on-ramp** (`DOCS/design/ai-realism.md` §1): safe/grayzone/enemy,
  push-where-weak, front-steps-forward. Slice 1 uses the raw influence share as a proto
  classification; slice 2 (cycle 5) swaps the *input* for a fog-respecting classification
  without touching this consumer (§8 below).

---

## 2. Where the change lives (and why not PoiMap)

**Location: `PoiOffensiveBotModule` (per-player, gated `enable-ai-experimental`), NOT
`PoiMap`.** `PoiMap` is a **shared world singleton** (`world.yaml:299`, one instance queried
by *both* bots) — the hard constraint forbids retuning it in place, and it has no per-profile
YAML to opt only `@experimental` in. The module is the established home for
`@experimental`-only, default-off scoring deltas: `SkipOutOfAmmoUnits`,
`CohesionSwitchEnabled`, and especially **`SrPressureScoreMultiplier` + `RescaleSrPressure`**
(`PoiOffensiveBotModule.cs:108-115, 214-218, 340-358`) are the exact template — a
post-`GetOffensiveTargets` rescale of the scored list, guarded so multiplier==inert is a
no-op and the frozen controls keep their exact ranking. We clone that shape.

This also respects the drift rule (`architecture.md:309`): a behavioural Info field on a
shared trait must **default to the frozen behaviour** and be opted in per-profile — here the
master switch defaults **off** and the sub-multipliers default **100 (inert)**, so even if
the switch were flipped the score is unchanged until the `@experimental` YAML supplies active
values.

---

## 3. Files / traits / fields to touch

### 3.1 `engine/OpenRA.Mods.Common/Traits/BotModules/PoiOffensiveBotModule.cs`

**(a) New Info fields** — insert in `PoiOffensiveBotModuleInfo` after
`SrPressureScoreMultiplier` (after `:115`, before `Create` at `:117`). All default to the
frozen/inert behaviour:

```csharp
[Desc("EXPERIMENTAL territorial balance-of-power bias. When true, rescale each offensive",
    "axis score by BalanceOfPowerFactor (friendly vs enemy InfluenceMap share at the target",
    "cell) so the army advances the front into comparatively-weak enemy sectors and stops",
    "lunging into enemy-dominated ground. OFF by default so Stable/Normal are byte-identical;",
    "only PoiOffensiveBotModule@experimental turns it on. Mirrors the SrPressureScoreMultiplier",
    "/ CohesionSwitchEnabled default-off pattern.")]
public readonly bool BalanceOfPowerBiasEnabled = false;

[Desc("Our local influence SHARE (%) at/below which a CONTACT cell counts as enemy-dominated",
    "→ damp the axis (don't lunge into strength). Share = friendly*100/(friendly+enemy).")]
public readonly int BopWeakSharePct = 40;

[Desc("Our local influence SHARE (%) at/above which a CONTACT cell counts as ours to press",
    "→ boost the axis (advance the front where we are comparatively strong).")]
public readonly int BopDominantSharePct = 60;

[Desc("Axis-score multiplier (x100) for a target on a contact cell we DOMINATE (share >=",
    "BopDominantSharePct). >100 boosts. Default 100 = inert (frozen).")]
public readonly int BopBoostMultiplier = 100;

[Desc("Axis-score multiplier (x100) for a target on a contact cell the ENEMY dominates",
    "(share <= BopWeakSharePct). <100 damps. Default 100 = inert (frozen).")]
public readonly int BopDampMultiplier = 100;
```

Note: an even front (weak < share < dominant) and an **empty-ground target (no enemy
influence at the cell)** both map to ×100 — the neutral default carries the frontline guard,
no separate field needed.

**(b) New instance fields** — near `poiMap`/`poiMapResolved` (`:140-141`):
```csharp
InfluenceMap influenceMap;
bool influenceResolved;
```

**(c) Resolve + call** — in `Reevaluate`, immediately after the SR-rescale block
(`:214-218`), before `if (targets.Count == 0)` (`:220`):
```csharp
if (!influenceResolved)
{
    influenceMap = world.WorldActor.TraitOrDefault<InfluenceMap>();
    influenceResolved = true;
}
if (Info.BalanceOfPowerBiasEnabled && influenceMap != null)
    targets = RescaleByBalanceOfPower(targets, tick);
```
(`world.WorldActor.TraitOrDefault<InfluenceMap>()` is exactly how `PoiMap` resolves it,
`PoiMap.cs:522`. Null → skip = neutral, so a map without the trait is safe.)

**(d) New method** — alongside `RescaleSrPressure` (`~:340`). Caches the two grids **once**
per reeval (not per target), samples friendly+enemy at each target's grid cell, applies the
pure factor, rebuilds the `ScoredPoi` with the new score, re-sorts with the same comparator,
and emits `[exp-terr]` telemetry (§4):
```csharp
List<ScoredPoi> RescaleByBalanceOfPower(List<ScoredPoi> targets, int tick)
{
    var friendly = influenceMap.GetFriendlyInfluence(player);
    var enemy = influenceMap.GetEnemyInfluence(player);
    var gw = influenceMap.GridWidth;
    var gh = influenceMap.GridHeight;

    int boosted = 0, damped = 0, neutral = 0, frontCells = 0;
    for (var x = 0; x < gw; x++)
        for (var y = 0; y < gh; y++)
            if (friendly[x, y] > 0 && enemy[x, y] > 0) frontCells++;

    var scaled = new List<ScoredPoi>(targets.Count);
    foreach (var p in targets)
    {
        var (gx, gy) = influenceMap.MapCellToGridCell(p.Location);
        int f = 0, e = 0;
        if (gx >= 0 && gx < gw && gy >= 0 && gy < gh) { f = friendly[gx, gy]; e = enemy[gx, gy]; }

        var mul = PoiOffenseMath.BalanceOfPowerFactor(f, e,
            Info.BopWeakSharePct, Info.BopDominantSharePct,
            Info.BopBoostMultiplier, Info.BopDampMultiplier);

        if (mul == 100) { neutral++; scaled.Add(p); continue; }
        if (mul > 100) boosted++; else damped++;

        var newScore = p.Score * mul / 100;
        scaled.Add(new ScoredPoi(p.Actor, p.Kind, p.Action, p.Value,
            p.DistanceCells, p.EnemyInfluence, newScore));

        Log.Write("debug", $"[exp-terr] bop player={player.PlayerName} target={p.Actor.Info.Name}@{p.Location} " +
            $"action={p.Action} f={f} e={e} share={(f + e > 0 ? f * 100 / (f + e) : -1)} mul={mul} " +
            $"score={p.Score}->{newScore} tick={tick}");
    }

    scaled.Sort((a, b) => PoiScoring.CompareForOrder(a.Score, a.DistanceCells, a.Actor.ActorID,
        b.Score, b.DistanceCells, b.Actor.ActorID));
    Log.Write("debug", $"[exp-terr] reeval player={player.PlayerName} frontlineCells={frontCells} " +
        $"boosted={boosted} damped={damped} neutral={neutral} tick={tick}");
    return scaled;
}
```
Cost: two `gridW×gridH` int allocs + one grid scan, **once per reeval** (every
`ReevaluateInterval`=100 ticks) per bot — negligible next to the module's existing per-reeval
`world.Actors` LINQ. `GetFriendly/EnemyInfluence` return snapshots (`InfluenceMap.cs:143-166`).

**(e) New pure function** — in `PoiOffenseMath` (`~:579`, sibling of `Chebyshev`; unit-tested
in `PoiOffenseTest`, keeping the v3-portable "all decision math is pure" invariant):
```csharp
/// <summary>Balance-of-power axis multiplier (x100). f,e = friendly/enemy influence at the
/// target cell. NO CONTACT (e<=0) → 100 (neutral): empty ground is not a front — never
/// reward "lowest enemy influence anywhere" (the frontline guard). CONTACT (e>0): local
/// share r = f*100/(f+e); r>=dominant → boost, r<=weak → damp, between → 100 (even front).</summary>
public static int BalanceOfPowerFactor(int f, int e, int weakSharePct, int dominantSharePct,
    int boostMul, int dampMul)
{
    if (e <= 0)
        return 100;                       // no enemy presence → not a contact cell → neutral
    var share = f * 100 / (f + e);        // f>=0, e>0 ⇒ denominator>0
    if (share >= dominantSharePct)
        return boostMul;
    if (share <= weakSharePct)
        return dampMul;
    return 100;                           // even front → unchanged
}
```

### 3.2 `mods/ww3mod/rules/ai/ai.yaml` — `PoiOffensiveBotModule@experimental` only (`:180-210`)

Append after `SrPressureScoreMultiplier: 260` (`:210`). **MiniYaml: no blank line inside the
block; keep the one blank line before `PoiGarrisonBotModule@experimental` (`:212`).**
```yaml
		# Experimental territorial balance-of-power bias (cycle 1). Rescale each offensive axis by
		# the friendly-vs-enemy InfluenceMap share at the target cell: press contact cells we
		# dominate (share >= 60 → x150), damp cells the enemy dominates (share <= 40 → x60), leave
		# even fronts and empty ground untouched (x100). Advances the front where safe; stops
		# lunging into strength. Default OFF on @stable = byte-identical.
		BalanceOfPowerBiasEnabled: true
		BopWeakSharePct: 40
		BopDominantSharePct: 60
		BopBoostMultiplier: 150
		BopDampMultiplier: 60
```
`PoiOffensiveBotModule@stable` (`:718-734`) — **untouched** (no BoP fields ⇒ code defaults ⇒
switch off ⇒ byte-identical). Verify post-edit that the `@stable` block is unchanged.

### 3.3 `engine/OpenRA.Test/OpenRA.Mods.Common/PoiOffenseTest.cs`

Append a `BalanceOfPowerFactor` region (mirrors the `ThreatFactor`/`Chebyshev` test idiom).
Cases:
- `NoEnemyInfluence_IsNeutral` — `f>0,e=0` and `f=0,e=0` → 100 (the frontline guard).
- `WeDominateContact_Boosts` — e>0, share ≥ dominant → boostMul.
- `EnemyDominatesContact_Damps` — e>0, share ≤ weak → dampMul.
- `EvenFront_IsNeutral` — e>0, weak < share < dominant → 100 (e.g. f==e → share 50).
- `BoundaryInclusive` — share exactly == dominant → boost; exactly == weak → damp.
- `ZeroFriendlyWithEnemy_Damps` — f=0,e>0 → share 0 ≤ weak → dampMul (deep in enemy ground).

Expected NUnit: **291 → ~297**. No new files (telemetry via `Log.Write`; per-match
`debug.log` is already preserved by the harness, established by the cycle-1 capture markers).

---

## 4. Telemetry (`[exp-terr]`, RETHINK §5)

Emitted to `debug.log` in the existing `[exp-*]` idiom (`[exp-offense]`, `[exp-sr]`):
- **Per boosted/damped target:** `[exp-terr] bop player=<name> target=<t>@<cell> action=<a>
  f=<F> e=<E> share=<r> mul=<m> score=<old>-><new> tick=<t>` — *what cells were rated weak/
  strong and what bias was applied* (the deliverable ask).
- **Per reeval:** `[exp-terr] reeval player=<name> frontlineCells=<n> boosted=<b> damped=<d>
  neutral=<u> tick=<t>` — *did the front advance / did contact rise*. `frontlineCells` counts
  cells with both-sides influence from the cached grids (same definition as
  `InfluenceMapMath.CountFrontlineCells`, `InfluenceMap.cs:262`, without a third alloc).
- **Which axis decisions changed:** derive by diffing the top-`MaxAxes` target ids of the
  pre- vs post-rescale `targets` list (cheap set-diff inside the method); log
  `[exp-terr] axis-shift player=<name> nowTop=<ids> wasTop=<ids> tick=<t>` when they differ.

**How pass/fail is read:** the batch bars are computed from the per-match verdict JSON by
`tools/autotest/parse-s2-batch.py` (`kills_cost`/`deaths_cost`, unchanged — no scorer touch).
The `[exp-terr]` markers are for **human diagnosis** of *why* (grep the preserved
`debug.log`), exactly as SR-contestation was proven "live in-window" by grepping
`action=Pressure` axes. Growing `parse-s2-batch.py` into a marker analyzer is the seed of
roadmap item 6 and is **out of scope this cycle** (RETHINK §5: markers ship *alongside* the
lever, the analyzer grows later).

---

## 5. Verification plan

1. **Unit (bias fires correctly + the guard holds):** the `PoiOffenseTest.BalanceOfPowerFactor_*`
   cases in §3.3. This is the primary "the bias math is correct, and E=0 stays neutral" proof,
   fully deterministic, no World. `dotnet test engine/OpenRA.Test/OpenRA.Test.csproj -c Release`.
2. **Compile + full suite:** `./make.ps1 all`; NUnit green (~297/297).
3. **In-engine firing proof (no dedicated autotest):** there is no cheap Lua autotest for
   offense-axis scoring under a controlled influence field (it needs a full bot + army +
   enemy + hundreds of ticks for influence to build). Consistent with how **SR-contestation
   and the capture cycles were verified** (LADDER: "SR axis proven live in-window: 8/10
   matches open action=Pressure"), the firing proof is the `[exp-terr]` markers in the
   benchmark batch: grep `debug.log` for `mul=150`/`mul=60` lines and `boosted>0`. A dedicated
   `test-bop-bias` Lua scenario is **optional future hardening**, not a cycle-1 gate.
4. **Benchmark verify (the bar), paired seeds 1017…10017 vs `@stable`** — per the PROPOSED
   bars in `LADDER.md` / `runs/260721_regime_rebaseline.md`:
   - **S2 (`tournament-s2-combat-river-zeta` + mirror, N=10):** PASS = paired-relative
     median(Exp swing) ≥ median(Stable swing) **+ $1,000** AND sign-delta ≥ 7/10 AND
     both-spawn ≥ 3/5 each, **AND the blocking batch-validity gate ≥ 6/10 engaged**. Cycle
     success (RETHINK §1): **S2 relative swing turns positive WITHOUT dropping engagement,
     engaged-count ≥ 6/10** — win by fighting smarter, not less.
   - **S1 (`tournament-s1-eco-river-zeta` + mirror, N=10): non-regression only** — Exp
     win-rate ≥ 0.40 floor + capture parity (±2/10). BoP is a contact/combat lever; S1 offers
     no economy edge, expect ~neutral, must not regress.
   - This same batch gives the already-merged **TECN ferry** (`90a173c4`) its first regime
     verdict for free (RETHINK §0 caveat).
   - **Decision branch (RETHINK §4):** if swing improves but engagement stays < 6/10 → the
     quiet is proven to be map geometry → stand up the S2 forced-contact variant. If
     engagement ≥ 6/10 → the regime is fine, keep it.
   - *Batch runs need explicit user goahead (CLAUDE.md hard rule).*

---

## 6. Risks & guards

- **Frozen-control drift.** Master switch default **off** + sub-multipliers default **100**
  (double belt-and-suspenders vs `architecture.md:309`). Guard: confirm the `@stable` block is
  untouched and NUnit's frozen-control expectations hold; the `@experimental`-vs-`@stable`
  delta stays measurable.
- **"Push where weak" → empty-ground economy grab** (RETHINK §10). Structural guard: the
  `e<=0 → 100` neutral rule means a cell with no enemy presence is **never** boosted — only
  *contact* cells we dominate rise. Read engaged-count as a first-class cycle outcome, not
  just swing.
- **Over-aggression regression** — the exact re-baseline loss mode (Exp lunged in, out-traded,
  seeds 6017/8017). The `BopDampMultiplier<100` on enemy-dominated cells is the *direct*
  countermeasure; watch those two seeds as the improvement cells.
- **Determinism.** F/E come only from `InfluenceMap` (seeded, sync-safe, refreshed every 25
  ticks, `InfluenceMap.cs:76-83`); no wall-clock, no render state; grids cached once per reeval;
  re-sort uses the deterministic `PoiScoring.CompareForOrder`. Sim-safe.
- **MiniYaml blank-line merge** on the `ai.yaml` edit (project hard rule) — no blank line
  *inside* the `@experimental` block; preserve the blank line before the next top-level entry.
- **TECN-ferry confound** (RETHINK §0/§10). The ferry is an unmeasured `@experimental` delta
  already in the tree; if the batch reads worse than the re-baseline, A/B the ferry toggle
  before blaming the BoP term.
- **Influence share on a stale/empty grid early game.** Before armies meet, all targets have
  e=0 → all neutral → the bias is inert until contact exists — which is correct (nothing to
  advance into yet) and matches the North Star's intel-driven intent.

---

## 7. Effort estimate

**M.** Engine: 1 pure function + 1 rescale method + 5 Info fields + 2 resolve fields
(~70 LOC, one file). YAML: 6 lines, one profile. Tests: ~6 NUnit cases, one file. No new
files. Verify: the standard S2 batch + S1 non-regression (one run slot). Directly comparable
to the SR-contestation cycle (rescale-in-module + one YAML field + benchmark), which is the
literal template being cloned.

---

## 8. Forward-compatibility — how slice 1 grows into the full territorial layer (AIM-HIGH)

Slice 1 is deliberately shaped so **the offense consumer never changes** when the fog-respecting
layer lands (cycle 5, RETHINK §7 row 5). The seam is the pure factor's *input*:

- **Slice 1 (now):** input = raw `InfluenceMap` friendly/enemy **share** at the cell.
  `BalanceOfPowerFactor(f, e, …)` maps share → {boost / neutral / damp}. Those three buckets
  are already the proto **safe / grayzone / enemy** trichotomy (dominant-share = safe-to-advance,
  even = grayzone/front, enemy-dominant = hostile).
- **Slice 2 (cycle 5):** introduce a `TerritoryMap` world trait (or extend `InfluenceMap`) that
  produces a **per-cell classification** respecting shroud — the own-half-safe prior at t0,
  intel-driven reclassification, **no see-through-fog** (`ai-realism.md` §1). The factor becomes
  `TerritoryFactor(classification, …)` reading the enum instead of the raw share; the module
  plumbing (resolve world trait → cache per reeval → rescale → resort → `[exp-terr]` markers) is
  **identical**, and the call site is a one-line swap. The `[exp-terr]` markers extend to log
  classification transitions. **No throwaway architecture:** the boost/neutral/damp bucketing,
  the per-reeval cache, and the telemetry all carry forward; only the source-of-truth for
  "how strong are we here" upgrades from raw influence to fog-respecting intel.
- **Cycle 6 (reinforce-weak / spread-along-front):** the *defense* side reuses the same read.
  `GetDefendTargets` already samples enemy influence (`PoiMap.cs:393`); adding the friendly
  read biases reserves toward thin/threatened own sectors — the "strength shifted to where the
  line is thin" end state — on the identical substrate. The balance-of-power layer thus serves
  both the advancing front (offense) and the held line (defense) from one influence read.

Anti-overfit gate: cycle 4 (Polar Disorder rung, RETHINK §6) validates that this share-reading
push generalizes off River Zeta's geometry **before** slice 2 deepens it.

---

## 9. Implementation checklist (for the IMPLEMENT+VERIFY cycle)

1. Branch off `main` (e.g. `exp-terr-offense-bias`).
2. `PoiOffensiveBotModule.cs`: add 5 Info fields (3.1a), 2 instance fields (3.1b), resolve+call
   (3.1c), `RescaleByBalanceOfPower` (3.1d), `PoiOffenseMath.BalanceOfPowerFactor` (3.1e).
3. `ai.yaml`: add 5 fields + comment to `@experimental` only (3.2); leave `@stable` untouched;
   check blank lines.
4. `PoiOffenseTest.cs`: add the `BalanceOfPowerFactor` region (3.3).
5. `./make.ps1 all` + `dotnet test … -c Release` → green (~297/297).
6. Confirm `@stable` byte-identical (git diff shows only `@experimental` YAML + engine additions
   gated on the default-off switch).
7. **User goahead**, then the S2 batch + S1 non-regression verify (§5.4); log to `runs/`,
   update `LADDER.md`; eager-merge on pass.
8. New non-obvious code facts → `WORKSPACE/DISCOVERIES.md` (dated, file:line).
