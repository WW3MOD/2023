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

using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Marks this actor as a spawn area. The closest ProductionFromMapEdge building will spawn units at this location instead of the map edge.")]
	class SpawnAreaInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) { return new SpawnArea(init.Self); }
	}

	class SpawnArea : INotifyAddedToWorld, INotifyRemovedFromWorld
	{
		public readonly Actor Self;

		public SpawnArea(Actor self)
		{
			Self = self;
		}

		public CPos Location => Self.Location;

		void INotifyAddedToWorld.AddedToWorld(Actor self) { }

		void INotifyRemovedFromWorld.RemovedFromWorld(Actor self) { }
	}
}
