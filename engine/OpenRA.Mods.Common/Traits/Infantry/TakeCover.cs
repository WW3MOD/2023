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

using System;
using System.Collections.Generic;
using System.Linq;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Make the unit go prone when under attack, in an attempt to reduce damage. Disable to go permanent prone or Pause to inactivate")]
	public class TakeCoverInfo : TurretedInfo
	{
		[Desc("How long (in ticks) the actor remains prone.",
			"Negative values mean actor remains prone permanently.")]
		public readonly int Duration = 150;

		[Desc("Prone movement speed as a percentage of the normal speed.")]
		public readonly int SpeedModifier = 60;

		[Desc("Prone firing inaccuracy as a percentage.")]
		public readonly int InaccuracyModifier = 100;

		[Desc("Prone movement speed as a percentage of the normal speed.")]
		public readonly int SpeedModifierWhenDeployed = 40;

		[Desc("Damage types that trigger prone state. Defined on the warheads.",
			"If Duration is negative (permanent), you can leave this empty to trigger prone state immediately.")]
		public readonly BitSet<DamageType> DamageTriggers = default;

		[Desc("Damage modifiers for each damage type (defined on the warheads) while the unit is prone.")]
		public readonly Dictionary<string, int> DamageModifiers = new Dictionary<string, int>();

		[Desc("Muzzle offset modifier to apply while prone.")]
		public readonly WVec ProneOffset = new WVec(500, 0, 0);

		[SequenceReference(prefix: true)]
		[Desc("Sequence prefix to apply while prone.")]
		public readonly string ProneSequencePrefix = "prone-";

		[Desc("Types of damage that triggers Panic.")]
		public readonly BitSet<DamageType> PanicTriggerDamageTypes = default;

		[Desc("How long (in ticks) the actor should panic for.")]
		public readonly int PanicLength = 100;

		[Desc("Panic movement speed as a percentage of the normal speed.")]
		public readonly int PanicSpeedModifier = 150;

		[Desc("The terrain types that this actor should avoid running on to while panicking.")]
		public readonly HashSet<string> AvoidTerrainTypes = new HashSet<string>();

		public override object Create(ActorInitializer init) => new TakeCover(init, this);

		public override void RulesetLoaded(Ruleset rules, ActorInfo ai)
		{
			if (Duration > -1 && DamageTriggers.IsEmpty)
				throw new YamlException("TakeCover: If Duration isn't negative (permanent), DamageTriggers is required.");

			base.RulesetLoaded(rules, ai);
		}
	}

	public class TakeCover : Turreted, INotifyIdle, INotifyDamage, INotifyBecomingIdle, INotifyMoving, INotifyAttack, IDamageModifier, IInaccuracyModifier, ISpeedModifier, ISync, IRenderInfantrySequenceModifier
	{
		readonly TakeCoverInfo info;

		readonly Actor self;

		readonly Mobile mobile;

		readonly Func<CPos, bool> avoidTerrainFilter;

		[Sync]
		int remainingDuration = 0;

		[Sync]
		int panicStartedTick;

		bool isPaused = false;

		bool IsProne => remainingDuration == -1 || (!IsTraitDisabled && remainingDuration > 0);

		bool IsPanicking => panicStartedTick > 0;

		bool IRenderInfantrySequenceModifier.IsModifyingSequence => (!isPaused && IsProne) || IsPanicking;
		string IRenderInfantrySequenceModifier.SequencePrefix => IsPanicking ? "panic-" : IsProne ? info.ProneSequencePrefix : "";

		public TakeCover(ActorInitializer init, TakeCoverInfo info)
			: base(init, info)
		{
			self = init.Self;
			this.info = info;
			if (info.Duration < 0 && info.DamageTriggers.IsEmpty)
				remainingDuration = info.Duration;

			mobile = init.Self.Trait<Mobile>();

			if (info.AvoidTerrainTypes.Count > 0)
				avoidTerrainFilter = c => info.AvoidTerrainTypes.Contains(init.Self.World.Map.GetTerrainInfo(c).Type);
		}

		void INotifyBecomingIdle.OnBecomingIdle(OpenRA.Actor self)
		{
			if (IsTraitPaused || IsTraitDisabled) // deployed
				return;

			remainingDuration = -1;
		}

		void INotifyMoving.MovementTypeChanged(OpenRA.Actor self, OpenRA.Mods.Common.Traits.MovementType type)
		{
			if (IsTraitPaused || IsTraitDisabled) // deployed
				return;

			if (remainingDuration > 0) // ongoing countdown from taking damage, stay prone
				return;

			if (type == MovementType.None || type == MovementType.Turn) // not an actual movement
				return;

			if (!IsProne)
				localOffset = info.ProneOffset;

			remainingDuration = 0; // Soldier should get up and run
		}

		void INotifyDamage.Damaged(Actor self, AttackInfo e)
		{
			if (e.Damage.DamageTypes.Overlaps(info.PanicTriggerDamageTypes))
			{
				Panic();
			}

			if (IsTraitPaused || IsTraitDisabled) // deployed
				return;

			if (remainingDuration == -1) // is already permanently prone
				return;

			/* // Uncommented so that all damage makes soldiers take cover because why not?
			if (e.Damage.Value <= 0 || !e.Damage.DamageTypes.Overlaps(info.DamageTriggers))
				return; */

			if (!IsProne)
				localOffset = info.ProneOffset;

			remainingDuration = info.Duration; // taking cover temporarily
		}

		void INotifyAttack.PreparingAttack(OpenRA.Actor self, in OpenRA.Traits.Target target, OpenRA.Mods.Common.Traits.Armament a, OpenRA.Mods.Common.Traits.Barrel barrel)
		{
			// TODO: MustStandUpToAttack
		}

		void INotifyAttack.Attacking(OpenRA.Actor self, in OpenRA.Traits.Target target, OpenRA.Mods.Common.Traits.Armament a, OpenRA.Mods.Common.Traits.Barrel barrel)
		{
			if (!IsProne)
				localOffset = info.ProneOffset;

			// TODO: Make (some) soldiers able to fire on the move, but for now this works
			remainingDuration = -1; // Go prone
		}

		protected override void Tick(Actor self)
		{
			base.Tick(self);

			if (!IsTraitPaused && remainingDuration > 0)
				remainingDuration--;

			if (remainingDuration == 0)
				localOffset = WVec.Zero;

			if (IsPanicking && self.World.WorldTick >= panicStartedTick + info.PanicLength)
			{
				self.CancelActivity();
				panicStartedTick = 0;
			}
		}

		void INotifyIdle.TickIdle(Actor self)
		{
			if (!IsPanicking)
				return;

			// Note: This is just a modified copy of Mobile.Nudge
			var cell = mobile.GetAdjacentCell(self.Location, avoidTerrainFilter);
			if (cell != null)
				self.QueueActivity(false, mobile.MoveTo(cell.Value, 0));

			// CVec delta = self.Location - e.Attacker.Location;

			// Game.Debug("Length {0}", delta.Length);

			// if (delta.Length < 1)
			// {
			// 	var cell = mobile.GetAdjacentCell(self.Location, avoidTerrainFilter);
			// 	if (cell != null)
			// 		self.QueueActivity(false, mobile.MoveTo(cell.Value, 0, null, false, Color.OrangeRed));
			// } else {
			// 	// while (delta.Length > 6) { delta /= 2; }
			// 	var cell = mobile.GetAdjacentCell(self.Location - delta, avoidTerrainFilter);
			// 	if (cell != null)
			// 		self.QueueActivity(false, mobile.MoveTo(cell.Value, 0, null, false, Color.OrangeRed));
			// }
		}

		public void Panic()
		{
			if (!IsPanicking)
				self.CancelActivity();

			panicStartedTick = self.World.WorldTick;
		}

		public override bool HasAchievedDesiredFacing => true;

		int IDamageModifier.GetDamageModifier(Actor attacker, Damage damage)
		{
			if (!IsProne)
				return 100;

			if (damage == null || damage.DamageTypes.IsEmpty)
				return 100;

			var modifierPercentages = info.DamageModifiers.Where(x => damage.DamageTypes.Contains(x.Key)).Select(x => x.Value);
			return Util.ApplyPercentageModifiers(100, modifierPercentages);
		}

		int IInaccuracyModifier.GetInaccuracyModifier()
		{
			if (!IsProne)
				return 100;

			var percentage = remainingDuration == -1 ? 100 : 10;

			return percentage;
		}

		// TODO: Change depending on unit experience, rookie soldiers gets almost completely pinned down when under fire?
		int ISpeedModifier.GetSpeedModifier()
		{
			return IsPanicking ? info.PanicSpeedModifier : IsProne ?
				remainingDuration == -1 ? info.SpeedModifierWhenDeployed : info.SpeedModifier
				: 100;
		}

		// Trait disables when unit is deployed
		protected override void TraitDisabled(Actor self)
		{
			remainingDuration = -1; // Take cover permanently
		}

		// When undeployed, the initial stance
		protected override void TraitEnabled(Actor self)
		{
			if (info.Duration < 0 && info.DamageTriggers.IsEmpty)
			{
				remainingDuration = info.Duration;
				localOffset = info.ProneOffset;
			}
		}

		protected override void TraitResumed(Actor self)
		{
			isPaused = false;
		}

		protected override void TraitPaused(Actor self)
		{
			isPaused = true;
		}
	}
}
