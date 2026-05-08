# Contributing to WW3MOD

Thanks for your interest. Bug reports, balance feedback, code, and asset contributions are all welcome.

## Where to start

- **Found a bug?** Open a [bug report](../../issues/new?template=bug_report.yml).
- **Have an idea?** Open a [feature request](../../issues/new?template=feature_request.yml). For larger changes, please discuss before coding — easier to land if we agree on direction first.
- **Want to playtest?** Grab the latest [release](../../releases) and head to Issues with anything that feels off.

## Building

See [README — Build from source](README.md#build-from-source). Requires .NET 8 or later.

## Code conventions

- **Engine code** lives in-repo under `engine/` and follows OpenRA's StyleCop rules — CI rejects style violations. When in doubt, mirror nearby code.
- **YAML rules** — keep blank lines between templates and preserve the existing comment headers in unit files.
- **Don't leave `Console.WriteLine` in engine code.** The engine ticks ~25 times per second; a stray print floods the log.
- **Run `make test` before opening a PR** to catch YAML and Lua regressions.

## Pull requests

- Branch off `main`. Keep PRs focused — one feature or fix per PR.
- The PR description should explain **what** changed and **why**. Include screenshots or short clips for any UI or gameplay-visible change.
- Be ready for review feedback; we may ask for revisions before merging.

## Licensing

WW3MOD is released under [GPL-3.0](COPYING). By submitting code, art, or other contributions, you agree to license your work under the same terms.

## Asset attribution

If you contribute sprites, sounds, or models adapted from another OpenRA mod or an external source, update [CREDITS.md](CREDITS.md) in the same PR with the source and original license.
