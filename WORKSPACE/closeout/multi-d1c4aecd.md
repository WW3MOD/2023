# Close-out — manager "Multi" (session `d1c4aecd`)

Validated against `origin/main` @ `35876332`. This manager's merge `bfd683c2` is still an ancestor of main.

**Scope of this manager, stated up front:** NAT / port-forwarding / server advertisement. It did **not** work on multiplayer desync at any point. That matters for two of the questions below.

---

## 1. Open work — nothing in flight; four items deliberately deferred

No work is in progress. Both tracks are terminal (`mp-port-forward` shipped, `nat-traversal` shipped) and all 5 tasks are completed. The four items below were consciously deferred, not dropped. **All four were re-checked in the tree at `35876332` and none was solved upstream.**

| Item | Evidence it is still open | Next concrete step |
|---|---|---|
| **Dedicated server** — the only option that structurally removes the NAT problem for *every* player | No work landed; `engine/OpenRA.Server/Program.cs` still builds headless and unused | Run the existing headless server on a VPS or always-on box, then add an "Official Server" button that Direct-Connects to a known hostname. The connect path already takes a hostname (`ConnectionTarget.cs:46`), so joiners need no inbound port at all |
| **~38 dead Fluent keys** in `mods/ww3mod/languages/en.ftl` | `no-port-forward` still at `:119`, `timeout-in` at `:84`, `chat-temp-disabled` at `:129` | Delete or rename the block `:84`–`:129` to the live `notification-`-prefixed names. Logged `[low]` in `WORKSPACE/bugs/discovered.md`. Deferred because a blind rename changes ~38 lobby strings at once and needs its own review |
| **`ServerCreationLogic.BuildNotices()` snapshots `Nat.Status`** | Still snapshotted at `ServerCreationLogic.cs:181-188` | Only worth doing as a layout restructure — the snapshot drives `Fonts[].Measure()` and then repositions three sibling labels by `Bounds.X`/`Width`. Low priority: the sibling notice blocks at `:146-159` already re-read `Nat.Status` in live closures, and the shipped FIX 2 means a stale read no longer produces a false *assertion* anywhere |
| **WW3MOD advertises on `master.openra.net`** | No `WebServices` override anywhere in `mods/ww3mod` | A decision, not a task: a total conversion listing itself on upstream's public infrastructure is both a dependency and a courtesy question. Pointing `WebServices.ServerList` / `ServerAdvertise` at our own master is a `mod.yaml` block, not code |

**Closed upstream, no longer open:** this manager's stale-HOTBOARD flag, reconciled at `56ad25e7`.

---

## 2. Uncommitted or unmerged artifacts — none

- NAT diagnostics merged to main at `bfd683c2` (two commits: `62aac8cd` implement, `453e8f07` review fixes). Verified an ancestor of `origin/main` @ `35876332`.
- Worktree `C:/Users/fredr/worktrees/ww3mod/nat-diagnostics` removed; branch `wt/nat-diagnostics` deleted after merge.
- Nothing uncommitted was left behind. The `wt/*` branches currently in the checkout belong to other managers.
- Build clean and **1394/1394** unit tests green on the merge result at the time of merge.

---

## 3. Unanswered questions to the user — none

One question was asked (`ask_user_question`, scope of the NAT work) and it was answered: **"Diagnostics + flip the UPnP default"**, which is exactly what shipped. The user also explicitly declined, for now, the dedicated-server and relay options. No question from this manager is outstanding.

---

## 4. Transcript-only knowledge

All of the following was persisted to the manager log before this report. Repeated here so the lead does not have to open the log.

### `wt/desync-guard` — **MERGED. Not abandoned, not lost.**

It landed at `b1178706` ("Merge wt/desync-guard: make the next multiplayer desync diagnosable") and reached origin via `d8b8d4d3` ("Merge origin/main: desync-guard and net-diagnostics line from the other machine"). Verified: `b1178706` is an ancestor of current main. The branch does not exist locally **because it was merged and cleaned up**, which is the expected end state — its absence is not evidence of loss.

**It was never this manager's work.** It appeared in main's history mid-session, from the other machine, and this manager only observed it while checking whether its own worktree base was current.

### Does Detectable's synced condition token supersede this session's desync findings?

**No — because this session produced none.** There is nothing to supersede. The desync thread was never owned here; the adjacency is only that both touch `engine/OpenRA.Game/Network/`.

One thing worth stating positively, so a future session does not have to re-derive it: **the NAT change is desync-neutral by construction.** Nothing in `Nat.cs`, `NetworkDiagnostics.cs` or `MasterServerPinger.cs` touches the simulation, the RNG, or any trait Info field. The adversarial reviewer verified exactly this as part of the `@stable` drift policy check. The restore-desync work and this work cannot interact.

### Checkout hygiene hazard (shared repo)

At one point during this session, local `main` was **14 commits behind `origin/main`** while the remote-tracking ref was fully current — fetches were landing but the branch was never fast-forwarded. A worker briefed on "current main" in that state would research stale code and report it as fact. **Check `git status -sb`, not just `git log`, before dispatching a worker whose task depends on repo state.**

### Diagnosing "Game has not been advertised online" (the incident that started this work)

- **Trap:** the lobby prints "Master server communication established" **even on failure** — it appeared directly above the failure lines in the original report. It is *not* a success signal. The real signal is the *absence* of "Server port is not accessible from the internet".
- The only trustworthy check is external: `Invoke-RestMethod https://ifconfig.co/port/1234` with a lobby open.
- Root cause that day: the router forwarded external TCP 1234 to a stale LAN IP (`192.168.0.21`) while the machine was `192.168.0.35`. Firewall and CGNAT were both ruled out — public IP `83.251.244.220` is routable, and `openra.exe` already had enabled inbound Allow rules on the Public profile.
- Residual user-side action, never completed: **a DHCP reservation for `192.168.0.35`**, without which a lease shuffle re-breaks the port-forward rule identically. An end-to-end join by a second machine was also never performed.

### Unverified path in the shipped code

The VPN-reject branch of `GetLocalAddress()` was never exercised with a tunnel actually winning the route. The filter is confirmed only in the pass-through direction (it returns `192.168.0.35` on this machine). To close it: select a Tailscale exit node and re-run `LocalAddressIsPrivateOrNothing` — `GetLocalAddress()` should go null and the lobby should degrade to naming no address. Nobody has hosted a genuinely failing lobby with this build either, so the four-line lobby output is read off the enqueue order rather than seen on screen.
