# DEFCON mode: what already exists, and what the proposal would cost

**Date:** 2026-09-08 · **Branch:** `wt/defcon-research`, forked from `main @ e68498e0` · **Status:** research only, no behaviour changed.

This maps the machinery the user's DEFCON proposal would land on. It does not design the mode and
does not propose an implementation. Every claim below carries a `file:line`; the
"verified / inferred" line at the end of each major section says which ones I read and which ones I
reasoned to.

---

# Page one — the six things worth knowing before the conversation

### 1. The lock a DEFCON ladder needs already exists. What is missing is the thing that *moves* it.

A nuclear power is gated by one condition on its trait — e.g. `RequiresCondition: !tacnuke-disabled`
(`mods/ww3mod/rules/player.yaml:465`). That condition flows into `SupportPowerInstance.Permitted`
(`engine/OpenRA.Mods.Common/Traits/SupportPowers/SupportPowerManager.cs:160-164`), and `Permitted`
is read by **all three** of the shop, the sidebar cameo and the actual firing:

| question | property | line |
|---|---|---|
| may it appear in the buy tab? | `Purchasable` | `SupportPowerManager.cs:175` |
| may its cameo appear in the support bin? | `Disabled` | `SupportPowerManager.cs:172` |
| may it actually fire? | `Ready` → `Active` → `!Disabled` | `SupportPowerManager.cs:183`, `:250`, `:293-296` |

So **"locked until DEFCON 1" is a single condition**, and it locks purchase *and* firing at once.
No new gating concept is needed.

What does **not** exist is anything that changes such a condition **mid-match**.
`GrantConditionOnLobbyOption` grants once, in `Created`, and has no revoke path at all
(`engine/OpenRA.Mods.Common/Traits/Conditions/GrantConditionOnLobbyOption.cs:45-54`). Every powers
gate in the mod is of that shape. A DEFCON level is by definition a value that changes during the
match, so the missing piece is a trait that publishes shared state and drives a per-player condition
from it. That is new code, but it is *small* new code, because the consumers are already wired.

### 2. Locking a nuke the player has already paid for is safe, and costs them nothing.

This was the question the brief flagged as deciding the shape of the mode. The answer is clean.

A purchased shot lives as an `int` on `SupportPowerChargeBank`
(`engine/OpenRA.Mods.Common/Traits/SupportPowers/SupportPowerChargeBank.cs:37`). It is only ever
`Grant`ed (`:45`) and `Consume`d (`:54`) — **nothing anywhere clears it**. `SupportPowerManager.cs`
touches the bank at only `:172, :175, :178, :204, :327, :343`.

So if a DEFCON ladder revokes the condition on a power the player has already bought: the cameo
disappears from the bin, `Activate` early-returns on `!Ready` (`SupportPowerManager.cs:293-296`),
and **the shot sits in the magazine**. Raise DEFCON again and the cameo comes back with the charge
intact. No refund logic, no lost money, no new economy code.

The two refund paths that *do* exist are about the build queue, not the magazine, and neither is in
the way:

- an item **still building** when its power stops being buildable is cancelled with a partial
  refund (`ProductionQueue.cs:379-396`, called every tick from `TickInner` at `:335-337`);
- an item **finished but not yet delivered** when the power went away is refunded **in full** and
  dropped (`SupportPowerProductionQueue.cs:190-200`).

Note the asymmetry that follows: a DEFCON step that locks a weapon **refunds a half-built purchase
but silently freezes a completed one**. That is a design decision the user should make knowingly,
not a bug — but it is the one place where the existing machinery makes a choice on its own.

### 3. Dead Hand has no owner, and that is why the change the user wants is not a small edit to it.

`DoomsdayStrike` is a **World** trait (`DoomsdayStrike.cs:21`). It fires from
`INotifyTimeLimit.NotifyTimerExpired` (`:287`) when the Doomsday Clock hits zero, and the warheads
are created with `new OwnerInit(world.WorldActor.Owner)` (`:598`) — the world-owning player. No
human player orders it, no player can stop it, and there is no order, no cameo and no cost anywhere
in the path.

