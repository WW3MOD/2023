# End-of-Message Block — Full Spec

The compact spec (glyph tables, order, terminal rule) lives in `CLAUDE.md` and is authoritative. This doc adds the rationale and worked examples.

## Reading model

Reading is bottom-up: terminal status glyph at the very bottom, supporting detail above. The user reads the terminal glyph first to identify the tab and what's expected of them.

**Skip the block** for trivial responses — one-line factual answers, pure clarification questions, or any reply where the block would be bigger than the answer itself. Mid-turn narration before tool calls ("Reading the file", "Committing now") stays as plain prose; the block rule applies only at end-of-turn. If even one line feels like padding, skip the block entirely.

## Per-category discipline

- `🧪` — absence means tests/build passed. Only include on issue.
- `👀` — only for specific behaviors to watch for. "Go try it" is implicit otherwise.
- `💡` — only for genuine new ideas, not restatements of agreed work.
- `⚠️` — only for real tradeoffs, not generic disclaimers.

## Examples

Triage finished, items added to v1 tracker:

```
📁 WORKSPACE/RELEASE_V1.md

✅ added 4 bugs to Phase B (artillery burst, ATGM lock, drone autotarget, palette)
✅ moved garrison overhaul to [T] (testing) — 5 specific checks listed
✅ deferred ammo-cost-money to v1.1

📦
```

Bug fix shipped, idea floated for later:

```
📁 engine/OpenRA.Mods.Common/Traits/Air/HeliEmergencyLanding.cs:142

✅ 😬 fixed crashed-heli capture — added foreign-crew check before ownership transfer
💡 same logic could give us recoverable wrecks for vehicles in v1.1

🏁
```

Playtest set up, game ready:

```
📁 WORKSPACE/playtests/260503_1530_garrison.md

✅ build clean, focus list written (garrison ports, ownership transfer, suppression duck/recall)

👀 launch a 2v1 on River Zeta, garrison both ports, force-fire from inside, take damage to 60%+ suppression

⏭️
```
