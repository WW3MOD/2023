# PLAYTEST — focused human-driven test session

**Trigger:** `PLAYTEST [topic]` — optional topic narrows the focus.

**Gives you:** a built game, a written focus list, and clear "things to try" so the playtest produces actionable findings. After the user reports back, run TRIAGE.

**When *not* to use it:** behavioral or deterministic bugs that an auto-assertion can verdict — use AUTOTEST. Visual / "feels off" / tuning work is what PLAYTEST is for.

---

## What I do

1. **Build**: `./make.ps1 all` (or `make all` on macOS/Linux). Hard-fail the playtest if the build fails.
2. **Pick focus**:
   - If the user gave a topic, scope to it.
   - Otherwise pull the highest-risk untested items from `CLAUDE/RELEASE_V1.md` Phase A (the `[T]` and untested `[ ]` items).
3. **Write a brief** to `CLAUDE/playtests/<YYMMDD_HHMM>_<topic>.md`:
   - Build hash (`git rev-parse --short HEAD`)
   - Focus list — what's under test, why
   - What to look for — specific behaviors / edge cases
   - Expected behavior per item
4. **Hand back** with a `👀` line listing concrete things to try.
5. **After the user reports findings** → run TRIAGE.

## Tips

- Brief should fit on one screen. Long briefs get skimmed.
- Keep "what to look for" specific. "Watch the artillery" is too vague; "watch whether Paladin's turret rotates from East to South before the muzzle flash" is testable.
- Playtest reports are raw and historical — never edit a past report. TRIAGE updates the tracker, not the report.
