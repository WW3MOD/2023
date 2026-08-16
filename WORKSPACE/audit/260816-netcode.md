# Netcode audit — can two strangers find each other and play a match?

Audited **read-only** on 2026-08-16 against `main @ 8b4ae9cd`.
(The brief stated `0dd133e7`; main advanced two commits — `1d89d1b0` + merge `8b4ae9cd`,
the crash-sweep merge — during the read-in phase. Nothing in that merge touches netcode.)

No game was launched, no server hosted, no test run. Two **read-only HTTP GETs** were made to
`master.openra.net` (`/versioncheck`, `/games`) — the same endpoints the game itself calls on
every launch. **Nothing was POSTed to `/ping`**, so no server was advertised.

Predictions were registered before verification in
[`260816-netcode-predictions.md`](260816-netcode-predictions.md). **Three were wrong** (P1, and the
version-notice expectation behind P3); they are reported below as findings.

Ranked by **how many strangers each finding stops**, not by technical severity.

---

## N1 — BLOCKER. The server browser works perfectly, and it will be empty.

**This is the finding that matters. It is not a code defect.**

Everything on the discovery path is functional. What is missing is anyone to discover.

Verified live against `master.openra.net` at audit time:

| | |
|---|---|
| Games listed right now | **334** |
| Distinct mods | **13** (`ra` 166, `d2k` 46, `ca` 41, `cnc` 39, `cameo` 9, `rv` 8, `hv` 8, `sp` 5, `e2140` 5, `ts` 3, `swp` 2, `rab` 1, `lethalstrike` 1) |
| WW3MOD games | **0** |

The master does **not** filter by mod — it returns every game to every client. Filtering is
client-side at `ServerListLogic.cs:871`:

```
if (!game.IsCompatible && !filters.HasFlag(MPGameFilters.Incompatible)) return true;
```

`IsCompatible` requires `ExternalMod.MakeKey(Mod, Version)` to be a registered external mod
(`GameServer.cs:175-177`), and the default `MPGameFilters` (`Settings.cs:301`) is
`Waiting | Empty | Protected | Started` — **`Incompatible` is not in it**. So all 334 other-mod
games are hidden, correctly.

Net result for a stranger opening Multiplayer: a blank list reading
**"No games found. Try changing filters."** (`common.ftl:667`) — advice that leads only to 334
games they cannot join.

**Two strangers can meet today only by coordinating out-of-band and using Direct Connect**
(`MultiplayerLogic.cs:37-46`, which does work), with the host having solved NAT. That is exactly
the audience the release framing says we do not have.

### The decision whose premise expired

PIPELINE item 53's dedicated-server bullet was **declined by the user "for now"** — under the
friends-and-testers reading, which the user **replaced on 2026-08-16** with public-release-to-
strangers. The decision was correct under the old premise and is the single highest-leverage
open item under the new one. It should be re-put to the user rather than treated as settled.

### And item 53's other open question is now answered by observation

Item 53 parks "is it a courtesy problem for a total conversion to list on upstream's master?"
in `AWAITING-USER.md`. **The live listing answers it: community total conversions are already
there in numbers** — Combined Arms (`ca`) is running 41 concurrent games, and `lethalstrike`
is listed with a single game. Listing is normal, accepted, needs no permission and no
registration (there is no credential anywhere in the advertise payload, `GameServer.cs:234-260`).
No `WebServices` override is needed to be *listed*; see N7 for why one may still be wanted.

`engine/OpenRA.Server/Program.cs` already builds headless, and `ConnectionTarget.cs:46` already
takes a hostname, so joiners of an official server need no inbound port at all.

---

## N2 — Sync reports ARE armed by default. **My prediction was wrong, and the brief's worry is unfounded.**

**P1 predicted `EnableSyncReports` defaults to `false`. It does not.**

`Settings.cs:97` — `public bool EnableSyncReports = true`, a **deliberate WW3MOD divergence**
from the upstream default of `false`, carrying a 10-line justification and an explicit
`// PITFALL: do not "restore" this to the upstream default.`

