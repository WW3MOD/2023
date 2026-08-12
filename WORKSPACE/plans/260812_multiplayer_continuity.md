# Multiplayer continuity — disconnects, rejoin, and claimable slots

**Status: DOCUMENTED, NOT SCHEDULED.** Written 2026-08-12 at user request after a discussion, with the explicit instruction *"lets not implement it now, plenty of more important things to do first."* Nothing here is due. Its job is to make the decision cheap when there **is** a reason to pick it up.

Grounded against `4d3c8f90`. Every code claim below was read, not remembered — but note the dates: if you are reading this months later, re-verify before trusting it.

---

## 1. The one thing to understand first

The request "let players join or rejoin a game in progress" contains **two features whose costs differ by roughly an order of magnitude**, and they are separable.

| | What it needs | Cost |
|---|---|---|
| **Continuity** — a disconnect doesn't ruin the match | Nothing transferred. Every client already simulates the dropped player's units, budget and Supply Route. Only *who issues orders* changes. | Tractable |
| **Admission** — a client that wasn't there joins an in-progress match | The joiner has nothing and must reach a bit-identical world state. | Large |

Both of the user's scenarios (disconnect→rejoin, claimable bot slots) collapse onto the same hard middle: **admission**. The differences between the scenarios are cheap; the shared middle is the whole bill.

**The practical consequence: continuity is deliverable on its own timeline and takes the worst outcome off the table without ever solving admission.** Do not let the two be scoped as one feature.

---

## 2. What already exists (more than expected)

- **The server already records the full order stream** when game-save is enabled — `Server.cs:136` holds a `GameSave`, and `Server.cs:972` dispatches every client order into it. Admission's state-transfer channel is therefore already half-built.
- **The server already knows the exact frame a client dropped at** — `Server.cs:1269` sets `player.DisconnectFrame = toDrop.LastOrdersFrame + 1`. The determinism boundary is recorded for free.
- **Disconnect is already a first-class event** with distinct notifications for player / teammate / observer / lobby (`Server.cs:102-111`), and `OnConnectionDisconnect` at `:437`.
- **Ownership transfer is well supported.** Many traits already handle an owner change correctly — `StoresPlayerResources`, `Power`, `InfersUpkeep`, `CashTrickler`, `Health`. `TemporaryOwnerManager` implements **reversible** transfer, which is the mechanism a rejoin-and-reclaim wants.
- **Lobby options are a cheap, well-trodden trait pattern** — implement `ILobbyOptions`; see `CrateSpawner`, `MapBuildRadius`, `LobbyDummyOptions`. A disconnect-policy dropdown is a small piece of work, not a design project.
- **Bot takeover is less risky than feared:** only **2 of 54** bot modules do any work at `WorldLoaded` (`ThreatMapManager`, `HarvesterBotModule`). Most tolerate being attached to a player mid-match.
- **The save format has an escape hatch for non-replayable state** — the file carries a trait-data section alongside the order stream (`GameSave.AddTraitData`), so state that genuinely cannot be reconstructed by replay has somewhere to live.

---

## 3. What is actually hard: admission requires determinism we do not yet have

**Restore is a replay from frame 0, not a snapshot.** The save file is literally "a list of orders in network frame format" plus metadata. Two consequences:

1. **Cost scales with match length.** The slow saved-game load the user reported is this, and it is not optimisable — it is the architecture. An admitted client must replay the whole match. Replay outruns real time so it converges, but for a long match it is a genuine wait, and in the claimable-slot case the match keeps advancing while the joiner chases it.

2. **Replay must reproduce an identical world, and ours provably did not.** The user's stuck-pause bug was exactly this: the restore replayed, **one synced field diverged**, the validating sync-hash comparison failed, and the game latched itself permanently unresumable. The field was a condition-token allocation handle in WW3MOD's own fog code — one of the only `[Sync]`-marked condition tokens in the engine. Three actors out of 34,084 lines of state, each off by exactly one. It took instrumented runs to find.

**That is the honest measure of difficulty.** Admission converts every latent determinism bug into "players cannot join". This is a total conversion with ~264 modified engine files, and the one bug we found was in our code, not upstream's.

**The counterweight:** the instrumentation this work needs now exists — `Test.ForceSyncReports` (overriding the `humanClients > 1` floor at `OrderManager.cs:107-112`), a per-frame sync-report dump, and an in-process save→restore probe with a reproducing scenario. The marginal cost of attempting admission dropped materially in August 2026.

