
- [x] The drones (that are spawned by DR) should be deprioritized when selecting.
    - DONE: `quadcopterdrone` now has `Selectable: Priority: 1` (default 10). In drag-select that includes both infantry and drones, only the higher-priority infantry will be selected. Drones can still be selected on their own.

- [x] Medics, for example, have a problem when ordering a group of soldiers to attack move, the medics cant fire so they jsut keep running towards the enemy and end up getting killed and not being useful.
    - DONE (Option C, per your choice): New trait `AutoFollowAlly`. Activates only when EngagementStance is Defensive (HoldPosition stays put, Hunt is unchanged). On idle and every 25 ticks: picks the nearest allied unit with an `AttackBase` trait within 20c0 and queues a `MoveWithinRange` to within `FollowDistance` (3c0). Skips other auto-followers to prevent medic-pairs trailing each other.
    - `^MEDI` defaults changed: `InitialEngagementStance` Hunt → Defensive, with `AutoFollowAlly` configured (FollowDistance: 3c0, SearchRange: 20c0).
    - Combined effect with the DefensiveRange: 5c0 from the previous TODO item: a Defensive medic now (a) only hunts patients within 5 cells and (b) follows the nearest combat ally when no patient is in range. So they stick with the squad on attack-moves and stop running into the enemy.
    - On Hunt: full heal range, no follow (free roam). On HoldPosition: no targeting, no follow (stay put).

- [x] When I gave three queued orders to three units, and then used shift-G (spread/scatter) they ended up only going to two of those points.
    - PARTIAL: I couldn't 100% pin down the bug from the code alone. Made the waypoint collection more robust — instead of taking the longest chain from a single unit, it now aggregates unique waypoints across ALL selected units. This handles the case where one unit has already finished a waypoint by the time you press Shift+G but slower units still hold it. If you can repro the issue after the next build, please note: how many units, how many waypoints, were any waypoints near each other or near current unit positions?

- [x] When units move over tiles with a lower move speed/mobility according to their "locomotor", they become slow from what seems like the point when they are in the center of the "fast" tile, all the way to when they reach the cetner of the "slow" tile and then they speed up again. So the problem with this is that they start slowing down before they even enter that tile, and sped up before they have left it. We should slow them down the moment they enter the slow tile, and speed up when they exit it.
    - DONE: Root cause was `MovePart.Tick` calling `mobile.MovementSpeedForCell(mobile.ToCell)` for both move halves. `MoveFirstHalf` is the **leaving** segment (cell-center → boundary or boundary → boundary while crossing one cell), so it should use `FromCell`'s speed. `MoveSecondHalf` is the **entering** segment (boundary → cell-center). Added an abstract `SpeedReferenceCell()` method overridden by each subclass. Now slowdown happens exactly when the unit crosses the cell boundary, not from the previous tile's center.

