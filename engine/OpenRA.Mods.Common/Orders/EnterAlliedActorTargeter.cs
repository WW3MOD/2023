#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Orders
{
	public class EnterAlliedActorTargeter<T> : UnitOrderTargeter where T : ITraitInfoInterface
	{
		readonly string enterCursor;
		readonly string enterBlockedCursor;
		readonly Func<Actor, TargetModifiers, bool> canTarget;
		readonly Func<Actor, bool> useEnterCursor;
		readonly Func<ActorInfo, TargetModifiers, bool> canTargetFrozen;

		/// <param name="canTargetFrozen">The fogged counterpart of <paramref name="canTarget"/>. It gets
		/// the target's ActorInfo — never the live actor — because this runs while the player cannot see
		/// the target, so any live state consulted here would be rendered as a cursor. It is mandatory
		/// rather than optional so that adding a caller forces the question "which half of my predicate
		/// survives fog?" to be answered at the call site.</param>
		public EnterAlliedActorTargeter(string order, int priority, string enterCursor, string enterBlockedCursor,
			Func<Actor, TargetModifiers, bool> canTarget, Func<Actor, bool> useEnterCursor,
			Func<ActorInfo, TargetModifiers, bool> canTargetFrozen)
			: base(order, priority, enterCursor, false, true)
		{
			this.enterCursor = enterCursor;
			this.enterBlockedCursor = enterBlockedCursor;
			this.canTarget = canTarget;
			this.useEnterCursor = useEnterCursor;
			this.canTargetFrozen = canTargetFrozen;
		}

		public override bool CanTargetActor(Actor self, Actor target, TargetModifiers modifiers, ref string cursor)
		{
			if (!target.Info.HasTraitInfo<T>() || !canTarget(target, modifiers))
				return false;

			// Allow allied, neutral, and enemy targets when VehicleCrew.AllowForeignCrew is set (crash-disabled)
			if (!self.Owner.IsAlliedWith(target.Owner) && !self.Owner.IsNeutralWith(target.Owner))
			{
				var vc = target.TraitOrDefault<VehicleCrew>();
				if (vc == null || !vc.AllowForeignCrew)
					return false;
			}

			cursor = useEnterCursor(target) ? enterCursor : enterBlockedCursor;
			return true;
		}

		// FOG BOUNDARY. Everything below reads the FrozenActor snapshot (Info, Owner) and never
		// target.Actor, which hands back the live actor and so would answer with state the player is
		// not permitted to see. The answer here picks a cursor, so a leak is not theoretical: it is
		// drawn under the mouse.
		public override bool CanTargetFrozenActor(Actor self, FrozenActor target, TargetModifiers modifiers, ref string cursor)
		{
			if (target == null || !target.IsValid)
				return false;

			// THE ONE PERMITTED LIVE READ ON THIS PATH, and it is here to DECLINE the click rather than
			// to answer it. FrozenActorLayer only drops a frozen actor once it stops being visible
			// (:386-390), so the ghost of a building that died under fog stays on screen and stays
			// clickable. Returning true for it issues an EnterTransport that ResolveOrder then discards
			// on the null backing actor, and the infantry does not move AT ALL. Returning false instead
			// lets UnitOrderGenerator's second pass rewrite the click into a Move
			// (OrderFallbackMath.AllowsMoveFallback is true for Neutral and Ally), which is what shipped
			// before this file was touched and what the player is plainly asking for.
			//
			// This does leak, weakly: a dead ghost walks the unit over, a live-but-full one does not.
			// That distinction is pre-existing, and it is the lesser evil — a silent no-op on the most
			// common fog interaction there is, is exactly the failure wt/cursor-honesty (a2466c3b) and
			// CrewMember.cs:88-93 both rule is the worst outcome available. Filed, not fixed here.
			if (target.Actor == null)
				return false;

			if (!target.Info.HasTraitInfo<T>() || !canTargetFrozen(target.Info, modifiers))
				return false;

			// The live path also admits an enemy whose VehicleCrew.AllowForeignCrew is set. That flag is
			// mutable trait state written by HeliEmergencyLanding, so consulting it here would announce
			// that a helicopter the player cannot see has crash-landed. A fogged enemy is simply not
			// targetable; once it is seen, CanTargetActor offers the order as before.
			if (!self.Owner.IsAlliedWith(target.Owner) && !self.Owner.IsNeutralWith(target.Owner))
				return false;

			// NOT useEnterCursor(...). Across the callers that predicate resolves to cargo occupancy,
			// crew-slot occupancy, docking reservations or resupply availability — none of which the
			// snapshot carries, and all of which are exactly what fog is meant to hide. An uninformative
			// cursor is the honest answer; a varying one would report hidden occupancy.
			cursor = enterCursor;
			return true;
		}
	}
}
