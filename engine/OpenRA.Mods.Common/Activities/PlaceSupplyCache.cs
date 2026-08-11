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
using OpenRA.Activities;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Activities
{
	/// <summary>
	/// The forward-delivery errand: drive to an ordered cell and unload the whole load there as a ground
	/// cache. Under believed danger this is the bot's delivery MODE — stop short of the platoon, unload
	/// everything, egress — so its defining property is that the supply is dumped THERE and not here.
	///
	/// <para>A NAMED TYPE because that property has to be legible from outside. A supply truck otherwise
	/// stops for anyone in its aura who needs a batch, which is right for an ordinary move and exactly
	/// wrong for this errand: a truck that halts to serve the platoon it was sent to unload NEAR never
	/// reaches the drop cell, never places a crate, and lingers in the danger zone the drop-and-leave
	/// doctrine exists to get it out of. The halt now yields to any committed supply errand
	/// (SupplyProvider.OnSupplyErrand), of which this is one.</para>
	///
	/// <para>Composed as one activity rather than a MoveTo with an unload queued behind it so that a
	/// cancel takes the drive and the unload together, and so the two cannot be separated by a
	/// pre-emption — the failure mode that latched SupplyProvider.restocking for good.</para>
	/// </summary>
	public class PlaceSupplyCache : Activity
	{
		readonly DropsSupplyCache transport;
		readonly IMove move;
		readonly IMoveInfo moveInfo;
		readonly CPos dropCell;
		readonly int toleranceCells;

		public PlaceSupplyCache(Actor self, DropsSupplyCache transport, CPos dropCell, int toleranceCells)
		{
			this.transport = transport;
			this.dropCell = dropCell;
			this.toleranceCells = toleranceCells;
			move = self.Trait<IMove>();
			moveInfo = self.Info.TraitInfo<IMoveInfo>();
		}

		protected override void OnFirstRun(Actor self)
		{
			// Stop WITHIN tolerance rather than on the exact cell: the ordered cell is a belief-field
			// cell, not a reserved parking space, and an exact-cell MoveTo gives up outright when
			// something is standing there. The crate lands on whatever cell the truck stopped on.
			QueueChild(move.MoveTo(dropCell, toleranceCells));
		}

		public override bool Tick(Actor self)
		{
			if (!IsCanceling)
				transport.TryPlaceCacheAt(dropCell, toleranceCells);

			return true;
		}

		public override IEnumerable<TargetLineNode> TargetLineNodes(Actor self)
		{
			yield return new TargetLineNode(Target.FromCell(self.World, dropCell), moveInfo.GetTargetLineColor());
		}
	}
}
