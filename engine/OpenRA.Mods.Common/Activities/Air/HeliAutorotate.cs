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
	/// <summary>
	/// Controlled autorotation descent for helicopters at heavy damage.
	/// The helicopter glides forward, slowly losing altitude.
	/// Player can steer (turn) but cannot gain altitude or stop.
	/// On landing: safe terrain = disabled + repairable, unsafe = destroyed.
	/// </summary>
	public class HeliAutorotate : Activity
	{
		readonly HeliEmergencyLanding emergencyLanding;
		readonly HeliEmergencyLandingInfo info;
		readonly Aircraft aircraft;
		readonly int forwardSpeed;
		bool landed;
		int landedTicks;
		bool rotorsStopped;

		public HeliAutorotate(Actor self, HeliEmergencyLanding emergencyLanding,
			HeliEmergencyLandingInfo info, Aircraft aircraft, int forwardSpeed)
		{
			this.emergencyLanding = emergencyLanding;
			this.info = info;
			this.aircraft = aircraft;
			this.forwardSpeed = forwardSpeed;

			// Player can issue turn orders but not cancel the autorotation
			IsInterruptible = false;
		}

		public override bool Tick(Actor self)
		{
			// Post-landing: stay grounded to prevent Aircraft idle takeoff behavior.
			// End activity only when crash-disabled is revoked (helicopter repaired).
			if (landed)
			{
				aircraft.CurrentVelocity = WVec.Zero;
				aircraft.RequestedAcceleration = WVec.Zero;

				// Wind down rotors after landing
				if (!rotorsStopped)
				{
					landedTicks++;
					if (landedTicks >= info.RotorWindDownTicks)
					{
						emergencyLanding.OnRotorsStopped(self);
						rotorsStopped = true;
					}
				}

				return !emergencyLanding.IsDisabledOnGround;
			}

			// If we're already at ground level, handle landing
			if (self.World.Map.DistanceAboveTerrain(self.CenterPosition).Length <= 0)
			{
				// Snap to ground level
				var groundPos = self.CenterPosition - new WVec(0, 0, self.World.Map.DistanceAboveTerrain(self.CenterPosition).Length);
				aircraft.SetPosition(self, groundPos);
				aircraft.CurrentVelocity = WVec.Zero;

				if (emergencyLanding.IsSuitableTerrain(self))
				{
					emergencyLanding.OnSafeLanding(self);
					landed = true;
					return false;
				}
				else
				{
					emergencyLanding.OnUnsafeLanding(self);
					return true;
				}
			}

			// Calculate forward movement based on current facing
			var forward = aircraft.FlyStep(aircraft.Facing);

			// Scale to autorotation speed
			var forwardLength = forward.Length;
			if (forwardLength > 0)
				forward = forward * forwardSpeed / forwardLength;

			// Apply descent
			var descent = new WVec(0, 0, -info.AutorotationDescentRate.Length);
			var move = forward + descent;

			// For CanSlide aircraft, we bypass the velocity system and move directly
			// to avoid interaction with Aircraft.Tick's velocity handling.
			// Zero the velocity so Aircraft.Tick doesn't apply additional movement.
			aircraft.CurrentVelocity = WVec.Zero;
			aircraft.RequestedAcceleration = WVec.Zero;

			// Move the aircraft
			aircraft.SetPosition(self, self.CenterPosition + move);

			// Allow player steering: accept facing changes from queued move orders
			// The activity is non-interruptible, but we process facing from child activities
			// This is handled by the player issuing move orders that get converted to facing

			return false;
		}

	}
}
