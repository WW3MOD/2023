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

using System.Linq;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Smart healer targeting: coordinates with HealerClaimLayer to avoid pile-ups, prioritizes critical patients.")]
	public class HealerAutoTargetInfo : TraitInfo
	{
		[Desc("HP percentage threshold. Heal to this level before switching to next critical patient.")]
		public readonly int StabilizeThreshold = 50;

		[Desc("Use HealerClaimLayer to prevent multiple healers targeting the same patient.")]
		public readonly bool ClaimTargets = true;

		[Desc("Ticks between target re-evaluation.")]
		public readonly int ScanInterval = 8;

		[Desc("Target types to scan for (must match Targetable trait on patients).")]
		public readonly BitSet<TargetableType> ValidTargetTypes = default;

		public override object Create(ActorInitializer init) { return new HealerAutoTarget(init.Self, this); }
	}

	public class HealerAutoTarget : IOverrideAutoTarget, ITick, INotifyCreated, INotifyActorDisposing
	{
		readonly HealerAutoTargetInfo info;
		readonly BitSet<TargetableType> validTargetTypes;
		HealerClaimLayer claimLayer;
		AttackBase[] attackBases;
		Actor currentTarget;
		int scanTick;

		public HealerAutoTarget(Actor self, HealerAutoTargetInfo info)
		{
			this.info = info;
			validTargetTypes = info.ValidTargetTypes;
		}

		void INotifyCreated.Created(Actor self)
		{
			attackBases = self.TraitsImplementing<AttackBase>().ToArray();

			// Stagger so multiple medics don't all rescan on the same tick.
			scanTick = self.World.SharedRandom.Next(0, info.ScanInterval);
		}

		void EnsureClaimLayer(Actor self)
		{
			if (claimLayer == null && info.ClaimTargets)
				claimLayer = self.World.WorldActor.TraitOrDefault<HealerClaimLayer>();
		}

		bool IOverrideAutoTarget.TryGetAutoTargetOverride(Actor self, out Target target)
		{
			target = Target.Invalid;
			EnsureClaimLayer(self);

			if (scanTick > 0 && IsValidTarget(self, currentTarget))
			{
				// Check stabilize-and-switch: if current target is above threshold,
				// see if there's a critical unclaimed patient nearby
				if (info.StabilizeThreshold > 0 && currentTarget != null)
				{
					var currentHealth = currentTarget.Trait<Health>();
					var currentHpPct = currentHealth.HP * 100 / currentHealth.MaxHP;
					if (currentHpPct >= info.StabilizeThreshold)
					{
						var critical = FindCriticalUnclaimed(self);
						if (critical != null)
						{
							ReleaseClaim(self);
							currentTarget = critical;
							TryClaimTarget(self, critical);
							target = Target.FromActor(critical);
							return true;
						}
					}
				}

				target = Target.FromActor(currentTarget);
				return true;
			}

			// Full rescan
			scanTick = info.ScanInterval;

			var best = FindBestTarget(self);
			if (best == null)
			{
				// No heal targets — release claim and let normal AutoTarget handle combat
				if (currentTarget != null)
				{
					ReleaseClaim(self);
					currentTarget = null;
				}

				return false;
			}

			if (best != currentTarget)
			{
				ReleaseClaim(self);
				currentTarget = best;
				TryClaimTarget(self, best);
			}

			target = Target.FromActor(best);
			return true;
		}

		WDist GetMaxHealRange()
		{
			var maxRange = WDist.Zero;
			foreach (var ab in attackBases)
			{
				if (ab.IsTraitDisabled)
					continue;
				var r = ab.GetMaximumRange();
				if (r > maxRange)
					maxRange = r;
			}

			return maxRange;
		}

		Actor FindBestTarget(Actor self)
		{
			var maxRange = GetMaxHealRange();

			if (maxRange == WDist.Zero)
				return null;

			Actor best = null;
			var bestScore = int.MaxValue;

			foreach (var a in self.World.FindActorsInCircle(self.CenterPosition, maxRange))
			{
				if (a == self || a.IsDead || !a.IsInWorld)
					continue;

				if (!self.Owner.IsAlliedWith(a.Owner))
					continue;

				var targetTypes = a.GetEnabledTargetTypes();
				if (!targetTypes.Overlaps(validTargetTypes))
					continue;

				var health = a.TraitOrDefault<Health>();
				if (health == null || health.HP >= health.MaxHP)
					continue;

				// Skip if claimed by another healer
				if (claimLayer != null && claimLayer.IsClaimed(a, self))
					continue;

				var hpPct = health.HP * 100 / health.MaxHP;
				var score = hpPct;

				// Critical targets get massive priority bonus
				if (info.StabilizeThreshold > 0 && hpPct < info.StabilizeThreshold)
					score -= 10000;

				// Slight distance tiebreaker (1 point per 10 cells)
				var dist = (self.CenterPosition - a.CenterPosition).Length;
				score += dist / 10240;

				if (score < bestScore)
				{
					bestScore = score;
					best = a;
				}
			}

			return best;
		}

		Actor FindCriticalUnclaimed(Actor self)
		{
			var maxRange = GetMaxHealRange();
			if (maxRange == WDist.Zero)
				return null;

			Actor best = null;
			var bestHpPct = int.MaxValue;

			foreach (var a in self.World.FindActorsInCircle(self.CenterPosition, maxRange))
			{
				if (a == self || a == currentTarget || a.IsDead || !a.IsInWorld)
					continue;

				if (!self.Owner.IsAlliedWith(a.Owner))
					continue;

				var targetTypes = a.GetEnabledTargetTypes();
				if (!targetTypes.Overlaps(validTargetTypes))
					continue;

				var health = a.TraitOrDefault<Health>();
				if (health == null || health.HP >= health.MaxHP)
					continue;

				var hpPct = health.HP * 100 / health.MaxHP;
				if (hpPct >= info.StabilizeThreshold)
					continue;

				if (claimLayer != null && claimLayer.IsClaimed(a, self))
					continue;

				if (hpPct < bestHpPct)
				{
					bestHpPct = hpPct;
					best = a;
				}
			}

			return best;
		}

		bool IsValidTarget(Actor self, Actor a)
		{
			if (a == null || a.IsDead || a.Disposed || !a.IsInWorld)
				return false;

			var health = a.TraitOrDefault<Health>();
			if (health == null || health.HP >= health.MaxHP)
				return false;

			// Check target types are still valid (e.g. damaged condition still active)
			var targetTypes = a.GetEnabledTargetTypes();
			if (!targetTypes.Overlaps(validTargetTypes))
				return false;

			return true;
		}

		void TryClaimTarget(Actor self, Actor patient)
		{
			claimLayer?.TryClaim(self, patient);
		}

		void ReleaseClaim(Actor self)
		{
			claimLayer?.RemoveClaim(self);
		}

		void ITick.Tick(Actor self)
		{
			if (scanTick > 0)
				--scanTick;

			// Clean up stale target
			if (currentTarget != null && !IsValidTarget(self, currentTarget))
			{
				ReleaseClaim(self);
				currentTarget = null;
			}
		}

		void INotifyActorDisposing.Disposing(Actor self)
		{
			ReleaseClaim(self);
		}
	}
}
