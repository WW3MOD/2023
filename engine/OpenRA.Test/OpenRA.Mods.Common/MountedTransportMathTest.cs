#region Copyright & License Information
/*
 * WW3MOD MountedTransportBotModule pure-geometry test (@experimental transport-shuttle improvement).
 *
 * Pins the two decisions the fog-legal shuttle turns believed data into, without a game run:
 *   (1) PICKUP CORRIDOR — a passenger walking the SR→drop lane is in-corridor; one off to the side, or
 *       past the endpoints, or when the corridor is disabled, is not. This is the "catch the mid-walk
 *       infantry" widen from the 14-cell reserve bubble.
 *   (2) STANDOFF INDEX — given believed anti-ground danger sampled from the intended drop back toward our
 *       SR, choose the drop cell just OUTSIDE the believed envelope (+ margin): keep an already-safe
 *       target, back off to first-safe+margin through a hot envelope, and fall back to the furthest-back
 *       cell when nothing sampled is safe.
 * Plus a determinism guard. Pure integer math; no world mounted, zero RNG.
 */
#endregion

using System.Collections.Generic;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class MountedTransportMathTest
	{
		// ---------- Pickup corridor ----------

		[TestCase(0, 5, TestName = "corridor: on-lane midpoint is in")]      // p directly on the lane
		public void OnLaneIsInCorridor(int _, int halfWidth)
		{
			var a = new CPos(0, 0);
			var b = new CPos(20, 0);
			var p = new CPos(10, 0);
			Assert.That(MountedTransportMath.InCorridor(a, b, p, halfWidth), Is.True);
		}

		[Test]
		public void WithinHalfWidthIsIn_BeyondIsOut()
		{
			var a = new CPos(0, 0);
			var b = new CPos(20, 0);

			// 3 cells off a horizontal lane, half-width 4 → in; half-width 2 → out.
			var p = new CPos(10, 3);
			Assert.That(MountedTransportMath.InCorridor(a, b, p, 4), Is.True, "3 off, width 4 should be in");
			Assert.That(MountedTransportMath.InCorridor(a, b, p, 2), Is.False, "3 off, width 2 should be out");
		}

		[Test]
		public void PastTheEndpointsIsOut()
		{
			var a = new CPos(0, 0);
			var b = new CPos(20, 0);

			// On the lane's infinite line but beyond b, and behind a — both outside the SPAN.
			Assert.That(MountedTransportMath.InCorridor(a, b, new CPos(25, 0), 4), Is.False, "beyond b");
			Assert.That(MountedTransportMath.InCorridor(a, b, new CPos(-5, 0), 4), Is.False, "behind a");
		}

		[Test]
		public void DisabledOrDegenerateIsAlwaysOut()
		{
			var a = new CPos(0, 0);
			var b = new CPos(20, 0);
			var p = new CPos(10, 0);
			Assert.That(MountedTransportMath.InCorridor(a, b, p, 0), Is.False, "width 0 = off");
			Assert.That(MountedTransportMath.InCorridor(a, b, p, -1), Is.False, "negative width = off");

			// Degenerate zero-length lane: never in-corridor (no direction to project onto).
			Assert.That(MountedTransportMath.InCorridor(a, a, a, 4), Is.False, "zero-length lane");
		}

		[Test]
		public void CorridorWorksOnADiagonalLane()
		{
			var a = new CPos(0, 0);
			var b = new CPos(20, 20);

			// A cell just off the 45° lane: perpendicular distance ≈ sqrt(2) ≈ 1.41 cells.
			var p = new CPos(11, 9);
			Assert.That(MountedTransportMath.InCorridor(a, b, p, 2), Is.True, "≈1.4 off, width 2 in");
			Assert.That(MountedTransportMath.InCorridor(a, b, p, 1), Is.False, "≈1.4 off, width 1 out");
		}

		// ---------- Standoff index ----------

		[Test]
		public void AlreadySafeTargetIsKept()
		{
			// dangers[0] (the intended drop) is at/below threshold → no back-off, keep index 0.
			var dangers = new List<int> { 0, 0, 0 };
			Assert.That(MountedTransportMath.ChooseStandoffIndex(dangers, 0, 2), Is.EqualTo(0));
		}

		[Test]
		public void BacksOffToFirstSafePlusMargin()
		{
			// Hot at the drop and one cell back, clears at index 2. threshold 0, margin 2 → 2+2 = 4.
			var dangers = new List<int> { 90, 40, 0, 0, 0, 0 };
			Assert.That(MountedTransportMath.ChooseStandoffIndex(dangers, 0, 2), Is.EqualTo(4));
		}

		[Test]
		public void MarginClampsToSampledRange()
		{
			// First safe at index 3, margin 5 would overshoot; clamp to last index (5).
			var dangers = new List<int> { 90, 90, 90, 0, 0, 0 };
			Assert.That(MountedTransportMath.ChooseStandoffIndex(dangers, 0, 5), Is.EqualTo(5));
		}

		[Test]
		public void ThresholdIsInclusiveAndTunable()
		{
			// With threshold 40, the index-1 cell (danger 40) already counts as safe → first-safe 1 + margin 0.
			var dangers = new List<int> { 90, 40, 10 };
			Assert.That(MountedTransportMath.ChooseStandoffIndex(dangers, 40, 0), Is.EqualTo(1));
		}

		[Test]
		public void NoSafeCellFallsBackToFurthestBack()
		{
			// Everything sampled is hot → use the furthest-back cell (closest to our SR), never the hot drop.
			var dangers = new List<int> { 200, 180, 160, 150 };
			Assert.That(MountedTransportMath.ChooseStandoffIndex(dangers, 0, 2), Is.EqualTo(3));
		}

		[Test]
		public void EmptyOrNullIsIndexZero()
		{
			Assert.That(MountedTransportMath.ChooseStandoffIndex(new List<int>(), 0, 2), Is.EqualTo(0));
			Assert.That(MountedTransportMath.ChooseStandoffIndex(null, 0, 2), Is.EqualTo(0));
		}

		[Test]
		public void IsDeterministic()
		{
			var dangers = new List<int> { 90, 40, 0, 0, 0 };
			var a = MountedTransportMath.ChooseStandoffIndex(dangers, 0, 2);
			var b = MountedTransportMath.ChooseStandoffIndex(dangers, 0, 2);
			Assert.That(a, Is.EqualTo(b));

			var c = MountedTransportMath.InCorridor(new CPos(0, 0), new CPos(20, 0), new CPos(10, 3), 4);
			var d = MountedTransportMath.InCorridor(new CPos(0, 0), new CPos(20, 0), new CPos(10, 3), 4);
			Assert.That(c, Is.EqualTo(d));
		}

		// ---------- Departure decision (fill-before-departure) ----------
		//
		// The behaviour under test is "make loads fuller WITHOUT letting a carrier hang waiting for a
		// passenger that is never going to arrive". The fullness half is easy to assert; the no-hang half is
		// the one that matters, so it gets an exhaustive invariant rather than a couple of examples.

		const int Min = 2;
		const int Timeout = 1500;
		const int Stall = 250;

		static CarrierDeparture Fill(int aboard, int seatTarget, int stillComing, int ticksLoading, int sinceBoard)
		{
			return MountedTransportMath.DecideDeparture(true, aboard, seatTarget, stillComing, Min,
				ticksLoading, Timeout, sinceBoard, Stall);
		}

		[Test]
		public void BaselinePathKeepsTheLegacyThresholdRule()
		{
			// Frozen default: leave the instant MinPassengersPerLoad are aboard, however many more were
			// ordered. This is the half-empty departure the fill lever exists to remove — pinned so a
			// profile that does not opt in cannot drift.
			Assert.That(MountedTransportMath.DecideDeparture(false, 2, 5, 3, Min, 10, Timeout, 10, Stall),
				Is.EqualTo(CarrierDeparture.Threshold));

			Assert.That(MountedTransportMath.DecideDeparture(false, 1, 5, 4, Min, 10, Timeout, 10, Stall),
				Is.EqualTo(CarrierDeparture.Wait));

			// Baseline ignores the stall bound entirely — only the hard timeout releases it.
			Assert.That(MountedTransportMath.DecideDeparture(false, 1, 5, 4, Min, 10, Timeout, 9999, Stall),
				Is.EqualTo(CarrierDeparture.Wait));
		}

		[Test]
		public void FillWaitsForTheSeatsItOrderedInsteadOfTheMinimum()
		{
			// The whole point: 2 aboard of 5 ordered, 3 still walking, plenty of time — baseline would drive
			// off half empty here, the fill path waits.
			Assert.That(Fill(2, 5, 3, 10, 10), Is.EqualTo(CarrierDeparture.Wait));
			Assert.That(Fill(5, 5, 0, 90, 5), Is.EqualTo(CarrierDeparture.Full));
		}

		[Test]
		public void FullIsMeasuredAgainstSeatsOrderedNotCapacity()
		{
			// Only 3 soldiers existed, so 3 were ordered aboard. That IS a full load; waiting for a 4th and
			// 5th the carrier never asked for would stall every under-strength run.
			Assert.That(Fill(3, 3, 0, 40, 5), Is.EqualTo(CarrierDeparture.Full));
		}

		[Test]
		public void NobodyElseComingReleasesTheWait()
		{
			// The user's case: the last seat can never be filled because that passenger no longer exists.
			// stillComing has dropped to 0 with 3 of 5 aboard → go, do not sit out the timeout.
			Assert.That(Fill(3, 5, 0, 60, 20), Is.EqualTo(CarrierDeparture.NobodyElseComing));

			// Same signal with nothing aboard means the load evaporated — abandon rather than deliver air.
			Assert.That(Fill(0, 5, 0, 60, 20), Is.EqualTo(CarrierDeparture.AbortEmpty));
		}

		[Test]
		public void StallReleasesAPassengerThatIsAliveButNeverArriving()
		{
			// A soldier re-tasked away by another module is still alive and in the world, so stillComing
			// stays positive forever. Only the progress bound can see this.
			Assert.That(Fill(3, 5, 2, 400, Stall), Is.EqualTo(CarrierDeparture.Stalled));
			Assert.That(Fill(3, 5, 2, 400, Stall - 1), Is.EqualTo(CarrierDeparture.Wait));

			// A stalled load still under the minimum is not worth delivering yet; it waits for the hard
			// bound, which will take it at whatever it has.
			Assert.That(Fill(1, 5, 2, 400, Stall), Is.EqualTo(CarrierDeparture.Wait));

			// Stall release is opt-out: at 0 only the hard timeout remains.
			Assert.That(MountedTransportMath.DecideDeparture(true, 3, 5, 2, Min, 400, Timeout, 9999, 0),
				Is.EqualTo(CarrierDeparture.Wait));
		}

		[Test]
		public void HardTimeoutTakesWhateverIsAboard()
		{
			Assert.That(Fill(1, 5, 2, Timeout + 1, 10), Is.EqualTo(CarrierDeparture.Timeout));
			Assert.That(Fill(0, 5, 2, Timeout + 1, 10), Is.EqualTo(CarrierDeparture.AbortEmpty));
		}

		[Test]
		public void WaitIsUnreachableOncePatienceElapsed_NoHangInvariant()
		{
			// THE load-bearing property. A carrier that waits for a fuller load is only safe if something
			// ends the wait regardless of what the passengers do — so sweep the whole input space and assert
			// that past either time bound the answer is never Wait. Both bounds are monotonic in elapsed
			// ticks, so a carrier cannot stay in Loading indefinitely for ANY passenger behaviour.
			foreach (var fill in new[] { false, true })
				for (var aboard = 0; aboard <= 6; aboard++)
					for (var seatTarget = 0; seatTarget <= 6; seatTarget++)
						for (var stillComing = 0; stillComing <= 6; stillComing++)
						{
							var pastHardBound = MountedTransportMath.DecideDeparture(fill,
								aboard, seatTarget, stillComing, Min, Timeout + 1, Timeout, 0, Stall);

							Assert.That(pastHardBound, Is.Not.EqualTo(CarrierDeparture.Wait),
								$"hung past the hard timeout: fill={fill} aboard={aboard} " +
								$"target={seatTarget} coming={stillComing}");
						}

			// And the stall bound alone releases any load worth delivering, well before the hard timeout.
			for (var aboard = Min; aboard <= 6; aboard++)
				for (var stillComing = 1; stillComing <= 6; stillComing++)
					Assert.That(Fill(aboard, 6, stillComing, 300, Stall), Is.Not.EqualTo(CarrierDeparture.Wait),
						$"hung past the stall bound: aboard={aboard} coming={stillComing}");
		}

		[Test]
		public void DepartureIsDeterministic()
		{
			Assert.That(Fill(2, 5, 3, 10, 10), Is.EqualTo(Fill(2, 5, 3, 10, 10)));
		}
	}
}
