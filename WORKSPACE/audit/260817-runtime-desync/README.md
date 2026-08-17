# Cross-runtime determinism probe — raw traces, 2026-08-17

Per-net-frame sync-hash traces from `Test.SyncHashLog`. Each file's `# runtime` header was written by the
engine from `Platform.RuntimeVersion`, so a file states which runtime produced it — do not infer that from
these filenames, and do not trust a filename over a header.

Regenerate a comparison with:

```sh
python3 tools/autotest/diff-synchash.py <a>.tsv <b>.tsv
```

## Long probe — full 12-minute bot match

`tournament-arena-composition-2p` + `tools/autotest/tournament-combat-12min-combatweighted.yaml`,
seed 20260817, 6000 net frames each (18000 world ticks — the complete match, ended by the time-limit win
rule, not by a wall-clock kill). 55 kills, 22 unit types, 4 aircraft produced.

| file | runtime | role |
|---|---|---|
| `e6.tsv` | 6.0.36 | what the shipped launcher actually selects on this machine |
| `e8-first.tsv` | 8.0.30 | the 2026-08-16 desync host's major |
| `e10.tsv` | 10.0.11 | the 2026-08-16 desync friend's major |
| `e8-repeat.tsv` | 8.0.30 | determinism control — same runtime, same seed, run twice |

All four data sections hash to `0d638c8495b4b96a9b685b0239a21a18`:

```sh
for f in e6 e8-first e10 e8-repeat; do grep -v '^#' $f.tsv | md5; done
```

## Short probe — combat / damage float path

`test-balance-tank-mass`, 4v4 Abrams vs T-90 to the death. Exercises `DamageWarhead`'s range-falloff and
directional-armour float arithmetic feeding `[Sync] Health` — the shortest float→hash path in the codebase.

| file | runtime | seed | role |
|---|---|---|---|
| `b8-first.tsv` | 8.0.30 | 700017 | baseline |
| `b8-repeat.tsv` | 8.0.30 | 700017 | determinism control |
| `b10.tsv` | 10.0.11 | 700017 | comparison |
| `b8-otherseed.tsv` | 8.0.30 | 700018 | **sensitivity control — must diverge, and does, at net frame 1** |

The first three are identical across all 111 frames and produced the same verdict to the hit point
(`ttk=13.2s | survivors=2/4 | hp=30392/112000`). The fourth is why "identical" means something here.
