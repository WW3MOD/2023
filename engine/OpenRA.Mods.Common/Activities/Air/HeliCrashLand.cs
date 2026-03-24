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

using System;
using OpenRA.Activities;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Mods.Common.Activities
{
	/// <summary>
	/// Uncontrolled crash descent for helicopters at critical damage.
	/// Helicopter spins (configurable), descends rapidly, and is always destroyed on impact.
	/// Crew is ejected on impact only if terrain supports infantry.
	/// </summary>
	public class HeliCrashLand : Activity
	{
		readonly HeliEmergencyLanding emergencyLanding;
		readonly HeliEmergencyLandingInfo info;
		readonly Aircraft aircraft;

		int spin;
		readonly int spinDirection;
		WVec driftVelocity;

		public HeliCrashLand(Actor self, HeliEmergencyLanding emergencyLanding,
			HeliEmergencyLandingInfo info, Aircraft aircraft)
		{
			this.emergencyLanding = emergencyLanding;
			this.info = info;
			this.aircraft = aircraft;
			IsInterruptible = false;

			// Random spin direction (clockwise or counterclockwise)
			if (info.SpinsOnCrash)
				spinDirection = self.World.SharedRandom.Next(2) * 2 - 1;

			// Capture current velocity as initial drift (decays over time)
			driftVelocity = new WVec(aircraft.CurrentVelocity.X, aircraft.CurrentVelocity.Y, 0);

			// Zero the aircraft velocity system to prevent Aircraft.Tick from interfering
			aircraft.CurrentVelocity = WVec.Zero;
			aircraft.RequestedAcceleration = WVec.Zero;
		}

		public override bool Tick(Actor self)
		{
			// Check if hit ground
			if (self.World.Map.DistanceAboveTerrain(self.CenterPosition).Length <= 0)
			{
				// Snap to ground
				var groundPos = self.CenterPosition - new WVec(0, 0, self.World.Map.DistanceAboveTerrain(self.CenterPosition).Length);
				aircraft.SetPosition(self, groundPos);
				aircraft.CurrentVelocity = WVec.Zero;

				// Crash impact — helicopter is always destroyed
				emergencyLanding.OnCrashImpact(self);

				return true;
			}

			// Apply spin
			if (info.SpinsOnCrash && spinDirection != 0)
			{
				if (Math.Abs(spin) < info.MaxSpinRate)
					spin += info.SpinAcceleration * spinDirection;

				aircraft.Facing = new WAngle(aircraft.Facing.Angle + spin);
			}

			// Decay drift velocity (forward momentum from before crash)
			if (driftVelocity != WVec.Zero)
			{
				var driftLength = driftVelocity.HorizontalLength;
				if (driftLength <= 2)
					driftVelocity = WVec.Zero;
				else
				{
					// Decay drift by ~2% per tick
					driftVelocity = driftVelocity * 98 / 100;
				}
			}

			// Build movement vector: drift + descent
			var descent = new WVec(0, 0, -info.CrashDescentRate.Length);
			var move = driftVelocity + descent;

			// Ensure Aircraft.Tick doesn't interfere
			aircraft.CurrentVelocity = WVec.Zero;
			aircraft.RequestedAcceleration = WVec.Zero;

			// Move
			aircraft.SetPosition(self, self.CenterPosition + move);

			return false;
		}

	}
}
