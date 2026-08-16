# Crash sweep — absent-key, optional-trait and lazy-asset throws

> **Scope:** hunt for crash-to-desktop defects ahead of public release, generalising from two crashes found on 2026-08-16.
> **Read-only.** No production code changed, no build, no game launch, no autotest.
> **Against `main` @ `fc62f3b4`**, working tree clean, in sync with `origin/main`.
> Line numbers are current at that SHA.

---

## 0. The brief's premise is half wrong

The sweep was commissioned on the theory that both 2026-08-16 crashes were "the same shape: a
lookup by a key that can be absent". **They are opposite failure modes**, and the distinction
changes what you should sweep for.

| | Veteran crash (`8c05b2ff`) | `ClientInSlot` crash |
|---|---|---|
| Exception | `KeyNotFoundException` | `InvalidOperationException` |
| Cause | key **absent** | key **duplicated** |
| Trigger | `Rules.Actors["PILOT"]` — YAML capitalised, dict keys lowercased at load | two clients in one slot |

`SingleOrDefault` **returns null on zero matches and throws only on two or more**. So the
`ClientInSlot` crash cannot be an absent-key bug; it is a duplicate-key bug. Sweeping only for
absent keys would have missed it entirely.

**The shape they actually share** is more useful than either: *a runtime lookup whose invariant is
established somewhere far away and never re-checked at the lookup site.* For the veteran crash the
invariant was "this name exists in `Rules.Actors`", established in YAML. For `ClientInSlot` it was
"at most one client per slot", established in `SetupShellmapBots`. Both lookup sites assumed a
guarantee made by code they do not reference.

**A third premise correction, on the sound specimen.** The brief describes the `Sound.LoadSound`
incident as showing that "a missing or corrupt asset is not a load-time error, it is a crash the
first time a weapon fires". Half of that is no longer true: `Sound.cs:59-66` checks
`fileSystem.Exists` **first** and returns `default`, and the result is memoised in a `Cache<>`
(`:97`), with `OpenAlSoundEngine.Play2D` null-checking. **A missing sound is a permanent, cached,
silent no-op — one log line, ever.** The only residual throw is `InvalidDataException` at `:81` for
a file that exists but no loader parses (all 24 shipped `.wav` are PCM or IMA ADPCM, both
supported — clean). The original `DirectoryNotFoundException` therefore came from a mix whose
backing *file* was removed under a live process — a torn install, not a bad name in YAML.
**The lazy-asset-crash premise is right, but it belongs to SPRITES, not sounds — and there it is
worse than the brief assumed. See §5a.**

That reframing predicts a different third class than the brief does — not more `.First()` calls,
but **more non-idempotent setup routines that can run twice**. `SetupShellmapBots`
(`engine/OpenRA.Game/Game.cs:592-628`) shows the tell precisely, and it is visible *within a single
loop*: `lobbyInfo.Slots[slotKey] = ...` (:603, indexer — idempotent) sits eight lines above
`lobbyInfo.Clients.Add(...)` (:613, append — not idempotent). Re-running overwrites the slot but
appends a second client to it. **When auditing a setup routine, diff its writes for that
overwrite-vs-append asymmetry.**

---

## 1. Severity calibration — an exception in tick really is crash-to-desktop

Worth establishing before ranking anything, because it justifies weighting tick-reachable throws
above everything else.

`Game.LogicTick()` (`engine/OpenRA.Game/Game.cs:822`, called from the run loop at `:1023`) has
**no `try`/`catch`**. The only handler is `AppDomain.CurrentDomain.UnhandledException` in the two
launchers (`OpenRA.Launcher/Program.cs:28`, `OpenRA.WindowsLauncher/Program.cs:67`), which routes to
`ExceptionHandler.HandleFatalError` — that writes an `exception-*.log` and stderr, and **does not
recover**. So any throw inside `World.Tick` terminates the process.

Confirmed by reading. A tick-reachable throw is a hard CTD, not a caught error dialog.

---

## 2. Predictions registered before verifying — and how they came out

