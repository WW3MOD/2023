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
using OpenRA.Mods.Common.Distortion;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Warheads
{
	[Desc("Emits a local heat haze - a screen-space refraction shimmer - at the impact position.",
		"Requires HeatHazeRenderer on the world actor.",
		"This is the weapon-side emitter; TimedHeatSource is the actor-side one, and both take the same",
		"`Heat:` definition. Nothing about it is nuclear: a thermobaric round, a flamethrower burst or a",
		"fuel fire want the same warhead with smaller numbers.")]
	public class HeatEventWarhead : Warhead
	{
		[FieldLoader.LoadUsing(nameof(LoadHeat), true)]
		[Desc("The heat envelope. See HeatEventDefinition for the keyframe vocabulary.")]
		public readonly HeatEventDefinition Heat = null;

		[Desc("Emit at ground level rather than at the impact altitude.",
			"Almost always what you want: the shimmer is over the heated GROUND and the air above it, and an",
			"airburst's impact position can be tens of cells up. Left at ground level the quad lands where",
			"the heat is; left at altitude it is drawn well above the crater it belongs to.")]
		public readonly bool ForceGroundLevel = true;

		static object LoadHeat(MiniYaml yaml)
		{
			return HeatEventDefinition.LoadFrom(yaml, "Heat", true);
		}

		public override void DoImpact(in Target target, WarheadArgs args)
		{
			if (target.Type == TargetType.Invalid)
				return;

			var world = args.SourceActor.World;
			var renderer = world.WorldActor.TraitOrDefault<HeatHazeRenderer>();
			if (renderer == null)
				return;

			var pos = target.CenterPosition;
			if (ForceGroundLevel)
				pos -= new WVec(0, 0, world.Map.DistanceAboveTerrain(pos).Length);

			renderer.Emit(pos, Heat);
		}
	}
}
