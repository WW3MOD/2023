#region Copyright & License Information
/*
 * Verifies the HP-fraction → stack-count mapping in
 * GrantStackingConditionOnHealthFraction. Pure-math test; no Actor / World.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Mods.Common.Test
{
	[TestFixture]
	public class GrantStackingConditionOnHealthFractionTest
	{
		// Vehicle / heli production config: fire ignites at 50% HP, peaks at 1% HP.
		const int Start = 50;
		const int End = 1;
		const int Max = 10;

		static int Stacks(int percent) =>
			GrantStackingConditionOnHealthFraction.CalculateStacks(percent, Start, End, Max);

		[Test]
		public void NoStacksAboveStart()
		{
			Assert.AreEqual(0, Stacks(100));
			Assert.AreEqual(0, Stacks(75));
			Assert.AreEqual(0, Stacks(51));
		}

		[Test]
		public void OneStackAtStart()
		{
			// Hitting 50% HP exactly = first stack ignites.
			Assert.AreEqual(1, Stacks(50));
		}

		[Test]
		public void OneStackJustBelowStart()
		{
			// 49% should still be the very-first-stack zone. 45% is in the
			// transition to stack 2 — accept either, the ramp width is small.
			Assert.AreEqual(1, Stacks(49));
			Assert.That(Stacks(45), Is.InRange(1, 2));
		}

		[Test]
		public void MidRangeRamp()
		{
			// At 25% HP the ramp should be near the middle of 1..10.
			var mid = Stacks(25);
			Assert.That(mid, Is.GreaterThanOrEqualTo(4).And.LessThanOrEqualTo(6),
				$"Expected 4-6 stacks at 25% HP, got {mid}");
		}

		[Test]
		public void AlmostMaxStacksLowHP()
		{
			Assert.That(Stacks(10), Is.GreaterThanOrEqualTo(7));
			Assert.That(Stacks(5), Is.GreaterThanOrEqualTo(8));
		}

		[Test]
		public void MaxStacksAtOrBelowEnd()
		{
			Assert.AreEqual(Max, Stacks(1));
			Assert.AreEqual(Max, Stacks(0));
		}

		[Test]
		public void StackCountIsMonotonicallyNonDecreasing()
		{
			// Walk from 100% HP to 0% HP. Stack count must never decrease.
			var prev = 0;
			for (var p = 100; p >= 0; p--)
			{
				var s = Stacks(p);
				Assert.That(s, Is.GreaterThanOrEqualTo(prev),
					$"Stacks dropped at {p}% HP: was {prev}, now {s}");
				prev = s;
			}
		}

		[Test]
		public void HitsEveryStackBetweenStartAndEnd()
		{
			// As HP slides from 100% → 0% every value 0..MaxStacks should
			// appear at some point. No missing levels (no skipped stacks).
			var seen = new bool[Max + 1];
			for (var p = 100; p >= 0; p--)
				seen[Stacks(p)] = true;

			for (var s = 0; s <= Max; s++)
				Assert.IsTrue(seen[s], $"Stack count {s} never appeared during 100% → 0% sweep");
		}

		[Test]
		public void DegenerateMaxStacks()
		{
			Assert.AreEqual(0, GrantStackingConditionOnHealthFraction.CalculateStacks(50, 50, 1, 0));
		}

		[Test]
		public void DegenerateZeroSpan()
		{
			// StartFraction == EndFraction: span clamps to 1, percent <= start
			// jumps straight to MaxStacks.
			Assert.AreEqual(Max, GrantStackingConditionOnHealthFraction.CalculateStacks(25, 25, 25, Max));
			Assert.AreEqual(0, GrantStackingConditionOnHealthFraction.CalculateStacks(26, 25, 25, Max));
		}
	}
}
