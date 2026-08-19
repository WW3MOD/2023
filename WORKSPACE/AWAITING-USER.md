# AWAITING-USER — what actually needs the user

> **Purpose:** the single place where everything **dependent on the user's decision, review, or grant** is parked. The manager adds items; the user resolves them in chat or by editing this file. Anything resolved moves to [RESOLVED](#resolved) with the ruling recorded — nothing is deleted.
>
> **The top section is the whole point of this file.** If it is long, it is wrong. Everything below `NEEDS YOU` is history.

> **Re-reconciled 2026-08-20 against `main` @ `a0cb877d`**, verified against the code, not against other workspace docs. **Of the 8 carried, 5 need you, 2 closed, 1 demoted to a manager call.** Three of the five changed shape today and are restated rather than carried — do not answer the older wording.
>
> Prior reconciliation (2026-08-19 @ `de78a1ed`) cut 27 items to 8, on two rulings that still hold:
>
> 1. **Full simulation/launch grants, 2026-08-19** *(decision record `.../manager-850d8885/decisions/06`)* — user: *"You have full grants to launch simulations but I suggest you do it from here so that multiple workers are not all starting simulations."* **Every "needs a run grant" item in this file is therefore no longer a user gate.** It is manager scheduling.
> 2. **Release ranking, 2026-08-16** *(decision record `.../manager-ed57f2e0/decisions/01`)* — the user chose **public release to strangers**, and rank is now *what a stranger encounters, how early, how visibly*.

---

## ANSWER SHEET — the whole file, in five answers

Reply with just the numbers if the recommendations are fine. Every one of these is reversible; none is a one-way door.

