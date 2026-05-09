# REVIEW — review recent changes for quality

**Trigger:** `REVIEW [N]` — defaults to last commit, optional N for last N commits.

**Gives you:** a quality pass on changes I made. Catches leftover debug code, common pitfalls, missing condition wiring, YAML formatting issues, accidental over-engineering.

**When *not* to use it:** trivial commits (one-line tweaks, doc updates).

---

## What I do

1. `git diff HEAD~N` — read the actual diff.
2. **Check for**:
   - Common pitfalls (see CLAUDE.md → "Common Pitfalls" section)
   - Leftover `Console.WriteLine` / temporary trace lines
   - YAML formatting issues — blank-line separators, lowercase actor names, missing inheritance
   - Conditions granted but never consumed (or consumed but never granted)
   - Accidental over-engineering — abstractions, helpers, validation for impossible cases (per CLAUDE.md instructions)
   - Magic numbers that should be named or YAML-tunable
3. **Report** findings — list each issue with file:line, severity (blocker / nit / question).
4. **Fix** each issue with user approval. For nits I'm confident about, just fix them and call out in the response.

## Tips

- Look for things the AI tends to add that the user doesn't want: defensive null checks for impossible nulls, fallbacks for non-existent failure modes, comments that restate the code.
- Compare against the original intent — does the implementation match the plan, or did scope creep in?
- Check git log style consistency with the rest of the repo.
