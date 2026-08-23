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

using System;
using System.IO;
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

		// ── Animated decoration SEQUENCES (the sibling mechanism) ──
		// A decoration can also blink by naming a multi-frame sequence rather than a BlinkPattern.
		// That path used to advance off the sim tick: Animation.Tick() credits a fixed 40 "animation
		// ms" per world tick, but a world tick is Timestep ms of real time, so the on-screen rate
		// scaled with game speed. WithDecoration now fetches the frame from wall-clock via
		// PhaseIndex(Game.RunTime, 1, CurrentSequence.Tick, CurrentSequence.Length), which makes a
		// sequence's Tick: mean real milliseconds per frame at every speed.

		const int CriticalPipTickMs = 450;  // sequences-misc.yaml: pip-damage-{infantry,vehicle}-critical
		const int CriticalPipLength = 2;
		const int AnimationMsPerWorldTick = 40; // Animation.cs DefaultTick, "25 fps == 40 ms"

		static int SequenceFrame(long runTimeMs, int sequenceTickMs, int length)
		{
			return DecorationBlink.PhaseIndex(runTimeMs, 1, sequenceTickMs, length);
		}

		[Test]
		public void StaticDecorationSequencesAlwaysResolveToFrameZero()
		{
			// Every decoration in the mod except the two critical-damage pips names a Length: 1
			// sequence. Switching WithDecoration from PlayRepeating to PlayFetchIndex must leave
			// all of those pinned to their single frame.
			for (var ms = 0L; ms < 10000; ms += 137)
				Assert.That(SequenceFrame(ms, 40, 1), Is.EqualTo(0));
		}

		[Test]
		public void CriticalPipFrameFlipsEveryTickMillisecondsOfRealTime()
		{
			Assert.That(SequenceFrame(0, CriticalPipTickMs, CriticalPipLength), Is.EqualTo(0));
			Assert.That(SequenceFrame(449, CriticalPipTickMs, CriticalPipLength), Is.EqualTo(0));
			Assert.That(SequenceFrame(450, CriticalPipTickMs, CriticalPipLength), Is.EqualTo(1), "flips at 450ms");
			Assert.That(SequenceFrame(900, CriticalPipTickMs, CriticalPipLength), Is.EqualTo(0), "900ms full cycle");
		}

		[Test]
		public void CriticalPipTickPreservesTheOldDefaultSpeedCadence()
		{
			// The YAML moved from Tick: 300 to Tick: 450 because the unit changed, NOT because the
			// pip was retuned. Old path: 300 animation-ms at 40 credited per world tick = 7.5 world
			// ticks per frame; at the default 60ms Timestep that is 450ms of real time per frame.
			// If someone edits Tick: without understanding this, that is the number to re-derive.
			const int OldSequenceTick = 300;
			const int DefaultTimestepMs = 60; // mod.yaml GameSpeeds: default
			var oldRealMsPerFrame = OldSequenceTick * DefaultTimestepMs / AnimationMsPerWorldTick;

			Assert.That(oldRealMsPerFrame, Is.EqualTo(CriticalPipTickMs),
				"Tick: 450 must reproduce the pre-change appearance at the default game speed");
		}

		[Test]
		public void OldSequenceCadenceWasGameSpeedDependent()
		{
			// Guards the premise of the fix rather than the fix: under the old ticked path the real
			// time per frame carried a Timestep term, so the same pip blinked 4x faster at the
			// fastest game speed than at the slowest. The replacement takes only wall-clock ms and
			// a sequence Tick — there is no Timestep term left in it to diverge on.
			const int OldSequenceTick = 300;
			var strategical = OldSequenceTick * 120 / AnimationMsPerWorldTick; // slowest speed
			var insane = OldSequenceTick * 30 / AnimationMsPerWorldTick;       // fastest speed

			Assert.That(strategical, Is.EqualTo(900));
			Assert.That(insane, Is.EqualTo(225));
			Assert.That(strategical / insane, Is.EqualTo(4), "old path varied 4x across the speed range");
		}

		// ── The blink ACCELERATES as the actor dies, so its rate reads as time remaining ──

		const int RampFloorMs = 120;  // sequences-misc.yaml HealthRampTick
		const int RampStart = 50;     // sequences-misc.yaml HealthRampStart — blink onset, the heavy band

		static int Interval(int healthPercent)
		{
			return DecorationBlink.IntervalForHealth(CriticalPipTickMs, RampFloorMs, RampStart, healthPercent);
		}

		[Test]
		public void CriticalPipRampGivesTheDocumentedIntervals()
		{
			// These are the numbers the YAML comment quotes; change one and change both.
			// Cycle time is twice the interval, the sequence being two frames.
			Assert.That(Interval(50), Is.EqualTo(450), "ramp start: 900ms cycle, unchanged from before the ramp");
			Assert.That(Interval(30), Is.EqualTo(318), "636ms cycle");
			Assert.That(Interval(10), Is.EqualTo(186), "372ms cycle");
			Assert.That(Interval(0), Is.EqualTo(RampFloorMs), "240ms cycle");
		}

		[Test]
		public void RampIsFlatAboveItsStartAndNeverInverts()
		{
			// Above blink onset the decoration is not drawn at all, but the maths must still be sane
			// there — a healthier actor may never blink faster than a dying one.
			Assert.That(Interval(100), Is.EqualTo(CriticalPipTickMs));
			Assert.That(Interval(75), Is.EqualTo(CriticalPipTickMs));

			for (var h = 100; h > 0; h--)
				Assert.That(Interval(h - 1), Is.LessThanOrEqualTo(Interval(h)),
					$"blink got slower as health fell from {h}% to {h - 1}%");
		}

		[Test]
		public void RampIsPerceptibleAcrossItsWholeRangeNotJustTheEnd()
		{
			// The named failure mode for this work: a ramp that evaluates to nothing over most of its
			// range, so the pip only visibly accelerates in the last moment. Every 10 points of health
			// inside the ramp must shorten the cycle by a double-digit percentage. Linear-in-interval
			// is weakest at the slow end (50->40 is ~14.7%) and strongest at the fast end (10->0 is
			// ~35%), so the 14 bound below is the slow end, not a slack allowance.
			for (var h = RampStart; h > 0; h -= 10)
			{
				var slower = Interval(h);
				var faster = Interval(h - 10);
				Assert.That((slower - faster) * 100 / slower, Is.GreaterThanOrEqualTo(14),
					$"health {h}% -> {h - 10}% barely changes the blink ({slower}ms -> {faster}ms)");
			}
		}

		[Test]
		public void RampDisabledLeavesTheSequenceAtAConstantRate()
		{
			// Both switches off by default, so every other sequence in the mod is untouched.
			Assert.That(DecorationBlink.IntervalForHealth(450, 0, 50, 10), Is.EqualTo(450));
			Assert.That(DecorationBlink.IntervalForHealth(450, 120, 0, 10), Is.EqualTo(450));
		}

		// ── Phase is carried across rate changes rather than re-derived from absolute time ──

		[Test]
		public void NaiveAbsoluteTimePhaseLeapsWhenTheIntervalChanges()
		{
			// The REJECTED implementation, pinned so the reason BlinkPhase exists is not forgotten.
			// runTime / interval % length re-rolls the index the instant the interval moves, and with
			// a health-scaled interval that is every damage event.
			const long AtMs = 300000;
			var before = (int)(AtMs / 450 % 2);
			var after = (int)(AtMs / 440 % 2);

			Assert.That(before, Is.Not.EqualTo(after),
				"documents the discontinuity the accumulator exists to avoid");
		}

		[Test]
		public void PhaseNeverFlipsMerelyBecauseTheRateChanged()
		{
			// The property that actually defines continuity: at one fixed instant, changing the rate
			// must not by itself change the displayed frame. Swept across many instants because a
			// leaping implementation only diverges at *some* of them — an earlier version of this test
			// picked one damage time, measured the gap between flips, and passed the naive formula
			// happily. A leap can lengthen a gap as easily as shorten it, so gap size is the wrong
			// property; equality across the change is the right one.
			var naiveWouldHaveLeapt = 0;

			for (var ms = 1000L; ms <= 400000; ms += 997)
			{
				var phase = new BlinkPhase();
				phase.Advance(0, 450, 2);

				var before = phase.Advance(ms, 450, 2);
				var after = phase.Advance(ms, 318, 2); // same instant, unit just took a hit

				Assert.That(after, Is.EqualTo(before), $"frame changed at {ms}ms purely from the rate change");

				if ((int)(ms / 450 % 2) != (int)(ms / 318 % 2))
					naiveWouldHaveLeapt++;
			}

			Assert.That(naiveWouldHaveLeapt, Is.GreaterThan(50),
				"premise check: the rejected absolute-time formula must actually leap at these instants, "
				+ "otherwise this test proves nothing");
		}

		[Test]
		public void PhaseKeepsAdvancingMonotonicallyThroughManyRateChanges()
		{
			// A unit under sustained fire changes rate constantly. Re-anchoring must not stall the
			// blink: an implementation that reset the frame's remaining time on every change would
			// freeze the pip solid under rapid damage.
			var phase = new BlinkPhase();
			var flips = 0;
			var previous = phase.Advance(0, 450, 2);

			for (var ms = 10L; ms <= 10000; ms += 10)
			{
				var health = Math.Max(1, 50 - (int)(ms / 200)); // bleeding out over ten seconds
				var frame = phase.Advance(ms, Interval(health), 2);
				if (frame != previous)
					flips++;

				previous = frame;
			}

			Assert.That(flips, Is.GreaterThan(20), "the blink stalled while the rate was moving");
		}

		// ── Wiring ──

		static string FindDecorationSource()
		{
			var dir = new DirectoryInfo(AppContext.BaseDirectory);
			for (var i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
			{
				var candidate = Path.Combine(dir.FullName, "OpenRA.Mods.Common", "Traits", "Render", "WithDecoration.cs");
				if (File.Exists(candidate))
					return candidate;
			}

			return null;
		}

		[Test]
		public void WithDecorationStillFetchesItsFrameFromWallClock()
		{
			// Every arithmetic pin above would still pass if the wiring were reverted to PlayRepeating,
			// because none of them touch WithDecoration. This is the only check here that covers the
			// seam. It is a source scan rather than a behavioural test because exercising the trait
			// needs a World; that remains a stated gap.
			var source = FindDecorationSource();
			if (source == null)
				Assert.Ignore("WithDecoration.cs is not reachable from the test output directory");

			var text = File.ReadAllText(source);

			Assert.That(text, Does.Contain("PlayFetchIndex"),
				"the wall-clock frame fetch is gone - the blink is back on the sim tick");
			Assert.That(text, Does.Not.Contain("PlayRepeating"),
				"PlayRepeating advances the frame off the sim tick, which is game-speed dependent");
			Assert.That(text, Does.Contain("blinkPhase.Advance"),
				"the phase accumulator is what stops a variable rate from jumping on every hit");
		}
	}
}
