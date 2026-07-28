#region Copyright & License Information
/*
 * WW3MOD territory-overlay stripe-intensity test — influence stack, Stage C (v2).
 *
 * Pins the pure staleness→stripe-opacity ramp the player-facing territory overlay draws:
 * fresh = no stripe, linear ramp minAlpha→maxAlpha over the staleness window, and a hard cap
 * (including the never-verified int.MaxValue input) with no overflow.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class TerritoryStripeMathTest
	{
		// Mirrors the TerritoryOverlayInfo defaults: (StripeMinAlpha, StripeMaxAlpha) with the
		// ControlFieldInfo StalenessWindow default.
		const int Window = 500;
		const int MinAlpha = 35;
		const int MaxAlpha = 150;

		[Test]
		public void FreshCellHasNoStripe()
		{
			Assert.Multiple(() =>
			{
				// Just-observed (0 ticks stale) and any non-positive input → clean, no stripe.
				Assert.That(TerritoryStripeMath.StripeAlpha(0, Window, MinAlpha, MaxAlpha), Is.EqualTo(0));
				Assert.That(TerritoryStripeMath.StripeAlpha(-5, Window, MinAlpha, MaxAlpha), Is.EqualTo(0));
			});
		}

		[Test]
		public void RampIsLinearBetweenFloorAndCeiling()
		{
			Assert.Multiple(() =>
			{
				// One tick stale: the floor kicks in immediately (barely-stale still shows a faint stripe).
				Assert.That(TerritoryStripeMath.StripeAlpha(1, Window, MinAlpha, MaxAlpha),
					Is.EqualTo(MinAlpha + (MaxAlpha - MinAlpha) * 1 / Window));

				// Half the window → halfway up the ramp.
				Assert.That(TerritoryStripeMath.StripeAlpha(Window / 2, Window, MinAlpha, MaxAlpha),
					Is.EqualTo(MinAlpha + (MaxAlpha - MinAlpha) / 2));

				// Monotonic non-decreasing across the window.
				var prev = -1;
				for (var t = 0; t <= Window; t += 50)
				{
					var a = TerritoryStripeMath.StripeAlpha(t, Window, MinAlpha, MaxAlpha);
					Assert.That(a, Is.GreaterThanOrEqualTo(prev), $"non-decreasing at t={t}");
					prev = a;
				}
			});
		}

		[Test]
		public void CapsAtWindowAndNeverVerified()
		{
			Assert.Multiple(() =>
			{
				// At the window edge → exactly MaxAlpha.
				Assert.That(TerritoryStripeMath.StripeAlpha(Window, Window, MinAlpha, MaxAlpha), Is.EqualTo(MaxAlpha));

				// Past the window → still capped at MaxAlpha (does not keep climbing).
				Assert.That(TerritoryStripeMath.StripeAlpha(Window * 4, Window, MinAlpha, MaxAlpha), Is.EqualTo(MaxAlpha));

				// Never-verified sentinel (int.MaxValue) is the maximally-stale reading — caps cleanly,
				// no overflow, because the clamp happens before the multiply.
				Assert.That(TerritoryStripeMath.StripeAlpha(int.MaxValue, Window, MinAlpha, MaxAlpha), Is.EqualTo(MaxAlpha));
			});
		}

		[Test]
		public void DegenerateWindowGivesMax()
		{
			// A non-positive window can't define a ramp — any staleness reads fully stale.
			Assert.That(TerritoryStripeMath.StripeAlpha(10, 0, MinAlpha, MaxAlpha), Is.EqualTo(MaxAlpha));
		}
	}
}
