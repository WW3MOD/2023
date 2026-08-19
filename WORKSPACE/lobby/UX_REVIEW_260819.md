# Lobby UX review — ground truth, comparison, and ranked proposals

**Worked against:** `main` @ `66fd33d3` (worktree `wt/lobby-ux`, branch created from that SHA).
**Status:** research only. Nothing implemented. No game was launched — every claim below is
**read from source**, and where I am inferring runtime behaviour rather than reading it I say so
explicitly and give the falsifier.

**Scope note.** This started as "fix the three named irritations" and was widened mid-review to
"re-examine the whole lobby, including what the redesign already changed." Section 5 is the
result of that widening and is the part I would read first — the biggest finding in this document
is not one of the three irritations.

---

## 0. Headline

Two of the user's three irritations are **already solved in code** and are discoverability or
semantics problems, not missing features. The third is a **real design flaw**. And separately from
all three, the review turned up **one defect that would embarrass the project in front of a
stranger within about fifteen seconds** (§5.1) and **one dead end a player can walk into by
clicking two prominent buttons in sequence** (§3.3).

| # | Irritation | Verdict |
|---|---|---|
| 1 | "Add and remove bots easily" | **Already solved as an affordance.** One click adds, one click fills all, one click clears all. The real defect is that the bot you get is chosen at **random** from two developer-named builds. This is a *semantics* problem. |
| 2 | "Spectators should show differently" | **Real design flaw.** Confirmed: spectators render as visually identical rows in the same list, distinguished only by a regular-weight word. |
| 3 | "Switching to and from spectating should be easier" | **Half real.** Going *to* spectator is one click. Coming *back* has no button at all, and can become **impossible** for a non-host. |

---

## 1. Ground truth — what the lobby is today

The lobby chrome is the engine's *common* chrome, modified in-repo:
`mods/ww3mod/mod.yaml` loads `common|chrome/lobby*.yaml`, not a `ww3mod|` copy. So all layout
citations below are `engine/mods/common/chrome/`, and all logic is
`engine/OpenRA.Mods.Common/Widgets/Logic/Lobby/`.

### 1.1 Layout

Two tabs: **Match** and **Advanced** (plus **Music**). The Match tab is a 2×2 quadrant grid:

| | Left | Right |
|---|---|---|
| **Top** | Map preview + inline map browser | Player roster (`CELL_LABEL: PLAYERS`, `lobby-players.yaml:47-54`) |
| **Bottom** | Common options + Active Changes chips + Preset bar | Chat |

Bottom of the roster cell carries two anchored strips:
- `LOBBY_SETUP_ROW` (`lobby-players.yaml:740-778`) — **Add Bots · Remove Bots · Auto-Team · Replay last**
- `SPECTATE_AREA` (`lobby-players.yaml:714-735`) — **Allow Spectators? · Spectate**

A stats strip counts bots and spectators (`lobby.yaml:424,431`; text built at `LobbyLogic.cs:738-744`).

### 1.2 Player rows

Roster rows are built in `LobbyLogic.UpdatePlayerList` (`LobbyLogic.cs:1090-1236`). Three templates:

- **Empty slot** → `SetupEmptySlotButtons` (`LobbyUtils.cs:528-587`). Host sees an inline strip
  **`[ Play ] [ + Add bot ] [ X ]`** (`lobby-players.yaml:506-525`; the `X` glyph is set in code at
  `LobbyUtils.cs:561`, overriding the YAML `Text:` — noted as a PITFALL there). Non-host sees a wide
  **"Play in this slot"** button (`LobbyUtils.cs:579-581`, string at `chrome.ftl:225`).
- **Editable row** — used for *your own* row and for *bots when you are host*
  (`LobbyLogic.cs:1113-1114`). Carries name, colour, faction, **team**, spawn, ready.
