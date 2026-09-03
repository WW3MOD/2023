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

using System.Collections.Generic;
using NUnit.Framework;
using OpenRA.Mods.Common.Tournament;

namespace OpenRA.Test
{
	/// <summary>
	/// Pins the Option 4.D (verdict_version 6) capture-contest metrics — the pure helpers behind
	/// BotVsBotMatchWatcher's observation-only telemetry. PoiEventClassifier labels each ownership
	/// transition; PoiRollup reduces the event stream into the per-side H2 (time-to-first-capture)
	/// discriminator + event tallies. No World/Actor setup needed.
	/// </summary>
	[TestFixture]
	public class PoiCaptureMetricsTest
	{
		// ---- PoiEventClassifier ----

		[Test]
		public void NeutralToBotIsCapture()
		{
			// oldWasNeutral, no prior ownership → capture.
			Assert.That(PoiEventClassifier.Classify(true, false, false), Is.EqualTo(PoiEventClassifier.Capture));
		}

		[Test]
		public void BotToDifferentBotIsSteal()
		{
			// old is a tracked bot != new, new never owned this POI → steal.
			Assert.That(PoiEventClassifier.Classify(false, false, true), Is.EqualTo(PoiEventClassifier.Steal));
		}

		[Test]
		public void ReturningPriorOwnerIsRecapture()
		{
			// new previously owned this POI → recapture, regardless of who held it in between.
			Assert.That(PoiEventClassifier.Classify(false, true, true), Is.EqualTo(PoiEventClassifier.Recapture));
		}

		[Test]
		public void RecaptureWinsOverStealAndCapture()
		{
			// Precedence guard: newPreviouslyOwned dominates the other flags (documented
			// recapture > capture > steal ordering — the Option B churn signal).
			Assert.That(PoiEventClassifier.Classify(true, true, true), Is.EqualTo(PoiEventClassifier.Recapture));
			Assert.That(PoiEventClassifier.Classify(false, true, false), Is.EqualTo(PoiEventClassifier.Recapture));
		}

		[Test]
		public void UnexpectedNonNeutralNonTrackedDefaultsToCapture()
		{
			// Conservative default: never happens in the WW3MOD model, must not throw / mislabel.
			Assert.That(PoiEventClassifier.Classify(false, false, false), Is.EqualTo(PoiEventClassifier.Capture));
		}

		// ---- PoiRollup ----

		static PoiCaptureEvent Ev(int tick, int oldOwner, int newOwner, string evt)
		{
			return new PoiCaptureEvent { Tick = tick, PoiId = 1, PoiType = "oilb", OldOwner = oldOwner, NewOwner = newOwner, Event = evt };
		}

		[Test]
		public void EmptyStreamHasNoFirstCapture()
		{
			var r = PoiRollup.Compute(new List<PoiCaptureEvent>(), 0);
			Assert.That(r.FirstCaptureTick, Is.EqualTo(-1));
			Assert.That(r.Captures, Is.EqualTo(0));
			Assert.That(r.Losses, Is.EqualTo(0));
		}

		[Test]
		public void NullStreamIsSafe()
		{
			var r = PoiRollup.Compute(null, 0);
			Assert.That(r.FirstCaptureTick, Is.EqualTo(-1));
		}

		[Test]
		public void FirstCaptureTickIsEarliestGain()
		{
			// Player 0 captures at 100, steals another at 300 → first-capture = 100.
			var events = new List<PoiCaptureEvent>
			{
				Ev(100, -1, 0, PoiEventClassifier.Capture),
				Ev(300, 1, 0, PoiEventClassifier.Steal),
			};

			var r = PoiRollup.Compute(events, 0);
			Assert.That(r.FirstCaptureTick, Is.EqualTo(100));
			Assert.That(r.Captures, Is.EqualTo(1));
			Assert.That(r.Steals, Is.EqualTo(1));
		}

