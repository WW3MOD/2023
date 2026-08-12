# tools/autotest/

Home of the WW3MOD automated-testing and demo harness.

## Layout

```
tools/autotest/
├── run-test.sh        Run one autotest scenario; exits 0/1/2/3 = pass/fail/skip/error
├── run-batch.sh       Run several, or --all
├── run-demo.sh        Same plumbing as run-test, but interactive (no verdict)
├── list-tests.sh      Show every test-* scenario + first description.txt line
├── list-demos.sh      Same, but demo-*
└── scenarios/
    ├── test-*/        Deterministic verdict-emitting scenarios
    └── demo-*/        Staged scenarios for human viewing
```

## Why a hidden folder?

`mods/ww3mod/mod.yaml` registers `tools/autotest/scenarios` as a `MapFolders` entry classified `Unknown`. That means:

- The engine **does** load the maps — `Launch.Map=test-foo` resolves by folder basename, the same way it always did.
- The in-game UI **doesn't** show them — every map chooser (lobby, missions, main-menu browser) only paints maps classified `System` / `User` / `Remote`.

This keeps the in-game map list focused on real maps without taking the harness out of reach for automation.

## Adding a scenario

See:
- `DOCS/recipes/AUTOTEST.md` — TDD loop for behavioral fixes (default in RELEASE mode)
- `DOCS/recipes/DEMO.md` — staging a scenario for human inspection

The conventional contract: `test-*` folders emit a JSON verdict via `Test.Pass`/`Test.Fail` Lua calls; `demo-*` folders never do.

## Result files

Runtime JSON verdicts land in `~/.ww3mod-tests/` — HOME-rooted because the engine needs a writable path regardless of where this repo lives. Not part of this folder.

Each run gets its OWN directory, `~/.ww3mod-tests/screenshots/<timestamp>_p<pid>_<test>/`, holding that run's `result.json`, screenshots and lifecycle log. `run-test.sh` prints it as `Run dir:`. The old shared `~/.ww3mod-tests/result.json` is no longer a verdict — it is stubbed with `"status":"moved"` so anything still reading it fails loudly rather than silently reporting another run's result.

`./selftest.sh` checks that every way a run can end (pass, fail, crash, hang, no result, unparseable verdict, harness error) is reported as a distinct named outcome with the right exit code. It stubs the launcher and sandboxes `HOME`, so it starts no game and cannot disturb a run in flight.
