# worldactor-gate

Catches the one shape of bug that stops **every match in the mod from starting** and is
invisible to every static gate we had: a trait on the **world actor** that dereferences
`.WorldActor` while the world actor is still being built.

```csharp
// engine/OpenRA/World.cs:252
WorldActor = CreateActor("World", new TypeDictionary());
```

The assignment happens *after* `CreateActor` returns. So while the world actor's own traits
are being constructed — their constructors, their `INotifyCreated.Created`, and the
`Create(...)` on their Info — `World.WorldActor` is still `null`. Reading through it there
throws a `NullReferenceException` during world construction, and no match starts at all.

That shipped on **2026-09-10** in `DefconWall.Created`. Six gates were green through it:
`make.ps1 all`, `make.ps1 check`, NUnit, the YAML lint, lua-gate and nav-guard. All six read
files. **Nothing we ran constructed a `World`.** This gate is the cheap static half of the
answer; [`tools/autotest/run-smoke.sh`](../autotest/run-smoke.sh) (`make.ps1 smoke`) is the
expensive dynamic half that actually starts the game.

## Run

```bash
.\make.ps1 worldactor-gate                              # selftest + check (also `make.ps1 w`)
make worldactor-gate                                    # the same, on Linux/macOS
.\make.ps1 check                                        # runs it FIRST, before the Debug build
python tools/worldactor-gate/worldactor_gate.py check    # the gate on its own
python tools/worldactor-gate/worldactor_gate.py selftest # fixtures + the tree tripwire
python tools/worldactor-gate/worldactor_gate.py report   # every hit with a per-line verdict
```

Standard-library Python only. No build, no engine, no game launch, ~5 s over 2143 files.

- **exit 2** — a world-located trait reads `.WorldActor` in a member that runs during
  construction. There is no warning band: the field is null there or it is not.
- **exit 0** — clean.

## Why it is in `check` and not `test`

`check` is a Debug build with analyzers; `test` is the contended YAML gate. Two reasons for
`check`:

1. **It is a C# lint.** CLAUDE.md already makes `check` non-optional before committing C#,
   so the gate sits on the path the change it guards already has to walk. Nothing in
   CLAUDE.md requires `test` for a pure C# edit.
2. **Cost is nil where it lands.** ~5 s against a clean Debug rebuild that costs minutes,
   and it runs *first* — so a violation is reported before you pay for the build, not after.
   `test` serialises badly across concurrent worktrees, and adding a fourth gate to it would
   make that worse for no gain.

## The discriminator is the actor, not the idiom

This is the part that makes the gate possible at all, and the part a grep cannot do.

```csharp
// DefconCasualtyObserver — [TraitLocation(SystemActors.Player)] — CORRECT
void INotifyCreated.Created(Actor self) { x = self.World.WorldActor.Trait<Foo>(); }

// DefconWall — [TraitLocation(SystemActors.World)] — CRASHED EVERY MATCH
void INotifyCreated.Created(Actor self) { x = self.World.WorldActor.Trait<Foo>(); }
```

Character for character the same line. Player actors and unit actors are built *after* the
world actor exists, so for them the field is populated and the read is fine; the engine is
full of this and it is not a smell. What is wrong is the same line on a world trait, where
`self` is already the world actor and is the correct receiver.

So the gate keys on `[TraitLocation(...)]` naming `World` (covering `SystemActors.World`,
`SystemActors.EditorWorld`, and combined flag lists) and reports nothing else. Of the 57
`.WorldActor` dereferences currently sitting in files that declare a world-located trait,
**0 are violations** — which is exactly why a gate is needed rather than a grep: the signal
is 0 in 57, and a reviewer reading a grep dump goes blind long before reaching the one that
matters.

## What is in scope

For every class carrying a world-located `[TraitLocation(...)]`:

| member | why it is in scope |
|---|---|
| the Info's `Create(...)` | runs inside `CreateActor`, before the assignment |
| the trait's constructor(s) | called from `Create`, same window |
| the trait's `Created(...)` | `INotifyCreated` fires inside `CreateActor` |

The trait class is resolved from `new Foo(...)` inside `Create`, **project-wide**, so a trait
declared in a different file from its Info is still followed; `FooInfo` → `Foo` is a fallback
for Infos whose `Create` is inherited or written in a shape the scan cannot read.

