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

using OpenRA.Activities;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Mods.Common.Activities
{
	public class Parachute : Activity
	{
		readonly IPositionable pos;
		readonly WVec fallVector;

		int groundLevel;

		public Parachute(Actor self)
		{
			pos = self.TraitOrDefault<IPositionable>();

			var actor = self.World.Map.Rules.Actors[self.Info.Name.ToLowerInvariant()];
			var actorPositionable = actor.TraitInfo<IPositionableInfo>();

			var fallRate = self.Info.TraitInfo<ParachutableInfo>().FallRate;
			fallVector = new WVec(0, 0, fallRate);

			// Horizontal movement
			var mobile = self.TraitOrDefault<Mobile>();
			if (mobile != null)
			{
				var cell = mobile.GetAdjacentCell(self.Location).Value;
				var horizontalDiff = cell - self.Location;

				fallVector += new WVec(
					horizontalDiff.X * 1024 / (self.CenterPosition.Z / fallRate),
					horizontalDiff.Y * 1024 / (self.CenterPosition.Z / fallRate),
					0);
			}

			IsInterruptible = false;
		}

		protected override void OnFirstRun(Actor self)
		{
			groundLevel = self.World.Map.CenterOfCell(self.Location).Z;
			foreach (var np in self.TraitsImplementing<INotifyParachute>())
				np.OnParachute(self);
		}

		public override bool Tick(Actor self)
		{
			var nextPosition = self.CenterPosition - fallVector;
			if (nextPosition.Z < groundLevel)
				return true;

			pos.SetCenterPosition(self, nextPosition);

			return false;
		}

		protected override void OnLastRun(Actor self)
		{
			var centerPosition = self.CenterPosition;
			pos.SetPosition(self, centerPosition + new WVec(0, 0, groundLevel - centerPosition.Z));

			foreach (var np in self.TraitsImplementing<INotifyParachute>())
				np.OnLanded(self);
		}
	}
}
