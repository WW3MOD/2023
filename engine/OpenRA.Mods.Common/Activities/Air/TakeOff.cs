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
	public class TakeOff : Activity
	{
		readonly Aircraft aircraft;

		public TakeOff(Actor self)
		{
			aircraft = self.Trait<Aircraft>();
		}

		protected override void OnFirstRun(Actor self)
		{
			if (aircraft.ForceLanding)
				return;

			if (!aircraft.HasInfluence())
				return;

			// We are taking off, so remove influence in ground cells.
			aircraft.RemoveInfluence();

			if (self.World.Map.DistanceAboveTerrain(aircraft.CenterPosition).Length > aircraft.Info.MinAirborneAltitude)
				return;

			if (aircraft.Info.TakeoffSounds.Length > 0)
				Game.Sound.Play(SoundType.World, aircraft.Info.TakeoffSounds, self.World, aircraft.CenterPosition);

			foreach (var notify in self.TraitsImplementing<INotifyTakeOff>())
				notify.TakeOff(self);
		}

		public override bool Tick(Actor self)
		{
			// Refuse to take off if it would land immediately again.
			if (aircraft.ForceLanding)
			{
				Cancel(self);
				return true;
			}

			var dat = self.World.Map.DistanceAboveTerrain(aircraft.CenterPosition);

			// CanSlide VTOL (helicopters): rise to halfway altitude, then complete TakeOff.
			// The next activity (Fly) will climb the rest while moving forward —
			// like a real pilot clearing obstacles before transitioning to forward flight.
			if (aircraft.Info.VTOL && aircraft.Info.CanSlide)
			{
				var halfwayAlt = new WDist(aircraft.Info.CruiseAltitude.Length / 2);
				if (dat < halfwayAlt)
				{
					Fly.VerticalTakeOffOrLandTick(self, aircraft, aircraft.Facing, halfwayAlt);
					return false;
				}

				return true;
			}

			if (dat < aircraft.Info.CruiseAltitude)
			{
				// Non-CanSlide VTOL: rise fully before flying forward
				if (aircraft.Info.VTOL)
				{
					Fly.VerticalTakeOffOrLandTick(self, aircraft, aircraft.Facing, aircraft.Info.CruiseAltitude);
					return false;
				}

				// Fixed-wing: climb while moving forward
				Fly.FlyTick(self, aircraft, aircraft.Facing, aircraft.Info.CruiseAltitude);
				return false;
			}

			return true;
		}
	}
}
