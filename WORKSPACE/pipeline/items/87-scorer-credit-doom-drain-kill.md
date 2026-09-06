### 87. The scorer charges the doom-drain finishing blow as a death and credits it to nobody

`[DECISION ITEM — fix the scorer, or read the swing metric with the bias. Benchmark-adjacent; do not change silently.]`

**Perceived:** the benchmark's "kills − deaths" swing says `@stable` out-trades `@experimental` by ~$7,300 a match; about $4,490 of that is an accounting artefact.

**Source:** `WORKSPACE/audits/260906-baseline-deaths-audit.md` (worker f7230dd0, 6c34eb62, merged @ f01e00d2). Filed at `main @ e8e57ada`.

---

`UpdatesPlayerStatistics.Killed` charges `DeathsCost` at `PlayerStatistics.cs:335`, above the `if (e.Attacker == null || e.Attacker == self) return;` gate at `:341`, and credits `KillsCost` at `:362`, below it. The doom model delivers the normal killing blow as **self-inflicted** damage (`ChangesHealth.cs:86` drains via `self.InflictDamage(self, …)`; `AutoTarget.cs:244` deliberately stops shooting a `critical-damage` unit), so across the 260905 corpus **3,068 of 7,616 deaths (40.3%), 49.5% of all value lost**, are charged to the victim and credited to no one. Count columns balance (both sit below the gate) — the one sanity check a reader can do cannot fail.

**Options:** (1) credit the last real attacker — remember the last non-self, non-null attacker per actor and credit it when the finishing blow is self-inflicted within N ticks (prove the field is never client-local before any `[Sync]`); (2) leave the scorer and read every swing metric with the bias (win rates are unaffected: uncredited share 41.6% vs 43.0% of each bot's own deaths on S2); (3) drop the swing metric from LADDER readings. Option (1) changes stat surfaces for humans too (end-game score screen) — say so.

**Settling the split inside the uncredited set** (vehicle drain vs heli crash-burn vs crew fire ladder) costs one diag line naming the attacker in `BotVsBotMatchWatcher` and a seed-1017 rerun — deterministic per seed.
