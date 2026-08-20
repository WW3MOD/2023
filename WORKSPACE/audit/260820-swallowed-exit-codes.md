# Swallowed exit codes — census, 2026-08-20

**Repo state audited: `main` @ `57822b4e`** (in sync with `origin/main`; verified with
`git status -sb` and `git rev-list --count HEAD..@{u}` = 0). Worktree `wt/exit-code-census`.
Read-only census. **Nothing was fixed.** No game launch, no `run-test.sh`, no `make test`,
no `./utility.sh --check-yaml`. Scratch recipes were run in `/tmp/xc` to prove shell semantics.

**21 findings.** Six of them sit in a live gate.

**The worst is F1: `packaging/functions.sh:50`.** `packaging.yml` runs `make engine`, never
`make all` — and `Makefile:172`'s `engine:` target builds only the engine, never `WW3MOD.sln`.
So the `find -exec dotnet publish` on that line is *the only build of the mod assembly anywhere
in the release pipeline*, and `find` throws its status away. A mod that fails to compile
produces a package with no `OpenRA.Mods.WW3MOD.dll` in it, the job goes green, and on a tag
`svenstaro/upload-release-action` publishes it. The player gets "Cannot locate type" — the same
symptom as this morning's incident, one layer further out, and this time in front of everybody.
This is instance (3) from the brief, verbatim: it was fixed in `Makefile:180-186` — where the
fix even carries a comment explaining this exact failure — and never carried across to
`packaging/`.

**Current-state note.** The YAML gate is green as of this session (`make test` exit 0, no new
lint errors, confirmed by the user). So nothing in the `make test` chain is hiding a real
failure right now. Everything below is ranked on **what it would hide**, not on an
attributable current red.

---

## How to read the method column

- **proved** — demonstrated on a scratch recipe in `/tmp/xc`, output quoted.
- **reasoned** — read the construct and applied known shell / PowerShell / Actions semantics.
  Legitimate, but weaker. Marked honestly; not upgraded.
- **traced** — reachability established by following the actual call graph in this repo.

No pwsh and no cmd.exe on this machine, so **every PowerShell and batch finding is reasoned,
not proved.** Two of them (F-lineage of `make.ps1 All-Command`) are corroborated by the fact
that they *already happened today* — that is stronger than a scratch test, and said where it applies.

---

# Tier 1 — a live gate that reports success on failure

## F1 — **[WORST]** `packaging/functions.sh:50` — the release pipeline's only mod build, discarded

```sh
find . -maxdepth 1 -name '*.sln' -exec dotnet publish -c Release \
  -p:TargetPlatform="${TARGETPLATFORM}" -r "${TARGETPLATFORM}" \
  -p:PublishDir="${DEST_PATH}" --self-contained true \;
```

**Reachable: yes.** `install_mod_assemblies` is called from all three platform scripts —
`packaging/linux/buildpackage.sh:80`, `packaging/macos/buildpackage.sh:151` and `:152`,
`packaging/windows/buildpackage.sh:102` — each passing `RUNTIME=net6`, which is the `else`
branch containing line 50. `packaging.yml` triggers on `push: tags: '*'` and `workflow_dispatch`.

**What it hides:** a failed `dotnet publish` of `WW3MOD.sln` reports success to
`install_mod_assemblies`, which reports success to `buildpackage.sh` — whose `set -e` therefore
never fires, because there is no non-zero status for it to fire on — which reports success to
the packaging job, which uploads the artifact and publishes the release.

**Why it is the worst rather than merely bad:** `packaging.yml:35`, `:86`, `:130` all run
`make engine`. Nothing in the release lane runs `make all`, `make check` or `make test`. So there
is no second gate behind this one. Verified by reading all three jobs.

*Method: **proved** (find semantics, below) + **traced** (call graph and `packaging.yml` read directly).*

```
$ find . -maxdepth 1 -name '*.sln' -exec false \;      -> exit 0
$ find . -maxdepth 1 -name '*.sln' -exec sh -c 'exit 7' \;  -> exit 0
```