Turning it into "*either player may choose to fire it*" is therefore **not a modification of this
trait**. A player-fired weapon is a support power: it needs an order name, a cameo, a target
selector, a `Permitted` gate and a place in the sidebar — none of which `DoomsdayStrike` has or is
shaped to have. The existing trait would become the *fallback* (fires if nobody chooses to), or be
switched off entirely.

What **is** reusable, and it is a lot: the target selection and clustering maths (`DoomsdayMath.cs`,
523 lines, pure and unit-tested), the two missile actors (`mods/ww3mod/rules/doomsday.yaml:50`,
`:114`), the statistics freeze (`DoomsdayStrike.cs:327-336`), the victory-check suspension
(`:281-285`, honoured at `ConquestVictoryConditions.cs:70` and `:102`), the map reveal (`:527-534`)
and the annihilation backstop (`:618-635`).

### 4. The lobby can express checkboxes and string dropdowns. Nothing else.

`LobbyOption` carries an `IReadOnlyDictionary<string, string> Values` plus a string `DefaultValue`
(`engine/OpenRA.Game/Traits/TraitsInterfaces.cs:683-717`); `LobbyBooleanOption` is that with two
fixed values (`:721-736`). There is **no integer option type, no slider and no free text**. An
integer setting is a dropdown of stringified numbers — that is exactly what the time limit does
(`TimeLimitManager.cs:88-97`).

So "players have full control of how it should work" is affordable **provided every knob is an
enumerated list**: DEFCON start level (5/4/3/2/1), escalation interval (a minutes dropdown), per-nuke
countdown reduction (a seconds or percent dropdown), whether the game-ender is granted at all. None
of that needs new widget work. A continuous knob would.

### 5. Two lobby surprises worth knowing before adding another option.

- **The `Category` string is dead.** `PowersLobbyOptions` passes `"Powers"` as the category on all
  six of its options (`PowersLobbyOptions.cs:280-372`). **Nothing reads `LobbyOption.Category`** —
  grouping in the lobby is done from two hard-coded id sets in `LobbyOptionsLogic.cs:71-95`
  (Common vs Advanced) and `:137-181` (section within Advanced).
- **Consequence:** an option whose id is not listed there falls into the unnamed "Other" section at
  the bottom of Advanced (`LobbyOptionsLogic.cs:188-190`, `:399-410`). That is where `doomsday` and
  `powers-sandbox` sit today. Any DEFCON option would land there too unless its id is added to
  `OptionSection` — a two-line change, but one that is easy to miss and invisible until someone
  opens the lobby.

### 6. There is no draw, and the shipped apocalypse already refuses to need one.

`enum WinState { Undefined, Won, Lost }` (`engine/OpenRA.Game/Player.cs:35`). There is no third
state. Everyone-loses *is* representable — mark every player failed and `CheckIfGameIsOver`
(`MissionObjectives.cs:174-183`) ends the match with all players defeated — but the shipped Dead
Hand deliberately does not do that. It freezes every player's score on the trigger tick
(`DoomsdayStrike.cs:327-336`), kills literally everything (`:618-635`), and then **picks a winner
from the frozen score** (`:651-657` → `ConquestVictoryConditions.cs:92-118`).

So the mode already has an answer to "everyone died simultaneously": *highest score before the first
warhead wins*. If the user wants mutual annihilation to be a genuine draw, that is a new outcome the
engine cannot currently express and the UI has no word for.

---

# Detailed trace

## 1. How a nuke is gated today, end to end

### 1.1 Lobby checkbox → player condition

`PowersLobbyOptions` is a World trait (`PowersLobbyOptions.cs:18`) implementing `ILobbyOptions`. It
registers six options (`:278-372`). The host's choice is stored in `LobbyInfo.GlobalSettings`, agreed
before the first tick.

`GrantConditionOnLobbyOption` — one instance per gate, on the **Player** actor — reads it:

```
optionEnabled = OptionOrDefault(Option, !GrantWhenOptionDisabled)        // :47-48
shouldGrant   = GrantWhenOptionDisabled ? !optionEnabled : optionEnabled // :50
```
(`GrantConditionOnLobbyOption.cs:45-54`)

