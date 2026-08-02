#region Copyright & License Information
/*
 * WW3MOD capture-supply guarantee (@experimental) — TECN floor + re-request tests.
 *
 * Pins the two decisions CaptureCoordinatorBotModule.MaintainTecnFloor turns into a production request, so the
 * S2 capture-supply fix (WORKSPACE/recon/260731) can't silently regress AND @stable byte-identity is proven:
 *   (1) EffectiveFloor — scaling OFF reproduces the static floor exactly (the frozen path); scaling ON is
 *       ~one capturer per neutral money POI, clamped to [staticFloor, cap].
 *   (2) ShouldRequestTecn — with the staleness knob OFF the predicate is EXACTLY the frozen
 *       `alive + pending < floor` gate; with it ON a stale pending request is re-issued ONLY while pending is
 *       below the floor (the in-flight cap that bounds pending to [0, floor] — the pending=82 deadlock fix).
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
		public void ShouldRequestTecn_PartialFloorPendingBelowFloor_ReissuesOnceStale()
		{
			// A partial in-flight case where pending is BELOW the floor (some capturers alive, one request in
			// flight): floor met only by alive+pending, pending < floor ⇒ the staleness backstop still applies.
			// Not yet stale ⇒ hold; once the bounded tick age passes ⇒ re-issue.
			const int Stale = 200;
			var lastRequest = 1000;

			// floor=3, alive=2, pending=1 ⇒ alive+pending==floor (met only by the pending request), pending<floor.
			// 199 ticks later — not stale yet.
			Assert.That(CaptureSupplyMath.ShouldRequestTecn(3, 2, 1, lastRequest + Stale - 1, lastRequest, Stale),
				Is.False, "must not re-issue before the staleness age");

			// Exactly the staleness age — re-issue.
			Assert.That(CaptureSupplyMath.ShouldRequestTecn(3, 2, 1, lastRequest + Stale, lastRequest, Stale),
				Is.True, "must re-issue once the pending request has gone stale");
		}

		// ---- ShouldRequestTecn: in-flight CAP (pending bounded to the floor) ----

		[Test]
		public void ShouldRequestTecn_PendingAtFloor_NeverReissues_EvenWhenStale()
		{
			// The measured deadlock (pending=82 / alive=0): the staleness re-issue used to fire whenever alive <
			// floor, adding a duplicate every stale interval without bound. With reliable peek-don't-pop delivery
			// the in-flight request is no longer lost, so once pending already meets the floor NO further request
			// is issued — however old the outstanding request is. This caps pending at the floor.
			Assert.That(CaptureSupplyMath.ShouldRequestTecn(5, 0, 5, 999999, 0, 200), Is.False,
				"pending==floor must not re-issue even long past the staleness age");

			// One over the floor (defensive) — still capped.
			Assert.That(CaptureSupplyMath.ShouldRequestTecn(5, 0, 6, 999999, 0, 200), Is.False);

			// The exact benchmark smell: floor=5, alive=0, pending climbing — never re-issued at/above floor.
			Assert.That(CaptureSupplyMath.ShouldRequestTecn(5, 0, 5, 17961, 17061, 200), Is.False,
				"the pending=82 growth path is now bounded at the floor");
		}

		[Test]
		public void ShouldRequestTecn_PendingBoundedToFloor_OverManyStaleScans()
		{
			// Simulate repeated stale scans with alive stuck at 0 (delivery not yet landed). Model the coordinator
			// loop: each scan that returns true adds one to pending. Assert pending never exceeds the floor —
			// i.e. it converges to the floor and stops, instead of growing to 82.
			const int Floor = 5;
			const int Stale = 200;
			var pending = 0;
			var lastRequest = 0;
			var tick = 0;

			for (var scan = 0; scan < 200; scan++)
			{
				tick += Stale; // every scan is "stale" so the cap — not the timer — is what bounds growth.
				if (CaptureSupplyMath.ShouldRequestTecn(Floor, 0, pending, tick, lastRequest, Stale))
				{
					pending++;
					lastRequest = tick;
				}

				Assert.That(pending, Is.LessThanOrEqualTo(Floor), $"pending must stay bounded (scan {scan})");
			}

			Assert.That(pending, Is.EqualTo(Floor), "pending converges to exactly the floor");
		}

		// ---------- ClampFloorToArmyShare (combat-quality budget split) ----------

		[Test]
		public void ClampFloorToArmyShare_InertAtOrAboveHundred()
		{
			// >= 100 = never bind: the floor passes through verbatim regardless of the army.
			Assert.That(CaptureSupplyMath.ClampFloorToArmyShare(5, 0, 100), Is.EqualTo(5));
			Assert.That(CaptureSupplyMath.ClampFloorToArmyShare(5, 2, 100), Is.EqualTo(5));
			Assert.That(CaptureSupplyMath.ClampFloorToArmyShare(5, 100, 150), Is.EqualTo(5));
		}

		[Test]
		public void ClampFloorToArmyShare_LowersWhenArmyThin()
		{
			// 50% of an 8-unit army = 4, below the floor of 5 ⇒ clamped down to 4 (budget yields to combat).
			Assert.That(CaptureSupplyMath.ClampFloorToArmyShare(5, 8, 50), Is.EqualTo(4));
			// A 20-unit army at 50% = 10 >= floor 5 ⇒ floor unchanged (clamp only ever lowers).
			Assert.That(CaptureSupplyMath.ClampFloorToArmyShare(5, 20, 50), Is.EqualTo(5));
			// No army at all ⇒ clamp to 0 (don't force capturers when there's no combat force to share with).
			Assert.That(CaptureSupplyMath.ClampFloorToArmyShare(5, 0, 50), Is.EqualTo(0));
		}
	}
}
