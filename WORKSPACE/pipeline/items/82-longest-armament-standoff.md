### 82. Let a specialist stand off at its long weapon's range

`[SWING — SEVEN YAML LINES, and it is a balance change on seven multi-role units. Wants `tools/combat-sim/` numbers BEFORE, not after, and should reach the user as a proposal rather than a merge.]`

**Perceived:** your Bradley has anti-tank missiles that reach a long way. Ordered at a tank, it
drives all the way in to autocannon range first — into the tank's own gun — and only then starts
shooting.

**Source:** `WORKSPACE/proposals/260902-safe-wins-and-swings.md` §Tier 2, swing 7. Filed 2026-09-02.

---

#### Mechanism, in the engine's own words

`AttackBase.EngageAtLongestArmamentRange` defaults `false` (`AttackBase.cs:82`) and the doc comment
at `:721-724` names the symptom in the player's words:

> *"a unit whose long-range weapon is the RIGHT weapon closes to its short-range weapon's band
> anyway, and the player sees it refuse the good weapon and drive at the target."*

**The shipped default takes the *minimum* of every valid armament's range.**

#### Citation that proves it does not exist

`grep -rn "EngageAtLongestArmamentRange" mods/` returns **exactly one YAML hit** —
`vehicles-russia.yaml:959`, on `tunguska` — plus one comment in `weapons-ballistics.yaml:716`.
**Re-verified 2026-09-02 in this worktree.** Every other multi-armament actor in the game is on the
default.

**The dry case is already handled**, which is the trap that would otherwise strand a missile-less
Tunguska: the longest branch ignores paused armaments and falls back only when all are paused
(documented at `:726-730`).

#### What makes it a bet

Seven YAML lines that are **a balance change on seven multi-role units**, two of them AA platforms
where standoff matters most. **It makes those units meaningfully stronger.**

Per the standing rule in `DOCS/recipes/BALANCE.md` this wants `tools/combat-sim/` numbers **before,
not after**, and per the user's 2026-08-02 ruling carried by item 32 — *"I do not want you to change
any unit stats without my explicit review and approval"* — **it should reach the user as a proposal
rather than a merge.** A range-engagement change is a stat change in everything but name.

#### Size

Trivial diff, medium work — **the cost is entirely simulation and review.**

#### Related

- Item 76 (wake up the vehicle that stopped fighting) also lives in `AttackBase` and also touches
  paused-armament handling. This item *relies* on the existing paused-armament fallback at
  `:726-730` behaving as documented; item 76 changes what "paused" means for autotargeting. **Land
  one, re-read the other.**
- Item 32's balance-proposal flow (`WORKSPACE/balance/`, numbered docs, individual user sign-off) is
  the mechanism this should go through.
