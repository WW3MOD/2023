# tools/autotest/

Home of the WW3MOD automated-testing, demo, screenshot and tournament harness.

**Every launcher here starts the game.** Who may run which one is CLAUDE.md §"Who runs what": a worker dispatched by a manager runs none of them; game launches are serialized because concurrent launches crash the host. The launch-free scripts are marked below. Run everything from the repository root as `./tools/autotest/<script>` — there is no copy at the root, and `./run-test.sh` there is exit 127, a launch failure rather than a result.

## Layout

**Single runs**

| Script | What it does |
|---|---|
| `run-test.sh` | Run one scenario to a verdict. Exit 0/1/2/3 = pass/fail/skip/error; the last line is `AUTOTEST_VERDICT outcome=… exit=…`. `--hidden` is the unattended profile (no window, **no PNGs**); `--map NAME` loads a shipped map on the scenario's rig. `--help` prints the flag list. |
| `run-demo.sh` | Open a `demo-*` scenario for a human (`--visible --audio`, exit 3 mapped to 0). Pass `--timeout` — it forwards none, so the 300 s watchdog closes the window. |
| `run-smoke.sh` | World-construction smoke gate (`make smoke` / `.\make.ps1 smoke`): the canary plus every shipped map. Exit 0 pass, 2 a map started and never reached a verdict (or failed), 3 nothing ran. Needs a built tree. |

**Batches and tournaments** (multi-run — need the go-ahead CLAUDE.md requires)

| Script | What it does |
|---|---|
| `run-batch.sh` | Run named scenarios, or `--all` (every `test-*` with a verdict call). Defaults to `--speed 8`. Grades `expected-status` declarations. |
| `run-tournament.sh` | N bot-vs-bot matches of a `tournament-*` scenario into a result dir (calls `launch-game.sh` directly). |
| `loop-tournament.sh` | Round after round of `run-tournament.sh` until a target or budget is met. |
| `run-synchash.sh`, `synchash-launcher.sh` | Cross-runtime determinism probe: one match, per-net-frame sync-hash trace. |

**Screenshots and UI drivers** (launches)

| Script | What it does |
|---|---|
| `start-screenshot-mode.sh` | Launch at the main menu with the command-file watcher on. |
| `screenshot.sh` | Launch-free on its own: write a `screenshot <label>` command to a running game's command file; `--wait` prints the newest manifest path. |
| `screenshot-lobby.sh` | One-shot skirmish-lobby capture. |
| `screenshot-editor-zones.sh` | Map-editor Zones panel, two frames. Tracked mode `100644`: run as `sh tools/autotest/screenshot-editor-zones.sh`. |
| `screenshot-hotkeys.sh`, `screenshot-infopanel.sh`, `watch-replay.sh` | Command-file drivers for the hotkeys panel, the Esc info panel, and replay prompts. |

**Launch-free**

| Script | What it does |
|---|---|
| `list-tests.sh`, `list-demos.sh` | List `test-*` / `demo-*` scenarios with their first `description.txt` line. |
| `selftest.sh` | Proves `run-test.sh`'s outcome reporting and `run-batch.sh`'s grading with a stub launcher and a sandboxed `HOME`. |
| `selftest-launch-failure.sh` | Exercises `run-test.sh`'s launch-failure detector against synthetic logs. Tracked mode `100644`: run as `sh tools/autotest/selftest-launch-failure.sh`. |
| `expected-status.sh` | Sourced by `run-batch.sh`; `./tools/autotest/expected-status.sh --selftest` proves the decision table. |
| `aggregate-tournament.sh`, `tournament-report.sh`, `compare-batches.sh` | Summarise / compare tournament result dirs. |
| `poll-copy-logs.sh` | Copy the global engine logs to a private dir while a run is in flight. |

**Scenarios** live in `scenarios/`, by prefix: `test-*` (verdict-emitting), `demo-*` (staged for viewing, no verdict), `tournament-*` (bot-vs-bot configs), `wip-*` (parked). Count them with `ls -d tools/autotest/scenarios/*/ | wc -l` — the number grows weekly.

## Why the scenarios don't appear in the game's map lists

`mods/ww3mod/mod.yaml` registers `tools/autotest/scenarios` as a `MapFolders` entry classified `Unknown`:

- The engine **does** load them — `Launch.Map=test-foo` resolves by folder name (`Game.LoadMap` matches UID or package directory name and never reads `Class`).
- The in-game UI **doesn't** show them — the map choosers paint only `System` / `User` / `Remote` maps.

## Adding a scenario

- [`DOCS/recipes/AUTOTEST.md`](../../DOCS/recipes/AUTOTEST.md) — the TDD loop for behavioural fixes (default in RELEASE mode), scenario rules, and the static checks to run before a launch
- [`DOCS/recipes/DEMO.md`](../../DOCS/recipes/DEMO.md) — staging a scenario for human inspection
- [`DOCS/recipes/SCREENSHOT.md`](../../DOCS/recipes/SCREENSHOT.md) — captures and command-file drivers

The contract: `test-*` folders emit a verdict via `Test.Pass`/`Test.Fail`/`Test.Skip`; `demo-*` folders never do.

## Result files

Verdicts land under `~/.ww3mod-tests/` — HOME-rooted because the engine needs a writable path wherever this repo lives. Each run gets its own directory, `~/.ww3mod-tests/screenshots/<timestamp>_p<pid>_<test>/`, holding that run's `result.json`, screenshots, lifecycle/missile logs and a copy of `debug.log`; `run-test.sh` prints it as `Run dir:`. The shared `~/.ww3mod-tests/result.json` is a `"status":"moved"` stub so anything still reading it fails loudly. Run dirs older than 7 days are deleted at the start of each run. Tournament results go to `tools/autotest/tournament-results/` (use a repo-relative `--result-dir`).
