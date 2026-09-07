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

using OpenRA.GameRules;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Warheads
{
	[Desc("Makes the screen shake. The model, and what these fields mean, is documented on",
		"ScreenShakerInfo — read that before picking numbers.")]
	public class ShakeScreenWarhead : Warhead
	{
		[Desc("Duration of the shaking, in ticks, measured from the moment the ground wave reaches",
			"the camera rather than from detonation.")]
		public readonly int Duration = 0;

		[Desc("PEAK SCREEN DISPLACEMENT IN PIXELS at the epicentre. This is NOT the pre-2026-09-06",
			"meaning: it used to be the numerator of an inverse-square falloff, where 80 was an",
			"ordinary tactical-nuke value. Now 80 would be eighty pixels of camera throw. Rough",
			"scale: 2 a shell landing nearby, 8 a building collapsing next to you, 14-20 a nuclear",
			"detonation on top of you.")]
		public readonly int Intensity = 0;

		[Desc("Per-axis scale on this effect's displacement, comma-separated.",
			"PITFALL: this used to default to 0,0 — and because upstream summed the multipliers of",
			"every live effect and applied the total to all of them, a warhead that omitted it",
			"contributed nothing AND could not be rescued by its own Intensity. Every ww3mod",
			"ShakeScreen omitted it, so the nuke shake only appeared when some ShakeOnDeath building",
			"happened to be dying at the same moment and supplied a multiplier for it. It now",
			"defaults to 1,1 and applies per effect.")]
		public readonly float2 Multiplier = new(1, 1);

		[Desc("Ticks to ramp from silence to full amplitude. Small is sharp; a large event that",
			"builds over half a second feels heavier than one that starts at full throw.")]
		public readonly int AttackTicks = 2;

		[Desc("Ticks for the amplitude to halve — the seismic coda decay constant, and the main",
			"lever for 'long and fading' against 'short and hard'. 0 derives Duration / 4, which",
			"leaves about 6% of peak at expiry.")]
		public readonly int DecayHalfLife = 0;

		[Desc("Ticks of linear taper at the end so the effect never stops dead on one frame.",
			"0 derives min(20, Duration / 3).")]
		public readonly int ReleaseTicks = 0;

		[Desc("Percent scale on the frequency this Intensity would otherwise imply. Below 100 is a",
			"deeper roll, above 100 a sharper rattle, without touching the amplitude.")]
		public readonly int FrequencyScale = 100;

		[Desc("Ticks this event's wave takes to travel one cell, overriding the global ground-wave",
			"speed. 0 inherits it. The reason this exists: a detonation emits TWO waves and they do",
			"not travel together. The ground wave is the global ~0.9 t/cell; the AIR BLAST that",
			"follows it is ~7.8 t/cell (the same number BlastWave uses as WaveSpeed 7), so a staged",
			"shake meant to represent the shockwave rattling the camera should set that here and it",
			"will then track the visible wavefront at every distance instead of only at ground zero.")]
		public readonly float PropagationTicksPerCell = 0f;

		[Desc("Ceiling on this event's arrival delay in ticks, overriding the global one. 0 inherits",
			"it. Set it WITH PropagationTicksPerCell or not at all: the global ceiling is sized for",
			"the fast ground wave, so a stage slowed down to chase an air blast saturates against it",
			"and every camera past that range gets the rattle at the same flat time however far away",
			"it is. The right value is the front's own travel time to the edge of its blast radius.",
			"It is raised per event rather than globally because it also fixes how long the effect is",
			"kept alive, and one slow superweapon stage should not lengthen every shake in the mod.")]
		public readonly int MaxPropagationDelay = 0;

		[Desc("Distance over which this event's amplitude falls by 1/e, overriding the global value.",
			"0 inherits it. Use it to give a small, frequent event a short horizon so a battle full",
			"of them does not sum into a permanent background rumble across the whole map.")]
		public readonly WDist AttenuationDistance = WDist.Zero;

		public override void DoImpact(in Target target, WarheadArgs args)
		{
			args.SourceActor.World.WorldActor.Trait<ScreenShaker>().AddEffect(target.CenterPosition,
				new ShakeParams
				{
					Duration = Duration,
					Intensity = Intensity,
					Multiplier = Multiplier,
					AttackTicks = AttackTicks,
					DecayHalfLife = DecayHalfLife,
					ReleaseTicks = ReleaseTicks,
					FrequencyScale = FrequencyScale,
					PropagationTicksPerCell = PropagationTicksPerCell,
					MaxPropagationDelay = MaxPropagationDelay,
					AttenuationDistance = AttenuationDistance
				});
		}
	}
}
