# Capture request — the in-game hotkey reference (`wt/hotkey-reference`, base `main @ d69e6883`)

**The worker did not launch the game and did not capture. This is the request; the manager runs it.**
Nothing below has been executed — the driver script is new and unrun, and that is the largest
unverified thing in this branch.

## What is being photographed, and why it is not a new panel

The hotkey reference is the engine's **Esc → Settings → Hotkeys** panel, which has always shipped
(`mod.yaml:208` loads `common|chrome/settings-hotkeys.yaml`; `settings.yaml:9` declares
`HOTKEYS_PANEL: Hotkeys`). This branch did not build a second list. It added the four
`HotkeyGroups:` entries without which 20 of the mod's own hotkeys were never drawn in it, and one
line in How To Play pointing at it. **So the capture is a regression check on an existing screen,
not a first look at a new one.**

## Run it

```sh
./tools/autotest/screenshot-hotkeys.sh            # default map river-zeta-ww3
```

> **Rev 3, 2026-09-22, after run `manual_hotkeys_260922_011140` returned rc=0.** That run was a
> real result — both clicks dispatched, the panel on screen — but its second frame was
> byte-identical to its first, so the new `Garrison & Transport Commands` section was never
> photographed: **the list cannot be scrolled by any existing verb**, and everything below the
> first ~11 rows was unreachable. The driver now types into `FILTER_INPUT` (new `type` cmd verb,
> engine side) and takes **three filtered shots** instead of one useless duplicate. The overlapping
> labels that run exposed are fixed too — see "The label overlap" below.
>
> **Rev 2** (run `260922_005942`, NO-RESULT): the click ids were never wrong and the resolver is
> fine — the clicks fired before the world finished loading. `click_until` replaced the blind sleep.
>
> **The rerun line has not changed at any revision.**

It needs an already-built tree (`launch-game.sh` does not build). It is `--hidden`-equivalent in
spirit but **not** hidden: it launches `Graphics.Mode=Windowed`, `1600,900`, deliberately, because
the settings window is authored 900×600 (`settings.yaml:11-12`) and a windowed 1600×900 frame puts
it centred with margin and keeps one `Read` at ~1,700 tokens. It writes to
`~/.ww3mod-tests/screenshots/manual_hotkeys_<ts>/` and prints `result.txt`.

Chain: `Test.OpenIngameInfoPanel=AutoSelect` opens `INGAME_MENU` → `click SETTINGS` → `click
HOTKEYS_PANEL` → two screenshots → `quit`. Both ids are assigned in C#, not authored:
`IngameMenuLogic.AddButton:331` sets `button.Id = id` from the bare string in `ingame-menu.yaml:5`'s
`Buttons:` list, and `SettingsLogic.AddSettingsTab:178` sets `tab.Id = id` from the panel key in
`settings.yaml:5-10`. `PollCommands` `.Trim()`s the verb argument, so there is no whitespace in play
either.

Each click is now sent in a retry loop that stops when `debug.log` shows
`[TestMode] external click: <id> → dispatched`, so **the precondition is the wait** and there is no
guessed sleep to get wrong on a slower or colder machine. Both retries are safe to repeat: after a
successful `click SETTINGS` the button is gone (`CreateSettingsButton` sets `hideMenu = true`, and
`IngameMenuLogic:193` gates the button container on `!hideMenu`), so a redundant send is a no-op;
and re-clicking a settings tab just re-selects the tab it is already on. If a click never lands, the
driver takes **one** frame labelled `99-stuck-at-<id>` so you can see where it got to, quits, and
exits 2 — it does not go on to photograph the wrong screen twice.

## What the first run actually showed (`manual_hotkeys_260922_005942`)

Worth keeping, because the frames argue for the wrong conclusion. Both PNGs were healthy
(1,024,258 bytes — and **byte-identical to each other**) pictures of the Esc menu with a Settings
button plainly on screen, which reads as "the menu was up, so the click ids must be wrong." The log
ordering says otherwise: `external click: SETTINGS → NO SUCH VISIBLE WIDGET` appears **above** the
sprite loads, `Scenario selection`, `[danger] reference`, `DEFCON wall region` and `Sync reports
disabled` — every one of those a world-construction line. There was no world yet, so no player HUD,
no `MenuButtonsChromeLogic` and no `INGAME_MENU` to find. The second click landed one line *after*
`Sync reports disabled`, missing by a hair. A frame at t≈26s showing the menu says nothing about
t=20s.

**Two traps that made this look like an id problem, both worth carrying:**

1. **`NO SUCH VISIBLE WIDGET` is two different failures wearing one message.** `ClickWidget`
   (`TestModeScreenshots.cs:282-294`) returns false both when `FindVisible` found nothing *and* when
   it found the widget but could not read a non-null `OnClick` field off it, and `:227` logs the
   same string either way.
