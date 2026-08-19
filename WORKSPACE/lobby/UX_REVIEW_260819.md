# Lobby UX review — ground truth, comparison, and ranked proposals

**Worked against:** `main` @ `66fd33d3` (worktree `wt/lobby-ux`, branch created from that SHA).
**Status:** research only. Nothing implemented. No game was launched — every claim below is
**read from source**, and where I am inferring runtime behaviour rather than reading it I say so
explicitly and give the falsifier.

**Scope note.** This started as "fix the three named irritations" and was widened mid-review to
"re-examine the whole lobby, including what the redesign already changed." The single most actionable
finding is **§6.1 / §L1 — every faction tooltip renders mangled** — and it is not one of the three
irritations.

**Revision history.** Rev 1 (`a9afd4c6`) led with a claim that the Advanced tab renders empty. The
manager's check A **refuted it**; rev 2 retracts that in §5.1, corrects §1.1, and re-ranks. Where rev 1
was wrong the text is struck rather than deleted, so the error and its cause stay legible.

---

## 0. Headline

Two of the user's three irritations are **already solved in code** and are discoverability or
semantics problems, not missing features. The third is a **real design flaw**. Separately, the review
turned up **every faction tooltip in the lobby rendering as one mangled line with a visible `\n`**
(§6.1) and **one dead end a player can walk into by clicking two prominent buttons in sequence**
(§3.3).

> **Retraction, 2026-08-19.** An earlier version of this document led with the claim that the
> **Advanced tab opens onto nothing**. That was **wrong** — there is no Advanced tab at all, and the
> always-visible options panel renders 12 real options. The manager's check A refuted it. **§5.1 is
> rewritten as a retraction** with the reasoning error named; the residual finding there is a scope
> question, not a defect, and is re-ranked down accordingly. Nothing else in this document depended
> on it.

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

**Corrected 2026-08-19 after check A** — an earlier draft of this section described "Match / Advanced
/ Music" tabs. That was wrong; there is no such tab row. The screen is a single 2×2 quadrant grid with
**all four quadrants visible at once**, and the only tab-like controls are local toggles inside two of
them:

| | Left | Right |
|---|---|---|
| **Top** | Map preview — toggles `MAP` / `CHANGE MAP` (inline browser) | Player roster (`CELL_LABEL: PLAYERS`, `lobby-players.yaml:47-54`) |
| **Bottom** | Options panel (always visible) + Active Changes chips + Preset bar | Chat — toggles `CHAT` / `MUSIC` |

The options panel renders **12 real options** with real values (Income Modifier, Starting Cash,
Passive Income, Kill Bounties, Game Speed, Doomsday Clock, Starting Units, Explored Map, Fog of War,
Separate Team Spawns, Sync, Debug Menu). There is exactly one `CATEGORY_FILTER` in the lobby chrome —
`lobby-players.yaml:854-856`, text **`All`** — so that one panel is the whole options surface. See
§5.1 for why I originally got this wrong.

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
| **L1** | **Faction tooltips unescape `\n`** | Faction tooltips stop being one mangled line; descriptions reappear | **Chrome** | **XS** |
| **L2** | **Spectators in their own tray** | Spectators leave the player list for a labelled, shorter-row tray | **Chrome** | **S–M** |
| **L3** | **Rename the bots** | "Stable AI 0802" stops appearing in a shipping opponent picker | **Data** | **XS** |
| **L4** | **Disable placeholder options** | Dead settings stop being clickable (pre-emptive; §5.1) | **Chrome** | **XS** |
| **L5** | **1366×768 overflow** | Buttons stop overlapping at a very common laptop width | **Chrome** | **S** |
| — | ~~Fix or hide the Advanced tab~~ | **Withdrawn** — there is no Advanced tab (§5.1). What remains is a scope question below, not a proposal. | — | — |

**Not a proposal — a question for the user.** §5.1 leaves one real item: **22 of the 34 designed
lobby options were never implemented and are silently hidden.** The hiding works correctly and the
lobby looks finished, so there is nothing to fix. The only question is **whether those 22 features
are still wanted** — a roadmap call, not UX work.

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

### L1 — Faction tooltips unescape `\n` — *confirmed statically; smallest fix in this document*

Promoted to the top after check A removed the Advanced-tab finding. **This one is confirmed by a
complete read of the chain**, and it is worse than the version on record at
`WORKSPACE/bugs/discovered.md:197-213`.

The chain, every link read:

