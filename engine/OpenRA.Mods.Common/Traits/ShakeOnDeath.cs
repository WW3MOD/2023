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
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Shakes the screen when this actor dies. The model, and what these fields mean, is",
		"documented on ScreenShakerInfo — read that before picking numbers.")]
	public class ShakeOnDeathInfo : TraitInfo
	{
		[Desc("DeathType(s) that trigger the shake. Leave empty to always trigger a shake.")]
		public readonly BitSet<DamageType> DeathTypes = default;

		[Desc("Duration of the shaking, in ticks, from the moment the wave reaches the camera.")]
		public readonly int Duration = 45;

		[Desc("PEAK SCREEN DISPLACEMENT IN PIXELS at the actor. NOT the pre-2026-09-06 meaning —",
			"see the note on ShakeScreenWarhead.Intensity.")]
		public readonly int Intensity = 5;

		[Desc("Per-axis scale on this effect's displacement, comma-separated.")]
		public readonly float2 Multiplier = new(1, 1);

		[Desc("Ticks to ramp from silence to full amplitude.")]
		public readonly int AttackTicks = 2;

		[Desc("Ticks for the amplitude to halve. 0 derives Duration / 4.")]
		public readonly int DecayHalfLife = 0;

		[Desc("Ticks of linear taper at the end. 0 derives min(20, Duration / 3).")]
		public readonly int ReleaseTicks = 0;

		[Desc("Percent scale on the frequency this Intensity would otherwise imply.")]
		public readonly int FrequencyScale = 100;

		[Desc("Ticks this event's wave takes to travel one cell. 0 inherits the global value.")]
		public readonly float PropagationTicksPerCell = 0f;

		[Desc("Ceiling on this event's arrival delay in ticks. 0 inherits the global value. Only",
			"worth setting alongside a slow PropagationTicksPerCell -- see ShakeScreenWarhead.")]
		public readonly int MaxPropagationDelay = 0;

		[Desc("Distance over which this event's amplitude falls by 1/e. 0 inherits the global value.",
			"Worth setting tight here: every building death on the map raises one of these, and the",
			"global horizon is generous enough that a busy battle would otherwise sum a lot of very",
			"distant collapses into a constant low rumble.")]
		public readonly WDist AttenuationDistance = WDist.Zero;

		public override object Create(ActorInitializer init) { return new ShakeOnDeath(this); }
	}

	public class ShakeOnDeath : INotifyKilled
	{
		readonly ShakeOnDeathInfo info;

		public ShakeOnDeath(ShakeOnDeathInfo info)
		{
			this.info = info;
		}

		void INotifyKilled.Killed(Actor self, AttackInfo e)
		{
			if (!info.DeathTypes.IsEmpty && !e.Damage.DamageTypes.Overlaps(info.DeathTypes))
				return;

			self.World.WorldActor.Trait<ScreenShaker>().AddEffect(self.CenterPosition,
				new ShakeParams
				{
					Duration = info.Duration,
					Intensity = info.Intensity,
					Multiplier = info.Multiplier,
					AttackTicks = info.AttackTicks,
					DecayHalfLife = info.DecayHalfLife,
					ReleaseTicks = info.ReleaseTicks,
					FrequencyScale = info.FrequencyScale,
					PropagationTicksPerCell = info.PropagationTicksPerCell,
					MaxPropagationDelay = info.MaxPropagationDelay,
					AttenuationDistance = info.AttenuationDistance
				});
		}
	}
}
