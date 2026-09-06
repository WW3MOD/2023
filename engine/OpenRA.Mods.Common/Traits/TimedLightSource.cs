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

using OpenRA.Mods.Common.Lighting;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Emits a time-varying local light from this actor. Requires LightEventManager on the world actor.",
		"This is the actor-side emitter; LightEventWarhead is the weapon-side one, and both take the same",
		"`Light:` definition.",
		"A burning wreck, a flare or a searchlight wants Light.Loop: true plus a RequiresCondition, so the",
		"envelope repeats for as long as the condition holds. A muzzle flash wants Loop: false and a short",
		"envelope, re-fired each time the condition is granted.")]
	public class TimedLightSourceInfo : ConditionalTraitInfo
	{
		[FieldLoader.LoadUsing(nameof(LoadLight), true)]
		[Desc("The light envelope. See LightEventDefinition for the keyframe vocabulary.")]
		public readonly LightEventDefinition Light = null;

		[Desc("Keep the light on the actor as it moves. Costs one partition update per tick while the actor is",
			"moving; set false for a light on something that cannot move.")]
		public readonly bool FollowActor = true;

		static object LoadLight(MiniYaml yaml)
		{
			return LightEventDefinition.LoadFrom(yaml, "Light", true);
		}

		public override void RulesetLoaded(Ruleset rules, ActorInfo ai)
		{
			base.RulesetLoaded(rules, ai);

			if (!rules.Actors[SystemActors.World].HasTraitInfo<LightEventManagerInfo>())
				throw new YamlException($"{nameof(TimedLightSource)} can only be used with the world {nameof(LightEventManager)} trait.");
		}

		public override object Create(ActorInitializer init) { return new TimedLightSource(init.Self, this); }
	}

	public class TimedLightSource : ConditionalTrait<TimedLightSourceInfo>, INotifyAddedToWorld, INotifyRemovedFromWorld, ITick
	{
		readonly LightEventManager manager;
		bool inWorld;
		int handle = -1;

		public TimedLightSource(Actor self, TimedLightSourceInfo info)
			: base(info)
		{
			manager = self.World.WorldActor.Trait<LightEventManager>();
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
			handle = manager.Emit(self.CenterPosition, Info.Light);
		}

		void Stop()
		{
			if (handle == -1)
				return;

			manager.Cancel(handle);
			handle = -1;
		}

		void ITick.Tick(Actor self)
		{
			if (handle == -1)
				return;

			// A non-looping envelope ends itself in the manager. Drop the stale handle so a later re-enable
			// starts a fresh event rather than silently doing nothing.
			if (!manager.IsLive(handle))
			{
				handle = -1;
				return;
			}

			if (Info.FollowActor)
				manager.Move(handle, self.CenterPosition);
		}
	}
}