`packaging/functions.sh:31` has the identical construct in the `mono` branch — unreachable, no
mono lane.

---

## F2 — `.github/workflows/ci.yml:60-65` — only the last command in the Windows step can redden it

```yaml
      - name: Check Mods
        run: |
          choco install lua --version 5.1.5.52
          $ENV:Path = $ENV:Path + ";C:\Program Files (x86)\Lua\5.1\"
          .\make.ps1 check-scripts
          .\make.ps1 test
```

**Reachable: yes — every push and every pull request** (`ci.yml` has bare `on: push` /
`pull_request`). No `shell:` key, so a Windows runner gets the `pwsh` default.

**What it hides:** a failed `choco install lua` reports success to the step, and a non-zero
`.\make.ps1 check-scripts` reports success to the step. PowerShell has no `set -e` for native
commands or for child `.ps1` exit codes — `exit N` inside `make.ps1` sets the caller's
`$LASTEXITCODE` and execution continues to the next line. Only `.\make.ps1 test` can fail the step.

`ci.yml:53-58` (`shell: powershell`, `.\make.ps1 check`) has the same shape but happens to be safe
today because the fallible command is last. It becomes unsafe the moment anyone appends a line.

*Method: **reasoned** — documented Actions `run:` wrapper plus PowerShell native-exit semantics.
No pwsh available locally to prove.*

---

## F3 — `make.ps1:196-211` and `engine/make.ps1:146-169` — `Check-Scripts-Command` cannot fail

```powershell
	if ((Get-Command "luac.exe" -ErrorAction SilentlyContinue) -ne $null)
	{
		foreach ($script in ls "mods/*/maps/*/*.lua") { luac -p $script }
		Write-Host "Check completed!" -ForegroundColor Green
	}
	else { Write-Host "luac.exe could not be found. Please install Lua." -ForegroundColor Red }
```

**Reachable: yes** — `ci.yml:64`, every push, and any developer running `make.ps1 check-scripts`.

**What it hides, two ways.** (a) `luac -p $script` inside a `foreach` with no `$lastexitcode`
check: a Lua syntax error in any script is swallowed, and the function then prints "Check
completed!" in green. Even in the most generous reading of PowerShell's script-exit rules only
the *last* file's status could ever survive, so files 1..N-1 are unconditionally lost. (b) The
`else` branch prints red and **returns normally** — if `choco install lua` failed or the
hardcoded `$ENV:Path` is wrong, the whole Lua gate is skipped and reports success.

The Unix side does neither: `Makefile:204-206` is `@printf "'luac' not found.\n" && exit 1`, and
`Makefile:210` puts every file through one `luac -p $(LUA_FILES)` invocation whose status
propagates. So Linux gates Lua and Windows only claims to.

Compounds with F2, which discards this step's result anyway.

*Method: **reasoned.***

---

## F4 — `.github/workflows/ci.yml:25-28` — the CRLF gate matches on the runner's PID

```yaml
        run: |
          . mod.config;
          awk '/\r$$/ { exit(1); }' mod.config || (printf "Invalid mod.config format...\n"; exit 1);
```

**Reachable: yes** — every push and pull request, Linux job.

**What it hides:** `$$` is Makefile escaping that was copy-pasted into a workflow. `Makefile:98`
has the identical line and is correct there, because `make` eats one `$`. In a `run:` block bash
expands `$$` to the shell's PID, so awk receives the program `/\r68861/` — a regex matching a CR
followed by that number. **A CRLF `mod.config` passes the gate.**

The step's other line, `. mod.config;`, sources into a shell that is discarded when the step ends
(each `run:` gets a fresh shell), so "Prepare Environment" does nothing whatsoever.

*Method: **proved** — ran the ci.yml text verbatim against a CRLF file.*

```
$ printf 'MOD_ID="x"\r\n' > crlf.config
$ bash -e -c "awk '/\r\$\$/ { exit(1); }' crlf.config || (printf 'INVALID\n'; exit 1)"
  -> exit 0     # CRLF slipped through
$ bash -e -c "awk '/\r\$/  { exit(1); }' crlf.config || (printf 'INVALID\n'; exit 1)"
INVALID
  -> exit 1     # correct program catches it
$ bash -c 'echo "awk program is: /\r$$/"'
awk program is: /\r68861/
```

