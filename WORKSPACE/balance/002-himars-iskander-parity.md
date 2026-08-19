# 002 — HIMARS ↔ Iskander: equal cost, strictly dominant warhead

Status: SUPERSEDED 2026-08-20 — do not act on the recommendation below
Source: `260802-parity-audit.md` §2 (himars ↔ iskander)

> **Superseded by [`260819-strike-shorad-parity.md`](260819-strike-shorad-parity.md).** The headline conclusion — Iskander dominates at equal cost — **holds, and is larger than recorded here.** But this doc's line references have gone stale (HIMARS `Cost` is at `vehicles-america.yaml:1009`, Iskander at `vehicles-russia.yaml:912`), and, more importantly, **it never mentions HP or armour**, which is where much of the gap actually lives: HIMARS `HP: 6000` vs Iskander `HP: 10000`, and HIMARS declares `Armor: Type: Light` with **no `Thickness` node at all** (`vehicles-america.yaml:1026-1027`) where Iskander has `Thickness: 15`. `Thickness: 0` makes `DamageWarhead` skip armour reduction entirely (`DamageWarhead.cs:217`), so HIMARS takes 100% of every hit.
>
> **Do not apply this doc's Option A ("Iskander → 8000").** It would price Russia out of a unit to hide a US authoring omission. Fix the omission first and re-measure. The successor's ranked options, and the live user question, are in [`../AWAITING-USER.md`](../AWAITING-USER.md) item 3.
>
> Also note the §22-25 claim *"no compensating US advantage visible in the static stats"* is now known to be **understated rather than wrong** — the platform comparison it waves off is itself part of the gap.

## Evidence

Both long-range strike vehicles cost **6000**:

- HIMARS: `mods/ww3mod/rules/ingame/vehicles-america.yaml:999`
- Iskander: `mods/ww3mod/rules/ingame/vehicles-russia.yaml:913`

But the Iskander warhead strictly dominates the HIMARS warhead on every axis
(`mods/ww3mod/rules/weapons/weapons-explosions.yaml`, verified first-hand):

| Axis | HIMARSExplosion (WE:558-588) | IskanderExplosion (WE:521-551) | RU edge |
|---|---|---|---|
| Direct damage | 36000, 80% falloff @ max | 54000, 100% @ max | +50% dmg, no falloff |
| Spread wave | 2500 dmg, pen 1800, @768 | 4000 dmg, pen 2500, @1024 | +60% dmg, +39% pen, wider |
| Shockwave | 7000 dmg, pen 1500, r2 c512 | 12000 dmg, pen 2000, r4 c0 | +71% dmg, +33% pen, double radius, no inner dead zone |

There is no compensating US advantage visible in the static stats (platform
HP/speed/cost do not offset a strict warhead dominance at equal price). This
is the audit's clearest cross-faction imbalance: at identical budget cost, the
RU strike option deletes strictly more army value per shot.

Caveat: static analysis cannot weigh reload time × accuracy × range
interactions end-to-end; the parity tournaments quantify the net effect.

## Proposed change

Two mutually exclusive options — **user picks one** (or rejects both):

**Option A (price the difference):** raise Iskander cost,
`vehicles-russia.yaml:913`: `Cost: 6000` → `Cost: 8000`.
Keeps the doctrinal flavor (Iskander as the bigger, scarier ballistic strike)
and prices it accordingly. No warhead edits.

**Option B (converge the warheads):** reduce IskanderExplosion toward the
HIMARS envelope in `weapons-explosions.yaml:521-551`: direct 54000 → 42000,
shockwave radius r4 → r3. Keeps costs symmetric; Iskander stays somewhat
stronger but not strictly dominant per dollar.

Recommendation: **Option A** — smaller diff, preserves flavor asymmetry the
mod intends elsewhere, and cost is the mod's universal balancing lever
(budget-allocation model, `DOCS/reference/game-model.md`).

## Expected effect

- Option A: RU pays a 33% premium per strike platform; fewer Iskanders per
  match under the AI's budget ceilings, dampening RU late-game strike edge.
- Metric: `faction_winrate_pct` across `tournament-parity-cross-usru` +
  `-swapped` (20 seeds each), before vs after; also per-match army-value
  curves in the verdict JSON for strike-exchange spikes.

## Risk

- Option A: AI purchase logic keys on cost thresholds — verify the RU bot
  profile (`mods/ww3mod/rules/ai/ai.yaml`) still buys Iskanders at 8000
  within its budget ceilings, else the change silently deletes the unit from
  bot play (audit §4 documents those ceilings).
- Option B: touches a shared weapons file; IskanderExplosion may be
  referenced by campaign/scenario content — grep before applying.
- Both: if measured cross-faction winrate is already ~50%, the dominance is
  being paid for somewhere the static audit missed — in that case REJECT and
  document where the offset lives.
