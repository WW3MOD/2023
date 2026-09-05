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
	}
}