---

## F5 — CI compiles the NUnit suite with `-warnaserror` and never runs it

`Makefile:237` and `make.ps1:179` both *build* `engine/OpenRA.Test/OpenRA.Test.csproj`. Nothing
executes it: `dotnet test` and `OpenRA.Test` as a *run* target appear nowhere under `.github/`.
`CLAUDE.md:31` documents `dotnet test engine/OpenRA.Test/OpenRA.Test.csproj --configuration
Release` as the unit-test command, and `ww3-dev.ps1 test` runs it correctly with a
`$LASTEXITCODE` check — but no automated gate invokes either.

**What it hides:** a failing unit test reports success to every push, every PR and every merge.

Not an exit-code-masking construct — a missing gate. Included because the census question is "is
this gate reporting success on failure", and a gate that never runs always reports success.

*Method: **proved by absence** — grep over `.github/` for `dotnet test` and `OpenRA.Test`.*

---

## F6 — `tools/autotest/loop-tournament.sh:173` — `| tee` eats the tournament's status

```sh
	./tools/autotest/run-tournament.sh "${SCENARIO}" \
		--seeds "${BATCH_SIZE}" --config "${CONFIG}" \
		--result-dir "${ROUND_DIR}" --max-wall-secs "${MAX_WALL_SECS}" \
		${MIRROR_ARG} 2>&1 | tee "${ROUND_DIR}/run.log" > /dev/null
```

`set -e` is on (line 37); **`pipefail` is not.** The pipeline's status is `tee`'s, which is 0.

**Reachable:** yes, but only behind an explicit user goahead (CLAUDE.md forbids autonomous
multi-test runs). That *raises* the cost rather than lowering it — this is an unattended
multi-hour loop, so a silent failure burns the whole session.

**What it hides:** a `run-tournament.sh` that exits non-zero — including its own `exit 3`
argument-validation paths — reports success to the loop. The loop then reads `summary.json` via
`parse_summary_field`; when the tournament never ran, that yields empty `VERDICTS`/`FAILS`, and
nothing checks for it. The loop spins its full round budget printing `verdicts=/N`, then exits
through its normal stop logic. **A tournament that never ran reports itself as a completed loop.**
`> /dev/null` after the `tee` means the operator sees nothing live either, so the failure is
invisible in both channels.

This is the documented `| tail` family — same mechanism, `tee` instead of `tail`.

*Method: **proved.***

```
$ sh -c 'set -e; sh -c "exit 3" 2>&1 | tee /dev/null > /dev/null; echo "REACHED next line"'
REACHED next line (failure invisible)
  -> script exit 0
$ sh -c 'set -e; set -o pipefail; sh -c "exit 3" 2>&1 | tee /dev/null >/dev/null; echo REACHED'
  -> script exit 3
```

Note the contrast one directory over: `run-test.sh` sets **both** `set -e` and `pipefail`, and
lines 132-141 carry a comment block warning callers about precisely this trap. The harness
protects itself; its own loop driver does not.

---

# Tier 2 — reachable, real, narrower blast radius

## F7 — `test.cmd` — no guard between the build and the test

```bat
@powershell -NoProfile -ExecutionPolicy Bypass -File make.ps1 %* all
@powershell -NoExit -NoProfile -ExecutionPolicy Bypass -File make.ps1 %* test
```

This is the **identical defect fixed in `launch-game.cmd` today in `e8855bf7`**, sitting in the
sibling file, untouched. `launch-game.cmd` now has `@if %errorlevel% neq 0 goto buildfailed`
between its build and its launch; `test.cmd` has nothing between its build and its test.

**What it hides:** a failed build runs the YAML/nav-guard gate against stale binaries, and
because line 2 is last, `test.cmd`'s exit code is the *test's*. A build failure can therefore be
reported as a passing test run. Compounds with F8 into the full inversion.

`build.cmd` has the same first line but nothing after it, so it is fine.

