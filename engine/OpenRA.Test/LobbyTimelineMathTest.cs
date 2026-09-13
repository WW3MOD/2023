#region Copyright & License Information
/*
 * THE MATCH TIMELINE'S ARITHMETIC AND THE ONE CONSISTENCY RULE BEHIND IT.
 *
 * Draw() is not reachable from a test, which is why the arithmetic lives in TimelineModel at all --
 * the same split DefconReadoutModel/DefconReadoutWidget already uses.
 *
 * ==== WHAT THIS FIXTURE STOPPED BEING ABOUT, AND WHY THAT IS NOT A LOSS OF COVERAGE ====
 * It used to pin SNAPPING: the bar was draggable, and a drag that produced a value outside an
 * option's enumerated set would be validated by the server as `Values.ContainsKey` and then read
 * back UNCHECKED by LobbySettingsNotification.cs:39, throwing KeyNotFoundException on the next
 * CLIENT JOIN -- the host saw a working lobby and the next player to connect was thrown out.
 *
 * The bar is read-only as of 2026-09-13 and writes no option of any kind, so there is no longer a
 * value for it to get wrong. The snapping tests were not weakened or skipped; the code they covered
 * was deleted along with the drag, and the hazard is closed structurally rather than defended.
 * The tests that remain cover what the bar still DOES: convert, position, caption, and warn.
 *
 * SCOPE, HONESTLY. This is the model's arithmetic and the consistency predicate. It does NOT prove
 * that LobbyTimelineLogic assembles the bands correctly from a live lobby -- that needs an
 * OrderManager and a resolved MapPreview, neither of which is constructible here -- and it cannot
 * check that anything is drawn. What the bar LOOKS like is a screenshot's job.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;
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
		}

		[Test]
		public void ZeroWidthAndZeroAxisDoNotDivideByZero()
		{
			Assert.That(TimelineModel.PxFromSeconds(300, 0, Width), Is.EqualTo(0));
			Assert.That(TimelineModel.PxFromSeconds(300, Axis, 0), Is.EqualTo(0));
		}

		[Test]
		public void AxisRoundsUpToALabelledTickSoTheLastStopIsAlwaysReachable()
		{
			// `timelimit` ships 0/10/20/30/40/60/90 minutes, so the largest reachable position on a
			// RULED (Skirmish) layout is 5400 s. An axis that stopped at the mockup's 3600 would draw
			// the 90-minute limit off the end of the bar.
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

		// THE ROUND TRIP THE ESCALATION BAR MAKES, end to end: a host's minutes become ticks through
		// the trait and seconds again through the widget's converter, and the bar must draw the
		// number the host picked. Both conversions are exact at 60 ms, so a drift here means one of
		// them was rewritten with the wrong tick rate.
		[Test]
		public void MinutesSurviveTheTripThroughTicksAndBackToTheBar()
		{
			const int Timestep = 60;
			var info = new DefconEscalationInfo();

			foreach (var minutes in info.NoRushOptions)
				Assert.That(TimelineModel.TicksToSeconds(info.NoRushTicks(minutes, Timestep), Timestep),
					Is.EqualTo(minutes * 60), $"the bar would draw {minutes} minutes as something else");

			foreach (var minutes in info.FirstWarheadsOptions)
				Assert.That(TimelineModel.TicksToSeconds(info.NuclearReleaseDelayTicks(minutes, Timestep), Timestep),
					Is.EqualTo(minutes * 60), $"the bar would draw {minutes} minutes as something else");
		}

		[Test]
		public void OffsetCarriesThePlusSignAndClockDoesNot()
		{
			// The plus is the user's ruling and is load-bearing: it is what tells the host the number
			// is a LENGTH rather than a time on the match clock. In the Escalation layout that is now
			// true of everything except the time limit, so the plus is what keeps the one absolute
			// number legible as the exception.
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
			Assert.That(widget.GetLayout().Bands, Is.Empty, "a fresh widget should start with no bands");
			Assert.That(widget.IsVisible(), Is.True,
				"an empty timeline must stay visible: Widget.TickOuter only ticks LogicObjects when IsVisible() is true, "
				+ "so a timeline that hid itself while empty could never be refilled by its own logic.");
		}

		// THE BAR MUST NOT EAT THE MOUSE. It is read-only, and it sits inside the scroll panel that
		// carries the option grid: a widget that took mouse focus and then did nothing would swallow
		// the wheel over its own 112px and make the list below unscrollable there, which reads as a
		// broken panel rather than as a read-only one. The fix is the ABSENCE of a HandleMouseInput
		// override — Widget's base returns false — so this pins the absence, which is the thing a
		// future edit would most plausibly undo by "adding a click handler for the tooltip".
		[Test]
		public void TheTimelineRefusesMouseInputSoTheScrollPanelKeepsIt()
		{
			var widget = new TimelineWidget();
			var click = new MouseInput(MouseInputEvent.Down, MouseButton.Left, int2.Zero, int2.Zero, Modifiers.None, 1);

			Assert.That(widget.HandleMouseInput(click), Is.False,
				"the read-only timeline claimed a click; the scroll panel underneath will never see it.");
			Assert.That(widget.HasMouseFocus, Is.False, "the read-only timeline took mouse focus.");
		}

		[Test]
		public void ABandKnowsItsOwnLengthAndNeverReportsANegativeOne()
		{
			var band = new TimelineBand(300, 900, () => "X", () => "y", Primitives.Color.White, Primitives.Color.Black);
			Assert.That(band.LengthSeconds, Is.EqualTo(600));

			// Reversed edges are reachable from a clamped layout (a band whose start is already past
			// the time limit), and the widget skips a non-positive width rather than drawing
			// backwards. A negative length here would make that check read the wrong way round.
			var reversed = new TimelineBand(900, 300, () => "X", () => "y", Primitives.Color.White, Primitives.Color.Black);
			Assert.That(reversed.LengthSeconds, Is.EqualTo(0));
		}

		// THE REGRESSION TEST FOR THE UNLABELLED BANDS (2026-09-13). The two open-ended spans used to
		// be fixed second counts, which is a SHRINKING SHARE of an axis whose other terms grow with
		// the host's clocks -- so at a 15-minute no-rush "NUCLEAR EXCHANGE" stopped being drawn.
		// What is pinned is the property that broke: every band's share of the bar stays within a
		// narrow range across the WHOLE configurable space, so a caption that fits at the default
		// fits everywhere.
		//
		// SCOPE, HONESTLY: this pins the WIDTHS, in percent of the bar. It cannot prove a caption
		// fits, because measuring needs a font and a renderer and neither is reachable from
		// OpenRA.Test. The 15 % floor is calibrated against "NUCLEAR EXCHANGE" at ~104px in the
		// 664px lobby bar; if the copy or the font changes, re-measure from a capture rather than
		// trusting this number.
		[Test]
		public void EveryBandKeepsAReadableShareOfTheBarAtEveryConfiguration()
		{
			var info = new DefconEscalationInfo();

			foreach (var noRushMinutes in info.NoRushOptions)
			{
				foreach (var warheadMinutes in info.FirstWarheadsOptions)
				{
					var noRush = noRushMinutes * 60;
					var warheads = warheadMinutes * 60;
					var configured = noRush + warheads;

					var peace = TimelineModel.IndeterminateNominalSeconds(configured);
					var exchange = TimelineModel.OpenEndedNominalSeconds(configured);

					// The full Escalation layout with a time limit set: five bands.
					var axis = noRush + peace + warheads + exchange + exchange;
					var where = $"no-rush {noRushMinutes}m / warheads {warheadMinutes}m";

					// The two OPEN-ENDED bands carry the longest captions on the bar and are the ones
					// that went blank, so they are the ones with a floor.
					Assert.That(100 * exchange / axis, Is.GreaterThanOrEqualTo(15),
						$"{where}: the nuclear and ending bands are under 15% of the bar and will not fit their captions");

					// And nothing may run away with the bar either -- a band at 60%+ squeezes every
					// other caption out, which is the same defect from the other side.
					foreach (var (name, span) in new[] { ("no-rush", noRush), ("peace", peace), ("war", warheads), ("exchange", exchange) })
						Assert.That(100 * span / axis, Is.LessThanOrEqualTo(60), $"{where}: the {name} band takes over the bar");
				}
			}
		}

		[Test]
		public void TheNominalSpansNeverCollapseToNothing()
		{
			// Degenerate and hostile inputs: a band of zero width is skipped by the widget, so a
			// share that rounded to 0 would silently delete the phase rather than draw it small.
			foreach (var configured in new[] { 0, 1, 59, 120 })
			{
				Assert.That(TimelineModel.IndeterminateNominalSeconds(configured), Is.GreaterThan(0));
				Assert.That(TimelineModel.OpenEndedNominalSeconds(configured), Is.GreaterThan(0));
			}
		}

		[Test]
		public void AnEmptyLayoutStillHasAPositiveAxis()
		{
			// Divide-by-zero insurance for PxFromSeconds, which every band position goes through.
			Assert.That(TimelineLayout.Empty.AxisSeconds, Is.GreaterThan(0));
			Assert.That(new TimelineLayout(null, 0, false, null, null).AxisSeconds, Is.GreaterThan(0));
		}
	}
}
