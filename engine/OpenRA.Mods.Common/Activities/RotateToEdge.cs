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
using OpenRA.Mods.Common.Effects;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Activities
{
	class RotateToEdge : Activity
	{
		static readonly Color EvacuateLineColor = Color.FromArgb(180, 255, 200, 80);

		readonly IHealth health;
		readonly PlayerResources playerResources;
		readonly bool showTicks;
		readonly int refundPercent;
		readonly int? fixedRefund;
		CPos? edgeCell;
		bool movingToEdge;
		int evacuatingToken = Actor.InvalidConditionToken;

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

		/// <summary>Find the SpawnArea closest to the player's Supply Route building.</summary>
		static CPos? FindClosestSpawnAreaForOwner(Actor self)
		{
			var spawnAreas = self.World.ActorsWithTrait<SpawnArea>()
				.Where(a => !a.Actor.IsDead && a.Actor.IsInWorld)
				.Select(a => a.Actor)
				.ToList();

			if (spawnAreas.Count == 0)
				return null;

			var ownSR = self.World.ActorsHavingTrait<ProductionFromMapEdge>()
				.FirstOrDefault(a => !a.IsDead && a.IsInWorld && a.Owner == self.Owner);
			var anchor = ownSR?.Location ?? self.Location;

			CPos? closest = null;
			var closestDist = int.MaxValue;
			foreach (var sa in spawnAreas)
			{
				var dist = (anchor - sa.Location).LengthSquared;
				if (dist < closestDist)
				{
					closestDist = dist;
					closest = sa.Location;
				}
			}

			return closest;
		}

		protected override void OnFirstRun(Actor self)
		{
			var aircraftInfo = self.Info.TraitInfoOrDefault<AircraftInfo>();
			var mobileInfo = self.Info.TraitInfoOrDefault<MobileInfo>();

			if (aircraftInfo != null)
			{
				// Aircraft evacuate toward the SpawnArea edge (where reinforcements enter)
				var spawnAreaHint = FindClosestSpawnAreaForOwner(self);
				var searchOrigin = spawnAreaHint ?? self.Owner.HomeLocation;
				var candidates = self.World.Map.GetSpawnCandidatesOnSameEdge(searchOrigin, 5);
				if (candidates.Length > 0)
					edgeCell = candidates[self.World.SharedRandom.Next(candidates.Length)];
				else
					edgeCell = self.World.Map.ChooseClosestEdgeCell(searchOrigin);
			}
			else if (mobileInfo != null)
			{
				// Ground units retreat toward the SpawnArea edge
				var spawnAreaHintGround = FindClosestSpawnAreaForOwner(self);
				var pathFinder = self.World.WorldActor.Trait<IPathFinder>();
				var locomotor = self.World.WorldActor.TraitsImplementing<Locomotor>().First(l => l.Info.Name == mobileInfo.Locomotor);
				var searchOrigin = spawnAreaHintGround ?? self.Location;
				edgeCell = self.World.Map.ChooseClosestMatchingEdgeCell(searchOrigin,
					c => mobileInfo.CanEnterCell(self.World, null, c) && pathFinder.PathExistsForLocomotor(locomotor, c, self.Location));
			}
			else
			{
				// No movement capability, sell immediately
				edgeCell = null;
			}

			// Grant evacuating condition for selection deprioritization
			if (evacuatingToken == Actor.InvalidConditionToken)
				evacuatingToken = self.GrantCondition("evacuating");
		}

		public override bool Tick(Actor self)
		{
			if (IsCanceling)
			{
				RevokeEvacuating(self);
				return true;
			}

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

		void RevokeEvacuating(Actor self)
		{
			if (evacuatingToken != Actor.InvalidConditionToken)
				evacuatingToken = self.RevokeCondition(evacuatingToken);
		}

		public override IEnumerable<TargetLineNode> TargetLineNodes(Actor self)
		{
			if (edgeCell.HasValue)
				yield return new TargetLineNode(Target.FromCell(self.World, edgeCell.Value), EvacuateLineColor);
		}

		void DoSell(Actor self)
		{
			RevokeEvacuating(self);
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
