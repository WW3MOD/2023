# Release readiness — one last look at everything

**Ref: `main @ 61d0c1f8`, 2026-09-21. Read-only pass: no build, no launch, no lint, no game start.**
Every `file:line` below was opened at that ref and read. Where I am relaying a figure from another
document rather than re-deriving it, the sentence says so. Where two documents agree and I could not
get to the code, it is in `## Watch` and not in a verdict.

**Audience is the one the 2026-08-16 block fixed: a public release to strangers.** The severity ladder
is that block's, unchanged. The "deferred to final pre-release polish" table (R4 / U9 / U4) is now due,
and it is answered per-row in §1.4.

---

## The three weakest areas

**Gameplay — the two things the user named are the two with the least evidence behind them.** DEFCON
Escalation is on the lobby dropdown and its signature phase, the DEFCON 2 cease-fire that the whole
mode is built to dramatise, was **measured at 98 ticks — 5.9 seconds** (`escalation-gameplay-review-260919.md`
§2.4, run `260920_010605_p1901`), shorter than the 66-tick banner announcing it; and in that same
measured match **neither side fired a single ladder warhead in 6.9 minutes of open nuclear phase**,
so the 1/20/50/100 kt ladder, the three postures and the four cooldowns have never been exercised by
anything, by construction.

**Product identity — the frame around the game still says OpenRA and Red Alert, and one panel is
actively wrong in a way nobody has filed.** `MainMenuLogic.cs:279-282` puts four lines behind the
main menu's `v` button and **three of them are false on a packaged release**: the version is a
hardcoded `"WW3MOD — Pre-Alpha"` on a tree that has shipped `v0.1.2`; `"Built: "` is
`DateTime.Now` evaluated when the menu opens, so it shows the player's own current date, every day,
forever; and `"Fork: "` renders `mod.yaml`'s `Version:`, which `packaging/windows/buildpackage.sh:106-108`
**overwrites with the WW3MOD tag** — so the line labelled as the OpenRA release this forked from reads
`v0.1.2`. Around it: `mods/ww3mod/icon.png` is still byte-identical to `engine/mods/ra/icon.png`,
one music track ships, and `credits.txt` has three sections reading `(nothing here yet)`.

**Stability — the one declared hard blocker is the one thing with no measurement at all.** The 2-human
desync (item 42) was promoted to a hard release blocker by the 2026-08-16 audience ruling; four 2-human
games desynced within seconds each, the replay artifacts have aged out and are gone
(`items/54-carried-defects-hygiene.md`), and **nothing has been tested against any of it since** —
the one fix that landed (`91056894`) was never confirmed against the reported symptom, and the
confirming test needs the user hosting.

---

# Part 1 — Reconciliation

Method per the pipeline README: one `git log -S` or grep on the **behaviour**, not the defect, then read
the file. A merged branch is not a finished item. Line cites are as measured at `61d0c1f8`; several have
drifted from the values recorded in `PIPELINE.md` and the drift is noted rather than silently corrected.

## 1.1 R-items

| # | Central behaviour grepped | Status at `61d0c1f8` |
|---|---|---|
| **R4** | `Name:` on the two `ModularBot` blocks | **CLOSED BY DECISION** (chrome half) — user ruled 2026-09-05 "leave it exactly as it is". The **difficulty ladder** half is genuinely open and is not release-gating. See §1.4. |
| **R5** | unbound hotkey declarations | **STILL OPEN, COSMETIC.** Unchanged; 9 declarations, none player-reachable. No work done, none warranted. |
| **R6** | `Key:` count in the garrison/cargo panel region | **PARTIAL, COSMETIC. Re-counted, region moved again.** `GARRISON_PANEL` is now `ingame-player.yaml:830`, `CARGO_PANEL` `:1053`, file 1714 lines (was 1379). In `:830-1269`: **11 `TooltipText:`, 0 `Key:`**. `Text: X` is 0 repo-wide. Labels and tooltips shipped; the same 11 buttons (`EJECT_PORT_0..7` at `:886,903,920,937,954,971,988,1005`, `EJECT_ALL:1040`, `UNLOAD_ALL_TROOPS:1244`, `DROP_SUPPLY:1260`) still carry no hotkey. **`PIPELINE.md`'s `:610-983` region cite is now wrong** — that range no longer contains either panel. |
| **R7** | product strings in the install chain | **PARTIAL — 2 of 7 fixed at `06470461`, 5 unchanged.** Fixed: `mod.config:49` `PACKAGING_FAQ_URL="https://github.com/WW3MOD/2023/issues"` and `:46` `PACKAGING_WEBSITE_URL`. **Still OpenRA:** `mod.config:84` install dir `"OpenRA WW3MOD"`; `:88` registry key `"OpenRAWW3MOD"`; `buildpackage.nsi:78` Start Menu `"OpenRA"` (cite drifted from `:54`); `buildpackage.nsi:307` `$APPDATA\OpenRA\ModMetadata`; `buildpackage.nsi:317`/`:383` desktop shortcut `"OpenRA - WW3MOD"`; `engine/Directory.Build.props:17` `<Product>OpenRA</Product>`, untouched since the upstream merge `c5bb5ece`. |
| **R9** | the onboarding panel's contestation wording | **SHIPPED — CLOSE. Both halves.** `c205810f` (2026-09-02) rewrote the panel: `ingame-info-howtoplay.yaml:123-136` now reads *"reinforcements slow, then stop, and a red bar fills. If it fills they are frozen out — and unless a teammate still holds a Route, that is the match."* That is the shipped mechanic exactly. **And the second instance the verdict named is fixed too**: `SupplyRouteContestation.cs:702-742` now evaluates `HasActiveTeamSupplyRoute()` **once** and forks on it — the "has lost their Supply Route! Production frozen." line at `:727` fires only on the rescuable branch, with a comment at `:716` naming the old defect by its symptom. `PIPELINE.md`'s R9 entry is stale and should be closed. |
| **R12** | one-click cursor for topping up a cache | **STILL OPEN, SHOULD-FIX at most.** No change; the round trip works, the affordance does not. |
| **R14** | the three independent reasons a captured heli cannot recover | **STILL OPEN, all three intact.** (1) `ChangesHealth@CrashBurn` `aircraft.yaml:266`; (2) `SpeedMultiplier@CrashDisabled` `:334`; (3) the repair host. **Correction to the standing note:** `HPAD` is not `~disabled`-at-`structures.yaml:688` — it is defined at **`structures.yaml:779`** with `Prerequisites: ~disabled, ~techlevel.medium` at `:794`, and **appears on 0 of the 10 shipped maps** (grepped each `map.yaml`). The gate is `CheckDisabledRecovery` at `Air/HeliEmergencyLanding.cs:412`, `AutorotationDamageState = DamageState.Heavy` at `:99`. ⚠️ My first grep for `^hpad:` returned nothing and I nearly filed "the actor does not exist" — it is `HPAD:`, the MiniYaml case trap from CLAUDE.md, caught on the second pass. |
| **R15** | a verb that promotes anyone into a vacated commander slot | **STILL OPEN, exactly as re-scoped.** `VehicleCrew.cs:617 FillSlot` / `:718 VacateSlot`; `VacateSlot` clears the flag and revokes the condition and does nothing else. Substitution ships in YAML (`vehicles.yaml:329-347`, `^CrewedVehicle3`); the inverse does not. `^CrewedVehicle2` (`:307`) still has neither direction. |
| **R16** | a second cargo sync defect beyond R10; "Phase 3" | **OBSOLETE AS FILED.** Unchanged since the 2026-09-02 verdict: the sync half was R10 and R10 is closed; "Phase 3" is one line in `RELEASE_V1.md` with no code. **Recommend striking the item and re-filing the Phase-3 design question inside item 83** — 83's dossier already flags them as the same missing UI design. |

