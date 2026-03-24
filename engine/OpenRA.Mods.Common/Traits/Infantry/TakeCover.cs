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

using System;
using System.Collections.Generic;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Makes the unit automatically run around when taking damage.")]
<<<<<<< C:/Users/fredr/AppData/Local/Temp/mo.tmp
	class ScaredyCatInfo : ConditionalTraitInfo, Requires<MobileInfo>
=======
	sealed class ScaredyCatInfo : TraitInfo, Requires<MobileInfo>
>>>>>>> C:/Users/fredr/AppData/Local/Temp/mu.tmp
	{
		[Desc("Chance (out of 100) the unit has to enter panic mode when attacked.")]
		public readonly int PanicChance = 100;

		[Desc("How long (in ticks) the actor should panic for.")]
		public readonly int PanicDuration = 250;

		[Desc("Panic movement speed as a percentage of the normal speed.")]
		public readonly int PanicSpeedModifier = 200;

<<<<<<< C:/Users/fredr/AppData/Local/Temp/mo.tmp
		[Desc("Chance (out of 100) the unit has to enter panic mode when attacking.")]
		public readonly int AttackPanicChance = 0;

		[Desc("The terrain types that this actor should avoid running on to while panicking.")]
		public readonly HashSet<string> AvoidTerrainTypes = new();
=======
		[Desc("Damage modifiers for each damage type (defined on the warheads) while the unit is prone.")]
		public readonly Dictionary<string, int> DamageModifiers = new();

		[Desc("Muzzle offset modifier to apply while prone.")]
		public readonly WVec ProneOffset = new(500, 0, 0);
>>>>>>> C:/Users/fredr/AppData/Local/Temp/mu.tmp

		[SequenceReference(prefix: true)]
		public readonly string PanicSequencePrefix = "panic-";

		public override object Create(ActorInitializer init) { return new ScaredyCat(init.Self, this); }
	}

	sealed class ScaredyCat : ITick, INotifyIdle, INotifyDamage, INotifyAttack, ISpeedModifier, ISync, IRenderInfantrySequenceModifier
	{
		readonly ScaredyCatInfo info;
		readonly Mobile mobile;
		readonly Actor self;
		readonly Func<CPos, bool> avoidTerrainFilter;

		[Sync]
<<<<<<< C:/Users/fredr/AppData/Local/Temp/mo.tmp
		int panicStartedTick;
		bool Panicking => panicStartedTick > 0;

		bool IRenderInfantrySequenceModifier.IsModifyingSequence => Panicking;
		string IRenderInfantrySequenceModifier.SequencePrefix => info.PanicSequencePrefix;
=======
		int remainingDuration = 0;

		bool isProne = false;
		void SetProneState(bool state)
		{
			localOffset = state ? info.ProneOffset : WVec.Zero;
			isProne = state;
		}

		bool IRenderInfantrySequenceModifier.IsModifyingSequence => isProne;
		string IRenderInfantrySequenceModifier.SequencePrefix => info.ProneSequencePrefix;
>>>>>>> C:/Users/fredr/AppData/Local/Temp/mu.tmp

		public ScaredyCat(Actor self, ScaredyCatInfo info)
		{
			this.self = self;
			this.info = info;
<<<<<<< C:/Users/fredr/AppData/Local/Temp/mo.tmp
			mobile = self.Trait<Mobile>();

			if (info.AvoidTerrainTypes.Count > 0)
				avoidTerrainFilter = c => info.AvoidTerrainTypes.Contains(self.World.Map.GetTerrainInfo(c).Type);
=======
		}

		protected override void Created(Actor self)
		{
			base.Created(self);

			if (info.DamageTriggers.IsEmpty)
			{
				remainingDuration = info.Duration;
				if (!IsTraitDisabled)
					SetProneState(true);
			}
>>>>>>> C:/Users/fredr/AppData/Local/Temp/mu.tmp
		}

		public void Panic()
		{
<<<<<<< C:/Users/fredr/AppData/Local/Temp/mo.tmp
			if (!Panicking)
				self.CancelActivity();
=======
			if (IsTraitPaused || IsTraitDisabled)
				return;

			if (e.Damage.Value <= 0 || !e.Damage.DamageTypes.Overlaps(info.DamageTriggers))
				return;

			if (!isProne)
				SetProneState(true);
>>>>>>> C:/Users/fredr/AppData/Local/Temp/mu.tmp

			panicStartedTick = self.World.WorldTick;
		}

		void ITick.Tick(Actor self)
		{
<<<<<<< C:/Users/fredr/AppData/Local/Temp/mo.tmp
			if (!Panicking)
				return;

			if (self.World.WorldTick >= panicStartedTick + info.PanicDuration)
			{
				self.CancelActivity();
				panicStartedTick = 0;
			}
=======
			base.Tick(self);

			if (IsTraitDisabled || info.Duration < 0)
				return;

			if (!IsTraitPaused && remainingDuration > 0)
				remainingDuration--;

			if (isProne && remainingDuration == 0)
				SetProneState(false);
>>>>>>> C:/Users/fredr/AppData/Local/Temp/mu.tmp
		}

		void INotifyIdle.TickIdle(Actor self)
		{
<<<<<<< C:/Users/fredr/AppData/Local/Temp/mo.tmp
			if (!Panicking)
				return;
=======
			if (!isProne)
				return 100;

			if (damage == null || damage.DamageTypes.IsEmpty)
				return 100;
>>>>>>> C:/Users/fredr/AppData/Local/Temp/mu.tmp

			// Note: This is just a modified copy of Mobile.Nudge
			var cell = mobile.GetAdjacentCell(self.Location, avoidTerrainFilter);
			if (cell != null)
				self.QueueActivity(false, mobile.MoveTo(cell.Value, 0));
		}

		void INotifyDamage.Damaged(Actor self, AttackInfo e)
		{
<<<<<<< C:/Users/fredr/AppData/Local/Temp/mo.tmp
			if (e.Damage.Value > 0 && self.World.SharedRandom.Next(100) < info.PanicChance)
				Panic();
=======
			return isProne ? info.SpeedModifier : 100;
>>>>>>> C:/Users/fredr/AppData/Local/Temp/mu.tmp
		}

		void INotifyAttack.Attacking(Actor self, in Target target, Armament a, Barrel barrel)
		{
<<<<<<< C:/Users/fredr/AppData/Local/Temp/mo.tmp
			if (self.World.SharedRandom.Next(100) < info.AttackPanicChance)
				Panic();
=======
			remainingDuration = 0;
			SetProneState(false);
>>>>>>> C:/Users/fredr/AppData/Local/Temp/mu.tmp
		}

		void INotifyAttack.PreparingAttack(Actor self, in Target target, Armament a, Barrel barrel) { }

		int ISpeedModifier.GetSpeedModifier()
		{
<<<<<<< C:/Users/fredr/AppData/Local/Temp/mo.tmp
			return Panicking ? info.PanicSpeedModifier : 100;
=======
			if (info.DamageTriggers.IsEmpty)
			{
				remainingDuration = info.Duration;
				SetProneState(true);
			}
>>>>>>> C:/Users/fredr/AppData/Local/Temp/mu.tmp
		}
	}
}
