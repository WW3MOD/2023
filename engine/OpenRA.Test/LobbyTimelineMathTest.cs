#region Copyright & License Information
/*
 * THE MATCH TIMELINE'S SNAPPING, pinned here because getting it wrong is not a cosmetic bug.
 *
 * A lobby option's value is validated by the server as `option.Values.ContainsKey(value)`, and then
 * read back UNCHECKED by LobbySettingsNotification.cs:39 -- `option.Values[...]` on live session
 * state. So a marker that ever produced a position OUTSIDE the option's enumerated set would throw
 * KeyNotFoundException on the next client join: the host would see a working lobby and the next
 * player to connect would be thrown out. The widget defends against that by carrying an INDEX into
 * a stop list rather than a position, and this fixture is what pins the index arithmetic.
 *
 * Draw() is not reachable from a test, which is why the arithmetic lives in TimelineModel at all --
 * the same split DefconReadoutModel/DefconReadoutWidget already uses.
 *
 * SCOPE, HONESTLY. This is the model's arithmetic only. It does NOT prove that the stop lists
 * LobbyTimelineLogic builds are drawn from the real options' Values dictionaries -- that binding is
 * checked by reading the trait, and by the fact that an unknown value would be refused by the
 * server rather than silently accepted.
 */
#endregion

using System.Linq;
using NUnit.Framework;
using OpenRA.Mods.Common.Widgets;

namespace OpenRA.Test
{
	[TestFixture]
	public class LobbyTimelineMathTest
	{
		const int Axis = 3600;
		const int Width = 880;

		[TestCase(0, 0)]
		[TestCase(1800, 440)]
		[TestCase(3600, 880)]
		public void PxFromSecondsIsLinearAcrossTheAxis(int seconds, int expected)
		{
			Assert.That(TimelineModel.PxFromSeconds(seconds, Axis, Width), Is.EqualTo(expected));
		}

		[Test]
		public void PositionsOutsideTheAxisAreClampedRatherThanExtrapolated()
		{
			Assert.That(TimelineModel.PxFromSeconds(-600, Axis, Width), Is.EqualTo(0));
			Assert.That(TimelineModel.PxFromSeconds(Axis * 2, Axis, Width), Is.EqualTo(Width));
			Assert.That(TimelineModel.SecondsFromPx(-40, Axis, Width), Is.EqualTo(0));
			Assert.That(TimelineModel.SecondsFromPx(Width * 2, Axis, Width), Is.EqualTo(Axis));
		}

		[Test]
		public void ZeroWidthAndZeroAxisDoNotDivideByZero()
		{
			Assert.That(TimelineModel.PxFromSeconds(300, 0, Width), Is.EqualTo(0));
			Assert.That(TimelineModel.PxFromSeconds(300, Axis, 0), Is.EqualTo(0));
			Assert.That(TimelineModel.SecondsFromPx(100, Axis, 0), Is.EqualTo(0));
		}

		// The real `defcon-pace` stop set: FastTicks/StandardTicks/SlowTicks are 2500/5000/9000 at
		// the mod's 60 ms timestep, i.e. 150 / 300 / 540 seconds. Unevenly spaced on purpose, which
		// is exactly why nearest-in-pixels is not the same answer as nearest-in-seconds.
		static readonly int[] PaceStops = { 150, 300, 540 };

		[TestCase(0, 0)]
		[TestCase(37, 0)]
		[TestCase(55, 1)]
		[TestCase(73, 1)]
		[TestCase(103, 2)]
		[TestCase(880, 2)]
		public void NearestStopSnapsToTheClosestDrawnStop(int px, int expected)
		{
			Assert.That(TimelineModel.NearestStop(px, PaceStops, Axis, Width), Is.EqualTo(expected));
		}

		[Test]
		public void NearestStopAlwaysReturnsAValidIndex()
		{
			foreach (var px in Enumerable.Range(-50, Width + 100))
			{
				var index = TimelineModel.NearestStop(px, PaceStops, Axis, Width);
				Assert.That(index, Is.InRange(0, PaceStops.Length - 1),
					$"px {px} produced index {index}, which is not a stop — the widget would write a value the server refuses.");
			}
		}

