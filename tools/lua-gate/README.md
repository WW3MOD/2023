# lua-gate

Catches the class of scenario bug that costs a launch slot and reports it as `fail`:
**Lua that names a scripting binding the engine does not register.**

Such a script compiles clean, aborts on load before a single tick, and writes
`status: fail` to `result.json` — which is the same thing a deliberately-failing RED arm
writes. So the run does not merely waste a slot; it is one careless glance away from being
banked as verification of a fix that never executed.

Two real aborts, two hours apart, motivated this:

| | what happened | caught? |
|---|---|---|
| `Trigger.OnTick(...)` | No such binding. Three of the four grep hits for the symbol were comments in other scenarios saying it does not exist. *(Historical: `Trigger.OnTick` was **added** on 2026-09-06 — authors kept reaching for it, so the binding was the cheaper fix than the eighth comment explaining its absence. The gate re-derives its surface from the C# on every run, so it now resolves the symbol with no change here. The abort was real when it happened.)* | **yes**, as an error |
| `Actor 'player' does not define a property 'Location'` | `test-drone-lost-track` walked `usa.GetActors()`, which includes the **player actor**. `Location` needs `Requires<IOccupySpaceInfo>`, and reading a property an actor does not define *throws* — so the `a.Location ~= nil` guard written to prevent exactly this could never fire. Fixed in `1d3c9db0`. | **yes**, as a warning, by one narrow heuristic — see below |

The second one is worth reading carefully, because it is **not** the failure it looks like.
The engine wording `Actor 'player' …` comes from `ScriptActorInterface`, not
`ScriptPlayerInterface`: the receiver was an `Actor` (the player system actor), not a
`Player`, and `Location` is a perfectly real actor property that was simply *filtered out
for that actor* by its missing trait. That is the trait-gated class in
[limitation 3](#what-this-does-not-check), which this gate cannot decide in general.

What catches it is a single targeted rule, not the general mechanism: **`GetActors()` is
the one collection documented to contain the player actor**, so reading any trait-gated
property off an element of it is flagged. Written a different way — a numeric `for` over a
saved list, or the same walk through a helper — it slips past. See
[Acceptance test](#acceptance-test) for both failures firing on their real code.

## Run

```bash
make lua-gate                                     # selftest + check; no build, no launch
.\make.ps1 lua-gate                               # the same, on Windows (also `make.ps1 l`)
./tools/lua-gate/lua_gate.py check                # the gate on its own
./tools/lua-gate/lua_gate.py check --scenario test-medic   # filter (substring, repeatable)
./tools/lua-gate/lua_gate.py check --strict       # warnings fail too
./tools/lua-gate/lua_gate.py api [--json]         # dump the binding surface it extracted
./tools/lua-gate/lua_gate.py selftest             # assert extractor + scanner still work
./tools/lua-gate/lua_gate.py verify --docs <md>   # diff the parse against engine reflection
```

Standard-library Python only. No build, no engine, no game launch.

- **exit 2 (fail)** — a reference that cannot resolve: an unknown member on a known engine
  table, or a member that does not exist on the CLR type the value provably has. **Also any
  wiring fault that makes a scenario inert** — see the second failure class below. Those are
  errors rather than warnings because there is exactly one correct outcome: a scenario the
  engine loads and never runs is never what anyone wanted.
- **exit 1 (warn)** — a base name that resolves to nothing; a trait-gated property read off
  a `GetActors()` element; an orphan `.lua` no `Scripts:` names; or a `test-` scenario that
  can reach no verdict. Real findings, but the remedy is sometimes "delete the file" or
  "rename it `demo-`", so they do not hard-fail a shared target.
- **exit 0** — every reference the gate can see resolves, and every scenario is wired to run.

`make lua-gate` and `make.ps1 lua-gate` fail only on exit 2, so a warning prints without
breaking `make test`.

## Where the API surface comes from

`ScriptContext` registers exactly three things, and the gate mirrors each:

| Lua | Engine | Gate reads |
|---|---|---|
| `Trigger.*`, `Actor.*`, `Player.*`, `Map.*`, `Test.*`, … | every `ScriptGlobal` subclass, table name from `[ScriptGlobal("…")]` (`ScriptContext.cs:211-226`) | the class body |
| `someActor.Foo` | union of `ScriptActorProperties` subclasses (`ScriptActorInterface.cs:38-47`) | all such classes |
| `somePlayer.Foo` | union of `ScriptPlayerProperties` subclasses (`ScriptPlayerInterface.cs:23-28`) | all such classes |
| `Stryker`, `Manpad`, … | every name under `Actors:` becomes an `Actor` global (`MapGlobal.cs:34-36`) | the scenario's `map.yaml` |
| `pos.X`, `cell.Layer` | the `ILuaTableBinding` indexers on `CPos`/`CVec`/`WPos`/`WVec`/`WDist`/`WAngle` | the `case "…"` labels |

Member selection reproduces `ScriptMemberWrapper.WrappableMembers`
(`ScriptMemberWrapper.cs:125-142`): public, instance, **declared on that class only**,
no generic method definitions, no fields. That is why `Trigger.GetScriptTriggers` — a
`public static` helper sitting in `TriggerGlobal` — is correctly reported as not a binding.

Which scripts a scenario loads is read from its `Scripts:` line, so `TestHarness.*` is
known only to scenarios that actually list `test-helpers.lua`.

**A declared script is checked wherever it resolves — the scenario folder or
`mods/ww3mod/scripts`.** Until 2026-09-15 only the first was scanned: a body shared between
scenarios (an A/B arm pair, or `javelin-probe-lib.lua` across its four) was loaded by the
engine and read here only for the globals it *defines*, never for the references it *makes*,
and the run still closed with `OK — every reference resolves`. The verdict-reachability check
was skipped outright for such a scenario rather than failing on it, so a shared body that
asserted nothing produced no finding at all. Both are pinned by `selftest`. A shared lib is
scanned once, under the first scenario that declares it — see the comment at the call site
for why per-scenario rescanning was tried and dropped.

### The parse is checked against engine reflection

Parsing C# with regular expressions is exactly the kind of thing that half-works and then
quietly under-reports, which for a gate means a false green. `ExtractLuaDocsCommand` walks
the same types through the same `WrappableMembers` by reflection, so it is ground truth:

```
$ ./utility.sh --lua-docs > /tmp/lua-docs.md          # needs a build
$ ./tools/lua-gate/lua_gate.py verify --docs /tmp/lua-docs.md
lua-gate verify: 22 tables, 372 members parsed from source vs 22 tables, 372 from --lua-docs.
lua-gate verify: OK — source parse is byte-identical to the engine's reflection dump.
```

`verify` is the only mode that needs a build, and it is not wired into any target. Re-run
it by hand after touching the extractor. `selftest` is the buildless stand-in: it pins ~20
specific facts (`Trigger.AfterDelay` exists, `Trigger.OnTick` does not, `Location` is an
actor property and not a player one, `CPos` is exactly `X`/`Y`/`Layer`) plus an end-to-end
pass over a snippet carrying both real failures.

## What this does NOT check

**Read this before treating a green run as evidence.** A guard that appears to cover a
failure it cannot see is worse than no guard, because the next person stops looking.

1. **Syntax and runtime errors of every other kind.** This resolves *names*. It does not
   compile the Lua and it does not run it. A scenario that parses clean and names only real
   bindings can still abort on load for any other reason.
2. **Argument types, arity and order.** `Trigger.AfterDelay("soon", 5)` passes here and
   throws at runtime. Only the member name is checked, never the call.
3. **Trait-gated actor and player properties — the big one, and the one that actually bit.**
   The actor set is the *union* over all `ScriptActorProperties`; the engine intersects it
   per-actor with that actor's traits (`ScriptContext.cs:362-373`). 73 of the 92 actor
   properties are gated this way — only 19 (`IsDead`, `IsInWorld`, `Owner`, `Type`,
   `Stance`, `Flash`, `Teleport`, …) are safe on every actor. `tank.Produce` is in the
   union, so it passes here and throws at runtime because the tank has no `Production`
   trait. Deciding this properly means resolving each actor's rules through inheritance,
   which this tool does not do. **The only slice of it that is checked is the one narrow
   `GetActors()` heuristic described at the top**, and that heuristic is pattern-matched on
   `for … in ipairs(…)`, so an equivalent walk written any other way is not covered.
   Do not read a green run as "every property read is valid for its receiver."
4. **Roughly half of all member accesses, because the receiver is untyped.** Measured over
   the current corpus: 5927 accesses, of which 3099 (52.3%) are on a known engine table, a
   pinned CLR value, or a Lua builtin, and **2828 are not checked at all**. A type is
   pinned only when the variable is assigned exactly once, at table-constructor depth 0,
   from a global-table member whose C# return type is known. Function parameters, callback
   parameters (`Trigger.OnKilled(a, function(self) … end)` — `self` is unchecked),
   table fields, and anything reassigned are all untyped and therefore unchecked.
5. **Anything reached through a table or a computed name.** `t[k].Foo`, `t.a.b`,
   `_G[name]`, and string-built member names are invisible. Only `base.Member` and
   `base:Member` with literal identifiers are read.
6. **Scoping is approximate.** A name declared `local` *anywhere* in a file is treated as
   local *everywhere* in it. If a scenario ever shadows an engine table (`local Trigger =
   …`), every `Trigger.*` in that file goes unchecked rather than being misreported. Errs
   toward silence.
7. **Lua defined by helper scripts is name-checked, not member-checked.** `TestHarness` is
   known to exist for scenarios that load `test-helpers.lua`, but `TestHarness.AssertWithn`
   is not flagged. Helper tables can be populated dynamically, and a member check on them
   produced enough false positives to be worse than nothing.
8. **The mod's own maps.** Coverage is `tools/autotest/scenarios/` only. `mods/ww3mod/maps/*.lua`
   (`arena.lua`, `river-zeta-frontline.lua`, `shellmap-open-field.lua`) is not gated.
9. **It cannot tell a stale reference from a new one.** If a binding is renamed in C#, the
   gate turns red on every scenario still using the old name — correct, but the fix is in
   the scenarios, not here.

Nothing on this list is caught by `make lua-gate` being green. In particular **(3) and (4)
mean a passing run does not prove a scenario will load.** It proves it will not fail *for
the two reasons above*.

## Acceptance test

**Failure 1**, plus a `Player`-receiver case, injected into a scratch copy of
`test-radar-only-targetable` wired through its real `rules.yaml` and `map.yaml`:

```
$ ./tools/lua-gate/lua_gate.py check --scenario zz-scratch-red
…/zz-scratch-red.lua:116: [error] Trigger.OnTick — table 'Trigger' (TriggerGlobal) defines no member 'OnTick'.
…/zz-scratch-red.lua:133: [error] usa.Location — 'usa' is a Player; no Player property 'Location' exists on any player. Did you mean HomeLocation?

lua-gate: FAIL — 2 undefined reference(s), 0 warning(s).
```

`HomeLocation` is in fact the property wanted there.

**Failure 2** on its own real code — `test-drone-lost-track` restored to its pre-fix state
at `1d3c9db0^`, then to the fixed state at `2890fccc`, with nothing else changed:

```
$ ./tools/lua-gate/lua_gate.py check --scenario zz-red-drone      # pre-fix
…/zz-red-drone.lua:96: [warn] a.Location — 'a' iterates a player's GetActors(), which
includes the player actor; 'Location' needs IOccupySpace and reading it off an actor that
lacks the trait THROWS (it does not return nil). Ask the question spatially
(Map.ActorsInCircle) or filter by Type first.
lua-gate: WARN — 1 warning(s).                                    # exit 1

$ ./tools/lua-gate/lua_gate.py check --scenario zz-red-drone      # post-fix
lua-gate: OK — every reference resolves to a registered binding.  # exit 0
```

Line 96 is the line that aborted the run, and the suggested remedy is the one the fix
actually used. The check discriminates between the two revisions rather than firing on
both.

The clean tree passes: 273 scripts across 307 scenarios, zero errors (at `wt/scenario-audit`,
base `main @ 6e5721ae`). Scenario count is now every directory, not only those carrying Lua —
a script-free tournament scenario can be inert in exactly the same ways.

## The second failure class: a scenario that is SILENTLY INERT

*(Added 2026-09-06, `wt/scenario-audit`.)*

Everything above is about Lua that names something the engine does not have. This section is
about a scenario the engine accepts completely — lint clean, `--check-yaml` clean, map draws
— **whose own content was never read.** Three of these were diagnosed by hand in one day,
twice after a launch slot had already been spent, and none of the existing gates can see
them, because there is nothing malformed to report.

The root is that every external-file field on a map is optional. `MapField.Deserialize`
(`Map.cs:99-108`) looks the key up in `map.yaml` and, when it is absent and the field is
`required: false`, plainly `return`s. No warning, no log line. A `rules.yaml` sitting next to
a `map.yaml` that never says `Rules: rules.yaml` is read by nobody, including the
`LuaScript` trait inside it that would have run the scenario.

**The engine's own Lua lint cannot see this, and the reason generalises.** `CheckLuaScript`
(`Lint/CheckLuaScript.cs:21-23`) reads the World actor's `LuaScriptInfo` and returns early
when there is none. An unloaded `rules.yaml` produces exactly that state, so the one lint
that exists for scripting returns clean *precisely because* the scripting was never wired.

| # | What is wrong | Severity | Engine reference |
|---|---|---|---|
| 1 | a `.yaml` in the scenario directory that `map.yaml` never declares | error | `Map.cs:102-107` returns silently |
| 2 | a declared file that is not in the directory | error | `Map.cs:583-592` swallows the open failure into `debug.log` and falls back to stock rules |
| 3 | `Scripts:` parented anywhere but `World > LuaScript` | error | `LuaScriptInfo` is `[TraitLocation(SystemActors.World)]`, `Scripting/LuaScript.cs:21-26` |
| 4 | `-LuaScript:` on `World` alongside a live `Scripts:` | error | a removal resolving last leaves no trait and no error |
| 5 | no reachable `Scripts:` names a `.lua` that exists | error | the file's `WorldLoaded` never runs |
| 6 | a top-level rules key differing from the mod's only by case | error | `MiniYaml.Merge` is ordinal, `MiniYaml.cs:410`, `:550` |
| 7 | a `test-` scenario reaching no terminal `Test` verdict | warn | no `result.json`; the run dies as a watchdog TIMEOUT-FAIL |
| 8 | a self-rescheduling driver function nothing ever starts | error | its `WorldLoaded` never names it, so the loop never ticks |
| 9 | `TestHarness.EnsurePower` with no `Test.ActivateSupportPower` | warn | the power is bought and charged; nothing fires it |

Reachability is the load-bearing word in 3, 5 and 6. A `Scripts:` line inside a `rules.yaml`
the map never declares does **not** count as declaring anything — which is the bug this gate
itself used to have. `scenario_scripts` read `rules.yaml` unconditionally, so on failure
mode 1 it reported a green run for a scenario whose script the engine had never heard of.
**A static checker of an optional-file system has to model the optionality, not just the
files**, or it reports the configuration the author intended rather than the one that runs.

Check 7 is a warning rather than an error because three of its four current findings are
deliberate: `test-burn-arena`, `test-burn-compare` and `test-minelayer-mode-survives-modifiers`
each say in their own header comment that they are manual, hold the window open and assert
nothing. They are demos wearing a `test-` prefix. The check cannot tell that from an
assertion that was lost, so it reports and lets a person decide.

### 8 and 9: the rig is wired, and the rig does nothing

*(Added 2026-09-19.)*

Modes 1–6 are about a scenario the engine never READ. These two are about one it read in
full, loaded, and ran — whose content still never executed. Same end state, opposite cause,
and the second is harder to see because every visible thing works.

**`demo-nuke-river-zeta` fired nothing on two consecutive runs.** Its purchase loop was a
textbook `local function step() … Trigger.AfterDelay(1, step) … end`, complete and correct,
and the copy it was made from had lost the one line at the bottom of `WorldLoaded` that
starts it. Cameras panned on schedule, five screenshots were taken, the scenario exited
clean, lua-gate passed. The only symptom was cash sitting at its starting value.

The trap that makes this a check rather than a grep is written into that scenario's own
source: **the literal string `Trigger.AfterDelay(1, step)` appears twice** — once as the
kickoff and once, identically indented, as the reschedule inside `step` itself. A person
grepping for the kickoff finds the reschedule and concludes the loop is wired. *(Writing the
acceptance test for this check, the author reconstructed the pre-fix file with
`grep -v` on that line and deleted **both** occurrences, which removed the self-reschedule
and made the check correctly stay silent. The trap is live and it is not hypothetical.)*

So mode 8 is decided by a real reachability walk over function bodies, not by a spelling:

* roots are file-scope code, plus every function bound to a **global** name — the engine
  calls `WorldLoaded` and `Tick`, and a global is callable from any other script the map
  loads, so nothing may declare one dead;
* a function bound to a **local** name becomes reachable once its name is *read* from
  somewhere already reachable — a direct call, a callback argument, a table field, a
  `return` — anything that is not a write to it;
* an **anonymous closure** is reachable when the code that creates it is reachable. It is a
  value being handed somewhere at that moment, and assuming it gets called errs toward
  silence.

It runs to a fixpoint, so a driver started from a function that is itself started from a
callback is reachable, and a **chain of two orphans** is not. A finding needs the function to
reschedule *itself* as well as being unreachable: a local that is merely never used is dead
weight rather than an inert scenario, and is a noisier finding that belongs to a different
check.

The span scanner underneath returns **`None` rather than a guess** whenever its block
keywords fail to balance. This feeds a check that reports code as *unreachable*, and a
scanner that had lost its place would report live code as dead — the one failure a gate must
not have. Silence is the right answer to "I no longer know where I am".

**Mode 9 is the same silence reached from the other end.** `TestHarness.EnsurePower` buys a
power and waits for its charge; `Test.ActivateSupportPower` is the only binding that puts it
on the map. A rig with the first and not the second builds its entire setup — cash, sandbox
checkbox, proxy actor, charge poll — and then completes having launched nothing, which from
the outside is indistinguishable from a strike that landed and did no damage.

It is a **warning**, not an error, for one honest shape the check cannot tell from the
mistake: a test asserting that a power *becomes available* — that the buy tab takes the
order, or that a cooldown expires — legitimately ensures a power and never fires it. No
scenario in the tree does that today; the severity is set for the one that eventually will.

Mode 9 is fed the scenario's **own body only**, exactly as check 7 is, and for a sharper
reason: `EnsurePower` is *defined* in `test-helpers.lua`, which every scenario in the tree
declares. A version of this check that scanned helper text would fire on all 350 of them.
That contract is pinned by a selftest case rather than left to this paragraph.



`Test.Pass`, `Test.Fail`, `Test.Skip` and `Test.ForceDesyncAndCapture` all end the run, and
all four funnel through `ExitWhenCapturesFlushed` in `TestGlobal.cs`. The gate looks for that
call rather than keeping a list of names, so a fifth verdict added in C# is picked up for
free and a renamed one stops counting instead of silently still counting. Missing
`ForceDesyncAndCapture` from a hand-written list was in fact the first false positive this
check produced.

### MiniYaml's indent arithmetic is transcribed, not approximated

`miniyaml_split` copies `MiniYaml.cs:239-263` line for line, because the rule is not
`leading_whitespace // 4`. A tab is one level and does **not** reset the space counter, and a
leftover run of 1-3 spaces is discarded contributing nothing:

| input | level |
|---|---|
| `"\tKey:"` | 1 |
| `"        Key:"` (8 spaces) | 2 |
| `"      Key:"` (6 spaces) | 1 |
| `"  Key:"` (2 spaces) | 0 |
| `"\t  \tKey:"` | 2 |

One scenario (`test-decisive-win-attribution`) already indents its `rules.yaml` with spaces,
so this is live, not hypothetical. The same transcription replaced a `^\t` regex in
`map_actor_names`, which would have returned an **empty** actor set for a space-indented
`map.yaml` — turning off every actor-global check for that scenario while still printing OK.

### Acceptance test

These checks find nothing on the clean tree, which is exactly why they need one: a guard
nobody has watched fire is indistinguishable from a guard that cannot. Each mechanism is
built as a synthetic scenario in a temp directory inside `selftest`, the check is required to
report it, and a correctly-wired twin is required to stay silent.

```
$ ./tools/lua-gate/lua_gate.py selftest
  …
  ok    a correctly wired scenario produces no finding
  ok    an undeclared rules.yaml is reported
  ok    ...and its Scripts: line is NOT counted as loaded
  ok    a declared-but-absent rules.yaml is reported
  ok    a Scripts: under Player instead of World is reported
  ok    a -LuaScript: alongside a live Scripts: is reported
  ok    a mis-cased top-level key (1tnk.husk vs 1TNK.Husk) is reported
  ok    ...and the correctly-cased 1TNK.Husk does not fire
  ok    a test- scenario with no reachable verdict is reported
  ok    ...and one reaching Test.Skip does not fire
  ok    a self-rescheduling driver nothing starts is reported
  ok    ...and the same driver with its kickoff restored is not
  ok    a driver started from a Trigger.OnKilled callback is not reported
  ok    a driver stored in a table field is not reported
  ok    a driver started indirectly through another function is not reported
  ok    a forward-declared `local f` / `f = function` driver, kicked off, is not reported
  ok    ...and the same one with its kickoff removed is reported
  ok    a chain of two orphans (the starter is itself never called) is reported
  ok    a scenario that charges a power and never fires it is reported
  ok    ...and one that fires it is not
  ok    helper text WOULD fire, so run_check must pass the scenario body only
  ok    run_check reports an unstarted driver as an ERROR
  ok    run_check reports a charged-but-unfired power

lua-gate selftest: OK — 72 case(s).
```

The four negative driver cases are the ones that earn the check its keep. A guard that
reports "nothing ever starts this" has to be trusted not to fire on the ways the tree
actually DOES start one, or the first person it lies to turns it off for everybody. The last
two run the whole of `run_check` over a synthetic scenario on disk rather than calling the
analysis directly — a finding that never reaches a `Finding()` is a check nobody will see
fire, and **that fixture immediately earned itself**: mode 9's finding was being dropped in
silence by the dedupe, which keys on `(path, line, symbol, severity)` and collided with
check 7 on the same file at the same line 0. Both are `warn`, both key off the scenario name.
Fixed by keying mode 9 on `TestHarness.EnsurePower` instead.

### What this section does NOT check

The list at the top of this README still applies in full. In addition:

1. **Whether a scenario that IS wired does anything useful.** Reaching `WorldLoaded` is not
   the same as asserting something true. A test whose `AssertWithin` predicate can never
   become true passes this and times out exactly like an unwired one.
2. **Inline rules in `map.yaml`.** `Rules:` may carry nodes as well as a filename
   (`MiniYaml.cs:633-635`), and a scenario doing that is not flagged for having no
   `rules.yaml` — correctly, but it also means the `Scripts:` reachability walk reads only
   the files, not any inline `LuaScript` block. No scenario in the tree does this today.
3. **`scenarios.yaml` overlays.** `Map.ApplyScenario` (`Map.cs:663-745`) merges a named
   scenario's actors, players and rules on top of the map's own, with a second identical
   swallowing `catch`. Nothing under `tools/autotest/scenarios/` uses it, so it is not
   modelled here; if it starts being used, this gate goes blind to a whole second layer.
4. **Case collisions below the top level.** Only top-level keys are compared. A mis-cased
   *trait* name fails loudly through `ObjectCreator`, so it is not in this class.
5. **The mod's own maps**, same as before: `mods/ww3mod/maps/` is not covered.
6. **Drivers reached through a computed name.** Mode 8 reads `f`, `t.f = f`, `{ f = f }`,
   `g(f)` and `return f`. It does **not** model `_G["st" .. "ep"]`, a name assembled at
   runtime, or a dispatch table indexed by a variable — limitation 5 at the top of this
   README, applied to reachability. All three err toward **silence**: an indirection the
   walk cannot see is a reference it does not count, which would make a live driver look
   dead — so anything of that shape is filtered out by the self-reschedule requirement
   before it can become a false positive. If a scenario ever does dispatch drivers through
   a table indexed by a computed key, this check goes blind to it rather than wrong about
   it.
7. **A driver whose body is in another file.** Locals are file-scoped, so a local driver
   cannot be started from elsewhere and the question does not arise; a *global* wrapper
   around one is treated as reachable without asking whether anything calls the wrapper.
   That is the deliberate root rule above, and it is where a determined orphan could hide.
8. **Whether the driver does anything once started.** Same limit as (1) in this list: a
   loop that ticks and asserts nothing passes mode 8 exactly like one that works.

## Findings on the clean tree

**`demo-experimental-capture-coordinator` — FIXED 2026-09-06.** It declared no `Scripts:` in
either `rules.yaml` or `map.yaml`, so the engine never loaded
`demo-experimental-capture-coordinator.lua`. Its whole body is one
`TestHarness.FocusBetween(...)` call that frames the camera across the four neutral
capturables, so the demo had been opening on an unframed camera while its own header
promised "Camera starts in the middle for full view". Cause: its `rules.yaml` began as a copy
of a *tournament* rules.yaml, which has no scripting section, and the `LuaScript` trait was
never added back. The four actor names the script uses (`NeutralBio`, `NeutralFcom`,
`NeutralOilb1`, `NeutralOilb2`) were confirmed present in `map.yaml` before wiring it up —
without that check, enabling the script would have framed nothing and looked identical.

**Modes 8 and 9 find nothing** (2026-09-19, `wt/lua-gate-driver`, base `main @ 64185a89`).
Scanned: 350 scenario directories, 315 declared scripts, 317 `.lua` files including the
shared bodies under `mods/ww3mod/scripts`. Zero orphaned drivers, zero charged-but-unfired
powers, and **zero span-scanner desyncs** — that last number is the one that makes the other
two mean anything, since a desynced file is skipped in silence. 26 scenarios name
`EnsurePower` or `ActivateSupportPower`; none has the first without the second.

Both were verified against real code rather than only synthetic fixtures:
`demo-nuke-river-zeta` with its kickoff line removed (and **only** that line — see above)
reports `step` as an error at its definition line, and the shipped file reports nothing.

**Four `test-` scenarios reach no verdict** (check 7 above). Three say so in their own
comments and are demos wearing a `test-` prefix; `test-artillery-turret` calls only
`FocusBetween` and `Select` and appears to have lost its assertion. All four are left for a
person: renaming a scenario or inventing an assertion is a judgement about what it was for.