1. `mods/ww3mod/rules/world.yaml` stores descriptions with `\n` as an intended title/body delimiter —
   e.g. `Description: America\nNATO's lead power. Precision airpower, networked armour…`
2. **MiniYaml does not unescape `\n`.** The proof is that four separate engine sites do it by hand:
   `LobbyCommands.cs:1444`, `MainMenuLogic.cs:713`, `GameInfoBriefingLogic.cs:32`,
   `ProductionTooltipLogic.cs:191` — all `.Replace("\\n", "\n")`.
3. The faction path does **not** do that replace (`LobbyUtils.cs:235-238`, `701`).
4. `SplitOnFirstToken(input, token = "\n")` (`LobbyUtils.cs:206-215`) splits on a **real newline**
   (0x0A). Against a literal backslash-`n`, `IndexOf` returns **-1**, so `split > 0` is false →
   `first = the entire input`, `second = null`.

**Result:** the tooltip *title* is the whole string including a visible `\n`, and the *description* is
empty. Every faction, every time the dropdown is opened.

**The sharp part:** commit `75ac6941` (2026-08-16, three days ago) set out to fix exactly this — its
message says the descriptions "carried a header and an empty body… so every faction showed a blank
description." It wrote real bodies. But the bodies are being swallowed into the title, because the
split never fires. **The symptom it fixed is still present, for a reason one layer below the one it
addressed.**

**Fix:** add `.Replace("\\n", "\n")` before the split, matching the four existing precedents. One
line.

*Falsifier, stated because I have not seen it rendered:* this is wrong if `FluentProvider.GetMessage`
unescapes, or if MiniYaml unescapes for this field specifically. Both are unlikely given that four
call sites do the replace manually — but a single screenshot of an open faction dropdown settles it,
and §9 explains why that screenshot is currently out of reach.

### L2 — Spectators in their own tray — *the visible win*

Move the spectator loop (`LobbyLogic.cs:1178-1229`) out of the `players` ScrollPanel into a distinct
container below it, headed with the **already-existing, already-translated** count string
(`en.ftl:644-649`): *"2 Spectators"*. Give the tray shorter rows and dimmer text (BAR's `MainList2`
pattern), and drop the now-redundant per-row "Spectator" word — the group heading carries it.

Pure chrome and display logic. Spectators are *already* a distinct concept in the session model
(`Slot == null`), already counted separately in the stats strip (`LobbyLogic.cs:742-744`), and already
addressable as a group by the bulk-kick feature. **Nothing synchronised changes.** This is the
lowest-risk item with the largest visible effect.

Three layout options are mocked up side by side — see §7.

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
doing regardless of the roadmap call on the 22 hidden options.

### L5 — 1366×768 overflow

On record at `lobby-players.yaml:736-739` and `WORKSPACE/BACKLOG.md:17`; see §6.2 item 2. L2 helps
incidentally by freeing vertical space.

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

### 5.1 ~~The Advanced tab opens onto nothing~~ — **RETRACTED. I was wrong.**

> **Correction, 2026-08-19.** The manager ran check A
> (`./tools/autotest/screenshot-lobby.sh advanced-tab --tab=advanced`, map `river-zeta-ww3`, at
> `66fd33d3`; screenshot `manual_lobby_260819_190518/001_advanced-tab.png`). **My conclusion was
> wrong, and the premise under it was wrong too.** The falsifier I wrote did its job. This section is
> rewritten; the original claim is struck rather than deleted so the error stays legible.

**What I claimed:** that a top-level **Advanced** tab renders with no options in it, and that this was
the single most embarrassing thing a stranger would hit.

**What is actually there:** **there is no Advanced tab at all.** The lobby's only top-level tabs are
`MAP` / `CHANGE MAP` and `CHAT` / `MUSIC`. The options panel is **always visible**, sits under the map
preview, and renders **12 real options with real values** — Income Modifier 100%, Starting Cash
$20000, Passive Income $100, Kill Bounties Off, Game Speed Real Time, Doomsday Clock No limit,
Starting Units None, Explored Map, Fog of War, Separate Team Spawns, Sync, Debug Menu — plus the
preset row and the "All settings at default" hint. **Nothing opens onto nothing. The lobby reads as
complete and coherent.**

**Where my reasoning broke.** I verified the *render* logic and never verified that anything
*invokes* it with the Advanced category. There is exactly **one** `CATEGORY_FILTER` in the whole lobby
chrome — `lobby-players.yaml:854-856`, whose text is **`All`**. No widget anywhere instantiates an
Advanced-filtered panel. `grep -ri advanced mods/ww3mod/chrome/` returns **nothing**. The only
"advanced" string in the lobby path is `LobbyLogic.cs:634`:

