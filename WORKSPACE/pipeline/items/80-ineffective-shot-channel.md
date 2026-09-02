### 80. A player-facing channel for "your shot did nothing"

`[SWING — the detector already runs in every shipped build; the work is a routing change plus a design decision. HARD CONSTRAINT: do NOT do this by turning `DamageNumbers` on.]`

**Perceived:** a shot that connects and accomplishes nothing looks identical to a shot that hurt.
There is no health bar and the only health indicator is a four-band pip, so against a high-HP vehicle
dozens of consecutive hits change nothing visible. A player firing the wrong weapon at the wrong
armour gets no signal at all.

**Source:** `WORKSPACE/proposals/260902-safe-wins-and-swings.md` §Tier 2, swing 5. Filed 2026-09-02.

---

#### Mechanism — the detector already runs in every shipped build

`DamageWarhead.InflictDamage` computes `effectiveThickness = thickness * armorPercent / 100`
(`:249`), applies penetration (`:250`), and then runs an anomaly gate on **every warhead application
in the game**:

```csharp
if (effectiveThickness > 0 && HitCheck.LostMostOfItsDamage(damageBeforeArmour, damage))   // :269
```

When it fires loud it already builds `$"ARMOUR {damageBeforeArmour}->{damage}"` (`:288`) and drops a
`FloatingText` on the victim — **gated on `debugVis.DamageNumbers`** (`:286`), which is a developer
checkbox defaulting `false`
(`engine/OpenRA.Game/Traits/World/DebugVisualizations.cs:54`).

**So the arithmetic, the threshold and the per-hit hook all ship. What does not ship is a player-side
surface.**

#### Citation that proves the player cannot see it

That `:286` gate is the proof, together with `DebugVisualizations.cs:54`. The work is a routing
change and a design decision, not a build.

#### What makes it a bet — three things, and the first is a hard constraint

1. ⚠️ **Do not do this by turning `DamageNumbers` on.** That default is guarded by a test and
   turning it on was ruled a **release blocker** (former PIPELINE R17, discharged 2026-08-30). The
   guard `DebugVisualizationDefaultsTest` asserts a `HITCHECK-OVERLAY-DEFAULT-ON` marker entry
   exists in `PIPELINE.md` **if and only if** that default is `true` — so flipping it fails the
   build unless an entry is filed in the same commit. **This must be a separate, player-shaped
   surface.**
2. **The armour path is only *one* reason a shot does nothing.** `Versus` is the other and is
   applied outside the anomaly gate, so **a readout that explains only armour will mislead.**
3. **Victim-side modifiers are unreachable from here.** Garrison cover, veterancy and prone are
   applied later in `Health.InflictDamage` and are not visible to the warhead at all — so the
   channel **can never be a complete explanation, only a true partial one.** That is a design
   constraint to accept up front, not a defect to fix later.

#### Size

Medium, **dominated by the design question of what the player sees and how often** — a per-hit
signal on a machine-gun burst is noise; a per-engagement one is a different feature.

#### Related

- Safe win 5 / the half-health readout (`wt/damage-readout`) is the other half of "the game does not
  explain damage." That one explains a threshold the player crossed; this one explains a shot that
  did nothing. Same complaint, different surfaces.
- Item 62 carries a live `Versus`-table defect (`IskanderTargeter`'s `Warhead@Target` zeroes an
  armour class this ruleset does not have while omitting three it does). **An honest "your shot did
  nothing" channel will surface that defect to players**, so the two should be looked at together.
