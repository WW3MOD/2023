### 85. Lead-hold — tanks pace infantry on a committed axis (item 64's last mechanism)

`[RECORDED, NOT A v1.0 FOCUS. User 2026-09-06 (question aEpyOHRYwtEx0HKX9mFdT, no variant picked), verbatim: "I dont think this is needed as a standalone feature, but maybe it can be worked into a larger change. I have some ideas. You can document this idea too so it is not lost, but it will probably not be a focus before v1.0 release." The 08-15 scope ruling had already declined a behaviour change of this kind.]`

**Perceived:** a combined-arms push arrives at the midline as a group; today the tanks arrive ~700 ticks before the first infantryman and the group spreads 15 cells (gate 8).

**Source:** item 64 remainder, worker 37fdcca8 (26aea66a, a56cf4ab; merged @ 75a68621). Filed at `main @ e8e57ada`.

---

The design lives in **item 64's dossier** (section dated 2026-09-06, "lead-hold design"): three variants, ranked. It is kept here so it can be folded into the larger change the user has in mind, not built on its own.

- **V1 throttle (the agent's recommendation)** — grant a `pace-with-infantry` condition to the fast subset while the axis's already-computed `clumpRadius` is wide, driving a `SpeedMultiplier@Pacing` to ~35%. No wait state, so the deadlock class the item names cannot occur; effect on d2 is arithmetic (~6-cell box vs gate 8). Costs tempo; needs a contact-release; touches shipped unit YAML. ~1–2 days.
- **V2 lead hold proper** — a third caller of the existing `TryOrderHold`; cheapest (~1 day) but a wait state in the slot the module documents as most exposed to churn (100% of today's retires).
- **V3 inverted echelon** — infantry leads, tanks bounded behind; bounds the lead but not the pace (tanks stutter).

**Measurement for any of them:** `test-push-departs-together`, two arms, one seed — d1 ≤ 300 (today ~699), **d2 ≤ 8 (today 15)**. Every variant moves BOTH bot profiles (shared trait at match opening): the baseline must be re-taken after it. The scenario ships `expected-status` fail until something lands.

**Already done — do not re-litigate:** the lone tank (bb89f9fd: `MinUnitsPerAmbush: 2`, `FreePoolMinAdvanceUnits: 2`), mission-held axes reinforced (555cba2b: `MissionReinforceEnabled` both profiles), the "march-back to muster" diagnosis RETRACTED (measured owed=0).
