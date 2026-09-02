### 77. The enemy Supply Route promises a move order and a health bar, and honours neither

`[SWING — the cheap half is hours; the valuable half is a design decision and is USER-GATED by its own shape]`

**Perceived:** the enemy Supply Route is the most obvious target on the map. You select your whole
armoured force and right-click it. The cursor says *move*. Your army drives across the map, parks on
it, and stands there being shot at, firing at nothing.

**Source:** `WORKSPACE/proposals/260902-safe-wins-and-swings.md` §Tier 2, swing 2. Filed 2026-09-02.

---

#### Mechanism

`structures.yaml:296-297` gives `SUPPLYROUTE` a `Targetable` whose **entire** type list is
`NoAutoTarget`, and `Armor: Type: Indestructable` at `:317-318`. No weapon in the mod lists
`NoAutoTarget` in `ValidTargets` — `grep -rn "NoAutoTarget" mods/ww3mod/rules/weapons/` returns
**zero files**.

So `ChooseArmamentsForTarget` finds nothing and `AttackBase.cs:845-846` refuses
(`if (!armaments.Any()) return false;`). With zero accepters,
`OrderFallbackMath.SelectionSuppressesRefusers` (`:106-109`) returns false, the retry re-resolves
against the terrain cell, and a **Move** is admitted. Because `GetCursor` runs through the same
resolver, **the move cursor is drawn before the click** — the promise is made in advance.

Two details sharpen it. `structures.yaml:294-295` gives it `Health: HP: 75000` and it carries
`SelectionDecorations:` (`:231`), so it renders a permanent health bar advertising a
destructibility that does not exist. And it is the **only** actor in the mod whose target list is
`NoAutoTarget` alone — every other user pairs it with real types (`structures.yaml:143`,
`structures-defenses.yaml:58`, `civilian.yaml:443`, the husk files, `misc.yaml:418`).

#### Citation, with an honest correction to the audit that filed it

The originating audit claims *"Nothing ever told you the building cannot be damaged."* **That is
false and the proposal explicitly refused to carry it.** The How To Play panel says it in those
words at `chrome/ingame-info-howtoplay.yaml:88-95`: *"You cannot build it, move it, or destroy
anyone's. Supply Routes are indestructible."*

**The live defect is narrower and still real: the panel says one thing and the cursor promises the
opposite at the moment of the click.**

Nothing in `PIPELINE.md` covers it — R12 is the supply cache, R9 is the panel's contestation
wording, item 17 is capture and is parked.

#### What makes it a bet

The cheap half — a blocked cursor, and suppressing a health bar nothing can spend — only removes
confusion and is hours.

**The valuable half is making the click *mean* something**, e.g. resolving an attack order on an
enemy SR into an attack-move to its contestation ring, teaching *"you surround this, you don't shell
it."* That is a real design decision **and it is the same shape as the sin `Passenger.cs:116-121`
was reverted for** — silently reinterpreting one order as another.

**It has to be visible** (distinct cursor, distinct target line) or it repeats a mistake this
project has already made and ruled on once. Do not ship the reinterpretation silently.

#### Size

Cheap half: hours. Real half: medium, and gated on a user ruling.

#### Related

- `SupplyRouteContestation` is the mechanic the "real half" would teach. Per CLAUDE.md, **contesting
  is not capturing and ownership never transfers** — any copy or cursor written here must not imply
  the SR can be taken.
- Item 17 (SR capture wiring) is user-deferred and is a *different* mechanic. Do not let this item
  drift into it.
