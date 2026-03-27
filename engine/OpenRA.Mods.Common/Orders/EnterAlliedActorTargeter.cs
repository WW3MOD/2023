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

		public EnterAlliedActorTargeter(string order, int priority, string enterCursor, string enterBlockedCursor,
			Func<Actor, TargetModifiers, bool> canTarget, Func<Actor, bool> useEnterCursor)
			: base(order, priority, enterCursor, false, true)
		{
			this.enterCursor = enterCursor;
			this.enterBlockedCursor = enterBlockedCursor;
			this.canTarget = canTarget;
			this.useEnterCursor = useEnterCursor;
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

		public override bool CanTargetFrozenActor(Actor self, FrozenActor target, TargetModifiers modifiers, ref string cursor)
		{
			if (target == null || target.Actor == null || target.Actor.IsDead)
				return false;

			if (!target.Actor.Info.HasTraitInfo<T>() || !canTarget(target.Actor, modifiers))
				return false;

			// Same foreign crew check for frozen actors
			if (!self.Owner.IsAlliedWith(target.Actor.Owner) && !self.Owner.IsNeutralWith(target.Actor.Owner))
			{
				var vc = target.Actor.TraitOrDefault<VehicleCrew>();
				if (vc == null || !vc.AllowForeignCrew)
					return false;
			}

			cursor = useEnterCursor(target.Actor) ? enterCursor : enterBlockedCursor;
			return true;
		}
	}
}
