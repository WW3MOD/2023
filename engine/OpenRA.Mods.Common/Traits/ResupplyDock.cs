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
using System.Linq;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Declares the point a client should park on to be served by this actor's repair/rearm",
		"services, instead of the actor's own centre.",
		"WHY THIS EXISTS: Activities.Resupply sends its clients onto host.CenterPosition and then",
		"measures arrival from the same point. For an EVEN-dimensioned building that point is a cell",
		"CORNER (BuildingInfo.CenterOffset averages the two middle cells), which no ground unit can",
		"ever stand on — so a zero arrival tolerance is unsatisfiable and the client parks next to the",
		"depot re-planning an approach forever. Resupply.cs said as much in a comment and deferred the",
		"fix; LOGISTICSCENTER becoming 2x2 is the case that needed it. It doubles as the art hook: the",
		"offset picks WHICH footprint cell the client stops on, so a building whose service point is",
		"drawn off-centre (the Logistics Center's crane, bottom-left) can park vehicles under it.",
		"This is deliberately NOT DockHost/DockClientManager. That pair is a whole reservation and",
		"queueing model; this is one offset consumed by the approach Resupply already runs.")]
	public class ResupplyDockInfo : TraitInfo
	{
		[Desc("Offset from this actor's CenterPosition to the dock point.",
			"The CELL CONTAINING IT is where ground clients stop, so for a Building it must name a",
			"footprint cell that units may stand on: '=' (OccupiedPassable). '+' is transit-only and",
			"'x' is blocked — a client sent to either can never arrive.")]
		public readonly WVec Offset = WVec.Zero;

		[Desc("Facing arriving ground clients turn to once on the dock cell. Ignored unless TurnToFace.")]
		public readonly WAngle Facing = WAngle.Zero;

		[Desc("Turn arriving ground clients to Facing. Off by default: a turn is an extra activity the",
			"client must finish before it is served, and most hosts do not care which way it points.")]
		public readonly bool TurnToFace = false;

		public override object Create(ActorInitializer init) { return new ResupplyDock(this); }
	}

	public class ResupplyDock
	{
		public readonly ResupplyDockInfo Info;

		public ResupplyDock(ResupplyDockInfo info)
		{
			Info = info;
		}

		/// <summary>World position clients are sent to. Read live, because a host may move.</summary>
		public WPos DockPosition(Actor self)
		{
			return self.CenterPosition + Info.Offset;
		}

		/// <summary>Facing to adopt at the dock, or null to keep whatever the approach left.</summary>
		public WAngle? DockFacing => Info.TurnToFace ? Info.Facing : null;

		/// <summary>
		/// <para>Where a serviced client should go to get OUT OF THE WAY: every cell adjacent to this
		/// actor's footprint that is not itself part of it. Off the BUILDING, not merely off the dock
		/// cell — the other footprint cells are transit-only on LOGISTICSCENTER and a unit parked on
		/// one would be shoved again a tick later by the idle handler, which is the phantom order
		/// test-depot-vacate-phantom exists to watch for.</para>
		///
		/// <para>THIS IS WHY A DOCK NEEDS A VACATE AT ALL. The dock cell has to be stayable or nothing
		/// could ever arrive on it, and there is no queue and no reservation here — the Logistics
		/// Centre carries no Reservable. So a client left standing on it blocks the next one FOREVER:
		/// MoveOnto waits rather than stacking when its single target cell is occupied
		/// (MoveOnto.cs:45-47) and the arrival test is cell equality with no near-enough fallback.
		/// Before the 2026-09-05 resize the dock was the 3x3's transit-only centre and the idle
		/// handler did this job by accident; making the cell stayable is what took that away.</para>
		///
		/// <para>Returned in a fixed CPos order, so a caller breaking distance ties on this sequence
		/// is deterministic. No RNG anywhere on this path.</para>
		/// </summary>
		public IEnumerable<CPos> VacateCandidates(Actor self)
		{
			// Non-Building hosts have no footprint to leave; their own cell is the whole of it.
			var building = self.TraitOrDefault<Building>();
			var footprint = building != null
				? new HashSet<CPos>(building.Info.Tiles(building.TopLeft))
				: new HashSet<CPos> { self.Location };

			var ring = new HashSet<CPos>();
			foreach (var cell in footprint)
			{
				foreach (var direction in CVec.Directions)
				{
					var neighbour = cell + direction;
					if (!footprint.Contains(neighbour))
						ring.Add(neighbour);
				}
			}

			return ring.OrderBy(c => c.X).ThenBy(c => c.Y);
		}
	}
}
