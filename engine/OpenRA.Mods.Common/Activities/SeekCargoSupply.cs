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

using System.Collections.Generic;
using System.Linq;
using OpenRA.Activities;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Activities
{
	/// <summary>
	/// Drives an empty unit toward a CargoSupply host (e.g. supply truck) with supply remaining,
	/// re-picks the target if it runs out of supply mid-route, and shows a target line.
	/// Used by AmmoPool.AutoRearm when the picked resupplier is a CargoSupply host.
	/// </summary>
	public class SeekCargoSupply : Activity
	{
		readonly IMove move;
		readonly IMoveInfo moveInfo;

		Actor currentTarget;
		bool moveQueued;
		int retargetTicks;

		const int RetargetInterval = 25;

		public SeekCargoSupply(Actor self, Actor initialTarget)
		{
			move = self.Trait<IMove>();
			moveInfo = self.Info.TraitInfo<IMoveInfo>();
			currentTarget = initialTarget;
		}

		bool TargetValid(Actor a)
		{
			if (a == null || a.IsDead || !a.IsInWorld)
				return false;

			var cs = a.TraitOrDefault<CargoSupply>();
			return cs != null && cs.EffectiveSupply > 0;
		}

		Actor FindBest(Actor self)
		{
			var rearmInfo = self.Info.TraitInfoOrDefault<RearmableInfo>();
			if (rearmInfo == null)
				return null;

			return self.World.ActorsHavingTrait<CargoSupply>()
				.Where(a => !a.IsDead && a.IsInWorld
					&& a.Owner == self.Owner
					&& rearmInfo.RearmActors.Contains(a.Info.Name)
					&& a.Trait<CargoSupply>().EffectiveSupply > 0)
				.ClosestToIgnoringPath(self);
		}

		public override bool Tick(Actor self)
		{
			if (IsCanceling)
				return true;

			// Bail if all ammo pools are full — rearm complete.
			var pools = self.TraitsImplementing<AmmoPool>().ToArray();
			if (pools.Length == 0 || pools.All(p => p.HasFullAmmo))
				return true;

			// Re-pick target if current one is invalid (dead, empty) or periodically.
			if (!TargetValid(currentTarget) || --retargetTicks <= 0)
			{
				retargetTicks = RetargetInterval;
				var newTarget = FindBest(self);

				if (newTarget != currentTarget)
				{
					currentTarget = newTarget;

					// Cancel any in-flight child move so we re-route.
					if (ChildActivity != null)
					{
						ChildActivity.Cancel(self);
						moveQueued = false;
					}
				}
			}

			if (currentTarget == null)
			{
				// No supply available anywhere — flag for pickup and exit.
				foreach (var p in pools)
					p.NeedsResupply = true;
				return true;
			}

			var cs = currentTarget.Trait<CargoSupply>();
			var rearmRange = cs.Info.RearmRange;
			var distSq = (currentTarget.CenterPosition - self.CenterPosition).HorizontalLengthSquared;

			if (distSq <= rearmRange.LengthSquared)
			{
				// In range — let CargoSupply push ammo. Stay put.
				if (ChildActivity != null && moveQueued)
				{
					ChildActivity.Cancel(self);
					moveQueued = false;
				}

				return false;
			}

			// Out of range — move within rearm range.
			if (!moveQueued)
			{
				QueueChild(move.MoveWithinRange(Target.FromActor(currentTarget), rearmRange,
					targetLineColor: moveInfo.GetTargetLineColor()));
				moveQueued = true;
			}

			TickChild(self);
			return false;
		}

		public override IEnumerable<TargetLineNode> TargetLineNodes(Actor self)
		{
			if (currentTarget != null && !currentTarget.IsDead && currentTarget.IsInWorld)
				yield return new TargetLineNode(Target.FromActor(currentTarget), moveInfo.GetTargetLineColor());
		}
	}
}
