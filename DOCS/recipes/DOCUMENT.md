# DOCUMENT — describe how the game is intended to work

**Trigger:** `DOCUMENT <topic>` (or `DOCUMENT` when the topic is obvious from context).

**Gives you:** a short, structured, **game-focused** description of a mechanic, faction, unit, building, or system — written so a curious player could read it and understand the *experience*. The agent reads from these on later sessions to make better game-aware decisions.

**Output lives in [`DOCS/gameplay/`](../gameplay/README.md)** — one `.md` per topic, plus a `README.md` index.

---

## What "documentation" means here

This is **the project of writing down how the game is supposed to work, feel, and look** — separate from the technical `reference/` docs (which describe code architecture and engineering trade-offs).

A gameplay doc:

- Describes the mechanic from a **player's vantage point**: what they see, what they do, what they feel.
- States the **rules** (numbers, timings, conditions) that govern the mechanic.
- Notes the **design intent** behind the rules — what experience the rule was chosen to create.
- May include a few code pointers or YAML snippets where they **tie the rules to the implementation**, but does NOT explain the C# class hierarchy or trait API. That's `reference/architecture.md`'s job.

A gameplay doc is NOT:

- A balance spreadsheet (those go in `WORKSPACE/balancing/`).
- An implementation walkthrough (those go in `reference/`).
- A how-to-build-an-AI guide (that's `WORKSPACE/ai/`).
- A patch note or changelog (commit history is fine for that).

If a curious player asked "how does X work in WW3MOD?", a gameplay doc is the answer.

## What I do when triggered

1. **Read the existing gameplay docs** in `DOCS/gameplay/` so the new doc has the same voice and depth.
2. **Research the mechanic in the code** — relevant YAML, C# traits, autotests. Verify everything I write is grounded in actual behaviour, not assumed.
3. **Write the doc** to `DOCS/gameplay/<topic>.md`:
   - Top of file: one-paragraph **What it is** for a player who just opened the doc.
   - **How it works** — rules, numbers, conditions. Be specific (e.g. "Technicians capture neutrals in 20 ticks ≈ 0.8 sim-sec at base speed").
   - **What you'll see / feel / do as a player** — the perspective.
   - **Strategic implications** — why this matters in a match.
   - **Code pointers** (last section, optional) — a few file:line refs that the curious can follow.
   - **Open questions / flagged uncertainties** — explicit. If I'm not sure about a number, I say so.
4. **Add the new doc to `DOCS/gameplay/README.md`** index.
5. **Cross-link** from neighbours — if `capturing.md` mentions Supply Routes, link to `supply-route.md`.

## When I add to the project even without an explicit trigger

This recipe is **always-on** for discovery updates:

- If during normal work I learn something non-obvious about how the game works that isn't in `DOCS/gameplay/` yet, I either:
  - **Add it** — when I'm confident from code/playtest evidence;
  - **Flag it** in the end-of-message block with `💡` and a one-line note if I'm not 100% sure, so the user can confirm before I commit it.

The bar is: **a curious player should never have to grep code to learn a rule that the agent has already learned.**

## Conventions

- One file per topic. If a topic grows past ~400 lines, split it (e.g. `capturing.md` → `capturing.md` + `capturable-structures.md`).
- Player vocabulary first, code names in parentheses when they appear (e.g. "Technicians (`tecn`)").
- Use existing project terms from `CLAUDE.md` and `DOCS/reference/supply-route.md` — Supply Route, beachhead, sector, etc.
- Avoid implementation jargon unless the player has to know it. "Production queue" yes; "BotBlackboard task posting" no.
- Numbers should be sourced — link to the YAML file:line or quote the trait setting.
- Don't promise content not in the game today. If a doc describes something planned, mark it `[planned]` or `[not yet wired]`.

## Open question to resolve over time

The existing `DOCS/reference/supply-route.md` and `DOCS/reference/economy.md` already read partly like gameplay docs (they describe player-facing mechanics, not just code). They could eventually move to `gameplay/`. For now they stay where they are; new docs go in `gameplay/`. We'll fix the seam later when the gameplay collection is larger and the right boundary is obvious.

## Cross-reference

- Recipe index: [`DOCS/recipes/README.md`](README.md)
- The gameplay index itself: [`DOCS/gameplay/README.md`](../gameplay/README.md)
- Modes: [`DOCS/modes/`](../modes/) — orthogonal; documentation work happens in any mode.
