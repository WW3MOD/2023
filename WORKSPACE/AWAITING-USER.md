# AWAITING-USER — what actually needs the user

> **Purpose:** the single place where everything **dependent on the user's decision, review, or grant** is parked. The manager adds items; the user resolves them in chat or by editing this file. Anything resolved moves to [RESOLVED](#resolved) with the ruling recorded — nothing is deleted.
>
> **The top section is the whole point of this file.** If it is long, it is wrong. Everything below `NEEDS YOU` is history.

> **Reconciled 2026-08-19 against `main` @ `de78a1ed`**, verified against the code and `git log` rather than against other workspace docs. **Of 27 items carried, 8 are genuinely open and 19 closed.** Two rulings dissolved most of the file:
>
> 1. **Full simulation/launch grants, 2026-08-19** *(decision record `.../manager-850d8885/decisions/06`)* — user: *"You have full grants to launch simulations but I suggest you do it from here so that multiple workers are not all starting simulations."* **Every "needs a run grant" item in this file is therefore no longer a user gate.** It is manager scheduling. That closed eight items outright.
> 2. **Release ranking, 2026-08-16** *(decision record `.../manager-ed57f2e0/decisions/01`)* — the user chose **public release to strangers**, and rank is now *what a stranger encounters, how early, how visibly*. That demoted the whole benchmark/danger-tuning thread out of the gating set, and it is why the surviving list below is short and mostly player-visible.

---

## NEEDS YOU

Ordered by how much it blocks a public release. Each says what happens if you say nothing.

### 1. Where do aircraft rearm and repair? — **a live gameplay hole**
Seven aircraft name a rearm host that **cannot exist in a game**: `RearmActors: hpad` ×4 and `afld` ×3 (`aircraft-america.yaml:235,399,530`; `aircraft-russia.yaml:233,419,574,669`). Both actors carry an unsatisfiable *build* prerequisite and are pre-placed on **zero of ten maps**. Every ground vehicle and infantry template instead names `logisticscenter`, which works. **So aircraft alone can never rearm.** Verified unchanged at `de78a1ed`; `git log -S AirframeReadiness` since 2026-08-01 returns only docs.

Options previously put to you were HPAD/AFLD (make them buildable) / point aircraft at `logisticscenter` / a new host. **The agent's own preferred option is one no worker proposed: put rearm on the Supply Route itself**, which every player already has exactly one of.

*Caveat for whoever implements the answer:* all three hosted arms of `AirframeReadiness` are structurally unreachable today, pinned by unit test and **never executed** — wiring any host makes all three go live in the same instant.

**If you say nothing:** aircraft run dry and stay dry, on every map, for every player.

### 2. Release version string — **the first thing a stranger reads**
**These are two independent strings and only one is cheap; earlier framings of this item ran them together.** Verified at `68e8b885`:

**(a) What the player actually reads — free to change, and it is ONE line, not two.** "Pre-Alpha" is **not in `mod.yaml` at all**. It is two hardcoded C# literals, but **only one of them is reachable**:
> - `MainMenuLogic.cs:280` — `"WW3MOD — Pre-Alpha"`, the main-menu **`v`** dropdown (`mainmenu.yaml:87` `INFO_BUTTON`, `:101` panel, `:109` label). **This is the live one — the only place a player ever reads the string.** Note it is `Visible: false` until the player clicks **`v`** (`mainmenu.yaml:106`), so it is not on screen at rest — the adjacent **`i`** button is the how-to-play briefing, a different thing.
> - `ModInfoPanelLogic.cs:22` — `"Version: Pre-Alpha"`. **Dead.** Its root widget `MOD_INFO_PANEL` occurs exactly once repo-wide — its own declaration at `info-panel.yaml:1`. Nothing opens it; the file is listed in `ChromeLayout` (`mod.yaml:204`) so it is parsed, never instantiated. Its logic ctor also demands `Action onExit, string shellmapName`, which no caller supplies — so it could not be opened as a plain child even by accident.
>
> Changing the live literal touches **no yaml**, so it does **not** move the multiplayer rules hash. *Trap, and it runs the opposite way to the obvious guess: someone who "fixes the version string" by editing `ModInfoPanelLogic.cs` changes **nothing on screen** and will believe it is done. Edit `MainMenuLogic.cs:280`. Decide separately whether the dead panel is wired up or deleted — it is not part of this decision.*

**(b) `mod.yaml:3` `Version: release-20230225` — not free, and arguably already correct.** The live panel renders it as **"Fork: "** + the value (`MainMenuLogic.cs:281`; the dead panel does the same at `ModInfoPanelLogic.cs:23`) — it is deliberately presented as *the OpenRA release this forked from*, which is true, and is a normal thing for a total conversion to state. It is also a deliberately frozen literal: `Server.cs:541` and `Handshake.cs:48` both record that its compatibility job was **superseded by `BuildFingerprint`**. Touched exactly once ever (`4894008b`, 2023-03-19). Changing it costs three concrete things — (i) `mod.yaml` is hashed verbatim into the rules segment (`BuildFingerprint.cs:310-313`), so it moves the multiplayer hash for everyone; (ii) **every existing replay disappears from the in-game browser**, which reads only `Replays/ww3mod/<Version>/` (`ReplayBrowserLogic.cs:145`); (iii) it orphans the launcher registration key `ww3mod-release-20230225` (`ExternalMods.MakeKey`).

