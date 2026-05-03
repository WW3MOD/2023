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
		readonly bool isAircraft;
		CPos? edgeCell;
		bool movingToEdge;
		int edgeRetries;
		int evacuatingToken = Actor.InvalidConditionToken;

		/// <summary>
		/// Constructor for Sellable trait (existing behavior).
		/// </summary>
		public RotateToEdge(Actor self, bool showTicks)
		{
			this.showTicks = showTicks;
			health = self.TraitOrDefault<IHealth>();
			playerResources = self.Owner.PlayerActor.Trait<PlayerResources>();
			isAircraft = self.Info.HasTraitInfo<AircraftInfo>();

			var sellableInfo = self.Info.TraitInfoOrDefault<SellableInfo>();
			refundPercent = sellableInfo?.RefundPercent ?? 100;
			fixedRefund = null;

			// Tick must run every frame so the aircraft early-sell check intercepts the
			// helicopter mid-flight, before Aircraft.Repulse pushes off-map units back
			// toward the map center and stalls them at the edge indefinitely.
			ChildHasPriority = false;
		}

		/// <summary>
		/// Constructor for rotation (DeliversCash) — fixed refund amount, no Sellable needed.
		/// </summary>
		public RotateToEdge(Actor self, bool showTicks, int refundAmount)
		{
			this.showTicks = showTicks;
			health = self.TraitOrDefault<IHealth>();
			playerResources = self.Owner.PlayerActor.Trait<PlayerResources>();
			isAircraft = self.Info.HasTraitInfo<AircraftInfo>();
			refundPercent = 100;
			fixedRefund = refundAmount;

			ChildHasPriority = false;
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
				// Aircraft evacuate toward the closest point in a wide zone (~15 tiles each side)
				// around the SpawnArea, sell on arrival at edge cell
				var spawnAreaHint = FindClosestSpawnAreaForOwner(self);
				var searchOrigin = spawnAreaHint ?? self.Owner.HomeLocation;
				var candidates = self.World.Map.GetSpawnCandidatesOnSameEdge(searchOrigin, 30);
				if (candidates.Length > 0)
					edgeCell = candidates.OrderBy(c => (self.Location - c).LengthSquared).First();
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

			// Aircraft early-sell: as soon as the helicopter enters the edge zone, dispose of it.
			// This must run every frame (hence ChildHasPriority=false) so we intercept BEFORE
			// the helicopter actually crosses the map boundary. If we let it cross, Aircraft.Repulse
			// shoves it back toward map center, fighting any forward-flight activity and leaving the
			// helicopter oscillating on the edge forever.
			if (isAircraft && movingToEdge
				&& (!self.World.Map.Contains(self.Location) || IsOnMapEdge(self)))
			{
				DoSell(self);
				return true;
			}

			// ChildHasPriority is false, so child activities are not auto-ticked — do it manually.
			if (ChildActivity != null)
			{
				TickChild(self);
				return false;
			}

			// Queue move to edge if not done yet
			if (!movingToEdge)
			{
				movingToEdge = true;

				if (isAircraft)
				{
					// Just Fly to the edge cell — no FlyForward/FlyOffMap. The early-sell check
					// above will dispose of the helicopter as soon as it enters the edge zone, so
					// Fly never has to fully complete its decel/snap sequence.
					QueueChild(new Fly(self, Target.FromCell(self.World, edgeCell.Value)));
				}
				else
				{
					var move = self.TraitOrDefault<IMove>();
					if (move != null)
						QueueChild(move.MoveTo(edgeCell.Value, 2, evaluateNearestMovableCell: true));
				}

				return false;
			}

			// Only sell if we actually reached the edge (or close to it).
			// If the move was blocked (e.g. building in the way), don't sell mid-map.
			if (IsNearMapEdge(self, 4))
			{
				DoSell(self);
				return true;
			}

			// Not near edge — path was blocked. Try again with a direct edge cell.
			if (++edgeRetries > 3)
			{
				// Give up after multiple retries — sell wherever we are.
				DoSell(self);
				return true;
			}

			movingToEdge = false;
			edgeCell = self.World.Map.ChooseClosestEdgeCell(self.Location);
			return false;
		}

		static bool IsOnMapEdge(Actor self)
		{
			return IsNearMapEdge(self, 2);
		}

		static bool IsNearMapEdge(Actor self, int margin)
		{
			var map = self.World.Map;
			var mpos = self.Location.ToMPos(map);
			return mpos.U <= map.Bounds.Left + margin - 1 || mpos.U >= map.Bounds.Right - margin
				|| mpos.V <= map.Bounds.Top + margin - 1 || mpos.V >= map.Bounds.Bottom - margin;
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
