### 74. Neutralising an enemy building announces it to the Neutral player and to nobody else

`[NEEDS A USER CALL BEFORE ANY CODE — balance-adjacent, deliberately not built]`

**Perceived:** your rifleman spends a full minute inside an enemy AA gun and turns it grey. No voice
line, no text, no sound. On the other side of the map, the player who just lost it is told nothing —
their defence stops working and they find out by noticing.

**Source:** `WORKSPACE/proposals/260902-safe-wins-and-swings.md` §Tier 1, safe win 8. Filed
2026-09-02.

---

#### Why this is filed as a question and not as work

**This is balance-adjacent, not purely UI, and that is the whole reason it is not in flight
alongside its nine siblings.** `DOCS/reference/game-model.md` already records soldier-neutralisation
as close to unanswerable against a bot and tracks it as a live balance risk. **Making it audible
will make players use it more.**

That is arguably the right outcome — an invisible dominant strategy is worse than a visible one, and
this project's standing preference is to price a bad state rather than hide it. But it is a call for
the user, not a thing to ship quietly on the strength of it being a two-line notification. **Put the
question; do not put the diff.**

#### Mechanism

`CaptureActor.cs:134` computes the new owner and passes it to `OnCapture` at `:146`:

```csharp
var newOwner = captures.Info.CaptureToNeutral ? w.WorldActor.Owner : self.Owner;
```

`CaptureNotification` then addresses **that** owner —
`Game.Sound.PlayNotification(..., newOwner, "Speech", info.Notification, faction)` and
`TextNotificationsManager.AddTransientLine(newOwner, info.TextNotification)` (`:73-74`). For a
soldier's neutralise `newOwner` **is the Neutral player**, so the announcement goes to nobody. The
victim's channel is the next two lines (`:77-78`) and is empty by default —
`LoseNotification = null` (`:35`).

#### Citation that proves it does not exist

The trait is applied with bare defaults: `structures.yaml:54` is the whole declaration
(`CaptureNotification:`, immediately followed by `ShakeOnDeath:` at `:55`), and the only other
declaration in the mod sets one field (`vehicles.yaml:111-112`, `Notification: UnitStolen`).
`en.ftl` has zero case-insensitive matches for `captur`.

Re-verified 2026-09-02 in this worktree: `grep -rn 'CaptureNotification\|LoseNotification'
mods/ww3mod/rules/` returns four lines — `structures.yaml:54`, a commented `structures.yaml:236`,
`vehicles.yaml:111`, and `player.yaml:159`. **`player.yaml:159` `LoseNotification: Lose` is a
different trait entirely** (the player's game-lost notification) and is not evidence that the
capture-side channel is wired — do not let a grep hit on the same field name close this.

**The asymmetry is the tell that it is a bug, not a design.** `CaptureToNeutral: true` appears
exactly once in the mod — `infantry.yaml:928` — so this is the soldier path specifically. The
technician path (`CaptureToNeutral` false → `newOwner = self.Owner`) announces correctly.

#### Size

Hours, plus two or three new `en.ftl` strings, which do not exist for anything in this space yet.

#### Related

- `DOCS/reference/game-model.md` — the standing soldier-neutralisation balance risk.
- Safe win 1 / item's siblings: the capture *cursor* work (`wt/capture-affordance`) is a different
  defect on the same verb and does not touch notifications.