2. **`HOTKEYS_PANEL` names two widgets.** `SettingsLogic` sets `tab.Id = id` on the tab button and
   `container.Id = panel.Key` on the panel — the same string. `FindVisible` walks children forward
   and takes the first match; `SETTINGS_TAB_CONTAINER` precedes `PANEL_CONTAINER` and the container
   is `IsVisible`-gated on being the active panel, so the button wins today. If that order ever
   changes, the driver would retry forever against a container with no `OnClick` and report a
   missing widget that is visibly on screen.

## The label overlap — fixed, and here is the arithmetic

Run `260922_011140` showed two descriptions colliding: one clipped off the panel's left edge with
the neighbouring column's label drawn over it. That is an overflow, not a layout bug, and the
column is small.

`Label@FUNCTION` is **198 px** wide and `Align: Right`, derived entirely from authored numbers:
`SETTINGS_PANEL` is 900 wide (`settings.yaml:11`); `PANEL_TEMPLATE` is `PARENT_WIDTH - 190 - 20` =
**690**; `Container@TEMPLATE` is `(PARENT_WIDTH - 24) / 2 - 10` = **323** (two columns);
`Label@FUNCTION` is `PARENT_WIDTH - 120 - 5` = **198**. Right-aligned with no scissor anywhere on
the path, so anything wider is drawn *leftwards* out of its own container and over whatever is
there. The label has no tooltip fallback either — `TruncateButtonToTooltip` is applied to the
`HOTKEY` button, not to this — so an over-long description has no recovery at all.

Measured every one of the 210 shipped descriptions in FreeSans 14 (`Regular`, via
`ChromeMetrics TextFont`), including the `:` that `BindHotkeyPref` appends. **Eight overflowed and
all eight were ours; not one upstream description did.** Shortened, with before → after in px:

| Hotkey | was | now | new text |
|---|---|---|---|
| `TransportDropSupply` | 328 | **151** | Drop all supply (= Deploy) |
| `UnloadMenu` | 325 | **176** | Unload menu (choose a class) |
| `TransportUnloadAll` | 273 | **161** | Unload all troops (= Deploy) |
| `GroupScatter` | 272 | **149** | Scatter queued waypoints |
| `Evacuate` | 253 | **165** | Evacuate (map edge, refund) |
| `ShowTerritory` | 236 | **167** | Hold to show territory overlay |
| `GarrisonEjectAll` | 230 | **164** | Unload all (ports and shelter) |
| `ShowAllOrders` | 199 | **160** | Hold to show friendly orders |

The eight `GarrisonEjectPortNN` rows went 197 → **131** ("Unload firing position N") as well: they
fit by one pixel, which is not a margin. The widest description in the mod is now
`WaypointMode` at **177 px**, leaving 21 px of headroom.

**Three of the eight were mine, added yesterday** — the `Garrison —` / `Transport —` prefixes. They
are gone: the group heading already says "Garrison & Transport", so the prefix was redundant as
well as too wide.

> The measurement is `fontTools` summing `hmtx` advances scaled to 14 px and truncated per glyph,
> which models `FreeTypeFont`'s `metrics.horiAdvance >> 6` (`:106`) and `SpriteFont.LineWidth`
> (`:248-255`). It is **not** a render: FreeType's hinting can shift an advance by a pixel, and
> `deviceScale` on a Retina display divides out but is not exactly reproduced here. Treat the
> numbers as ±a few px — which is why the target was ≤190, not ≤198.

## The expected frames (1600×900)

Four now, and each is a genuinely different state — the old duplicate second shot is gone.

### `01-hotkeys-panel-top` — unfiltered

- A 900×600 settings window, centred, tab column on the left with **Display / Audio / Input /
  Hotkeys / Advanced**, `Hotkeys` highlighted.
- A filter box top-left, a context dropdown top-right, and a scrolling list of
  `<Description>:` ⟶ `<key>` rows under **bold group headings**.
- **The four headings this branch added must be present and must read as prose, not slugs:**
  `Engagement Stance Commands`, `Cohesion Commands`, `Resupply Behaviour Commands`,
  `Garrison & Transport Commands`. A heading rendering as `hotkey-group-cohesion-commands` means the
  fluent key did not resolve — check `mods/ww3mod/languages/en.ftl`.
- Under the first three: nine rows showing `Ctrl+Alt+A`, `Ctrl+Alt+D`, `Ctrl+Alt+F` and
  `Ctrl+Alt+1` … `Ctrl+Alt+6`. **These nine have never appeared in this panel before.**
