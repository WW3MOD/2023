# Dispatch pack — how to continue on the other machine

> Companion to [`RESUME-260816.md`](RESUME-260816.md). That file is *what happened*; this one is *what to do next and how to run it*.

## Honest answer on preserving the crashed workers' work

**There is very little to preserve, and that is good news rather than bad.** Checked before writing this:

- **No code is lost.** All four in-flight worktrees (`packaging`, `bot-precedence`, `onboarding`, `shellmap-session`) were level with `main` — not one worker had committed a line.
- **Little analysis is lost either.** Eleven of the fifteen had been running about ten minutes and were still in the file-reading phase; their transcripts are almost entirely tool calls with no conclusions yet. The two oldest got furthest: the crash worker had reached the point of capturing `make test` baselines and had spotted that its first two captures were not comparable (one console-truncated, one a full log); the onboarding worker was still exploring.
- The transcripts remain readable cross-machine if you ever want them — `list_peer_sessions` on the MSI peer, then `read_peer_transcript`. **Not worth the tokens.** Re-dispatching is cheaper and produces better work than resuming a half-formed one.

**So: do not try to resume them. Re-dispatch from the pack below.** Everything they had been given is either in this file or in a committed audit report.

---

## Kickoff prompt — paste this into a fresh manager session on the other machine

```
Read WORKSPACE/RESUME-260816.md and WORKSPACE/RESUME-260816-dispatch-pack.md first, then
`git pull` and confirm main is at dffb8364 or later.

You are picking up a release push for WW3MOD that was running on the other
machine until its weekly budget ran out. The audit phase is COMPLETE — nine
reports are committed under WORKSPACE/audit/260816-*.md and you should treat
them as findings, not re-do them. Five fix batches already shipped.

Operating rules for this machine, which are different from the last one:
- MAXIMUM 5 CONCURRENT WORKERS. The previous machine crashed under 15. This is
  a hard cap, not a target — fewer is fine.
- There is NO hurry to burn budget. Prefer doing one thing properly to
  parallelising for its own sake.
- SERIALISE heavy commands. Only one worker at a time may run `make.ps1 all`,
  `dotnet test`, or `make.ps1 test`. They contend for the same output DLLs and
  a concurrent build fails on locked files. Note make.ps1 must be run through
  PowerShell — bash chokes on its syntax.
- No worker launches the game or runs an autotest/batch/tournament. That
  authority is yours alone, and the project's standing no-autonomous-multi-test
  rule still applies on top of it.
- Workers commit to their own branch and NEVER merge or push. You merge after
  review, verify build + NUnit, then push main.

Current state: main @ dffb8364, build clean, NUnit 1494/1494, make test RED
with 5 distinct defects.

Start with Wave 1 in the dispatch pack. Report back before starting Wave 2.
```

---

## How to run it — 5 concurrent, in waves

Ordered so each wave leaves the tree in a state the next one can build on, and so no two workers in a wave contend for the same files.

### Wave 1 — the release gate and the known crash (3 workers)

Deliberately only three, because two of them need the build and lint serialised.

1. **Packaging** — the whole release depends on it.
2. **Shell-map crash** — fully diagnosed, just needs implementing.
3. **U10 onboarding relocation** — user-requested, self-contained, touches nothing the other two touch.

### Wave 2 — read-only audits (5 workers, no build contention)

All static analysis, so they can genuinely run together: **determinism sweep · stability sweep · netcode readiness · weapons/armour matrix · economy conformance.**

### Wave 3 — design and research (4 workers)

**Garrison usability · selective unload · combined-arms · release plan.** All produce documents and mockups for the user to sign off, not merged code.

### Wave 4 — bots and maps (2 workers, one at a time if either needs measuring)

**Bot procurement precedence · maps + the cordon decision.**

---

## The tasks, ready to re-dispatch

Each is self-contained. The "context" column is what to tell the worker to read first — that is where the accumulated knowledge lives, so a fresh worker starts as informed as the crashed one was.