- **Non-editable row** — remote players. Team shows as a read-only label
  (`lobby-players.yaml:441-443`); its dropdown is parked off-screen at `X:-200`
  (`lobby-players.yaml:448-452`). Handicap is parked off-screen in *both* templates
  (`lobby-players.yaml:283-285`, `453-460`) — dropped by design.

> **Stale doc found.** `WORKSPACE/BACKLOG.md:15` claims "a non-host can never set their own team,
> so can't enable Team chat in MP." That is **no longer true**. Your own row uses the *editable*
> template regardless of host status (`LobbyLogic.cs:1113`), and `SetupEditableTeamWidget` runs on it
> (`LobbyLogic.cs:1128`); the team dropdown is on-screen at `X:256` (`lobby-players.yaml:273-275`).
> The team column was restored by `95329170`/`d046df19`. **Recommend deleting that backlog line.**

### 1.3 Bots — what exists

| Action | Cost today | Where |
|---|---|---|
| Add one bot to a slot | **1 click** (`+ Add bot`) | `LobbyUtils.cs:552-554` |
| Add a *specific* bot type | 2 clicks via the slot dropdown | `LobbyUtils.cs:59-98` |
| Fill every empty slot | **1 click** (`Add Bots`) | `LobbySetupRowLogic.cs:45,71-95` |
| Remove every bot | **1 click** (`Remove Bots`) | `LobbySetupRowLogic.cs:48,97-105` |
| Remove one bot | 2 clicks (slot dropdown → Open) | `LobbyUtils.cs:69` |

This is **better than upstream OpenRA**, which has no per-row add button and buries bulk actions in a
"Slot Admin" menu. The affordance is not the problem.

**The problem is which bot you get.** Both one-click paths choose the type at random:

- `LobbySetupRowLogic.cs:92` — `var bot = botTypes.Random(Game.CosmeticRandom);`
- `LobbyUtils.cs:598` — `var botType = botTypes[Game.CosmeticRandom.Next(botTypes.Length)];`

And the pool is exactly two, both named as build artifacts (`mods/ww3mod/rules/ai/ai.yaml:44-45,49-50`):

```
ModularBot@experimental:  Name: Experimental AI
ModularBot@stable:        Name: Stable AI 0802
```

So a first-time player clicks `+ Add bot` four times and gets four opponents, each secretly one of
two different AIs, named after a development branch and a datestamp. **There is no difficulty
concept anywhere in the lobby.** This is already logged as a release blocker
(`WORKSPACE/PIPELINE.md:132`, item R4).

### 1.4 Spectators — what exists

Spectators are clients with `Slot == null`. They are appended into **the same `players` ScrollPanel**
as the player rows, in the same loop-index sequence (`LobbyLogic.cs:1178-1229`).

Confirmed absences — I grepped `lobby-players.yaml` for a spectator header, divider, or count label
and **found none**:

- no section header between the last player and the first spectator
- no divider rule, no indent, no background tint, no reduced row height
- spectator rows are the *same* 36px full width as player rows, and the YAML comment says so on
  purpose: *"Same dimensions as TEMPLATE_EDITABLE_SPECTATOR so the column rhythm is consistent"*
  (`lobby-players.yaml:625-631`)

The only distinguishing mark is a `Label@SPECTATOR` reading "Spectator"
(`chrome.ftl:226`) at `X:405`, and the redesign **deliberately de-emphasised it** to regular weight
(`lobby-players.yaml:600-609`): *"spectator label de-emphasised — it's a status indicator, not an
action."* Defensible in isolation; combined with identical geometry it removed the last strong signal.

**The user's complaint here is precisely correct and I can reproduce it from the source.**

### 1.5 Spectator switching — the asymmetry

**Going to spectator — easy.** A `Spectate` button, always visible while you hold a slot
(`lobby-players.yaml:728-734`, wired `LobbyLogic.cs:313-314`), issuing `spectate`.

