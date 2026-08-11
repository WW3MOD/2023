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
	/// A supply transport driving to a ground cache to load whatever will fit — the inverse of
	/// <c>DropsSupplyCache.DropSupplyCacheHere</c>, and the only route by which supply put on the ground
	/// gets back into a truck on the seven shipped maps that have no Logistics Centre.
	///
	/// <para>A NAMED TYPE for the same reason as <see cref="RestockSupply"/>: this is a move whose whole
	/// purpose is to stop being empty, and the dry-move rule must not cancel it. Composed as one
	/// activity so a cancel takes the drive and the transfer together rather than leaving a transfer
	/// queued behind a dead move.</para>
	///
	/// <para>The pickup is ORDER-DRIVEN, never a proximity aura like <c>AbsorbsSupplyCache</c>: the drop
	/// places its crate on the truck's own cell, so a passive absorber would re-swallow the load it had
	/// just dropped, and would also eat forward dumps placed on purpose for infantry to walk to.</para>
	/// </summary>
	public class CollectSupplyCache : Activity
	{
		readonly Actor cache;
		readonly SupplyProvider supply;
		readonly IMove move;
		readonly IMoveInfo moveInfo;
		readonly CPos cacheCell;
		readonly int toleranceCells;

		public CollectSupplyCache(Actor self, Actor cache, int toleranceCells)
		{
			this.cache = cache;
			this.toleranceCells = toleranceCells;
			supply = self.Trait<SupplyProvider>();
			move = self.Trait<IMove>();
			moveInfo = self.Info.TraitInfo<IMoveInfo>();
			cacheCell = self.World.Map.CellContaining(cache.CenterPosition);
		}

		protected override void OnFirstRun(Actor self)
		{
			// Stop WITHIN tolerance: the crate is a Building and occupies its cell, so an exact-cell
			// MoveTo could never complete.
			QueueChild(move.MoveTo(cacheCell, toleranceCells));
		}

		public override bool Tick(Actor self)
		{
			if (IsCanceling)
				return true;

			if (supply == null || cache.IsDead || !cache.IsInWorld)
				return true;

			// ARRIVAL CHECK — the whole difference between a pickup and a siphon, and the same guard
			// DropSupplyCacheAt applies for the same reason. A Move to a cell with no route does not
			// FAIL: PathFinder bails to NoPath and Move.Tick treats an empty path as arrival, completing
			// in ~2 ticks at the cell the truck was already standing on. Without this the transfer would
			// run from there, i.e. one right-click drains any crate on the map.
			var delta = self.Location - cacheCell;
			if (!SupplyDropMath.ArrivedAtDropCell(delta.X, delta.Y, toleranceCells))
			{
				Log.Write("debug",
					$"[supply] crate-collect-refused truck={self.ActorID}@{self.Location} owner={self.Owner.PlayerName} "
					+ $"reason=never-arrived ordered={cacheCell} tolerance={toleranceCells}c");
				return true;
			}

			var cacheProvider = cache.TraitOrDefault<SupplyProvider>();
			if (cacheProvider == null)
				return true;

			// Capped at our own headroom: a crate holding more than we can take is partially emptied and
			// stays put with the remainder. A crate drained to 0 despawns through its own
			// SupplyProvider.RemoveBelowSupply, not from here.
			var taken = System.Math.Min(supply.Info.TotalSupply - supply.CurrentSupply, cacheProvider.CurrentSupply);
			if (taken <= 0 || !cacheProvider.DeductSupply(taken))
				return true;

			supply.AddSupply(taken);

			// EDGE — the mirror of crate-placed, and unconditional for the same reason: it is the one
			// line that says supply came back OFF the ground, as distinct from an errand being issued.
			Log.Write("debug",
				$"[supply] crate-collected truck={self.ActorID}@{self.Location} owner={self.Owner.PlayerName} "
				+ $"from={cache.ActorID} amount={taken} left={cacheProvider.CurrentSupply}");

			return true;
		}

		public override IEnumerable<TargetLineNode> TargetLineNodes(Actor self)
		{
			if (!cache.IsDead && cache.IsInWorld)
				yield return new TargetLineNode(Target.FromActor(cache), moveInfo.GetTargetLineColor());
		}
	}
}
