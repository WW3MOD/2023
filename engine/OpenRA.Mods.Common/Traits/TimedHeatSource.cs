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

using OpenRA.Mods.Common.Distortion;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Emits a local heat haze from this actor. Requires HeatHazeRenderer on the world actor.",
		"This is the actor-side emitter; HeatEventWarhead is the weapon-side one, and both take the same",
		"`Heat:` definition.",
		"A burning wreck, a jet exhaust or a running engine wants Heat.Loop: true plus a RequiresCondition,",
		"so the shimmer repeats for as long as the condition holds. A muzzle blast wants Loop: false and a",
		"short envelope, re-fired each time the condition is granted.")]
	public class TimedHeatSourceInfo : ConditionalTraitInfo
	{
		[FieldLoader.LoadUsing(nameof(LoadHeat), true)]
		[Desc("The heat envelope. See HeatEventDefinition for the keyframe vocabulary.")]
		public readonly HeatEventDefinition Heat = null;

		[Desc("Keep the shimmer on the actor as it moves. Set false for heat left on the ground by",
			"something that has moved on - a scorched patch does not follow the tank that made it.")]
		public readonly bool FollowActor = true;

		static object LoadHeat(MiniYaml yaml)
		{
			return HeatEventDefinition.LoadFrom(yaml, "Heat", true);
		}

		public override void RulesetLoaded(Ruleset rules, ActorInfo ai)
		{
			base.RulesetLoaded(rules, ai);

			if (!rules.Actors[SystemActors.World].HasTraitInfo<HeatHazeRendererInfo>())
				throw new YamlException($"{nameof(TimedHeatSource)} can only be used with the world {nameof(HeatHazeRenderer)} trait.");
		}

		public override object Create(ActorInitializer init) { return new TimedHeatSource(init.Self, this); }
	}

	public class TimedHeatSource : ConditionalTrait<TimedHeatSourceInfo>, INotifyAddedToWorld, INotifyRemovedFromWorld, ITick
	{
		readonly HeatHazeRenderer heat;
		bool inWorld;
		int handle = -1;

		public TimedHeatSource(Actor self, TimedHeatSourceInfo info)
			: base(info)
		{
			heat = self.World.WorldActor.Trait<HeatHazeRenderer>();
		}

		void INotifyAddedToWorld.AddedToWorld(Actor self)
		{
			inWorld = true;
			Restart(self);
		}

		void INotifyRemovedFromWorld.RemovedFromWorld(Actor self)
		{
			inWorld = false;
			Stop();
		}

		protected override void TraitEnabled(Actor self)
		{
			Restart(self);
		}

		protected override void TraitDisabled(Actor self)
		{
			Stop();
		}

		void Restart(Actor self)
		{
			if (!inWorld || IsTraitDisabled)
				return;

			Stop();
			handle = heat.Emit(self.CenterPosition, Info.Heat);
		}

		void Stop()
		{
			if (handle == -1)
				return;

			heat.Cancel(handle);
			handle = -1;
		}

		void ITick.Tick(Actor self)
		{
			if (handle == -1)
				return;

			// A non-looping envelope ends itself in the tracker. Drop the stale handle so a later
			// re-enable starts a fresh event rather than silently doing nothing.
			if (!heat.IsLive(handle))
			{
				handle = -1;
				return;
			}

			if (Info.FollowActor)
				heat.Move(handle, self.CenterPosition);
		}
	}
}
