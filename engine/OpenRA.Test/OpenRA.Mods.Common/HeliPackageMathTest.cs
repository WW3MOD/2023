#region Copyright & License Information
/*
 * WW3MOD @experimental attack-heli package-size doctrine (Item B) pure-math pin.
 *
 * Pins HeliPackageMath.ShouldLaunchPartial, the decision that lets an attack-heli mission launch
 * BELOW the randomised preferred (pairing) size — down to MinAttackSquadSize — instead of benching
 * helis until a full pair forms. A single attack heli is a large investment, so it deploys rather
 * than idling; but the bot still PREFERS a pair, holding out for a second only when income is high
 * enough to afford massing one. Pure integer math; no world mounted; deterministic.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class HeliPackageMathTest
	{
		const int Preferred = 2;   // pairing target
		const int MinSize = 1;     // a lone heli may deploy
		const int IncomeThresh = 6000;

		[Test]
		public void SoloHeli_LowIncome_Launches()
		{
			// One heli ready, income below the pair-up threshold ⇒ commit the solo heli rather than bench it.
			Assert.That(
				HeliPackageMath.ShouldLaunchPartial(ready: 1, preferredSize: Preferred, minSize: MinSize,
					spendable: 1000, incomeThresh: IncomeThresh),
				Is.True);
		}

		[Test]
		public void SoloHeli_HighIncome_WaitsForPair()
		{
			// One heli ready but income is high ⇒ we can afford to mass, so hold out for a second (no launch).
			Assert.That(
				HeliPackageMath.ShouldLaunchPartial(ready: 1, preferredSize: Preferred, minSize: MinSize,
					spendable: 12000, incomeThresh: IncomeThresh),
				Is.False);
		}

		[Test]
		public void PairReady_HighIncome_Launches()
		{
			// A second heli already exists (ready == preferred) ⇒ the "wait for a pair" clause no longer bites,
			// so even at high income the package launches. (The caller takes the full-size branch here anyway;
			// this pins that ShouldLaunchPartial would not veto it.)
			Assert.That(
				HeliPackageMath.ShouldLaunchPartial(ready: 2, preferredSize: Preferred, minSize: MinSize,
					spendable: 12000, incomeThresh: IncomeThresh),
				Is.True);
		}

		[Test]
		public void BelowMinSize_NeverLaunches()
		{
			// Nothing ready (or below the configured floor) ⇒ never launch, regardless of income.
			Assert.Multiple(() =>
			{
				Assert.That(
					HeliPackageMath.ShouldLaunchPartial(ready: 0, preferredSize: Preferred, minSize: MinSize,
						spendable: 0, incomeThresh: IncomeThresh),
					Is.False);
				Assert.That(
					HeliPackageMath.ShouldLaunchPartial(ready: 1, preferredSize: Preferred, minSize: 2,
						spendable: 0, incomeThresh: IncomeThresh),
					Is.False, "min size 2 benches a lone heli");
			});
		}

		[Test]
		public void IncomeBoundaryIsInclusive()
		{
			// Exactly at the threshold counts as HIGH income ⇒ wait for the pair.
			Assert.That(
				HeliPackageMath.ShouldLaunchPartial(ready: 1, preferredSize: Preferred, minSize: MinSize,
					spendable: IncomeThresh, incomeThresh: IncomeThresh),
				Is.False);
		}

		[Test]
		public void Deterministic()
		{
			Assert.That(
				HeliPackageMath.ShouldLaunchPartial(1, Preferred, MinSize, 1000, IncomeThresh),
				Is.EqualTo(HeliPackageMath.ShouldLaunchPartial(1, Preferred, MinSize, 1000, IncomeThresh)));
		}
	}
}