		[Test]
		public void NearestStopTieGoesToTheLowerIndexRegardlessOfListOrder()
		{
			// Two stops equidistant from the midpoint between them.
			var stops = new[] { 600, 1200 };
			var midPx = (TimelineModel.PxFromSeconds(600, Axis, Width) + TimelineModel.PxFromSeconds(1200, Axis, Width)) / 2;
			Assert.That(TimelineModel.NearestStop(midPx, stops, Axis, Width), Is.EqualTo(0));
		}

		[Test]
		public void AnEmptyStopListDoesNotThrow()
		{
			Assert.That(TimelineModel.NearestStop(400, System.Array.Empty<int>(), Axis, Width), Is.EqualTo(0));
		}

		[Test]
		public void AxisRoundsUpToALabelledTickSoTheLastStopIsAlwaysReachable()
		{
			// `timelimit` ships 0/10/20/30/40/60/90 minutes, so the largest reachable position is
			// 5400 s. An axis that stopped at the mockup's 3600 would put 90 minutes off the end of
			// the bar and make it unreachable by drag — a REGRESSION against the dropdown it replaces.
			Assert.That(TimelineModel.AxisSecondsFor(new[] { 600, 1800, 5400 }, 3600), Is.EqualTo(5400));

			// Rounds up rather than truncating.
			Assert.That(TimelineModel.AxisSecondsFor(new[] { 3601 }, 3600), Is.EqualTo(4200));

			// The minimum holds when every stop is small.
			Assert.That(TimelineModel.AxisSecondsFor(new[] { 150, 300 }, 3600), Is.EqualTo(3600));

			// Degenerate input still yields a positive axis, so nothing downstream divides by zero.
			Assert.That(TimelineModel.AxisSecondsFor(System.Array.Empty<int>(), 0), Is.EqualTo(TimelineModel.TickSeconds));
			Assert.That(TimelineModel.AxisSecondsFor(null, 0), Is.EqualTo(TimelineModel.TickSeconds));
		}

		// 60 ms per tick, i.e. 16.67 ticks/s — NOT 25 and NOT 40. See conventions.md; this repo has
		// assumed the wrong rate at eleven sites and the 1.5x error looks plausible every time.
		[TestCase(5000, 300)]
		[TestCase(10000, 600)]
		[TestCase(2500, 150)]
		[TestCase(9000, 540)]
		public void TicksConvertAtTheDefaultSixtyMillisecondTimestep(int ticks, int expected)
		{
			Assert.That(TimelineModel.TicksToSeconds(ticks, 60), Is.EqualTo(expected));
		}

		[Test]
		public void TicksToSecondsDoesNotDivideByZero()
		{
			Assert.That(TimelineModel.TicksToSeconds(5000, 0), Is.EqualTo(0));
		}

		[Test]
		public void OffsetCarriesThePlusSignAndClockDoesNot()
		{
			// The plus is the user's ruling and is load-bearing: it is what tells the host the number
			// is a GAP from the previous marker rather than a time on the match clock.
			Assert.That(TimelineModel.Offset(600), Is.EqualTo("+10:00"));
			Assert.That(TimelineModel.Clock(600), Is.EqualTo("10:00"));
			Assert.That(TimelineModel.Clock(150), Is.EqualTo("2:30"));

			// The minute field does NOT roll over into hours. WidgetUtils.FormatTimeSeconds does
			// (WidgetUtils.cs:235-236) and captioned a 60-minute limit "1:00:00", which contradicts
			// the axis label "60 min" sitting directly under it. Both ends of the shipped `timelimit`
			// set are pinned here because 90 is the axis end and 60 is the rollover boundary.
			Assert.That(TimelineModel.Clock(3600), Is.EqualTo("60:00"));
			Assert.That(TimelineModel.Clock(5400), Is.EqualTo("90:00"));
		}
	}
}
