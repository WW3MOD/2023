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

using System.Collections.Generic;
using System.Linq;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Grants a condition to this actor when enemy units are within range. Does not change ownership.")]
	public class ProximityContestableInfo : TraitInfo, IRulesetLoaded
	{
		[FieldLoader.Require]
		[GrantedConditionReference]
		[Desc("The condition to grant when contested by enemy units.")]
		public readonly string Condition = null;

		[Desc("Maximum range at which enemy ProximityCaptor actors trigger the contested state.")]
		public readonly WDist Range = WDist.FromCells(5);

		[Desc("Allowed ProximityCaptor types that can contest this actor.")]
		public readonly BitSet<CaptureType> CaptorTypes = new BitSet<CaptureType>("Player", "Vehicle", "Tank", "Infantry");

		public void RulesetLoaded(Ruleset rules, ActorInfo info)
		{
			var pci = rules.Actors[SystemActors.Player].TraitInfoOrDefault<ProximityCaptorInfo>();
			if (pci == null)
				throw new YamlException("ProximityContestable requires the `Player` actor to have the ProximityCaptor trait.");
		}

		public override object Create(ActorInitializer init) { return new ProximityContestable(init.Self, this); }
	}

	public class ProximityContestable : INotifyAddedToWorld, INotifyRemovedFromWorld
	{
		readonly ProximityContestableInfo info;
		readonly Actor self;
		readonly List<Actor> enemyActorsInRange = new List<Actor>();

		int proximityTrigger;
		int conditionToken = Actor.InvalidConditionToken;

		public ProximityContestable(Actor self, ProximityContestableInfo info)
		{
			this.info = info;
			this.self = self;
		}

		void INotifyAddedToWorld.AddedToWorld(Actor self)
		{
			proximityTrigger = self.World.ActorMap.AddProximityTrigger(self.CenterPosition, info.Range, WDist.Zero, ActorEntered, ActorLeft);
		}

		void INotifyRemovedFromWorld.RemovedFromWorld(Actor self)
		{
			self.World.ActorMap.RemoveProximityTrigger(proximityTrigger);
			enemyActorsInRange.Clear();

			if (conditionToken != Actor.InvalidConditionToken)
				conditionToken = self.RevokeCondition(conditionToken);
		}

		bool CanContestBy(Actor a)
		{
			if (a == self || a.Disposed)
				return false;

			// Only enemy units can contest
			if (self.Owner.RelationshipWith(a.Owner) != PlayerRelationship.Enemy)
				return false;

			var pc = a.Info.TraitInfoOrDefault<ProximityCaptorInfo>();
			return pc != null && pc.Types.Overlaps(info.CaptorTypes);
		}

		void ActorEntered(Actor other)
		{
			if (!CanContestBy(other))
				return;

			enemyActorsInRange.Add(other);
			UpdateCondition();
		}

		void ActorLeft(Actor other)
		{
			if (enemyActorsInRange.Remove(other))
				UpdateCondition();
		}

		void UpdateCondition()
		{
			// Clean up disposed actors
			enemyActorsInRange.RemoveAll(a => a.Disposed || !a.IsInWorld);

			var shouldBeContested = enemyActorsInRange.Count > 0;
			var isContested = conditionToken != Actor.InvalidConditionToken;

			if (shouldBeContested && !isContested)
				conditionToken = self.GrantCondition(info.Condition);
			else if (!shouldBeContested && isContested)
				conditionToken = self.RevokeCondition(conditionToken);
		}
	}
}
