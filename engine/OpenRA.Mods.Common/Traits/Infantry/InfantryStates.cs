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
using OpenRA.Support;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Make the unit go prone when under attack, in an attempt to reduce damage. Disable to go permanent prone or Pause to inactivate")]
	public class InfantryStatesInfo : TurretedInfo, IObservesVariablesInfo
	{
		[ConsumedConditionReference]
		[Desc("Conditions to activate a third custom sequence")]
		public readonly BooleanExpression ProneCondition = null;

		public readonly string ProneGrantsCondition = "prone";

		[Desc("Prone movement speed as a percentage of the normal speed.")]
		public readonly int ProneSpeedModifier = 60;

		[ConsumedConditionReference]
		[Desc("Conditions to activate a third custom sequence")]
		public readonly BooleanExpression PanicCondition = null;
		public readonly string PanicGrantsCondition = "panicking";

		[Desc("Panic movement speed as a percentage of the normal speed.")]
		public readonly int PanicSpeedModifier = 150;

		[ConsumedConditionReference]
		[Desc("Conditions to activate a third custom sequence")]
		public readonly BooleanExpression ActiveCondition = null;

		[Desc("Damage modifiers for each damage type (defined on the warheads) while the unit is prone.")]
		public readonly Dictionary<string, int> ProneDamageModifiers = new Dictionary<string, int>();

		[Desc("Muzzle offset modifier to apply while prone.")]
		public readonly WVec ProneOffset = new WVec(500, 0, 0);

		[SequenceReference(prefix: true)]
		[Desc("Sequence prefix to apply while prone.")]
		public readonly string ProneSequencePrefix = "prone-";

		[SequenceReference(prefix: true)]
		[Desc("Sequence prefix to apply while prone.")]
		public readonly string ActiveSequencePrefix = "active-";

		[SequenceReference(prefix: true)]
		[Desc("Sequence prefix to apply while panicing.")]
		public readonly string PanicSequencePrefix = "panic-";

		[Desc("The terrain types that this actor should avoid running on to while panicking.")]
		public readonly HashSet<string> AvoidTerrainTypes = new HashSet<string>();

		public override object Create(ActorInitializer init) => new InfantryStates(init, this);

		public override void RulesetLoaded(Ruleset rules, ActorInfo ai)
		{
			base.RulesetLoaded(rules, ai);
		}
	}

	public class InfantryStates : Turreted, IObservesVariables, IRenderInfantrySequenceModifier, INotifyIdle, ISpeedModifier, IDamageModifier, ISync
	{
		public override IEnumerable<VariableObserver> GetVariableObservers()
		{
			foreach (var observer in base.GetVariableObservers())
				yield return observer;

			if (info.ProneCondition != null)
				yield return new VariableObserver(ProneConditionsChanged, info.ProneCondition.Variables);

			if (info.PanicCondition != null)
				yield return new VariableObserver(PanicConditionsChanged, info.PanicCondition.Variables);

			if (info.ActiveCondition != null)
				yield return new VariableObserver(ActiveConditionChanged, info.ActiveCondition.Variables);
		}

		[Sync]
		public bool IsProne { get; private set; }

		void ProneConditionsChanged(Actor self, IReadOnlyDictionary<string, int> conditions)
		{
			if (IsProne != info.ProneCondition.Evaluate(conditions))
			{
				if (IsProne)
					ProneTraitDisabled(self);
				else
					ProneTraitEnabled(self);
			}
		}

		int proneConditionToken = Actor.InvalidConditionToken;
		int panicConditionToken = Actor.InvalidConditionToken;

		[Sync]
		public bool IsPanicking { get; private set; }

		void PanicConditionsChanged(Actor self, IReadOnlyDictionary<string, int> conditions)
		{
			if (IsPanicking != info.PanicCondition.Evaluate(conditions))
			{
				if (IsPanicking)
					PanicTraitDisabled(self);
				else
					PanicTraitEnabled(self);
			}
		}

		[Sync]
		public bool IsActive { get; private set; }

		void ActiveConditionChanged(Actor self, IReadOnlyDictionary<string, int> conditions)
		{
			if (IsActive != info.ActiveCondition.Evaluate(conditions))
			{
				if (IsActive)
					ActiveTraitDisabled(self);
				else
					ActiveTraitEnabled(self);
			}
		}

		readonly InfantryStatesInfo info;

		readonly Actor self;

		readonly Mobile mobile;

		readonly Func<CPos, bool> avoidTerrainFilter;

		bool isPaused = false;

		bool IRenderInfantrySequenceModifier.IsModifyingSequence => !isPaused && (IsProne || IsActive || IsPanicking);
		string IRenderInfantrySequenceModifier.SequencePrefix =>
			IsPanicking ? info.PanicSequencePrefix :
			IsActive ? info.ActiveSequencePrefix :
			IsProne ? info.ProneSequencePrefix : "";

		public InfantryStates(ActorInitializer init, InfantryStatesInfo info)
			: base(init, info)
		{
			self = init.Self;
			this.info = info;

			mobile = init.Self.Trait<Mobile>();

			if (info.AvoidTerrainTypes.Count > 0)
				avoidTerrainFilter = c => info.AvoidTerrainTypes.Contains(init.Self.World.Map.GetTerrainInfo(c).Type);
		}

		public override bool HasAchievedDesiredFacing => true; // Used for what?

		protected override void Tick(Actor self)
		{
			base.Tick(self);
		}

		void INotifyIdle.TickIdle(Actor self)
		{
			if (!IsPanicking)
				return;

			// Note: This is just a modified copy of Mobile.Nudge
			var cell = mobile.GetAdjacentCell(self.Location, avoidTerrainFilter);
			if (cell != null)
				self.QueueActivity(false, mobile.MoveTo(cell.Value, 0));
		}

		int ISpeedModifier.GetSpeedModifier()
		{
			if (IsPanicking)
			{
				return info.PanicSpeedModifier;
			}

			if (IsProne)
				return info.ProneSpeedModifier;

			return 100;
		}

		int IDamageModifier.GetDamageModifier(Actor attacker, Damage damage)
		{
			if (!IsProne)
				return 100;

			if (damage == null || damage.DamageTypes.IsEmpty)
				return 100;

			var modifierPercentages = info.ProneDamageModifiers.Where(x => damage.DamageTypes.Contains(x.Key)).Select(x => x.Value);
			return Util.ApplyPercentageModifiers(100, modifierPercentages);
		}

		protected void ProneTraitEnabled(Actor self)
		{
			IsProne = true;

			if (proneConditionToken == Actor.InvalidConditionToken)
				proneConditionToken = self.GrantCondition(info.ProneGrantsCondition);

			localOffset = info.ProneOffset;
		}

		protected void ProneTraitDisabled(Actor self)
		{
			IsProne = false;

			if (proneConditionToken != Actor.InvalidConditionToken)
				proneConditionToken = self.RevokeCondition(proneConditionToken);

			localOffset = WVec.Zero;
		}

		protected void PanicTraitEnabled(Actor _)
		{
			self.CancelActivity();

			IsPanicking = true;

			if (panicConditionToken == Actor.InvalidConditionToken)
				panicConditionToken = self.GrantCondition(info.PanicGrantsCondition);
		}

		protected void PanicTraitDisabled(Actor _)
		{
			IsPanicking = false;

			if (panicConditionToken != Actor.InvalidConditionToken)
				panicConditionToken = self.RevokeCondition(panicConditionToken);
		}

		protected void ActiveTraitEnabled(Actor _)
		{
			IsActive = true;
		}

		protected void ActiveTraitDisabled(Actor _)
		{
			IsActive = false;
		}

		protected override void TraitResumed(Actor _)
		{
			isPaused = false;
		}

		protected override void TraitPaused(Actor _)
		{
			isPaused = true;
		}
	}
}
