### 86. The ambush lane takes 2 of 3 units at the opening and leaves offense below its own floor

`[DOCTRINE RULING NEEDED before any code — every candidate fix is on a trait live on BOTH profiles at match opening]`

**Perceived:** in a small opening army the bot's only tank walks 22 cells forward as half of an "ambush pair" and dies there while the rest of the army never leaves the Supply Route.

**Source:** rendezvous diagnosis, worker 37fdcca8, CONFIRMED by run `260906_091912_p10120_test-combined-arms-rendezvous` (item 64 dossier, 2026-09-06). Filed at `main @ e8e57ada`.

---

#### What the log proves (main @ b6207b9b tree)

`[exp-staging] hold-under-min pool=1 min=2` — offense holds the tank correctly; then at t200 `[exp-ambush] lane … post=28,12 units=2` and `[exp-ledger] free=1 held=2 by=ambush:2`: `LaneAmbushBotModule` takes two of the three eligible units (the tank among them) and posts them 40% of the way to the enemy SR, with **zero `retire` lines** all run. The tank walks 8,16 → 20,14 and dies at t585 (killer unlogged — hypothesis: Russia's 2-unit flank via 32,19). `ai.yaml:1046-1047` predicted it verbatim: *"at the opening the lane takes the entire army"*. `MinUnitsPerAmbush: 2` (bb89f9fd) turned one lone forward unit into a forward pair — it did not remove the behaviour.

#### Candidate rulings (pick one; each is a doctrine change)

(a) **Army-share reserve** — the lane may not take units while offense sits below `FreePoolMinAdvanceUnits`; (b) **danger-aware post cell** — no post beyond a believed-danger threshold; (c) **losing-lane retire** — a lane whose units take damage without contact retires. (a) is the smallest and directly answers the symptom.

**Measurement:** `test-combined-arms-rendezvous` — the tank alive at the rendezvous (scenario now traces positions every 100 ticks and prints the tank's last cell on death, 26aea66a). Related: the ambush block (items 67–71) is USER-GATED — this item is about the lane's *share* at the opening, not about ambush itself.