		[Test]
		public void DestroyedIsNotAFirstCapture()
		{
			// A destroyed event with new_owner = player must NOT set time-to-first-capture
			// (new_owner is -1 for destroyed in practice, but guard the label directly too).
			var events = new List<PoiCaptureEvent>
			{
				Ev(50, 0, 0, PoiEventClassifier.Destroyed),
			};

			var r = PoiRollup.Compute(events, 0);
			Assert.That(r.FirstCaptureTick, Is.EqualTo(-1));
		}

		[Test]
		public void StealCountsAsLossForTheVacatingSide()
		{
			// Player 0 captures (t100); player 1 steals it from 0 (t300). From 0's view: 1 capture,
			// 1 loss. From 1's view: 1 steal, no loss, first-capture at 300.
			var events = new List<PoiCaptureEvent>
			{
				Ev(100, -1, 0, PoiEventClassifier.Capture),
				Ev(300, 0, 1, PoiEventClassifier.Steal),
			};

			var r0 = PoiRollup.Compute(events, 0);
			Assert.That(r0.Captures, Is.EqualTo(1));
			Assert.That(r0.Losses, Is.EqualTo(1));
			Assert.That(r0.Steals, Is.EqualTo(0));

			var r1 = PoiRollup.Compute(events, 1);
			Assert.That(r1.Steals, Is.EqualTo(1));
			Assert.That(r1.Losses, Is.EqualTo(0));
			Assert.That(r1.FirstCaptureTick, Is.EqualTo(300));
		}

		[Test]
		public void RecaptureChurnTallies()
		{
			// Option B churn: 0 captures (t100), 1 steals (t200), 0 recaptures (t300).
			// 0: captures=1, recaptures=1, losses=1 (lost at t200). 1: steals=1, losses=1 (lost at t300).
			var events = new List<PoiCaptureEvent>
			{
				Ev(100, -1, 0, PoiEventClassifier.Capture),
				Ev(200, 0, 1, PoiEventClassifier.Steal),
				Ev(300, 1, 0, PoiEventClassifier.Recapture),
			};

			var r0 = PoiRollup.Compute(events, 0);
			Assert.That(r0.Captures, Is.EqualTo(1));
			Assert.That(r0.Recaptures, Is.EqualTo(1));
			Assert.That(r0.Losses, Is.EqualTo(1));
			Assert.That(r0.FirstCaptureTick, Is.EqualTo(100));

			var r1 = PoiRollup.Compute(events, 1);
			Assert.That(r1.Steals, Is.EqualTo(1));
			Assert.That(r1.Losses, Is.EqualTo(1));
		}

		[Test]
		public void DestructionCountsAsLossForLastOwner()
		{
			// Derrick blown up while player 0 held it → 0 records a loss, nobody gains.
			var events = new List<PoiCaptureEvent>
			{
				Ev(100, -1, 0, PoiEventClassifier.Capture),
				Ev(400, 0, -1, PoiEventClassifier.Destroyed),
			};

			var r0 = PoiRollup.Compute(events, 0);
			Assert.That(r0.Captures, Is.EqualTo(1));
			Assert.That(r0.Losses, Is.EqualTo(1));
		}

		[Test]
		public void EvictionToNeutralChargesTheHolderALossAndCreditsNobody()
		{
			// Mirrors BotVsBotMatchWatcher.cs:367-370 and :436 — the classifier inputs and the
			// OwnerIndex mapping are reproduced here, not exercised through the watcher.
			// Soldiers evict a POI's owner without taking it (CaptureToNeutral): the structure
			// drops to Neutral, so the watcher records OldOwner = holder, NewOwner = -1
			// (OwnerIndex maps every NonCombatant to -1). The classifier only sees
			// (oldWasNeutral: false, newPreviouslyOwned: false, oldWasTrackedBot: true) and so
			// labels it `steal` — misleading in the raw event stream, but it cannot move a
			// tally, because no tracked side is keyed -1 and the gain branch never fires.
			Assert.That(PoiEventClassifier.Classify(false, false, true), Is.EqualTo(PoiEventClassifier.Steal));

			var events = new List<PoiCaptureEvent>
			{
				Ev(100, -1, 0, PoiEventClassifier.Capture),
				Ev(300, 0, -1, PoiEventClassifier.Steal),
			};

			var r0 = PoiRollup.Compute(events, 0);
			Assert.That(r0.Captures, Is.EqualTo(1));
			Assert.That(r0.Losses, Is.EqualTo(1));
			Assert.That(r0.Steals, Is.EqualTo(0));

			var r1 = PoiRollup.Compute(events, 1);
			Assert.That(r1.Steals, Is.EqualTo(0));
			Assert.That(r1.Losses, Is.EqualTo(0));
			Assert.That(r1.FirstCaptureTick, Is.EqualTo(-1));
		}

