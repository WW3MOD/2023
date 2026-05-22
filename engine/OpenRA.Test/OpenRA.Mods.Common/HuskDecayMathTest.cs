#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using NUnit.Framework;

namespace OpenRA.Test
{
	/// <summary>
	/// Mirrors the alpha/phase state machine from HuskDecay. The trait needs a
	/// World/Actor to test end-to-end (terrain lookup, splash effect, frame-end
	/// dispose), but the timing-driven state transitions and fade alpha are
	/// pure math and reproduced here.
	///
	/// State machine: Waiting → Fading → Done.
	///   Waiting: ticks++, when ticks >= Delay → switch to Fading, ticks = 0.
	///   Fading:  ticks++, alpha = 1 - ticks/FadeDuration; when ticks >= FadeDuration → Done.
	///   Done:    alpha = 0, dispose at end of frame.
	/// </summary>
	[TestFixture]
	public class HuskDecayMathTest
	{
		enum Phase { Waiting, Fading, Done }

		sealed class HuskState
		{
			public int Delay;
			public int FadeDuration;
			public int Ticks;
			public Phase Phase = Phase.Waiting;

			public void Tick()
			{
				Ticks++;
				if (Phase == Phase.Waiting && Ticks >= Delay)
				{
					Phase = Phase.Fading;
					Ticks = 0;
				}
				else if (Phase == Phase.Fading && Ticks >= FadeDuration)
				{
					Phase = Phase.Done;
				}
			}

			public float Alpha
			{
				get
				{
					if (Phase == Phase.Fading)
						return 1f - (float)Ticks / FadeDuration;

					if (Phase == Phase.Done)
						return 0f;

					return 1f; // Waiting
				}
			}
		}

		// --- Phase transitions ---

		[Test]
		public void StartsInWaitingWithFullAlpha()
		{
			var h = new HuskState { Delay = 100, FadeDuration = 50 };
			Assert.That(h.Phase, Is.EqualTo(Phase.Waiting));
			Assert.That(h.Alpha, Is.EqualTo(1f));
		}

		[Test]
		public void TransitionsToFadingAfterDelay()
		{
			var h = new HuskState { Delay = 5, FadeDuration = 10 };
			for (var i = 0; i < 5; i++)
				h.Tick();
			Assert.That(h.Phase, Is.EqualTo(Phase.Fading));
			Assert.That(h.Ticks, Is.EqualTo(0), "ticks reset on transition");
			Assert.That(h.Alpha, Is.EqualTo(1f), "alpha not yet decayed");
		}

		[Test]
		public void StaysInWaitingBeforeDelay()
		{
			var h = new HuskState { Delay = 10, FadeDuration = 5 };
			for (var i = 0; i < 9; i++)
				h.Tick();
			Assert.That(h.Phase, Is.EqualTo(Phase.Waiting));
			Assert.That(h.Alpha, Is.EqualTo(1f));
		}

		[Test]
		public void TransitionsToDoneAfterFadeDuration()
		{
			var h = new HuskState { Delay = 2, FadeDuration = 4 };
			h.Tick(); h.Tick();                // 2 ticks: phase=Fading, ticks=0
			h.Tick(); h.Tick(); h.Tick(); h.Tick(); // 4 fade ticks
			Assert.That(h.Phase, Is.EqualTo(Phase.Done));
			Assert.That(h.Alpha, Is.EqualTo(0f));
		}

		// --- Alpha curve ---

		[Test]
		public void AlphaIsOneAtFadeStart()
		{
			var h = new HuskState { Delay = 1, FadeDuration = 10 };
			h.Tick(); // Phase = Fading, ticks = 0
			Assert.That(h.Phase, Is.EqualTo(Phase.Fading));
			Assert.That(h.Alpha, Is.EqualTo(1f));
		}

		[Test]
		public void AlphaIsHalfwayAtMidpoint()
		{
			var h = new HuskState { Delay = 0, FadeDuration = 10 };
			h.Tick(); // Phase = Fading, ticks = 0
			for (var i = 0; i < 5; i++)
				h.Tick();
			Assert.That(h.Alpha, Is.EqualTo(0.5f).Within(0.0001f));
		}

		[Test]
		public void AlphaApproachesZeroBeforeDone()
		{
			var h = new HuskState { Delay = 0, FadeDuration = 10 };
			h.Tick(); // Phase = Fading, ticks = 0
			for (var i = 0; i < 9; i++)
				h.Tick();
			Assert.That(h.Alpha, Is.EqualTo(0.1f).Within(0.0001f));
		}

		[Test]
		public void AlphaDecreasesMonotonically()
		{
			var h = new HuskState { Delay = 0, FadeDuration = 100 };
			h.Tick();
			var prev = h.Alpha;
			for (var i = 0; i < 90; i++)
			{
				h.Tick();
				Assert.That(h.Alpha, Is.LessThanOrEqualTo(prev),
					$"Alpha should not increase: prev={prev}, now={h.Alpha}");
				prev = h.Alpha;
			}
		}

		[Test]
		public void TotalLifespanIsDelayPlusFadeDuration()
		{
			var delay = 50;
			var fade = 25;
			var h = new HuskState { Delay = delay, FadeDuration = fade };
			var ticksUntilDone = 0;
			while (h.Phase != Phase.Done && ticksUntilDone < 1000)
			{
				h.Tick();
				ticksUntilDone++;
			}

			// Phase transition consumes one Tick call: Waiting needs `delay` ticks to reach Delay,
			// the next Tick (delay+1) resets ticks=0 in Fading phase. Then `fade` more ticks.
			Assert.That(ticksUntilDone, Is.EqualTo(delay + fade));
		}

		[Test]
		public void ZeroDelayIsImmediateFade()
		{
			var h = new HuskState { Delay = 0, FadeDuration = 5 };
			h.Tick();
			Assert.That(h.Phase, Is.EqualTo(Phase.Fading));
		}

		[Test]
		public void ZeroFadeDurationIsImmediateDone()
		{
			// Edge case: 0 fade triggers Done on the first Fading tick.
			// (FadeDuration=0 means tick >= 0 immediately satisfies Done condition.)
			var h = new HuskState { Delay = 1, FadeDuration = 0 };
			h.Tick(); // → Fading, ticks=0
			h.Tick(); // → ticks=1 >= 0 → Done
			Assert.That(h.Phase, Is.EqualTo(Phase.Done));
			Assert.That(h.Alpha, Is.EqualTo(0f));
		}
	}
}
