# WW3MOD Development Collaboration Guide

How to work efficiently with AI assistance (Claude Code) on this project. Written for FreadyFish, CmdrBambi, and any future contributors.

---

## 1. The Build-Test-Verify Loop

This is the core development cycle:

```
1. AI makes changes (C# or YAML)
2. AI runs `./make.ps1 all` to compile — catches errors immediately
3. AI adds temporary debug logs to the changed systems
4. AI launches the game via `launch-game.cmd`
5. Human plays, observes, reports back
6. AI reads log files to correlate with human observations
7. Once verified: AI strips debug logs, commits clean code
```

### Important: Close the game before building
The build will fail if OpenRA is running because DLLs are locked. Always close the game before asking for code changes.

### Debug Logging (not Console.WriteLine!)

OpenRA has built-in logging. **Never use `Console.WriteLine`** — it fires every tick and causes massive log spam.

| Method | Where it shows | Use for |
|---|---|---|
| `Game.Debug("message")` | In-game chat + log file | Things you want to see live while playing |
| `Log.Write("debug", "message")` | Log file only | Per-tick data, noisy diagnostics |

Log files are at: `Documents/OpenRA/Logs/`

**Debug log discipline:**
- Commit the feature BEFORE adding debug logs
- Never commit debug logs
- Strip logs after testing, then commit any fixes

## 2. Communication Patterns

### What works best

| You say | AI does |
|---|---|
| "Do X" | Does it and commits. No discussion. |
| "I saw X in-game" | Investigates, diagnoses, fixes, commits. |
| "Let's test" | Adds debug logs, builds, launches. |
| "X feels wrong" | Asks 1-2 clarifying questions, then fixes. |
| *paste error/screenshot* | Reads the stack trace, fixes the root cause. |

### What's less efficient

- **"What do you think about..."** for simple changes — just say "do it", you can revert
- **Explaining things you already know** — assume expertise, skip tutorials
- **Multiple unrelated tasks in one message** — fine for small things, but complex features benefit from one focus per message

### Bug Reports

The most useful format:
```
"Infantry didn't suppress when hit by MG fire. They were standing in the open,
MG was about 5 cells away. No prone animation triggered."
```

Include: what happened, what should have happened, rough conditions. AI will figure out the rest.

## 3. Session Structure

### Start of session
- AI refreshes context from CLAUDE.md, KNOWN_BUGS.md, TODO.md
- Human states the focus: "today we're doing garrison polish" or "fix these 3 bugs"
- AI checks git status for any uncommitted work

### During session
- Small increments: change, build, commit, repeat
- For big features: AI outlines approach in 2-3 sentences, human approves, AI builds
- Human can playtest while AI works on the next thing in parallel

### End of session
- All work committed clean (no debug logs)
- CLAUDE.md updated if anything changed architecturally
- KNOWN_BUGS.md / TODO.md updated if bugs were fixed or discovered

## 4. Commit Discipline

- **Commit frequently** — small commits are preferred over large batches
- **Never push** — the human pushes manually after review
- **Descriptive messages** — "Fix garrison port targeting for AT weapons" not "fix bug"
- **Debug logs never committed** — they exist only in working tree during testing

Typical sequence:
```
git commit "Add SupplyProvider distance-based delay"    ← clean feature
  ... add Game.Debug() logs for testing ...
  ... human plays and verifies ...
  ... strip logs, fix any issues found ...
git commit "Fix SupplyProvider delay calculation at max range"  ← clean fix
```

## 5. Testing Strategy

### Unit Tests (NUnit)

The test project lives at `engine/OpenRA.Test/` and uses NUnit 3. Tests run via:
```bash
dotnet test engine/OpenRA.Test/OpenRA.Test.csproj
```

**What CAN be unit tested** (no running game needed):

| Area | Examples |
|---|---|
| Math & formulas | WDist calculations, damage falloff, suppression decay |
| AmmoPool logic | GiveAmmo/TakeAmmo clamping, reload tick math, SupplyValue costs |
| SupplyProvider math | Distance-to-delay calculation, supply depletion |
| YAML parsing | MiniYaml round-trips (already exists) |
| Targeting scores | Weapon-role matching, value-based priority |

**What CANNOT be unit tested** (need in-game verification):

- Movement, pathfinding, visual behavior
- Garrison port positions and firing arcs
- Aircraft flight paths
- UI widgets, render pipeline
- Multi-actor interactions requiring a full World

### Writing New Tests

WW3MOD-specific tests go in `engine/OpenRA.Test/OpenRA.Mods.Common/`. Pattern:

```csharp
using NUnit.Framework;
namespace OpenRA.Test
{
    [TestFixture]
    public class MySystemTest
    {
        [Test]
        public void DescriptiveTestName()
        {
            // Arrange → Act → Assert
        }
    }
}
```

### Regression Testing Workflow

When a bug is found during playtesting:
1. Write a test that reproduces the math/logic failure
2. Verify the test fails
3. Fix the bug
4. Verify the test passes
5. Commit test + fix together

This prevents the same bug from returning after future changes.

## 6. Pre-Flight Checklist

Before every game launch, verify:

1. **Build succeeds** — `./make.ps1 all` exits clean
2. **No Console.WriteLine** — grep changed files for stray console output
3. **No test values** — no `Cost: 1` or other placeholder values in YAML
4. **New traits registered** — if a new C# trait was added, check mod.yaml if needed

The `ww3-dev.ps1` helper script automates checks 1-3.

## 7. Parallel Work

While the human is playtesting, the AI can:
- Work on the next feature
- Research OpenRA source for upcoming tasks
- Write regression tests for the feature being tested
- Update documentation

Just say: "go ahead on X while I test"

## 8. File Organization

| File | Purpose | Who updates |
|---|---|---|
| `CLAUDE.md` | AI instructions, architecture docs, current state | AI (auto-updates) |
| `DOCS/TODO.md` | Prioritized v1 release tasks | Both |
| `DOCS/KNOWN_BUGS.md` | Active bugs and fixed bugs for reference | Both |
| `DOCS/IDEAS.md` | Feature ideas for later | Both |
| `DOCS/COLLABORATION.md` | This file — how to work together | Both |

## 9. Common Gotchas

1. **Game must be closed before building** — DLLs are locked while running
2. **Don't use `Console.WriteLine`** — use `Game.Debug()` or `Log.Write()`
3. **Aircraft `Cost: 1`** — test values get left in YAML. Always verify costs after changes
4. **CanSlide vs fixed-wing** — completely separate code paths in Fly.cs. See CLAUDE.md
5. **YAML blank lines matter** — templates must be separated by blank lines
6. **SeedsResource crashes** — maps without IResourceLayer crash if SeedsResource actors exist
7. **FrozenActor.Actor can be null** — always null-check after superweapons

## 10. Getting Started (New Contributors)

1. Clone the repo: `git clone https://github.com/WW3MOD/2023.git`
2. Build: `./make.ps1 all` (Windows) or `make all` (Linux/Mac)
3. Run: `launch-game.cmd` (Windows) or `./launch-game.sh`
4. Read `CLAUDE.md` for full architecture overview
5. Read `DOCS/TODO.md` for current priorities
6. Read `DOCS/KNOWN_BUGS.md` for known issues