```csharp
case "advanced": case "options": pendingTestTab = PanelType.Options; break;
```

— and that sits inside `if (TestMode.IsActive && ...)`. **"Advanced" is a test-driver alias for the
Options panel, not a tab.** I read the `CategoryAdvanced` vocabulary in `LobbyOptionsLogic.cs` and the
"Options → Advanced" phrasing in old redesign commit messages, and inferred a user-facing tab from
both. Neither is evidence about what the chrome instantiates. **Commit messages describe an intent at
a point in time; later waves superseded them. I should have grepped the chrome for the tab before
building a headline finding on it.**

**What survives, and it is much smaller.** The *mechanism* I traced is correct — 12 real options
render, and the remaining **22 of the 34 designed options are placeholders whose sections are
suppressed wholesale** (`LobbyOptionsLogic.cs:379-380`). Restated honestly:

> **22 of 34 designed lobby options were never implemented, and are silently hidden.**

That is a **scope and roadmap question, not a visible defect**. A player sees a coherent 12-option
panel and has no way to know the other 22 were ever designed. The suppression is working exactly as
intended and is the reason the lobby looks finished. **The only open question is whether those 22
features are still wanted** — that is the user's call, not a bug. `L2` is re-ranked accordingly in §4:
**not a release blocker, not a defect.**

**A correction to my own check-A instructions, which makes the refutation stronger.** I specified "a
map with **no** scenarios defined — that condition is what makes this bite." That was inverted:
`river-zeta-ww3` **has 2 scenarios** and is the **only** one of the nine maps that defines any (I
re-verified: it is the sole `scenarios.yaml` under `mods/ww3mod/maps/`). So the check ran on the one
map most likely to *populate* an options panel, and the result still refuted me — which is a stronger
disconfirmation than the condition I asked for. Worth recording separately: **the Scenario dropdown is
a real, populated lobby control** ("No Scenario" + "CONQUEST | 2 SCENARIOS") on that map, so my §1
aside that it "yields nothing on the normal case" is true for 8 of 9 maps but should not be read as
"it is never seen."

**Still valid, and independent of all of the above:** placeholder options are dimmed by **colour
only** — `checkbox.IsDisabled` does not test `option.Placeholder` (`LobbyOptionsLogic.cs:439-446`).
A placeholder is fully clickable and its `OnClick` fires a real `option <id> <state>` network order
that resets every client's ready flag, for a setting that does nothing. Unreachable today *because*
the suppression hides them — so this is latent, not live, and it becomes live the instant one real
option joins those sections. **L4 stands as a one-line pre-emptive fix.**
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

### 6.1 Faction tooltips render one mangled line — **now the most severe item here**

Full chain in §L1. Every faction tooltip shows its whole description as a single title line with a
visible `\n`, and an empty body — including on the faction dropdown a new player opens on their first
lobby. Confirmed by a complete static read; the fix is one `.Replace("\\n", "\n")` with four existing
precedents in the engine. **Commit `75ac6941` tried to fix this three days ago and treated the layer
above it**, so the symptom is still live.

*(This item replaces the retracted Advanced-tab finding as the top "stranger would notice" entry.)*

### 6.2 The rest

