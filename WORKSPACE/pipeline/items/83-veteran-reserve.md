### 83. The reserve remembers — veterans come back as veterans

`[SWING — LARGE. The actor plumbing exists; the ledger, the surface and the price rule do not. Reason (ii) below could kill it outright.]`

**Perceived:** your Abrams has three gold chevrons, twenty minutes of life, and no ammo. You pull it
out. Instead of vanishing for scrap it appears as a reserve unit — *"Abrams (Veteran III)"* —
cheaper than a fresh one, arriving with its chevrons and a full magazine. **The verb stops being a
euphemism: today "rotate out" means *sell*.**

**Source:** `WORKSPACE/proposals/260902-safe-wins-and-swings.md` §Tier 2, swing 8. Filed 2026-09-02.

---

#### Why it is worth doing

It closes **the largest gap between what this game says it is and what it does.**
`DOCS/reference/supply-route.md` calls the SR the place units *"muster after being deployed in from
off-map reserves"* — **and there are no reserves.**

It also fixes a real economic hole: **veterancy is the only thing in this economy that appreciates,
and the refund arithmetic cannot see it**, so the correct play with a veteran is never to rotate it.

#### Citation that proves it does not exist

`grep -c "Experience\|Level\|Rank"
engine/OpenRA.Mods.Common/Traits/CustomSellValue.cs` returns **0**; the same grep on
`Activities/RotateToEdge.cs` returns **0**. **Both re-verified 2026-09-02 in this worktree.**

`GetSellValue` (`CustomSellValue.cs:28`) reads only `CustomSellValueInfo.Value` or `ValuedInfo.Cost`
minus missing ammo and supply, and `RotateToEdge` ends in `self.Dispose()` with **no ledger write on
any branch.**

#### What makes it a bet — three things, and the second could kill it

1. **Balance.** A reserve that returns a veteran cheap and full is a *stronger* play than keeping it
   fighting, **which inverts the tension the mechanic is for.** It needs a real cost, and that is
   tuning, not coding.
2. ⚠️ **Sidebar scope — this is the one that could kill it.** Reserves need a UI surface that does
   not exist, and **the same class of work is already an open, unstarted thread**: *"Cargo Phase 3 —
   template sidebar"* (`RELEASE_V1.md:138`, one line with zero code hits; see item R16's verdict,
   which rules it *"too vague to dispatch"* and needing a design pass rather than a worker).
   **If that is hard, this is hard for the same reason, and for the same missing design.**
3. **`ProducibleWithLevel` is prerequisite-gated, not order-gated**, so it does not model "this unit
   at this rank". Either accept coarse rank tiers or write a new init path. **Do not assume the
   trait drops in.**

#### Size

**Large.** The actor plumbing exists; the ledger, the surface and the price rule do not.

#### Related

- Item 77 / safe win 6 (`wt/evac-refund`, putting a number on the Evacuate button) is the *readout*
  for the refund this item would change the value of. If both are wanted, **this one changes the
  number the other displays** — sequence accordingly.
- ⚠️ **Do not conflate "Cargo Phase 3" with `260722_phase3_redteam.md`**, which is the AI
  tactical-positioning phase and unrelated (per R16's verdict).
