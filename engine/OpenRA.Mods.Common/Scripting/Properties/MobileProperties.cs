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

using OpenRA.Mods.Common.Activities;
using OpenRA.Mods.Common.Traits;
using OpenRA.Scripting;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Scripting
{
	[ScriptPropertyGroup("Movement")]
	public class MobileProperties : ScriptActorProperties, Requires<MobileInfo>
	{
		readonly Mobile mobile;

		public MobileProperties(ScriptContext context, Actor self)
			: base(context, self)
		{
			mobile = self.Trait<Mobile>();
		}

		[ScriptActorPropertyActivity]
		[Desc("Moves within the cell grid. closeEnough defines an optional range " +
			"(in cells) that will be considered close enough to complete the activity.")]
		public void Move(CPos cell, int closeEnough = 0)
		{
			// PITFALL: this is NOT the player's move order. The constructor used here leaves
			// evaluateNearestMovableCell false, so Mobile.NearestMoveableCell never runs and the
			// destination is used raw — no clamping to a standable cell, and no clamping to a
			// REACHABLE one. Mobile.ResolveOrder passes true. A scenario testing anything in the
			// order path wants Test.IssueMoveOrder instead; one written against this API was RED
			// on the fixed build and on the broken one alike, because the code under test never ran.
			Self.QueueActivity(new Move(Self, cell, WDist.FromCells(closeEnough)));
		}

		[ScriptActorPropertyActivity]
		[Desc("Moves within the cell grid, ignoring lane biases.")]
		public void ScriptedMove(CPos cell)
		{
			Self.QueueActivity(new Move(Self, cell));
		}

		[ScriptActorPropertyActivity]
		[Desc("Moves from outside the world into the cell grid.")]
		public void MoveIntoWorld(CPos cell)
		{
			var pos = Self.CenterPosition;
			mobile.SetPosition(Self, cell);
			mobile.SetCenterPosition(Self, pos);
			Self.QueueActivity(mobile.ReturnToCell(Self));
		}

		[ScriptActorPropertyActivity]
		[Desc("Leave the current position in a random direction.")]
		public void Scatter()
		{
			Self.QueueActivity(false, new Nudge(Self));
		}

		[ScriptActorPropertyActivity]
		[Desc("Move to and enter the transport.")]
		public void EnterTransport(Actor transport)
		{
			// THE TRANSPORT'S POSITION IS PASSED, and without it this binding cannot enter a
			// building its owner is not allied to.
			//
			// A script queues this activity SYNCHRONOUSLY, so its first tick lands before the world
			// has finished computing who can see what; an order for the same thing resolves a tick or
			// more later and misses the window entirely. On that first tick Enter asks
			// Target.Recalculate whether the transport is visible, and if it is not, the Approaching
			// case gives up BEFORE queueing any move, because the last-visible target it would fall
			// back on has never been set (Enter.cs:105-106, :130-132). The unit does not stop at the
			// door -- it never leaves its cell.
			//
			// MEASURED, run 260921_164455, three lanes differing in one variable each: two riflemen
			// sent into a NEUTRAL civilian building moved ZERO CELLS; the same call into a USA-owned
			// copy of the same building loaded two; and the same neutral building loaded two when the
			// men were ordered in through the order layer. FrozenUnderFog returns visible
			// unconditionally for an ALLY-owned actor (:24, :130-133) -- that is the whole of why the
			// owned lane worked, and why this looked for a week like a rule about ownership. The
			// cargo filter was asked and answered True; nothing ever refused these men.
			//
			// Handing Enter the position is the same remedy MoveAdjacentTo already applies one layer
			// down for the same situation (:36-43). It is safe HERE specifically because a map script
			// named this transport and can read any actor on the map anyway; the order layer and the
			// bots still pass null and are byte-identical.
			Self.QueueActivity(new RideTransport(Self, Target.FromActor(transport), null, transport.CenterPosition));
		}

		[Desc("Whether the actor can move (false if immobilized).")]
		public bool IsMobile => !mobile.IsTraitDisabled && !mobile.IsTraitPaused;
	}
}
