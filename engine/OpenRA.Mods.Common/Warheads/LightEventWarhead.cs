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
using OpenRA.Mods.Common.Lighting;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Warheads
{
	[Desc("Emits a time-varying local light at the impact position. Requires LightEventManager on the world actor.",
		"This is the weapon-side emitter; TimedLightSource is the actor-side one, and both take the same",
		"`Light:` definition.")]
	public class LightEventWarhead : Warhead
	{
		[FieldLoader.LoadUsing(nameof(LoadLight), true)]
		[Desc("The light envelope. See LightEventDefinition for the keyframe vocabulary.")]
		public readonly LightEventDefinition Light = null;

		[Desc("Emit at ground level rather than at the impact altitude. An airburst still lights the ground",
			"either way - the terrain footprint is computed from horizontal distance - but a light left at",
			"altitude is further from every sprite on the ground and so dimmer on them.")]
		public readonly bool ForceGroundLevel = false;

		static object LoadLight(MiniYaml yaml)
		{
			return LightEventDefinition.LoadFrom(yaml, "Light", true);
		}

		public override void DoImpact(in Target target, WarheadArgs args)
		{
			if (target.Type == TargetType.Invalid)
				return;

			var world = args.SourceActor.World;
			var manager = world.WorldActor.TraitOrDefault<LightEventManager>();
			if (manager == null)
				return;

			var pos = target.CenterPosition;
			if (ForceGroundLevel)
				pos -= new WVec(0, 0, world.Map.DistanceAboveTerrain(pos).Length);

			manager.Emit(pos, Light);
		}
	}
}
