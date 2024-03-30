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

using System;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("This actor is passable.")]
	public class PassableInfo : ConditionalTraitInfo
	{
		[Desc("Which crush classes does this actor belong to.")]
		public readonly BitSet<PassClass> PassClasses = new BitSet<PassClass>("none");

		[Desc("Player Relationship to be able too pass over (Crush) this Passable.")]
		public readonly PlayerRelationship PassedByRelationships = PlayerRelationship.None; // PlayerRelationship.Ally | PlayerRelationship.Neutral | PlayerRelationship.Enemy;

		[Desc("Relationships where the crush action kills this Passable.")]
		public readonly PlayerRelationship CrushedByRelationships = PlayerRelationship.None;

		[Desc("Probability of mobile actors noticing and evading a crush attempt.")]
		public readonly int WarnProbability = 100;

		[Desc("Sound to play when being passed (crushed).")]
		public readonly string CrushSound = null;

		[Desc("Sound to play when being crushed to death.")]
		public readonly string KillSound = null;

		public override object Create(ActorInitializer init) { return new Passable(init.Self, this); }
	}

	public class Passable : ConditionalTrait<PassableInfo>, IPassable, INotifyBeingPassed
	{
		readonly Actor self;

		public Passable(Actor self, PassableInfo info)
			: base(info)
		{
			this.self = self;
		}

		void INotifyBeingPassed.WarnPass(Actor self, Actor passer, BitSet<PassClass> passClasses)
		{
			// if (!PassableInner(self, passer, passClasses))
			// 	return;

			// Quick fix for infantry losing their queue after being nudged by friendly vehicles
			if (self.Owner.RelationshipWith(passer.Owner) == PlayerRelationship.Ally)
				return;

			var mobile = self.TraitOrDefault<Mobile>();
			if (mobile != null && self.World.SharedRandom.Next(100) <= Info.WarnProbability)
				mobile.Nudge(passer);
		}

		void INotifyBeingPassed.OnBeingPassed(Actor self, Actor passer, BitSet<PassClass> passClasses)
		{
			// if (!PassableInner(self, passer, passClasses))
			// 	return;

			Game.Sound.Play(SoundType.World, Info.CrushSound, passer.CenterPosition);

			var passerMobile = passer.TraitOrDefault<Mobile>();

			/* Helicopter landing on a mine: Exception has occurred: CLR/System.NullReferenceException
				An unhandled exception of type 'System.NullReferenceException' occurred in OpenRA.Mods.Common.dll: 'Object reference not set to an instance of an object.'
				at OpenRA.Mods.Common.Traits.Passable.OpenRA.Mods.Common.Traits.INotifyBeingPassed.OnBeingPassed(Actor self, Actor passer, BitSet`1 passClasses) in C:\Users\fredr\Desktop\WW3MOD\engine\OpenRA.Mods.Common\Traits\Passable.cs:line 74
				at OpenRA.Mods.Common.Traits.Aircraft.PassAction(Actor self, Func`2 action) in C:\Users\fredr\Desktop\WW3MOD\engine\OpenRA.Mods.Common\Traits\Air\Aircraft.cs:line 865
				at OpenRA.Mods.Common.Traits.Aircraft.FinishedMoving(Actor self) in C:\Users\fredr\Desktop\WW3MOD\engine\OpenRA.Mods.Common\Traits\Air\Aircraft.cs:line 848
				at OpenRA.Mods.Common.Traits.Aircraft.SetPosition(Actor self, WPos pos) in C:\Users\fredr\Desktop\WW3MOD\engine\OpenRA.Mods.Common\Traits\Air\Aircraft.cs:line 839
				at OpenRA.Mods.Common.Activities.Fly.VerticalTakeOffOrLandTick(Actor self, Aircraft aircraft, WAngle desiredFacing, WDist desiredAltitude, Boolean idleTurn) in */
			if (Info.CrushedByRelationships.HasRelationship(self.Owner.RelationshipWith(passer.Owner))
				&& passerMobile.Info.LocomotorInfo.Crushes.Overlaps(passClasses)
				&& self.IsAtGroundLevel())
			{
				foreach (var notifyCrushed in self.TraitsImplementing<INotifyBeingPassed>())
					notifyCrushed.OnBeingCrushed(self, passer, passerMobile.Info.LocomotorInfo.Crushes);
			}
		}

		void INotifyBeingPassed.OnBeingCrushed(Actor self, Actor passer, BitSet<PassClass> passClasses)
		{
			var passerMobile = passer.TraitOrDefault<Mobile>();

			if (passer.Info.HasTraitInfo<MineImmuneInfo>())
				return;

			Game.Sound.Play(SoundType.World, Info.KillSound, passer.CenterPosition);
			self.Kill(passer, passerMobile != null ? passerMobile.Info.LocomotorInfo.CrushDamageTypes : default);
		}

		bool IPassable.PassableBy(Actor self, Actor passer, BitSet<PassClass> passClasses)
		{
			return PassableInner(self, passer, passClasses);
		}

		LongBitSet<PlayerBitMask> IPassable.PassableBy(Actor self, BitSet<PassClass> passClasses)
		{
			if (IsTraitDisabled || !Info.PassClasses.Overlaps(passClasses))
				return self.World.NoPlayersMask;

			if (Info.PassedByRelationships.HasRelationship(PlayerRelationship.Ally) || Info.CrushedByRelationships.HasRelationship(PlayerRelationship.Ally))
				if (Info.PassedByRelationships.HasRelationship(PlayerRelationship.Enemy) || Info.CrushedByRelationships.HasRelationship(PlayerRelationship.Enemy))
					return self.World.AllPlayersMask;
				else
					return self.Owner.AlliedPlayersMask;
			else
				if (Info.PassedByRelationships.HasRelationship(PlayerRelationship.Enemy) || Info.CrushedByRelationships.HasRelationship(PlayerRelationship.Enemy))
					return self.World.AllPlayersMask.Except(self.Owner.AlliedPlayersMask);

			return self.World.NoPlayersMask;

			// return self.World.AllPlayersMask.Except(self.Owner.AlliedPlayersMask);

			// if (Info.PassedByRelationships.HasRelationship(PlayerRelationship.Ally) || Info.CrushedByRelationships.HasRelationship(PlayerRelationship.Ally))
			// 	if (Info.PassedByRelationships.HasRelationship(PlayerRelationship.Neutral) || Info.CrushedByRelationships.HasRelationship(PlayerRelationship.Neutral))
			// 		if (Info.PassedByRelationships.HasRelationship(PlayerRelationship.Enemy) || Info.CrushedByRelationships.HasRelationship(PlayerRelationship.Enemy))
			// 			return self.World.AllPlayersMask;
			// 		else
			// 			return self.World.AllPlayersMask.Except(self.Owner.EnemyPlayersMask);
			// 	else
			// 		return self.Owner.AlliedPlayersMask;
			// else if (Info.PassedByRelationships.HasRelationship(PlayerRelationship.Neutral) || Info.CrushedByRelationships.HasRelationship(PlayerRelationship.Neutral))
			// 		if (Info.PassedByRelationships.HasRelationship(PlayerRelationship.Enemy) || Info.CrushedByRelationships.HasRelationship(PlayerRelationship.Enemy))
			// 			return self.World.AllPlayersMask.Except(self.Owner.AlliedPlayersMask);
			// 		else
			// 			return self.World.AllPlayersMask.Except(self.Owner.AlliedPlayersMask).Except(self.Owner.EnemyPlayersMask);
			// else if (Info.PassedByRelationships.HasRelationship(PlayerRelationship.Enemy) || Info.CrushedByRelationships.HasRelationship(PlayerRelationship.Enemy))
			// 	return self.Owner.EnemyPlayersMask;

			// return self.World.NoPlayersMask;
		}

		bool PassableInner(Actor self, Actor passer, BitSet<PassClass> passClasses)
		{
			if (IsTraitDisabled)
				return false;

			var relationship = self.Owner.RelationshipWith(passer.Owner);
			if (!Info.PassedByRelationships.HasRelationship(relationship) && !Info.CrushedByRelationships.HasRelationship(relationship))
				return false;

			return Info.PassClasses.Overlaps(passClasses);
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


	[Desc("Tag trait for stuff that should not trigger mines.")]
	class MineImmuneInfo : TraitInfo<MineImmune> { }
	class MineImmune { }
}
