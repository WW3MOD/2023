#region Copyright & License Information
/*
 * WW3MOD combined-arms rendezvous test.
 *
 * Pins RendezvousMath — the channel that makes a mounted transport deliver its infantry to the cell the
 * OFFENSIVE RESERVE is mustering on, instead of to a destination it invented for itself. Before this existed
 * the two halves of a combined-arms body computed cells with different arithmetic (control-field steepest
 * descent vs a 50% linear lerp toward the top POI) and could not read each other, so a ferry that worked
 * perfectly still dropped its passengers away from the armour meant to protect them.
 *
 * Two properties carry the weight here:
 *   - Rendezvous_CollapsesTheGapThatLerpLeaves — the headline: the delivered cell IS the armour's cell.
 *   - RunawayAnchor_IsRejected... — the bounded-divergence escape, so a forward-walking anchor can never
 *     drag a loaded transport into enemy ground.
 *
 * Pure integer math, deterministic, zero RNG.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class RendezvousMathTest
	{
		// A representative opening geometry: own SR in the corner, the armour's control-field staging anchor
		// out along one bearing, and the transport's legacy 50%-lerp cell along a different one. These are the
		// two cells that historically diverged.
		const int SrX = 10;
		const int SrY = 10;
		const int AnchorX = 22;
		const int AnchorY = 14;
		const int LerpX = 16;
		const int LerpY = 24;
		const int Margin = 4;

		static (int X, int Y) Resolve(bool enabled, bool hasAnchor, int anchorX = AnchorX, int anchorY = AnchorY, int margin = Margin)
		{
			RendezvousMath.ResolveDropOff(enabled, hasAnchor, SrX, SrY, anchorX, anchorY, LerpX, LerpY, margin,
				out var x, out var y);
			return (x, y);
		}

		// ---------- Chebyshev distance ----------

		[Test]
		public void CellDistance_IsChebyshev()
		{
			Assert.That(RendezvousMath.CellDistance(0, 0, 4, 4), Is.EqualTo(4));
			Assert.That(RendezvousMath.CellDistance(3, 1, 3, 9), Is.EqualTo(8));
			Assert.That(RendezvousMath.CellDistance(5, 5, 5, 5), Is.EqualTo(0));

			// Diagonal costs the same as the longer axis — a king move, matching ground locomotion.
			Assert.That(RendezvousMath.CellDistance(0, 0, 6, 2), Is.EqualTo(6));
		}

		// ---------- the headline ----------

		[Test]
		public void Rendezvous_CollapsesTheGapThatLerpLeaves()
		{
			// The defect, stated as an assertion: the two independently-computed cells are NOT the same place.
			// If this ever stops holding the rest of the fixture is measuring nothing, so assert it explicitly
			// rather than assuming it.
			Assert.That(RendezvousMath.CellDistance(AnchorX, AnchorY, LerpX, LerpY), Is.GreaterThan(0),
				"fixture geometry is degenerate — the lerp and the anchor must differ for this to test anything");

			var off = Resolve(enabled: false, hasAnchor: true);
			Assert.That(RendezvousMath.CellDistance(off.X, off.Y, AnchorX, AnchorY), Is.GreaterThan(0),
				"disabled: the transport must still deliver to its own lerp cell, away from the armour");

			var on = Resolve(enabled: true, hasAnchor: true);
			Assert.That(RendezvousMath.CellDistance(on.X, on.Y, AnchorX, AnchorY), Is.EqualTo(0),
				"enabled: the transport must deliver exactly where the armour is mustering");
		}

		// ---------- baseline preservation (the frozen profile) ----------

		[Test]
		public void Disabled_IsIdentityOnTheFallback()
		{
			var cell = Resolve(enabled: false, hasAnchor: true);
			Assert.That(cell.X, Is.EqualTo(LerpX));
			Assert.That(cell.Y, Is.EqualTo(LerpY));
		}

		[Test]
		public void NoPublishedAnchor_FallsBack()
		{
			// The offensive module publishes nothing until its own staging resolves (flat control field, no
			// believed enemy). The transport must not stall waiting for it — it keeps its legacy destination.
			var cell = Resolve(enabled: true, hasAnchor: false);
			Assert.That(cell.X, Is.EqualTo(LerpX));
			Assert.That(cell.Y, Is.EqualTo(LerpY));
		}

		// ---------- bounded divergence: the escape ----------

		[Test]
		public void RunawayAnchor_IsRejectedSoALoadedTransportIsNotWalkedDeep()
		{
			// The staging anchor advances with the believed front. An anchor that has walked far past the
			// transport's own fallback must be refused, or a loaded carrier follows it into enemy ground —
			// the one outcome the standing constraint on this pair forbids.
			var fallbackReach = RendezvousMath.CellDistance(SrX, SrY, LerpX, LerpY);
			var runaway = SrX + fallbackReach + Margin + 1;

			var cell = Resolve(enabled: true, hasAnchor: true, anchorX: runaway, anchorY: SrY);
			Assert.That(cell.X, Is.EqualTo(LerpX), "a runaway anchor must fall back, not be chased");
			Assert.That(cell.Y, Is.EqualTo(LerpY));
		}

		[Test]
		public void AnchorExactlyAtTheMargin_IsAccepted()
		{
			// Boundary: <= margin is in. Pins the comparison as inclusive so a later refactor can't quietly
			// turn it into <, which would reject the common case of an anchor a shade beyond the lerp.
			var fallbackReach = RendezvousMath.CellDistance(SrX, SrY, LerpX, LerpY);
			var edge = SrX + fallbackReach + Margin;

			var cell = Resolve(enabled: true, hasAnchor: true, anchorX: edge, anchorY: SrY);
			Assert.That(cell.X, Is.EqualTo(edge));
			Assert.That(cell.Y, Is.EqualTo(SrY));
		}

		[Test]
		public void AnchorNearerThanTheFallback_IsAlwaysAccepted()
		{
			// Pulling the drop-off BACK toward our own SR is unconditionally safe, so it must pass at any
			// margin — including a zero margin, where the forward direction is fully closed.
			var cell = Resolve(enabled: true, hasAnchor: true, anchorX: SrX + 1, anchorY: SrY + 1, margin: 0);
			Assert.That(cell.X, Is.EqualTo(SrX + 1));
			Assert.That(cell.Y, Is.EqualTo(SrY + 1));
		}

		[Test]
		public void NegativeMargin_ClampsToZeroRatherThanInverting()
		{
			// A mis-set config must fail conservative. With the margin clamped to 0 an anchor level with the
			// fallback still passes, and anything beyond it does not.
			var fallbackReach = RendezvousMath.CellDistance(SrX, SrY, LerpX, LerpY);

			var level = Resolve(enabled: true, hasAnchor: true, anchorX: SrX + fallbackReach, anchorY: SrY, margin: -50);
			Assert.That(level.X, Is.EqualTo(SrX + fallbackReach), "an anchor no further out than the fallback stays acceptable");

			var beyond = Resolve(enabled: true, hasAnchor: true, anchorX: SrX + fallbackReach + 1, anchorY: SrY, margin: -50);
			Assert.That(beyond.X, Is.EqualTo(LerpX), "negative margin must not widen the gate");
		}

		[Test]
		public void AcceptanceIgnoresBearing_OnlyReach()
		{
			// The gate is about how FAR the anchor is from our SR, not which way it lies. Two anchors at equal
			// reach on opposite bearings must resolve identically, so the rendezvous never prefers one flank.
			var fallbackReach = RendezvousMath.CellDistance(SrX, SrY, LerpX, LerpY);
			var reach = fallbackReach + Margin;

			Assert.That(RendezvousMath.AnchorAcceptable(SrX, SrY, SrX + reach, SrY, LerpX, LerpY, Margin), Is.True);
			Assert.That(RendezvousMath.AnchorAcceptable(SrX, SrY, SrX - reach, SrY, LerpX, LerpY, Margin), Is.True);
		}
	}
}
