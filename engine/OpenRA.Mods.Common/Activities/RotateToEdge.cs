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
using OpenRA.Activities;
using OpenRA.Mods.Common.Effects;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Activities
{
	class RotateToEdge : Activity
	{
		readonly IHealth health;
		readonly PlayerResources playerResources;
		readonly bool showTicks;
		readonly int refundPercent;
		readonly int? fixedRefund;
		CPos? edgeCell;
		bool movingToEdge;

		/// <summary>
		/// Constructor for Sellable trait (existing behavior).
		/// </summary>
		public RotateToEdge(Actor self, bool showTicks)
		{
			this.showTicks = showTicks;
			health = self.TraitOrDefault<IHealth>();
			playerResources = self.Owner.PlayerActor.Trait<PlayerResources>();

			var sellableInfo = self.Info.TraitInfoOrDefault<SellableInfo>();
			refundPercent = sellableInfo?.RefundPercent ?? 100;
			fixedRefund = null;
		}

		/// <summary>
		/// Constructor for rotation (DeliversCash) — fixed refund amount, no Sellable needed.
		/// </summary>
		public RotateToEdge(Actor self, bool showTicks, int refundAmount)
		{
			this.showTicks = showTicks;
			health = self.TraitOrDefault<IHealth>();
			playerResources = self.Owner.PlayerActor.Trait<PlayerResources>();
			refundPercent = 100;
			fixedRefund = refundAmount;
		}

		protected override void OnFirstRun(Actor self)
		{
			var aircraftInfo = self.Info.TraitInfoOrDefault<AircraftInfo>();
			var mobileInfo = self.Info.TraitInfoOrDefault<MobileInfo>();

			if (aircraftInfo != null)
			{
				// Aircraft fly directly to nearest map edge
				edgeCell = self.World.Map.ChooseClosestEdgeCell(self.Location);
			}
			else if (mobileInfo != null)
			{
				// Find the SpawnArea closest to the player's own Supply Route building,
				// so units always retreat toward their own map edge (not the nearest one)
				var spawnAreas = self.World.ActorsWithTrait<SpawnArea>()
					.Where(a => !a.Actor.IsDead && a.Actor.IsInWorld)
					.Select(a => a.Actor)
					.ToList();

				CPos? spawnAreaHint = null;
				if (spawnAreas.Count > 0)
				{
					// Find this player's Supply Route building to anchor the search
					var ownSR = self.World.ActorsHavingTrait<ProductionFromMapEdge>()
						.FirstOrDefault(a => !a.IsDead && a.IsInWorld && a.Owner == self.Owner);
					var anchor = ownSR?.Location ?? self.Location;

					var closestDist = int.MaxValue;
					foreach (var sa in spawnAreas)
					{
						var dist = (anchor - sa.Location).LengthSquared;
						if (dist < closestDist)
						{
							closestDist = dist;
							spawnAreaHint = sa.Location;
						}
					}
				}

				var pathFinder = self.World.WorldActor.Trait<IPathFinder>();
				var locomotor = self.World.WorldActor.TraitsImplementing<Locomotor>().First(l => l.Info.Name == mobileInfo.Locomotor);
				var searchOrigin = spawnAreaHint ?? self.Location;
				edgeCell = self.World.Map.ChooseClosestMatchingEdgeCell(searchOrigin,
					c => mobileInfo.CanEnterCell(self.World, null, c) && pathFinder.PathExistsForLocomotor(locomotor, c, self.Location));
			}
			else
			{
				// No movement capability, sell immediately
				edgeCell = null;
			}
		}

		public override bool Tick(Actor self)
		{
			if (IsCanceling)
				return true;

			// If no edge cell found, sell immediately
			if (!edgeCell.HasValue)
			{
				DoSell(self);
				return true;
			}

			// Wait for child activities to complete
			if (ChildActivity != null)
				return false;

			// Queue move to edge if not done yet
			if (!movingToEdge)
			{
				movingToEdge = true;
				var move = self.TraitOrDefault<IMove>();
				if (move != null)
					QueueChild(move.MoveTo(edgeCell.Value, 2, evaluateNearestMovableCell: true));

				return false;
			}

			// Arrived at edge (or close enough) — sell
			DoSell(self);
			return true;
		}

		void DoSell(Actor self)
		{
			int refund;

			if (fixedRefund.HasValue)
			{
				// Rotation: use pre-calculated amount, scale by HP
				var hp = health != null ? (long)health.HP : 1L;
				var maxHP = health != null ? (long)health.MaxHP : 1L;
				refund = (int)(fixedRefund.Value * hp / maxHP);
			}
			else
			{
				// Sellable: use sell value and refund percent
				var sellValue = self.GetSellValue();
				var hp = health != null ? (long)health.HP : 1L;
				var maxHP = health != null ? (long)health.MaxHP : 1L;
				refund = (int)((sellValue * refundPercent * hp) / (100 * maxHP));
			}

			refund = playerResources.ChangeCash(refund);

			foreach (var ns in self.TraitsImplementing<INotifySold>())
				ns.Sold(self);

			if (showTicks && refund > 0 && self.Owner.IsAlliedWith(self.World.RenderPlayer))
				self.World.AddFrameEndTask(w => w.Add(new FloatingText(self.CenterPosition, self.Owner.Color, FloatingText.FormatCashTick(refund), 30)));

			self.Dispose();
		}
	}
}
