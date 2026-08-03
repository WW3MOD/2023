#region Copyright & License Information
/*
 * WW3MOD transport-heli empty-delivery decision test.
 *
 * Pins TransportLoadMath — the gate that stopped a transport heli flying its delivery leg EMPTY. A
 * dispatched transport now stages Loading -> Delivering: it departs only once cargo is actually aboard
 * (Dispatch), delivers a partial load on timeout if at least one soldier boarded, and ABORTS an empty
 * load (nobody boarded — killed / poached / never reached the heli) instead of delivering nothing.
 * Pure integer math; no world mounted; deterministic.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class TransportLoadMathTest
	{
		const int Min = 4;
		const int Timeout = 1500;

		[Test]
		public void FullLoadAboard_DispatchesImmediately()
		{
			// Enough passengers aboard, well within the timeout ⇒ go now.
			Assert.That(
				TransportLoadMath.Decide(passengersAboard: 4, minPassengers: Min, ticksLoading: 10, loadTimeoutTicks: Timeout),
				Is.EqualTo(TransportLoadDecision.Dispatch));
		}

		[Test]
		public void OverloadedAboard_Dispatches()
		{
			// More than the minimum (a bigger transport) still dispatches — the gate is >=, not ==.
			Assert.That(
				TransportLoadMath.Decide(6, Min, 10, Timeout),
				Is.EqualTo(TransportLoadDecision.Dispatch));
		}

		[Test]
		public void PartialLoad_WithinTimeout_KeepsWaiting()
		{
			// Some but not all boarded, still inside the window ⇒ wait for the rest.
			Assert.That(
				TransportLoadMath.Decide(2, Min, Timeout - 1, Timeout),
				Is.EqualTo(TransportLoadDecision.Wait));
		}

		[Test]
		public void EmptyLoad_WithinTimeout_KeepsWaiting()
		{
			// Nobody aboard yet but the soldiers are still walking over ⇒ wait, do not abort early.
			Assert.That(
				TransportLoadMath.Decide(0, Min, Timeout - 1, Timeout),
				Is.EqualTo(TransportLoadDecision.Wait));
		}

		[Test]
		public void PartialLoad_PastTimeout_DispatchesPartial()
		{
			// The key survivability case: at least one soldier boarded before the window elapsed ⇒ deliver the
			// partial load rather than waiting forever for reinforcements that may never come.
			Assert.That(
				TransportLoadMath.Decide(1, Min, Timeout + 1, Timeout),
				Is.EqualTo(TransportLoadDecision.Dispatch));
		}

		[Test]
		public void EmptyLoad_PastTimeout_Aborts()
		{
			// The empty-delivery bug's exact case: nobody ever boarded (killed / poached / never arrived) and the
			// window elapsed ⇒ ABORT — the heli must NOT fly the delivery empty.
			Assert.That(
				TransportLoadMath.Decide(0, Min, Timeout + 1, Timeout),
				Is.EqualTo(TransportLoadDecision.Abort));
		}

		[Test]
		public void TimeoutBoundaryIsStrict()
		{
			// Exactly AT the timeout is still within the window (the code uses strictly-greater), so an empty
			// load at the boundary keeps waiting one more eval rather than aborting.
			Assert.That(
				TransportLoadMath.Decide(0, Min, Timeout, Timeout),
				Is.EqualTo(TransportLoadDecision.Wait));
		}

		[Test]
		public void FullLoad_TakesPriorityOverTimeout()
		{
			// A full load past the timeout still dispatches (full — not an abort): the aboard check is first.
			Assert.That(
				TransportLoadMath.Decide(Min, Min, Timeout + 100, Timeout),
				Is.EqualTo(TransportLoadDecision.Dispatch));
		}

		[Test]
		public void DecisionsAreDeterministic()
		{
			Assert.That(
				TransportLoadMath.Decide(2, Min, Timeout + 1, Timeout),
				Is.EqualTo(TransportLoadMath.Decide(2, Min, Timeout + 1, Timeout)));
		}
	}
}
