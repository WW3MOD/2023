# Skip the whole-mod YAML gate for a merge proven to contain no YAML and no C#

_Recorded 2026-08-30T16:29:58.738Z by 17dc66e4_

## The call

Merged and pushed `wt/curation-0830` (`53b01ad9`) **without** running `./utility.sh --check-yaml`, `make all`, or `dotnet test`. The full gate costs ~100 minutes; this change could not move any of the three verdicts.

## Why this is not the mistake it superficially resembles

This project has already been burned by "my diff has no compiled code, so I can skip the build" — that reasoning cost a scarce run grant on 2026-08-17. The ruling from that incident is recorded as **building is a property of the WORKTREE, not of the change**: a fresh worktree has no `engine/bin`, so it cannot launch regardless of what the diff contains.

That failure does not generalize to this one, and the distinction is the point:

- There, the question was *can this directory run the game* — a property of the environment, which the diff says nothing about.
- Here, the question is *can this content change a gate's verdict* — a property of the content, which the diff answers completely. The merge lands in the **main checkout**, already built and already gated green at `974c2ce4` an hour earlier.

## The premise was proven, not asserted

That is the load-bearing part. Asserting "it's only docs" is the failure mode; I checked:

- `git diff --name-only 974c2ce4..HEAD` → **3 `.lua`, 7 `.md`. Zero `.yaml`, zero `.cs`.** `--check-yaml` reads MiniYaml and, per `.maestro/MAESTRO.md`, **does not validate Lua at all**. NUnit and the build compile C#. None of the three has an input in this changeset.
- The three Lua files needed more than a filename check, because a scenario constant hiding in a comment-looking line would be a real behavioural change. Stripped trailing comments and blank lines from both sides and compared digests: **all three byte-identical in executable code.** The first two are trailing-comment edits on unchanged `local ObserveTicks = 300` / `local SampleTicks = 250` lines; the third changes only a comment block, which shifted the line count and made a naive digest differ — that near-miss is exactly why the check was worth running rather than eyeballing the diff.

## Rule for the next generation

Skipping the gate is legitimate **only** when the gate's own inputs are provably absent from the changeset, and "provably" means a command whose output you read — not an impression from the diff. If the merge contains one `.yaml` or one `.cs`, the gate runs, however trivial the change looks. And a Lua touch is never dismissed on the filename: strip comments and compare, because a constant edit and a comment edit are visually identical.
