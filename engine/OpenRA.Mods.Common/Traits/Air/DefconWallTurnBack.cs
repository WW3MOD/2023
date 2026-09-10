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

/*
 * LAYER 2 OF THE DEFCON 3 WALL: turn back on approach.
 *
 * Modelled on CarrierSlave.ReturnWithinDistance (CarrierSlave.cs:129-154), which is the same problem
 * already solved for the drone leash and live on the quadcopter today: a ticked trait, an interval
 * counter, a distance test, and an ordinary MoveTo queued unqueued so it replaces what the airframe
 * was doing.
 *
 * WHY TRAIT-LEVEL AND TICKED rather than a check inside the flight activities. Layer 1 refuses the
 * ORDER, and that leaks badly: attack standoffs, attack-move, return-to-base, idle drift, rally
 * replay from the Supply Route, and any straight-line flight that merely clips the line on the way
 * to a perfectly legal destination all reach the line without an illegal order ever being issued.
 * A ticked trait sees the airframe's POSITION and so catches every one of them regardless of which
 * activity is running -- and it looks right, because the aircraft banks and flies home under
 * ordinary movement instead of stopping dead.
 *
 * TWO COSTS, BOTH ACCEPTED AND BOTH DESIGN DECISIONS RATHER THAN DEFECTS:
 *   - It overshoots by up to CheckInterval x speed. It is soft by construction. Layer 3 is the hard
 *     stop that sits behind it, and Margin below is what buys the room to turn before layer 3 fires.
 *   - It cancels the player's order, which reads as the unit disobeying. Tolerable at DEFCON 3,
 *     where the order was illegal anyway.
 */

using OpenRA.Activities;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Turns an aircraft back when it approaches the " + nameof(DefconWall) + " dividing line.",
		"Inert unless the World actor carries " + nameof(DefconWall) + " AND a map has authored a line",
		"AND the match is at a DEFCON level the wall stands at -- so on every shipped map today this",
		"trait ticks a single boolean test and returns.")]
	public class DefconWallTurnBackInfo : TraitInfo, Requires<AircraftInfo>
	{
		[Desc("Ticks between checks. The airframe can travel this many ticks x its speed past the",
			"trigger point before anything happens, so this trades responsiveness against per-airframe",
			"cost. 5 ticks is 0.3 s at the 60 ms timestep.")]
		public readonly int CheckInterval = 5;

		[Desc("Start turning back this far BEFORE the line, in world units (1024 = one cell). This is",
			"what makes the layer soft-edged on purpose: a fast aircraft needs room to bank, and",
			"turning it at the line itself would let it coast across into layer 3's hard refusal,",
			"which stops it dead and looks broken.")]
		public readonly WDist Margin = new WDist(3072);

		[Desc("How far back onto its own side the aircraft is sent, in world units, measured from the",
			"line. Must exceed " + nameof(Margin) + " or the aircraft arrives still inside the trigger",
			"band and turns back again on the next check, which reads as a stutter.")]
		public readonly WDist Clearance = new WDist(5120);

		public override object Create(ActorInitializer init) { return new DefconWallTurnBack(this); }
	}

	public class DefconWallTurnBack : INotifyCreated, ITick
	{
		readonly DefconWallTurnBackInfo info;

		DefconWall wall;
		IMove move;
		int ticksUntilCheck;

		public DefconWallTurnBack(DefconWallTurnBackInfo info)
		{
			this.info = info;
		}

		void INotifyCreated.Created(Actor self)
		{
			// Resolved once. TraitOrDefault so a map that strips DefconWall leaves this inert.
			wall = self.World.WorldActor.TraitOrDefault<DefconWall>();
			move = self.TraitOrDefault<IMove>();
		}

		void ITick.Tick(Actor self)
		{
			// The cheap gate first, and it is the one every shipped map takes: no wall trait, or the
			// wall is down. IsActive is a plain field read.
			if (wall == null || !wall.IsActive || move == null || !self.IsInWorld || self.IsDead)
				return;

			if (--ticksUntilCheck > 0)
				return;

			ticksUntilCheck = info.CheckInterval;

			var depth = wall.DepthBeyondWall(self.Owner, self.CenterPosition);
			if (depth < -info.Margin.Length)
				return;

			// Already heading home under a turn-back queued on an earlier check: leave it alone rather
			// than re-queuing every interval, which would restart the move and stall the airframe.
			if (self.CurrentActivity is DefconWallReturn)
				return;

			var home = wall.NearestPositionOnOwnSide(self.Owner, self.CenterPosition, info.Clearance);
			var cell = self.World.Map.Clamp(self.World.Map.CellContaining(home));

			self.QueueActivity(false, new DefconWallReturn(move.MoveTo(cell)));
		}
	}

	/// <summary>
	/// A transparent wrapper around the ordinary move home, existing only so the trait above can
	/// recognise its own order on the next check. Without it there is no way to tell "already turning
	/// back" from "flying somewhere illegal", and the trait re-queues the move every interval --
	/// which cancels the move it queued last time and leaves the airframe hovering on the line.
	/// </summary>
	public class DefconWallReturn : Activity
	{
		public DefconWallReturn(Activity inner)
		{
			QueueChild(inner);
		}

		// ChildHasPriority defaults to true (Activity.cs:103), and under it TickOuter runs
		// `TickChild(self) && (finishing || Tick(self))` (Activity.cs:125) -- so this is not reached
		// at all until the child move has finished, at which point ChildActivity is already null and
		// the wrapper completes. Written as a test rather than a bare `return true` so that the one
		// thing this class does is legible without re-deriving that line.
		public override bool Tick(Actor self)
		{
			return ChildActivity == null;
		}
	}
}