## 1.2 Queue items

| # | Behaviour grepped | Status |
|---|---|---|
| **22** | the Bar A thresholds, and whether they are ratified | **STILL OPEN — one user word, no run.** `tools/autotest/parse-case01-bar.py:33-37` live and unratified: `BAR_A_DEF_MAX_MEAN=50`, `BAR_A_ATT_MIN_MEAN=300`, `BAR_A_MIN_SEEDS=6`, `BAR_B_DEF_MAX=0`. `AWAITING-USER.md` §4. |
| **32** | the four parity scenarios; a result artifact | **PARTIAL — authored, never run.** All four exist (`tournament-parity-{mirror-us,mirror-ru,cross-usru,cross-usru-swapped}`). No result artifact anywhere in the tree. Part (a)'s numbers are stale against shipped YAML; part (c) has one doc defect (proposal `003` reads `PROPOSED`, its YAML shipped at `bba63d11`). |
| **34** | `Cargo.LockForPickup` wiping the transport's queue | **STILL OPEN, blocker intact.** `Cargo.cs:557 → LockForPickup(:576) → self.CancelActivity()(:583)`. Cites drifted from `:552/:571/:578`. Human path still one-directional. |
| **35** | `[ferry] refused reason=` | **STILL OPEN.** Zero grep hits across `engine/`. The instrumentation the re-cut names as step 1 does not exist. |
| **39** | branding strings | **PARTIAL — subsumed by R7 + §2.2.** `mod.yaml:4` `Website:` is now the WW3MOD repo. The in-game info panel (§2.2 D1) is the live half and is unfiled. |
| **40** | danger-scale stage (c) | **STILL OPEN.** No re-derivation landed. It does **not** gate item 43 any more (43 was taken 2026-09-05/06). |
| **42** | 2-human desync, tested against anything | **STILL OPEN — and untested, which is the whole point.** `EnableSyncReports` plumbing is live at `OrderManager.cs:142-170` (ANDed with a human-client count and a lobby option, `:159`, `:162`) — **the user must host.** Artifacts under `audit/260817-runtime-desync/` are 8 TSVs from the *saved-game restore* leak, not from a 2-human game. **Hard release blocker by the 2026-08-16 ruling and the only item in this table that cannot be advanced without the user.** |
| **43** | a valid benchmark corpus | **PARTIAL — re-take discharged, disclosure half open.** `benchmarks/260905-rebaseline.md` exists. ⚠️ **The baseline is already invalidated again**: `@stable` has moved at least twice since (`MissionReinforceEnabled` via `b6207b9b`; `MinUnitsPerAmbush`/`FreePoolMinAdvanceUnits` at `bb89f9fd`, both profiles). |
| **44** | `PreemptScanInterval` sites; a RED control | **(a) done. (b) STILL OPEN, blocked on a run.** Five sites re-counted: `defaults.yaml:439, 686, 771, 797, 878` — the count matches the item, **all five cites have drifted** from `:396,641,726,752,833`. No RED control has ever been run against the redesigned assertion. |
| **45** | the incommensurability the spec names | **STILL OPEN, concrete.** `Missile.cs:205 CloseEnough = new(298)` (engine default — it is **not** in ATGM's YAML, which is the trap) against `weapons-missiles.yaml:12 Inaccuracy: 512`. Spec shipped (`85d146c8`); the measurement it demands has not been taken. |
| **46** | each asset slot, on disk | **STILL OPEN — zero drift, re-measured not relayed.** `mods/ww3mod/icon.png` md5 `e9b6dc3d…` **byte-identical** to `engine/mods/ra/icon.png`. Exactly one music track: `bits/sounds/music/journey.aud`. `loadscreen.png` present (6110 B) and untouched since `1218bd90` *"Loadscreen, removed logo for now"*. **This belongs in `AWAITING-USER.md`, not a worker queue** — the verdict from 09-02 stands. |
| **48** | unit voices | **PARTIAL, and the stub's headline is IMPRECISE — correct it.** The stub says *"a US GI says 'Yes sir' for a Russian conscript"*. **The faction-variant mechanism ships and works**: `rules/sound/voices.yaml:1-12` `GenericVoice` declares `america: .v01,.v03` / `russia: .r01,.r03`, so a Russian conscript plays RA's *Soviet* variant, not the American one. **What is actually true, and is the dossier's own wording, is worse and duller:** every line in the file is Red Alert's, in English, and the roster is RA's verbatim — `TanyaVoice`, `VolkovVoice`, `EinsteinVoice`, `AntVoice`, `StavrosVoice`, `DogVoice` are all still declared (`:57-134`). No WW3-authored VO exists. |
| **49** | any committed screenshot/observation artifact | **STILL OPEN.** The onboarding commit's own screenshot claim is uncorroborated by the tree (`items/48` records this). |
| **53** | networking leftovers | **STILL OPEN, user-declined in part.** Not re-derived this pass. |
| **54** | the two surviving lines | **STILL OPEN, small.** Contains the record that the desync replays are gone — carry it. |
| **55** | multiplayer continuity | **PARKED, correctly.** Gated on item 42's determinism, which is RED. |
| **56** | the two flags, and the acceptance bar | **PARTIAL — code shipped and ON, acceptance never taken.** `ai.yaml:1754 IgnoreDangerForDelivery: true`, `:1652 ClusterStickinessNeedMargin: 1000` (**both cites drifted again** — HOTBOARD's `:1754`/`:1652` are correct, `PIPELINE.md`'s `:1339` is dead). Both on the shared instance, so **`@stable` carries them**. The bar — one bot-vs-bot match on `tournament-s1-eco-river-zeta` with the `[composition] census` precondition — is unchanged and still owed. **This is a RUN, not a dispatch.** |
| **62** | the `Versus` tables; nav-guard's map count | **STILL OPEN, both, verbatim.** `weapons-missiles.yaml:394-401` `IskanderTargeter Warhead@Target` zeroes `Brick` and omits Unarmored / Kevlar / Indestructable. **Verified rather than relayed:** across `mods/ww3mod/rules/` the declared armour types are `Concrete 13, Heavy 20, Indestructable 1, Kevlar 1, Light 45, Medium 13, None 6, Unarmored 3, Wood 4` — **`Brick` is declared zero times**, and the three omitted classes are all real. `HIMARSTargeter` inherits it unchanged (`:403`). nav-guard: `baseline.json` `states.live` = **10 keys** against **357** scenario directories + 10 maps. The `rotor-stopped` line is refuted and must not be dispatched. |
| **64** | `RendezvousWithOffensiveStaging`, by key not by line | **STILL OPEN AND STILL SWITCHED OFF.** `ai.yaml:2258` and `:2353` both `false`; C# default `false` at `MountedTransportBotModule.cs:92`. Both profiles byte-identical. **Never run in a live match.** The residual axis↔staging beat (`PartitionHeldAxes` before `BuildFreePool`) is untouched. |
| **74** | a notification on neutralisation | **STILL OPEN — needs the user's call, not a diff.** Unchanged. |
| **75** | `ShowNever` | **STILL OPEN.** Exactly one occurrence under `mods/`: `infantry.yaml:57` (drifted from `:56`). Still blocked on a screenshot pair, which this pass could not take. |
| **76** | a paused armament's scan radius | **STILL OPEN.** No gate landed. |
| **77** | the SR order/cursor honesty | **STILL OPEN.** `structures.yaml:216` `TargetTypes: NoAutoTarget, …`; the panel/cursor contradiction is unaddressed. |
| **78** | the spawn-and-bounds arithmetic the item asks for first | **STILL OPEN, and the free settlement has still not been done.** No evacuation-edge study exists in `WORKSPACE/`. **Do that before anything else on this item** — the dossier says drop it if the arithmetic says the nearest edge is usually the owner's own. |
| **79** | entry displacement | **STILL OPEN.** Zero `Displacement`/`EntryOffset`/`DropPoint` symbols in `SupplyRouteContestation.cs`. |
| **80** | the anomaly channel, and its gate | **STILL OPEN.** `HitCheck.cs:165-166` builds and writes the string on every warhead application; `CombatDebugOverlay.cs:131` gates the *visible* readout on `debugVis.DamageNumbers`, whose default-off is test-guarded and is a declared release blocker to flip. Routing is the work. |
| **81** | the asymmetric `IsRelevantActor` test | **STILL OPEN, one line, and a balance call.** `SupplyRouteContestation.cs:33 CaptorTypes = ("Player","Vehicle","Tank","Infantry")` — no `Plane`; enemy path tests it at `:290`, allied path does not. |
| **82** | `EngageAtLongestArmamentRange` opt-ins | **STILL OPEN.** Exactly one actor opts in — `vehicles-russia.yaml:981` (tunguska). Shipped default still takes the minimum. |
| **83** | any reserve/veterancy-bank symbol | **STILL OPEN, LARGE.** Zero hits for `VeteranReserve`/`ReserveVeterancy` anywhere. |
| **84** | a hold-when-truck-closing gate on `AutoSeekSupplies` | **STILL OPEN.** The trait exists (`AutoSeekSupplies.cs`) and its Info carries `Enabled = false (:34)` and `ReturnWhenEmpty = false (:69)` — **neither is the gate this item describes.** User-approved in principle, unbuilt. |
| **85** | lead-hold / speed matching | **STILL OPEN.** User: not standalone, not before v1.0. |
| **86** | whether the lane consults the offense floor | **STILL OPEN — and this is the precise shape.** Both mitigations shipped on both profiles (`ai.yaml:772/1073` experimental, `:3150/3204` stable). But `LaneAmbushBotModule` **never reads `PoiOffensiveBotModule`'s pool size**: its own comment at `:385` says the lane posts and *"[offense's] own `FreePoolMinAdvanceUnits` then decides"*, i.e. the lane takes first and offense discovers it is short. The one-line ruling the item asks for is unimplemented. |
| **87** | scorer attribution | **STILL OPEN.** `parse-s2-batch.py:49` still computes `kills_cost - deaths_cost` with the bias unaccounted. A decision item. |
| **88** | drifted in-tree citations | **STILL OPEN, LOW.** 30 live sites still assert a 25-tick-per-second rate across `engine/`, `mods/`, `tools/`. ⚠️ **This audit found six more drifted line cites** (R6, R7, 34, 44, 56, 75) — the class is growing, not shrinking. |
| **89** | `MarkAsPlaceholder`; the mode dropdown | **SHIPPED AS A MODE, NOT VERIFIED AS A GAME.** `DefconEscalation.cs:265 MarkAsPlaceholder = false`; `:63 ModeDefault = Skirmish` with a `[Desc]` at `:60-62` saying it **must remain** Skirmish until the feature is complete; dropdown visible (`:66`), Escalation and Skirmish are its two entries (`:367-371`), Sandbox admitted only when `ModeDefault` names it (`:373-374`). Borders authored on all nine derived maps (`positioning-borders-260919.md`). **The open half is gameplay, not construction — see §2.7.** |
| **90** | `ScenarioLobbyDropdown` in source | **NOT STARTED, correctly recorded.** The two-correction history in the stub is the reliable version; the layer is still entirely in source. Sequencing (shellmap → dropdown → deletion) is load-bearing. |

