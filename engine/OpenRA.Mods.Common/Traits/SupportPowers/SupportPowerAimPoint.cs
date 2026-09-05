#region Copyright & License Information
/*
 * WW3MOD — resolves the point a support power actually strikes from the point the player clicked.
 *
 * THE USER'S ASK, verbatim: "a strike on any tile that holds an actor targets instead the center of
 * the actor, even if that is between tiles and not center tile." The last clause is the whole
 * design constraint. A 2x2 building's centre is a cell CORNER and a 3x3's is a cell centre one cell
 * diagonally from its top-left; neither is expressible by snapping to a cell, so the resolved aim
 * point is a WPos and the order carries Target.FromPos.
 *
 * WHY IT IS NOT COSMETIC, on the Kinzhal specifically. IskanderExplosion's Warhead@Target is a
 * TargetDamage warhead, and TargetDamageWarhead scales every hit by HitShape.CenterProximityPercent
 * — the impact's distance from the victim's CENTRE, normalised against the shape's half-diagonal
 * (RectangleShape.cs:123-127). For a 3x3 Logistics Center (hitshape +/-1536, half-diagonal 2172) a
 * corner footprint cell sits 1448 units off centre, so the warhead lands at 33% and 54000 damage
 * arrives as ~17800. Which of the nine cells the player clicked decided whether the strike killed
 * the building. Snapping to the actor's centre makes that reachable percentage 100 for every cell
 * of the footprint. Pinned in SupportPowerAimPointTest.
 *
 * IT DOES NOT FIX THE WARHEAD. The same weapon is fired by the Iskander launcher's own Explodes,
 * which never goes through a support-power order — the defect is untouched there and is filed for
 * its own item (WORKSPACE/DISCOVERIES.md 2026-09-04).
 */
#endregion

