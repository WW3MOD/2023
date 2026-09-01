### 72. (Post-release, user-requested record) AI-generated intermediate damage-state sprites for garrisonable buildings

**Perceived:** a building you are shelling visibly falls apart in stages instead of snapping from
"intact" to "damaged" and then sitting unchanged while its cover quietly collapses. The player can
*see* how safe a building still is to occupy, rather than having to read a number in a panel.

_**The user, verbatim (2026-09-01), on the same message that ruled destructibility "leave it
as-is for now":** "The artwork is also very limited, offering only healthy/damaged sprites. Ideally
I would like to use the artwork that exists, run it through some AI generative process and produce
more sprites of various states."_

**Explicitly POST-RELEASE.** The same message ends *"But that is all a lot of work and now we are
in a development phase of trying to get our initial release ready."* This item exists to be
findable, not to be scheduled. Do not pull it into v1.

---

#### Why this is worth recording rather than doing now

The art limit is the *reason* the rubble model has to be a single terminal step. There are two
sprite states, so there can be two visual states, so a continuous damage curve has nowhere to
render itself.

**Note the dependency direction, because it is the opposite of what it looks like.** The engine
side does **not** wait on the art:

- Damage is already capped to a terminal 1 HP rubble state — `GarrisonManager.cs:1415-1435`.
- Occupant protection already interpolates continuously with HP and already has a distinct rubble
  tier — `GarrisonProtection.cs:63-74`, shipped values at `civilian.yaml:115-119`.

So the *simulation* already has a gradient; only the *presentation* is binary. Proposal P2 in
[`WORKSPACE/garrison-destructibility-260901.md`](../../garrison-destructibility-260901.md) sharpens
that gradient in YAML alone and is worth doing with the current art. **This item would make P2's
gradient legible; it is not a prerequisite for it.** Anyone who reaches this item having skipped P2
has the order backwards.

#### What a future session would need to establish first

1. **How many states the sequence system will take.** `RenderSprites`/`WithSpriteBody` and the
   damage-state sequence wiring decide whether 3–4 intermediate frames are even addressable, or
   whether the mod is structurally limited to healthy/damaged. **Unverified — establish before
   estimating anything.**
2. **Whether generated frames can hold palette discipline.** `^CivBuilding` sets
   `RenderSprites: Palette: player` (`civilian.yaml:24-25`) and `^DesertCivBuilding` overrides to
   `desert` (`:131-132`). Generated art that does not sit in the mod's palette will read as foreign
   regardless of quality.
3. **The blast radius: 41 `^CivBuilding` descendants plus GTWR/PBOX/HBOX.** A per-actor art task
   at that count is the bulk of the cost, not the generation itself.

#### Related

- Item 73 (multi-block buildings) is the same user message's second idea and is much larger. This
  one is a presentation change; 73 is a simulation change.
- `WORKSPACE/garrison-destructibility-260901.md` — the audit that establishes what the engine
  already does, and the destructibility ruling this item sits under.