*Method: **reasoned** (cmd.exe batch semantics; no Windows here).*

## F8 — `make.ps1:140-151` — `make.ps1 test` on an unbuilt tree reports success

```powershell
function Test-Command
{
	NavGuard-Command
	if ((CheckForUtility) -eq 1) { return }      # <- prints red, exits 0
	InvokeCommand "$utilityPath $modID --check-yaml"
}
```

`CheckForUtility` prints "OpenRA.Utility.exe could not be found" in red and returns 1;
`Test-Command` returns; the script falls off the end; exit 0. **The MiniYAML gate reports
success having checked nothing.** Note also that `test` is absent from the dotnet pre-flight list
at `make.ps1:401` (`all`, `clean`, `check` only), so this path never checks the SDK either.

The Linux counterpart cannot do this: `utility.sh:6` is `set -e` and `:49-53` hard-exits 1 when
the binary or VERSION is missing, so `make test` goes red.

Chain it with F7: build fails → utility never produced → `make.ps1 test` prints red and exits 0 →
`test.cmd` exits 0. **A failed build reports a passing test run.**

*Method: **reasoned**; corroborated by the fact that this exact `return`-instead-of-`exit` shape
is what caused both of today's incidents.*

## F9 — `engine/Makefile:134` — brace expansion under `dash` skips 196 of 199 Lua files

```make
	@find lua/ mods/*/{maps,scripts}/ -iname "*.lua" -print0 | xargs -0n1 luac -p
```

`make` runs recipes with `/bin/sh`. **dash does not do brace expansion**; bash does. On a Linux
box where `/bin/sh` is dash, `find` is handed the literal `mods/*/{maps,scripts}/`, errors on it,
and `xargs` masks find's non-zero status.

**Reachable: Linux developers running `make check-scripts` in `engine/`. Not CI** — `ci.yml:64`
runs the *top-level* `make check-scripts`, which is a different recipe. Correcting the obvious
assumption: this one does not reach the pipeline.

**What it hides:** 196 of the engine's 199 Lua files are silently skipped and the target reports
success. Only the 3 files in `lua/` are checked, because that path exists and find processes it
before erroring on the second.

*Method: **proved**, with the real paths, in `engine/`.*

```
/bin/sh    -> mods/ra/maps/ mods/ra/scripts/       (bash: expands)
/bin/dash  -> mods/*/{maps,scripts}/               (does not expand)

$ /bin/dash -c 'find lua/ mods/*/{maps,scripts}/ -iname "*.lua" -print0 | xargs -0n1 echo >/dev/null'
find: mods/*/{maps,scripts}/: No such file or directory
  -> exit 0
$ find lua/ -iname '*.lua' | wc -l                    ->   3
$ bash -c 'find mods/*/{maps,scripts}/ -iname "*.lua" | wc -l'  -> 196
```

## F10 — `Makefile:56` — the mod's Lua gate cannot see the autotest scripts

```make
LUA_FILES = $(shell find mods/*/maps/* -iname '*.lua' 2> /dev/null)
```

The glob covers `mods/*/maps/*` only. `mods/ww3mod/scripts/` holds 5 more — including
`scenario.lua` and `test-helpers.lua`, the library the autotest scenarios load. **3 of the mod's
8 Lua files are checked; 5 are checked by no gate at all.**

Reachable via `ci.yml:44` (`make check-scripts`) on every push, so this is a live coverage hole
rather than a swallowed status. Adjacent to the census class, and listed for the same reason as
F5: the gate reports success over ground it never covered.

*Method: **proved** by counting both globs.*

## F11 — `Makefile:192,194` — the last surviving `find -exec`, in `clean`

```make
	@find . -maxdepth 1 -name '*.sln' -exec $(MSBUILD) -t:clean \;
	@find . -maxdepth 1 -name '*.sln' -exec $(DOTNET) clean \;
```

Instance (3) from the brief, still live in the sibling target. `all:` was fixed today;
`clean:` was not.

**Reachable:** humans only — grep finds no automated caller of `make clean` anywhere in the repo
or CI. That is what keeps it out of Tier 1.