using System.Collections.Generic;
using System.Linq;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	/// <summary>
	/// Resolves a support power order's clicked cell to the centre of an actor standing on it.
	/// </summary>
	public static class SupportPowerAimPoint
	{
		/// <summary>
		/// Returns <paramref name="order"/> with its target replaced by the centre of the actor
		/// occupying the targeted cell, or the order unchanged when no actor occupies it.
		/// </summary>
		public static Order SnapToActorCenter(World world, Order order)
		{
			var aim = Resolve(world, order.Target);
			if (aim == null)
				return order;

			var snapped = order.WithTarget(Target.FromPos(aim.Value));

			// Order.WithTarget copies the constructor fields only; these three are settable and are
			// set on support power orders (SelectGenericPowerTarget sets SuppressVisualFeedback).
			snapped.SuppressVisualFeedback = order.SuppressVisualFeedback;
			snapped.IsImmediate = order.IsImmediate;
			snapped.Type = order.Type;

			return snapped;
		}

		/// <summary>
		/// The aim point for <paramref name="target"/>, or null to leave the target alone.
		/// </summary>
		public static WPos? Resolve(World world, in Target target)
		{
			// FREEZES, does not follow. Target.FromActor re-reads the actor's position every time
			// CenterPosition is asked for, so an actor target would walk the impact point around
			// for the whole flight; BallisticMissileFly reads it once in its constructor
			// (BallisticMissileFly.cs:45) but NukePower and the beacon/camera do not. A ballistic
			// missile does not track, so the actor's position at activation is what gets flown to.
			if (target.Type == TargetType.Actor)
				return target.Actor.CenterPosition;

			if (target.Type != TargetType.Terrain)
				return null;

			var pos = target.CenterPosition;
			var cell = world.Map.CellContaining(pos);

			Actor best = null;
			var bestFootprint = 0;
			var bestDistanceSq = 0L;

			foreach (var a in CandidatesAt(world, cell))
			{
				if (a.Disposed || !a.IsInWorld || a.OccupiesSpace == null)
					continue;

				var footprint = FootprintSize(a);
				var distanceSq = (a.CenterPosition - pos).HorizontalLengthSquared;

				if (best == null || IsBetterCandidate(footprint, distanceSq, a.ActorID,
					bestFootprint, bestDistanceSq, best.ActorID))
				{
					best = a;
					bestFootprint = footprint;
					bestDistanceSq = distanceSq;
				}
			}

			// Actor.CenterPosition verbatim, NOT re-projected onto the terrain. It is the same
			// origin CenterProximityPercent measures from (HitShape.cs:166-170), so an exact match
			// reads 100%; re-deriving a ground Z would put the impact outside a hitshape's vertical
			// band on sloped ground for no gain, since the percentage ignores Z entirely.
			return best?.CenterPosition;
		}

		/// <summary>
		/// Every actor whose body covers <paramref name="cell"/>, from BOTH footprint indices.
		/// </summary>
		/// <remarks>
		/// <para>TWO INDICES, BECAUSE ONE OF THEM ANSWERS A DIFFERENT QUESTION THAN IT LOOKS LIKE.
		/// The ActorMap influence layer is keyed on <c>IOccupySpace.OccupiedCells</c>, and for a
		/// building that is <c>BuildingInfo.OccupiedTiles</c> — which yields <c>'x'</c>,
		/// <c>'X'</c> and <c>'+'</c> and deliberately NOT <c>'='</c> (<c>Building.cs:178-190</c>).
		/// <c>'='</c> is <c>OccupiedPassable</c>: a cell inside the building that units may walk
		/// through, so it is absent from the index on purpose. <c>OccupiedCells</c> answers "can
		/// something stand here", which is a PATHFINDING question, not "is the building here".
		/// </para>
		///
		/// <para>MEASURED, not reasoned: a Kinzhal clicked on the Logistics Center's top-left cell
		/// resolved to no actor and landed 1448 units off centre for 24820 damage where a centred
		/// shot did 60000. That building's footprint is <c>=+= +++ =+=</c>, so all four of its
		/// corners are <c>'='</c> and all four were invisible to <c>GetActorsAt</c> — the exact
		/// cells a player is most likely to click when aiming at the edge of a building.</para>
		///
		/// <para>BuildingInfluence is the index that does answer the presence question: Building
		/// registers <c>Info.Tiles(Location)</c> with it (<c>Building.cs:370</c>), and
		/// <c>Tiles</c> includes <c>'='</c> (<c>Building.cs:156-169</c>). Both indices are
		/// footprint-based, so an AIRBORNE aircraft is still in neither — Aircraft.OccupiedCells
		/// returns only its landing cells (<c>Aircraft.cs:809-812</c>) and it is not a Building.
		/// That is load-bearing: it is what stops a strike snapping onto a helicopter's centre
		/// 3000 units above the ground.</para>
		///
		/// <para>A building occupying the clicked cell by an <c>'x'</c> or <c>'+'</c> tile appears
		/// in both, and is therefore ranked twice. That is harmless because
		/// <see cref="IsBetterCandidate"/> is STRICT — an actor never beats itself — so a duplicate
		/// cannot displace the leader or make the result depend on which index was read first.
		/// </para>
		/// </remarks>
		static IEnumerable<Actor> CandidatesAt(World world, CPos cell)
		{
			foreach (var a in world.ActorMap.GetActorsAt(cell))
				yield return a;

			var buildings = world.WorldActor.TraitOrDefault<BuildingInfluence>();
			if (buildings == null)
				yield break;

			foreach (var a in buildings.GetBuildingsAt(cell))
				yield return a;
		}

		/// <summary>
		/// Cells the actor's body covers, used only to rank two candidates on the same cell.
		/// </summary>
		/// <remarks>
		/// A building is measured by <c>Info.Tiles</c> rather than by <c>OccupiedCells</c> for the
		/// same reason <see cref="CandidatesAt"/> consults two indices: <c>OccupiedCells</c> drops
		/// the passable cells, which would report the 3x3 Logistics Center as 5 cells rather than
		/// 9 and could rank it below a genuinely smaller building whose footprint happens to be
		/// solid.
		/// </remarks>
		static int FootprintSize(Actor a)
		{
			var building = a.TraitOrDefault<Building>();
			if (building != null)
				return building.Info.Tiles(a.Location).Count();

			return a.OccupiesSpace.OccupiedCells().Length;
		}

		/// <summary>
		/// Ranks two actors occupying the same cell. The bigger footprint wins — it is the one the
		/// aim point actually moves, and the one the player was clicking at. Ties go to the actor
		/// nearest the click, then to the lowest ActorID so the choice never depends on ActorMap
		/// enumeration order.
		/// </summary>
		public static bool IsBetterCandidate(int footprint, long distanceSq, uint actorId,
			int bestFootprint, long bestDistanceSq, uint bestActorId)
		{
			if (footprint != bestFootprint)
				return footprint > bestFootprint;

			if (distanceSq != bestDistanceSq)
				return distanceSq < bestDistanceSq;

			return actorId < bestActorId;
		}
	}
}
