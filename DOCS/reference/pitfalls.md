# PITFALL Comment System

Recurring traps get a one-line `// PITFALL:` (or `# PITFALL:` in YAML) comment **at the temptation site** in the code — the line a careless reader would actually be looking at when about to fall in. To list every known trap: `git grep PITFALL`.

The short version lives in `CLAUDE.md`; this doc is the full spec.

## The temptation-site rule

An anchor only helps if the reader's eyes are on it when at risk. So:

- ✅ trap = the file/line I'm editing → anchor at that line
- ⚠️ trap = an API I call from elsewhere → anchor at the API definition (partially works via grep)
- ❌ trap = a universal habit ("don't do X anywhere") → no anchor location helps. Use a hook or a one-liner in CLAUDE.md's engine code rules.

Placing a PITFALL where the broken *code* lives (rather than where the *temptation* arises) is dead weight — see commit history for the Log.cs case where this went wrong.

## Format

- Literal tag `PITFALL` (greppable). One line, WHY only — what breaks if you ignore it.
- Link out for longer context: `// PITFALL: facing is counterclockwise — see DOCS/reference/conventions.md#wangle`. *(Corrected 2026-08-19: this example pointed at `architecture.md#wangle`, which has never existed — the WAngle section is in `conventions.md`. A template's example gets copied verbatim into real comments, so a broken one propagates.)*
- At the temptation line, not in a function header. Cap ~3 per file — more is a refactor signal.
- Date when tied to an incident: `// PITFALL (2026-03): Cost: 1 shipped to main, broke balance`.

## When to write one

- Bug fix where the root cause would surprise a reader.
- Non-local invariant enforced elsewhere ("don't reorder these two lines").
- A trap Claude or the user has hit more than once.
- An OpenRA quirk that bites only WW3MOD's modified usage.

## Don't write one for

- "What" descriptions — well-named code is enough.
- Generic best-practice (null checks, input validation) absent a specific incident.
- One-shot fixes — comments are for *recurring* traps.
- **Universal anti-patterns** (don't use X anywhere) — the temptation arises in arbitrary files. Use a pre-commit hook (see `tools/git-hooks/`) or an engine code rule in CLAUDE.md.

## Pruning

When changing code near a `PITFALL`, re-read it. Outdated → remove or update. A wrong PITFALL is worse than no PITFALL because it will be trusted.

## How the system grows

New PITFALLs are added *during bug fixes*, not via mass passes. AUTOTEST step 8 prompts for one after every green; RELEASE bug-fix flow does the same; FINALIZE checks for them in the wrap. The compounding happens at the moment of fix when context is freshest — that's the only phase that scales.

**Backfill (occasional, high-precision only):**

```bash
git log --grep='regression\|came back\|still broken\|again\b' --oneline -- '*.cs' '*.yaml'
```

Bug fixes that explicitly note recurrence are real PITFALL candidates. One-shot bugs usually aren't — adding anchors for them creates noise that erodes the signal of the real ones. Avoid exhaustive "walk every file" passes; they over-comment and the bar slips.
