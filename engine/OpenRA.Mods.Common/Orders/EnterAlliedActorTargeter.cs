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
			if ((!self.Owner.IsAlliedWith(target.Owner) && !self.Owner.IsNeutralWith(target.Owner)) || !target.Info.HasTraitInfo<T>() || !canTarget(target, modifiers))
				return false;

			cursor = useEnterCursor(target) ? enterCursor : enterBlockedCursor;
			return true;
		}

		public override bool CanTargetFrozenActor(Actor self, FrozenActor target, TargetModifiers modifiers, ref string cursor)
		{
			// Allied actors are never frozen
			if ((!self.Owner.IsAlliedWith(target.Actor.Owner) && !self.Owner.IsNeutralWith(target.Actor.Owner)) || !target.Actor.Info.HasTraitInfo<T>() || !canTarget(target.Actor, modifiers))
				return false;

			cursor = useEnterCursor(target.Actor) ? enterCursor : enterBlockedCursor;
			return true;
		}
	}
}