- Under `Garrison & Transport Commands`: eleven rows whose key column reads **`Undefined`**. That
  is correct and is the point — they ship bindable, not bound. (`Hotkey.Invalid` is
  `Keycode.UNKNOWN`, and `keycode.unknown = Undefined` in `common|fluent/common.ftl:950`; the ten
  `SupportPower` slots already in this panel render the same way, so the two blocks should match.)
- **This frame shows only the first ~11 rows** — `HOTKEY_LIST` is 395 px tall at 30+5 per row —
  so it reaches `Game Commands` and no further. The new groups are in the filtered shots below,
  **not** here; their absence from this frame is expected.
- **Two things run `260922_011140` already confirmed and that should still be true:**
  `Waypoint (queue orders) mode: O` in **red** — that is `HasDuplicates` against
  `ProductionTypePowers`, the collision filed in `bugs/discovered.md`, now visible rather than
  merely computed; and `Power-down mode: Undefined`, which matches `PowerDown: # X` having its key
  commented out in place.
- **No label should now overlap its neighbour or run off the panel's left edge.** That is the
  regression this revision is mostly for.

### `02-filter-position`, `03-filter-spacing`, `04-filter-ammo` — filtered

The driver types into `FILTER_INPUT`; the filter is a case-insensitive substring of the
**description** (`HotkeysSettingsLogic.cs:335-343`). No single filter reaches all four new groups —
they share no common word, checked across all 210 descriptions — so there are three:

| Frame | Filter | Expect |
|---|---|---|
| `02` | `position` | **Engagement Stance Commands** (`Defensive positioning: Ctrl + Alt + D`, `Hold position: Ctrl + Alt + F`) **and Garrison & Transport Commands** (`Unload firing position 1…8`, all `Undefined`) — 10 rows, two new headings in one frame |
| `03` | `spacing` | **Cohesion Commands** — `Tight / Loose / Spread spacing`, `Ctrl + Alt + 1/2/3`. 3 rows |
| `04` | `ammo` | **Resupply Behaviour Commands** — `Hold when out of ammo: Ctrl + Alt + 4`, `Evacuate when out of ammo: Ctrl + Alt + 6`. 2 rows (`Auto-resupply` has no "ammo" in it and is correctly absent) |

Together these prove all four new headings render as prose rather than raw `hotkey-group-…` slugs,
which is the one thing a static check could not settle: the four fluent keys are four separate
lines in `en.ftl` and a typo in one would not affect the others.

**Reading budget:** each frame is ~1,900 tokens. `01` and `02` carry most of the evidence; `03` and
`04` each confirm one fluent string and can be skipped if the budget is tight.

## Also worth one frame, but not scripted

**How To Play, to check the new last line fits.** `ingame-info-howtoplay.yaml` gained `FOOT_L3` at
`Y: 412`, and the same widget is loaded in two places with different room:
`./tools/autotest/screenshot-infopanel.sh` reaches the in-match one via its tab strip (the panel
sits at `Y:65` in a 500px `PANEL_ROOT`, so the row ends at 494 — 6px of slack), while the **main
menu** copy is the tight one: `HowToPlayBriefingLogic.cs:26` loads it into `BRIEFING_CONTENT`, whose
declared height this branch moved 420 → 435. Nothing clips to that number, so the check is visual:
**does the last line sit clear of the Back button, or is it crowding it?** The main-menu route needs
Mode 2 (`start-screenshot-mode.sh`, then the How To Play button) and a human click.

## What makes this a NO-RESULT rather than a failure

The script grades itself and exits 2 on any of these; `result.txt` names which.

1. **A PNG under 120 KB.** A near-flat frame compresses to nothing (SCREENSHOT.md: ~59 KB black vs
   1.6 MB real). Black frame ⟶ the shot beat the first render pass. Re-run; do not report a blank
   panel as a missing panel.
2. **No PNG at all** — the game never reached `LogicTick`, or died in the build/launch.
3. **`click SETTINGS` or `click HOTKEYS_PANEL` not dispatched.** This is the failure mode that
   *looks* like a result: `send` waits only for the cmd file to be consumed, which happens whether
   or not the widget was found, so a click fired before the Esc menu existed photographs the map and
   still writes two healthy multi-megabyte PNGs. The script therefore greps `debug.log` for
   `[TestMode] external click: <id> → dispatched` (`TestModeScreenshots.cs:227`) and fails without
   both. **If you see two large PNGs and exit 2, believe the exit code, not the file sizes.**
4. **The game exits before a command is consumed** — `send` reports it and the run is not a result.
5. **A `99-stuck-at-<id>.png` exists at all.** That frame is the driver saying which step it never
   got past; `result.txt` names the same id on a `stuck:` line.

A non-zero exit from this script is not a verdict about the branch. Same family as the launcher-127
and zero-byte-log traps in `CLAUDE.md`: nothing was photographed, so nothing was shown.
