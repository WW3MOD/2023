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

using OpenRA.Primitives;

namespace OpenRA.Mods.Common
{
	/// <summary>
	/// The one colour meaning "the GAME issued this order, not the player".
	/// </summary>
	/// <remarks>
	/// Every self-issued dispatch — auto-seeking supplies, rearming dry, a healer walking to a
	/// patient, wandering, fleeing, an idle-cell nudge, an auto-acquired attack — paints its target
	/// line in this colour, so a player who selects a unit that is moving without being told to can
	/// read WHY at a glance instead of guessing whether the game is broken.
	///
	/// <para>Why a colour rather than a flag on <c>TargetLineNode</c>: provenance would have to be
	/// threaded through <c>IMove.MoveTo</c>/<c>MoveWithinRange</c>/<c>MoveToTarget</c> and the
	/// <c>Move</c>/<c>Attack</c> activity constructors — 29 call sites — to reach the renderer. The
	/// colour is ALREADY threaded down that exact path and already reaches the renderer, so it can
	/// carry the bit for free. The cost is that this must stay a value nothing else uses; that is
	/// what <see cref="IsAutomatic"/> is asserting, and there is a test pinning it.</para>
	///
	/// <para>Chosen blue because the player already reads the one existing automatic line (the
	/// healer's, <c>AutoFollowAlly</c>) as "blue means the game did it" — but that line was
	/// <c>self.Owner.Color</c>, the player's OWN colour, so it only looked blue by luck of the lobby
	/// and collided outright with <c>Mobile</c>'s green or <c>AttackBase</c>'s crimson for a player
	/// who picked those. This makes the blue real.</para>
	/// </remarks>
	public static class AutomaticOrder
	{
		/// <summary>DodgerBlue. Must not equal <c>Mobile.TargetLineColor</c> (Green),
		/// <c>AttackBase.TargetLineColor</c> (Crimson), <c>Patrol</c>/<c>AttendAlly</c> (Cyan/LimeGreen)
		/// or the capture/board pink (FFC850B4).</summary>
		public static readonly Color LineColor = Color.FromArgb(30, 144, 255);

		public static bool IsAutomatic(Color color)
		{
			return color.ToArgb() == LineColor.ToArgb();
		}
	}
}
