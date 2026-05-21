# Autoburn — release-v1-staleness

> Audit of `WORKSPACE/RELEASE_V1.md` against git log for items that have shipped but were not pruned. **Recommendation report only** — no edits to the tracker.

## Context

- Tracker file lives at its current path since 2026-05-09 (commit `c2da7a75` folder reshape).
- Heavy trim happened at `c47971e1` (2026-05-09, ~57% byte reduction — "WORKSPACE cleanup: aggressive trim of v1 tracker").
- Last tracker edit: `bfbc0637` on 2026-05-12 (AI overhaul note refreshed). The 2026-05-13 commit `37758fe8` added the automation track but only touched Phase 0.
- Today: 2026-05-21. **Staleness window for unpruned shipped items is ~8 days** (2026-05-13 → today).
- Many March/April commits in the repo predate tracker creation; tracker carried items forward from that period intentionally, so a March commit "matching" an open item is **not** evidence of stale pruning.

## Summary

- **0 likely shipped** — no item is unambiguously shipped-and-forgotten.
- **3 partial / ambiguous** — one item where a referenced sub-fix shipped but the description doesn't reflect it; two `[T:trusted]` items awaiting playtest verdict to be removed.
- **Index of unshipped open items** at the bottom (Phase A big systems, most of Phase B, Phase C polish).

The tracker is in good shape. The user has been actively pruning. Recommendations below are nits, not big wins.

---

## Likely shipped

*(none — pruning is current)*

---

## Partial / ambiguous

### 1. Supply cache "real supply actor + destructible" — bar sub-task shipped

**Tracker line (Phase A / Active items in flight):**
> `! [ ]` **Dropped supply cache: real supply actor + destructible** — current cache from TRUK deploy doesn't act as a supply actor with its own bar, and may be indestructible. Should be very destructible (large explosion on death, size scaling with remaining supplies), targetable by other supply trucks to replenish, possibly auto-replenish via stance on the cache. Needs design discussion before code. (Underlying TRUK deploy → drop cache shipped 260504, commit b3699b63.)

**Matching commits:**
- `b94b857a` (2026-05-11) **Show supply bar on dropped SUPPLYCACHE — add SelectionDecorations**
- `753d7c4a` (2026-05-11) DropsSupplyCache trait: TRUK behaviors on top of SupplyProvider
- `7a32e3df` (2026-05-11) Rip CargoSupply: TRUK is now a SupplyProvider
- `2ab93552` (2026-05-11) Economy: per-batch SupplyValue, CreditValue collapsed, YAML retuned