## 1.3 Ambush block (67–71) — user-gated, nothing implementable

| # | Behaviour grepped | Status |
|---|---|---|
| **67** | the observer floor in `MapLayers.AddSource` | **STILL OPEN, and re-priced correctly.** The cheap half shipped (`1ff73ae5` + `1ad638e7`) and is test-guarded. What survives is the option a previous session priced and **declined** — it moves fog rendering, radar and the AI belief layer. Re-opening needs a reason, not a re-file. |
| **68** | every grantor of `enable-ambush-tactics` | **STILL OPEN — HEADLINE DEFECT, and the code says so itself.** Grantors at `61d0c1f8`: `LaneAmbushBotModule.cs`, plus seven autotest scenarios. The seam is `defaults.yaml:454-455 ExternalCondition@ambushtactics`, and the comment block at `:421-429` states outright that the grant is **per-unit, bot-posted only**, and that `GetConditionCount` is 0 *"on humans"*. **A human who clicks Ambush still gets plain hold-fire.** |
| **69** | the three sibling detectability defects | **STILL OPEN.** The cover-ladder headline is refuted and merged; the siblings (two `dugin` timer bugs, the `primary`-only firing penalty, infantry CV ceiling) are untouched. |
| **70** | `test-case01b-detect` having ever run | **STILL OPEN. The cheapest measurement in the programme has still never been taken.** No result artifact. |
| **71** | `Prone*` tokens among all `DamageTypes:` | **STILL OPEN — re-counted, and the ratio is worse.** Exactly **one** `Prone*` token (`weapons-superweapons.yaml:1470 DamageTypes: Prone30Percent`) against **194** `DamageTypes:` declarations under `rules/weapons/` — the denominator has moved 145 → 194 since the item was written; the numerator is still 1. Prone reduces damage from one superweapon and nothing else. |