**Recommended: do (a) only and leave (b) alone** — that removes the "unfinished" signal at zero risk while keeping an accurate fork marker. Values for (a) are your taste: **`Beta`** — honest while known gaps remain (aircraft rearm, item 1) and unremarkable for an indie mod; **`1.0`** — strongest signal to a stranger, but claims finished; **`0.9`** — numeric without claiming 1.0. If you want (b) changed too, say so explicitly and accept (i)–(iii).

**If you say nothing:** a public release ships announcing itself as Pre-Alpha.

### 3. Keep advertising on `master.openra.net`? — courtesy as much as dependency
There is still **no `WebServices` block anywhere in `mods/ww3mod/`** (re-verified at `68e8b885`), so a total conversion lists itself on upstream OpenRA's public infrastructure. Pointing `WebServices.ServerList` / `ServerAdvertise` at our own master is a `mod.yaml` block, not code. Note an adjacent decision was already taken *around* this and left it alone deliberately: `languages/en.ftl:36` records a choice **not** to reword the consent dialog to say "optimize WW3MOD", precisely because the payload really does go to `master.openra.net`.

> **This is a decision, not a defect — do not let it be re-raised as "multiplayer is broken".** The missing block breaks nothing. All six `WebServices` fields are `readonly string`s **pre-initialised to openra.net URLs** (`WebServices.cs:21-26`), and `Manifest.Get<T>()` lazily constructs them when no yaml node exists (`Manifest.cs:254-269`). **No `mod.yaml` in this repo declares the block — not ww3mod, and not stock `ra`/`cnc`/`d2k`/`ts` either.** Relying on the defaults *is* the stock arrangement, so WW3MOD already advertises and already reads the list. The real reason no games are visible is that **nobody is hosting one**, which is a different item.

> **One sub-item here is not a taste call and should be decided with it:** `VersionCheck` is also left at the openra.net default. The code half is verified: `WebServices.cs:50` initialises status to `Latest` and only downgrades on a recognised reply (`outdated`/`unknown`/`playtest`), so an unrecognised response leaves the game reading "up to date" **by accident rather than by agreement**. The network half is *not* re-verified here — `WORKSPACE/audit/260816-netcode.md` **N7** measured upstream returning an empty body for `?mod=ww3mod` and already recommended pinning this. The exposure: if upstream ever returns `unknown` for mods it does not know, every WW3MOD player is shown *"You are running an unrecognized version of OpenRA. Download the latest version from www.openra.net"* — an OpenRA identity string, in front of strangers, with no change on our side. Pinning it is one line, but it is a `mod.yaml` line and so carries the same rules-hash churn as 2(b).

**If you say nothing:** status quo — it keeps advertising there, and the `VersionCheck` exposure above stays open.

### 4. Balance proposal 002 — Iskander strictly dominates HIMARS at equal cost
`WORKSPACE/balance/002-himars-iskander-parity.md`, still `Status: PROPOSED`. Iskander is still `Cost: 6000` (`vehicles-russia.yaml:912`), same as HIMARS (`vehicles-america.yaml:1009`), with a strictly better warhead. Option A (recommended): Iskander → 8000. **No stat change is applied without your explicit approval** — that standing rule is why this has not moved.

**If you say nothing:** Russia keeps a free upgrade over the US equivalent.

### 5. Tunguska ↔ Stryker SHORAD parity — the *real* question, now visible
The duplicate-`Health` authoring bug is fixed (see RESOLVED), and fixing it made the underlying asymmetry explicit rather than accidental: **Tunguska is 1700 cost / 8000 HP against strykershorad's 2500 / 14000.** Cheaper *and* 43% less durable is a coherent design choice — it is just not one anyone has taken on purpose. Same sign-off rule as item 4.

**If you say nothing:** the asymmetry ships as-is.

### 6. Splash / menu art direction — *low, and possibly already answered*
Full-bleed image behind the load screen, keep the live-map menu **[78]** / logo only, no code change **[62]** / static full-screen background on the main menu, losing the shellmap **[34]**. Default on skip was the **first**. Context that makes it real: **there is no code path for a full-screen image anywhere in the mod** — `LogoStripeLoadScreen` draws a 256×256 logo, a gray bar and text, and the main menu renders a live playing map. A full-bleed splash is a chrome change *plus* art, not an asset drop. The agent's view: the live shellmap looks better in motion, so replacing it tends to read as *more* amateur.