The host's value reaches every client exactly as the brief describes (`Server.cs:349` →
lobby globals → `OrderManager.cs:118`), so the host trap is a real mechanism — but the default
is safe, and a second gate at `OrderManager.cs:116-119` requires `humanClients > 1`, which two
strangers satisfy. `OrderManager.cs:124-126` additionally logs whether reporting was armed, so
a missing report is diagnosable before the next desync rather than after it.

**A stranger hosting a 2-human game produces real sync reports on both machines.**

### The one live exposure, and it is a trap set for the N1 fix

`launch-dedicated.sh:63` and `launch-dedicated.cmd:16` (plus the `engine/` copies) hard-default
`EnableSyncReports=False`.

So **the moment an official dedicated server ships to fix N1, sync reporting switches off for
every game played on it** — reintroducing precisely the host trap item 42 names, through the
fix for the top finding. N1 and N2 must be handled together: whoever stands up the server must
set `Server.EnableSyncReports=True` explicitly.

---

## N3 — BLOCKER (experience). A desync ends the match with no dialog and no explanation.

Given a live unresolved 2-human desync, this is the experience strangers will actually have.

`OrderManager.OutOfSync` (`:85-95`) → `World.OutOfSync()` → `EndGame()`, which latches
`IsGameOver`. `SetPauseState` then early-returns forever (`World.cs:453-456`), so the world is
**permanently unresumable**. Selection, order generation and chat all switch off.

The entire user-facing output is one system chat line, in the chat panel that was just disabled:

```
notification-desync-compare-logs = Out of sync in frame { $frame }.
```

`common.ftl:868`. **Inherited** — restored from upstream at `c966311b` (2026-03-26, "Upstream
merge: restore all chrome YAML and fluent files from upstream"). Note that the current upstream
text **no longer even names `syncreport.log`**; earlier notes in this repo quote the older
string that did. So the player now gets a frame number and nothing else: no dialog, no file
path, no instruction, no defeat screen.

Cheap and worth doing independently of the desync fix: a dialog that names the file, its path,
and tells both players the match cannot continue.

---

## N4 — A dropped player is never defeated, so the match cannot end.

`World.OnClientDisconnected` (`:610-624`) fires `INotifyPlayerDisconnected`. **Nothing in the
repo implements that interface** — only the declaration (`TraitsInterfaces.cs:712`) and the two
dispatch sites (`World.cs:256`, `Player.cs:240`).

> **Trap, recorded so nobody else trips on it:**
> `WORKSPACE/audit/logs-260816-snapshot/Logs/traitreport.log:149` reads
> `INotifyPlayerDisconnected: 5`, which looks like five implementors. That file counts
> **queries, not implementors** — the same file opens with `IAirborneVisibility: 723287`.
> Five queries = one world + four players. The interface has zero implementors.

No rejoin exists: `Server.cs:487-494` rejects any join once `State == GameStarted`, and
`DropClient` removes the client from `LobbyInfo` entirely (`:1243`), so the slot is gone.
Everything in `plans/260812_multiplayer_continuity.md` is documented and unimplemented (item 55).

So the dropped player's army freezes in place, keeps auto-defending, and the player is never
marked `Lost`. To end the match the survivor must destroy the abandoned `SUPPLYROUTE`:

- **75,000 HP** (`rules/ingame/structures.yaml:262-263`)
- `MustBeDestroyed: RequiredForShortGame: true` (`:232-233`)
- `Targetable: TargetTypes: NoAutoTarget` (`:265-266`) — **units will not engage it on their own**; it must be manually force-attacked

### Correction to a claim raised during this audit

It was suggested that `Armor: Type: Indestructable` (`:270-271`) might make the Supply Route
literally unkillable, which would make the match unwinnable. **It does not.** That armor name
appears exactly once in all of `mods/ww3mod/rules/` — the declaration itself — and per the
`DamageWarhead.DamageVersus` rule already established in PIPELINE item 40, **an unlisted armor
class matches nothing and takes the unmodified 100%**. Omission is the opposite of a zero. The
structure is fully damageable; the problem is 75k HP plus `NoAutoTarget`, not invulnerability.

---

## N5 — Ten seconds of unexplained freeze before the game says anything.

Lockstep: `OrderManager.TryTick` advances only when every client has a packet queued
(`:219`, `:338-366`). One slow peer stops the world for everyone.

Adaptive latency exists but is nearly inert: `OrderBuffer.cs:23-27` clamps its per-client tick
scale to **1.0–1.1**, so it can slow the *fast* client by 10% and cannot absorb a real stall.

There is **no in-game "waiting for players" overlay** (`label-waiting-for-players` exists only in
the server *browser*, `ServerListLogic.cs:87,177`). Timeline a stranger experiences:

| Elapsed | What happens |
|---|---|
| 0–10 s | Screen frozen. **No message of any kind.** |
| 10 s | Chat: "X is experiencing connection problems" (`PlayerPinger.cs:32,69-72`) |
| every 20 s | Chat: "X will be dropped in N seconds" (`:33,82-98`) |
| 60 s | Client dropped (`PlayerPinger.cs:34`) |

All inherited. The first ten seconds are the expensive part — that is where a stranger
alt-F4s and assumes the game crashed.

---

## N6 — WW3MOD advertises wearing stock Red Alert's face.

`GameServer.cs:212-232` builds the advertise payload from the manifest. Literal values sent:

| Field | Value | Source |
|---|---|---|
| `Mod` | `ww3mod` | `mod.config:9` |
| `Version` | `release-20230225` | `mod.yaml:3` |
| `ModTitle` | `WW3MOD` | `mod.yaml:2` |
| `ModWebsite` | `https://www.openra.net` | `mod.yaml:5` |
| `ModIcon32` | `https://www.openra.net/images/icons/ra_32x32.png` | `mod.yaml:7` |

Both URL fields are already `TODO(release)`-flagged in `mod.yaml`. In-game this is survivable —
games group under a header reading `WW3MOD (release-20230225)` (`GameServer.cs:138`), so they
are distinguishable from `Red Alert (release-20230225)` by title despite the identical version
string. On openra.net's **web** listing, a WW3MOD game would show the stock RA icon and link to
openra.net. WW3MOD-authored, and cheap.

Worth noting for context: upstream's current release is **`release-20250330`** (visible in the
live listing). WW3MOD advertises a two-year-old engine tag as its mod version.