Two things about this matter for DEFCON:

1. **The fallback when the option is not registered at all is `!GrantWhenOptionDisabled`, not the C#
   default field.** Every gate in the mod is written `GrantWhenOptionDisabled: true` so an absent
   option resolves to *off*. The in-tree comments say this repeatedly and correctly
   (`PowersLobbyOptions.cs:82-101`, `player.yaml:169-181`, `player.yaml:566-583`) — **verified
   against the code; the comments are accurate.**
2. **It runs in `Created` and never again.** `conditionToken` is assigned once and there is no revoke
   (`:52-54`). This is a one-shot gate. It cannot be the DEFCON mechanism.

The live gates:

| condition | granted by | option id | consumed by |
|---|---|---|---|
| `tacnuke-disabled` | `player.yaml:454-457` | `tactical-nuke` | `player.yaml:465` |
| `highyieldnuke-disabled` | `player.yaml:584-587` | `high-yield-nuke` | `player.yaml:592` |
| `nuke-arsenal-disabled` | `rules/ingame/nuclear-arsenal.yaml:61-64` | `nuclear-arsenal` | 9 powers in that file + `player.yaml:412` |
| `powers-sandbox-disabled` | `player.yaml:182-185` | `powers-sandbox` | 3 × `ProvidesPrerequisite` at `:186-194` |

### 1.2 Condition → "may I have this power"

`SupportPowerInstance.Permitted` (`SupportPowerManager.cs:160-164`) is the conjunction of four
things:

```
Manager.Self.Owner.WinState != WinState.Lost
&& (prereqsAvailable || DevMode.AllTech)
&& instancesEnabled
&& !oneShotFired
```

- `instancesEnabled` is recomputed **every tick** from `Instances.Any(i => !i.IsTraitDisabled)`
  (`:246`) — this is the `RequiresCondition` path, and it is dynamic.
- `prereqsAvailable` is driven by the TechTree through `PrerequisitesAvailable` / `Unavailable`
  (`:121-135`, `:236-242`) — this is the `Prerequisites: powers.america` path, and it is **also**
  dynamic: `ProvidesPrerequisite` is a `ConditionalTrait` whose `TraitEnabled` / `TraitDisabled` call
  `techTree.ActorChanged` (`ProvidesPrerequisite.cs:94-104`).

**Both routes into `Permitted` already update at runtime.** That is the single most useful fact in
this document for the proposal.

### 1.3 Purchase-time gating

Powers are bought from the **Powers** tab (`SupportPowerProductionQueue@Powers`,
`player.yaml:103-107`). Items are bodiless proxy actors, fifteen of them, `power.gbu57` …
`power.tsarbomba` (`rules/powers.yaml:198-543`), one per power.

The queue filters **both** listings on the same `Purchasable`:

```
bool Purchasable(ActorInfo unit) => instance != null && instance.Purchasable;  // :103-107
AllItems()       => base.AllItems().Where(Purchasable);        // :109-112
BuildableItems() => base.BuildableItems().Where(Purchasable);  // :114-117
```
(`SupportPowerProductionQueue.cs`)

Filtering `AllItems` too is what makes a gated power **absent** from the shop rather than greyed out.
Completion banks a shot rather than producing an actor: `instance.GrantCharge(); EndProduction(item);
return true;` (`:202-204`).

`Purchasable = bank.CanPurchase(Permitted)` = `Enabled && permitted`
(`SupportPowerChargeBank.cs:80-83`) — deliberately **independent of `Charges`**, so a power can be
restocked while one is already banked (`:76-79`).

### 1.4 Fire-time gating

Ordering a power is a normal synced order. `SupportPowerManager` is `IResolveOrder`
(`SupportPowerManager.cs:28`, `:102-107`), so every client resolves the same order on the same tick.
`SupportPowerInstance.Activate` gates on `Ready` first (`:293-296`), spends a charge (`:327`), then
calls the trait's `Activate`. `MissileStrikePower.Activate` (`:160-166`) calls `PlayLaunchSounds()`,
which plays the *incoming* warning to any non-allied local player (`SupportPower.cs:283-297`) — i.e.
**a launch is announced to the victim at order time**, before the missile exists.