## 1.4 The deferred-polish table — now due

| Row | Verdict now |
|---|---|
| **R4 — lobby AI names** | **KEEP DEFERRING.** The user ruled 2026-09-05 "leave it exactly as it is", and the premise still holds: bot work has not stopped (items 40, 64, 86 are live, and `@stable` has moved twice since the last baseline). The `0902` stamp still records *which* snapshot the copy is, and that is worth more than the presentation costs. **Revisit when bot work stops; the noun is the user's to pick.** |
| **U9 — art + audio** | **DUE, AND IT IS THE USER'S WORK, NOT A WORKER'S.** Re-measured this pass, not relayed: icon byte-identical to RA's, one music track, empty loadscreen slot. The standing document exists. **Move it to `AWAITING-USER.md`** — a worker cannot author any of it, and the only worker-shaped fragment (a 2-line `dr` sequence edit) is pointless until a `dr` Russian image exists. |
| **U4 — command bar icons** | **DUE, but the deliverable is unlocated.** The duplicate-map table the ruling names as the deliverable ("19 of 25 buttons share art across 11 sprites; 14 new icons needed") **could not be found in `audit/260816-content-completeness.md`** by grep at this ref. Either it lives elsewhere or it was never written. **Settle that before dispatching anything** — see `## Watch`. |

## 1.5 `bugs/discovered.md` — open entries

60 dated entries; 8 carry an explicit `FIXED` marker. Of the rest, the ones that bear on a public
release:

| Entry | Status |
|---|---|
| `[2026-09-20] [MEDIUM]` **`demo-nuke-arsenal` cannot fire 2 of its 6 warheads** | **STILL OPEN.** Faction-tiered prerequisites (`player.yaml:238-243`) vs an all-USA shot table. Demo-only, but it is one of the artifacts used to show the nukes off. |
| `[2026-09-20] [MEDIUM]` **AT mine fires one 4000-damage warhead, never the 10000 direct hit** | **STILL OPEN BY DESIGN — needs a balance ruling.** The duplicate-key bug is fixed; restoring `@Target` is a balance change requiring a combat-sim pass. |
| `[2026-09-20] [LOW]` **A-10 30mm never reloads while docked** | **STILL OPEN BY DESIGN.** Same shape; also perturbs the derived `A10.Airstrike`. |
| `[2026-09-19] [MEDIUM]` **DEFCON 2 transition banner destroyed before anyone sees it** | **STILL OPEN — and §2.4's measurement makes it worse than filed.** The banner holds 66 ticks; the phase measured 98. This is `escalation-gameplay-review` **§B6**, which that document ranks as the single top item in its list. |
| `[2026-09-19] [MEDIUM]` **Three-or-more-side Escalation gets no border and a total hold-fire** | **STILL OPEN.** Unenforced constraint with a non-graceful failure. §B3. A stranger filling a 4-slot lobby hits it. |
| `[2026-09-19] [LOW — not fixed]` **Windows installer ignores `/D=`** | **STILL OPEN, LOW.** |
| `[2026-09-14] [LOW]` **`player.WinState` can never …** | **STILL OPEN** — scenario-author trap, not gameplay. |
| `[2026-09-12] [UNKNOWN]` **`bradley` has no drawable sprite** | **RESOLVED BY THIS PASS — NOT A DEFECT, and the entry should say so.** The entry declined to check reachability. I checked: `sequences.yaml:208-213` resolves `bradley` `idle` to `1tnk`, which is absent from the tree — **but `mod.yaml:12-22` mounts `~conquer.mix` from `^SupportDir|Content/ra/v2/`**, which is where `1tnk.shp` lives and which the first-run content installer fetches. The bradley draws. **The real finding underneath is an identity one, not a rendering one: a US IFV is drawn with Red Alert's Allied Light Tank sprite.** |
| `[2026-09-05] [LOW]` **`CRAM` fires with an empty magazine** | **STILL OPEN.** |
| `[2026-09-05] [med]` **fixed-wing ordered to land on a same-owner `afld` never lands** | **STILL OPEN.** Player-visible; see §2.7. |
| `[2026-08-30] [med]` **production tooltip overlaps the sidebar and is not opaque** | **STILL OPEN — and it is the most-seen UI defect in this list.** `ProductionTooltipLogic` clamps `leftWidth` to `MaxTooltipWidth` on both bounds, and `TooltipContainerWidget.GetAnchoredPosition:154` clamps only vertically. Observed at 3584×2240; **never compared against `main` by screenshot.** |
| `[2026-08-30] [low]` **raw YAML weapon keys leak into the tooltip** (`5.56mm.DMR`, `TankRound.Abrams`, `HIMARSTargeter`) | **STILL OPEN.** `AmmoPool.cs:200-209` strips `^`, `-`, `_` and never `.`. **One character in one `Replace`.** |
| `[2026-08-30] [low]` **single-pool units get no ammo-cost total** | **STILL OPEN.** `ProductionTooltipLogic.cs:208` gates on `pools.Length >= 2`. |
| `[2026-08-30] [low]` **five actors state an armour class they do not have** | **STILL OPEN.** `heli`, `hind`, `mi28` say Medium and are Heavy; `lccv`, `mnly` say None and are Light. Balance-visible. |
| `[2026-08-30] [low]` **Stryker SHORAD fires the Bradley's chaingun; `30mm.Stryker` does not exist** | **STILL OPEN.** `vehicles-america.yaml:919`. The actor's name disagrees with its armament. |
| `[2026-08-27] [high]` ×5 — logistics/rearm economy | **NOT RE-DERIVED THIS PASS.** See `## Watch`. |
| `[2026-09-01] [med]` **Cargo's emergency bail-out has never run for any garrisonable building** | **STILL OPEN.** |
| `[2026-09-02] [MED]` **~60% of tournament losses credited to nobody** | **STILL OPEN** — same defect as item 87. |