		[Test]
		public void RetakingAnEvictedPoiClassifiesFromNeutral()
		{
			// Mirrors BotVsBotMatchWatcher.cs:367-370 and :436.
			// After an eviction the POI is Neutral, so whoever's technician walks in next is
			// classified against a neutral predecessor: a fresh owner captures, the evicted
			// owner recaptures. Neutral is never a tracked bot, so it is never charged a loss.
			Assert.That(PoiEventClassifier.Classify(true, false, false), Is.EqualTo(PoiEventClassifier.Capture));
			Assert.That(PoiEventClassifier.Classify(true, true, false), Is.EqualTo(PoiEventClassifier.Recapture));

			var events = new List<PoiCaptureEvent>
			{
				Ev(100, -1, 0, PoiEventClassifier.Capture),
				Ev(300, 0, -1, PoiEventClassifier.Steal),
				Ev(500, -1, 1, PoiEventClassifier.Capture),
			};

			var r1 = PoiRollup.Compute(events, 1);
			Assert.That(r1.Captures, Is.EqualTo(1));
			Assert.That(r1.Losses, Is.EqualTo(0));
			Assert.That(r1.FirstCaptureTick, Is.EqualTo(500));

			var r0 = PoiRollup.Compute(events, 0);
			Assert.That(r0.Losses, Is.EqualTo(1));
		}

		[Test]
		public void EveryEventIsCreditedToExactlyOneSide()
		{
			// REGRESSION SHAPE — the ClientIndex misattribution. Owner fields used to be
			// Player.ClientIndex, which every map-player bot inherits from the host (Player.cs:191),
			// so both bots keyed 0 and each side's rollup absorbed the other's captures. The
			// signature is a conservation failure: per-side tallies sum to more than the events
			// that actually happened. Note this pins the CONSUMER's behaviour given a collapsed
			// key; the producer is guarded at runtime in BotVsBotMatchWatcher.DiscoverSrsOnFirstTick,
			// because Player needs a World and cannot be constructed here.
			var collapsed = new List<PoiCaptureEvent>
			{
				Ev(100, -1, 0, PoiEventClassifier.Capture),
				Ev(300, -1, 0, PoiEventClassifier.Capture),
			};

			Assert.That(PoiRollup.Compute(collapsed, 0).Captures, Is.EqualTo(2),
				"a shared key merges both sides into a single tally");

			var distinct = new List<PoiCaptureEvent>
			{
				Ev(100, -1, 0, PoiEventClassifier.Capture),
				Ev(300, -1, 1, PoiEventClassifier.Capture),
			};

			var d0 = PoiRollup.Compute(distinct, 0);
			var d1 = PoiRollup.Compute(distinct, 1);
			Assert.That(d0.Captures, Is.EqualTo(1));
			Assert.That(d1.Captures, Is.EqualTo(1));
			Assert.That(d0.Captures + d1.Captures, Is.EqualTo(distinct.Count),
				"with distinct keys every capture is credited exactly once across sides");
			Assert.That(d0.FirstCaptureTick, Is.EqualTo(100));
			Assert.That(d1.FirstCaptureTick, Is.EqualTo(300));
		}
	}
}