**What it hides:** a failed `dotnet clean` reports success to `make clean`, so a clean that did
not clean looks done and the next build reuses stale `obj/` — the mixed-binary failure mode this
whole census is about, just reached the slow way.

**Second, independent bug on the same line:** there is no `{}` in either `-exec`, so the found
`.sln` is never passed. It runs a bare `dotnet clean` / `msbuild -t:clean` against whatever the
CWD implies. Inherited from the upstream SDK.

*Method: **proved.***

```
$ find . -maxdepth 1 -name '*.sln' -exec echo "RAN:" dotnet clean \;
RAN: dotnet clean          # no filename — '{}' is absent from the recipe
```

## F12 — `launch-game.cmd` — a fatal game crash exits 0

`:crashdialog` prints the crash banner, calls `pause`, and falls off the end of the script. A
batch file that runs off the end exits with the last command's status, and `pause` returns 0.
`:noengine` and `:badconfig` end in a bare `exit /b`, which preserves the current ERRORLEVEL —
also 0, because `pause` ran immediately before.

So `launch-game.cmd` reports success for: engine files missing, mod.config broken, and **the game
crashing fatally**. `:buildfailed` is the one label that gets it right (`exit /b 1`) — added
today, two lines away from three that did not.

**Reachable but currently unconsumed:** the autotest harness uses `launch-game.sh`, never the
`.cmd` (verified by grep across `tools/`). The only caller is `ww3-dev.ps1:93`, which invokes it
last and ignores the status. So today nothing reads the lie. Worth fixing for symmetry, not urgency.

*Method: **reasoned** (cmd.exe semantics; no Windows available).*

## F13 — `packaging/linux/buildpackage.sh:103,116,122,138` — `sed | sed > file` without pipefail

`set -e` at line 3, no `set -o pipefail`. A failed first `sed` (missing or unreadable `AppRun.in`,
`.desktop.in`, launcher template) reports success because the trailing `sed` and the redirect
exit 0 — an empty or truncated `AppRun` / `.desktop` / `usr/bin/openra-ww3mod` is written and the
AppImage is built and uploaded around it. `packaging/windows/buildpackage.sh` inherits the same
missing option; `packaging/macos/buildpackage.sh:16` gets it right with
`set -o errexit -o pipefail`.

Low likelihood — the inputs are tracked files — so this is correct-by-accident rather than by
construction.

*Method: **reasoned**; the pipefail mechanic itself is proved under F6 and F17.*

---

# Tier 3 — conditional or dormant

## F14 — `make.ps1:112-124` — no python means nav-guard silently skips

```powershell
	if ($python -eq $null)
	{
		Write-Host "nav-guard needs python on PATH; skipping." -ForegroundColor Yellow
		return
	}
```

`NavGuard-Command` is the first thing `Test-Command` calls, and it is reached from `ci.yml:65`.
A runner or dev box without python on PATH skips the map-connectivity guard entirely and reports
success. `Makefile:38-44` cannot: it `$(error)`s at parse time when python is absent.

Dormant because GitHub's Windows runners ship python. *Method: **reasoned**.*

## F15 — `make.ps1:186` and `engine/make.ps1:136` — a missing utility silently drops two gates

```powershell
	if ((CheckForUtility) -eq 0)
	{
		InvokeCommand "$utilityPath $modID --check-explicit-interfaces"
		InvokeCommand "$utilityPath $modID --check-conditional-trait-interface-overrides"
	}
```

When the binary is absent the two interface-violation checks do not run and `check` reports
success. Safe today: `engine/make.ps1`'s `Check-Command` builds `engine/OpenRA.sln` in Debug just
above, which produces `OpenRA.Utility.exe`.

**What would make it unsafe:** removing `OpenRA.Utility` from `engine/OpenRA.sln`, renaming the
output, or changing `$utilityPath`. The failure mode is not an error — the gate just goes quiet.
The Makefile has no equivalent: `Makefile:241,243` call `./utility.sh` unconditionally and
`utility.sh` hard-exits 1 when the binary is missing.

