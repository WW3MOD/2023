# Crash: `ClientInSlot` — "Sequence contains more than one matching element"

Date: 2026-08-16 · Repo state: `main @ 81e5a440` (matches `Build: 81e5a44046/…` in the syncreport) · **Read-only diagnosis, no fix applied.**

Crash log: `WORKSPACE/audit/logs-260816-snapshot/Logs/exception-2026-08-16T163234Z.log`

## 1. The violated invariant

`Session.ClientInSlot` (`engine/OpenRA.Game/Network/Session.cs:92-95`) uses `SingleOrDefault`, so it asserts **at most one `Session.Client` in `Session.Clients` may carry a given `Slot` key**. Nothing in `Session` enforces this — it is an invariant maintained *by convention* by whoever mutates `Clients`.

Normally the mutator is the **server**: `LobbyCommands.cs:438,473,633,637` and `Server.cs:1136,1243,1250` remove the previous occupant before/when seating a client. Those are the only `Clients.Remove*` calls in the tree (verified by grep across `engine/`). The client-side `Session` is a deserialized mirror of the server's, so it inherits the server's correctness.

`MapStartingLocations.Created` (`engine/OpenRA.Mods.Common/Traits/World/MapStartingLocations.cs:136-138`) iterates every slot and calls `ClientInSlot(kv.Key)` during World-actor construction, so a duplicated slot key is a **hard throw during world creation** — before `CreateMapPlayers` runs (`World.cs:249` → `:262`), which matches the stack exactly.

## 2. How the Session reaches that state

**The mutator here is not the server.** `SetupShellmapBots` (`engine/OpenRA.Game/Game.cs:564-629`, added by `f2a0979c5`, 2026-04-05) writes directly into the live client-side `OrderManager.LobbyInfo`:

- `Game.cs:603` — `lobbyInfo.Slots[slotKey] = new Session.Slot{…}` — a dictionary **assignment**, idempotent.
- `Game.cs:614` — `lobbyInfo.Clients.Add(new Session.Client{… Slot = slotKey …})` — a list **append**, *not* idempotent.

It never clears `Clients` first. So **any second `SetupShellmapBots` run against the same `Session` appends a duplicate client for every slot key the two maps share** → `ClientInSlot` throws on the next world creation.

There are two `LoadShellMap` overloads and only one of them resets:

| | resets session? |
|---|---|
| `Game.cs:537` `LoadShellMap(string uid)` | **yes** — `Disconnect(); JoinLocal();` at `:548`, with a comment saying stale slots were already causing a `KeyNotFoundException` |
| `Game.cs:525` `LoadShellMap()` (in the crash stack) | **no** — goes straight to `SetupShellmapBots` at `:528` |

`Disconnect()` (`Game.cs:1106`) → `JoinLocal()` (`Game.cs:111`) builds a **fresh** `Session` holding one spectator client with `Slot == null`, which never matches a slot key. That is why the `(uid)` overload is safe and the parameterless one is not.

### The log header pins which call site it was — and kills the briefed timeline hypothesis

`ExceptionHandler.cs:34-38` writes the `on map …` line from **`Game.OrderManager.World`**. In `StartGame` (`Game.cs:186`) `OrderManager.World` is only reassigned *after* the `World` constructor returns, so the reported map is the **world that was already live**, not the one being loaded.

The header names **River Zeta WW3**, i.e. `Game.OrderManager.World != null` at crash time. After `Disconnect(); JoinLocal();` the current OrderManager has `World == null` and the header line would be **absent**.

**Therefore the crashing `LoadShellMap()` was not preceded by a `Disconnect()`.** That rules out, individually:

- `IngameMenuLogic.cs:280-282` and `:302-304` — both "exit to menu" paths `Disconnect()` on the line before. **Ruled out.**
- `UnitOrders.cs:177-178` (unavailable map on `StartGame`) — `Disconnect()` first. **Ruled out.**
- `GameSaveLoadingLogic.cs:28-30` — `Disconnect()` first. **Ruled out.**
- `BlankLoadScreen.cs:66,101` — startup, runs once against the `JoinLocal()` at `Game.cs:514`. First injection, no duplicate. **Ruled out.**

So the **briefed hypothesis — that leaving the desynced multiplayer game carried two humans' slots into the shell map — is contradicted by the evidence.** Every in-game exit path disconnects first, and the header proves this one did not. The 16:25 desync is almost certainly *incidental*: it explains why the user was sitting on the main menu clicking things at 16:32, not what threw.

Also ruled out on the same reasoning: a disconnect/reconnect leaking a slot, host migration, an observer taking a play slot, and MP slot state surviving into the shell map — all of those require the crashing session to be the network `Session`, and a network `Session` is server-authored and server-pruned (`Server.cs:1243`). The duplicate here is client-side and self-inflicted.

### What is left: a second injection on the main menu

At the main menu the current `OrderManager` **is** the shellmap OM, owning a live River Zeta world with 6 injected bots (`debug.log:13`, `SetupShellmapBots: Injected 6 bots for map 'River Zeta WW3'`). Two call sites can re-enter `LoadShellMap()` from there without a reset:

1. **`MainMenuLogic.cs:205`** (added `2da6cb30a`, 2026-03-27) — the shellmap **Random toggle**: `Game.RunAfterTick(Game.LoadShellMap)`, the parameterless overload. Second injection → duplicates on every shared `MultiN` key → throw. *Reachability caveat:* line `:196` only reaches `:205` when `IsRandomMode()` is false (`:102` — `!Game.Settings.Game.ShellmapUseOrder`), and `ShellmapUseOrder` defaults to `false` (`Settings.cs:328`). So the user must previously have alt-clicked a map into ordered mode. **Verified by code reading; not observed in a log.**
2. **`Game.cs:540-543`** — the `LoadShellMap(uid)` **fallback**: if `uid` is not in `GetAvailableShellmaps()`, it calls `LoadShellMap()` *before* reaching the `Disconnect()` at `:548`. The Replay button (`MainMenuLogic.cs:295`) passes `world.Map.Uid`, which is normally available, so I could not confirm a trigger. **Hypothesis, unverified.**

The `RunAfterTick` in both matches `ActionQueue.PerformActions` in the stack.

### Evidence gap — state honestly

The crashing session's own `debug.log` **did not survive**: the surviving `debug.log` is the *post-crash* relaunch (it continues past River Zeta into a successful `Nuclear Winter WW3` load at `:169`, so it did not crash), and `debug.log.1` is from an unrelated `worktrees/ww3mod/medic-autonomy` run. All snapshot files share mtime `2026-08-16T18:40:04Z` (copy time). **The duplicate-append mechanism is verified from code and from the exception header; the exact button the user pressed is not.**

## 3. WW3MOD's or upstream's?

**WW3MOD's.** `git blame` against the vendoring squash `7362fbc6b` (2023-03-20):

- `Session.cs:92-95` `ClientInSlot` — `7362fbc6b`, **pristine upstream**.
- `MapStartingLocations.cs:134-142` — `7362fbc6b`, **pristine upstream**.
- `Game.cs:564` `SetupShellmapBots` — **`f2a0979c5` (2026-04-05), WW3MOD**.
- `Game.cs:537` `LoadShellMap(uid)` — **`dc9d82d64` (2026-03-26), WW3MOD**.
- `MainMenuLogic.cs:205` — **`2da6cb30a` (2026-03-27), WW3MOD**.

Upstream never injects bot clients into a client-side `Session` and never reloads a shell map into a live one, so upstream cannot reach this state. **Not related to the elimination-cascade work at `f49b6aca`** — that touches outcome resolution, not `Session.Clients`. The two upstream files are victims, not causes.

## 4. Severity for a public release

**Medium-high. Reachable by an ordinary player with no multiplayer and no desync**, purely from the main menu — the whole mechanism lives in the shellmap selector, which is WW3MOD's own front-page UI.

Mitigating: it is a **hard crash to desktop, but not persistent**. The corrupt `Session` is in-process only — nothing is written to disk (`SetupShellmapBots` mutates `OrderManager.LobbyInfo`; only `PromoteShellmap`/`SetRandomMode` call `Settings.Save()`, and they persist map order, not clients). **Relaunching gives a clean session; the install is not wedged.** Confirmed by the surviving `debug.log`, which is a healthy relaunch.

Aggravating: it is on the **main menu of a pre-alpha public build** — the first screen a new player touches — and it kills the process with no dialog.

Because the desync is not a precondition, this should **not** be triaged as downstream of the multiplayer bug. It stands alone.

## 5. Proposed fix shape (not a patch)

**Fix where the duplicate is created, not at the call site.** `SingleOrDefault → FirstOrDefault` in `Session.cs:94` would convert a hard crash into a silently half-corrupt lobby — two clients claiming one spawn point, with `MapStartingLocations` and `CreateMapPlayers` disagreeing about who owns it. That trades a loud bug for a quiet one, in shared upstream code, for the benefit of one WW3MOD-only code path. **Recommend against.**

Preferred, in order:

1. **`SetupShellmapBots` should be idempotent** (`Game.cs:592-626`): clear prior shellmap-injected clients before appending — e.g. drop clients whose `Slot` is being re-seated, or all `Bot != null` clients with `BotControllerClientIndex == LocalClientId`. This makes every entry path safe regardless of who calls it, and is the smallest change.
2. **Make the two `LoadShellMap` overloads agree** (`Game.cs:525` vs `:537`): give the parameterless one the same `Disconnect(); JoinLocal();` the `(uid)` one already has, and route the `:542` fallback through the reset. Two overloads with different session-lifecycle contracts is the underlying design bug — `:546`'s comment shows this class of bug was already hit once and patched on one branch only.

Do **both**: (2) fixes the known paths, (1) is the invariant guard that stops the next call site from reintroducing it. Tradeoff: (2) alone leaves `SetupShellmapBots` a loaded gun; (1) alone leaves the two overloads inconsistent for anything else that depends on a fresh session.

Optionally, a debug-only assertion that `Clients` has no duplicate non-null `Slot` after any local mutation would have caught this at the point of corruption rather than one world-load later.

## NEEDS A RUN (not executed — user is playing)

Repro attempt for candidate 1, no autotest required:

1. Launch the game, land on the main menu (shellmap loads, bots injected — confirm `SetupShellmapBots: Injected N bots` in `debug.log`).
2. Alt+click a map in the shellmap dropdown to set **ordered** mode (`ShellmapUseOrder = true`).
3. Click the **Random** toggle button.
4. Expect: crash with this exact stack. Confirm `debug.log` shows a **second** `SetupShellmapBots: Injected` line with no intervening mod/session reset.

Negative control: repeat with the **prev/next** buttons instead (`MainMenuLogic.cs:155`, the `(uid)` overload) — expect no crash, since that path resets.
