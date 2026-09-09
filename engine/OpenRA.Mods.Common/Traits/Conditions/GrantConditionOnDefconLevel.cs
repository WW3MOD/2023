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

/*
 * The piece that did not exist: a condition producer that GRANTS AND REVOKES as a match rule moves.
 *
 * Every powers gate in this mod is GrantConditionOnLobbyOption, which grants once in Created and has
 * no revoke path whatsoever (Conditions/GrantConditionOnLobbyOption.cs:45-54) -- correct for a lobby
 * checkbox, which cannot change mid-match, and useless for anything that can.
 *
 * The CONSUMER side was already fully dynamic and needs no changes to react. SupportPowerInstance
 * recomputes `instancesEnabled` from `Instances.Any(i => !i.IsTraitDisabled)` every tick
 * (SupportPowerManager.cs:246), and ProvidesPrerequisite is a ConditionalTrait whose enable/disable
 * calls techTree.ActorChanged. So a revoke here reaches a support power's icon and a prerequisite's
 * availability on the next tick with nothing else being written.
 *
 * ONE CONDITION PER LEVEL, mutually exclusive, so YAML can key on any of them independently:
 *   RequiresCondition: defcon-1                 -- exactly at DEFCON 1
 *   RequiresCondition: defcon-1 || defcon-2     -- at 2 or below
 * A "2 or below" cumulative form is deliberately NOT granted: it is expressible with `||` today, and
 * a second, overlapping family of condition names is the kind of thing that gets out of step.
 */

using System.Collections.Generic;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[TraitLocation(SystemActors.Player)]
	[Desc("Grants a condition matching the current DEFCON level, and revokes it when the level moves.",
		"Attach to the Player actor. Requires " + nameof(DefconEscalation) + " on the World actor;",
		"without it, or in the Skirmish game mode, this trait grants nothing at all.")]
	public class GrantConditionOnDefconLevelInfo : TraitInfo
	{
		[GrantedConditionReference]
		[Desc("Condition granted while the match sits at that DEFCON level. The key is the level.",
			"Exactly one is held at a time; changing level revokes the old one before granting the new.",
			"A level with no entry here simply grants nothing, which is how a level can be left unwired.")]
		public readonly Dictionary<int, string> Conditions = new Dictionary<int, string>
		{
			{ 3, "defcon-3" },
			{ 2, "defcon-2" },
			{ 1, "defcon-1" },
		};

		public override object Create(ActorInitializer init) { return new GrantConditionOnDefconLevel(this); }
	}

	public class GrantConditionOnDefconLevel : INotifyCreated, ITick
	{
		readonly GrantConditionOnDefconLevelInfo info;

		DefconEscalation escalation;
		int conditionToken = Actor.InvalidConditionToken;
		int heldLevel = DefconEscalationState.NoLevel;

		public GrantConditionOnDefconLevel(GrantConditionOnDefconLevelInfo info)
		{
			this.info = info;
		}

		// The whole grant decision, as a pure function, because it is the one thing worth being able to
		// assert about this trait without a World: "no condition is granted in Skirmish" is exactly
		// `ConditionFor(NoLevel, ...) == null`. Apply below has no other way to reach a condition name.
		public static string ConditionFor(int level, IReadOnlyDictionary<int, string> conditions)
		{
			if (level == DefconEscalationState.NoLevel || conditions == null)
				return null;

			if (!conditions.TryGetValue(level, out var condition) || string.IsNullOrEmpty(condition))
				return null;

			return condition;
		}

		void INotifyCreated.Created(Actor self)
		{
			escalation = self.World.WorldActor.TraitOrDefault<DefconEscalation>();

			// Apply immediately so the opening level is held from the first tick rather than one tick in.
			Apply(self);
		}

		// Polling one int per player per tick, rather than an INotify* the World trait fires. It is
		// deliberate: polling has no creation-order dependency between the World actor and the player
		// actors, and no way to miss an edge. There are at most a handful of player actors, and the
		// body below returns on the first comparison in every tick where nothing moved.
		void ITick.Tick(Actor self)
		{
			Apply(self);
		}

		void Apply(Actor self)
		{
			var level = escalation?.Level ?? DefconEscalationState.NoLevel;
			if (level == heldLevel)
				return;

			heldLevel = level;

			if (conditionToken != Actor.InvalidConditionToken)
				conditionToken = self.RevokeCondition(conditionToken);

			var condition = ConditionFor(level, info.Conditions);
			if (condition != null)
				conditionToken = self.GrantCondition(condition);
		}
	}
}
