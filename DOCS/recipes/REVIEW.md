# REVIEW — review recent changes for quality

**Trigger:** `REVIEW [N]` — defaults to last commit, optional N for last N commits.

**Gives you:** a quality pass on changes I made. Catches leftover debug code, known pitfalls, missing condition wiring, YAML merge mistakes, accidental over-engineering.

**When *not* to use it:** trivial commits (one-line tweaks, doc updates).

---

## What I do

1. `git diff HEAD~N` — read the actual diff.
2. **Check for**:
   - Known pitfalls — [`DOCS/reference/conventions.md`](../reference/conventions.md) (engine code rules, YAML idioms, §"PITFALL comments") and [`DOCS/reference/pitfalls.md`](../reference/pitfalls.md); `git grep PITFALL` near the touched lines
   - Leftover `Console.WriteLine` / temporary trace lines
   - **MiniYaml merge mistakes** — the three causes in CLAUDE.md's MiniYaml rule: a missing blank line between top-level entries (adjacent entries merge); a rules override whose top-level key differs in **case** from the defining key (`t03:` against `T03:` overrides nothing — keep the defining key's exact case); `Inherits@` / `-Key:` in the wrong order. Lowercase is correct only for actor **types** placed in a `map.yaml`.
   - Missing inheritance, and `-Trait:` removals of traits that are not inherited
   - Conditions granted but never consumed (or consumed but never granted)
   - A new behavioural Info field on a trait shared by both bot profiles that does not default to baseline (CLAUDE.md, `@stable` rule)
   - Accidental over-engineering — abstractions, helpers, validation for impossible cases
   - Magic numbers that should be named or YAML-tunable
   - C# changes: was `make check` / `.\make.ps1 check` run? Release builds strip analyzers, so a green `all` says nothing about RCS-class errors.
3. **Report** findings — each with file:line and severity (blocker / nit / question).
4. **Fix** each issue with user approval. For nits I'm confident about, just fix them and call them out.

## Tips

- Look for things the AI tends to add that the user doesn't want: defensive null checks for impossible nulls, fallbacks for non-existent failure modes, comments that restate the code.
- Compare against the original intent — does the implementation match the plan, or did scope creep in?
- Check git log style consistency with the rest of the repo.