*Method: **reasoned.***

## F16 — `utility.cmd:32` — `EXIT /B 0` hardcoded on the branch labelled "for use by other scripts"

```bat
if %argC% GEQ 2 (
    @REM This option is for use by other scripts so we don't want any extra output here...
    call bin\OpenRA.Utility.exe %*
    EXIT /B 0
)
```

Every scripted `utility.cmd <mod> --check-something` invocation returns 0 whatever the utility
did. `engine/utility.cmd:18` is identical.

**Reachable: barely.** `make.ps1` calls the `.exe` directly, and `tools/cameo/build.sh:56` only
falls back to `utility.cmd` when `utility.sh` is not executable — which it always is, being
tracked with the exec bit. But `tools/cameo/README.md:26,31` and `tools/cameo/convert.py:298`
both instruct the user to run `./utility.cmd ww3mod --check-missing-sprites` by hand. Output is
still printed, so a human reads the real result; only the exit code lies. Harmful the moment
anything scripts it.

*Method: **reasoned.***

## F17 — `packaging/windows/buildpackage.sh:110` — pipeline masks a `grep` against a path that does not exist

```sh
		MOD_VERSION=$(grep 'Version:' "mods/${MOD_ID}/mod.yaml" | awk '{print $2}')
```

**Unreachable today:** guarded by `PACKAGING_OVERWRITE_MOD_VERSION == "True"`, and
`mod.config:112` sets it to `"True"`, so only the `set_mod_version` branch runs.

**If flipped to False:** the path is relative and `cd "${PACKAGING_DIR}"` ran at line 52, so it
resolves to `packaging/windows/mods/ww3mod/mod.yaml`, which does not exist. `linux:88` and
`macos:160` use the correct `${BUILTDIR}`-rooted path — only the Windows copy is wrong. `set -e`
is on but `pipefail` is not, so `awk` returns 0, the failure is swallowed, and the installer is
built while printing `Mod version  will remain unchanged.` with an empty version.

*Method: **proved** (pipefail mechanic); unreachability **traced** to `mod.config:112`.*

```
$ set -e;             V=$(grep x /nonexistent | awk '{print $2}')  -> exit 0, V=''
$ set -e -o pipefail; V=$(grep x /nonexistent | awk '{print $2}')  -> exit 2
```

## F18 — `packaging/macos/buildpackage.sh:178,230,234` — absent signing secrets ship an unsigned DMG, green

`if [ -n "${MACOS_DEVELOPER_IDENTITY}" ]; then codesign ...; fi` and the analogous notarization
guard. Reached from `packaging.yml:78-88` on every tag; the secrets arrive via `env:`, so a
typo'd or unset repository secret simply expands to empty.

**What it hides:** a missing signing configuration reports success to the release job. The DMG is
built, uploaded to the public release, and Gatekeeper-blocks on every user's machine, with no
warning anywhere in the log. The header comment documents the skip as intentional for local
builds; it is a trap for the release lane specifically.

*Method: **reasoned.***

## F19 — `fetch-engine.sh` — no `set -e`, a hardcoded `exit 0`, and unreachable dead code

```sh
echo "Compiling engine..."
cd "${ENGINE_DIRECTORY}" || exit 1
chmod u=rwx,g=r,o=r fetch-geoip.sh
make version VERSION="${ENGINE_VERSION}"
exit 0

echo "Automatic engine management is disabled."     # unreachable
echo "Please manually update the engine to ..."     # unreachable
exit 1                                              # unreachable
```

The only top-level shell script in the repo with **no `set -e`** (verified across all of them).

**Dormant:** `engine/VERSION` is `release-20230225` and `mod.config` pins the same, so the script
takes the early `exit 0` on every invocation. The whole download/`rm -rf`/unzip/`mv` sequence —
unguarded and errexit-less — is dead, as is the `AUTOMATIC_ENGINE_MANAGEMENT` block.