---

# Part 2 — Weakest areas, by the axes the user named

Ranked within each axis by what a stranger meets, how early, how visibly.

## 2.1 Stability — **the axis with the least evidence, not the most defects**

| | Defect | Evidence | Severity |
|---|---|---|---|
| **S1** | **The 2-human desync has never been tested against any fix.** Four games desynced within seconds; one cause fixed (`91056894`), a second located and named but unfixed; the replay artifacts aged out and are gone. | `items/42-multiplayer-desync.md`; `items/54`; plumbing live at `OrderManager.cs:142-170` | **BLOCKER** — promoted by the 2026-08-16 audience ruling and never demoted. |
| **S2** | **Saved-game restore is RED on a second, independent leak** — located, named, not fixed, not re-measured since 2026-08-13. Net frame 711 / world tick 2130, actor `4712 at.america`: `Mobile.Facing` 256 vs 118. Still live on `@stable`. | HOTBOARD, cites re-checked at `554895ba`; `Activities/Move/AttackMoveActivity.cs:195-196`, `:236`, `:247`, `:260` | **BLOCKER** — a stranger who saves and reloads gets a divergent world. |
| **S3** | **A whole bug class was invisible to every gate until 2026-09-19.** `DefconWall` threw in `INotifyCreated.Created`, **every match failed to start**, and `all`, `check`, `dotnet test`, `test`, lua-gate and nav-guard were all green through it. | CLAUDE.md; guards landed at `ba6a6557` (smoke gate + `worldactor-gate`) | **Now mitigated, not closed** — the two new guards are themselves unexercised against a fresh instance of the class. |

