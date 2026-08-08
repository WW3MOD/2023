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

		// How far past the boundary a GROUND unit drives before it sells. Kept short: maps vary in how much authored
		// border they have (test maps here run Bounds tight against MapSize, i.e. none), and a unit driven far out
		// would be sliding over nothing.
		const int GroundOffMapCells = 2;

		// Slack over the honest travel time, so the drive-off deadline only fires for a STALLED leg.
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

			// The ground drive-off leg REPLACES what used to be an unconditional DoSell, so it must not be able to
			// end without selling. Two exits, and they are exhaustive:
			//   * the Drag finished — the normal case, and it lands exactly on the off-map target, so this doubles
			//     as the "arrived" signal. There is deliberately no separate boundary predicate: it could only fire
			//     EARLIER than this, which would pop the unit mid-slide and truncate the very animation this leg
			//     exists to produce.
			//   * the deadline expired — the backstop for the one case with no natural end. Drag stops advancing
			//     while its mover trait is disabled (Drag.cs:49-50), so an EMP'd unit would otherwise sit out
			//     there forever, unsellable.
			if (drivingOffMap)
			{
				if (ChildActivity == null || --driveOffDeadline <= 0)
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
			// The margin is unchanged: this still means "the move succeeded". Only what happens on success has
			// changed — a ground unit now drives the remaining cells off the map instead of vanishing here.
			if (IsNearMapEdge(self, 4))
			{
				// Aircraft keep the original immediate sell — they already exit past the boundary under their own
				// power, so this is only the fallback for a Fly that ended early.
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

		// Hand the ground unit off from the pathfinder to world-space movement for the last few cells. It has to be
		// a hand-off rather than a longer move order: a Mobile actor cannot path out there at all, because
		// Locomotor.MovementCostForCell reports every cell outside Map.Bounds as unreachable (Locomotor.cs:191-193).
		//
		// WHAT DRAG ACTUALLY MOVES, because two things here depend on it: Drag drives SetCenterPosition, which sets
		// CenterPosition and notifies, but never calls SetLocation (Mobile.cs:540-553 vs :591-610). Actor.Location
		// is ToCell, so only the WORLD POSITION leaves the map — the actor's CELL stays at the last in-bounds cell
		// for the whole leg. Consequences:
		//   * every CPos-keyed consumer (shroud, influence, pathing) is safe by construction, not by guard;
		//   * the unit KEEPS OCCUPYING that cell until Dispose, because AddInfluence/RemoveInfluence only re-run on
		//     SetLocation (Mobile.cs:638-648). So it blocks the cell it left from for the length of the leg —
		//     roughly 1-2s for a vehicle and up to several seconds for Speed-25 infantry. That is a real @stable
		//     behaviour change and it matters where several units evacuate to the same edge or a SpawnArea.
		void StartDriveOff(Actor self)
		{
			drivingOffMap = true;

			var target = ComputeOffMapPos(self, edgeCell.Value, GroundOffMapCells);
			var speed = self.Info.TraitInfoOrDefault<MobileInfo>()?.Speed ?? 0;
			var ticks = EvacDriveOffMath.DriveOffTicks((target - self.CenterPosition).HorizontalLength, speed);
			driveOffDeadline = ticks + DriveOffDeadlineSlack;

			// Refuse cancellation for the last two cells. Activity.Cancel tests IsInterruptible on ITSELF, not on
			// its child (Activity.cs:197-208), so without this a cancel would set Canceling on us, the IsCanceling
			// branch in Tick would drop the still-Active Drag, and the actor would be left alive outside the map
			// with no sell and no refund. Reachable on @stable by hand: sell a vehicle, then order it to move.
			IsInterruptible = false;

			QueueChild(new Drag(self, self.CenterPosition, target, ticks));
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