---

## N7 — PREDICTION WRONG, and it is good news: the "unrecognized version" notice does not fire.

I expected this to be the top blocker, and it is not.

`WebServices.cs:26` points `VersionCheck` at `https://master.openra.net/versioncheck` with no
mod override, and `Debug.CheckVersion` defaults `true` (`Settings.cs:156`). A `"unknown"` reply
sets `ModVersionStatus.Unknown`, which unconditionally shows a notice bar in the **server
browser** (`ServerListLogic.cs:200-201`) reading:

> **You are running an unrecognized version of OpenRA. Download the latest version from www.openra.net**
> (`chrome.ftl:241`, not overridden in `mods/ww3mod/languages/en.ftl`)

That would have been a first-session blocker: an OpenRA identity string telling the player their
install is wrong, on the exact screen where they are trying to find a game.

**It does not fire.** Live query, with a control:

```
GET /versioncheck?protocol=1&engine=release-20230225&mod=ww3mod&version=release-20230225
  -> HTTP 200, empty body
GET /versioncheck?protocol=1&engine=release-20230225&mod=ra&version=release-20230225
  -> HTTP 200, "outdated"
```

`WebServices.cs:50-56` initialises `status = ModVersionStatus.Latest` and only moves it on the
literals `outdated` / `unknown` / `playtest`. An empty body matches none, so **status stays
`Latest`** and the notice stays hidden. The control proves the endpoint is live and the query
shape is right.

**But this is load-bearing behaviour we do not own.** WW3MOD reads as "up to date" purely
because upstream's master returns an empty string for unknown mods. If upstream ever changes
that default to `unknown`, **every WW3MOD player sees the notice with no change on our side.**
Pinning `WebServices.VersionCheck` in `mod.yaml` (a MiniYaml block, not code) removes the
dependency for a few minutes' work. Recommend doing it regardless of N1.

