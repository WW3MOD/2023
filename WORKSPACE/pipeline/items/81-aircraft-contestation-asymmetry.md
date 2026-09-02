### 81. Enemy aircraft cannot contest a Supply Route; friendly aircraft defend one

`[SWING — ONE-LINE DIFF, and the clearest case in the source document of a cheap diff that is NOT a safe win. The two possible fixes point in OPPOSITE balance directions. USER CALL, present as a question rather than a diff.]`

**Perceived:** you park a gunship over the enemy beachhead. The bar does not move — and the panel
that told you to *"park units inside the ring"* (`ingame-info-howtoplay.yaml:116`) gave no hint your
most mobile unit is exempt. Meanwhile a friendly gunship hovering over *your* SR counts its full
purchase price as defensive value and triples your recovery.

**Source:** `WORKSPACE/proposals/260902-safe-wins-and-swings.md` §Tier 2, swing 6. Filed 2026-09-02.

---

#### Mechanism

`SupplyRouteContestation.IsRelevantActor` (`:243-263`) applies **two different tests** depending on
which side the actor is on:

```csharp
if (rel == PlayerRelationship.Enemy)
{
    var pc = a.Info.TraitInfoOrDefault<ProximityCaptorInfo>();
    return pc != null && pc.Types.Overlaps(info.CaptorTypes);   // :252-253
}

if (rel == PlayerRelationship.Ally)
{
    var valued = a.Info.TraitInfoOrDefault<ValuedInfo>();
    return valued != null && valued.Cost > 0;                   // :258-259
}
```

**An enemy actor must carry a matching captor type. An allied actor need only cost money.**

#### Citation that proves the asymmetry is live

`CaptorTypes` defaults to `{Player, Vehicle, Tank, Infantry}` (`:33`) and is **not overridden on
`SUPPLYROUTE`** — the trait block at `structures.yaml:303-316` contains no `CaptorTypes`, and the
only occurrence anywhere in `mods/` is `misc.yaml:442`, on a different actor. **Re-verified
2026-09-02 in this worktree: `grep -rn 'CaptorTypes' mods/` returns exactly that one line.**

Every aircraft resolves to `Types: Plane` via `^NeutralAirborne` (`aircraft.yaml:76-77`), inherited
by `^Airborne` (`:101`) and thence by both `^Aircraft` and `^Helicopter`. So an **enemy** aircraft
fails the overlap test; an **allied** one never takes that branch and passes on cost alone.

#### Overlap, stated

`WORKSPACE/audit/260816-systems-completeness.md:448` already carries *"[POLISH] Aircraft,
helicopters and ships cannot contest"*. **What is new here is the ally/enemy asymmetry**, which that
entry does not mention — it treats aircraft as *absent* from contestation when they are in fact
**present and one-sided.**

#### What makes it a bet

**The fix is one line either way and the two directions point in opposite balance directions:**

- **Adding `Plane` to `CaptorTypes`** makes helicopters a cheap, hard-to-answer siege tool against a
  beachhead that may have no AA.
- **Excluding allied air from defensive value** is the conservative move.

Whichever is chosen, **it changes siege play at the mod's central mechanic.** This is a user call,
not a manager call, **and it should be presented as a question rather than a diff.**

#### Size

One line of code; **the design call is the whole cost.**

#### Related

- Item 79 (contestation entry displacement) changes the same trait's consequences. Do not land both
  without a baseline between them.
- Recovery arithmetic for whoever sizes the "friendly gunship triples recovery" half:
  `FriendlyRecoveryMultiplier` is `3` (`:72`, YAML `structures.yaml:313`), consumed at `:476` and
  `:496`, against `BaseRecoveryTicks: 3000` = **180 s** (not 120 s — the 25 tps assumption is wrong;
  see CLAUDE.md).
