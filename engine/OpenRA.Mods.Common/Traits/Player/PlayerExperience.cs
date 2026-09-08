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
	[TraitLocation(SystemActors.Player)]
	[Desc("This trait can be used to track player experience based on units killed with the `" + nameof(GivesExperience) + "` trait.",
		"It can also be used as a point score system in scripted maps, for example.",
		"Attach this to the player actor.")]
	public class PlayerExperienceInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) { return new PlayerExperience(); }
	}

	public class PlayerExperience : ISync
	{
		[Sync]
		public int Experience { get; private set; }

		/// <summary>
		/// <para>True once the score has been sealed. Set by DOOMSDAY mode on the tick the clock expires, so
		/// that the winner is decided on the score as it stood BEFORE the warheads landed and nothing the
		/// annihilation does can move it.</para>
		///
		/// <para>NOT [Sync]-ed, and that is deliberate rather than an oversight: it is set from
		/// DoomsdayStrike.NotifyTimerExpired, which runs inside the synchronised tick on every client at
		/// the same WorldTick, so the flag is already identical everywhere. Experience itself stays synced
		/// and is the value a desync would actually surface.</para>
		/// </summary>
		public bool Frozen { get; private set; }

		public void Freeze() { Frozen = true; }

		public void GiveExperience(int num)
		{
			// THE choke point. Every score-affecting event in the engine arrives here — kills through
			// GivesExperience, captures, donations, infiltration, repair rewards, Lua — so one guard on
			// this line is what stops all of them at once. In particular it is why the kills caused by the
			// Dead Hand salvo itself credit nobody.
			if (Frozen)
				return;

			Experience += num;
		}
	}
}
