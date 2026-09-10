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

		// THE REGRESSION TEST FOR THE BLANK BAR (2026-09-10). LobbyTimelineLogic had
		// `IsVisible = () => markers.Length > 0`, which is self-latching: Widget.TickOuter
		// (Widget.cs:512-524) ticks a widget's LogicObjects only inside `if (IsVisible())`, so the
		// first Rebuild that came up empty made the widget invisible, which stopped its logic
		// ticking, which meant Rebuild never ran again. The bar never drew and nothing threw.
		//
		// SCOPE, HONESTLY: this pins the WIDGET's own contract — an empty timeline still reports
		// visible, so it still ticks. It cannot stop a ChromeLogic from assigning IsVisible from
		// outside, which is what actually happened; the comment at that call site is what guards
		// that. Pinning the widget is still worth it because it is the half a future edit is most
		// likely to reach for.
		[Test]
		public void AnEmptyTimelineIsStillVisibleSoItsLogicKeepsTicking()
		{
			var widget = new TimelineWidget();
			Assert.That(widget.GetMarkers(), Is.Empty, "a fresh widget should start with no markers");
			Assert.That(widget.IsVisible(), Is.True,
				"an empty timeline must stay visible: Widget.TickOuter only ticks LogicObjects when IsVisible() is true, "
				+ "so a timeline that hid itself while empty could never be refilled by its own logic.");
		}

		// ==================== CAPTION COLLISION ====================
		//
		// SCOPE, AND IT IS THE HONEST LIMIT OF THIS WHOLE SECTION: these pin the RULE — given left
		// edges, widths and priorities, which labels get drawn — and they use MEASURED WIDTHS AS DATA,
		// read off a 1.25x lobby capture (`THE LINE LIFTS` 60px, `NUKES PURCHASABLE` 101px) and
		// divided back to CSS px. They do NOT prove those widths are what `font.Measure` returns at the
		// real widget width, because measuring needs a loaded font and a renderer, and neither is
		// reachable from OpenRA.Test. So: if the fonts or the copy change, these tests keep passing and
		// the bar can collide again. **The rule is covered; the metrics are not.** The thing that would
		// catch a metrics regression is another lobby capture, and nothing here substitutes for it.
		const int LineLiftsWidth = 60;
		const int PurchasableWidth = 101;
		const int MatchEndsWidth = 45;
		const int ValueWidth = 30;

		// Priorities as TimelineWidget.CaptionPriority assigns them: highlighted 2, live 1, dimmed
		// placeholder 0. Restated rather than referenced because that method is private to the widget;
		// if it changes, this constant is the thing that should be re-read.
		const int PriorityHighlight = 2;
		const int PriorityLive = 1;
		const int PriorityPlaceholder = 0;

		static int[] Spans(params int[] centres) => centres;

		/// <summary>Left edges from centres, with the widget's own bounds clamp applied.</summary>
		static int[] LeftsFor(int[] centres, int[] widths, int width = Width)
		{
			var lefts = new int[centres.Length];
			for (var i = 0; i < centres.Length; i++)
				lefts[i] = System.Math.Clamp(centres[i] - (widths[i] / 2), 0, width - widths[i]);

			return lefts;
		}

		[Test]
		public void TheReportedDefaultCollisionDropsTheInertCaptionAndKeepsTheLiveOne()
		{
			// THE REGRESSION TEST FOR WHAT SHIPPED, in the numbers it was measured in. Markers 48px
			// apart at the DEFAULT configuration (`defcon-pace: standard` = 5:00, and
			// `nuclear-unlock-interval: 10` = 10:00), captions 60px and 101px wide. They need
			// (60+101)/2 = 80.5px of centre separation, so 48 could never work and every host saw
			// `THE LIN<GLIFTS>PURCHASABLE` on opening the lobby.
			var centres = Spans(91, 139);
			var widths = new[] { LineLiftsWidth, PurchasableWidth };
			var priorities = new[] { PriorityPlaceholder, PriorityHighlight };

			var visible = TimelineModel.VisibleLabels(LeftsFor(centres, widths), widths, priorities);

			Assert.That(visible[1], Is.True,
				"the live gold caption must survive — it is the one the bar exists to show");
			Assert.That(visible[0], Is.False,
				"the inert dimmed caption must be the one sacrificed; if both are drawn they overstrike");
		}

		[Test]
		public void TheValueRowSurvivesTheSameCollisionBecauseItIsResolvedSeparately()
		{
			// The point of resolving the two rows independently: at 48px apart the WORDS collide and the
			// TIMES do not, so the host loses "THE LINE LIFTS" and still reads `5:00` under that marker.
			// Suppressing both rows together would discard a number that was never in collision.
			var centres = Spans(87, 137);
			var widths = new[] { ValueWidth, ValueWidth };
			var priorities = new[] { PriorityPlaceholder, PriorityHighlight };

			var visible = TimelineModel.VisibleLabels(LeftsFor(centres, widths), widths, priorities);

			Assert.That(visible, Is.EqualTo(new[] { true, true }),
				"`5:00` and `10:00` are ~30px at 50px apart and must both still be drawn");
		}

		[Test]
		public void TheHighestPriorityCaptionIsDrawnInEveryConfigurationOfTheWholeRange()
		{
			// THE INVARIANT THAT MAKES SUPPRESSION SAFE, checked across the entire configurable space
			// rather than at the default: `nuclear-unlock-interval` runs 0/5/7/10/15/20 minutes — 0 puts
			// the nuclear marker nearly on top of the pace marker — against all three `defcon-pace`
			// stops. The nuclear caption may never be the one dropped, at any of them.
			var intervals = new[] { 0, 300, 420, 600, 900, 1200 };
			var widths = new[] { LineLiftsWidth, PurchasableWidth, MatchEndsWidth };
			var priorities = new[] { PriorityPlaceholder, PriorityHighlight, PriorityLive };

			foreach (var paceSeconds in PaceStops)
			{
				foreach (var intervalSeconds in intervals)
				{
					var centres = Spans(
						TimelineModel.PxFromSeconds(paceSeconds, Axis, Width),
						TimelineModel.PxFromSeconds(intervalSeconds, Axis, Width),
						Width);

					var lefts = LeftsFor(centres, widths);
					var visible = TimelineModel.VisibleLabels(lefts, widths, priorities);

					Assert.That(visible[1], Is.True,
						$"pace {paceSeconds}s / interval {intervalSeconds}s dropped the nuclear caption, " +
						"which is the highest-priority label and is placed against an empty row");

					// And nothing that IS drawn overlaps anything else that is drawn, at any of them.
					for (var a = 0; a < widths.Length; a++)
					{
						for (var b = a + 1; b < widths.Length; b++)
						{
							if (!visible[a] || !visible[b])
								continue;

							var overlaps = lefts[a] < lefts[b] + widths[b] + TimelineModel.LabelGap
								&& lefts[b] < lefts[a] + widths[a] + TimelineModel.LabelGap;

							Assert.That(overlaps, Is.False,
								$"pace {paceSeconds}s / interval {intervalSeconds}s draws labels {a} and {b} " +
								$"overlapping: [{lefts[a]}..{lefts[a] + widths[a]}] and [{lefts[b]}..{lefts[b] + widths[b]}]");
						}
					}
				}
			}
		}

		[Test]
		public void SuppressionIsByPriorityAndThenLeftToRightRatherThanByArrayOrder()
		{
			// Determinism: the same three labels in the same places must resolve the same way however
			// the caller happened to order them. A rule that fell back on array order would move which
			// caption vanished when an unrelated marker was inserted.
			var widths = new[] { 60, 60 };
			var lefts = new[] { 100, 120 };

			Assert.That(TimelineModel.VisibleLabels(lefts, widths, new[] { PriorityLive, PriorityHighlight }),
				Is.EqualTo(new[] { false, true }), "the highlighted label must win regardless of position");

			Assert.That(TimelineModel.VisibleLabels(lefts, widths, new[] { PriorityHighlight, PriorityLive }),
				Is.EqualTo(new[] { true, false }));

			// Equal priority falls to the LEFTMOST, deterministically.
			Assert.That(TimelineModel.VisibleLabels(lefts, widths, new[] { PriorityLive, PriorityLive }),
				Is.EqualTo(new[] { true, false }), "an equal-priority tie must go to the left label");
		}

		[Test]
		public void LabelsThatDoNotTouchAreAllDrawnAndDegenerateInputDoesNotThrow()
		{
			// The common case must not be a suppression: three well-separated labels all survive.
			var widths = new[] { 40, 40, 40 };
			var lefts = new[] { 0, 200, 400 };
			Assert.That(TimelineModel.VisibleLabels(lefts, widths, new[] { 0, 0, 0 }),
				Is.EqualTo(new[] { true, true, true }));

			// Exactly LabelGap apart is clear; one pixel closer is not. Pinned because an off-by-one
			// here is the difference between a readable row and a touching one.
			Assert.That(TimelineModel.VisibleLabels(new[] { 0, 40 + TimelineModel.LabelGap }, new[] { 40, 40 }, new[] { 0, 0 }),
				Is.EqualTo(new[] { true, true }), "labels exactly LabelGap apart must both be drawn");
			Assert.That(TimelineModel.VisibleLabels(new[] { 0, 40 + TimelineModel.LabelGap - 1 }, new[] { 40, 40 }, new[] { 0, 0 }),
				Is.EqualTo(new[] { true, false }), "one pixel inside LabelGap must suppress");

			// A zero-width label reserves nothing, or an empty string would suppress a real neighbour.
			Assert.That(TimelineModel.VisibleLabels(new[] { 100, 100 }, new[] { 0, 40 }, new[] { PriorityHighlight, PriorityLive }),
				Is.EqualTo(new[] { false, true }));

			Assert.That(TimelineModel.VisibleLabels(System.Array.Empty<int>(), System.Array.Empty<int>(), System.Array.Empty<int>()), Is.Empty);
			Assert.That(TimelineModel.VisibleLabels(null, null, null), Is.Empty);
			Assert.That(TimelineModel.VisibleLabels(new[] { 0 }, null, null), Is.EqualTo(new[] { false }));
			Assert.That(TimelineModel.VisibleLabels(new[] { 0, 100 }, new[] { 40 }, null), Is.EqualTo(new[] { false, false }),
				"a widths array shorter than lefts must draw nothing rather than index out of range");
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