---

## N8 — Lobby and post-match are in good shape.

Audited and found working; no blocker. **P7 and P8 both correct.**

- **Force-start works.** `Server.cs:348` — `EnableSingleplayer = settings.EnableSingleplayer || Type != ServerType.Dedicated`. Any in-game-hosted lobby is non-dedicated, so this is **always true** and the `false` default at `Settings.cs:81` never bites a normal host. It restricts dedicated servers only — worth knowing before N1's server is stood up.
- **Spectators** on by default (`Session.cs:213`); **kick** and vote-kick both work; temp-bans are in-memory only and lost on host restart.
- **Replays record client-side by default** and are not gated by any setting (`Game.cs:66-70`; both call sites take the default). `ServerSettings.RecordReplays = false` is a *separate* dedicated-server-only recorder. Files land in `Replays/ww3mod/release-20230225/`, browsable via Extras → Replays.
- **End-of-match stats screen opens automatically** (`LoadIngamePlayerOrObserverUILogic.cs:62-82` → `GAME_INFO_PANEL`): per-player faction, score and APM.
- Confirmed: replaying a desynced game produces **no** sync report (`OrderManager.cs:117` excludes `ReplayConnection`), so a desync must be diagnosed on the live client. The `.orarep` files hold the order streams only.
- **Host NAT guidance is genuinely good** (P6 correct): the create-server dialog gives distinct UPnP-enabled / not-supported / disabled / LAN-only notices covering firewall, port forwarding and where to toggle UPnP (`ServerCreationLogic.cs:142-200`, strings at `chrome.ftl:379-392`), colour-coded by status. `AdvertiseOnline` defaults `true` (`Settings.cs:54`).
- **Host quitting is handled cleanly**, not as a hang: the server is in-process, `DropClient` shuts it down when the admin leaves a non-dedicated server (`Server.cs:1279-1280`), and the guest gets a "Connection Failed" dialog (`DisconnectWatcherLogic.cs:23-42`). **P5 correct.**

---

## N9 — Fresh regression, landed today: faction tooltips will render a literal `\n`.

`75ac6941` (2026-08-16, "Write the faction descriptions and fix the Random Side string") closed
audit item R8 by writing real descriptions into `mods/ww3mod/rules/world.yaml:241,245,254`:

```
Description: America\nNATO's lead power. Precision airpower, networked armour and air cavalry: ...
```

That `\n` style is correct for `Buildable.Description`, because `ProductionTooltipLogic.cs:191`
unescapes it. **The faction dropdown does not.** The engine unescapes `\\n` at exactly six sites
— mission briefings (`MissionBrowserLogic.cs:306`, `GameInfoBriefingLogic.cs:32`,
`LobbyCommands.cs:1444`), main-menu news (`MainMenuLogic.cs:697`), production tooltips
(`ProductionTooltipLogic.cs:191`) and mod content (`ModContentLogic.cs:51`) — and the faction
picker is not among them.

`LobbyUtils.cs:235-238` passes the raw string to `SplitOnFirstToken(description)`, which searches
for a **real** newline (`:206-215`). It finds none, so `first` = the entire blob and `second` =
`null`. The tooltip title becomes one long line containing a visible `\n`, and the description
body is empty.

WW3MOD-authored, hours old, and it is in the faction picker every single-player and multiplayer
match passes through. **Fix is one `.Replace("\\n", "\n")` at `LobbyUtils.cs:235`** — matching
what the other six sites already do. Not fixed here; this audit is read-only.

---

## N10 — Dead Fluent keys confirmed, and the block is larger than item 53 records.

Item 53 reports "~38 dead Fluent keys" at `mods/ww3mod/languages/en.ftl:84–129`. Verified still
present, and the same defect extends at least to **`:619-620`** (`search-status-failed`,
`search-status-no-games` — the engine looks up `label-search-status-*`, `ServerListLogic.cs:30,33`).
The whole block uses pre-`notification-` / pre-`label-` names the engine no longer resolves.