**How we would know each is fixed.** S1: **only the user can settle it** — two humans, the user hosting (`EnableSyncReports` reaches clients from the host's lobby globals), and *copy the replays out of the run directory immediately*, because the last set aged out before anyone did. S2: a scenario that saves at a pinned tick and reloads, asserting `Mobile.Facing` parity on the named actor — this is agent-doable and has never been authored. S3: one `./make.ps1 smoke` against a deliberately-broken `INotifyCreated` (a RED control) — the guard has never been shown to fail.

## 2.2 Professionalism — **the cheapest axis, and it contains a defect nobody has filed**

| | Defect | Evidence | Severity |
|---|---|---|---|
| **D1** | **The main menu's info panel is wrong on three of its four lines, on every packaged release.** `"WW3MOD — Pre-Alpha"` is hardcoded on a tree that has shipped `v0.1.0/1/2`. `"Built: "` is **`DateTime.Now` evaluated when the menu opens** — a stranger sees today's date labelled as the build date, forever. `"Fork: "` renders `mod.yaml`'s `Version:`, which packaging **overwrites with the WW3MOD tag**, so the line describing the OpenRA fork point reads `v0.1.2`. | `MainMenuLogic.cs:279-282`; `mod.config:104 PACKAGING_OVERWRITE_MOD_VERSION="True"`; `packaging/windows/buildpackage.sh:106-108` → `engine/packaging/functions.sh:153-161` | **BLOCKER** — "immediately-visible *this is unfinished* signal", and the `Built:` line is not a signal but a falsehood. **NEW — in no tracker.** ⚠️ `AWAITING-USER.md` §1(b) reasons that `Version:` "is deliberately presented as the OpenRA release this forked from, **which is true**". That reasoning was done against the source tree and **is false on every install a stranger runs.** |
| **D2** | **The install chain still says OpenRA in five places.** Install dir, registry key, Start Menu folder, `$APPDATA\OpenRA\ModMetadata`, the desktop shortcut name, and `<Product>OpenRA</Product>`. Survived three verification passes untouched. | `mod.config:84, :88`; `buildpackage.nsi:78, 307, 317, 383`; `Directory.Build.props:17` | **BLOCKER** per the 2026-08-16 ranking; **the cheapest blocker in the file.** |
| **D3** | **Credits has three empty sections.** `MUSIC / (nothing here yet)`, `ART / (nothing here yet)`, `SOUND / (nothing here yet)` — on a reachable main-menu panel (`MainMenuLogic.cs:453`, `mainmenu.yaml:286`). | `mods/ww3mod/credits.txt` | **SHOULD-FIX** — the licensing prose above it is careful and good; the three holes undercut it. |

**How we would know.** D1/D2/D3 are all settled by **one packaged build plus one screenshot of the `v` panel** — no scenario, no play-through. D1 additionally wants a test that asserts the version label is not a literal.

## 2.3 Visual polish — **the identity assets are still entirely Red Alert's**

| | Defect | Evidence | Severity |
|---|---|---|---|
| **V1** | **The mod chooser shows Red Alert's icon.** `mods/ww3mod/icon.png` md5 `e9b6dc3d42d3f3e28d2747c69a1dd412` — **byte-identical** to `engine/mods/ra/icon.png`. Re-measured this pass. | disk | **BLOCKER** — literally the first image a stranger sees. |
| **V2** | **The startup screen has a hole where the logo goes.** `loadscreen.png` untouched since `1218bd90` *"Loadscreen, removed logo for now"*. | git log; pixel count relayed, not re-derived (see `## Watch`) | **BLOCKER.** |
| **V3** | **Every unit is drawn with a Red Alert sprite, by design, and one case is loud.** `sequences.yaml:208-213`: the `bradley` — a US IFV — resolves to `1tnk`, RA's Allied Light Tank. This is not a bug (`~conquer.mix` is mounted at `mod.yaml:22`) but it is the visible half of "still Red Alert". All 15 Russian cameos are md5-identical to their US twins (relayed from the 09-02 re-measure). | as cited | **SHOULD-FIX** — and it is the user's art work (U9), not a worker's. |

**How we would know.** V1/V2 by looking at the mod chooser and the startup screen once assets exist. V3 is not agent-settleable at all.

## 2.4 Performance — **measured precisely, in the one condition that is not a game**

| | Defect | Evidence | Severity |
|---|---|---|---|
| **P1** | **The nuke-perf rig's own README says it does not measure the thing a player feels.** Verbatim: *"Frame rate. This rig measures simulation ticks. It says nothing about whether the detonation looks smooth"*, and *"Anything about a real match. There are no bots, no production, no combat… the absolute numbers are a floor for what a live game would pay, not an estimate of it."* | `tools/nuke-perf/README.md:247-260` | **SHOULD-FIX** — the levers that landed (`4061796b`, `74c3f01d`) are real and byte-identical-verified; the open question is late-game cost, not detonation cost. |
| **P2** | **The salvo-vs-exchange A/B pair is outstanding.** HOTBOARD: the two endgame worktrees are held open *"once the post-push salvo-vs-exchange perf pair has run from `exchange-variants`' frozen Release build"*. It has not run. | HOTBOARD, stamped `df5a0fb4` | **SHOULD-FIX** — and it is one run. |
| **P3** | **Nothing measures a nuke during a populated late game.** The rig's map holds 448 stationary Russian actors and one US Supply Route 54 cells away. | `tools/nuke-perf/README.md:247-260` | **POLISH**, but it is the question the user's play-through will actually raise. |

**How we would know.** P2: one back-to-back pair, Profile A, `--hidden`, per the README's own recipe. P3: a new scenario is cheap — fire a Sarmat into an existing `tournament-*` map mid-match and read `tick_time` p50/max. P1: only the user, watching.

## 2.5 Documentation / information — **the strongest axis, with one hole**

The How To Play panel is accurate (R9 closed), reachable from both the main menu (`mainmenu.yaml` `HOWTOPLAY_BUTTON`) and in-game, and versioned so it shows once. README is genuinely good. `web/README.md` documents the release step. The gap:

| | Defect | Evidence | Severity |
|---|---|---|---|
| **I1** | **There is no hotkey list a player can read.** Nine hotkey declarations are unbound (R5) and 11 garrison/cargo buttons carry no key (R6), but the larger point is that nothing in the game enumerates the bindings that *do* exist. | `chrome/` inventory — no help/keys panel | **SHOULD-FIX.** |
| **I2** | **Nothing teaches the Escalation mode.** It is on the lobby dropdown (`DefconEscalation.cs:66`), the How To Play panel covers only the Supply Route economy, and the in-match readout is the only instruction that exists. | `ingame-info-howtoplay.yaml` | **SHOULD-FIX** — and it is downstream of §2.7's rulings; do not write copy for a mode still changing shape. |
| **I3** | **Production tooltips leak internal identifiers.** `5.56mm.DMR`, `TankRound.Abrams`, `HIMARSTargeter`. | `AmmoPool.cs:200-209` | **SHOULD-FIX**, one character. |

## 2.6 UI / chrome

| | Defect | Evidence | Severity |
|---|---|---|---|
| **U1** | **The production tooltip overlaps the sidebar and is not opaque** — portraits are legible *through* it. Every player meets this within seconds. | `bugs/discovered.md` 2026-08-30; `ProductionTooltipLogic`, `TooltipContainerWidget:154` | **SHOULD-FIX**, arguably BLOCKER on a wide display. |
| **U2** | **Infantry give no selection feedback at all** — box-select six riflemen, nothing changes. | `infantry.yaml:57 ShowNever: true`, the only occurrence under `mods/` | **BLOCKER if the `ShowNever` was accidental, POLISH if deliberate** — and nobody can tell by reading. Two screenshots settle it. |
| **U3** | **11 garrison/cargo buttons still carry no hotkey.** Labels and tooltips shipped. | `ingame-player.yaml:830-1269`, 0 `Key:` | **COSMETIC** — R5's verdict already downgraded this class. |

## 2.7 Gameplay — **Escalation and the nukes: what has never been verified**

**What HAS been verified**, so it is not re-litigated: the phase clocks derive from deployment
arithmetic and the shipped 5-minute default is the number that arithmetic produces (§1.1–1.4, pinned by
`DefconEscalationTest.ThePhaseClockDefaultsMatchTheDeploymentDerivation`); the fire-discipline rule keys
on the engine's own provenance test (`DefconFireDiscipline.cs:143-158`); borders derive or are authored
on all ten maps; and Skirmish is a strict no-op, pinned.

**Claims about Escalation and the nuclear ladder that NO scenario and NO play-through has ever tested:**

1. **That the DEFCON 2 cease-fire is a phase at all.** Measured: **98 ticks, 5.9 s**, against a 66-tick
   transition banner — *"the player gets one banner and a half"*. A controlled seeded pair **rejected**
   the cheap geometric fix (b1a made it **shorter**, 38 ticks, by concentrating fire). §B1 is closed with
   *"there is no geometric fix"*; §B6 (one combined banner) is the whole remedy and is unbuilt.
2. **That the nuclear ladder is ever climbed.** Measured: both bots fired **exactly once each**, both
   game-enders inside the Dead Hand final exchange; **6900 ticks of open nuclear phase with zero ladder
   warheads.** The 1/20/50/100 kt rungs, the 5/7/9/12-minute cooldowns and the three postures **were not
   exercised at all**. The review states the operational consequence plainly: *"bot-vs-bot runs cannot
   tune the ladder, the cooldowns or the postures, because an even match never reaches them."*
3. **That the bots do the thing the phase is named for.** Measured (R3): at the half-way tick **neither
   side had a single ground unit within twelve cells of either open crossing** — they spent Positioning
   capturing the map's fourteen neutral objectives. §B12.
4. **That an Escalation match opens with anything to position.** The default `Starting Units` is shared
   with Skirmish and is not `Motorized`. §B2. Never played.
5. **That three or more sides is survivable.** Unenforced; no border derives and a total hold-fire
   results. A stranger filling a 4-slot lobby hits it. §B3, and a `MEDIUM` bug entry.
6. **That hold-fire's six fire-path guards hold.** *"None … has a test"* — they sit in per-actor trait
   methods and nothing in `OpenRA.Test` can construct a `World`. Three scenarios are named as owed.
7. **That a revoked ladder band removes a power's cameo and buy-tab entry.** Reasoned, never observed.
8. **That the demo used to show the nukes off works.** It cannot fire 2 of its 6 warheads and has not
   been able to since the powers were faction-tiered.

**Skirmish** is the mode a stranger actually plays and it is the better-tested of the two — but its
weakest visible behaviours are the bot ones:

| | Defect | Evidence | Severity |
|---|---|---|---|
| **G1** | **The ambush lane takes units at the opening without consulting the offense floor** — item 86. The only tank walks 22 cells forward as half a pair and dies; the army never leaves the SR. Confirmed by run `260906_091912`. | `LaneAmbushBotModule.cs:385`, which says so in its own comment | **BLOCKER** per the bot ruling — "visibly-stupid behaviour a player would screenshot". |
| **G2** | **The combined-arms push is merged and switched off** — item 64. `ai.yaml:2258`/`:2353` both false. The first tank still outruns its infantry. | as cited | **SHOULD-FIX** — and the next action is a **flip and a run**, not a dispatch. |
| **G3** | **Supply trucks: the fix is in and ON, and the acceptance bar has never been taken** — item 56, the item's own tag reads *highest priority in the whole queue*. | `ai.yaml:1754`, `:1652` | **SHOULD-FIX** — one bot-vs-bot match settles it. |
| **G4** | **A human clicking Ambush gets a different feature from the bots** — item 68, the ambush programme's headline, and `defaults.yaml:421-429` states the asymmetry in its own comment. | as cited | **SHOULD-FIX** — but **user-gated**; nothing in that block may be implemented. |

**How we would know.** G1: `test-push-departs-together` already exists and ships `expected-status: fail`
— a RED→GREEN pair on it settles both G1 and G2's residual. G3: one match with the `[composition]
census` precondition. §2.7 items 1–8: five are scenario-shaped and agent-doable; **2, 4 and the feel of
the whole mode are play-through questions only.**

---

# Part 3 — Proposed plan

Ordered. One subsystem per package. **"Launch"** = the manager runs a scenario; **"capture"** = a
screenshot pass per `DOCS/recipes/SCREENSHOT.md`; **"user"** = cannot be closed without the user.

| # | Package | What the player sees change | Files | Measurement | Size | Needs |
|---|---|---|---|---|---|---|
| **1** | **Release identity panel** | The `v` panel stops claiming Pre-Alpha, stops showing today's date as the build date, and stops labelling the WW3MOD tag as the OpenRA fork point. | `MainMenuLogic.cs:279-282`; a new `OpenRA.Test` assertion that the version label is not a string literal | one packaged build + one screenshot | **S** | capture; **one user word** (the version string — `AWAITING-USER` §1) |
| **2** | **Install chain de-OpenRA-ing** | Installs to a WW3MOD folder, a WW3MOD Start Menu entry, a WW3MOD desktop shortcut, a WW3MOD registry key. | `mod.config:84,88`; `packaging/windows/buildpackage.nsi:78,307,317,383`; `engine/Directory.Build.props:17` | build one installer and read the five strings | **S** | no launch. ⚠️ **One-way door for existing installs** — changing the registry key and install dir orphans v0.1.x installations; that is a deliberate call, not a cleanup. |
| **3** | **Tooltip legibility** | The production tooltip stops showing the sidebar through itself, stops printing `TankRound.Abrams`, and gives single-pool units an ammo total. | `ProductionTooltipLogic.cs:156,159,208`; `AmmoPool.cs:200-209`; `TooltipContainerWidget:154` | before/after screenshot at two resolutions | **S** | capture |
| **4** | **Infantry selection feedback** | Box-selected riflemen show a bracket — or they provably should not. | `infantry.yaml:57` | **two screenshots, side by side, shown to the user** | **S** | capture; **user picks** |
| **5** | **Credits + the U9/U4 hand-off** | Credits stops saying "(nothing here yet)" three times; the art/audio asks move to `AWAITING-USER.md` where the user can act on them. | `credits.txt`; `AWAITING-USER.md`; locate-or-write the U4 icon table | desk only | **S** | none |
| **6** | **§B6 — the Escalation signature moment** | When the border opens and the first casualty lands inside the banner window, the player gets **one** banner naming both, plus an event-log line naming who chose. Today they get a banner and a half. | `DefconEscalation.cs` banner path; `DefconReadoutModel.cs` | a scenario asserting one banner where two edges land inside `BannerHoldTicks` | **M** | launch |
| **7** | **Escalation's three unenforced/untested guards** | A 3+ side Escalation lobby refuses to start rather than producing a no-border total hold-fire; hold-fire's six fire-path guards get their three scenarios. | `DefconWall.cs:352-395`; `NuclearExchange.cs:764-768`; lobby refusal; 3 new scenarios | RED+GREEN per scenario | **M** | launch |
| **8** | **The bot opening — items 86 + 64** | The opening push is a formation, and the lone tank stops walking 22 cells forward to die. | `LaneAmbushBotModule.cs:385` cross-gate; flip `ai.yaml:2258`/`:2353` | `test-push-departs-together` RED→GREEN, then one match | **M** | launch. ⚠️ **Moves `@stable`** — say so in the commit and re-take the baseline. |
| **9** | **Item 56's acceptance bar** | Nothing changes; we find out whether the shipped fix works. | none — this is a run | one bot-vs-bot match on `tournament-s1-eco-river-zeta`, precondition `[composition] census` with `earned>0` and non-zero `truk`, then `crate-placed ÷ drop` vs 15.0% | **S** | launch |
| **10** | **Saved-game restore leak** | Save and reload stops producing a divergent world. | `Activities/Move/AttackMoveActivity.cs:195-196,236,247,260` | a save/reload scenario asserting `Mobile.Facing` parity — **this does not exist and is the missing instrument** | **L** | launch |
| **11** | **The nuke perf pair + a populated-map nuke** | We learn whether a nuke in a real late game stutters. Today nothing measures that. | `tools/nuke-perf/`; one new scenario on a `tournament-*` map | Profile A back-to-back pair; then `tick_time` p50/max mid-match | **M** | launch |
| **12** | **`demo-nuke-arsenal` + the nuke demos** | The demo used to show the nukes off fires all six warheads. | `demo-nuke-arsenal.lua` `SHOTS`; a Russia seat or a Russia client | run the demo, count six detonations | **S** | launch |
| **13** | **The `Versus` table repair (item 62)** | Iskander and HIMARS stop taking 100% against three armour classes they were meant to be scaled against, and stop carrying a zero for an armour class the mod does not have. | `weapons-missiles.yaml:394-403` | combat-sim per `BALANCE.md` | **S** | ⚠️ **balance change — goes through item 32's sign-off flow** |
| **14** | **Item 78's free arithmetic** | Nothing — this is the study that decides whether item 78 exists at all. | spawn/bounds arithmetic over the 10 maps | if the nearest edge is usually the owner's own, **drop item 78** | **S** | no launch |
| **15** | **In-game hotkey reference** | A player can read what the keys are. | a new chrome panel; `ingame-player.yaml:830-1269` gets the 11 missing keys while we are there | capture | **M** | capture |

**Do NOT do now**, and each for a stated reason: item 40 (open-ended rework, explicitly not release-gating);
item 43's re-baseline (it will be invalidated again by package 8 — take it *after*); items 67–71
(user-gated, nothing may be implemented); items 72/73/83 (post-release by the user's own ruling);
item 90 (sequencing is load-bearing and the shellmap must come first); R4's rename (user ruled *leave it*).

## Questions only the user can answer by playing

1. **Is a cold war that stays cold the drama, or an anticlimax?** (§B11.) An even match reaches the
   nuclear phase and declines it for seven minutes. If you want matches to reach the exchange, the levers
   are `NuclearBotModule`'s `LosingArmyRatioPercent` (60) and `LosingStreakRequired` (3) — not the mode.
2. **Does the DEFCON 2 cease-fire feel like a phase, or like a flicker?** Measured at 5.9 s. §B1 is closed
   on "there is no geometric fix". If it still reads wrong in play, §B6 is the only remaining answer.
3. **Do the nuke yields, the cooldowns and the three postures feel right?** Nothing has exercised them
   and nothing can — this needs an asymmetric match or you.
4. **Should an Escalation match open with `Motorized` starting units?** (§B2.) It is the mode's first
   impression and it is currently whatever Skirmish's default is.
5. **The post-cascade whiteout scale and the 16-tick relight cadence** — HOTBOARD flags both as
   *"open for the user's in-game look, not for more measurement"*.
6. **Does a nuke stutter in a real late-game match?** The rig says it cannot answer this.
7. **The version string** (`AWAITING-USER` §1), **the case-01 bar** (§4), **the HIMARS `Thickness`
   omission** (§3), **splash art** (§5) — four one-word answers that unblock four items.
8. **Infantry selection brackets** — package 4 produces two screenshots; you pick.
9. **Two new rulings not yet in `AWAITING-USER.md`:** the pre-captured band-only rule (all 90 eligible
   structures now get an owner; zero stay neutral) and whether the editor's Zones tool is the border-
   authoring path going forward.

## One-way doors

- **Package 2** — changing the Windows registry key and install dir **orphans existing v0.1.x installs**.
- **Any rename of `brics`** — `player.brics`, `sidebar-brics`, `FactionSuffix-russia: brics` are live
  identifiers. CLAUDE.md forbids it. **Nothing in this plan touches them.**
- **Tag/version bumps** — `web/latest.txt` must be bumped *after* tagging or everyone on the previous
  release is told they are current (`web/README.md`).
- **Asset deletion** — nothing in this plan deletes an asset. `mods/ww3mod/icon.png` and
  `loadscreen.png` are **replacements**, not removals.
- **`mod.yaml:3 Version:`** — changing it moves the multiplayer rules hash, hides every existing replay
  from the in-game browser, and orphans the launcher registration key. Package 1 deliberately fixes the
  *label*, not the value.

---

## Watch

**What I could not settle by reading, and where I may be wrong.**

- **The loadscreen pixel claim is relayed, not re-derived.** I confirmed `loadscreen.png` has not been
  touched since `1218bd90` *"Loadscreen, removed logo for now"*, which is strong circumstantial evidence,
  but PIL is not installed on this machine and I did not count the alpha channel. The "0 of 65536" figure
  is the 09-02 measurement, carried forward. Same for the 15/15 Russian cameo md5s — my filename probe
  found no America/Russia cameo pair to compare, so I could not re-measure; the 09-02 figure stands
  unverified by me.
- **U4's deliverable may not exist.** The deferred-polish ruling names "the duplicate-map table (19 of 25
  buttons share art across 11 sprites; 14 new icons needed)" as the thing to produce, and says the
  content-completeness audit *is* that document. **Grep for those figures in
  `audit/260816-content-completeness.md` at this ref returns nothing.** Either the table lives somewhere
  I did not look or it was never written. Do not dispatch package 5's U4 arm until that is settled.
- **I did not re-derive the five `[high]` 2026-08-27 logistics/rearm entries** (free rearming at the
  Logistics Centre, the docking-host affordability regression, the dual-arm `himars`/`iskander` service).
  They are the largest untouched cluster in `bugs/discovered.md` and they bear on the economy, which is
  the mod's central system. If one of them is still live it probably outranks several things I ranked
  above it.
- **Everything in §2.7 comes from one document.** `escalation-gameplay-review-260919.md` is careful — it
  reverses its own recommendation twice and closes §B1 on a controlled seeded pair — but its five runs are
  single samples, its author explicitly did not launch anything, and I did not re-run any of them. The
  98-tick figure, the 6900-tick ladder silence and the R3 census are that document's readings, not mine.
- **I did not verify that any of the 15 packages builds.** No build, no launch, no lint was run in this
  pass, by instruction. Every size estimate is from reading the call sites, not from touching the code.
- **The "no test pins the version strings" claim is a negative from one grep.** I searched
  `engine/OpenRA.Test/` for `INFO_BUILD_DATE` and `Pre-Alpha` and found nothing, which is how I concluded
  package 1 needs a new assertion. A test asserting it by a different name would not have shown up.
- **`bugs/discovered.md` has no OPEN/CLOSED convention.** I classified 60 entries by reading their
  headers for a `FIXED`/`RETRACTED` marker. An entry fixed in a commit that never updated its entry
  reads as open here, and at least one — the `bradley` sprite — turned out not to be a defect at all once
  I checked the thing its author declined to check. Assume the same is true of others I did not open.
