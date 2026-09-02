### 73. (Post-release, user-requested record) Multi-block interconnected buildings — destroy part of a structure, occupants relocate inside it

**Perceived:** shelling a building drives its garrison from one wing into another instead of doing
nothing visible until the whole thing is rubble. A large building becomes a place that is fought
through room by room, rather than a single hit-point pool with men attached.

_**The user, verbatim (2026-09-01):** "Ideally I would like to create buildings that consists of
multiple blocks, that all are interconnected so that parts of a building can be destroyed, and
soldiers are forced to relocate to other parts of the building, etc. But that is all a lot of work
and now we are in a development phase of trying to get our initial release ready."_

**Explicitly POST-RELEASE, and large.** The user classified it as "a lot of work" in the same
breath as proposing it. This record exists so the idea is not lost and not re-litigated from
scratch; it is not a queued task.

---

#### Why this is genuinely big, stated concretely so nobody under-estimates it

Four load-bearing pieces of today's model each assume **one building = one indivisible actor**:

1. **Ports are fixed at actor creation.** `GarrisonManagerInfo.Ports` is loaded once by `LoadPorts`
   (`GarrisonManager.cs:112-127`) and `PortStates[]` is sized in the constructor (`:210-213`).
   There is no runtime path to add or remove a firing port. "This wing collapsed, its two windows
   are gone" has no representation today.
2. **Capacity is a plain int.** `CargoInfo.MaxWeight` (`Cargo.cs:33`) is `readonly int` with no
   condition hook and no capacity-modifier interface anywhere in the trait. "Half the building is
   gone so it holds half as many" needs new engine plumbing.
3. **Health is one pool.** Per-block damage means either several actors that must be kept coherent,
   or a sub-actor damage model that does not exist.
4. **There is no inside.** Shelter occupants are out of the world entirely (`Cargo`), and port
   occupants are placed at the building's own `Location` by `DeployToPort`. "Relocate to another
   part of the building" implies interior positions the engine currently has no concept of.

#### The cheap approximation, if this is ever wanted without the full build

A multi-actor cluster — several adjacent normal garrisonable buildings that a future trait treats
as one logical structure — gets most of the *player-visible* behaviour (part collapses, men move
to the neighbouring part) without per-block health inside a single actor. Occupants would relocate
by ordinary exit-and-enter rather than by an interior move. **Untested and unscoped; recorded as a
direction, not a plan.** Note it would inherit the existing exit behaviour, including the fact
that a bailing or ejected occupant steps into the open.

#### Traps a future session must know before touching this

- **Garrisoning transfers ownership.** `DynamicOwnership` defaults true
  (`GarrisonManager.cs:89`) and a building is claimed on entry while it is Neutral (`:256-261`).
  A multi-block structure spanning several actors would need a single coherent answer for who owns
  the *structure* when different blocks hold different players' men. `CheckOwnershipAfterExit`
  (`:299-331`) already transfers to `remainingOwners.First()` out of a `HashSet<Player>` —
  unordered — which is tolerable today only because entry is allied-or-neutral only
  (`EnterAlliedActorTargeter.cs:49`). Multiply the actors and that assumption needs re-checking.
- **`Cargo` relays owner changes onto passengers.** `Cargo.cs:1204-1211` calls
  `p.ChangeOwner(newOwner)` for every passenger when the building changes hands. Any cluster design
  that moves men between actors of differing ownership will convert them silently.
- **Do not assume Red Alert building behaviour applies.** Per `CLAUDE.md`, the gameplay model is
  rebuilt; verify against the current traits rather than OpenRA norms.

#### Related

- Item 72 (generated damage-state sprites) is the same user message's first idea, far smaller, and
  is a presentation change rather than a simulation one.
- `WORKSPACE/garrison-destructibility-260901.md` — the 2026-09-01 audit and destructibility ruling
  this sits under, including why the death-content on these buildings is deliberately unreachable.