Inert — the engine strings win — but ~40 lines that look like they are localising the multiplayer
experience and are not. Renaming them blind changes ~40 lobby strings at once, which is why item
53 deferred it; that reasoning still holds.

---

## Attribution summary

| Finding | Origin |
|---|---|
| N1 empty browser | Inherited plumbing; **WW3MOD decision** (no dedicated server) |
| N2 sync reports on | **WW3MOD-authored** (`Settings.cs:97`, deliberate divergence) |
| N2 dedicated-script trap | Inherited defaults, **live for WW3MOD's own scripts** |
| N3 desync UX | **Inherited** (`c966311b`, upstream merge) |
| N4 no continuity | **Inherited**; documented-not-built by WW3MOD (item 55) |
| N5 stall UX | **Inherited** |
| N6 RA icon / openra.net URL | **WW3MOD-authored** (`mod.yaml:5,7`, TODO-flagged) |
| N7 version notice | **Inherited**, currently benign by upstream accident |
| N8 lobby / replays | **Inherited**, plus WW3MOD force-start confirm |
| N9 faction `\n` | **WW3MOD-authored, `75ac6941`, 2026-08-16** |
| N10 dead keys | **WW3MOD-authored** |

---

## What I could not verify, and where I may be wrong

1. **Nothing here was observed running.** Every finding is static reading plus two HTTP GETs. No
   game was launched, per the operating rules.
2. **N9 is the claim most worth a cheap check.** I traced the absence of an unescape and the
   `SplitOnFirstToken` behaviour, but I did not see the tooltip. **One screenshot of the lobby
   faction dropdown settles it** and would take one launch. If I am wrong, the likely reason is an
   unescape somewhere in the widget/font layer that my grep for `Replace("\\n"` did not match.
3. **N7 depends on a third party's undocumented behaviour.** I measured what
   `master.openra.net` returns *today*. I have no visibility into upstream's policy, whether it is
   stable, or whether they would object to WW3MOD advertising. The 41 concurrent Combined Arms
   games are strong evidence of accepted practice, not a guarantee.
4. **N5's first ten seconds are inferred.** The simulation stops; whether the *renderer* keeps
   drawing (so the UI still responds and it merely looks frozen) versus the window going
   unresponsive is not determinable statically. This changes how bad it feels but not that it is
   unexplained.
5. **N4's 75,000 HP is not calibrated.** I did not compare it against typical structure HP or
   against a player's realistic damage output, so "a long grind" is a judgement, not a measurement.
   The `NoAutoTarget` half is solid.
6. **I did not audit LAN discovery** (`ServerListLogic.cs:485-521` merges LAN beacons). Two
   strangers are by definition not on a LAN, so it is out of scope for this question — but it is a
   genuine untested path.
7. **The `IsCompatible` / `ExternalMods` dependency is a real fragility I downgraded.** A client
   launched without `Engine.LaunchPath` never registers its own mod and would see *zero* games
   including WW3MOD's. I verified all shipped launchers pass it (`packaging/macos/launcher.m:189`,
   `packaging/linux/openra.in`, `openra.appimage.in`, `launch-game.sh:60`, `launch-game.cmd:23`),
   so it hits developers rather than strangers. **I did not find the Windows installer's launch
   path** — if it does not pass `Engine.LaunchPath`, this becomes a blocker on the largest platform.
   That is the single check I would most want someone to close.

---

## Two runs worth scheduling, precisely stated

Neither was performed. The manager holds the grant.

1. **One lobby screenshot, no match required.** Launch to the multiplayer lobby, open the faction
   dropdown, hover America. **Proves or kills N9** (literal `\n` in the tooltip). Also confirms the
   seven placeholder lobby options are suppressed in the Advanced tab. Cost: one launch, no match.
2. **A 2-human game with the user hosting** — already item 42(iv)'s standing confirming test. This
   audit adds one thing to check while it runs: `debug.log` should contain
   `Sync reports enabled (setting True, human clients 2, replay False)` from
   `OrderManager.cs:124`. **If that line reads `disabled`, N2 is wrong** and the whole desync
   investigation is running blind. It costs nothing to grep for and would falsify this report's
   second finding.
