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

		[Desc("How far to look for patients. This is the NOTICE radius, not the heal range —",
			"the healer still has to walk into weapon range to treat anyone. 0 = fall back to the",
			"max heal weapon range (the pre-SearchRange behaviour, i.e. only notice what is already",
			"in range).")]
		public readonly WDist SearchRange = WDist.Zero;

		[Desc("Ignore patients above this health percentage — light scratches are not worth a house call.",
			"100 = treat any damage at all. Gates ACQUISITION only: once a patient is picked up he is",
			"treated to full, so tuning this down does not leave half-healed men lying around.")]
		public readonly int MaxPatientHealthPercent = 100;

		[Desc("Ticks a patient stays benched after the healer failed to reach him.")]
		public readonly int AbandonCooldown = 250;

		public override object Create(ActorInitializer init) { return new HealerAutoTarget(init.Self, this); }
	}

	public class HealerAutoTarget : IOverrideAutoTarget, ITick, INotifyCreated, INotifyActorDisposing
	{
		readonly HealerAutoTargetInfo info;
		readonly BitSet<TargetableType> validTargetTypes;
		HealerClaimLayer claimLayer;
		AttackBase[] attackBases;
		Actor currentTarget;
		Actor abandoned;
		int abandonedTicks;
		int scanTick;

		/// <summary>The patient this healer has claimed, whether or not it is close enough to treat yet.
		/// <see cref="AutoFollowAlly"/> reads this to walk the healer over — the attack layer refuses to
		/// approach an auto-target on any stance below Hunt (Attack.cs, engagement stance gate), so
		/// closing the distance is the follow layer's job, not the attack layer's.</summary>
		public Actor CurrentPatient => currentTarget;

		/// <summary>How close the healer must get before it can actually treat <see cref="CurrentPatient"/>.</summary>
		public WDist HealRange => GetMaxHealRange();

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

			// This trait answers for the healer unconditionally — "nobody" is an answer, not an
			// abstention. Returning false hands the decision to AutoTarget's own scan, which is NOT
			// bounded by the heal weapon's one cell: it uses AutoTarget.ScanRadius, 25 cells on ^MEDI.
			// That scan applies none of the rules below, so it would pick up anyone merely `damaged`
			// clean across the field — overruling MaxPatientHealthPercent — and, because it re-issues an
			// attack every scan interval, cancel the follow move that was walking us to the patient this
			// trait actually chose.
			if (ArmamentsPaused())
			{
				// Suppressed, typically: he cannot treat anyone at all right now. Say so, and let go of
				// the case rather than holding a claim another healer could be acting on.
				if (currentTarget != null)
				{
					ReleaseClaim(self);
					currentTarget = null;
				}

				return true;
			}

			var patient = SelectPatient(self);

			// Only hand the attack layer a patient it can actually treat from where it stands. A patient
			// that has been noticed but not yet reached stays claimed and is walked to by AutoFollowAlly.
			// PITFALL: returning an out-of-range patient here does NOT make the healer approach it — on any
			// stance below Hunt the Attack activity reports UnableToAttack instead of closing (Attack.cs,
			// "Engagement stance movement restrictions"). Worse, AttackMoveActivity re-scans mid-march and
			// cancels its move child whenever a scan returns a target, so an unreachable patient would stall
			// marching healers every 10 ticks.
			if (patient != null && IsInHealRange(self, patient))
				target = Target.FromActor(patient);

			return true;
		}

		/// <summary>True when every enabled heal armament is paused — suppression, typically. The healer
		/// can still acquire and aim; he simply cannot fire, and Armament.CanFire declines in silence.</summary>
		bool ArmamentsPaused()
		{
			var found = false;
			foreach (var ab in attackBases)
			{
				if (ab.IsTraitDisabled)
					continue;

				// A paused AttackBase fires nothing whatever the state of its armaments — Attack.DoAttack
				// skips the whole trait. ^MEDI's is paused while garrisoned at a port.
				if (ab.IsTraitPaused)
				{
					found = true;
					continue;
				}

				foreach (var armament in ab.Armaments)
				{
					if (armament.IsTraitDisabled)
						continue;

					if (!armament.IsTraitPaused)
						return false;

					found = true;
				}
			}

			return found;
		}

		Actor SelectPatient(Actor self)
		{
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
							return critical;
						}
					}
				}

				return currentTarget;
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

				return null;
			}

			if (best != currentTarget)
			{
				ReleaseClaim(self);
				currentTarget = best;
				TryClaimTarget(self, best);
			}

			return best;
		}

		bool IsInHealRange(Actor self, Actor patient)
		{
			var healRange = GetMaxHealRange();
			return healRange > WDist.Zero
				&& (patient.CenterPosition - self.CenterPosition).HorizontalLengthSquared <= healRange.LengthSquared;
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

		WDist GetEffectiveSearchRange()
		{
			// The notice radius is configured outright. It used to be derived from the heal weapon's
			// range, which pinned it to a single cell and made every wider-radius feature here — the
			// critical-first scoring, the claim de-confliction, stabilize-and-switch — unreachable.
			return info.SearchRange > WDist.Zero ? info.SearchRange : GetMaxHealRange();
		}

		bool IsWorthTreating(Health health)
		{
			return health.HP < health.MaxHP
				&& health.HP * 100 / health.MaxHP <= info.MaxPatientHealthPercent;
		}

		Actor FindBestTarget(Actor self)
		{
			var maxRange = GetEffectiveSearchRange();

			if (maxRange == WDist.Zero)
				return null;

			Actor best = null;
			var bestScore = int.MaxValue;

			foreach (var a in self.World.FindActorsInCircle(self.CenterPosition, maxRange))
			{
				if (a == self || a == abandoned || a.IsDead || !a.IsInWorld)
					continue;

				if (!self.Owner.IsAlliedWith(a.Owner))
					continue;

				var targetTypes = a.GetEnabledTargetTypes();
				if (!targetTypes.Overlaps(validTargetTypes))
					continue;

				var health = a.TraitOrDefault<Health>();
				if (health == null || !IsWorthTreating(health))
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
			var maxRange = GetEffectiveSearchRange();
			if (maxRange == WDist.Zero)
				return null;

			Actor best = null;
			var bestHpPct = int.MaxValue;

			foreach (var a in self.World.FindActorsInCircle(self.CenterPosition, maxRange))
			{
				if (a == self || a == currentTarget || a == abandoned || a.IsDead || !a.IsInWorld)
					continue;

				if (!self.Owner.IsAlliedWith(a.Owner))
					continue;

				var targetTypes = a.GetEnabledTargetTypes();
				if (!targetTypes.Overlaps(validTargetTypes))
					continue;

				var health = a.TraitOrDefault<Health>();
				if (health == null || !IsWorthTreating(health))
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

		/// <summary>Give up on the current patient — called by the follow layer when it cannot path to him.
		/// Releases the claim so another healer can take the case, and benches this one for a while so the
		/// very next scan doesn't just pick him again.</summary>
		public void AbandonPatient(Actor self)
		{
			if (currentTarget == null)
				return;

			abandoned = currentTarget;
			abandonedTicks = info.AbandonCooldown;
			ReleaseClaim(self);
			currentTarget = null;
			scanTick = 0;
		}

		void ITick.Tick(Actor self)
		{
			if (scanTick > 0)
				--scanTick;

			if (abandonedTicks > 0 && --abandonedTicks == 0)
				abandoned = null;

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