| # | The question, in one line | Recommended answer |
|---|---|---|
| **1** | What should the game call its own version on the menu? | **"Beta"** |
| **2** | Should we stop borrowing OpenRA's public identity? | **Pin the version-check, set our own website URL, keep using their server list** |
| **3** | Iskander outclasses HIMARS at the same price — fix which end? | **Fix the HIMARS armour omission first (it's a bug), then re-measure before touching cost** |
| **4** | Ratify the pass/fail numbers for the forest-ambush test? | **Yes, as measured** |
| **5** | Build a full-screen splash image? | **No — put the logo in the slot that already exists** |

**If you answer nothing at all:** the mod ships announcing itself as "Pre-Alpha", pointing strangers at openra.net as its homepage, with a known armour bug on one US unit, one test that cannot be trusted, and an empty hole where the startup logo goes.

---

## NEEDS YOU

Ordered by how much it blocks a public release. Each says what happens if you say nothing.

### 1. Release version string — **the first thing a stranger reads**

> **The decision:** *what should the game call its own version when a new player clicks the `v` button on the main menu?* Right now it says **"WW3MOD — Pre-Alpha"**.
>
> | Option | What it costs | What it signals |
> |---|---|---|
> | **"Beta"** ← recommended | nothing — one C# string | honest while the art slots are still empty; unremarkable for an indie mod |
> | **"1.0"** | nothing | strongest signal, but claims finished |
> | **"0.9"** | nothing | numeric without claiming 1.0 |
> | leave "Pre-Alpha" | nothing | tells a stranger not to bother |
>
> **Why Beta:** it is the only value that is *true*. The remaining gaps are real (empty startup logo, one music track, placeholder installer icon — see the art item) and a stranger who reads "1.0" and then meets those gaps trusts the next claim less. Beta costs you nothing to upgrade later.
>
> **This is a one-word answer.** Everything below is the detail for whoever edits it, not for you.

**These are two independent strings and only one is cheap; earlier framings of this item ran them together.** Re-verified at `a0cb877d`:

**(a) What the player actually reads — free to change, and it is ONE line, not two.** "Pre-Alpha" is **not in `mod.yaml` at all**. It is two hardcoded C# literals, but **only one of them is reachable**:
> - `MainMenuLogic.cs:280` — `"WW3MOD — Pre-Alpha"`, the main-menu **`v`** dropdown (`mainmenu.yaml:87` `INFO_BUTTON`, `:101` panel, `:109` label). **This is the live one — the only place a player ever reads the string.** Note it is `Visible: false` until the player clicks **`v`** (`mainmenu.yaml:107`), so it is not on screen at rest — the adjacent **`i`** button is the how-to-play briefing, a different thing.
> - `ModInfoPanelLogic.cs:22` — `"Version: Pre-Alpha"`. **Dead.** Its root widget `MOD_INFO_PANEL` occurs exactly once repo-wide — its own declaration at `info-panel.yaml:1`. Nothing opens it; the file is listed in `ChromeLayout` (`mod.yaml:204`) so it is parsed, never instantiated. Its logic ctor also demands `Action onExit, string shellmapName`, which no caller supplies — so it could not be opened as a plain child even by accident.
>
> Changing the live literal touches **no yaml**, so it does **not** move the multiplayer rules hash. *Trap, and it runs the opposite way to the obvious guess: someone who "fixes the version string" by editing `ModInfoPanelLogic.cs` changes **nothing on screen** and will believe it is done. Edit `MainMenuLogic.cs:280`. Decide separately whether the dead panel is wired up or deleted — it is not part of this decision.*

**(b) `mod.yaml:3` `Version: release-20230225` — not free, and arguably already correct.** The live panel renders it as **"Fork: "** + the value (`MainMenuLogic.cs:281`; the dead panel does the same at `ModInfoPanelLogic.cs:23`) — it is deliberately presented as *the OpenRA release this forked from*, which is true, and is a normal thing for a total conversion to state. It is also a deliberately frozen literal: `Server.cs:541` and `Handshake.cs:48` both record that its compatibility job was **superseded by `BuildFingerprint`**. Touched exactly once ever (`4894008b`, 2023-03-19). Changing it costs three concrete things — (i) `mod.yaml` is hashed verbatim into the rules segment (`BuildFingerprint.cs:310-313`), so it moves the multiplayer hash for everyone; (ii) **every existing replay disappears from the in-game browser**, which reads only `Replays/ww3mod/<Version>/` (`ReplayBrowserLogic.cs:145`); (iii) it orphans the launcher registration key `ww3mod-release-20230225` (`ExternalMods.MakeKey`).

**Recommended: do (a) only and leave (b) alone** — that removes the "unfinished" signal at zero risk while keeping an accurate fork marker. If you want (b) changed too, say so explicitly and accept (i)–(iii).

**If you say nothing:** a public release ships announcing itself as Pre-Alpha.

### 2. Public identity — three places the mod still presents itself as OpenRA

> **The decision:** *how much of OpenRA's public identity do we keep borrowing?* Three separate things, one answer each. **Recommended: yes / yes / no.**
>
> | | The thing | Recommended | Consequence of doing it |
> |---|---|---|---|
> | **(a)** | The mod tells strangers its homepage is **`https://www.openra.net`** | **Change it** — give me a URL, or say "no site yet" and I remove it | Free. Not hashed as a rules change. |
> | **(b)** | The mod asks **openra.net** whether it is up to date | **Pin it to a no-op** | One `mod.yaml` line. Moves the multiplayer rules hash — everyone must be on the same build, which they are anyway pre-release. |
> | **(c)** | The mod lists its games on **openra.net's server browser** | **Leave it** | Changing it means running our own master server. Not worth it before anyone is hosting games. |
>
> **Why (a) matters more than it looks:** that URL is not decoration. `GameServer.cs:226` publishes it as `ModWebsite` **into the public server listing**, and `DiscordService.cs:169` uses it for rich presence. A stranger who finds a WW3MOD game in the browser is told the game's website is openra.net. `mod.yaml:4` already carries a `TODO(release)` saying exactly this and waiting on you to pick a URL.
>
> **Why (b) matters:** if upstream ever answers `unknown` for a mod it does not recognise, every WW3MOD player gets a dialog reading *"You are running an unrecognized version of OpenRA. Download the latest version from www.openra.net"* — OpenRA's name and OpenRA's download link, in front of your players, triggered by a change on someone else's server. Today it is silent only by luck: `WebServices.cs:50` starts at `Latest` and only downgrades on a *recognised* reply, and upstream currently returns an empty body (`WORKSPACE/audit/260816-netcode.md` **N7**).
>
> **Why not (c):** the alternative is hosting infrastructure, and nobody is hosting games yet.

**Premise correction, 2026-08-19 (`d7279968`) — an earlier version of this item had it backwards.** It is **not** true that WW3MOD fails to advertise. All six `WebServices` fields are `readonly string`s **pre-initialised to openra.net URLs** (`WebServices.cs:20-25`), and `Manifest.Get<T>()` lazily constructs them when no yaml node exists. **No `mod.yaml` in this repo declares the block — not ww3mod, and not stock `ra`/`cnc`/`d2k`/`ts` either.** Relying on the defaults *is* the stock arrangement, so WW3MOD already advertises and already reads the list. **Do not let this be re-raised as "multiplayer is broken".** The reason no games are visible is that nobody is hosting one.

An adjacent decision was already taken *around* this and left it alone deliberately: `languages/en.ftl:36` records a choice **not** to reword the consent dialog to say "optimize WW3MOD", precisely because the payload really does go to `master.openra.net`.

**If you say nothing:** the mod keeps advertising there, keeps naming openra.net as its homepage in the public server list, and the `VersionCheck` exposure stays open.

### 3. Iskander vs HIMARS — **restated; the shape changed on 2026-08-19**

> **The decision:** *Russia's rocket artillery beats America's at the same price. Which end do we touch — and do we fix the bug underneath it first?*
>
> **Do not answer the old version of this question.** It asked you to approve "Iskander 6000 → 8000". A re-audit (`890b2b54`) found the gap is **larger** than filed *and* that part of it is a **HIMARS authoring bug**, so raising Iskander's price now would be paying to hide a defect.
>
> | Option | What it does | Consequence |
> |---|---|---|
> | **A — fix the HIMARS armour omission, alone, then re-measure** ← recommended | HIMARS is the only mobile combat vehicle in the mod that declares an armour type with **no thickness value** (`vehicles-america.yaml:1026-1027`). Its peer M270 has `Thickness: 8`; Iskander has `15`. Thickness `0` makes the engine **skip armour reduction entirely** (`DamageWarhead.cs:217`), so HIMARS eats 100% of every hit. | Smallest possible change. It may close most of the gap by itself, at which point there is nothing left to rule on. |
> | **B — raise Iskander to 8000–9000** | The original proposal. | Prices Russia out of a unit rather than fixing why it wins. Confounds a bug with a balance call. |
> | **C — converge the two warheads** | Iskander does **1.63×** the direct damage and covers **7.23×** the blast area against an MBT. | Biggest gameplay change; the two units stop being distinct. |
>
> **Why A first:** you cannot tell how much of "Iskander dominates" is design and how much is a missing field, until the field is there. A is a bug fix that happens to be a stat change — which is why it still needs your word under the no-stat-changes rule. **If you approve A, nothing else happens until it is re-measured and brought back to you.**

Verified at `a0cb877d`: Iskander `Cost: 6000` / `HP: 10000` / `Light, Thickness: 15` (`vehicles-russia.yaml:912,928,931`); HIMARS `Cost: 6000` / `HP: 6000` / `Light`, **no `Thickness` node** (`vehicles-america.yaml:1009,1025,1026-1027`). Full audit: [`balance/260819-strike-shorad-parity.md`](balance/260819-strike-shorad-parity.md). The older `002-himars-iskander-parity.md` is **superseded** and now carries a banner saying so.

> **The obvious objection was checked, because in MiniYaml a missing field is usually inherited rather than absent.** It is genuinely absent here. HIMARS inherits `^Combatant`, `^WheeledVehicle`, `^GainsExperience`, `^AutoTargetGroundAssaultMove`; **no `Thickness` node exists anywhere in `rules/ingame/vehicles.yaml` or `rules/defaults.yaml`**, and the engine's own default is `public readonly int Thickness = 0` (`Armor.cs:24`). **HIMARS is the only actor in either faction's vehicle file that declares `Armor` without `Thickness`** — every other one names a value (humvee 10, m113 15, bradley 15, abrams 700, m109 10, m270 8, strykershorad 15; no Russian vehicle omits it). A commented-out actor further down the same file (`:1170-1173`) even preserves the full idiom, `Thickness: 8` plus `Distribution`. That is what makes this read as an omission rather than a choice — but it is your call, not mine.

**Not a factor, so you are not being asked about it:** a separate audit (`7ec36b8c`) established that `Penetration` is a **linear damage scale, not a gate** (`DamageWarhead.cs:224-230` — under-penetrating still deals `damage × penetration ÷ thickness`), and that the widely-quoted "166 of 236 warheads left at the default" was both **miscounted** (correct: 167 of 238) and **irrelevant**, because 297 of 358 actors have `Thickness: 0` and skip the mechanism entirely. It yielded **zero new defects**. Bulk-"fixing" those 167 would silently add ~15–20% damage against every armoured target mod-wide. **This is a red herring for shipped balance and should not be reopened.**

**If you say nothing:** a known armour omission ships on a US unit, and Russia keeps a free upgrade over the US equivalent.

### 4. Case-01 test bar — **restated; it now needs a yes/no, not numbers**

> **The decision:** *the forest-ambush test finally has real pass/fail numbers. Ratify them?*
>
> **Do not answer the old version of this question.** It asked you to supply X/Y/N because the bar was ill-posed. Two things changed on 2026-08-19: the numbers were mined from the one real measurement that exists, and the test was found to be **incapable of failing at all** — its scenario called `Test.Pass` unconditionally (`78a44c90`), so every "green" it ever reported was meaningless. `e14dced3` gave it teeth.
>
> | Option | Consequence |
> |---|---|
> | **Ratify as measured** ← recommended | Defenders must lose **0** on every seed; across ≥6 seeds, mean defender loss ≤ **50cr** and mean attacker loss ≥ **300cr**. Test becomes trustworthy; manager then runs one sabotage (RED) and one confirmation (GREEN) batch. |
> | Loosen the attacker floor | Fewer false failures on the noisy axis; weaker test. |
> | Ask for a fresh calibration batch first | Costs one 6-seed run before anything can be ratified. |
>
> **Why ratify as measured:** the numbers are not invented. They come from the 2026-07-28 six-seed batch, and the audit that found the test toothless *also* checked the calibration and recommended keeping it unchanged — the defender clause has room for roughly three defender deaths across a whole batch, and the noisy attacker axis is deliberately kept soft and non-per-seed.
>
> **A correction that runs in your favour:** the bar was originally set against a recon (`recon/260728-trees-concealment.md`) that got **prone bonuses and tree-cover thresholds both wrong, in the same direction** — it made hiding look harder than it is (claimed ~7 dense tree cells to conceal a Vision-3 infantryman; the real figure is 4 at point-blank, 2–3 at range, and prone *does* grant concealment). That recon is retracted in place and superseded by `recon/260819-infantry-visibility-stances.md`. **The error did not corrupt the bar** — the batch measured detection directly rather than deriving it — and correcting it *widens* the defenders' margin. So the numbers are, if anything, conservative.

Thresholds live at `tools/autotest/parse-case01-bar.py:33-36` (batch-level) and `tools/autotest/scenarios/test-case01-forest-ambush/test-case01-forest-ambush.lua:18` (per-seed). The parser refuses to report green on an undersized or duplicated-seed batch — it reports UNEVALUABLE rather than passing.

**If you say nothing:** the test keeps its committed numbers but nobody has agreed to them, so a future red result gets argued with instead of acted on.

### 5. Splash / menu art direction — **narrowed; the art deferral does not cover it**

> **The decision:** *do you want a full-screen splash image at startup, or is a logo in the existing slot enough?*
>
> | Option | What it needs from you | Consequence |
> |---|---|---|
> | **Logo only, no new chrome** ← recommended | one 256×256 logo (three sizes) | The startup screen stops having a **hole** in it. No code change. |
> | Full-bleed image behind the load screen | art **plus** a chrome code change | There is **no code path for a full-screen image anywhere in the mod** — `LogoStripeLoadScreen` draws a 256×256 logo, a gray bar and text. This is engineering, not an asset drop. |
> | Static background on the main menu, losing the shellmap | art plus code | The menu currently renders a **live playing map**. Replacing motion with a still tends to read as *more* amateur, not less. |
>
> **Why logo-only:** the logo work is needed no matter which way you answer — `pipeline/items/46-release-art-audio.md` says so explicitly. Today `loadscreen.png`'s logo area is **0 of 65536 non-transparent pixels**: `1218bd90` ("Loadscreen, removed logo for now") emptied it and it was never restored. Filling the slot that already exists fixes the visible defect; building a new slot is a separate, larger job you can still choose later.

**⚠️ The 2026-08-16 art deferral does *not* close this, and I am flagging that rather than assuming it.** You said of the art/audio TODO lists: *"you can skip it fully now… just document it as a standing todo pre-release."* That deferral is about **producing assets for slots that already exist** (logo, installer icon, Russian cameos, music) — and it is discharged: the standing document is `pipeline/items/46-release-art-audio.md`. This item asks something different — whether to **build a new slot**. If you meant the deferral to cover this too, say so and it closes.

**If you say nothing:** startup shows a gray bar with an empty hole where the logo should be.

---

## COMING BACK TO YOU LATER — not open now, but will be

- **Missile miss-detonation rule, per weapon class.** You deliberately declined to pick a single global rule, so the class taxonomy (SACLOS wire-guided / fire-and-forget / top-attack / AA / cruise) plus a rule per class is a **Phase 2 deliverable owed back to you for agreement**. Already settled and not up for re-litigation: AA missiles are exempt and may fly on until fuel-out.
- **Sweep outcomes.** The Item-31 aggressiveness sweep, the Fires P2/P3 enablement sweep and the `@stable` re-baseline ladder are now manager-schedulable (see the launch grant). What comes back to you is the **ship value** each sweep argues for, not permission to run it.
- **Item-24 repoint gates — moved here 2026-08-20; there is nothing to ratify yet.** *(Was NEEDS YOU item 7.)* It asked you to accept, or overrule, a **KEEP OFF** recommendation while the gates ship **ON** in both profiles (`rules/ai/ai.yaml:199,2102` `StrategicCaptureRepointEnabled: true`; `:816,2207` `DefendRepointEnabled: true` — re-verified at `a0cb877d`, unchanged since 2026-07-29). **The recommendation rests on a void measurement.** The A/B (`ai-bench/runs/260729_item24_ab_result.md`, 40 matches, 2026-07-29) ran on the `tournament-*` configs, and those are the same matches this file already records as void: `PlayerResources.Tick` gated income *and* upkeep on `Playable`, which map-player bots are not, so **both bots had no economy in any of them**. Fixed since, at `20aa5a8a` / `b91b5a88`. Byte-identical arms prove the gates **never fired in those matches** — not that they never fire; a bot with no money builds little and captures less, which is exactly the condition that would starve the feature of opportunities. So: no evidence either way, and asking you to ratify a void number would waste your attention. **Re-measure on the fixed instrument, then bring back the ship value** — same shape as the sweeps above. Invisible to a player either way.

---

## RECORDS — proceeded past; do **NOT** re-ask

> Each was asked, went unanswered, and **the agent proceeded**. They are **overridable records**: you can still redirect any of them, but re-asking spends your attention on a decision already made in code. Do not promote these into NEEDS YOU, and do not reword them into a fresh question.

- **Tactical-layer default for humans** *(posted 2026-08-04)* — auto supply-seek / OOA evac **ON by default** for human units, stance-disableable. Shipped as `AutoSeekSupplies` (`f15cfbde`). _Moved here 2026-08-19: the 08-11 reconciliation kept it open on the principle that "proceeding on a default is not the user deciding." That is true, but it is the definition of a record, not of a gate — it has shipped and been played since. Redirect reverses it in one line._
- **OOA fallback** *(posted 2026-08-04)* — vehicle out of ammo with no reachable rearm source: **terminal evac + sell** (conf 80). Bot-module-only; human units stay player-controlled by deliberate design. _Moved here 2026-08-19, same reasoning. **Updated 2026-08-20:** this used to carry a warning that "no reachable rearm source" was permanently true for aircraft. The 2026-08-19 rearm ruling (see RESOLVED) makes that the **designed** state rather than an accident, and gives it defined behaviour — `EvacuateWhenUnrearmable` flies the airframe off the map edge. Human-owned aircraft are excluded from the bot-side sell, consistent with the rule above._
- **How a supply truck decides a delivery is too dangerous to drive in** *([`closeout/bdedd544.md`](closeout/bdedd544.md) §3, which says explicitly "Do not re-ask or reword")* — resolved in code as a **two-limb classifier**: a cell stands out against the player's own live median **OR** exceeds an absolute figure, **plus a floor so a quiet field reads quiet**. Both limbs are required: purely relative classified a cell reading **462,272 as safe** (a saturated field has an enormous median); purely absolute is how the original thresholds broke.
- **One autotest run on the instrumented order log** (`Test.Mode=true Test.UnitLifecycleLog=<path>`) — never answered, agent proceeded. **No longer needs asking at all** under the 08-19 launch grant; still the cheapest way to rank churn sources empirically.
- **Whether to chase the content-divergence desync theory** *([`closeout/54ab3880.md`](closeout/54ab3880.md) §3)* — never answered, and now largely **moot**: superseded by the `Detectable` condition-token finding, and a real 2-human desync capture has since been taken (both sync reports at `WORKSPACE/audit/logs-260816-snapshot/Logs/`, divergence net frame 1264 / tick 3792, actor `4617 e3.russia`), which ruled out content divergence directly.
- **An autotest run at the real 3840 helicopter altitude** — asked, unanswered, **moot**: superseded when you rescoped the missile work.

---

## RESOLVED

### Closed 2026-08-20 — re-reconciliation against `main` @ `a0cb877d`

**Answered by the user:**

- **Where do aircraft rearm and repair? — RULED 2026-08-19, and the ruling is shipped.** *(Was NEEDS YOU item 1.)* Verbatim: *"Airplanes uses the airfield, helicopters use helipad, if those do not exist they must evacuate (They cannot be rearmed in that case). Airplanes are not in the game now, and probably wont be either so no need to look into that, but helipads should be possible to use to rearm helicopters, if a helipad exists (Cannot be built in this mod, can only be used if one exist on a map as a neutral/capturable structure)."* Recorded at [`DOCS/reference/economy.md:39`](../DOCS/reference/economy.md).
  **Three things this ruling settles, each of which had been filed as a defect:** (1) the seven airframes keep naming `hpad`/`afld` — **do not repoint them at `logisticscenter`**; (2) the `~disabled` build prerequisite on HPAD/AFLD is **correct and intended**, not a gap — they are map-placed capturables, not buildables; (3) **the absence of a host is not a bug** — it has defined behaviour. The worker fix that made HPAD/AFLD reachable was merged (`1db6514f`) and then **reverted at `68e8b885`** precisely because it implemented a design the user had not chosen. The ruling shipped instead as `EvacuateWhenUnrearmable` (`6242c63b`, merged `89539ff9`), which flies an out-of-ammo helicopter off the map edge when no host exists. Bot helicopters are deliberately excluded (`IncludeBotOwners: false`) so it cannot fight `HelicopterSquadBotModule.EvacuateWhenIdle`.
  *Residual, and it is not a user question:* HPAD has no world sprite (`hpad.shp`/`hpadmake.shp` are in `lint-baseline.txt`), so no map can place one until that art exists. That is art production, tracked under the standing art item.

**Premise inverted — the question was resting on a number that does not mean what it looked like:**

- **Tunguska ↔ Stryker SHORAD parity — CLOSED, no ruling needed.** *(Was NEEDS YOU item 5.)* The item read *"Tunguska is 1700 cost / 8000 HP against strykershorad's 2500 / 14000 — cheaper **and** 43% less durable"*, and asked you to sign off an asymmetry nobody had chosen on purpose. **That comparison omitted armour, and including it reverses the conclusion.** Verified at `a0cb877d`: Tunguska `Thickness: 19` (`vehicles-russia.yaml:789-791`) against strykershorad's `15` (`vehicles-america.yaml:852-854`), so durability per credit is **89.4 vs 84.0 — the Tunguska is the *more* cost-efficient survivor**, not a 43% weaker one. It also out-damages the SHORAD in every target class (≈6.8× vs infantry, ≈6.3× vs helicopters, ≈2.1× vs MBT) at 68% of the price. The SHORAD's extra 800 credits buy things the Tunguska has none of — 9 infantry seats, a Hellfire AT missile, and ~8× magazine endurance. **These are two units doing two different jobs at two different prices, which is what a faction difference is supposed to look like.** No stat change proposed; nothing to approve. Audit: [`balance/260819-strike-shorad-parity.md`](balance/260819-strike-shorad-parity.md) (`890b2b54`).
  *Kept because it is the useful half:* the same re-audit found the **Iskander/HIMARS** gap is real and **larger** than filed, and surfaced the HIMARS armour omission underneath it. That is NEEDS YOU item 3, restated.

### Closed 2026-08-19 — prior reconciliation

**Dissolved by the full launch grant of 2026-08-19** — none of these is a user decision any more; all are manager scheduling:

- **`test-autotarget-preempt-air` RED+GREEN pair** — **the runs were already spent, on 2026-08-12 (`f910ac7d`), and the result is the item's real news: BOTH PASSED.** The RED control (`PreemptScanInterval` pinned to 0, verified applied via a trace, not assumed) was supposed to fail and did not. **So this test does not discriminate the fix at all** — the unaided break beats the 110-tick budget on its own. `68b627ce` still claims a behaviour nothing has observed. That is now a *test-design* problem, not a grant problem.
- **Missile Phases 3–5 repro batch** — grant no longer needed. Phases 0/1 never needed one.
- **`@stable` benchmark re-baseline ladder** — grant no longer needed, **and it was explicitly dropped out of the release-gating set on 2026-08-16.** Separately reframed on 08-14: every `tournament-*` number is **void, not stale** — `PlayerResources.Tick` gated income and upkeep on `Playable`, which map-player bots are not, so both bots had no economy in any match ever run. There is no valid prior number to diff against.
- **Eight failing autotests — triage pass** — grant no longer needed.
- **Cross-faction + RU-mirror test batches** — grant no longer needed. Note the configs measure `ai.yaml` asymmetry (skews A1/A2) as well as unit stats.
- **Item-31 aggressiveness sweep** and **Fires P2+P3 + brain-1c sweep** — the grant half is closed; both remain OFF in `@experimental` (`OpportunisticAdvanceEnabled: false` `ai.yaml:420`; `PreparatoryFires: false` `:626`; `SuppressionCoordinatedAdvance: false` `:638`; `AllocationScoreQuantizeBandPct: 0` `:383`) as queued work, not as gates. Sweep specs preserved in git history at `de78a1ed^`.
- **Post-merge benchmark goahead** — same; folded into the re-baseline above.
- **Standing sims/tests state / weekly burn budget** *(2026-08-02)* — overtaken entirely.

**Answered by the user:**

- **Missile audit scope → every guided missile in the game.** Everything on the `Projectile: Missile` path — ATGM, AA, top-attack, ship-launched, cruise. Ranking, not exclusion, keeps it proportionate. *(`closeout/missiles-e2475f8d.md` §3.)*
- **Close-range AA at 2–4 cells → it is a bug; the missiles should hit.** Verbatim: *"Should have the same hit chance regardless of distance. If we want to limit firing to a min distance, we can set that on the weapon, but as long as the weapon can fire the missile should be able to hit."* Read as a **general design invariant**: hit probability must be distance-invariant across a weapon's permitted envelope, and range limiting is the job of `MinRange`/`Range` — never an emergent consequence of projectile physics. **This rules out "make the launcher refuse the shot" as a fix.**
- **FX audit items 10 and 12 — DECLINED** *(struck at `ab2c4745`)*. Asked directly whether the verdict pass had missed them or declined them, you chose *"Declined — retire both from the queue."* `VehicleCookoffTiny` keeps `Explosions: piff` / `ImpactSounds: gun27.aud`; `HandGrenade` keeps `explosion_medium`. **Do not re-propose without new art.** _Recorded because the long-standing manager read — that the silence was oversight rather than rejection — was **wrong**, and it looked like a strong inference at the time._
- **Bail order / `EmergencyBailDelay: 45` — REJECTED.** You want passengers out **earlier**, not later. The "bail parity" proposal rested on making infantry wait ~45 ticks to match the crew; that is backwards. `Cargo.cs:110` keeps `EmergencyBailDelay = 0` and no YAML sets it — which is now the intended state, not an oversight. **Do not re-propose parity.** _(The caveat that made this risky is moot as a result: `EmergencyBailDelay > 0` remains the one uncovered code path on that branch, and now stays uncovered because nothing sets it.)_

**Overtaken by shipped code:**

- **AA stand-down, all three questions.** `16eca8e8` (2026-08-12) shipped the **per-shooter clamp** — `min(claim,100)`, taking the stand-down from 172 ticks to 55 — plus the `ValidTargets`/`IsValidAgainst` repair and the render-only marker `WithHoldingFireDecoration.cs`, wired at `defaults.yaml:306,567,679`. Question (a), *which aircraft type was involved*, was **never answered and no longer gates anything** — the fix is generic. _Your framing was the most useful sentence in the item and is worth keeping: **a unit refusing to shoot a live enemy tells the player nothing** — which is what cost you an evening suspecting trees. That is what the marker exists for._
- **Balance proposal 003 — Mi-28 advertised AA.** Shipped at `bba63d11`: `Ataka.AA` is defined (`weapons-missiles.yaml:184`) and mounted as `Armament@2_Air` (`aircraft-russia.yaml:387`). Split from `Ataka` rather than adding `Air` to it, because the SACLOS ground flight profile (cruise 100, turn 20) cannot reach helicopters cruising at 1560–2560. **The proposal doc still says `Status: PROPOSED` and is stale** — flagged, not edited.
- **Balance proposal 001 — Tunguska duplicate `Health`.** Applied 2026-08-11 as the **behaviour-neutral** dedup you asked for, not the 14000-preserving variant originally proposed. Mechanism confirmed: `MiniYaml.MergeIntoResolved` merges the later node over the earlier *in place*, so the survivor keeps the first block's position and the last block's values — effective HP was, and remains, 8000. `--resolved-rules tunguska` byte-identical before and after. **The parity question it exposed was promoted to NEEDS YOU item 5 — and then closed on 2026-08-20 when a re-audit showed the comparison had omitted armour and inverted once it was included.** See the 2026-08-20 closures above.
- **A live play window.** You have played `@experimental` live at least twice since this was raised (2026-08-13 → pipeline items 56–61; 2026-08-15 → items 63–65), and the 2-human desync game was played and captured. Screenshot review also happened (`9686b4d6`, `12b031f6`). **Three claims from the original ask remain unobserved** — the credits screen opening, a 64×48 cameo in a 62×46 slot, NVorbis decoding an `.ogg` — but those are now a manager launch, not a request for your time.

**Stale premise — the question rested on something no longer true:**

- **Streak protocol / non-wins / queue handling** *(three questions, 2026-07-31)* — what counts as one game in a 10-win streak, whether draws break it, whether the streak campaign supersedes the parked pipeline items. **The corpus these questions were about is void**: every `tournament-*` match was played by two bots with no economy at all, so there is no streak to protocolise. Combined with the 2026-08-16 reranking (which moved the whole bot-benchmark thread out of the gating set), the campaign is superseded in substance. **Stated honestly: no user statement ever retired it** — if you still want a streak campaign, it starts from zero on a fixed instrument.
- **Ambush gate (b) pricing** *(2026-07-29)* — "price again with more seeds, or keep default-off and close." The pricing run was to be taken on the tournament suite, and it is explicitly gated behind the re-baseline. Same void-corpus reason. Re-raise only after a valid instrument exists.
- **LANDED auto-branch disposition** *(2026-07-29)* — the premise was that `auto/may-salvage`, `auto/spread-prefix` and `auto/b1-walkback` were "left intact on origin". **They are not on origin** — the only `origin/auto/*` refs are `pips-zoom`, `preserved-wip-260520` and `transport-idle`. All three exist locally only, which needs no decision from you.
- **Post-measurement decisions — five with a stated order** *(2026-08-10)* — the item's own **binding constraint was the durability-weight question, and it was answered by measurement, not by a pick**: `f2a31035` established the weight is RA-scaled but moves the reference only −19%, so it does not have to be fixed before the thresholds are re-derived; stage (a) then shipped (`f09183e2`, merged `ddcc5d6c`). The remaining four are queue-ordering questions, and the 2026-08-16 reranking took the whole thread **out of the release-gating set**. Queued work now, not a gate. *(Magnitudes for whoever reads a new baseline: ground reference 3412 → 2748 (−19.5%), air 3627 → 2034 (−43.9%) — **the two channels rescale by different factors**.)*

**Informational — no decision was ever being requested:**

- **Asset licensing.** [`ASSET-LICENSING.md`](ASSET-LICENSING.md) inventories the 1,246 binary files this repo redistributes. **It states its own answer: ship as-is, not a release blocker, execute none of it now.** It sat in the OPEN list for eight days without ever asking you anything. The free tier (steps 1–6, ~549 files / ~44% of everything redistributed at zero gameplay cost, the largest piece being 124 voice files no actor attaches) is available if you ever want it scheduled — **do not act on the removal plan without an explicit instruction.** Caveat worth carrying: the doc's counts and reachability are mechanically verified and solid, but its **origin column is inference** from filenames — no file was listened to, no sprite viewed. `chem/`, `robot/` and `informan/` are parked "unknown, high-risk" **on suspicion alone and may be entirely clean.**

### Earlier rulings, kept

- **Burning ejected crew is INTENDED — do not "fix" it** *(asked as an approval question and **DENIED**; `DISCOVERIES.md` at `d53779d9`)* — verbatim: *"The crew is supposed to burn sometimes, when the vehicle is heavily damaged. I see no need to change any of that, sometimes it just looks cool (in a dark way) to see your enemies crawling out of the vehicle only to burn and die."* **The mechanism looks exactly like a defect and will be re-diagnosed as one:** `VehicleCrew.cs:358-362` grants `onfire` with **no duration**, unlike `VehicleCookoff`'s `Duration: 100`, against `ChangesHealth@BurnDamage_3` at −1% MaxHP every 8 ticks. A fix was built and **reverted at `36ad9865`**. **Binding consequence for the test suite: every phase of `test-evac-suite` must assert *who got out*, never *who is still alive*** — post-ejection survival is not guaranteed, so a survivor-count assertion is a coin flip no threshold can stabilise (the 12 → 8 → 6 threshold walk of 2026-05-09 was three attempts at exactly that).
- **FX audit item 17 (un-flatten the middle of the explosion ladder) — DECLINED** — *"we need more effects, but I don't think it is worth it now."* **Do not re-propose without new art.**
- **SR flow shape** *(posted 2026-08-04, decided 2026-08-05)* — you picked the non-default arm: **"Advance immediately, singly — zero assembly anywhere; maximally responsive but arrives piecemeal into contact."** Implemented as `ImmediateReinforcementCommit` (`ai.yaml:770`), suppressing the fill-completion massing hold at the forward muster and nothing else. Post-retreat dwell, `SectorPostureHold`, the free-pool forward stager and transport-fill waits stay live. @experimental-only. Revert path: drop the single line.
- **NAT scope** — answered *"Diagnostics + flip the UPnP default"*, which is what shipped.