`IWorldLoaded.WorldLoaded`, `ITick` and every later member are **never** reported. By the time
they run the field is assigned. `.WorldActorInfo` is never reported either — it is `Map`/
`ActorInfo` metadata, a different member, available throughout.

## Negative fixtures

A gate nobody has watched fail is indistinguishable from a gate that cannot. `selftest` runs
six planted violations that must fire and eight correct forms that must stay silent:

```
$ python tools/worldactor-gate/worldactor_gate.py selftest
  red   ok: world trait reads WorldActor in Created -> BadWall.Created line 9
  red   ok: world trait reads WorldActor in its constructor -> BadCtor.BadCtor() line 8
  red   ok: Info.Create itself reads WorldActor -> BadCreateInfo.Create line 6
  red   ok: combined flags still count as world-located -> BadBoth.Created line 8
  red   ok: null-conditional is still a finding -> BadSoft.Created line 8
  red   ok: trait class declared before its Info still counts -> BadOrderFirst.Created line 4
  green ok: the shipped fix: `self`, not self.World.WorldActor
  green ok: a PLAYER trait may read WorldActor in Created
  green ok: an unattributed per-actor trait may read WorldActor in Created
  green ok: a world trait reading WorldActor in WorldLoaded is correct
  green ok: a world trait reading WorldActor in ITick is correct
  green ok: WorldActorInfo is map metadata, not the actor
  green ok: a comment naming the trap is not the trap
  green ok: a lambda stored in Created runs later and is correct
  tree  ok: …DefconWall.cs:DefconWall.Created is in scope (246 members total)
selftest: ok. 6 planted violations caught, 8 correct forms left alone, tree tripwire in scope.
```

The **green** half is the half that matters most. The correct idiom outnumbers the broken one
by roughly forty to one here, so a matcher that cannot tell them apart is not a gate, it is
noise that gets switched off. Two of those greens are specifically about not reading text as
code: `DefconWall.cs` and `NuclearExchange.cs` both discuss `self.World.WorldActor` at length
in comments explaining why they must not use it, and a naive matcher reports the *fix* as the
bug.

The **tree tripwire** is the last line, and it guards a different failure. Fixtures prove the
matcher can tell red from green; they cannot prove it is still *pointed at* anything. A gate
whose scope quietly collapses to zero members passes every fixture it owns and reports a clean
tree forever. So `selftest` asserts that `DefconWall.Created` — the exact member the 2026-09-10
crash lived in — is still in the in-scope set.

### Verified against the real bug, not only fixtures

Re-planting the historical line in the real file:

```
$ python tools/worldactor-gate/worldactor_gate.py check
  VIOLATION engine/OpenRA.Mods.Common/Traits/World/DefconWall.cs:326
            DefconWall.Created runs during world construction (declared by DefconWallInfo),
            and World.WorldActor is null there.
            escalation = self.World.WorldActor.TraitOrDefault<DefconEscalation>();
            Fix: use `self` -- it is the same actor and is always valid. If the read must
            wait, move it to IWorldLoaded.WorldLoaded.
worldactor-gate: 1 violation(s). See engine/OpenRA/World.cs:252.
```

## Accepted exceptions

`ACCEPTED` at the top of the script maps `path.cs:Class.Member` to a written reason. It is
**empty**, and an entry costs a justification on purpose.

Note what is *not* an accepted form: `WorldActor?.Trait<…>()`. A null-conditional does not
throw, so it survives — but in these three members it silently resolves to `null` every time,
which trades a loud crash for a quiet wrong answer. If one is genuinely wanted, it goes in
`ACCEPTED` with a reason rather than being waved through by the matcher.

## What this does NOT check

1. **A `Created` inherited from a base class in another type.** It is attributed to the base,
   not to the world trait that inherits it.
2. **A lambda invoked immediately inside `Created`.** Lambdas compile to their own methods. A
   lambda merely *stored* and run later is correct code anyway, which is the common case.
3. **Reflection and `ObjectCreator`-built instances.** Invisible to any source matcher.
4. **Every other null-during-construction field.** `WorldActor` is the one with a body count.
   `DefconWall` has a second comment warning that `Player.HomeLocation` is likewise unset in
   `Created` — same class of mistake, not covered here.
5. **Whether the world actually constructs.** That is not a static question. `make.ps1 smoke`.