**What arms it:** bumping `ENGINE_VERSION` in `mod.config` without updating `engine/VERSION`, or
anything that rewrites `engine/VERSION`. With `AUTOMATIC_ENGINE_MANAGEMENT="False"` the script
does *not* print "Automatic engine management is disabled" — that message is after the `exit 0`.
It runs `make version` against the **in-repo** engine, rewriting `engine/VERSION` and the Version
strings of six `mods/*/mod.yaml` files, then exits 0 regardless. `Makefile:173`'s guard —
`@./fetch-engine.sh || (printf "Unable to continue without engine files\n"; exit 1)` — cannot
fire, because the literal `exit 0` makes it unreachable. Destructive and green.

Not a local regression: `git log` shows the file untouched since the initial SDK import
(`7362fbc6 Starting point (#2)`).

*Method: **reasoned**; deadness **traced** to `engine/VERSION` + `mod.config`.*

## F20 — `make.ps1` — the `return` siblings of the branch that was fixed today

`All-Command:15-19` ("No custom solution file found. Aborting." → `return`), `:21-24`
(`CheckForDotnet` → `return`), `Clean-Command:44-53` (both again), `Check-Command:155-159`
("Skipping static code checks." → `return`). All exit 0.

**Dormant.** `WW3MOD.sln` is tracked, and `make.ps1:406-409` hard-exits 1 on a missing SDK before
`All-Command` is ever dispatched, so the dotnet branches are dead.

Recorded because these are bug (1)'s remaining siblings **inside the very function whose comment
at lines 31-34 explains that `return` here is what launched the game on stale binaries**. If
`WW3MOD.sln` is ever renamed, `launch-game.cmd` prints "Aborting." and then launches the game.

Same shape, Unix side: `Makefile:186` — `@set -e; for sln in $(MOD_SOLUTION_FILES); do ...; done`
— an empty `MOD_SOLUTION_FILES` makes the loop a silent no-op rather than a syntax error, so
`make all` would build the engine only and report success. **Proved:** `sh -c 'set -e; for s in ;
do echo x; done; echo reached-end'` → `rc=0`, in sh, dash and bash alike. Also dormant.

*Method: **reasoned** for the PowerShell half, **proved** for the make half.*

## F21 — `tools/git-hooks/pre-commit:43` — `git diff | awk` without pipefail

```sh
violations=$(git diff --cached --diff-filter=AM -U0 -- '*.cs' | ALLOW_REGEX="$ALLOW_REGEX" awk '...')
```

`set -e` at line 13, no `pipefail`. If `git diff --cached` fails, `awk` still exits 0, `violations`
is empty, and the hook passes. Same for the `while ... done < <(git diff --cached --name-only)`
CRLF loop, which just iterates zero times.

Very low likelihood — `git diff --cached` failing inside a commit is close to inconceivable — and
listed only for completeness of the pattern sweep.

*Method: **reasoned.***

---

# Non-bugs that look like bugs

These are the entries that should stop someone "fixing" a non-bug next month.

**`tools/autotest/run-batch.sh` has no `set -e`, and that is correct.** It captures `rc=$?` from
every `run-test.sh` (line 194), tallies pass/fail/skip/error, and exits with the non-pass count
clamped to 99 (lines 207-221). `set -e` would abort the batch on the first failing test, which is
the opposite of what a batch runner is for. **Would become unsafe** if anyone added a
`| tee`-style pipeline around the `run-test.sh` call, or replaced `rc=$?` with an `if` wrapper.

**`tools/autotest/selftest.sh` has no `set -e`, and that is also correct** — it accumulates
`FAILURES` across independent cases and exits with the count (line 289). More than that, lines
257-281 are a *deliberate regression test for this very census's subject*: it asserts that
`tail -1` of a failing run still carries `AUTOTEST_VERDICT outcome=FAIL`, and that the verdict
reaches stderr when stdout is redirected. The comment reads "`| tail` is the caller mistake that
has hit twice." The harness has a countermeasure; F6 shows the loop driver above it does not.

**`find ... | xargs` does propagate the child's failure.** In `engine/Makefile:134` it is only
`find`'s *own* status that is lost. **Proved:** `printf 'one\0two\0' | xargs -0n1 sh -c 'exit 1' _`
→ non-zero. So the F9 fix is about the brace expansion, not about replacing xargs.

