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
	/// Controlled autorotation descent for helicopters at heavy damage.
	/// The helicopter glides forward, slowly losing altitude.
	/// Player can steer (turn) but cannot gain altitude or stop.
	/// Features: gradual acceleration, player steering, flare before touchdown.
	/// On landing: safe terrain = disabled + repairable, unsafe = destroyed.
	/// </summary>
	public class HeliAutorotate : Activity
	{
		readonly HeliEmergencyLanding emergencyLanding;
		readonly HeliEmergencyLandingInfo info;
		readonly Aircraft aircraft;
		readonly int targetForwardSpeed;
		bool landed;
		int landedTicks;
		bool rotorsStopped;

		// Acceleration: ramp up from 0 to targetForwardSpeed
		int currentSpeed;

		public HeliAutorotate(Actor self, HeliEmergencyLanding emergencyLanding,
			HeliEmergencyLandingInfo info, Aircraft aircraft, int forwardSpeed)
		{
			this.emergencyLanding = emergencyLanding;
			this.info = info;
			this.aircraft = aircraft;
			this.targetForwardSpeed = forwardSpeed;

			// Player can issue steering orders but not cancel the autorotation
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

			// --- Steering: turn toward player-designated facing ---
			var desiredFacing = emergencyLanding.DesiredAutorotationFacing;
			if (desiredFacing.HasValue)
			{
				var turnSpeed = aircraft.Info.TurnSpeed;
				aircraft.Facing = Util.TickFacing(aircraft.Facing, desiredFacing.Value, turnSpeed);
			}

			// --- Acceleration: gradually ramp up speed ---
			if (currentSpeed < targetForwardSpeed)
			{
				currentSpeed = Math.Min(currentSpeed + info.AutorotationAcceleration, targetForwardSpeed);
			}

			// --- Flare: reduce descent rate and speed near ground ---
			var altitude = self.World.Map.DistanceAboveTerrain(self.CenterPosition).Length;
			var flareAltitude = info.FlareAltitude.Length;
			var effectiveSpeed = currentSpeed;
			var descentRate = info.AutorotationDescentRate.Length;

			if (flareAltitude > 0 && altitude < flareAltitude)
			{
				// Linear interpolation: at flareAltitude = 100%, at ground = FlarePercent%
				var flareFraction = (float)altitude / flareAltitude;

				// Descent: lerp from FlareDescentPercent% to 100%
				var descentPercent = info.FlareDescentPercent + (int)((100 - info.FlareDescentPercent) * flareFraction);
				descentRate = descentRate * descentPercent / 100;

				// Speed: lerp from FlareSpeedPercent% to 100%
				var speedPercent = info.FlareSpeedPercent + (int)((100 - info.FlareSpeedPercent) * flareFraction);
				effectiveSpeed = effectiveSpeed * speedPercent / 100;
			}

			// Calculate forward movement based on current facing
			var forward = aircraft.FlyStep(aircraft.Facing);

			// Scale to effective autorotation speed
			var forwardLength = forward.Length;
			if (forwardLength > 0)
				forward = forward * effectiveSpeed / forwardLength;

			// Apply descent
			var descent = new WVec(0, 0, -descentRate);
			var move = forward + descent;

			// For CanSlide aircraft, we bypass the velocity system and move directly
			// to avoid interaction with Aircraft.Tick's velocity handling.
			// Zero the velocity so Aircraft.Tick doesn't apply additional movement.
			aircraft.CurrentVelocity = WVec.Zero;
			aircraft.RequestedAcceleration = WVec.Zero;

			// Move the aircraft
			aircraft.SetPosition(self, self.CenterPosition + move);

			return false;
		}

	}
}
