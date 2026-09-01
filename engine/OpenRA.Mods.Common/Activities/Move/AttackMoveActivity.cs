#region Copyright & License Information
/*
 * Copyright 2007-2022 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using OpenRA.Activities;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Activities
{
	public class AttackMoveActivity : Activity
	{
		readonly Func<Activity> getMove;
		readonly bool isAssaultMove;
		readonly AutoTarget autoTarget;
		readonly AttackMove attackMove;

		bool runningMoveActivity = false;
		int token = Actor.InvalidConditionToken;
		Target target = Target.Invalid;
		int checkTick = 0;

		// Stage 2 (PIPELINE item 8): latched once we decide to halt into an ambush. From then on we stop
		// scanning / advancing and only drain the cancelling move child; when it is gone the attack-move
		// completes and the unit drops to idle (AmbushTickIdle owns the ambush from there).
		bool haltedForAmbush = false;

		// Latched once the unit has run itself completely dry. Same drain-then-finish shape as
		// haltedForAmbush: stop scanning and advancing, let the cancelled move child wind down, then
		// end the attack-move so the unit drops to idle.
		bool haltedForEmptyAmmo = false;

		/// <summary>
		/// The point Shift-G replays for this activity — see GroupScatterHotkeyLogic, which documents it
		/// as "the MAIN points the player clicked". For a player order that is the ORDERED cell, stated by
		/// the caller; for internal callers with no order behind them it is inferred from the move.
		/// </summary>
		public readonly CPos? OriginalDestination;

		/// <summary>
		/// For callers with no player order point to state — rally points, paradrops, resupply, Hunt,
		/// Patrol, AttackWander, Reservable. The destination is INFERRED by building the move once and
		/// reading it back, which is only sound when the move's cell is not a per-unit answer.
		/// </summary>
		public AttackMoveActivity(Actor self, Func<Activity> getMove, bool assaultMoving = false)
			: this(self, getMove, assaultMoving, null) { }

		/// <summary>
		/// For a player order, which knows the cell that was clicked and must say so.
		/// </summary>
		/// <remarks>
		/// Inferring it instead is a defect, and a quiet one. The player attack-move closure relocates
		/// through Mobile.NearestMoveableCell, which is per-unit by construction — it short-circuits on
		/// the unit's own location, tests CanEnterCell/CanStayInCell against the unit's own locomotor and
		/// gates on CanReach, the unit's own pathfinding domain (Mobile.cs:850-871). Reading the move back
		/// therefore records a DIFFERENT cell for each unit in one selection, and Shift-G replays cells
		/// nobody clicked. Plain Move never had this because Mobile.ResolveOrder passes the raw cell with
		/// evaluateNearestMovableCell: true (Mobile.cs:1092), leaving relocation to Move.OnFirstRun — so
		/// the two order types disagreed on the same screen for the same click. Pinned by
		/// GroupScatterWaypointTest.
		/// </remarks>
		public AttackMoveActivity(Actor self, Func<Activity> getMove, CPos orderedDestination, bool assaultMoving = false)
			: this(self, getMove, assaultMoving, orderedDestination) { }

		AttackMoveActivity(Actor self, Func<Activity> getMove, bool assaultMoving, CPos? orderedDestination)
		{
			this.getMove = getMove;
			autoTarget = self.TraitOrDefault<AutoTarget>();
			attackMove = self.TraitOrDefault<AttackMove>();
			isAssaultMove = assaultMoving;
			ChildHasPriority = false;

			if (orderedDestination.HasValue)
			{
				OriginalDestination = orderedDestination;
				return;
			}

			// Cache the destination before any ticks can modify it (for group scatter)
			var tempActivity = getMove();
			if (tempActivity is SmartMoveActivity sma)
				OriginalDestination = sma.OriginalDestination;
			else if (tempActivity is Move m)
				OriginalDestination = m.Destination;
		}

		protected override void OnFirstRun(Actor self)
		{
			if (attackMove == null || autoTarget == null)
			{
				QueueChild(getMove());
				return;
			}

			if (isAssaultMove)
				token = self.GrantCondition(attackMove.Info.AssaultMoveCondition);
			else
				token = self.GrantCondition(attackMove.Info.AttackMoveCondition);
		}

		public override bool Tick(Actor self)
		{
			if (IsCanceling || attackMove == null || autoTarget == null)
				return TickChild(self);

			// Stage 2: after an ambush halt we no longer scan or advance — just drain the cancelling
			// move child (same idiom as the IsCanceling drain above). When it is gone TickChild returns
			// true and the attack-move completes, dropping the unit to idle.
			if (haltedForAmbush)
				return TickChild(self);

			// Completely out of ammo: END the march rather than un-sticking the engagement and walking
			// on to the destination with nothing to fire. The attack children abort themselves (see
			// Activities/Attack.cs), but without this the unit would just resume advancing and only
			// reach idle at the far end of the order. Ending here hands the unit to
			// AmmoPool.INotifyBecomingIdle -> AutoRearmIfDry, which is what picks the right
			// disposition per resupply stance (Auto: rearm, Hold: stay put and flag, Evacuate: rotate out).
			if (haltedForEmptyAmmo || AmmoPool.CannotFight(self))
			{
				if (!haltedForEmptyAmmo)
				{
					haltedForEmptyAmmo = true;
					runningMoveActivity = false;
					ChildActivity?.Cancel(self);
				}

				return TickChild(self);
			}

			var engStance = autoTarget.EngagementStanceValue;

			// CPU improvement - Only check every 10 ticks
			if (checkTick-- <= 0 && (ChildActivity == null || runningMoveActivity))
			{
				// Scan for targets. Always bypass the AutoTarget per-actor scan-interval rate
				// limit — AttackFollow.Tick runs opportunity-fire scans every tick and shares
				// the same nextScanTime counter, which left AttackMove starved of scan slots
				// during a move (the symptom: attack-move never engages, opportunity-fire fires
				// only at the moment the unit happens to be still). The 10-tick checkTick
				// cadence below provides our own rate limit.
				// fromProtectedOverride must be threaded into the stamp below. An AttackMoveActivity is
				// NOT necessarily a fresh attack-move order that superseded an earlier attack: Resupply
				// (:231, :258), the Mobile nudge (Mobile.cs:1096), aircraft repositioning
				// (Aircraft.cs:1496), AttackWander, Hunt, Patrol and Reservable all queue one INTERNALLY,
				// with no order issued at all. Re-stamping there would launder a player's target into an
				// autotarget-acquired one and make their order preemptable.
				target = autoTarget.ScanForTarget(self, false, true, true, out var fromProtectedOverride);

				// Cancel the current move activity and queue attack activities if we find a new target.
				if (target.Type != TargetType.Invalid)
				{
					// HoldPosition during attack-move: only fire at targets in range without stopping
					if (engStance == EngagementStance.HoldPosition)
					{
						var inRange = autoTarget.ActiveAttackBases
							.Any(ab => target.IsInRange(self.CenterPosition, ab.GetMaximumRange()));

						if (!inRange)
							target = Target.Invalid;
					}
				}

				if (target.Type != TargetType.Invalid)
				{
					// Stage 2 — halt-before-contact (PIPELINE item 8), behind the default-off
					// AmbushTacticsCondition gate. When an Ambush unit that is attack-moving / auto-moving
					// scans an enemy while its group is still UNSEEN, END the march and drop the unit to
					// idle so the proven AmbushTickIdle path (silent pre-aim + hold-fire-until-spotted +
					// coordinated spring via TriggerNearbyAmbushAllies + damage retaliation) takes over —
					// instead of firing on contact. Reusing the idle path adds no new fire/spring code.
					//
					// Reach: only an Ambush-stance unit WITH the gate granted gets here; for every other
					// unit the short-circuit below is false and the original engage path runs unchanged.
					// NOTE (b8d2e601, 2026-08-02): the old "every @stable / control bot is FireAtWill and
					// nothing grants the gate by default" claim is dead — LaneAmbushBotModule@stable
					// (in ai.yaml) posts @stable ambushers, sets Ambush stance AND grants the gate, so
					// this halt branch is live for @stable too.
					// Plain player Move never enters this activity (it is a bare Move), so — per resolved
					// fork B — a plain Move is always obeyed; only attack-move / bot auto-move can halt.
					var ambushGate = autoTarget.Info.AmbushTacticsCondition;
					var tacticsEnabled = autoTarget.Stance == UnitStance.Ambush
						&& !string.IsNullOrEmpty(ambushGate)
						&& self.GetConditionCount(ambushGate) > 0;

					if (tacticsEnabled && AmbushTactics.ShouldHaltBeforeContact(
						tacticsEnabled, autoTarget.Stance, hasValidTarget: true, GroupDetectedBy(self, autoTarget, target)))
					{
						// Latch the halt and cancel the march. We do NOT queue an attack — the unit will
						// idle and AmbushTickIdle takes over (pre-aim + hold fire until spotted). Draining
						// the cancelled child via the haltedForAmbush branch lets Mobile release its cell
						// reservations cleanly before the activity completes.
						haltedForAmbush = true;
						runningMoveActivity = false;
						ChildActivity?.Cancel(self);
						return TickChild(self);
					}

					checkTick = 0;

					runningMoveActivity = false;
					ChildActivity?.Cancel(self);

					var engagementSource = fromProtectedOverride ? AttackSource.Default : AttackSource.AttackMove;
					foreach (var ab in autoTarget.ActiveAttackBases)
						QueueChild(ab.GetAttackActivity(self, engagementSource, target, false, false));
				}

				// Continue with the move activity (or queue a new one) when there are no targets.
				if (ChildActivity == null)
				{
					runningMoveActivity = true;
					QueueChild(getMove());
					checkTick = 10;
				}
			}

			// If the move activity finished, we have reached our destination and there are no more enemies on our path.
			return TickChild(self) && runningMoveActivity;
		}

		// Stage 2 helper: is the target's owner currently able to SEE any Ambush-stance member of this
		// unit's group (self, or a nearby ally within the coordination radius)? While false the ambush is
		// unblown, so halting to hold the alpha strike is worthwhile; once true the group is exposed and
		// must engage now. Determinism: the FindActorsInCircle result only gates a boolean OR (any member
		// seen), which is iteration-order-independent; CanBeViewedByPlayer is sim-legal and draws no RNG.
		static bool GroupDetectedBy(Actor self, AutoTarget autoTarget, in Target target)
		{
			var targetOwner = target.Type == TargetType.Actor ? target.Actor.Owner
				: target.Type == TargetType.FrozenActor ? target.FrozenActor.Owner
				: null;

			// Unknown owner ⇒ treat as detected (do NOT halt): never silently stall a march on an
			// unattributable contact.
			if (targetOwner == null)
				return true;

			if (self.CanBeViewedByPlayer(targetOwner))
				return true;

			var coordRadius = WDist.FromCells(autoTarget.Info.AmbushCoordinationRadius);
			foreach (var ally in self.World.FindActorsInCircle(self.CenterPosition, coordRadius))
			{
				if (ally == self || ally.Owner != self.Owner || !ally.IsInWorld || ally.IsDead)
					continue;

				var allyAutoTarget = ally.TraitOrDefault<AutoTarget>();
				if (allyAutoTarget == null || allyAutoTarget.Stance != UnitStance.Ambush)
					continue;

				if (ally.CanBeViewedByPlayer(targetOwner))
					return true;
			}

			return false;
		}

		protected override void OnLastRun(Actor self)
		{
			if (token != Actor.InvalidConditionToken)
				token = self.RevokeCondition(token);
		}

		public override IEnumerable<Target> GetTargets(Actor self)
		{
			if (ChildActivity != null)
				return ChildActivity.GetTargets(self);

			return Target.None;
		}

		public override IEnumerable<TargetLineNode> TargetLineNodes(Actor self)
		{
			foreach (var n in getMove().TargetLineNodes(self))
				yield return n;

			yield break;
		}
	}
}