**`packaging/package-all.sh:30-45`'s `if [ $? -ne 0 ]` blocks are dead, and the script is correct
because of it.** It reads exactly like "print and continue"; it is not, because `set -e` at line 2
fires on the failing child before the `if` is evaluated. **Proved:** child exiting 7 →
`package-all` exits 7, and the message never prints. Two caveats worth keeping: it becomes a live
instance of the bug the instant `set -e` is removed or the call is wrapped in `if`/`||`/`&&`; and
the code's *stated* intent (try Windows, report, still try Linux) is not what happens — it aborts
after the first failure. Comment and behaviour disagree. Nothing in CI invokes it.

**`for` loops under `set -e` do abort** — `packaging/*/buildpackage.sh`'s
`for f in ${PACKAGING_COPY_ENGINE_FILES}` and the `for LIB in .../*.dll` loops are fine. `set -e`
applies inside loop bodies. **Would become unsafe** if such a loop were moved into a function used
as an `if` condition or on the left of `&&`/`||`, which suspends `set -e` for its whole dynamic extent.

**`engine/packaging/functions.sh`'s `install_assemblies() ( set -o errexit || exit $? ; ... )`
idiom is right.** A subshell function body with its own errexit is self-protecting and does not
depend on the caller — which matters, because `Makefile:200` sources it into a bare `sh -c`. The
mod's own `packaging/functions.sh:16` uses a *brace* function with no errexit and is entirely
dependent on its callers; safe today only because all three `buildpackage.sh` set `-e`.

**`make.ps1`'s `InvokeCommand` is sound.** Appending `; $success = $?` and testing it is the
correct way around `Invoke-Expression` always succeeding, and `Invoke-Expression` runs in the
caller's scope so `$success` is readable. Likewise `CheckForDotnetSdk:271`'s `& dotnet --list-sdks
| Out-Host` — the comment explains it is `Out-Host` precisely so native-command output does not
contaminate the function's integer return value. That is this exact bug class, already handled.

**`engine/fetch-geoip.sh`'s `curl ... || echo "Warning: Download failed"` is deliberate.** A
GeoIP refresh failure is intentionally non-fatal and must not fail the build. Correct.

**`packaging/macos/buildpackage.sh:184,248` is the *inverse* risk, not this one.**
`hdiutil attach | egrep | sed 1q | awk` under `pipefail`: `sed 1q` closes the pipe early, `egrep`
can take SIGPIPE (141), and errexit aborts a packaging run that actually succeeded. Flaky-red, not
flaky-green. Noted only so it is not mistaken for a masking bug later.

**`launch-dedicated.sh` / `.cmd` loop forever by design** — a dedicated server relauncher, not a
gate.

---

# Where the class clusters

Three generative causes account for all 21:

1. **PowerShell has no `set -e`.** Every `return`-instead-of-`exit` finding (F3, F8, F14, F15,
   F20) and both CI Windows-step findings (F2) come from the same root: in PowerShell a failure
   is only reported if someone writes the check. Eight findings. The Unix twin of each of these
   targets is correct, because `make` aborts per recipe line and `set -e` is on.
2. **A construct that runs a command has its own exit code.** `find -exec` (F1, F11), `tee`
   (F6), `grep|awk` (F17), `sed|sed` (F13), `xargs` masking find (F9), `EXIT /B 0` (F16), a
   hardcoded `exit 0` (F19), `pause` (F12). Nine findings.
3. **A gate that does not run.** F5 (NUnit never invoked), F10 (glob misses 5 of 8 Lua files),
   F4 (the regex cannot match). Three findings.

The cheapest structural mitigations, for whoever fixes this — **not done here, this is a census:**
`set -o pipefail` alongside every existing `set -e` in `packaging/` and `tools/autotest/`;
`$LASTEXITCODE` checks after every native call in both `make.ps1` files; and replacing
`find -exec` with the `set -e; for ... done` form the `Makefile` already adopted at line 183.
F1 alone is a two-line change and closes the worst of them.
