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

using System.Linq;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Drains supply from nearby SUPPLYCACHE actors into this actor's SupplyProvider pool.",
		"Requires SupplyProvider on the same actor.")]
	public class AbsorbsSupplyCacheInfo : ConditionalTraitInfo, Requires<SupplyProviderInfo>
	{
		[Desc("Maximum range to absorb supply caches.")]
		public readonly WDist Range = WDist.FromCells(2) + new WDist(512);

		[Desc("Supply absorbed per tick from a cache into this actor's pool.")]
		public readonly int TransferRate = 50;

		[Desc("Ticks between absorption ticks.")]
		public readonly int TransferInterval = 5;

		[ActorReference]
		[Desc("Actor type(s) to drain. Defaults to supplycache.")]
		public readonly string CacheActor = "supplycache";

		[Desc("Relationships of cache owners that can be absorbed.")]
		public readonly PlayerRelationship ValidRelationships = PlayerRelationship.Ally;

		public override object Create(ActorInitializer init) { return new AbsorbsSupplyCache(init, this); }
	}

	public class AbsorbsSupplyCache : ConditionalTrait<AbsorbsSupplyCacheInfo>, ITick
	{
		readonly Actor self;
		SupplyProvider supplyProvider;
		int tickCounter;

		public AbsorbsSupplyCache(ActorInitializer init, AbsorbsSupplyCacheInfo info)
			: base(info)
		{
			self = init.Self;
		}

		protected override void Created(Actor self)
		{
			base.Created(self);
			supplyProvider = self.Trait<SupplyProvider>();
		}

		void ITick.Tick(Actor self)
		{
			if (IsTraitDisabled)
				return;

			if (--tickCounter > 0)
				return;

			tickCounter = Info.TransferInterval;

			// Pool full — nothing to do
			if (supplyProvider.CurrentSupply >= supplyProvider.Info.TotalSupply)
				return;

			var headroom = supplyProvider.Info.TotalSupply - supplyProvider.CurrentSupply;
			var toTransfer = System.Math.Min(Info.TransferRate, headroom);

			var cache = self.World.FindActorsInCircle(self.CenterPosition, Info.Range)
				.FirstOrDefault(a => !a.IsDead && a.IsInWorld
					&& a.Info.Name == Info.CacheActor
					&& Info.ValidRelationships.HasRelationship(self.Owner.RelationshipWith(a.Owner))
					&& a.TraitOrDefault<SupplyProvider>() != null
					&& a.Trait<SupplyProvider>().CurrentSupply > 0);

			if (cache == null)
				return;

			var cacheProvider = cache.Trait<SupplyProvider>();
			var available = System.Math.Min(toTransfer, cacheProvider.CurrentSupply);
			if (available <= 0)
				return;

			if (!cacheProvider.DeductSupply(available))
				return;

			supplyProvider.AddSupply(available);

			if (cacheProvider.CurrentSupply <= 0)
				cache.World.AddFrameEndTask(w => { if (!cache.IsDead && cache.IsInWorld) cache.Dispose(); });
		}
	}
}
