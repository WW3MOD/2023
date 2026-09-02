### 76. Wake up the vehicle that stopped fighting

`[SWING — a live behavioural change to every armed vehicle on BOTH bot profiles. Must be measured, not reasoned.]`

**Perceived:** a damaged tank you send to attack drives over, points at the enemy, and then sits
there for the rest of the match. It will not shoot when repaired, will not react when something
drives past, and will not go for ammo even if a supply truck parks beside it. Only a fresh order
from you unsticks it.

**Source:** `WORKSPACE/proposals/260902-safe-wins-and-swings.md` §Tier 2, swing 1 — ranked first
among the swings there, on the grounds that it has the best evidence-to-value ratio in the tier:
every link was read at HEAD, it fires in every match within minutes of first contact, it hits
**human** units as well as bots, and **it is a defect rather than a design proposal** — the
disagreement is about the fix, not about whether something is wrong.

---

#### Mechanism — one root, four consequences

WW3MOD wires its two most common unit states onto armament *pause*, which every engine consumer
treats as a blink you hold aim through. The widest of them is `heavy-damage-attained`, i.e. below
half health (`Health.cs:108-109`).

1. **The unit goes sensor-blind.** `AttackBase.GetMaximumRange()` skips paused armaments —
   `if (armament.IsTraitPaused) continue;` at `:596-597` — and returns `WDist.Zero` when all are
   paused. `AutoTarget` uses it as its scan radius whenever `ScanRadius` is unset:
   `Info.ScanRadius > 0 ? WDist.FromCells(Info.ScanRadius) : ab.GetMaximumRange()`, at
   `AutoTarget.cs:1114` and `:1177`. **No vehicle in the mod sets `ScanRadius`** — the only two
   template hits are `infantry.yaml:310` and `:2423`, plus four dev-map/scenario overrides.
2. **An attack order given to it never ends.** `AbandonWhenArmamentsPaused` defaults `false`
   (`AttackBase.cs:72`) and **exactly one actor in the mod opts in** — the medic,
   `infantry.yaml:2314`. Without it the order is accepted: the unit closes, aims, fires nothing, and
   never goes idle.
3. **So it never asks for resupply again.** `AmmoPool` declares
   `INotifyCreated, INotifyAttack, INotifyBecomingIdle, IResolveOrder, ISync` (`AmmoPool.cs:268`) —
   **no `ITick`**. A unit already idle never re-fires the becoming-idle transition.
4. **The one readout built for this cannot fire.** `WithHoldingFireDecoration` reads
   `AutoTarget.LastHeldFireTick`, stamped only inside the `targetsInRange` loop — which is empty
   when the scan radius is zero.

#### Citation that proves it does not exist

The four above are each a read line. The tightest single one: `grep -rn "AbandonWhenArmamentsPaused"
mods/` returns **exactly one line**, `infantry.yaml:2314` — re-verified 2026-09-02 in this worktree.

The `wt/paused-cursor` work that merged at `4bbd0fad` fixed the **cursor** only — it added
`RefusesForPause`, consumed at `AttackBase.cs:860` and `:903` — and the doc comment immediately above
the first call site (`:853-859`) states that without the opt-in *"the order is then accepted and the
unit really does close and aim, so a refusal here would be the mirror lie."* **Do not read
`4bbd0fad` as having addressed this item**; it is the same subject and a different half.

#### What makes it a bet

It is a live behavioural change to **every armed vehicle on both bot profiles**, so `@stable` moves
by construction and the next benchmark baseline must be re-taken knowingly (CLAUDE.md's `@stable`
policy: this is a deliberate, visible improvement flowing to the control, which is allowed — but it
must be *said* in the commit message).

Worse, the system has a documented **accidental rescue**: autotargeting is currently the only thing
that makes a dry vehicle re-check resupply, so anything that changes the idle/non-idle rhythm can
move behaviour in a direction nobody predicted.

**This must be measured, not reasoned.** It needs its own RED/GREEN pair, and the RED is stageable
without a launch: a tank at Heavy damage with a **full magazine** (so the ammo guards cannot rescue
it), ordered to attack, asserted with `TestHarness.HoldsAttackActivity`.

#### Size

Three small independent changes; medium overall, **dominated by measurement rather than code**.

#### Related

- Safe win 5 / the half-health readout (`wt/damage-readout`) presents the *same* threshold to the
  player. This item changes what the threshold does; that one explains it. They touch the same
  condition and should not be measured in the same run.
- Item 40 (danger-scale rework) also moves both profiles by construction and also gates the
  benchmark re-baseline (item 43). If both land before a baseline is taken, the baseline cannot
  attribute a change to either.
