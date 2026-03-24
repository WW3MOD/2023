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
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[TraitLocation(SystemActors.World)]
	[Desc("Tracks which actors are under temporary player control via the Control All Units debug mode.")]
	public class ControlAllUnitsManagerInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) { return new ControlAllUnitsManager(); }
	}

	public class ControlAllUnitsManager : ITick
	{
		readonly HashSet<Actor> playerControlledActors = new HashSet<Actor>();

		public void MarkPlayerControlled(Actor a)
		{
			playerControlledActors.Add(a);
		}

		public bool IsPlayerControlled(Actor a)
		{
			return playerControlledActors.Contains(a);
		}

		void ITick.Tick(Actor self)
		{
			if (!DeveloperMode.IsControlAllUnitsActive(self.World))
			{
				if (playerControlledActors.Count > 0)
					playerControlledActors.Clear();

				return;
			}

			playerControlledActors.RemoveWhere(a =>
			{
				if (a.IsDead || a.Disposed || !a.IsInWorld)
					return true;

				if (!a.IsIdle)
					return false;

				// "Parked" units in HoldPosition stay under player control
				var autoTarget = a.TraitOrDefault<AutoTarget>();
				if (autoTarget != null && autoTarget.EngagementStanceValue == EngagementStance.HoldPosition)
					return false;

				return true;
			});
		}
	}
}
