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

/* Could add Crush Damage, and give that much damage instead of just killing instantly */

using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("This actor is crushable.")]
	class CrushableInfo : ConditionalTraitInfo
	{

		[Desc("Which crush classes does this actor belong to.")]
		public readonly BitSet<CrushClass> CrushClasses = new BitSet<CrushClass>("none");

		[Desc("Player Relationship to be able too pass over (Crush) this Crushable.")]
		public readonly PlayerRelationship CrushedByRelationships = PlayerRelationship.Ally | PlayerRelationship.Neutral | PlayerRelationship.Enemy;

		[Desc("Relationships where the crush action kills this Crushable.")]
		public readonly PlayerRelationship KilledByRelationships = PlayerRelationship.Ally | PlayerRelationship.Neutral | PlayerRelationship.Enemy;

		[Desc("Probability of mobile actors noticing and evading a crush attempt.")]
		public readonly int WarnProbability = 100;

		[Desc("Sound to play when being passed (crushed).")]
		public readonly string CrushSound = null;

		[Desc("Sound to play when being crushed to death.")]
		public readonly string KillSound = null;

		public override object Create(ActorInitializer init) { return new Crushable(init.Self, this); }
	}

	class Crushable : ConditionalTrait<CrushableInfo>, ICrushable, INotifyCrushed
	{
		readonly Actor self;

		public Crushable(Actor self, CrushableInfo info)
			: base(info)
		{
			this.self = self;
		}

		void INotifyCrushed.WarnCrush(Actor self, Actor crusher, BitSet<CrushClass> crushClasses)
		{
			if (!CrushableInner(self, crusher, crushClasses))
				return;

			var mobile = self.TraitOrDefault<Mobile>();
			if (mobile != null && self.World.SharedRandom.Next(100) <= Info.WarnProbability)
				mobile.Nudge(crusher);
		}

		void INotifyCrushed.OnCrush(Actor self, Actor crusher, BitSet<CrushClass> crushClasses)
		{
			if (!CrushableInner(self, crusher, crushClasses))
				return;

			Game.Sound.Play(SoundType.World, Info.CrushSound, crusher.CenterPosition);

			var crusherMobile = crusher.TraitOrDefault<Mobile>();

			if (Info.KilledByRelationships.HasRelationship(self.Owner.RelationshipWith(crusher.Owner))
				&& crusherMobile.Info.LocomotorInfo.Kills.Overlaps(crushClasses))
			{
				Game.Sound.Play(SoundType.World, Info.KillSound, crusher.CenterPosition);
				self.Kill(crusher, crusherMobile != null ? crusherMobile.Info.LocomotorInfo.CrushDamageTypes : default);
			}
		}

		bool ICrushable.CrushableBy(Actor self, Actor crusher, BitSet<CrushClass> crushClasses)
		{
			return CrushableInner(self, crusher, crushClasses);
		}

		LongBitSet<PlayerBitMask> ICrushable.CrushableBy(Actor self, BitSet<CrushClass> crushClasses)
		{
			if (IsTraitDisabled || !Info.CrushClasses.Overlaps(crushClasses))
				return self.World.NoPlayersMask;

			return Info.CrushedByRelationships.HasRelationship(PlayerRelationship.Ally) ? self.World.AllPlayersMask : self.World.AllPlayersMask.Except(self.Owner.AlliedPlayersMask);
		}

		bool CrushableInner(Actor self, Actor crusher, BitSet<CrushClass> crushClasses)
		{
			if (IsTraitDisabled)
				return false;

			var shouldCrush = Info.CrushedByRelationships.HasRelationship(self.Owner.RelationshipWith(crusher.Owner));

			var relationship = self.Owner.RelationshipWith(crusher.Owner);
			if (!Info.CrushedByRelationships.HasRelationship(relationship))
				return false;

			return Info.CrushClasses.Overlaps(crushClasses);
		}

		protected override void TraitEnabled(Actor self)
		{
			self.World.ActorMap.UpdateOccupiedCells(self.OccupiesSpace);
		}

		protected override void TraitDisabled(Actor self)
		{
			self.World.ActorMap.UpdateOccupiedCells(self.OccupiesSpace);
		}
	}
}