Recorded so the wrong ones are visible rather than quietly dropped.

| # | Prediction | Outcome |
|---|---|---|
| 1 | The two-crashes-one-shape premise is wrong; `ClientInSlot` is a duplicate-key crash | **RIGHT** — see §0 |
| 2 | Capitalised YAML actor names are a wide-open crash class, since lint lowercases before checking | **WRONG** — see §3. The most useful negative result in this sweep |
| 3 | Most `.First()`/`.Single()` hits will be vendored upstream and uninteresting | Broadly right |

---

## 3. WRONG PREDICTION, and why it matters: capitalised actor names are NOT an open crash class

I expected this to be the headline. It is not, and the manager should **not** spend effort on it.

The reasoning that made it look alarming is real as far as it goes:

- `CheckActorReferences.cs:70` lowercases a value **before** its `ContainsKey` test (an upstream
  workaround for OpenRA #4124). So lint declares a capitalised `[ActorReference]` value valid.
- `Ruleset` lowercases every actor name at load, and `ActorInfoDictionary` uses the default
  ordinal comparer. So a capitalised name genuinely misses at runtime.
- **101 capitalised `[ActorReference]` values are present in shipped mod YAML** across 44 distinct
  fields — husks (`Actor: HIND.Husk`), pilots (`PilotActor: PILOT`), missiles
  (`Actors: IskanderMissile`), `Mine: MINV`, and the `SupportActors` starting-unit lists.

**But the consumers normalise, so none of the 101 can crash.** `Actor.cs:163` does
`name = name.ToLowerInvariant()` *before* the `ContainsKey`/lookup pair, so **everything routed
through `World.CreateActor` is safe** — which is every husk, pilot, missile and starting unit in
that list. Spot-checked the non-`CreateActor` consumers too: `EjectOnDeath.cs:64,90` and
`AirstrikePower.cs:74` both `.ToLowerInvariant()` explicitly.

The residual risk is only a consumer that indexes `Rules.Actors[...]` with a YAML-supplied name
*without* going through actor creation — which is exactly what the veteran crash was
(`PlayerStatistics` built an `ArmyUnit` directly). I enumerated those consumers; see §4.

**Contradicts the reconciliation audit.** `260816-bug-reconciliation.md` §4 lists
`AirstrikePower.cs:104` as a latent defect that "still passes `info.UnitType` un-lowercased into
`CreateActor`". Both halves are now wrong: `:104` does pass it un-lowercased, but `CreateActor`
lowercases internally, and the *other* consumer at `:74` lowercases explicitly. That entry should be
struck.

**The veteran fix is durable, not a point patch.** `PlayerStatistics.cs:295` normalises once into a
single `actorName` field consumed by all seven downstream sites (`:307,319,351,359,371,385,400,411`),
with a comment naming the lint gap. A new call site would have to go out of its way to reintroduce it.

---

## 4. Verified clean — negative results worth recording

Recorded so nobody re-derives them. Each was checked against code, not assumed.

- **Influence-stack round-robin schedulers.** `DangerFieldLayer.cs:467`, `ControlField.cs:558` and
  `BeliefStore.cs:185` all do `cursor = (cursor + 1) % participants.Count` inside a tick — a
  `DivideByZeroException` if the participant list is ever empty, which a human-vs-human game could
  plausibly produce. **All three carry `if (participants.Count == 0) return;` on the immediately
  preceding line.** Correctly guarded.
- **YAML-configurable divisors.** `InfluenceMap.CellSize` (default 2) / `ValueDivisor` (100),
  `DangerFieldLayerInfo.HealthDivisor` (1000) / `CostDivisor` (5000) are divisors reachable in tick.
  All defaults non-zero; no shipped YAML sets any of them to 0 (`world.yaml:284,291` set `CellSize`
  to 8 and 2). `DangerFieldLayer.cs:998` guards `shotsPerBurst` with `burst > 0 ? burst : 1`.
  Latent for a modder or a map-rules override only — **not** reachable in the shipped game.
- **`DropsSupplyCache`.** `:129` calls `self.Trait<SupplyProvider>()`, which throws when absent, and
  `:134`/`:152` then null-check the result — a guard that can never fire. Harmless, because
  `DropsSupplyCacheInfo : Requires<SupplyProviderInfo>` makes the trait's presence a load-time
  constraint. The `Rules.Actors[Info.SupplyCacheActor]` lookup at `:176` is safe both ways: the
  default and the only YAML value (`vehicles.yaml:593`) are lowercase `supplycache`.
  **`Requires<T>` is what makes `Trait<T>()` safe** — worth knowing before flagging any
  `Trait<T>()` call as a null-deref risk.
- **`Map.cs:1230`** `Rules.Actors[actor.Value.Value]` is pre-filtered at `:1223` to names already
  present in `Rules.Actors`, so it cannot miss. It is also a map-editor path.
- **`LobbyPresetLogic.cs`** (WW3MOD-authored) contains no `Slots[...]`, `Players[...]`, `.First(`,
  `.Single(`, `.Max(` or `.Last(` call at all.

---

## 5. Confirmed still-live from the prior audit

- **`CreatesShroud` → `NotImplementedException`.** Re-verified at `fc62f3b4`:
  `AffectsMapLayer.cs:201` is `public virtual MapLayers.Type Type => throw new
  NotImplementedException();`, `CreatesShroud.cs` has **no `Type` member**, and `CreatesShroud`
  appears in **no** mod YAML. Still latent, still a one-line fix, still fires the instant anyone
  adds a jammer/smoke/stealth actor or a map author adds the trait via map rules.

---

## 5a. The one systemic finding: WW3MOD moved a load crash to a render crash, and did not harden the read

**This is the most important thing in the sweep, and it is a mechanism rather than a bug instance.**

Upstream OpenRA **throws at map load** when a sequence references sprite frames that do not exist.
WW3MOD deliberately replaced that with a clamp — `DefaultSpriteSequence.cs:629-648`, carrying an
explicit `// PITFALL: clamping here is WW3MOD-specific - upstream throws.` The stated purpose is to
keep an incomplete Red Alert install bootable. A missing file yields
`sprites = Array.Empty<Sprite>()` (`:621`) and, for `Length: *`, `length = 0`.

**The consumer was never hardened to match.** `DefaultSpriteSequence.cs:697-698`:

```csharp
var index = GetFacingFrameOffset(facing) * length.Value + frame % length.Value;
var sprite = sprites[index];
```

`frame % length.Value` throws `DivideByZeroException` when `length == 0`; `sprites[index]` throws
`IndexOutOfRangeException` on the empty array. The `if (sprite == null)` guard on the next line is
unreachable in that case. `Animation.Render` (`engine/OpenRA.Game/Graphics/Animation.cs:62`) has no
zero-length check either.

Net effect: **a missing sprite file for any sequence that something actually draws is a crash on the
first frame it is drawn, and it throws again every frame after.** Verified both halves personally.

Why this is worth acting on even though nothing triggers it today:

- **It is invisible to developers.** The failure moved off the loading screen — where a developer
  with a complete install would never see it anyway — to first render on a machine that lacks the
  file. That is precisely the stranger's-clean-install asymmetry this sweep exists to catch.
- **It is invisible to lint.** **No lint pass checks that any asset file exists** — not
  `CheckSequences`, and there is no sound lint at all. `SpriteCache.MissingFiles` is exposed but
  unconsumed. `make test` does not reproduce the cross-reference that found this.
- **It is armed but unloaded.** Only four sequence filenames in the mod resolve to nothing
  (`pip-cloak`, `pip-cover` in `sequences-misc.yaml:199-238`; `b2bomb` in `sequences-ingame.yaml:378`;
  `emp_fx01` in `sequences.yaml:2`, which sits under an abstract `^VehicleOverlays` node nothing
  inherits). **None is drawn by any trait**, so there is no live crash. The next dangling reference
  that *is* drawn becomes one.

Cheapest durable fix: guard the zero-length case at `GetSprite` rather than removing the clamp.

## 5b. Ranked live findings

Ranked by *probability a real player triggers it × severity*. **Nothing in this sweep is a live
crash on shipped content.** The honest headline is that the two 2026-08-16 crashes do not have a
population of siblings — see §7.

| # | Finding | `file:line` | Bucket | In tick? | Trigger a player would recognise | Status |
|---|---|---|---|---|---|---|
| 0 | **Zero-length sprite sequence → `DivideByZeroException` / `IndexOutOfRangeException` on first render, every frame after.** See §5a — a mechanism, not an instance | `DefaultSpriteSequence.cs:697-698` (armed by the clamp at `:629-648`) | **WW3MOD** | yes (render) | The first time the game tries to draw a unit/effect whose sprite file is missing from *that player's* install | **ARMED, no live instance** — the 4 dangling refs are never drawn. Highest-value fix in the sweep |
| 1 | `.ToDictionary(o => o.Id, o => o)` over every `ILobbyOptions` throws `ArgumentException` if two lobby options share an ID. Duplicate-key shape — same family as the `ClientInSlot` specimen | `LobbyPresetLogic.cs:312` | **WW3MOD** | no | Opening the lobby on a custom map that adds a colliding lobby option; the lobby fails to open. Reached on lobby OPEN, not only on a button click (`:132` `ApplyPreset(LastGamePresetName)`) | **LATENT** — shipped config has one dropdown (`player.yaml:187`), no collision |
| 2 | `Values.ToDictionary(v => v, …)` throws on a duplicated dropdown value | `LobbyPrerequisiteDropdown.cs:67` | **WW3MOD** | no | Same, via a map that duplicates a value | **LATENT** — shipped `Values:` are 21 distinct integers |
| 3 | `CreatesShroud` has no `Type` override; base throws `NotImplementedException` | `AffectsMapLayer.cs:201` | **WW3MOD** | yes (world entry) | Any actor with `CreatesShroud` hard-crashes on entering the world | **LATENT** — trait in no mod YAML; fires the instant a jammer/smoke/stealth actor or a map rule adds it. One-line fix |
| 4 | `self.TraitsImplementing<AmmoPool>().First(ap => ap.Info.Name == useAmmo)` throws on zero matches | `Demolish.cs:56,78` | **WW3MOD** | activity | Ordering a demolition with an actor whose `UseAmmo` names a pool it lacks | **LATENT ×2** — both users (`^E6`, `^SF`) declare the matching pool *and* are `Prerequisites: ~disabled` |
| 5 | `task.Carrier.Trait<Cargo>().PassengerCount` is an unconditionally-evaluated argument to `AIUtils.BotDebug` — the one place the resolve-once-then-gate idiom is abandoned | `MountedTransportBotModule.cs:1121`, `HelicopterSquadBotModule.cs:1290` | **WW3MOD** | yes (bot tick) | None today — safe only via pruning earlier in the same tick | **SOFT** — a refactor moving this line out from under its guard reopens it |
| 6 | `owner.Units.First()` with no internal guard | `GroundStates.cs:27` | **UPSTREAM** (see §6 — blame misattributes this) | yes | An AI squad's last member dies while the squad still ticks | **NOT LIVE** — all 8 callers sit behind `if (!owner.IsValid) return;` |

## 5c. Not crashes, but found on the way — missing sound files (all silent no-ops)

Filed here because the cross-reference that found them is not reproduced by any lint, so they will
not surface again on their own. Each was confirmed absent from `mods/ww3mod/`, from
`engine/mods/{ra,common}`, and from the contents of all 15 RA `.mix` archives.

- **`A10.wav` ×2 and `A10.aud` — `rules/weapons/weapons-ballistics.yaml:650,686,701`. The A-10's
  gun run is completely silent.** `a10gun.wav` *is* shipped, so this looks like a rename that missed
  three references. Most player-visible item in this list — a marquee support aircraft with no
  weapon audio.
- `ptnkfire.aud` (`weapons-other.yaml:515`), `pcanfire.aud` (`:541`), `icolseta.aud` (`:573`),
  `icolexpa.aud` (`:611`)
- `splashl1.aud` / `splashl2.aud` — `weapons-effects.yaml:620`, `weapons-explosions.yaml:454,476`
  (missiles splashing into water)
- `place2.aud` (`structures-defenses.yaml:526`), `clicky1.aud` (`vehicles.yaml:432`)
- Counterstrike/Aftermath music in `rules/sound/music.yaml` — those packages are genuinely optional
  and `Sound.PlayMusic` null-guards at `Sound.cs:259-263`. Not a defect.

## 5d. What to do, cheapest first

1. **Guard the zero-length read** at `DefaultSpriteSequence.cs:697-698` (§5a). Two lines, and it
   disarms the one mechanism in this sweep that converts a future asset slip into a hard CTD.
   Prefer this over removing the clamp — the clamp is deliberate and load-bearing for partial installs.
2. **Add an asset-existence lint.** The real gap is that *no* pass checks whether a referenced
   sprite or sound file exists; `SpriteCache.MissingFiles` is already exposed and unconsumed. This is
   what would have caught both §5a's dangling refs and §5c's silent sounds.
3. **`CreatesShroud` `Type` override** — one line, closes a guaranteed world-entry crash the moment
   anyone adds the trait (§5).
4. **Fix the three `A10` sound references** (§5c) — player-visible, trivial.
5. **Do NOT spend effort on the 101 capitalised actor names** (§3). They cannot crash.

## 6. Method, and where it is weak

**Authorship filter.** `git ls-tree` at the vendoring squash `7362fbc6` vs `HEAD` gives 523 engine
`.cs` files that did not exist upstream. Mapping each to its introducing commit and subtracting
`git rev-list c5bb5ece ^7362fbc6` (the later "apply release-20250330" re-merge) splits them
**249 upstream re-merge / 164 genuinely WW3MOD-authored** (413 after excluding `OpenRA.Test`). File
list at `/tmp/ww3_authored.txt` during the sweep; regenerate rather than trust it.

**The trap this avoids:** filtering by file is useless here — WW3MOD has touched ~1,840 engine `.cs`
files — and filtering by "file is new since `7362fbc6`" is *actively misleading*, because 60% of the
new files came from the upstream re-merge, not from WW3MOD. Both filters must be applied.

**`git blame` systematically OVER-attributes to WW3MOD — do not trust it alone.** Merge and
restore commits re-touch upstream lines without changing their text, and the resulting blame SHA is
post-vendoring, so the line reads as WW3MOD-authored when it is vanilla OpenRA.

Worked example, caught in this sweep. `GroundStates.cs:27`
(`owner.SquadManager.FindClosestEnemy(owner.Units.First().CenterPosition)`) blames to `6f9dd239`,
which is post-`7362fbc6`, so the mechanical filter buckets it WW3MOD. But `6f9dd239` is
*"Upstream merge: restore WW3MOD files from main, fix 10 compilation errors"* — a merge, not
authored logic. `git show 7362fbc6:…/GroundStates.cs` has the line **verbatim** at line 26; only
`protected` → `protected static` ever changed. The `.First()` is vanilla upstream.

**The reliable test is comparing the line TEXT against `git show 7362fbc6:<path>`, not blaming the
SHA.** The reconciliation audit knew this class ("109 were false survivors") but attributed it only
to the re-merge `c5bb5ece`; `6f9dd239` is a second, separate false-survivor source, so the real
false-survivor set is larger than that method accounted for.

**Where this sweep is weak:**
- Static only. Nothing here was reproduced in a running game; no run was permitted.
- The `[ActorReference]` field list was harvested by regex over `public readonly` declarations, so
  any field declared in another form was not scanned.
- §3 clears the 101 capitalised values against the consumers I checked. I did not exhaustively
  enumerate every consumer of every one of the 44 fields — I checked the actor-creation path plus
  the direct `Rules.Actors[...]` sites. A consumer that *compares* `a.Info.Name == Info.SomeField`
  against a capitalised value would silently never match — not a crash, but a real silent bug class
  this sweep did not chase.