**⚠️ Cannot determine, flagged rather than guessed:** on 2026-08-16 you deferred the art/audio TODO lists and icon placeholders — *"you can skip it fully now… just document it as a standing todo pre-release."* That may or may not cover this question. **If it does, say so and this closes.**

### 7. Item-24 repoint gates — decide, or accept them on
A/B over 40 matches showed byte-identical arms → the recommendation was **KEEP OFF**, but they are committed **ON in both profiles**: `StrategicCaptureRepointEnabled: true` at `ai.yaml:195` and `:2092`, `DefendRepointEnabled: true` at `:812` and `:2197`. Re-verified at `de78a1ed`; unchanged since 2026-07-29. So the shipping baseline bot runs a feature its own measurement said does nothing. This is invisible to a player and a manager could reasonably just take it — flagged here only because nobody ever has.

### 8. Case-01 bar ratification — needs numbers, not a direction
The 1:3 cost-ratio bar is ill-posed (÷0 when defender losses hit zero). Proposed reframe: *"def casualties ≤ X AND att casualties ≥ Y over N seeds."* Awaiting ratified X/Y/N before iterating case-01 to GREEN. Unlike the tournament work, this bar does **not** depend on the voided bot corpus.

---

## COMING BACK TO YOU LATER — not open now, but will be

- **Missile miss-detonation rule, per weapon class.** You deliberately declined to pick a single global rule, so the class taxonomy (SACLOS wire-guided / fire-and-forget / top-attack / AA / cruise) plus a rule per class is a **Phase 2 deliverable owed back to you for agreement**. Already settled and not up for re-litigation: AA missiles are exempt and may fly on until fuel-out.
- **Sweep outcomes.** The Item-31 aggressiveness sweep, the Fires P2/P3 enablement sweep and the `@stable` re-baseline ladder are now manager-schedulable (see the launch grant). What comes back to you is the **ship value** each sweep argues for, not permission to run it.

---

## RECORDS — proceeded past; do **NOT** re-ask

> Each was asked, went unanswered, and **the agent proceeded**. They are **overridable records**: you can still redirect any of them, but re-asking spends your attention on a decision already made in code. Do not promote these into NEEDS YOU, and do not reword them into a fresh question.

- **Tactical-layer default for humans** *(posted 2026-08-04)* — auto supply-seek / OOA evac **ON by default** for human units, stance-disableable. Shipped as `AutoSeekSupplies` (`f15cfbde`). _Moved here 2026-08-19: the 08-11 reconciliation kept it open on the principle that "proceeding on a default is not the user deciding." That is true, but it is the definition of a record, not of a gate — it has shipped and been played since. Redirect reverses it in one line._
- **OOA fallback** *(posted 2026-08-04)* — vehicle out of ammo with no reachable rearm source: **terminal evac + sell** (conf 80). Bot-module-only; human units stay player-controlled by deliberate design. _Moved here 2026-08-19, same reasoning. Note this interacts with NEEDS YOU item 1: "no reachable rearm source" is currently **always** true for aircraft._
- **How a supply truck decides a delivery is too dangerous to drive in** *([`closeout/bdedd544.md`](closeout/bdedd544.md) §3, which says explicitly "Do not re-ask or reword")* — resolved in code as a **two-limb classifier**: a cell stands out against the player's own live median **OR** exceeds an absolute figure, **plus a floor so a quiet field reads quiet**. Both limbs are required: purely relative classified a cell reading **462,272 as safe** (a saturated field has an enormous median); purely absolute is how the original thresholds broke.
- **One autotest run on the instrumented order log** (`Test.Mode=true Test.UnitLifecycleLog=<path>`) — never answered, agent proceeded. **No longer needs asking at all** under the 08-19 launch grant; still the cheapest way to rank churn sources empirically.
- **Whether to chase the content-divergence desync theory** *([`closeout/54ab3880.md`](closeout/54ab3880.md) §3)* — never answered, and now largely **moot**: superseded by the `Detectable` condition-token finding, and a real 2-human desync capture has since been taken (both sync reports at `WORKSPACE/audit/logs-260816-snapshot/Logs/`, divergence net frame 1264 / tick 3792, actor `4617 e3.russia`), which ruled out content divergence directly.
- **An autotest run at the real 3840 helicopter altitude** — asked, unanswered, **moot**: superseded when you rescoped the missile work.

---

## RESOLVED

### Closed 2026-08-19 — this reconciliation

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
- **Balance proposal 001 — Tunguska duplicate `Health`.** Applied 2026-08-11 as the **behaviour-neutral** dedup you asked for, not the 14000-preserving variant originally proposed. Mechanism confirmed: `MiniYaml.MergeIntoResolved` merges the later node over the earlier *in place*, so the survivor keeps the first block's position and the last block's values — effective HP was, and remains, 8000. `--resolved-rules tunguska` byte-identical before and after. **The parity question it exposed is promoted to NEEDS YOU item 5.**
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
