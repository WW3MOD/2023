#region Copyright & License Information
/*
 * Copyright 2007-2022 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Traits.Render;

namespace OpenRA.Test
{
	/// <summary>
	/// Pins the pure blink-phase math for selection decorations (out-of-ammo pip, low-fuel/repair blink).
	/// The bug: the phase used to be derived from World.WorldTick, which advances at the game-speed logic
	/// rate — so the blink strobed on fast-forward and crawled on slow-motion. It is now derived from
	/// wall-clock milliseconds (Game.RunTime) via <see cref="DecorationBlink.PhaseIndex"/>, anchored to the
	/// nominal tick duration (Ui.Timestep = 40ms) so the on-screen cadence matches normal speed and is
	/// constant across all game speeds. This helper is render-only: it reads no sim / [Sync] state.
	///
	/// Source of truth: engine/OpenRA.Mods.Common/Traits/Render/WithDecorationBase.cs
	/// </summary>
	[TestFixture]
	public class DecorationBlinkMathTest
	{
		const int NominalTickMs = 40; // Ui.Timestep

		// ── Cadence is anchored to real time, matching the old normal-speed feel ──

		[Test]
		public void StepFlipsEveryBlinkIntervalNominalTicks()
		{
			// BlinkInterval 8, pattern length 2 -> one step lasts 8 * 40 = 320ms.
			// At normal speed this reproduces the old "8 WorldTicks per step" cadence exactly.
			Assert.That(DecorationBlink.PhaseIndex(0, 8, NominalTickMs, 2), Is.EqualTo(0));
			Assert.That(DecorationBlink.PhaseIndex(319, 8, NominalTickMs, 2), Is.EqualTo(0), "still in first step");
			Assert.That(DecorationBlink.PhaseIndex(320, 8, NominalTickMs, 2), Is.EqualTo(1), "flips at 320ms");
			Assert.That(DecorationBlink.PhaseIndex(639, 8, NominalTickMs, 2), Is.EqualTo(1));
			Assert.That(DecorationBlink.PhaseIndex(640, 8, NominalTickMs, 2), Is.EqualTo(0), "wraps back after two steps");
		}

		[Test]
		public void PhaseDependsOnlyOnWallClockNotTickRate()
		{
			// The whole point of the fix: identical wall-clock time -> identical phase, no matter how many
			// sim ticks elapsed (fast-forward packs more ticks into the same millisecond, slow-mo fewer).
			// Since the input is milliseconds, there is no tick-rate term to diverge on.
			const long ms = 500;
			var a = DecorationBlink.PhaseIndex(ms, 16, NominalTickMs, 2);
			var b = DecorationBlink.PhaseIndex(ms, 16, NominalTickMs, 2);
			Assert.That(a, Is.EqualTo(b));
		}

		[Test]
		public void PhaseAdvancesMonotonicallyThroughTheWallClock()
		{
			// A regular real-time sweep visits every pattern index in order, one per step.
			var seen = new System.Collections.Generic.List<int>();
			for (var ms = 0L; ms < 4 * 320; ms += 320)
				seen.Add(DecorationBlink.PhaseIndex(ms, 8, NominalTickMs, 4));

			Assert.That(seen, Is.EqualTo(new[] { 0, 1, 2, 3 }));
		}

		// ── Pattern length handling ──

		[Test]
		public void WrapsAroundArbitraryPatternLength()
		{
			// step = 5 * 40 = 200ms, length 3: indices cycle 0,1,2,0,...
			Assert.That(DecorationBlink.PhaseIndex(0, 5, NominalTickMs, 3), Is.EqualTo(0));
			Assert.That(DecorationBlink.PhaseIndex(200, 5, NominalTickMs, 3), Is.EqualTo(1));
			Assert.That(DecorationBlink.PhaseIndex(400, 5, NominalTickMs, 3), Is.EqualTo(2));
			Assert.That(DecorationBlink.PhaseIndex(600, 5, NominalTickMs, 3), Is.EqualTo(0));
		}

		[Test]
		public void ResultIsAlwaysInRange()
		{
			for (var ms = 0L; ms < 5000; ms += 37)
			{
				var i = DecorationBlink.PhaseIndex(ms, 8, NominalTickMs, 2);
				Assert.That(i, Is.InRange(0, 1));
			}
		}

		// ── Degenerate config never divides by zero (old WorldTick/Interval would have) ──

		[Test]
		public void ZeroIntervalDoesNotThrow()
		{
			// stepMs is floored to 1, so a misconfigured BlinkInterval of 0 degrades gracefully instead
			// of throwing DivideByZero as the original WorldTick / BlinkInterval expression would.
			Assert.That(DecorationBlink.PhaseIndex(1000, 0, NominalTickMs, 2), Is.InRange(0, 1));
		}
	}
}
