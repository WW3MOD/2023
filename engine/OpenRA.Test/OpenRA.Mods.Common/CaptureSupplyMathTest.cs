#region Copyright & License Information
/*
 * WW3MOD capture-supply guarantee (@experimental) — TECN floor + re-request tests.
 *
 * Pins the two decisions CaptureCoordinatorBotModule.MaintainTecnFloor turns into a production request, so the
 * S2 capture-supply fix (WORKSPACE/recon/260731) can't silently regress AND @stable byte-identity is proven:
 *   (1) EffectiveFloor — scaling OFF reproduces the static floor exactly (the frozen path); scaling ON is
 *       ~one capturer per neutral money POI, clamped to [staticFloor, cap].
 *   (2) ShouldRequestTecn — with the staleness knob OFF the predicate is EXACTLY the frozen
 *       `alive + pending < floor` gate; with it ON an undelivered pending request is re-issued once stale.
 * Pure integer decisions; no world mounted.
 */
#endregion

using OpenRA.Mods.Common.Traits;
using NUnit.Framework;

namespace OpenRA.Test
{
	[TestFixture]
	public class CaptureSupplyMathTest
	{
		// ---- EffectiveFloor ----

		[Test]
		public void EffectiveFloor_ScalingOff_ReturnsStaticFloor_ByteIdentity()
		{
			// The frozen path: scaling disabled ⇒ the static floor verbatim, regardless of POI count / cap.
			Assert.That(CaptureSupplyMath.EffectiveFloor(false, 1, 14, 5), Is.EqualTo(1));
			Assert.That(CaptureSupplyMath.EffectiveFloor(false, 3, 0, 5), Is.EqualTo(3));
			Assert.That(CaptureSupplyMath.EffectiveFloor(false, 1, 99, 0), Is.EqualTo(1));
		}

		[Test]
		public void EffectiveFloor_ScalingOn_OneCapturerPerNeutralPoi_WithinClamp()
		{
			// Between the static floor and the cap ⇒ exactly the POI count (~1 capturer per free oilb).
			Assert.That(CaptureSupplyMath.EffectiveFloor(true, 1, 3, 5), Is.EqualTo(3));
			Assert.That(CaptureSupplyMath.EffectiveFloor(true, 1, 4, 5), Is.EqualTo(4));
		}

		[Test]
		public void EffectiveFloor_ScalingOn_NeverBelowStaticFloor()
		{
			// Few or zero reachable POIs still keep at least today's floor (never LESS aggressive than frozen).
			Assert.That(CaptureSupplyMath.EffectiveFloor(true, 1, 0, 5), Is.EqualTo(1));
			Assert.That(CaptureSupplyMath.EffectiveFloor(true, 2, 1, 5), Is.EqualTo(2));
		}

		[Test]
		public void EffectiveFloor_ScalingOn_CappedAtMax()
		{
			// A POI-dense map cannot balloon the request pool past the cap.
			Assert.That(CaptureSupplyMath.EffectiveFloor(true, 1, 14, 5), Is.EqualTo(5));
			Assert.That(CaptureSupplyMath.EffectiveFloor(true, 1, 6, 5), Is.EqualTo(5));
		}

		// ---- ShouldRequestTecn: frozen behaviour (staleTicks <= 0) ----

		[Test]
		public void ShouldRequestTecn_StalenessOff_IsExactlyTheFrozenGate()
		{
			// Frozen path requests iff alive + pending < floor, for every (alive, pending) around the boundary.
			// tick/lastRequestTick are irrelevant when the staleness knob is off.
			for (var alive = 0; alive <= 4; alive++)
			{
				for (var pending = 0; pending <= 4; pending++)
				{
					const int Floor = 3;
					var expected = alive + pending < Floor;
					Assert.That(CaptureSupplyMath.ShouldRequestTecn(Floor, alive, pending, 5000, 10, 0),
						Is.EqualTo(expected),
						$"frozen gate mismatch at alive={alive} pending={pending}");
				}
			}
		}

		[Test]
		public void ShouldRequestTecn_StalenessOff_NeverReissuesAPendingRequest()
		{
			// Floor met only by a pending request ⇒ frozen path never re-issues, no matter how old the request.
			Assert.That(CaptureSupplyMath.ShouldRequestTecn(1, 0, 1, 99999, 0, 0), Is.False);
		}

		// ---- ShouldRequestTecn: un-deadlock (staleTicks > 0) ----

		[Test]
		public void ShouldRequestTecn_UnderFloorCountingPending_AlwaysRequests()
		{
			// alive + pending < floor ⇒ request regardless of the staleness knob.
			Assert.That(CaptureSupplyMath.ShouldRequestTecn(3, 0, 1, 1000, 1000, 200), Is.True);
		}

		[Test]
		public void ShouldRequestTecn_EnoughAlive_NeverRequests()
		{
			// alive >= floor ⇒ never request, even with a stale timer and zero pending.
			Assert.That(CaptureSupplyMath.ShouldRequestTecn(1, 1, 0, 5000, 0, 200), Is.False);
			Assert.That(CaptureSupplyMath.ShouldRequestTecn(3, 3, 0, 5000, 0, 200), Is.False);
		}

		[Test]
		public void ShouldRequestTecn_PendingButUndelivered_ReissuesOnceStale()
		{
			// Floor met only by a pending request that hasn't delivered a TECN. Not yet stale ⇒ hold; once the
			// bounded tick age passes ⇒ re-issue (the un-deadlock).
			const int Stale = 200;
			var lastRequest = 1000;

			// 199 ticks later — not stale yet.
			Assert.That(CaptureSupplyMath.ShouldRequestTecn(1, 0, 1, lastRequest + Stale - 1, lastRequest, Stale),
				Is.False, "must not re-issue before the staleness age");

			// Exactly the staleness age — re-issue.
			Assert.That(CaptureSupplyMath.ShouldRequestTecn(1, 0, 1, lastRequest + Stale, lastRequest, Stale),
				Is.True, "must re-issue once the pending request has gone stale");
		}
	}
}