**Ambiguity:** the "doesn't act as a supply actor with its own bar" complaint is **already addressed** by `b94b857a` (selection decorations now show the cache's supply bar) — but the tracker text still reads as if the bar is missing. The "destructible", "replenishable by other supply trucks", and "auto-replenish via stance" sub-tasks remain genuinely open and need design.

**Suggestion:** rewrite the entry to scope it down to what's *still* open (destructibility + replenishment + design pass), and acknowledge the bar shipped in `b94b857a`. The `! [ ]` urgent flag is probably still warranted for the destructibility piece.

---

### 2. Iskander/HIMARS shockwave radius `[T:trusted]` — playtest verdict overdue

**Tracker line (Phase A / Active items in flight):**
> `[T:trusted]` **Iskander/HIMARS shockwave radius too large** — tuned 260509 (commit `9578557c`). `MaxRadius` values verified in `weapons-explosions.yaml`: Iskander 4c0 (line 495), HIMARS 2c512 (line 532). Feel needs human eye in next playtest

**Status:** Code-verified, fix referenced. The only thing blocking removal is a playtest report. No playtest report exists in `WORKSPACE/playtests/` newer than `260509_1152_focused_brief.md` (2026-05-09).

**Suggestion:** schedule a focused playtest pass on artillery feel; once user is satisfied, **remove the entry**. Per CLAUDE.md / tracker preface, `[T]` items pass playtest → removed entirely.

---

### 3. Helicopter→helicopter missile vanish `[T:trusted]` — playtest verdict overdue

**Tracker line (Phase B / Active bugs):**
> `[T:trusted]` **Helicopter→helicopter missiles silently vanish on impact** — fixed 260510. ... `Missile.cs:1067` mid-tick segment-aim-point proximity check ... `Missile.cs:1059` airburst gate on `!flyStraight` ... Hellfire `Warhead@Spread.Penetration: 1→20` ... Result: Apache one-shots a Mi-28 at 22 cells (autotest `test-heli-vs-heli-missile`). All other missile autotests still pass

**Status:** Fix is in tree (`d64a7a68`), autotest passes, all sub-changes referenced. Same situation as #2 — no playtest verdict yet, blocking removal.

**Suggestion:** stage a quick heli-vs-heli scenario or include it in the next playtest, then **remove**.

---

## Not shipped (index)

Open items where no recent commit suggests they've shipped. Listed for completeness — not every item needs a per-item note.

### Phase 0 — Tooling
- Automation workflow track (260513, plan only — open)
- Autotester focus-steal (subsumed by automation)
- Autotester launch position (subsumed by automation)

### Phase A — Big systems (all `[ ]` or `[T]` waiting on playtest)
- Garrison overhaul `[T]` — large body of garrison commits; needs playtest verdict
- Cargo system (Phases 2A–E)
- Helicopter crash + crew overhaul
- Stance rework (4 phases)
- AI overhaul `[~]` — Phase 2+ brain work still unstarted; Stage A+B and tournament harness shipped (already noted in tracker)
- Supply Route contestation
- Three-mode move system
- Vehicle crew system (slot ejection / re-entry / commander)
- Infantry mid-cell redirect

### Phase A — Supply & ammo economy
- Supply & ammo economy overhaul `[T]` — P1–P3 shipped; needs playtest verdict
- Verify unit sell value at different ammo levels

### Phase A — Active items in flight (besides items #1–#3 above)
- Supply truck → building = transfer supplies (new feature, not started)
- Vehicle off-map evac flight
- Littlebird rotor spins after safe landing

### Phase A — Known design issues
- Buildings invisible / fog visibility model `[~]`
- Visibility / fog design decisions

### Phase B — Active bugs
- Heavy artillery deliberately ignores infantry
- Some enemy soldiers untargetable (mutual)
- Bridge pathing (investigated 260509, fix options drafted)
- Allied shared vision blinks rapidly (cannot reproduce)
- Helicopter husks on water don't sink — `HuskDecay` (March 2026) plays splash + disposes on water but no visual *sinking*. If user wants sink animation, item is correctly open; if "dispose-on-water" is enough, this could be considered shipped. Worth a one-line clarification in the tracker.
- ATGM units can't unload while shooting (attack lock)
- Walking sequence speed mismatches locomotor
- Mobile sensor (CounterBatteryRadar) — investigated 260509
- River Zeta: neutral SAM + broken capturable

### Phase B — Drone fixes
- DR animations
- Drone autotarget of other drones broken — autotest scaffold added by sibling autoburn branch `auto/bugs-survey` (`test-dr-jams-drone/`); no engine fix yet
- Anti-drone weapon too effective
- Drone death needs crash animation

### Phase B — Aircraft polish
- Edge spawn/leave for planes
- Helicopter landing refinement
- Apache shouldn't shoot guns at structures
- Ballistic missile tilt fix — heavy April work (12+ commits ending `49b64f7c`); tracker was created **after** that work, so the item carrying forward implies residual issues remain. If user believes April work was enough, this could be retested and removed.

### Phase B — Combat / suppression / bypass
- Suppression tuning (vehicle values)
- Flametrooper effective vs unarmored
- Units out of ammo reject attack orders
- No-ammo units must reject attack-move
- Shoot at last known location
- Ballistics deprioritize targets

### Phase B — Supply Route
- Captured SR handling
- Primary SR selection UI

### Phase B — AI
- AI builds Logistics Centers, rearms
- AI conscripts don't abandon capture for squad orders
- AI stops firing at buildings marked for capture
- AI garrisons defense buildings
- AI uses attack-move for aircraft

### Phase B — Misc gameplay
- Helicopter force-land tuning + crew bloat fix + crew vehicle re-entry testing

### Phase C — Polish (all open)
- Unit firing sounds
- Explosion sounds
- Unit voice responses
- Unit icons
- Per-unit rot/bleedout sprites — `578ad474` (March) reused e1 sprite frames; the tracker explicitly says "currently uses generic e1" so this is correctly still open
- Unit description box sizing
- Garrison Phase 4 (sidebar icon panel rewrite)
- Cargo Phase 3 (template sidebar)
- Pre-release perf pass
- 6-player skirmish slow on MacBook

---

## Method notes

- Searched `git log --since='3 months ago'` with keyword greps for each open tracker item.
- Checked `git blame` on `WORKSPACE/RELEASE_V1.md` to confirm items were added *after* their candidate commits (i.e. the user already knew the candidate commit existed and chose to keep the item open).
- Cross-checked with `WORKSPACE/HOTBOARD.md` "Recent Wins" — most items there are already removed from the tracker (good pruning hygiene).
- Cross-checked with `WORKSPACE/playtests/` — only `260509_1152_focused_brief.md` exists, so any `[T]` item carrying a playtest dependency cannot have been verified post-trim.
- Cross-checked with `WORKSPACE/autoburn/README.md` to confirm the prior autoburn run salvage state.

The tracker is in healthy shape; user's active pruning (commits `c47971e1`, `a4a8a04f`, `7517cad5`, `c2698d96`, `f77cad68`, `bfbc0637`) keeps it current. Main lever to drop more items is **scheduling a playtest pass** so the `[T]` and `[T:trusted]` items can be verified and removed.