**Coming back — no button exists.** `SPECTATE_AREA.IsVisible` requires `Slot != null`
(`LobbyLogic.cs:321`), so the moment you become a spectator **the entire strip disappears** —
including the `Allow Spectators?` checkbox, which a spectating host also loses.

To return you must scroll the roster, find an empty slot row, and click `Play` / `Play in this slot`.
That button is well-labelled. It is simply *elsewhere*, and it is **conditional on an empty slot
existing**.

The host cannot help by pulling you back either: `ShowPlayerActionDropDown` offers Kick, Transfer
Admin, and **"Move to Spectator"** (`LobbyUtils.cs:134-140`) — there is **no reverse action**.

---

## 2. How other games solve this

Researched via web sources; each claim marked CONFIRMED where a primary source was found.

### Bots

- **OpenRA upstream** — per-slot: dropdown → Bots → type (2–3 clicks). Bulk: Slot Admin → Configure
  Bots → Add/Remove. **Add inserts a random bot type** — and this is the *standing complaint*, open
  since Dec 2020 as [issue #18914](https://github.com/OpenRA/OpenRA/issues/18914): hosts must
  "manually fix each ai player's type." The requested fix is a **configurable default bot type**,
  framed as "reducing unnecessary clicks." CONFIRMED.
  → **WW3MOD inherited upstream's single most-complained-about lobby defect, verbatim.**
- **Warcraft III** — the slot dropdown reads **Computer (Easy / Normal / Insane)**. Bot *and*
  difficulty are **one selection, not two**. CONFIRMED.
- **Beyond All Reason** — dedicated add-bot button; difficulty via a **gear icon on the bot row**.
  Changing a bot's type is destructive (kick, re-add). Autohost fills unspecified params from a
  saved `localBots` **preset**. CONFIRMED.
- **AoE2 DE** — slot row → dropdown → Open / Closed / AI. Two clicks. CONFIRMED.
- No game found ships a button literally named "Fill with bots" — WW3MOD's `Add Bots` is ahead here.

### Spectator separation

- **Beyond All Reason** — spectators live in a **separate list object** (`MainList2`), rendered in a
  **smaller font, collapsed**. Exactly the "collapsed tray" pattern. (CONFIRMED for the *in-game*
  player list; I could **not** confirm BAR's lobby layout.) An open issue
  ([#5142](https://github.com/beyond-all-reason/Beyond-All-Reason/issues/5142)) proposes a labelled
  **"Team Spectators"** group alongside Team 1 / Team 2.
- **AoE2 DE** — spectator slots existed in HD and were **deliberately removed** in DE in favour of a
  spectate feature *outside* lobby setup, because in-lobby spectator slots clogged the lobby.
  CONFIRMED — the strongest evidence that mixing spectators into the slot list is a known mistake.
- **Dota 2** — Radiant / Dire / Unassigned divisions; real spectators sit **entirely outside the ten
  slots** as broadcaster and coach channels. CONFIRMED.
- **Warcraft III** — observers **consume player slots**; a 12-slot map can host none. The anti-pattern.

### Spectator switching

- **OpenRA** — [issue #15864](https://github.com/OpenRA/OpenRA/issues/15864) asks for a server option
  to **default joiners into spectator**, citing "spectator trains" where many spectators join one by
  one and each must manually switch. CONFIRMED.
- **BAR** — [issue #3945](https://github.com/beyond-all-reason/Beyond-All-Reason/issues/3945) asks for
  a **"join as spectator?" confirmation**, because players get loaded in as spectators unintentionally.
  Intent should be captured at join time. CONFIRMED.
- **Dota 2** — you **click a slot** to take it; host **drags** players between teams; there is a
  **Swap Teams** button. CONFIRMED.

### Worth stealing, unprompted

- **Difficulty folded into the menu entry** (WC3) — halves the interaction for the common case.
- **A saved default bot** (BAR `localBots`, OpenRA #18914) — makes the *next* click predictable.
- **Don't let bulk buttons touch spectators** — Dota 2 has a documented bug where "Randomize All
  Players" swaps spectators into player slots. A constraint on any future balance/shuffle button.
- **`spectators-label` already exists** in this repo — `mods/ww3mod/languages/en.ftl:644-649` renders
  "No Spectators / One Spectator / N Spectators". A ready-made tray header, already translated, unused
  in the lobby roster.

---

## 3. The three irritations — judgement

### 3.1 Bots — **not a design flaw; a semantics flaw**

The buttons the user is asking for **already exist and are one click each** (§1.3). If the user has
not noticed them, that is worth knowing on its own — but I suspect what they actually felt is:
*"I click Add Bots and I don't understand what I got."* They are right not to understand. The answer
is genuinely random, and the two possible answers are called "Experimental AI" and "Stable AI 0802".

**Do not add more bot buttons.** Fix what the existing button *means*.

### 3.2 Spectators — **a real design flaw**

Confirmed from source (§1.4). Same list, same row size, no header, no divider, and the one textual
cue was deliberately toned down. Worth fixing properly.

### 3.3 Switching — **real, and worse than reported**

The asymmetry is real (§1.5). But there is a specific trap that I think is the single best find in
this review:

> **The dead end.** Click **`Add Bots`** — every empty slot is now a bot. Then click **`Spectate`**.
> You are now a spectator, the Spectate strip has vanished, and **there is no empty slot to return
> to**. `slot_open` is admin-gated (`LobbyCommands.cs:463`, `ValidateSlotCommand(..., true)`), so a
> non-host **cannot free a slot themselves**, and the host has no "move to player" action to rescue
> them with (`LobbyUtils.cs:134-140`). The only escapes are the host clicking `Remove Bots`, or the
> spectator leaving and rejoining the server.
>
> Both buttons involved are large, adjacent, and on the main screen. This is reachable by accident in
> two clicks.

*Inference flag:* I have not launched the game to walk this path — it is derived from the visibility
predicates and the server-side admin gate cited above. The falsifier is simple: if some other widget
offers a non-host route back into a slot, the trap is not real. I grepped for one and did not find it.

---

## 4. Ranked proposals

**Ordered legibility-first, per the user's ruling.** Tier 1 changes only what the player *sees* — no
lobby state is written and no order is sent. Tier 2 changes what the lobby *does*. Everything in
Tier 1 can ship without touching a synchronised value.

**Sync classes:** *Chrome* = layout/display only, zero network effect. *Data* = a YAML string edit,
no code path changes. *Existing order* = sends an order the lobby already sends today with a
different payload — no protocol change, but it writes synchronised lobby state and **resets every
client's ready flag**. *New state* = new synchronised data. **Nothing here is in the last category.**

### Tier 1 — Legibility (ship these first)

| # | Proposal | Player sees | Sync class | Cost |
|---|---|---|---|---|
| **L1** | **Spectators in their own tray** | Spectators leave the player list for a labelled, shorter-row tray | **Chrome** | **S–M** |
| **L2** | **Fix or hide the Advanced tab** | A top-level tab stops opening onto nothing (§5.1) | **Chrome** | **XS–S** |
| **L3** | **Rename the bots** | "Stable AI 0802" stops appearing in a shipping opponent picker | **Data** | **XS** |
| **L4** | **Disable placeholder options** | Dead settings stop being clickable (pre-emptive; §5.1) | **Chrome** | **XS** |
| **L5** | **1366×768 overflow** | Buttons stop overlapping at a very common laptop width | **Chrome** | **S** |
| **L6** | **Faction tooltip `\n`** | Tooltips stop showing a literal `\n`, if confirmed | **Chrome** | **XS** |

### Tier 2 — Behaviour (needs more care; all still low-risk)

| # | Proposal | Player sees | Sync class | Cost |
|---|---|---|---|---|
| **B1** | **Stop the random bot pick** | `+ Add bot` gives a predictable, chosen opponent | Existing order (`slot_bot`) | **S** |
| **B2** | **"Join as player" while spectating** | A way back that stays visible | Existing order (`slot`) | **S** |
| **B3** | **Host can move a spectator into a slot** | Reverse of the existing "Move to Spectator" | Existing order (`slot`) | **S** |
| **B4** | **Close the Add-Bots dead end** | The trap in §3.3 stops being reachable | Existing order | **S** |

> **On the bot item being split.** L3 and B1 were one proposal until the legibility ruling, and
> splitting them is genuinely useful rather than bookkeeping: **renaming is a pure data edit that
> touches no code path**, while making the pick deterministic changes which bot the lobby actually
> instantiates. L3 alone removes the embarrassment; B1 removes the confusion. **L3 is worth shipping
> on its own even if B1 is never done.**

### L1 — Spectators in their own tray — *the visible win*

Move the spectator loop (`LobbyLogic.cs:1178-1229`) out of the `players` ScrollPanel into a distinct
container below it, headed with the **already-existing, already-translated** count string
(`en.ftl:644-649`): *"2 Spectators"*. Give the tray shorter rows and dimmer text (BAR's `MainList2`
pattern), and drop the now-redundant per-row "Spectator" word — the group heading carries it.

Pure chrome and display logic. Spectators are *already* a distinct concept in the session model
(`Slot == null`), already counted separately in the stats strip (`LobbyLogic.cs:742-744`), and already
addressable as a group by the bulk-kick feature. **Nothing synchronised changes.** This is the
lowest-risk item with the largest visible effect.

Three layout options are mocked up side by side — see §7.

### L2 — Fix or hide the Advanced tab

See §5.1. **Decision needed from the user before anything is built**, and one screenshot should
confirm the diagnosis first (§9).

### L3 — Rename the bots

`Experimental AI` / `Stable AI 0802` → player-facing names, in
`mods/ww3mod/rules/ai/ai.yaml:44-45,49-50`. A datestamped branch name in a shipping opponent picker is
the kind of detail a stranger screenshots. **Pure data edit — no code path, no order, no behaviour
change.** The bots themselves are untouched, so the `@stable` benchmark is unaffected.

*Watch:* `WORKSPACE/bugs/discovered.md:1229-1235` records ~38 orphan lobby Fluent keys where edits are
silent no-ops. Confirm the bot `Name:` field renders directly rather than via a Fluent key before
assuming a one-line edit is enough.

### L4 — Disable placeholder options *(pre-emptive, one line)*

Add `|| option.Placeholder` to the disable predicate at `LobbyOptionsLogic.cs:444`. Placeholders are
currently dimmed by colour only and remain clickable, firing a real network order for a setting that
does nothing (§5.1). Unreachable today; live the moment one real option joins those sections. Worth
doing regardless of what happens to the Advanced tab.

### L5 / L6 — Overflow and tooltips

Both are on record and both are cheap; see §6.3 and §6.4. L6 needs a screenshot to confirm before
it is worth touching.

### B1 — Stop the random bot pick

Replace `Random(...)` at `LobbySetupRowLogic.cs:92` and `LobbyUtils.cs:598` with an explicit choice.
Shape, cheapest first:
1. Deterministic default — always the same bot unless chosen otherwise (exactly the fix OpenRA
   #18914 has been asking for since 2020).
2. `+ Add bot` becomes a small dropdown listing the bots by name, WC3-style (**recommended** — one
   extra click only when you want the other one, and it makes the roster legible).
3. A remembered "last bot used", persisted like the existing lobby presets.

*Scope honesty:* a genuine **difficulty ladder** (Easy/Normal/Hard) is **not** a lobby task. It needs
new `ModularBot` definitions and tuned AI behaviour, and it interacts with the `@stable` benchmark
policy in `CLAUDE.md`. **B1 deliberately does not propose one** — only that the two bots that already
exist be deliberately chosen rather than rolled for. The ladder is a separate, much larger question.

### B2 — A way back from spectating

Relax `SPECTATE_AREA.IsVisible` (`LobbyLogic.cs:321`) so the strip persists while spectating, and swap
the button: `Spectate` when you hold a slot, **`Join as player`** when you do not. It issues
`slot <key>` for the first open slot — the same order the `Play in this slot` button already sends
(`LobbyUtils.cs:581`). Disable with a clear reason when no slot is free.

Fixes the "Allow Spectators? vanishes for a spectating host" bug in the same change.

### B3 — Host can move a spectator into a slot

Add the mirror of "Move to Spectator" to `ShowPlayerActionDropDown` (`LobbyUtils.cs:117-142`), issuing
`slot <key>` on the spectator's behalf into the first open slot. Existing order; host-gated exactly as
the current entry is.

### B4 — Close the dead end

Cheapest correct fix: make `Add Bots` **leave one slot open** when the clicking client is not
themselves in a slot; or have `Join as player` (B2) open a bot slot for a **host** spectator. For a
*non-host* spectator the only real fix is B3. **Recommend B2 + B3 together** — they close it from both
ends and B4 then needs no separate work.

---

## 5. The redesign, re-examined

Per the widened mandate, treating the merged `feature/lobby-redesign` as a draft. Roughly **35 lobby
commits** across five waves are in `main` — far more than the three "Step" commits.

### 5.1 The Advanced tab appears to open onto nothing — **the embarrassment finding**

This is the one I would fix first for a public release.

The chain, all read from source:

1. `LobbyDummyOptions.cs` defines **34 lobby options** — "Weapon Range", "Damage Scale", "Snipers",
   "MANPADS", "Friendly Fire" and so on, each with a polished description.
2. Every one of them is stamped **`opt.Placeholder = true`** (`LobbyDummyOptions.cs:36-38`). They do
   nothing whatsoever.
3. An option is "Common" **iff** its id is in a 12-item allow-list; **everything else is "Advanced"**
   (`LobbyOptionsLogic.cs:180-183`). All 12 Common ids are real options.
4. `RenderAdvancedSections` **skips any section that is entirely placeholders**
   (`LobbyOptionsLogic.cs:379-380`) — a deliberate and correct call: *"a section consisting entirely
   of placeholder options is just visual noise."*
5. All three Advanced sections — Unit Availability, Combat Tuning, Game Rules — draw **only** from
   that all-placeholder pool.
6. The one real option that could land in Advanced is the **Scenario** dropdown, and it
   `yield break`s when the map defines no scenarios (`ScenarioLobbyDropdown.cs:27-29`) — which is the
   normal case for a conquest map.

**Therefore, on a standard multiplayer map, the ADVANCED tab renders with no options in it.** A
top-level tab, given equal billing beside Match, that opens onto an empty panel.

> **Inference flag — read this before acting.** This is a static read of the render path; I did not
> launch the game. The falsifier is precise: **if any non-placeholder `LobbyOption` exists whose id is
> not in `CommonOptionIds` and not in `HiddenOptionIds`, Advanced is not empty.** I enumerated the
> `ILobbyOptions` implementers and checked ww3mod's `world.yaml` — `PowersLobbyOptions` is commented
> out (`world.yaml:554`), and `shortgame/crates/creeps/buildradius/allybuild/techlevel` are explicitly
> hidden (`LobbyOptionsLogic.cs:88-92`). I believe the conclusion holds, but **one screenshot settles
> it and should be taken before any work starts.**

If confirmed, the options are: hide the tab when it would be empty (smallest); promote a few real
options back into it so it has a reason to exist; or remove it for v1 and restore it when the
features ship. **This is a user decision, not mine.**

*Related latent hazard, not currently live:* placeholder options are dimmed by **colour only** —
`checkbox.IsDisabled` does **not** include `option.Placeholder` (`LobbyOptionsLogic.cs:439-446`). A
placeholder is fully clickable and its `OnClick` issues a real `option <id> <state>` network order,
syncing to all clients and resetting everyone's ready flag, for a setting that does nothing. Today
this is unreachable because the sections are hidden. It becomes live the instant anyone adds one real
option to one of those sections. **Recommend adding `|| option.Placeholder` to the disable predicate
regardless of what happens to the tab** — a one-line pre-emptive fix.

### 5.2 Common options on the Match panel — **keep**

Sound call, and it is what makes the Advanced tab empty rather than making the lobby worse. The 12
promoted options are the ones players actually change. Grouped Economy / Match / World with headers
suppressed in the flat panel (`LobbyOptionsLogic.cs:113-136, 350-357`). No objection.

### 5.3 Active Changes chips — **keep, with one caveat**

Chips summarise every option differing from default, with `+`/`-`/`!` glyphs carrying the colour
(`LobbyActiveChangesLogic.cs:37-56`), an "All settings at default" empty state
(`lobby-players.yaml:1030`), and a `+N more` overflow cap. A stranger understands this — it reads as
a diff of the host's settings, and the empty state teaches it on first sight. Genuinely good, and I
would not touch it.

*Caveat:* the amber warning set includes **`friendly-fire`** (`LobbyActiveChangesLogic.cs:36-42`),
which is a **placeholder** (`LobbyDummyOptions.cs`). If that option ever becomes reachable before it
becomes functional, the chip strip will warn about a rule change that does not exist. Tie this to the
§5.1 fix.

### 5.4 Preset save/load — **the weakest of the three; needs a stranger's eye**

`PRESET [Default ▾] [Save As] [Reset]` on the Match panel (`lobby-players.yaml:1055-1076`), persisted
to `$SupportDir/lobby-presets.yaml`.

Reasons for doubt, in order:
- **It occupies permanent prime real estate for a returning-host feature.** A first-time player has
  no presets and cannot want any; they see a control whose value is zero until their second session.
- **It is the most netcode-adjacent thing in the lobby.** Applying a preset issues a *loop* of real
  orders — `option`, `slot_open`, `slot_bot`, `faction`, `team` (`LobbyPresetLogic.cs:323,348,371,375,405,407`).
  One click fans out N broadcasts and N ready-resets. It works, but it is the highest-blast-radius
  button on the screen and it looks like a save dialog.
- **`Replay last` in the setup row is the same feature in one click** (`LobbySetupRowLogic.cs:56-57`)
  and is the case people actually want.

I am **not** proposing removal — it is shipped, it works, and the user may value it. I am flagging it
as the control most likely to be the "doesn't make sense to me" the user could not name, and
suggesting it is the best candidate to demote (collapse into a small ⋯ menu) if the Match panel needs
room for the spectator tray. **Worth asking the user directly.**

---

## 6. Other things a stranger would notice

Not raised by the user; found during the review.

1. **§5.1 — the empty Advanced tab.** Highest severity.
2. **Bot names** — "Stable AI 0802" in a shipping opponent picker (§1.3). Already `PIPELINE.md:132` R4.
3. **1366×768 overflow** — the setup-row buttons overflow into `SPECTATE_AREA` by ~80px at that width.
   Documented and accepted at `lobby-players.yaml:736-739` and `WORKSPACE/BACKLOG.md:17` ("fix only if
   a player reports it"). 1366×768 is still one of the most common laptop resolutions; for a public
   release I would raise this above "only if reported". **Overlapping buttons are a first-impression
   defect.** L1 helps incidentally by freeing vertical space.
4. **Faction tooltips may render a literal `\n`** — on record at `WORKSPACE/bugs/discovered.md:197-213`;
   `LobbyUtils.cs:235-238` passes the description through `SplitOnFirstToken` with no unescape. Static
   trace only; **one screenshot settles it.** Faction tooltips are on the path of every new player.
5. **~38 orphan lobby/server Fluent keys** in `mods/ww3mod/languages/en.ftl`
   (`WORKSPACE/bugs/discovered.md:1229-1235`) — edits there are silent no-ops. A trap for whoever does
   the copy pass; worth knowing *before* L3's renaming work, since renaming is a copy edit.
6. **Dead chrome** — `SKIRMISH_TABS`/`MULTIPLAYER_TABS` (~100 lines, force-hidden, **duplicate child
   IDs**) and an unreachable Servers panel (`WORKSPACE/BACKLOG.md:13`). Invisible to players; a
   correctness hazard for anyone editing lobby YAML, since duplicate IDs make `Get<T>` ambiguous.

---

## 7. Mockup

`WORKSPACE/lobby-ux-mockup.html` shows the current layout beside **three** spectator-tray designs
(L1), plus the L3/B1 bot menu and the B2 return button in context. Presented for a pick, not a review.

---

## 8. What I did not verify

- **No game was launched** — launches are serialized through the manager, so I took none. Every
  behavioural claim in this document is a source read. The three that most deserve confirmation are
  written up as runnable requests in §9.
- **I did not confirm the 1366×768 overflow visually** — I am repeating the in-repo comment and
  backlog entry, both of which state it as measured.
- **BAR's lobby layout** could not be confirmed from text sources; the `MainList2` pattern I cite is
  from BAR's *in-game* player list. The pattern is sound; the provenance is narrower than I would like.
- **I did not audit the Music tab or the chat panel** beyond noting their positions.
- **`WORKSPACE/BACKLOG.md:15` is stale** (§1.2) and I have not removed it — that is an edit, and this
  branch is research-only.

---

## 9. MANAGER: please run this

Three checks I could not make myself. All three are **static observations of a lobby screen** — none
needs a match played, and all three can be answered from **a single skirmish lobby on a normal
conquest map**. None of my Tier 1 proposals should be built before check A returns.

### A. Is the Advanced tab empty? *(blocks L2 — highest value)*

- **Setup:** open a skirmish lobby on any standard conquest map (one with **no scenarios** defined —
  that condition is what makes this bite).
- **Do:** click the **Advanced** tab.
- **Capture:** one screenshot of the Advanced panel.
- **The answer is:** whether any option rows render at all.
  - **No rows** → §5.1 confirmed; L2 becomes a release-blocking fix and the user picks
    hide / repopulate / drop.
  - **Some rows** → my inference is wrong. Please note **which** options appear, because that tells me
    exactly which non-placeholder option escaped the 12-item Common allow-list, and I will correct §5.1.
- **Bonus, same screenshot:** if any row is **greyed out**, try clicking it. If it toggles, L4 is live
  today rather than latent and moves up the ranking.

### B. Do faction tooltips show a literal `\n`? *(blocks L6)*

- **Setup:** same lobby, **Match** tab.
- **Do:** hover the **faction dropdown** on any player row, then open it and hover an entry.
- **Capture:** one screenshot with the tooltip visible.
- **The answer is:** whether the description contains a visible `\n` instead of a line break.
  On record at `WORKSPACE/bugs/discovered.md:197-213`; I only traced it statically.

### C. The spectate dead end *(confirms §3.3 — lowest priority of the three)*

- **Setup:** same lobby.
- **Do:** click **Add Bots** (fills every empty slot), then click **Spectate**.
- **Capture:** one screenshot of the players panel afterwards.
- **The answer is:** whether any control offers a route back into a player slot. I expect the
  Spectate strip to have vanished entirely and every slot to be occupied.
- **Note:** in *skirmish* you are always host, so you can always click `Remove Bots` to escape. The
  genuinely trapped case is a **non-host in multiplayer**, which is harder to stage — if that is
  expensive, the skirmish screenshot plus the admin gate at `LobbyCommands.cs:463` is enough
  corroboration and I would not spend a multiplayer setup on it.

**If only one can be run, run A.**
