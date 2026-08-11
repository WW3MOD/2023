#region Copyright & License Information
/*
 * WW3MOD capture-supply guarantee (@experimental) — TECN floor + re-request tests.
 *
 * Pins the two decisions CaptureCoordinatorBotModule.MaintainTecnFloor turns into a production request, so the
 * S2 capture-supply fix (WORKSPACE/recon/260731) can't silently regress. NOTE (b8d2e601, 2026-08-02): these no
 * longer prove "@stable byte-identity" — see the reach note above the first test; the OFF cases now pin the
 * switch contract only, not any shipped profile:
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
		// REACH NOTE (b8d2e601, 2026-08-02) — READ BEFORE TRUSTING A GREEN RUN HERE.
		// Every "frozen path" case below — EffectiveFloor with scaleEnabled=false, and ShouldRequestTecn with
		// staleTicks<=0 — pins a configuration that NO live profile selects any more. @stable.tecn was promoted
		// to full @experimental parity, so both twins now set ScaleTecnFloorToPois: true and
		// TecnRequestStaleTicks: 200 (ai.yaml CaptureCoordinatorBotModule@experimental.tecn and @stable.tecn).
		// These cases are kept deliberately, as a contract on the SWITCH SEMANTICS (off ⇒ the pre-feature answer
		// verbatim) — they are NOT coverage of shipped behaviour and can stay green while live capture supply is
		// broken. The ON cases (scaling, staleness, in-flight cap) are what production actually executes.
		// Same caveat for ClampFloorToArmyShare at the bottom of this file, only harder: its only caller is
		// gated on TecnFloorArmyShareCapPct < 100 (CaptureCoordinatorBotModule.cs:824) and both profiles leave
		// it at 100 (ai.yaml @experimental.tecn; omitted on @stable ⇒ default 100), so that function has NO production caller
		// at all today. Do not delete these — they are the regression net if a switch is ever turned back off.

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

		[Test]
		public void EffectiveFloor_ScalingOn_ExactBoundaries()
		{
			// POI count exactly at the static floor ⇒ that floor (the scaled < floor branch is not taken).
			Assert.That(CaptureSupplyMath.EffectiveFloor(true, 3, 3, 5), Is.EqualTo(3));
			// POI count exactly at the cap ⇒ the cap (the scaled > cap branch is not taken).
			Assert.That(CaptureSupplyMath.EffectiveFloor(true, 1, 5, 5), Is.EqualTo(5));
		}

		[Test]
		public void EffectiveFloor_ScalingOn_CapBelowStaticFloor_CapWins()
		{
			// Documented (intentional) mis-set: floorCap < staticFloor. The floor lift runs first
			// (scaled=max(poi, staticFloor)) then the cap clamp runs LAST, so the cap is the outer bound and
			// wins — lowering demand below the static floor. A safe direction, not a bug (see CaptureSupplyMath
			// EffectiveFloor doc). Pins that the clamp order is floor-then-cap.
			Assert.That(CaptureSupplyMath.EffectiveFloor(true, 5, 3, 2), Is.EqualTo(2));
			Assert.That(CaptureSupplyMath.EffectiveFloor(true, 5, 9, 2), Is.EqualTo(2));
		}

		[Test]
		public void EffectiveFloor_ScalingOn_NegativePoiCount_HeldAtStaticFloor()
		{
			// Defensive: a negative POI count (should never happen) is lifted to the static floor by the
			// never-below-floor clamp, so demand can't go negative.
			Assert.That(CaptureSupplyMath.EffectiveFloor(true, 1, -5, 5), Is.EqualTo(1));
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

		// ---- ShouldRequestTecn: degenerate floor + non-monotonic tick guards ----

		[Test]
		public void ShouldRequestTecn_FloorZeroOrNegative_NeverRequests()
		{
			// A zero (or negative) floor means "no capturers required" — alive >= floor is trivially true, so
			// the first gate short-circuits to false regardless of pending / staleness.
			Assert.That(CaptureSupplyMath.ShouldRequestTecn(0, 0, 0, 5000, 0, 200), Is.False);
			Assert.That(CaptureSupplyMath.ShouldRequestTecn(-1, 0, 0, 5000, 0, 200), Is.False);
		}

		[Test]
		public void ShouldRequestTecn_NonMonotonicTick_DoesNotFalselyReissue()
		{
			// Defensive: if currentTick < lastRequestTick (should never happen with a synced monotonic clock),
			// the age is negative so the >= staleTicks staleness test is false ⇒ no premature re-issue in the
			// partial in-flight case (alive+pending==floor, pending<floor).
			Assert.That(CaptureSupplyMath.ShouldRequestTecn(3, 2, 1, 900, 1000, 200), Is.False,
				"a backwards tick must not read as stale");
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