**Cheapest honest proving ground:** get save→restore of a long match to validate *reliably*, repeatedly, on real maps. If it does, admission is mostly plumbing. If it does not, admission was never going to work — and we learn that for the price of a test run instead of a feature.

---

## 4. Disconnect policy as a lobby rule (user's proposal, 2026-08-12)

The user's framing — *a menu of disconnect behaviours, chosen in the lobby* — is the right shape, and everything in it is **continuity**, so none of it is gated on admission.

Candidate policies:

- **Pause and wait** — show who dropped, let the remaining players wait or vote to proceed.
- **Bot takeover** — the dropped player keeps playing, badly. Cheapest meaningful option.
- **Transfer to a teammate** — units, budget and Supply Route(s) pass to an ally.
- **Eliminate** — current behaviour.

### Why transfer fits WW3MOD unusually well

There are no factories and no tech tree, so a transfer avoids the mess it would be in stock RA — no production queues to merge, no build state to reconcile. The economy is budget allocation plus the Supply Route, and **a player holding two Supply Routes is already a representable state**: capturing neutral SRs is part of the game model. "Inherit my teammate's beachhead" maps onto existing concepts rather than requiring new ones. See `DOCS/reference/game-model.md` and `DOCS/reference/supply-route.md`.

### Three design constraints, in priority order

**(a) This lives in the code that misfired two days earlier.** `wt/elimination-cascade` (`f49b6aca`, 2026-08-10) fixed a bug where destroying one bot's Supply Route marked **every player slotted after them** as defeated: `ResolveTeamElimination` mutated `WinState` inside a loop whose team-membership test *read* `WinState`, and a `Lost` player is instantly `Spectating`, and `RelationshipWith` returns `Ally` for any `Spectating` player before consulting alliance masks. The fix snapshots membership before the first `MarkFailed` and uses `SameTeam` (alliance masks, immune to `Spectating`) instead of `IsAlliedWith`.

Every disconnect policy must decide **what the dropped player now is** — lost, spectating, present-but-bot-driven, or gone-and-inherited. That is precisely the axis that just produced a cascade bug. Use the alliance-mask discipline that fix introduced; do not write a fresh loop over players.

**(b) The selection rule must read synced state.** "Highest-scoring teammate" must resolve identically on every client or it is a desync — and score is exactly the kind of value that may derive from display statistics rather than simulation state. Slot order, alliance mask, or an heir nominated in the lobby are trivially safe. **Prefer the boring selector**; see §3 for why we are not casual about this.

The takeover/transfer moment itself must be an **order in the stream**, not a local decision, for the same reason.

**(c) The balance question is bigger than the technical one.** A dropped player converted to a bot leaves a nominal 2v2. Transferring their army and income to a teammate makes it 1v2 with doubled resources — frequently **stronger**, and in team games that creates a real incentive to **drop deliberately to consolidate**. Choose the policy set for what it does to a 2v2, not for what feels fairest to the individual who left.

This argues for: conservative default (pause → bot), transfer available for groups who want it, and **transfer implemented reversibly** (`TemporaryOwnerManager`) so a returning player is not permanently robbed by a bad connection.

---

## 5. Recommended ordering, if this is ever picked up

1. **Bot takeover on disconnect.** No state transfer, small blast radius, removes the worst outcome.
2. **Disconnect-policy lobby option** with pause/bot/eliminate. Cheap plumbing on a known pattern.
3. **Transfer-to-teammate**, reversible, with a synced selector — only after (1) and (2) have proven the win-state handling is sound.
4. **Reliable save→restore of long matches on real maps.** The gate. Not a feature — a measurement.
5. **Admission** (rejoin, claimable slots) — only if (4) comes back clean.

Steps 1–3 deliver most of the felt value and never touch replay.

---

## 6. Known unknowns

- Whether upstream OpenRA has since solved any of this. Not checked; worth ten minutes before starting.
- Whether the shipped desync fix (`e1bbf244`) actually holds — its own author records it as unconfirmed against the live bug. A confirmation run against `test-savegame-resume-riverzeta` was in flight when this was written; **check its outcome before trusting §3's optimism about the instrumentation being sufficient.**
- How much slower than real time replay actually is. Nobody has measured the ratio, and it decides whether catch-up-while-live is viable at all for claimable slots.