(An in-tree comment cites this as `SupportPower.cs:238-251` at `player.yaml:656-657`. The claim is
right; the line numbers are stale — the method is at `:283-297`. Cosmetic, noted for accuracy.)

**Verified by reading:** all of §1. **Inferred:** nothing.

---

## 2. The current lobby surface

### 2.1 What `PowersLobbyOptions` registers

All six, with the shipped values after the `mods/ww3mod/rules/world.yaml:693-701` overrides:

| id | label | default | visible | order | locked |
|---|---|---|---|---|---|
| `airstrikes` | Airstrikes | **on** (`:30`) | **NO** (`world.yaml:694`) | 81 | no |
| `airstrike-cooldown` | Airstrike Cooldown | `4min` (`:48`) | **NO** (`world.yaml:696`) | 82 | no |
| `tactical-nuke` | Tactical Nuclear Strike (20 kt) | **off** (`:68`) | yes | **83** (`world.yaml:698`) | no |
| `high-yield-nuke` | Strategic Nuclear Strike (6 Mt) | **on** (`:111`) | yes | **84** (`world.yaml:701`) | no |
| `nuclear-arsenal` | Nuclear Arsenal | **on** (`:144`) | yes | 104 (C# default) | no |
| `powers-sandbox` | Sandbox: All Support Powers | **off** (`:185`) | yes | 105 (C# default) | no |

The airstrike cooldown dropdown offers `2min / 3min / 4min / 5min / 8min`
(`PowersLobbyOptions.cs:293-300`) but is hidden, so no player sees it today.

The two airstrike options are **hidden, not deleted** — the fields stay so the re-enable is two
`Visible` flips plus uncommenting the power blocks (`world.yaml:690-692`, `player.yaml:706-709`).

Nothing is `Locked` anywhere: every visible option can be changed by the host.

### 2.2 The sandbox sub-settings

Three fields on the same trait, all unreachable with the checkbox off because every consumer guards
on `SandboxSettingsOrNull` returning non-null (`PowersLobbyOptions.cs:269-276`):

- `SandboxRemovesPurchaseDelay = true` (`:207`) — one-tick purchase, ignores the Supply Route
  contestation throttle. Read only by `SupportPowerProductionQueue` (`:129-181`).
- `SandboxRemovesLaunchDelay = true` (`:222`) — drops `MissileDelay`, read by
  `MissileStrikePower.Activate` (`:349-352`).
- `SandboxStandoffPercent = 100` (`:247`) — identity by default; the off-map flight is untouched.

These are **not** lobby options. They are trait fields a scenario's `rules.yaml` can override.

### 2.3 The Doomsday Clock and the Doomsday checkbox

Two separate options, registered by two separate traits, and they are **not adjacent in the UI**:

- `timelimit` — `TimeLimitManager`, relabelled **"Doomsday Clock" / "Nuclear apocalypse when the
  clock runs out"**, display order 61 (`world.yaml:617-620`). Values 0/10/20/30/40/60/90 minutes
  (`TimeLimitManager.cs:32`), **default 0 = "No limit"** (`:46`, not overridden). It is in
  `CommonOptionIds` and in section "Match" (`LobbyOptionsLogic.cs:117`, `:126`), i.e. the front panel.
- `doomsday` — `DoomsdayStrike`, label "Doomsday", **default ON**, display order 62
  (`DoomsdayStrike.cs:44-63`, `:193-194`). It is **not** in `CommonOptionIds` and **not** in
  `OptionSection`, so it renders in the "Other" section at the bottom of the Advanced tab.

**So by default the salvo is enabled and the clock is off** — Dead Hand never fires unless the host
picks a time limit. Worth stating plainly in the conversation: the user may have been playing with a
clock set and not realise the shipped default is "no clock".

### 2.4 Where each powers option actually renders

`LobbyOptionsLogic` ignores `LobbyOption.Category` entirely and groups by id:

- `tactical-nuke`, `high-yield-nuke`, `nuclear-arsenal` → **Advanced ▸ Game Rules**
  (`LobbyOptionsLogic.cs:177-180`), sorted by display order among `friendly-fire` (74, placeholder)
  and `powers-enabled` (80, placeholder).
- `powers-sandbox` and `doomsday` → **Advanced ▸ Other** (unlisted, so `GetSection` returns
  `"Other"`, `:188-190`).
- `airstrikes`, `airstrike-cooldown` → filtered out by `o.IsVisible` (`:316`).

Also relevant to the "full control" ask: `powers-enabled` ("Powers Enabled", default on, order 80) is
a **placeholder** from `LobbyDummyOptions` (`LobbyDummyOptions.cs:217-219`) — stamped
`Placeholder = true` (`:37`) and **controlling nothing**. A master powers switch appears to exist and
does not.

### 2.5 What kinds of option the engine supports

Two, and only two (`TraitsInterfaces.cs:683-736`):

- `LobbyBooleanOption` — a checkbox. Values fixed to `True` / `False`.
- `LobbyOption` — a dropdown over an arbitrary `string → label` dictionary.

Integers are dropdowns of stringified integers (`TimeLimitManager.cs:88-97`;
`LobbyDummyOptions.MakePercentDropdown`, `:22-28`). There is no numeric entry, no slider, and no
multi-select. Reading is always `GlobalSettings.OptionOrDefault(id, fallback)`, returning a string.

**Verified by reading:** all of §2. **Inferred:** the exact on-screen row order within a section — I
read the sort at `:317` and the section maps, but did not render the lobby.

---

## 3. What Dead Hand does now

### 3.1 The trigger

`TimeLimitManager.Tick` fires at `ticksRemaining == 0` and notifies **world traits first, then player
traits** (`TimeLimitManager.cs:158-167`). `DoomsdayStrike` is a world trait, so it runs before
`ConquestVictoryConditions` sees the same event — which is what lets it freeze the score before
anything else reads it.

(The ordering claim is correct. Several in-tree comments cite it as `TimeLimitManager.cs:150-157` —
`DoomsdayStrike.cs:294`, `ConquestVictoryConditions.cs:96` — and `WORKSPACE/DISCOVERIES.md:12256`
cites `:142-150` and `world.yaml:529`. **Those line numbers are stale**; the loops are at `:160-165`
and the trait is at `world.yaml:617`. The substance is unaffected.)

### 3.2 The sequence

`DoomsdayStrike.NotifyTimerExpired` (`:287-312`), in order:

1. bail if already triggered, or `!enabled`, or `TestMode` without `RunInTestMode` (`:289-293`);
2. `FreezeStatistics()` — `PlayerStatistics.Freeze()` and `PlayerExperience.Freeze()` on every player
   (`:327-336`);
3. `SalvoInProgress = true` — suspends ordinary victory checks (`:281-285`);
4. `BuildSalvo()` — enumerate structures, cluster into cities, jitter, separate, schedule
   (`:341-483`);
5. `RevealMap()` — `MapLayers.Disabled = true` for every player (`:527-534`);
6. system line "DEAD HAND ACTIVATED. Incoming." (`:311`).

Then per tick (`:548-570`): launch scheduled warheads; `Annihilate()` at `annihilationTick` (kill
every actor with `HealthInfo`, `:618-635`); `Resolve()` at `resolutionTick` (clear the suspension,
re-raise `NotifyTimerExpired` on the player actors, `:651-657`).

### 3.3 Who owns the firing — the answer the brief asked for

**Nobody.** Specifically:

- the trait is on the **World** actor (`[TraitLocation(SystemActors.World)]`, `:21`);
- warheads are created with `new OwnerInit(world.WorldActor.Owner)` (`:598`) — the world-owning
  player, deliberately *not* looked up by the name "Neutral" (comment at `:596-597`);
- there is **no order, no support power, no cameo and no cost** anywhere in the path;
- it is not scripted — the one scenario that uses it
  (`tools/autotest/scenarios/demo-doomsday-deadhand/rules.yaml`) only sets `TimeLimitTicks: 250` and
  `RunInTestMode: true`; the firing is still the trait's.

**Therefore: "unlock a weapon either player may fire" is a new mechanism, not a parameter change.**
The player-facing half — order name, cameo, target cursor, sidebar entry, permission gate — has no
counterpart in `DoomsdayStrike` at all. The good news is that the *existing* fifteen support powers
are exactly that shape, so a game-ender modelled as a support power (granted rather than bought, or
bought at a price nobody reaches by accident) inherits all of it for free.

**Verified by reading:** all of §3. **Inferred:** nothing.

---

## 4. What the proposal would actually require

Element by element. "Exists" means I read the code that does it.

### 4.1 A DEFCON level as shared game state — **partly exists, needs one new trait**

**What exists.** The pattern is in the tree twice over: `TimeLimitManager` holds an int derived from
`WorldTick` (`:151-156`), and `SupplyRouteContestation` holds a live per-player bar with `ISync` and
`[Sync]` members (`engine/OpenRA.Mods.Common/Traits/SupplyRouteContestation.cs:145-239`) — including
the note that `ISync` on the *class* is load-bearing, because `Actor.cs` only hashes a trait when
`trait is ISync`, so `[Sync]` fields on a non-`ISync` class are silently inert (`:145-152`).

**Determinism is not the hard part here, and this is what I would tell the user to stop worrying
about.** A DEFCON level driven by (a) `WorldTick` and (b) support-power orders is deterministic by
construction, because both inputs are already identical on every client: `WorldTick` trivially, and
orders because `SupportPowerManager.ResolveOrder` runs on the synced order path
(`SupportPowerManager.cs:28`, `:102-107`). `DoomsdayStrike` is the existing proof — it schedules a
whole salvo including RNG draws from `World.SharedRandom` (`World.cs:50`, `:222`) and stays
byte-identical because every draw is shared and every collection is sorted by `ActorID` before use
(`DoomsdayStrike.cs:249-256` remarks, `:363-366`).

**What does not exist:** any trait that publishes a level and drives a per-player condition from it.
That is the one genuinely new piece. It is small — a world trait plus either a player-side reader or
a `ProvidesPrerequisite` toggled by it — but it is new.

### 4.2 Escalation over time — **exists in kind**

`TimeLimitManager` already does the whole thing: a tick countdown, plus warning thresholds at named
minute marks with speech and text notifications (`:170-182`, configured at `world.yaml:624-633`). A
DEFCON ladder on a wall clock is the same machinery with five thresholds instead of nine.

### 4.3 Escalation by player action — **needs a hook that does not exist, but the seam does**

There is no "a player fired a nuke" event. There is a place to put one:
`SupportPowerInstance.Activate` (`SupportPowerManager.cs:293-334`) is the single seam every order
source funnels through — human order generator, bot `QueueOrder`, and Lua alike (comment at
`:298-303`) — and it is synced. `INotifySupportPower.Activated` already fires from there
(`SupportPower.cs:279-280`) but is an **actor-level** notification, not a world-level one.

### 4.4 Weapons locked until DEFCON 1 — **exists, see §1.2**

One condition per power, already consumed by all nine arsenal powers plus the two generics. The only
work is making the condition *move*.

### 4.5 A post-first-use countdown that accelerates per nuke fired — **new, but trivial given 4.1 and 4.3**

An int on the same world trait, decremented per tick and additionally per launch event. No engine
concept is missing; it is arithmetic on state that would already exist.

**The UI is the real cost here.** There is currently **no on-screen clock at all**.
`TimeLimitManager` supports one via `CountdownLabel` / `CountdownText` (`:68-72`, wired at
`:135-148`), and **neither field is set anywhere in `mods/ww3mod/`** — I grepped the whole mod
directory for both names and got nothing. The Doomsday Clock today is a lobby dropdown plus periodic
text lines. A DEFCON indicator, an accelerating countdown and a "you may launch" prompt are all new
widget work.

### 4.6 A game-ender granted to every player at zero — **shape exists, granting does not**

The natural shape is a support power with `RequiresPurchase: False` and a condition that goes true at
zero — at which point `Permitted` becomes true, the cameo appears, and it can be fired once
(`SupportPowerInfo.OneShot` already exists and latches, `SupportPowerManager.cs:330-334`).

Two concrete gaps: nothing grants a condition from world state today (§4.1); and there is no warhead
actor for a MIRV game-ender that a *player* fires — `DeadHandStrategicMissile`
(`doomsday.yaml:114-166`) is spawned directly by the trait, not by a power. `MissileStrikePower` does
support multi-warhead salvos (`AimPoints`, `AimPointInterval`, `MissileStrikePower.cs:65-87`), and
`MissileStrikePower@Sarmat` is already a six-RV MIRV (`nuclear-arsenal.yaml:195-197`) — so a
player-fired MIRV is an existing capability, just not this actor.

### 4.7 A retaliation window where others may answer — **new, and the sharpest hidden cost**

Nothing in the engine models "an offer open to other players for N ticks". It is expressible as
another slice of the same shared state, but it is where "all players" stops being obvious.

### 4.8 "All players" in FFA and team games — **the description implicitly assumes 1v1**

What exists:

- Teams are read from `LobbyInfo.ClientWithIndex(p.ClientIndex).Team`, and the time-limit verdict
  already groups by team and sums team score (`ConquestVictoryConditions.cs:105-118`).
- **Team 0 means free-for-all**, and gets an explicit special case:
  `victoriousTeam.Key == myTeam && (myTeam != 0 || victoriousTeam.First().Player == self.Owner)`
  (`:113`).
- Alliances are **fixed at game start**: "Players, NonCombatants, and IsAlliedWith are all fixed once
  the game starts, so we can cache the result" (`:77-78`).
- `world.Players` includes non-combatants and spectators; every existing consumer filters with
  `!p.NonCombatant && p.Playable` (`:106`).

The questions the code cannot answer: does a DEFCON level exist once per world or once per team? Does
an ally's launch open *your* retaliation window? In a 3-player FFA, does the second launch shorten
the third player's window, or does each launch open its own? Every one of these changes the shape of
the state in §4.1, so they are worth settling before anything is built.

**Verified by reading:** every "exists" claim, all cited. **Inferred:** the sizing judgements
("small", "trivial") — my estimate from the code shape, not measured.

---

## 5. Open questions for the user, ranked by downstream cost

**1. Does DEFCON escalate on a clock, on player actions, or both?**
This decides whether §4.1's state is a countdown (cheap, one new trait, deterministic for free) or an
event-driven accumulator (needs the launch hook of §4.3, and needs the "what counts as escalation"
rule written down — a nuke? any strike? kills? losing a Supply Route?). It also decides whether the
mode rewards aggression or merely schedules it. **Everything else in the design hangs off this
answer.**

**2. If everyone fires their game-ender, who wins?**
The engine has no draw (`Player.cs:35`). The shipped Dead Hand already answers this — highest frozen
score wins (`DoomsdayStrike.cs:651-657`) — so "reuse that" is a zero-cost answer. "It is a draw" is a
new outcome with no engine state and no UI string behind it. "Everyone loses" is representable (all
`Lost`, `MissionObjectives.cs:174-183`) and would show every player a defeat screen.

**3. Is the game-ender bought, or granted free at DEFCON 0?**
Bought means it goes in the buy tab with a price and interacts with the economy (and with the freeze
asymmetry in page-one §2). Granted means a condition flips and the cameo appears. Granted is much
less machinery; bought is more interesting economically. The proposal's wording — "all players *get*
a game ender nuke" — reads as granted, but it is worth confirming.

**4. Is the DEFCON level global, or per team?**
See §4.8. In a 1v1 the distinction is invisible; in a 2v2 it is the difference between "your ally's
nuke escalates the world" and "your ally's nuke escalates your side". This changes the shape of the
shared state, so it is cheap to answer now and expensive to change later.

**5. What happens to a nuke the player already owns when DEFCON *rises* again — if it can rise?**
The proposal describes a one-way ratchet. If DEFCON can de-escalate, page-one §2 says the shot just
waits in the magazine — probably the desired behaviour, but it should be a decision rather than a
side effect.

**6. Does the retaliation window close, and what happens to a player already defeated when it opens?**
`Permitted` already excludes `WinState.Lost` (`SupportPowerManager.cs:161`), so a defeated player
cannot fire — meaning "all others can choose to fire theirs too" silently excludes anyone already
eliminated. That may be exactly right; it should be intentional.

**7. Do the existing nuke lobby checkboxes survive, or does DEFCON replace them?**
Today `tactical-nuke`, `high-yield-nuke` and `nuclear-arsenal` are three independent host switches
(§2.1). If DEFCON governs nuclear release, those three and the DEFCON option can contradict each
other — the host can enable the arsenal *and* set DEFCON so it is never released. `world.yaml:639`
records the mod's own view that "a mode selected by two dropdowns that must agree is a worse lobby
than one".

---

## Appendix: things found on the way

- **A dormant first pass at this exact idea already exists in the tree, commented out:**
  `LobbyPrerequisiteCheckbox@NuclearAllowed`, ID `nuclearallowed`, label **"Nuclear"**, description
  **"The conflict has escallated to nuclear warfare."**, prerequisite `global-nuclear`
  (`mods/ww3mod/rules/player.yaml:904-910`). `LobbyPrerequisiteCheckbox` is a live engine trait
  (`engine/OpenRA.Mods.Common/Traits/Player/LobbyPrerequisiteCheckbox.cs`), so uncommenting it would
  work — as a *static* host switch, not a ladder.
- **`WORKSPACE/RELEASE_V1.md:176` already lists "[v1.1] Rename tech levels to 'DEFCON'"** — an
  unrelated cosmetic item that shares the word. Worth disambiguating so the two do not get conflated.
- **`powers-enabled` in the lobby does nothing.** It is a `LobbyDummyOptions` placeholder
  (`LobbyDummyOptions.cs:217-219`, stamped `Placeholder = true` at `:37`). It reads as a master powers
  switch and is not one.
- **`DoomsdayStrike` and `TimeLimitManager` are two options that must agree**, and their defaults
  disagree in effect: the salvo defaults ON, the clock defaults to "No limit" (§2.3). Nothing warns
  the host.
- **Stale `file:line` citations in in-tree comments** (substance correct in every case):
  `TimeLimitManager.cs:150-157` → actually `:160-165` (cited at `DoomsdayStrike.cs:294` and
  `ConquestVictoryConditions.cs:96`); `SupportPower.cs:238-251` → actually `:283-297` (cited at
  `player.yaml:656-657`); `WORKSPACE/DISCOVERIES.md:12256` cites `TimeLimitManager.cs:142-150`,
  `ConquestVictoryConditions.cs:85-104` and `world.yaml:529`, now `:160-165`, `:92-118` and `:617`.
- **No `DOCS/reference/` claim was found to contradict the code.** The only Dead Hand mention there is
  `architecture.md:312-317`, about the music track of that name, and it is correct.

---

## Method, and where I could be wrong

Every `file:line` above was read in this worktree at `main @ e68498e0` and quoted from the file, not
from prior context and not from `DOCS/reference/`. I did not build, did not launch the game, and did
not run any lint or test — per the task constraints, and because nothing here required it.

**Not verified, and would need a run to settle:**

- The exact visual row order and grouping in the rendered lobby. I read the sort (`:317`) and both
  section maps; I did not open the lobby, so the table in §2.4 is a reading of the logic rather than
  a screenshot.
- That a banked charge really does survive a condition being revoked and restored **in a live
  session**. The code path is unambiguous — `SupportPowerChargeBank` has no clear path and `Permitted`
  is recomputed per tick — but I inferred the end-to-end behaviour rather than observing it, and
  page-one §2 leans on it harder than anything else here. `engine/OpenRA.Test/SupportPowerChargeBankTest.cs`
  exists; I did not read or run it. If the manager wants this pinned before the design conversation,
  the cheapest proof is a unit test, not a game launch:
  `dotnet test engine/OpenRA.Test/OpenRA.Test.csproj --configuration Release --filter SupportPowerChargeBank`
  — pass criterion: existing tests green, and if a new case is wanted, that `Grant(1)` followed by any
  number of `IconVisible(false)` calls still leaves `Charges == 1`.
- The sizing language in §4 ("small", "trivial") is judgement from code shape, not a measured
  estimate, and is the claim in this document I would trust least.
