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
		WPos? aircraftDespawnPos;
		bool movingToEdge;
		bool drivingOffMap;
		int driveOffDeadline;
		int edgeRetries;
		int evacuatingToken = Actor.InvalidConditionToken;

		// Anti-cheese: helicopter must clear this many cells past the boundary before despawn so in-flight missiles can land.
		const int AircraftOffMapCells = 5;

		// PIPELINE item 38. How far past the boundary a GROUND unit drives before it sells, and how much slack the
		// deadline gets over the honest travel time.
		//
		// Deliberately much shorter than the aircraft margin above, which is a gameplay number (missiles must be able
		// to land on a fleeing helicopter) rather than a cosmetic one. Two cells is enough to read as "drove off the
		// map" and is the conservative choice against the one thing outside the playable area that is NOT uniform
		// across maps: how much authored border there is. Cells outside Map.Bounds still exist and still have terrain
		// — the influence layer simply declines to index them (ActorMap.AddInfluence skips !layer.Contains(uv)) — but
		// a map whose bounds sit tight against MapSize has very little of it, and a unit driven far past the edge
		// would be sliding over nothing. At two cells it is behind the border on any map and half off-screen anyway.
		const int GroundOffMapCells = 2;
		const int DriveOffDeadlineSlack = 50;

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

			// Tick every frame so the aircraft off-map despawn check fires while Fly is still running.
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

				// Push the destination past the boundary; EvacuatingOffMap stops repulsion from snapping us back.
				aircraftDespawnPos = ComputePastEdgePos(self, edgeCell.Value, AircraftOffMapCells);
				var aircraft = self.TraitOrDefault<Aircraft>();
				if (aircraft != null)
					aircraft.EvacuatingOffMap = true;
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

			// Despawn only once genuinely past the boundary so missiles aren't whooshed at empty air.
			if (isAircraft && movingToEdge && IsClearOfMapEdge(self, AircraftOffMapCells))
			{
				DoSell(self);
				return true;
			}

			// PIPELINE item 38 — the ground drive-off leg, which REPLACES what used to be an unconditional
			// DoSell the moment the unit got near the edge. Because it replaces a sell that could not fail, it
			// must not be able to fail either: every way this leg can end has to end in a sell. There are three,
			// and they are deliberately redundant rather than layered.
			//   * cleared the boundary — the intended exit, and the only one the player is meant to see;
			//   * the Drag finished — it ran its full length but we are still inside (a short leg, or a map with
			//     unusual bounds). Sell anyway: the unit has travelled, which is all the change promised;
			//   * the deadline expired — the backstop for the one case that has no natural end. Drag is
			//     IsInterruptible = false and simply stops advancing while its mover trait is disabled
			//     (Drag.cs:49-50), so a unit whose Mobile is disabled mid-leg — EMP, a crate, a temporary
			//     condition — would otherwise sit outside the playable area forever, unsellable and unkillable.
			// Checked BEFORE the child tick so a leg that has already carried us clear ends on this eval.
			if (drivingOffMap)
			{
				if (ChildActivity == null || --driveOffDeadline <= 0 || IsClearOfBounds(self, GroundOffMapCells))
				{
					DoSell(self);
					return true;
				}

				TickChild(self);
				return false;
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
					var target = aircraftDespawnPos.HasValue
						? Target.FromPos(aircraftDespawnPos.Value)
						: Target.FromCell(self.World, edgeCell.Value);
					QueueChild(new Fly(self, target));
				}
				else
				{
					var move = self.TraitOrDefault<IMove>();
					if (move != null)
						QueueChild(move.MoveTo(edgeCell.Value, 2, evaluateNearestMovableCell: true));
				}

				return false;
			}

			// Only proceed if we actually reached the edge (or close to it).
			// If the move was blocked (e.g. building in the way), don't sell mid-map.
			// The margin is unchanged: this predicate still means "the move succeeded", and only what happens
			// on success has changed — the unit now drives the remaining few cells off the map under Drag
			// instead of vanishing here, which is the whole of item 38.
			if (IsNearMapEdge(self, 4))
			{
				// Aircraft keep the original immediate sell. They normally never get here — the off-map despawn
				// check above fires while Fly is still running — so this is the fallback for a Fly that ended
				// early, and it stays exactly as it was. The drive-off leg is a GROUND fix specifically: aircraft
				// already exit past the boundary under their own power, which is what item 38 asked for.
				if (isAircraft)
				{
					DoSell(self);
					return true;
				}

				StartDriveOff(self);
				return false;
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

		static bool IsNearMapEdge(Actor self, int margin)
		{
			var map = self.World.Map;
			var mpos = self.Location.ToMPos(map);
			return mpos.U <= map.Bounds.Left + margin - 1 || mpos.U >= map.Bounds.Right - margin
				|| mpos.V <= map.Bounds.Top + margin - 1 || mpos.V >= map.Bounds.Bottom - margin;
		}

		// Hand the ground unit off from the pathfinder to world-space movement for the last few cells.
		//
		// It has to be a hand-off rather than a longer move order: a Mobile actor cannot path here at all, because
		// Locomotor.MovementCostForCell reports every cell outside Map.Bounds as unreachable (Locomotor.cs:191-193).
		// Drag bypasses the locomotor by driving SetCenterPosition directly, which is also why the unit stops
		// occupying cells partway through — ActorMap silently declines to index influence outside the layer, so it
		// blocks nothing on its way out. That is correct for a unit that is leaving.
		void StartDriveOff(Actor self)
		{
			drivingOffMap = true;

			var target = ComputeOffMapPos(self, edgeCell.Value, GroundOffMapCells);
			var speed = self.Info.TraitInfoOrDefault<MobileInfo>()?.Speed ?? 0;
			var ticks = EvacDriveOffMath.DriveOffTicks((target - self.CenterPosition).HorizontalLength, speed);

			// Slack over the honest travel time so the deadline is a backstop for a STALLED leg, never a race
			// against a merely slow one.
			driveOffDeadline = ticks + DriveOffDeadlineSlack;

			QueueChild(new Drag(self, self.CenterPosition, target, ticks));
		}

		// True when the actor's cell has cleared the PLAYABLE bounds by `margin` on any side.
		//
		// Distinct from IsClearOfMapEdge below, which the aircraft path uses: that one measures against
		// Map.Bounds too but is expressed in the engine's own inclusive/exclusive mix inline. This routes through
		// EvacDriveOffMath so the boundary arithmetic — the part that is easy to get off by one and impossible to
		// eyeball in a running game — is pinned by unit tests rather than by a screenshot.
		static bool IsClearOfBounds(Actor self, int margin)
		{
			var map = self.World.Map;
			var mpos = self.Location.ToMPos(map);
			return EvacDriveOffMath.IsClearOfBounds(mpos.U, mpos.V,
				map.Bounds.Left, map.Bounds.Top, map.Bounds.Right, map.Bounds.Bottom, margin);
		}

		// World position `cellsPast` cells outside the boundary, on the far side of the edge cell.
		//
		// Takes its direction from the map centre rather than from the unit's own approach vector (the way
		// ComputePastEdgePos does) because by the time this is called the unit is essentially ON the edge cell, so
		// the approach vector has collapsed to near-zero and its direction is noise. Centre-to-edge is always
		// outward and never degenerate, since an edge cell is never the centre.
		static WPos ComputeOffMapPos(Actor self, CPos edgeCell, int cellsPast)
		{
			var map = self.World.Map;
			var edgePos = map.CenterOfCell(edgeCell);
			var centre = map.CenterOfCell(new MPos(
				(map.Bounds.Left + map.Bounds.Right) / 2,
				(map.Bounds.Top + map.Bounds.Bottom) / 2).ToCPos(map));

			var diff = edgePos - centre;
			var dist = diff.HorizontalLength;
			if (dist <= 0)
				return edgePos;

			var extLen = cellsPast * 1024;
			var ext = new WVec((int)((long)diff.X * extLen / dist), (int)((long)diff.Y * extLen / dist), 0);
			return edgePos + ext;
		}

		// True when the actor is at least `cellsPast` cells outside the map boundary on any side.
		static bool IsClearOfMapEdge(Actor self, int cellsPast)
		{
			var map = self.World.Map;
			var mpos = self.Location.ToMPos(map);
			return mpos.U + cellsPast <= map.Bounds.Left || mpos.U >= map.Bounds.Right + cellsPast
				|| mpos.V + cellsPast <= map.Bounds.Top || mpos.V >= map.Bounds.Bottom + cellsPast;
		}

		// World position that's `cellsPast` cells past the edge cell, in the direction the actor is heading.
		static WPos ComputePastEdgePos(Actor self, CPos edgeCell, int cellsPast)
		{
			var edgePos = self.World.Map.CenterOfCell(edgeCell);
			var diff = edgePos - self.CenterPosition;
			var dist = diff.HorizontalLength;
			if (dist <= 0)
				return edgePos;

			var extLen = cellsPast * 1024;
			var ext = new WVec((int)((long)diff.X * extLen / dist), (int)((long)diff.Y * extLen / dist), 0);
			return edgePos + ext;
		}

		void RevokeEvacuating(Actor self)
		{
			if (evacuatingToken != Actor.InvalidConditionToken)
				evacuatingToken = self.RevokeCondition(evacuatingToken);

			if (isAircraft)
			{
				var aircraft = self.TraitOrDefault<Aircraft>();
				if (aircraft != null)
					aircraft.EvacuatingOffMap = false;
			}
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
