#region Copyright & License Information
/*
 * WW3MOD AmmoEvacMath tests — @experimental out-of-ammo disposition (Wave A).
 *
 * Pure-logic pins for the rearm-or-evacuate judgement the bot applies to its own dry combat vehicles. The engine's
 * unit-side fallback is flag-only when no resupplier exists (AmmoPool.cs:313-320), so this decision is what stops
 * an empty vehicle standing at the front as a free kill.
 *
 * The load-bearing property pinned here is TOTALITY: a dry, orderable unit ALWAYS receives an actionable
 * disposition — never None. That is what makes "no hold-and-recheck loop" safe: there is no state in which the
 * sweep looks at a dry vehicle and decides to leave it parked.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class AmmoEvacMathTest
	{
		// ---------- WithinSeekBudget ----------

		[Test]
		public void WithinSeekBudget_ZeroOrNegativeIsUnlimited()
		{
			Assert.Multiple(() =>
			{
				Assert.That(AmmoEvacMath.WithinSeekBudget(9999, 0), Is.True,
					"budget <= 0 ⇒ unlimited (the legacy AmmoPool.AutoRearm reading: any source is worth the drive)");
				Assert.That(AmmoEvacMath.WithinSeekBudget(9999, -1), Is.True);
			});
		}

		[Test]
		public void WithinSeekBudget_Boundary()
		{
			Assert.Multiple(() =>
			{
				Assert.That(AmmoEvacMath.WithinSeekBudget(39, 40), Is.True, "inside the budget ⇒ drive");
				Assert.That(AmmoEvacMath.WithinSeekBudget(40, 40), Is.True, "AT the budget ⇒ still drive (inclusive)");
				Assert.That(AmmoEvacMath.WithinSeekBudget(41, 40), Is.False, "past the budget ⇒ not worth the drive");
			});
		}

		// ---------- Decide ----------

		[Test]
		public void Decide_NotOutOfAmmoIsNone()
		{
			// A unit with rounds left is none of the sweep's business, whatever the source situation is.
			Assert.Multiple(() =>
			{
				Assert.That(AmmoEvacMath.Decide(false, true, true, 5, 40), Is.EqualTo(AmmoEvacAction.None));
				Assert.That(AmmoEvacMath.Decide(false, true, false, 0, 40), Is.EqualTo(AmmoEvacAction.None),
					"still armed ⇒ never evacuated merely because no depot exists");
			});
		}

		[Test]
		public void Decide_ImmobileIsNone()
		{
			// An immobile unit can reach neither a host nor the map edge, so neither action is issuable; ordering
			// one would only cancel whatever it is doing.
			Assert.Multiple(() =>
			{
				Assert.That(AmmoEvacMath.Decide(true, false, true, 1, 40), Is.EqualTo(AmmoEvacAction.None));
				Assert.That(AmmoEvacMath.Decide(true, false, false, 0, 40), Is.EqualTo(AmmoEvacAction.None),
					"an immobile dry unit is NOT evacuated — it cannot walk to the edge");
			});
		}

		[Test]
		public void Decide_ReachableSourceSeeksRearm()
		{
			Assert.That(AmmoEvacMath.Decide(true, true, true, 12, 40), Is.EqualTo(AmmoEvacAction.SeekRearm),
				"a host inside the seek budget is worth driving to — rearming beats scrapping the hull");
		}

		[Test]
		public void Decide_NoSourceEvacuates()
		{
			Assert.That(AmmoEvacMath.Decide(true, true, false, 0, 40), Is.EqualTo(AmmoEvacAction.Evacuate),
				"no host at all ⇒ TERMINAL evac (the flag-only engine branch this lever replaces)");
		}

		[Test]
		public void Decide_SourceBeyondBudgetEvacuates()
		{
			// The distinction the seek budget buys: a host EXISTS, but is so far that the refund is worth more than
			// a dry hull crawling across the map to it.
			Assert.Multiple(() =>
			{
				Assert.That(AmmoEvacMath.Decide(true, true, true, 41, 40), Is.EqualTo(AmmoEvacAction.Evacuate));
				Assert.That(AmmoEvacMath.Decide(true, true, true, 40, 40), Is.EqualTo(AmmoEvacAction.SeekRearm),
					"exactly at the budget still rearms");
			});
		}

		[Test]
		public void Decide_UnlimitedBudgetAlwaysSeeksAnExistingSource()
		{
			Assert.That(AmmoEvacMath.Decide(true, true, true, 500, 0), Is.EqualTo(AmmoEvacAction.SeekRearm),
				"budget 0 = unlimited ⇒ reproduces the legacy 'drive to the closest host at any range' behaviour");
		}

		[Test]
		public void Decide_DryOrderableUnitIsNeverLeftParked()
		{
			// TOTALITY — the property that makes the no-hold-and-recheck design safe. Over every combination of
			// source presence and distance, a dry MOBILE unit always gets an actionable disposition.
			foreach (var sourceExists in new[] { true, false })
			{
				foreach (var distance in new[] { 0, 1, 40, 41, 9999 })
				{
					foreach (var budget in new[] { 0, 40 })
					{
						var action = AmmoEvacMath.Decide(true, true, sourceExists, distance, budget);
						Assert.That(action, Is.Not.EqualTo(AmmoEvacAction.None),
							$"dry+mobile must always act (source={sourceExists} dist={distance} budget={budget})");
					}
				}
			}
		}

		// ---------- EvacRefund ----------

		[Test]
		public void EvacRefund_ScalesByHealthFraction()
		{
			// Mirrors RotateToEdge.cs:275-280 / DOCS/reference/economy.md: sellValue x HP/MaxHP, integer-truncating.
			Assert.Multiple(() =>
			{
				Assert.That(AmmoEvacMath.EvacRefund(1000, 1000, 1000), Is.EqualTo(1000), "undamaged ⇒ full value");
				Assert.That(AmmoEvacMath.EvacRefund(1000, 500, 1000), Is.EqualTo(500), "half health ⇒ half value");
				Assert.That(AmmoEvacMath.EvacRefund(1000, 1, 3), Is.EqualTo(333), "truncates, never rounds up");
				Assert.That(AmmoEvacMath.EvacRefund(1000, 0, 1000), Is.EqualTo(0), "a dead hull refunds nothing");
			});
		}

		[Test]
		public void EvacRefund_DegenerateInputs()
		{
			Assert.Multiple(() =>
			{
				Assert.That(AmmoEvacMath.EvacRefund(0, 500, 1000), Is.EqualTo(0), "no sell value ⇒ no refund");
				Assert.That(AmmoEvacMath.EvacRefund(-5, 500, 1000), Is.EqualTo(0), "never a negative refund");
				Assert.That(AmmoEvacMath.EvacRefund(1000, 500, 0), Is.EqualTo(1000),
					"maxHp <= 0 reads as full health (the engine's health == null fallback)");
				Assert.That(AmmoEvacMath.EvacRefund(1000, -10, 1000), Is.EqualTo(0), "negative HP clamps to 0");
				Assert.That(AmmoEvacMath.EvacRefund(1000, 5000, 1000), Is.EqualTo(1000),
					"over-max HP clamps to full — never refunds more than the sell value");
			});
		}

		[Test]
		public void EvacRefund_LargeValuesDoNotOverflow()
		{
			// The long widening in the implementation: sellValue x hp would overflow int32 here.
			Assert.That(AmmoEvacMath.EvacRefund(2_000_000, 1_000_000, 2_000_000), Is.EqualTo(1_000_000));
		}
	}
}