- [x] We should be able to target the Supply Route, and it should be the default order for any unit with a weapon, and that order means Go to the flag and stay there until it is fully captured/ceutralized, so that we can order an "attack" on the SR and then queue up other orders after it is captured.
    - DONE (works for both enemy and allied SRs, per your choice).
    - New trait `AttacksSupplyRoutes` added to the `^AutoTarget` template — every armed unit now has it. Provides a `SupplyRouteOrderTargeter` at order priority 8 (beats AttackBase's 6), so right-clicking an SR queues `AttackSupplyRoute` instead of bouncing off the SR's indestructible-1HP floor.
    - Cursor: `attack` (red) on enemy/neutral SRs, `guard` (green) on allied SRs.
    - New activity `AttackSupplyRoute`: walks the unit into the SR's contestation `Range` (10c0 default), then idles in range. Completion conditions:
      - **Enemy SR**: completes when the SR's owner is no longer playable (defeated / Neutral / NonCombatant). The SR flips to Neutral on owner defeat (per ^Building OwnerLostAction = ChangeOwner), so this naturally fires when you've routed the enemy.
      - **Allied SR**: completes when `NetEnemySurplus <= 0` AND `ControlBarFraction >= 95` — i.e. no enemies are pressing AND the bar has recovered.
      - **Neutral SR**: completes when `NetEnemySurplus <= 0`.
    - `SupplyRouteContestation` got public accessors (`ControlBar`, `ControlBarFraction`, `NetEnemySurplus`, `NetFriendlySurplus`, `IsPassive`, `Info`) so the activity can read state.
    - Order is queueable — Shift+right-click queues "stay until resolved, then do the next thing".

- [x] Units that are auto resupplying/evacuating are sometimes not showing the waypoint line towards their target. Maybe it happens only when they are on auto and targeting a supplytruck or something, not sure. And by the way I think they try to follow empty trucks now, not sure but check this. They should only target trucks with supply left, and if that changes (the truck runs out of supply during) they should stop and switch target
    - DONE: New `SeekCargoSupply` activity replaces the bare `MoveWithinRange` call. It (1) re-picks the closest CargoSupply truck with supply remaining every 25 ticks, (2) bails to a new target if the current one runs out of supply mid-route, (3) shows a target line via the move's `targetLineColor`, (4) flags `NeedsResupply` if no supply trucks have any supply left.

- [~] Medics on "defensive" stance should still "hunt" for hurt soldiers to heal, but at a reduced range, maybe 5 tiles. The problem arises from the limitation that hunt is very long range, and medics might end up running across the battlefield and getting killed. We want to be able to differentiate between long (hunt), short (defensive) and hold position completely. This is how we have designed the new settings for all units basically, but the defensive stance doesnt seem to make any units move at all. For regular soldiers defensive stance means they can reposition if they need to in order to aquire a targeting solution (LoS), but they should not follow the enemy, instead they jsut moves perhaps a little bit to not be blocked, fire and then return to their original position. Hunt means go after enemies, do no return to the original position.
    - PARTIAL: Done — **medic short-range hunt on Defensive**.
      - Added `DefensiveRange` field to `HealerAutoTarget`; `FindBestTarget`/`FindCriticalUnclaimed` now query the medic's `EngagementStanceValue` and clamp the search radius to `DefensiveRange` (5c0 = 5 cells) when on Defensive. Set `DefensiveRange: 5c0` on `^MEDI` in `infantry.yaml`.
      - On Hunt: full max heal range (current behavior). On Defensive: 5 cells max. On HoldPosition: existing AutoTarget already prevents movement — the medic won't pursue at all.
    - **NEEDS DESIGN DISCUSSION** for **defensive stance reposition + return for combat units**. My proposal (pick or override):
      - **Stash original position** when the unit enters Defensive stance (or on creation if Defensive is the initial stance).
      - When AutoTarget picks a target it can't see (LoS blocked), allow the unit to step 1-3 cells to acquire LoS.
      - After the target dies / leaves range, queue a Move back to the stashed original position.
      - Need a clear answer: should the "origin" be the cell the unit was in when stance was set, or the cell where it last received a player order? I lean the latter (player order = "this is where I want you" intent).
      - Also need: should this trigger on every fire opportunity, or only when there's a high-value target in known-but-unseen range?

- [x] Healing soldiers should be limited to one medic per soldier, so we cant heal faster by adding more and more medics. We also need to make the autotargeting for medics to take this into account. If one soldier is hurt, and we have multiple medics, only the closest one should go there, the other ones should remain in place. So when a soldier is targeted it should be deprioritized somehow by the autotargeter.
    - ALREADY DONE: The `HealerClaimLayer` (world trait) + `HealerAutoTarget` (per-medic) system already implements 1:1 claims. Configured on the medic in `infantry.yaml:2058` with `ClaimTargets: true`. Only one medic claims each patient; others skip claimed targets. Critical patients (<50% HP) get priority and trigger stabilize-and-switch. **Test note:** if you see medics still piling up, it's a bug in the existing system, not a missing feature — please playtest and confirm.

- [x] The Flame troopers burst should be less scattered, so the shots in the burst lands closer together, make it half of the current scatter/spread but the same accuracy otherwise
    - DONE: `Flamespray.Inaccuracy` halved from 768 to 384 (~0.75 cells → ~0.375 cells of random per-shot offset). The deterministic burst-walk pattern (FirstBurstTargetOffset / FollowingBurstTargetOffset) was left unchanged so the spray still fans out the same way, just tighter.
    - Q: did you also want the deterministic walk shrunk (the fan pattern)? If so, halve those too.

- [x] HIMARS and Iskander should one-shot any unit, I think the damage needs to be 3x at the impact point, but maybe even less in the area around as they have a wide spread of their damage. We have a shockwave animation for the nuke, I would like to reuse it here as well but smaller radius obviously. What else can we do along thees lines? Fix the things i mentioned and then give me some suggestions to make them properly balanced and cooler.
    - DONE — three changes per missile in `weapons-explosions.yaml`:
      - **TargetDamage 3x:** Iskander 18000 → 54000, HIMARS 12000 → 36000 (one-shots any unit at impact point).
      - **AoE damage halved:** Iskander spread damage 8000 → 4000, HIMARS spread 5000 → 2500 (since shockwave will pick up the rest).
      - **New ShockwaveDamage warhead:** Iskander 7c0 radius (lighter than nuke's 30c0), HIMARS 4c0 radius. Falloff curves give heavy damage in the inner ring, fading out. Reuses the same `ShockwaveDamage` system as the nuke with smaller alpha values.
    - **Suggested follow-ups for balance + cool factor (your call which to do):**
      - **Suppression on impact** — copy the nuke's `GrantExternalCondition: Condition: suppression-1` warheads at smaller scale (Iskander ~5c0, HIMARS ~3c0). Survivors of the AoE are pinned and can't fire effectively for a few seconds.
      - **Screen shake** — add a small `ShakeScreen` warhead (Duration 15, Intensity 25 for Iskander; smaller for HIMARS). Sells the impact.
      - **Crater + scorch** — `LeaveSmudge` warheads (Crater size 2 for Iskander, 1 for HIMARS; scorch ring around) so the impact leaves a permanent visual reminder.
      - **EMP pulse on Iskander only** — small `empdisable` condition (Range 4c0, Duration 50, vehicles/structures only). Iskander becomes the "soft kill the cluster" weapon, HIMARS is pure burst damage.
      - **Cooldown / cost rebalance** — with one-shot capability, you'll probably want to bump cost or cooldown so spam doesn't trivialize defense. Suggest checking current ReloadAmmoPool / cost values after playtesting.
      - **Different sound + flash** — copy the `FlashPaletteEffect` from nuke at lower intensity (Duration 10, FlashType Light) to give a clear visual cue when a missile lands offscreen.
      - **Targeting reveal** — the targeter weapon could grant a brief vision pulse at the target so the missile is "guided" — adds realism and gives the enemy a few-second warning.