### 1. Packaging — a stranger cannot install
**Read first:** `WORKSPACE/audit/260816-install-packaging.md`
Three stacked blockers: the packaged build ships without the `ra` mod it hard-depends on (`mod.config:104`'s copy list is empty, so `engine/packaging/functions.sh:122-131` never copies it); the RA content installer is dead configuration (`mod.yaml:13` declares `DefaultFileSystem`, which does not implement the interface `BlankLoadScreen.cs:131` gates on); CI targets `macos-11` and `windows-2019`, both retired.
**The trap:** the old machine had full RA content in `%APPDATA%`, which is exactly why this survived. Whichever machine works on it, **the verification that matters is launching once with the content directory renamed aside.** Until that happens the fix is unproven.

### 2. Shell-map crash — hard crash to desktop
**Read first:** `WORKSPACE/audit/260816-crash-clientinslot.md`
`SetupShellmapBots` (`Game.cs:614`) appends bot clients non-idempotently, so a second injection puts two clients in one slot and `ClientInSlot`'s `SingleOrDefault` throws. Fix is **both**: make it idempotent, and make the two `LoadShellMap` overloads agree on session lifecycle. **Not** `SingleOrDefault` → `FirstOrDefault`, which hides a corrupt session in shared upstream code.
**Partial progress worth knowing:** the crashed worker had started capturing `make test` baselines and found its first two captures were not comparable — one console-truncated, one a full log. Capture both the same way.

### 3. U10 — move the how-to-play briefing to the main menu
**Read first:** commit `dd6171cd`, and the user quote in `RESUME-260816.md`.
Remove the in-game auto-open so the game starts normally; re-home to the main menu with first-launch-only auto-open plus a permanent "i" in the top-right; keep the in-game tab but never push it. Do **not** build a lexicon — the user floated it as an idea, not a request.
Two things `dd6171cd` records and must be preserved: the `!TestMode.IsActive` guard is load-bearing, and the panel belongs to ww3mod's own chrome copy rather than shared common chrome. Also fix one factual error: the panel says losing the Supply Route link "puts them out of the match" when the mechanic is passive and reversible (`SupplyRouteContestation.cs:354-373`).
**Unexplained and must not be papered over:** the gate reads correct and the user's `settings.yaml` already says `HowToPlayVersion: 1`, so it should already have stopped firing. Move it anyway; do not claim the repeat is fixed.

### 4. Determinism sweep — finish what movement started
**Read first:** `WORKSPACE/audit/260816-desync-rootcause.md` (**its §5 is wrong** — the one-third rounding bug does not exist).
Three known leads: `Armament.cs:521` runs a float chain into `SharedRandom.Next`; three traits carry dead `[Sync]` annotations with no `ISync` (`VehicleCrew`, `SupplyRouteContestation`, `CohesionSlotMemory`); and the engine's own lint flags that class — find out what else it flags that is being ignored.
**Highest-value sweep: every `IOrderGenerator` for the shape that produced a live desync** — mutating simulation state without yielding an `Order` (`EjectRallyOrderGenerator.cs:62`).

### 5. Stability sweep — find the next crash first
**Read first:** `WORKSPACE/audit/260816-bug-reconciliation.md`
Two hard crashes were found in one day, both the same shape: a lookup by a key that can be absent. Sweep for the pattern — `dict[key]`, `.Single()`, `.SingleOrDefault()`, `.First()` — plus null derefs on optional traits and anything that throws inside a tick. Separate WW3MOD-authored code from upstream via `git blame` against the vendoring squash `7362fbc6`; upstream is much less interesting.

### 6. Netcode readiness — what happens when two strangers play
**Read first:** PIPELINE items 42, 53, 55.
Server browser, NAT guidance, disconnects, rejoin, lobby slot handling, replays. **One specific check worth its weight:** establish whether a released build records sync reports by default — `EnableSyncReports` is read from the *host's* lobby globals by every client, so a host without it silently disables recording for both players, which looks identical to the bug itself.

### 7. Weapons + armour matrix
**Read first:** `DOCS/recipes/BALANCE.md`, `DOCS/reference/conventions.md`
161 weapons × armour classes, never built. **The load-bearing rule: an unlisted armour class takes 100% damage, so an omission is the opposite of a zero** — every omission is a potential balance bug in the direction nobody expects. Four named defects to verify: Mi-28 has no AA weapon (`secondary-air` referenced 3× defined 0×); Iskander/HIMARS `Versus` zeroes a nonexistent class and omits three real ones; `humvee` declares `RenderSprites` twice (any map overriding it fails to load, presenting as a hang); supply caches below 50 serve nobody and never despawn.

### 8. Economy conformance
**Read first:** `DOCS/reference/economy.md` — **it declares itself normative over the code**, so code contradicting it is a spec violation to report, not a doc to fix.
Walk every rule against the code. Re-check the five-point "Verify" list at `RELEASE_V1.md:44-48` independently. Check the T0=1 → T9=1500 tier table against ~63 AmmoPools. And check whether spent ammo is deducted from cashback at evac for **all** unit types — buy, fire everything, evacuate for full value would be a live money pump.

### 9. Garrison usability (research + mockups)
**Anchor finding:** `GarrisonManager.cs:641` writes `ps.IsDucking` every tick and a repo-wide grep finds **only writes, zero reads** — so graduated suppression is inert and reads as binary. That may be most of why garrison feels unintuitive.
User wants ideas and will go hands-on. Deliver a ranked list plus an HTML mockup under `WORKSPACE/`.

### 10. Selective passenger unload (design + mockups)
**Hard constraint:** the design **must travel as a proper `Order`**. The neighbouring eject-rally code sets state client-locally with no order and that is a live desync — per-passenger selection is the same shape of state and would reproduce it exactly.
Deliver three interaction options with tradeoffs, plus an HTML mockup the user can judge by feel.

### 11. Combined-arms coordination (research)
**Read first:** PIPELINE item 64, and commits `314f0ed3` (rendezvous — measured **INERT**), `97cb73c2` / `4c4d8a49` (stand-off — the rendezvous half measured **HARMFUL**).
The obvious fix has already been tried and made things worse. **The valuable question is why**, not what to build. Mechanism: armour and mounted infantry compute destinations independently with different arithmetic and cannot see each other. Named risk is deadlock — prior art at `bd3abacf`, where a coupling that looked like caution read as paralysis.

### 12. Bot procurement precedence
**Read first:** PIPELINE items 63, 64, 66.
One disease: **the system has a notion of HOW MANY and no notion of WHEN.** Trucks went two-at-t=0 → floor set to 0 → almost none ever; medics ran the same course in reverse. Supply the missing ordering axis; do not rebalance quantities or you buy a third report with a different unit. Acceptance is a **responsiveness** measure (time from first dry unit to truck ordered), not a count — and measuring it needs a run, so the manager schedules that.

### 13. Maps + the cordon decision
All nine playable maps fail the cordon check — **69 of the 89 remaining lint errors**, and the single biggest reason `make test` is red. `4f67b375` recorded it rather than fixing it. **Needs a decision: re-cordon or waive.** Come back with a recommendation, not options. Then per-map readiness: previews, metadata, spawn fairness, whether a `logisticscenter` exists (aircraft rearm depends on it and it is on only three maps), and actor counts.

### 14. Release plan — ModDB, GitHub, copy
**Lead with the hook: there is no base building.** No factories, no tech tree; units arrive from off-map reserves via a fixed Supply Route; you win by cutting the enemy's link. Read `DOCS/reference/game-model.md` to get it right.
**Sequence honestly:** launch copy is worthless until a stranger can install the game, so this comes after packaging.

### 15. Knowledge-bank curation
**Read first:** `DOCS/reference/README.md`
Verify unpromoted `WORKSPACE/DISCOVERIES.md` entries against the code and promote the true ones; reject freely. Fold in today's durable lessons — the unlisted-armour-class rule, lint lowercasing before validating while runtime consumers do not, sync reports being single-sided, a consumed-but-never-granted condition meaning the consumer is permanently off, and `make test`'s count being meaningless because the validator relints once per map across 185 maps.
**One correction owed:** `WORKSPACE/audit/260816-desync-rootcause.md` §5. Mark it corrected rather than deleting — a refuted hypothesis with its refutation is worth more than a silent removal.

---

## Manager state that lived only in the MSI daemon — transcribed here so it transfers

Maestro's backlog and tracks are stored in the daemon's database, **not in the repo**, so none of it reaches the other machine on its own. Both are written out below. The new manager should recreate whatever it finds useful and treat this section as the authoritative handover of that state.

### The backlog — 8 queued items and their real disposition

Several are already satisfied by work that landed today; do not re-run those.

| # | Item | Disposition |
|---|---|---|
| 1 | Clean-machine install + packaging audit | **DONE** — `audit/260816-install-packaging.md`. The *fix* is not done and is Wave 1 above |
| 2 | Multiplayer + netcode readiness | **NOT DONE** — dispatched, killed by the crash. Re-dispatch (task 6 above) |
| 3 | Crash + stability sweep | **NOT DONE** — dispatched, killed by the crash. Re-dispatch (task 5 above) |
| 4 | Maps: previews, playability, lint/nav-guard coverage | **NOT DONE** — dispatched, killed by the crash. Re-dispatch (task 13 above) |
| 5 | Performance: 6-player skirmish slow on MacBook | **NEVER DISPATCHED.** Reported 260508, never profiled. Read git history for prior perf work (shadow-cache freeze, density layer, AI tick budgets) before re-investigating. Note the shadow-layer rewrite at `c63bad56` may have moved this — re-measure before assuming the old report still holds |
| 6 | Rename lobby AI opponents (R4) | **DEFERRED by user ruling** — the Experimental/Stable split is useful while bots are still being developed. Revisit at final polish |
| 7 | Art + audio work list (U9) + icon set (U4) | **DOCUMENTS DONE** — `audit/260816-content-completeness.md` and `audit/260816-command-bar-research.md`. The work itself is the user's and is deferred |
| 8 | Suspected memory leak | **DONE** — `audit/260816-memory-investigation.md`, fixes shipped at `c63bad56`. Verdict: mostly not the game (94%-full disk + Defender) |

### The track ledger

**Shipped and closed** — no action needed, listed so nobody reopens them: the nine audit slices (chrome, systems, bugs, install, build/test, content, plus desync forensics, crash triage, command-bar research), and five fix tracks (correctness, gunner-seat, presentation, move-determinism, memory).

**Open, and each has a section above:**

| Track | State |
|---|---|
| **U2 desync** | Root-caused as far as static analysis goes; NOT fixed, and the evidence weakened. Needs the two-runtime replay |
| **U7 shell-map crash** | Diagnosed, fix shape agreed, not implemented |
| **U10 onboarding** | Not started. Repeat-firing cause still unexplained |
| **U3 command bar** | Shipped — but wants one screenshot to confirm by eye |
| **U4 icons** | Costed (14 icons needed); user's call on placeholders, recommendation is not to |
| **U5 garrison** | Research not started |
| **U6 selective unload** | Design not started |
| **U8 release plan** | Not started; sequenced after packaging |
| **U9 art/audio** | Document delivered, work deferred to the user |
| **U11 memory** | Shipped. Remaining action is the user's: disk space + a Defender exclusion |

**Also carried in `WORKSPACE/PIPELINE.md`**, which IS in the repo and is the durable source: the ranked release findings R1–R16, the binding ranking function, and the deferral block.

## The single most valuable run, whenever a machine can spare it

**Replay one desyncing match on ONE machine under both .NET runtimes with `Test.ForceSyncReports=true`, and diff the `syncdiag-*` files.** Identical output kills the runtime hypothesis outright and redirects the whole desync investigation. It is one run and it is decisive either way.
