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

using System.Collections.Generic;
using System.Linq;
using OpenRA.Activities;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Activities
{
	/// <summary>
	/// Wraps a Move activity to selectively fire at targets while moving.
	/// Unlike AttackMoveActivity, this does NOT chase targets. It only fires when:
	/// - The unit was recently damaged (return fire / self-defense)
	/// - The target isn't already saturated with enough incoming damage (needs our firepower)
	/// </summary>
	public class SmartMoveActivity : Activity
	{
		readonly Activity moveInner;
		readonly SmartMoveInfo info;
		AutoTarget autoTarget;
		SmartMove smartMove;

		bool runningMoveActivity;
		int checkTick;

		/// <summary>The original destination cell, cached at construction time before any ticks can modify the inner Move.</summary>
		public readonly CPos? OriginalDestination;

		public SmartMoveActivity(Activity moveInner, SmartMoveInfo info)
		{
			this.moveInner = moveInner;
			this.info = info;
			ChildHasPriority = false;

			// Cache the original destination before any execution modifies it
			if (moveInner is Move move)
				OriginalDestination = move.Destination;
		}

		protected override void OnFirstRun(Actor self)
		{
			autoTarget = self.TraitOrDefault<AutoTarget>();
			smartMove = self.TraitOrDefault<SmartMove>();

			// If no AutoTarget trait or on HoldFire stance, just run the plain move
			if (autoTarget == null || autoTarget.Stance <= UnitStance.HoldFire)
			{
				QueueChild(moveInner);
				return;
			}
		}

		public override bool Tick(Actor self)
		{
			if (IsCanceling || autoTarget == null || autoTarget.Stance <= UnitStance.HoldFire)
				return TickChild(self);

			var engStance = autoTarget.EngagementStanceValue;

			if (checkTick-- <= 0 && (ChildActivity == null || runningMoveActivity))
			{
				// A unit with nothing left to fire must not be interrupted, or it never travels at all.
				// ChooseArmamentsForTarget filters on IsTraitDisabled only (AttackBase.cs:437, literal
				// "FF TODO Check ammo?") and an empty armament is PAUSED, not disabled — so a dry unit
				// still reports a weapon in range below, cancels its own move child, and queues an attack
				// that AmmoPool.CannotFight ends on its first tick (Attack.cs:117). runningMoveActivity
				// stays false, so the move child is never re-queued and the next tick re-scans and repeats
				// it: the unit is pinned on its cell, and because this activity never completes it never
				// goes idle either, so none of the idle-triggered resupply paths can reach it. Only
				// Force-Move moved such a unit, because "ForceMove" bypasses IWrapMove entirely
				// (Mobile.cs:1032) and so never enters this activity.
				//
				// Deliberately the SAME predicate the resupply dispatch uses (AutoSeekSupplies'
				// ReturnWhenEmpty and AmmoPool.AutoRearmIfAllEmpty): the set of units sent away to rearm
				// is then exactly the set this refuses to pin, so the two cannot drift into disagreeing
				// about what "empty" means. Skipping the scan outright rather than filtering its result
				// also stops a unit that cannot shoot from paying for a full target scan every interval.
				// It is an early-out for the wholly dry unit, NOT the whole ammo story: CannotFight needs
				// EVERY pool empty, so a man with one pool still loaded is not covered here at all. The
				// per-armament filter below is what covers him — read the two as a pair.
				//
				// Scan for targets but don't allow moving toward them (allowMove = false)
				var target = AmmoPool.CannotFight(self)
					? Target.Invalid
					: autoTarget.ScanForTarget(self, false, true, !runningMoveActivity);

				if (target.Type != TargetType.Invalid)
				{
					// Only interrupt movement for enemy targets — allied heal/repair targets
					// from HealerAutoTarget should not cause stop-and-engage loops
					var isEnemy = target.Type == TargetType.Actor &&
						self.Owner.RelationshipWith(target.Actor.Owner).HasRelationship(PlayerRelationship.Enemy);

					if (!isEnemy)
						target = Target.Invalid;
				}

				if (target.Type != TargetType.Invalid)
				{
					// Filter armaments: NoSelfDefenseInterrupt weapons (e.g. drone jammer) can fire
					// opportunistically when stationary, but must NOT cancel a player Move.
					//
					// !IsTraitPaused is what stops a weapon the unit cannot actually fire from cancelling
					// the move. ChooseArmamentsForTarget already answers "valid against THIS target"; it
					// does not answer "and loaded", because it filters IsTraitDisabled only and an empty
					// armament is PAUSED. The CannotFight gate above cannot cover this: it needs EVERY
					// pool empty, and the standard rifleman ^E3 ends an infantry firefight with a spent
					// DMR and a loaded RPG the RPG's InvalidTargets: Infantry never let him spend. Against
					// infantry the DMR is then his only offered armament and it cannot fire, while
					// CannotFight stays false — so he stopped, aimed, and never moved again, and
					// Attack.cs:117's guard (also CannotFight) did not end the attack either.
					//
					// IsTraitPaused rather than an ammo-only test on purpose: it is exactly the condition
					// Armament.CanFire itself refuses to fire on (Armament.cs:327), so it also covers the
					// suppressed / EMP'd / heavily-damaged weapon, which wedges a move identically (see
					// ^AT's "!ammo-primary || suppressed >= 10"). Stopping to aim a weapon that cannot
					// shoot loses the move AND the shot; walking on costs only a shot that was never
					// available. The scan repeats every ScanInterval, so a pause that lifts mid-journey
					// gets the unit engaging again on a later tick.
					//
					// PITFALL: this belongs HERE, not in ChooseArmamentsForTarget. That method has nine
					// callers, and AttackBase.AbandonWhenArmamentsPaused (AttackBase.cs:65-72) exists
					// precisely because "all armaments paused" must NOT end an attack by default —
					// "holding aim through a brief pause is the wanted behaviour". Filtering paused
					// armaments at the shared method would flip that opt-in field on for every unit in
					// the game and strip the attack cursor off any momentarily-paused unit.
					var interruptingArmaments = autoTarget.ActiveAttackBases
						.SelectMany(ab => ab.ChooseArmamentsForTarget(target, false))
						.Where(a => !a.Info.NoSelfDefenseInterrupt && !a.IsTraitPaused);

					var inRange = interruptingArmaments.Any(a => target.IsInRange(self.CenterPosition, a.MaxRange()));

					if (inRange)
					{
						// Self-defense: always return fire when under attack
						var underFire = smartMove != null &&
							(self.World.WorldTick - smartMove.LastDamagedTick) < info.UnderFireDuration;

						// Overkill check: skip targets that already have enough damage incoming
						var targetSaturated = target.Type == TargetType.Actor &&
							target.Actor.AverageDamagePercent >= info.OverkillThreshold;

						// HoldPosition during SmartMove: don't stop to engage, keep moving
						// (fire stance still controls IF they fire while passing)
						var holdingPosition = engStance == EngagementStance.HoldPosition;

						if (!holdingPosition && (underFire || !targetSaturated))
						{
							checkTick = 0;
							runningMoveActivity = false;
							ChildActivity?.Cancel(self);

							foreach (var ab in autoTarget.ActiveAttackBases)
								QueueChild(ab.GetAttackActivity(self, AttackSource.AutoTarget, target, false, false));
						}
					}
				}

				// Resume or start moving when no valid in-range target
				if (ChildActivity == null)
				{
					runningMoveActivity = true;
					QueueChild(moveInner);
					checkTick = info.ScanInterval;
				}
			}

			// Complete when the move finishes (we've reached the destination)
			return TickChild(self) && runningMoveActivity;
		}

		public override IEnumerable<Target> GetTargets(Actor self)
		{
			if (ChildActivity != null)
				return ChildActivity.GetTargets(self);

			return Target.None;
		}

		public override IEnumerable<TargetLineNode> TargetLineNodes(Actor self)
		{
			return moveInner.TargetLineNodes(self);
		}
	}
}