1. **Bot names** — "Stable AI 0802" in a shipping opponent picker (§1.3). Already `PIPELINE.md:132` R4.
2. **1366×768 overflow** — the setup-row buttons overflow into `SPECTATE_AREA` by ~80px at that width.
   Documented and accepted at `lobby-players.yaml:736-739` and `WORKSPACE/BACKLOG.md:17` ("fix only if
   a player reports it"). 1366×768 is still one of the most common laptop resolutions; for a public
   release I would raise this above "only if reported". **Overlapping buttons are a first-impression
   defect.** L2 helps incidentally by freeing vertical space.
3. **~38 orphan lobby/server Fluent keys** in `mods/ww3mod/languages/en.ftl`
   (`WORKSPACE/bugs/discovered.md:1229-1235`) — edits there are silent no-ops. A trap for whoever does
   the copy pass; worth knowing *before* L3's renaming work, since renaming is a copy edit.
4. **Dead chrome** — `SKIRMISH_TABS`/`MULTIPLAYER_TABS` (~100 lines, force-hidden, **duplicate child
   IDs**) and an unreachable Servers panel (`WORKSPACE/BACKLOG.md:13`). Invisible to players; a
   correctness hazard for anyone editing lobby YAML, since duplicate IDs make `Get<T>` ambiguous.
5. **22 of 34 designed lobby options were never implemented** and are silently hidden (§5.1). Not a
   visible defect — a roadmap question.

---

## 7. Mockup

`WORKSPACE/lobby-ux-mockup.html` shows the current layout beside **three** spectator-tray designs
(L1), plus the L3/B1 bot menu and the B2 return button in context. Presented for a pick, not a review.

---

## 8. What I did not verify — and one thing I got wrong

**I got §5.1 wrong.** I claimed a user-facing Advanced tab renders empty. There is no such tab. The
error was inferring UI structure from code vocabulary (`CategoryAdvanced`) and from old commit
messages, without grepping the chrome to see whether anything instantiates it. Nothing did. **The
lesson worth keeping: a commit message describes an intent at a point in time; five later waves
superseded it. Read the chrome, not the changelog, for what a screen contains.** The falsifier I
wrote is what caught this, and it caught it in one screenshot — that discipline paid for itself.

Still unverified:

- **No game was launched by me** — launches are serialized through the manager. Every behavioural
  claim here is a source read.
- **L1 (faction tooltip)** is a complete static chain but I have not seen it rendered. Falsifier
  stated in §L1. This is the claim I would most like a human eye on, and §9 explains why that is
  currently blocked.
- **The 1366×768 overflow** — I am repeating the in-repo comment and backlog entry, both of which
  state it as measured. I did not measure it.
- **BAR's lobby layout** could not be confirmed from text sources; the `MainList2` pattern I cite is
  from BAR's *in-game* player list. Sound pattern, narrower provenance than I would like.
- **The Music tab and chat panel** were not audited beyond noting their positions.
- **`WORKSPACE/BACKLOG.md:15` is stale** (§1.2) and I have not removed it — that is an edit, and this
  branch is research-only.

---

## 9. Outstanding checks — status and cost

### Check A — DONE, and it refuted me

Run by the manager: `./tools/autotest/screenshot-lobby.sh advanced-tab --tab=advanced`,
`river-zeta-ww3`, at `66fd33d3`. Screenshot `manual_lobby_260819_190518/001_advanced-tab.png`.
Result and correction in §5.1. **Working as intended; no follow-up.**

### Check B — WITHDRAWN, resolved statically instead

I originally asked for a screenshot of a faction tooltip. **That is no longer needed.** Following the
chain through `world.yaml` → MiniYaml → `SplitOnFirstToken` closed it without a launch, and produced
a *stronger* result than a screenshot would have: not just "the tooltip looks wrong" but the exact
mechanism, the one-line fix, four in-engine precedents for that fix, and the reason the commit from
three days ago missed it. See §L1. **A screenshot would now only be belt-and-braces confirmation of a
chain I can already show end to end.**

### Check C — NOT WORTH A STAGING HOOK. My recommendation: skip it.

The spectate dead end (§3.3) needs mouse input — click `Add Bots`, then `Spectate` — and the agent
does not drive the game's UI. The available route would be a `Test.OpenLobbyTab`-style staging hook,
per the technique at `WORKSPACE/DISCOVERIES.md:6687`, where a case was temporarily added to that
switch to reach an otherwise-unreachable lobby state.

**I do not think it earns its cost, for three reasons:**

1. **The mechanism is fully read, not inferred.** `SPECTATE_AREA.IsVisible` requires `Slot != null`
   (`LobbyLogic.cs:321`); `slot_open` is admin-gated (`LobbyCommands.cs:463`,
   `ValidateSlotCommand(..., true)`); `ShowPlayerActionDropDown` has no reverse of "Move to Spectator"
   (`LobbyUtils.cs:117-142`). The only residual doubt is whether some *other* widget offers a route
   back, and I grepped for one and found none.
2. **The fix does not depend on the answer.** B2 and B3 are each small, independently useful, and
   worth doing whether or not the dead end is reachable exactly as I describe. Confirming it would not
   change what gets built.
3. **A hook is the wrong instrument anyway.** The hook would stage the *state*; it still could not
   click the buttons. What it would actually prove is what the panel looks like for a slotless
   client — which is the thing I already read off the visibility predicates.

**If it is ever worth pinning, an NUnit test is the better instrument than a launch** — there is
precedent in this repo at `f2e19907`, which pinned the force-start confirm state machine with unit
tests. The visibility predicates are lambdas over `OrderManager` state and are testable the same way,
with no launch and no focus theft. **I would only spend that if B2/B3 get built, as a regression pin
rather than a diagnosis.**
