#region Copyright & License Information
/*
 * WW3MOD target preemption — AutoTargetPriority band resolution.
 *
 * Pins the predicate that decides which priority band a unit assigns to a target. Target preemption
 * compares the incumbent's band against a candidate's and only switches on a STRICTLY higher one, so
 * if this resolver silently answers "no band" for everything the comparison degrades into
 * unconditional retargeting — all stickiness gone, for humans and both bot profiles.
 *
 * That is not hypothetical. The matcher used to lead with:
 *     if (!ati.OnlyTargets.Except(targetTypes).Any() || ...) continue;
 * OnlyTargets defaults to an EMPTY BitSet and nothing in mods/ ever sets it. An empty set Except
 * anything is empty, so .Any() was false, so !false was true, so EVERY priority entry was skipped for
 * EVERY target and the resolver always returned NoTargetPriorityBand. The autotest cannot see this —
 * the unit still shoots things — which is exactly why it is pinned here instead.
 *
 * Values mirror ^AutoTargetAAIFV in mods/ww3mod/rules/defaults.yaml (the Stryker SHORAD's set):
 * Helicopter 5, Aircraft 4, Vehicle 3 (InvalidTargets: Unarmored), Infantry 2.
 */
#endregion

using System.Collections.Generic;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class AutoTargetPriorityBandTest
	{
		static AutoTargetPriorityInfo Priority(string validTargets, int priority, string invalidTargets = null)
		{
			// The Info fields are readonly, which is what FieldLoader exists to populate.
			var info = new AutoTargetPriorityInfo();
			FieldLoader.LoadField(info, "ValidTargets", validTargets);
			FieldLoader.LoadField(info, "Priority", priority.ToString());
			if (invalidTargets != null)
				FieldLoader.LoadField(info, "InvalidTargets", invalidTargets);

			return info;
		}

		// ^AutoTargetAAIFV, in declaration order.
		static List<AutoTargetPriorityInfo> AaIfvPriorities()
		{
			return new List<AutoTargetPriorityInfo>
			{
				Priority("Helicopter", 5),
				Priority("Aircraft", 4),
				Priority("Vehicle", 3, "Unarmored"),
				Priority("Infantry", 2),
			};
		}

		static int Band(params string[] targetTypes)
		{
			return AutoTarget.ResolveTargetPriorityBand(AaIfvPriorities(), PlayerRelationship.Enemy,
				new BitSet<TargetableType>(targetTypes));
		}

		[Test]
		public void HelicopterResolvesToItsOwnBand()
		{
			// The reported bug: a SHORAD must rate a helicopter above the ground unit it is shooting.
			Assert.That(Band("Air", "Helicopter"), Is.EqualTo(5));
		}

		[Test]
		public void VehicleResolvesToItsOwnBand()
		{
			// A t90 is Ground, Vehicle, Heavy — band 3 for this priority set.
			Assert.That(Band("Ground", "Vehicle", "Heavy"), Is.EqualTo(3));
		}

		[Test]
		public void HelicopterOutranksVehicle()
		{
			// The comparison preemption actually performs. Guards against both bands collapsing to the
			// same value, which would make the switch never fire (or always fire).
			Assert.That(Band("Air", "Helicopter"), Is.GreaterThan(Band("Ground", "Vehicle", "Heavy")));
		}

		[Test]
		public void EveryRealTargetResolvesToSomeBand()
		{
			// The regression guard proper: the OnlyTargets bug made all of these NoTargetPriorityBand.
			Assert.That(Band("Air", "Helicopter"), Is.GreaterThan(AutoTarget.NoTargetPriorityBand));
			Assert.That(Band("Ground", "Vehicle", "Heavy"), Is.GreaterThan(AutoTarget.NoTargetPriorityBand));
			Assert.That(Band("Ground", "Infantry"), Is.GreaterThan(AutoTarget.NoTargetPriorityBand));
		}

		[Test]
		public void HighestMatchingBandWins()
		{
			// A helicopter that also reads as Aircraft takes the Helicopter band, not the first match.
			Assert.That(Band("Air", "Aircraft", "Helicopter"), Is.EqualTo(5));
		}

		[Test]
		public void InvalidTargetsOverrulesValidTargets()
		{
			// An Unarmored vehicle is excluded from the Vehicle band and falls through to no match.
			Assert.That(Band("Ground", "Vehicle", "Unarmored"), Is.EqualTo(AutoTarget.NoTargetPriorityBand));
		}

		[Test]
		public void UnmatchedTargetHasNoBand()
		{
			Assert.That(Band("Water"), Is.EqualTo(AutoTarget.NoTargetPriorityBand));
		}

		[Test]
		public void IncompatibleRelationshipHasNoBand()
		{
			var ally = AutoTarget.ResolveTargetPriorityBand(
				new[] { Priority("Helicopter", 5) },
				PlayerRelationship.Ally,
				new BitSet<TargetableType>("Air", "Helicopter"));

			// ValidRelationships defaults to Ally|Neutral|Enemy, so narrow it to prove the term is live.
			var info = new AutoTargetPriorityInfo();
			FieldLoader.LoadField(info, "ValidTargets", "Helicopter");
			FieldLoader.LoadField(info, "ValidRelationships", "Enemy");
			var enemyOnly = AutoTarget.ResolveTargetPriorityBand(
				new[] { info }, PlayerRelationship.Ally, new BitSet<TargetableType>("Air", "Helicopter"));

			Assert.That(ally, Is.EqualTo(5));
			Assert.That(enemyOnly, Is.EqualTo(AutoTarget.NoTargetPriorityBand));
		}
	}
}
