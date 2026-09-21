#region Copyright & License Information
/*
 * THE DEFCON READOUT: the approved copy, the visibility rules and the tick arithmetic.
 *
 * THE LOAD-BEARING TESTS HERE ARE RuleLinesAreApprovedCopy AND TheStepsFollowTheEnumOrder.
 *
 * The first because the three rule lines are the feature. The mockup's own note says it outright --
 * "these three lines do more work than the graphics do" -- and they are the one part of this work
 * that a later reader is most likely to "improve" toward the mechanism, because the mechanism is
 * what the code around them is about. Each line states what the player may and may not do; none
 * mentions a stance, an AttackSource or a condition. Asserting them to the character is the only
 * way that survives someone tidying the file.
 *
 * The second because the nuclear ladder HAS ALREADY CHANGED SHAPE ONCE: the ruling of 2026-09-10
 * split 50/100 kt into two rungs and renumbered GameEnder from 4 to 5. A readout carrying its own
 * copy of the rung list would not have failed anything when that happened -- it would have gone on
 * drawing four boxes with every label one rung out of place, which is a readout that lies. Deriving
 * the steps from the enum and pinning that derivation here is what makes the next renumber a test
 * failure instead.
 *
 * Nothing here constructs a Widget. DefconReadoutModel is a plain class for exactly that reason,
 * stated in its own header: OpenRA.Test can construct neither a World nor a Renderer, so the
 * alternative to this split is asserting nothing at all about what the player reads.
 */
#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Common.Widgets;

namespace OpenRA.Test
{
	[TestFixture]
	public class DefconReadoutTest
	{
		[Test]
		public void RuleLinesAreApprovedCopy()
		{
			// Verbatim from the wording table in WORKSPACE/mockups/defcon-hud-directions.html, EXCEPT
			// at DEFCON 3. That line gained its second clause on 2026-09-19 because the phase gained a
			// second rule three days earlier -- 5fef37dc gates Armament.CanFire at the Positioning
			// level, so nothing fires there by any path, and the table's line described only the
			// border. RuleLine's own comment carries the argument. Do not "restore" the table's
			// wording: it would put the HUD back to stating one of the two rules in force.
			Assert.That(DefconReadoutModel.RuleLine(3), Is.EqualTo("The border is closed. Nothing may cross it, and nothing may fire."));
			Assert.That(DefconReadoutModel.RuleLine(2), Is.EqualTo("Your units will not fire on their own. Every shot is one you order."));
			Assert.That(DefconReadoutModel.RuleLine(1), Is.EqualTo("Everything is released. Units engage on sight."));
		}

		[Test]
		public void LevelNamesAreApprovedCopy()
		{
			Assert.That(DefconReadoutModel.LevelName(3), Is.EqualTo("Positioning"));
			Assert.That(DefconReadoutModel.LevelName(2), Is.EqualTo("Weapons free"));
			Assert.That(DefconReadoutModel.LevelName(1), Is.EqualTo("Open war"));
		}

		[Test]
		public void TheRuleLinesNameNoMechanism()
		{
			// The rule the copy was written to: each line says what the player can and cannot do right
			// now, never the machinery that makes it so. These four words are the ones the traits
			// behind this feature actually use, so they are the ones a well-meaning edit would reach
			// for. The BANNER copy is deliberately exempt -- "Autonomous fire is released" is approved
			// verbatim and is a statement about the world rather than about a trait.
			var forbidden = new[] { "stance", "condition", "autonomous", "target" };

			for (var level = DefconEscalationState.Floor; level <= DefconEscalationState.Ceiling; level++)
			{
				var line = DefconReadoutModel.RuleLine(level).ToLowerInvariant();
				foreach (var word in forbidden)
					Assert.That(line, Does.Not.Contain(word), $"the DEFCON {level} rule line names the mechanism.");
			}
		}

		[Test]
		public void SkirmishDrawsNothing()
		{
			// The strict-no-op rule reaching the screen. Skirmish leaves the level at NoLevel, so the
			// readout asks the LEVEL rather than the mode and gets the right answer for free.
			Assert.That(DefconReadoutModel.IsVisible(DefconEscalationState.NoLevel), Is.False);
			Assert.That(DefconReadoutModel.LevelName(DefconEscalationState.NoLevel), Is.Null);
			Assert.That(DefconReadoutModel.RuleLine(DefconEscalationState.NoLevel), Is.Null);
			Assert.That(DefconReadoutModel.ShowsClock(DefconGameMode.Skirmish, DefconEscalationState.NoLevel), Is.False);
			Assert.That(DefconReadoutModel.ShowsTrigger(DefconGameMode.Skirmish, DefconEscalationState.NoLevel), Is.False);
			Assert.That(DefconReadoutModel.ShowsNuclear(DefconGameMode.Skirmish, DefconEscalationState.NoLevel, true), Is.False);

			for (var level = DefconEscalationState.Floor; level <= DefconEscalationState.Ceiling; level++)
				Assert.That(DefconReadoutModel.IsVisible(level), Is.True, $"DEFCON {level} is not drawn at all.");
		}

		[Test]
		public void OnlyTheThreeToTwoTransitionHasAClock()
		{
			// The one thing every direction in the mockup had to teach: 3 -> 2 is a clock and 2 -> 1 is
			// an event. Drawn the same way, the player sits waiting for a bar that never fills.
			Assert.That(DefconReadoutModel.ShowsClock(DefconGameMode.Escalation, 3), Is.True);
			Assert.That(DefconReadoutModel.ShowsClock(DefconGameMode.Escalation, 2), Is.False);
			Assert.That(DefconReadoutModel.ShowsClock(DefconGameMode.Escalation, 1), Is.False);

			// Sandbox is PINNED at its configured level, so even DEFCON 3 has nothing to count toward.
			Assert.That(DefconReadoutModel.ShowsClock(DefconGameMode.Sandbox, 3), Is.False);
		}

		[Test]
		public void TheClockFieldIsNeverBlank()
		{
			// LOAD-BEARING, NOT COSMETIC. The player has just spent minutes watching a countdown in
			// that exact spot; the emptied slot is what teaches them the rung ended. An edit that
			// "simplified" this to an empty string would delete the teaching and leave the layout
			// looking identical.
			Assert.That(DefconReadoutModel.NoClock, Is.Not.Null.And.Not.Empty);
			Assert.That(DefconReadoutModel.NoClock.Trim(), Is.Not.Empty);
		}

		[Test]
		public void TheTriggerLineIsShownExactlyWhereTheTriggerWorks()
		{
			// Pinned to the trait that implements it rather than to a literal 2, so a renumbered ladder
			// moves both together.
			Assert.That(DefconReadoutModel.ShowsTrigger(DefconGameMode.Escalation, DefconFireDiscipline.HoldFireLevel), Is.True);
			Assert.That(DefconReadoutModel.ShowsTrigger(DefconGameMode.Escalation, DefconEscalationState.Ceiling), Is.False);
			Assert.That(DefconReadoutModel.ShowsTrigger(DefconGameMode.Escalation, DefconEscalationState.Floor), Is.False);

			// A CASUALTY MOVES NOTHING IN SANDBOX (DefconEscalationState.ReportCasualty), so promising
			// the player that the first kill ends the phase would be a lie there.
			Assert.That(DefconReadoutModel.ShowsTrigger(DefconGameMode.Sandbox, DefconFireDiscipline.HoldFireLevel), Is.False);
			Assert.That(DefconReadoutModel.ShowsTrigger(DefconGameMode.Skirmish, DefconFireDiscipline.HoldFireLevel), Is.False);
		}

		[Test]
		public void LedgerBandsFollowTheEnumOrder()
		{
			// THIS IS TheStepsFollowTheEnumOrder, MOVED. The nuclear block used to draw ONE row of six
			// boxes -- Hold included -- for the viewer alone. The 2026-09-13 ledger replaced it with
			// one row PER SIDE, and a row lists what a side HOLDS, so Hold is not a box. The guarantee
			// the old test existed for is unchanged and is why this one is here: the ladder renumbered
			// once already (2026-09-10 split 50/100 kt and moved GameEnder from 4 to 5), and a
			// hand-written list of bands would not have failed anything -- it would have gone on
			// drawing five boxes with every label one rung out of place, which is a ledger that lies
			// rather than one that fails.
			var rungs = (NuclearRung[])Enum.GetValues(typeof(NuclearRung));
			var bands = DefconReadoutModel.LedgerBands();

			Assert.That(bands.Count, Is.EqualTo(rungs.Length - 1),
				"the ledger draws a different number of boxes than the ladder has firable rungs.");

			// FIVE BOXES: 1 kt, 20 kt, 50 kt, 100 kt, END.
			Assert.That(bands.Count, Is.EqualTo(5));

			Assert.That(bands, Has.No.Member(NuclearRung.Hold),
				"Hold is not something a side holds -- it is the absence of it.");

			for (var i = 0; i < rungs.Length; i++)
				Assert.That((int)rungs[i], Is.EqualTo(i),
					$"{rungs[i]} is not at index {i} -- the enum has been renumbered and the ledger indexes it positionally.");

			for (var i = 0; i < bands.Count; i++)
				Assert.That((int)bands[i], Is.EqualTo(i + 1),
					$"{bands[i]} is not one above index {i}; the ledger's boxes are off by a rung.");

			Assert.That(DefconReadoutModel.LedgerRungLabel(NuclearRung.FiftyKiloton), Is.EqualTo("50kt"));
		}

		[Test]
		public void TheTopBandIsLabelledAsAnEndingAndNotAsAYield()
		{
			// Every other box is a yield the player weighs against another yield. The top one is not
			// a bigger bomb -- decision 01 makes it reachable ONLY as a grant, and firing it ends the
			// match -- so a number there invites it to be read as one more step up the same scale.
			Assert.That(DefconReadoutModel.LedgerRungLabel(NuclearRung.GameEnder), Is.EqualTo("END"));

			// The full-name label is untouched: it is the one the value slot draws, where the yield
			// IS the information.
			Assert.That(DefconReadoutModel.RungLabel((int)NuclearRung.GameEnder), Is.EqualTo("200 kt+"));
		}

		[Test]
		public void EveryBandHasALedgerLabel()
		{
			foreach (var band in DefconReadoutModel.LedgerBands())
				Assert.That(DefconReadoutModel.LedgerRungLabel(band), Is.Not.Null.And.Not.Empty,
					$"{band} has no ledger label, so its box would draw empty.");
		}

		[Test]
		public void NothingIsLitBeforeTheGateOpens()
		{
			// The block IS drawn before release -- it carries RELEASE IN m:ss, which is the ten-minute
			// wait the mode used to serve with a blank screen. Every box under that countdown must be
			// dark, on BOTH rows: a lit box beneath a countdown to the moment boxes light contradicts
			// the largest text in the panel.
			foreach (var band in DefconReadoutModel.LedgerBands())
				Assert.That(
					DefconReadoutModel.CellFor(band, NuclearReleaseLadder.Highest, false, false),
					Is.EqualTo(DefconReadoutModel.LedgerCell.Dark),
					$"{band} is lit before release.");
		}

		[Test]
		public void ReleaseLightsExactlyTheOneKilotonBox()
		{
			// The state both sides are in the instant the gate opens: level = Kiloton, no cooldown.
			// One lit box each and four dark ones, which is the position the whole match is then
			// measured against.
			var cells = new List<DefconReadoutModel.LedgerCell>();
			foreach (var band in DefconReadoutModel.LedgerBands())
				cells.Add(DefconReadoutModel.CellFor(band, (int)NuclearRung.Kiloton, true, false));

			Assert.That(cells[0], Is.EqualTo(DefconReadoutModel.LedgerCell.Held));
			for (var i = 1; i < cells.Count; i++)
				Assert.That(cells[i], Is.EqualTo(DefconReadoutModel.LedgerCell.Dark),
					$"band index {i} is not dark at release.");
		}

		[Test]
		public void EveryBandAtOrBelowTheLevelIsLitAndNothingAboveIt()
		{
			// THE RATCHET, DRAWN. A side at 50 kt holds 1 kt, 20 kt and 50 kt -- levels are cumulative
			// and the ledger is what makes them countable. v1 needed a fourth cell here because a
			// retaliation grant could light ONE box above the permanent level; there is no such box in
			// v2, and a ledger with a gap in it would be a model nobody has.
			var cells = new List<DefconReadoutModel.LedgerCell>();
			foreach (var band in DefconReadoutModel.LedgerBands())
				cells.Add(DefconReadoutModel.CellFor(band, (int)NuclearRung.FiftyKiloton, true, false));

			for (var i = 0; i < 3; i++)
				Assert.That(cells[i], Is.EqualTo(DefconReadoutModel.LedgerCell.Held),
					$"band index {i} is not lit at level 3.");

			for (var i = 3; i < cells.Count; i++)
				Assert.That(cells[i], Is.EqualTo(DefconReadoutModel.LedgerCell.Dark),
					$"band index {i} is lit above the level.");
		}

		[Test]
		public void ACooldownDimsTheWHOLEROWAndNotOneBox()
		{
			// THE v2 CHANGE, AND THE ONE A READER WILL EXPECT TO BE WRONG. Under v1 a shot muted the
			// BAND it was fired from and left the others lit; a side could be drawn holding three
			// ready warheads. One cooldown now covers every band the side holds, which is exactly why
			// the clock moved out of the boxes and onto the row.
			foreach (var band in new[] { NuclearRung.Kiloton, NuclearRung.TwentyKiloton, NuclearRung.FiftyKiloton })
				Assert.That(DefconReadoutModel.CellFor(band, (int)NuclearRung.FiftyKiloton, true, true),
					Is.EqualTo(DefconReadoutModel.LedgerCell.Charging),
					$"{band} stayed lit while the side was on cooldown");

			// AND A BAND ABOVE THE LEVEL IS STILL DARK RATHER THAN CHARGING. "Not held" and "held but
			// reloading" are different facts and the row draws them differently; a cooldown must not
			// promote a band the side does not have into one it is merely waiting for.
			Assert.That(DefconReadoutModel.CellFor(NuclearRung.GameEnder, (int)NuclearRung.FiftyKiloton, true, true),
				Is.EqualTo(DefconReadoutModel.LedgerCell.Dark));
		}

		[Test]
		public void AReloadingBandIsNotDrawnAsAReadyOne()
		{
			// Held and Charging are the same PERMISSION and different FACTS, and the difference is
			// the entire value of putting the enemy's row on screen: a side whose arsenal is coming
			// back in forty seconds is not covered right now, and a ledger drawing them lit would say
			// they were.
			Assert.That(DefconReadoutModel.CellFor(NuclearRung.Kiloton, (int)NuclearRung.Kiloton, true, true),
				Is.EqualTo(DefconReadoutModel.LedgerCell.Charging));

			Assert.That(DefconReadoutModel.CellFor(NuclearRung.Kiloton, (int)NuclearRung.Kiloton, true, false),
				Is.EqualTo(DefconReadoutModel.LedgerCell.Held));
		}

		[Test]
		public void TheRowsClockSaysREADYRatherThanZero()
		{
			// "READY" AND NOT "0:00" OR AN EM DASH. The dash is this panel's other convention and it
			// means "this rung ends on an event rather than a clock" -- an ABSENCE of a countdown.
			// Here the countdown has RUN OUT, which is the opposite fact and the one the player has
			// been waiting for, so it is worth a word. A row reading 0:00 forever would also be a
			// readout inventing a countdown, which is the fault the -1 rule in v1 existed to avoid.
			Assert.That(DefconReadoutModel.LedgerReady, Is.EqualTo("READY"));
			Assert.That(DefconReadoutModel.LedgerReady, Is.Not.EqualTo(DefconReadoutModel.NoClock));
			Assert.That(DefconReadoutModel.LedgerReady, Does.Not.Contain(":"));
		}

		[Test]
		public void AnObserverIsNeverToldThatASideIsThem()
		{
			// "YOU" is the shortest thing that makes a row countable at a glance, and it is exactly the
			// word that is a LIE on an observer's screen: there is no local player there to be "you",
			// and an observer following Russia would otherwise read that Russia is them.
			Assert.That(DefconReadoutModel.LedgerRowLabel(true, true, "USA"), Is.EqualTo("YOU"));
			Assert.That(DefconReadoutModel.LedgerRowLabel(true, false, "Russia"), Is.EqualTo("ENEMY"));

			Assert.That(DefconReadoutModel.LedgerRowLabel(false, true, "USA"), Is.EqualTo("USA"));
			Assert.That(DefconReadoutModel.LedgerRowLabel(false, false, "Russia"), Is.EqualTo("RUSSIA"));

			// A side with no name at all -- nothing registered, or a stripped trait -- falls back to
			// the pair rather than drawing a blank row label.
			Assert.That(DefconReadoutModel.LedgerRowLabel(false, true, null), Is.EqualTo("YOU"));
			Assert.That(DefconReadoutModel.LedgerRowLabel(false, false, ""), Is.EqualTo("ENEMY"));
		}

		[Test]
		public void TheArmedBannerNamesBothOptionsAndUsesAProvenGlyph()
		{
			var line = DefconReadoutModel.ArmedBannerLine((int)NuclearRung.TwentyKiloton);

			Assert.That(DefconReadoutModel.ArmedBannerTitle, Is.EqualTo("ESCALATED"));
			Assert.That(line, Is.EqualTo("20 kt now available"));

			// NO CLOCK AND NO DEADLINE, WHICH IS THE v2 CHANGE. This read "20 kt available for 3:00 —
			// reply or hold" because a retaliation grant expired; a LEVEL does not, so a banner that
			// counted down would be the HUD urging the player to take an escalation the whole design
			// is trying to make a deliberate choice. Pinned as an absence because the natural
			// "improvement" is to put a number back in a line that has room for one.
			Assert.That(line, Does.Not.Contain(":"), "the escalation banner has grown a countdown");
			Assert.That(line, Does.Not.Contain("reply"));

			// THE TITLE IS NOT "ARMED". A level rise is not a weapon being handed over for a minute --
			// it is the match's ceiling moving, permanently -- and the word is the ruling.
			Assert.That(DefconReadoutModel.ArmedBannerTitle, Is.Not.EqualTo("ARMED"));
		}

		[Test]
		public void TheReleaseBannerSaysWhatBothSidesGot()
		{
			Assert.That(DefconReadoutModel.NuclearReleaseBannerTitle, Is.EqualTo("NUCLEAR RELEASE"));
			Assert.That(DefconReadoutModel.NuclearReleaseBannerLine, Is.EqualTo("1 kt available to both sides"));

			// BOTH SIDES, SAID OUT LOUD. The release is simultaneous and symmetric, and a banner that
			// said only "1 kt available" would read as a private advantage on the tick the match stops
			// being conventional.
			Assert.That(DefconReadoutModel.NuclearReleaseBannerLine, Does.Contain("both sides"));
		}

		[Test]
		public void EveryRungHasBothLabels()
		{
			foreach (NuclearRung rung in Enum.GetValues(typeof(NuclearRung)))
			{
				Assert.That(DefconReadoutModel.ShortRungLabel(rung), Is.Not.Null.And.Not.Empty,
					$"{rung} has no step label, so its box would draw empty.");
				Assert.That(DefconReadoutModel.RungLabel((int)rung), Is.Not.Null.And.Not.Empty,
					$"{rung} has no readout label.");
			}
		}

		[Test]
		public void TheNuclearReadoutAppearsWhenTheGateMattersAndNotBefore()
		{
			// The ten-minute wait this whole readout exists to explain: at DEFCON 1 with the gate
			// still shut, the countdown is running and the block MUST be on screen.
			Assert.That(DefconReadoutModel.ShowsNuclear(DefconGameMode.Escalation, DefconEscalationState.Floor, false), Is.True);

			// Above DEFCON 1 the gate is not counting at all -- NuclearReleaseGate.Tick returns early
			// at any other level, so TicksUntilRelease sits at the full delay. A frozen clock is worse
			// than no clock.
			Assert.That(DefconReadoutModel.ShowsNuclear(DefconGameMode.Escalation, 3, false), Is.False);
			Assert.That(DefconReadoutModel.ShowsNuclear(DefconGameMode.Escalation, 2, false), Is.False);

			// Once open it stays up whatever the level says.
			Assert.That(DefconReadoutModel.ShowsNuclear(DefconGameMode.Escalation, DefconEscalationState.Floor, true), Is.True);

			// THE CEILING CASE IS GONE. `nuclear-ceiling` is dropped by the 2026-09-13 ruling, so a
			// host can no longer say "no nuclear weapons this match" and there is no permanently-empty
			// block left to suppress.

			// Skirmish and Sandbox get their bands from NuclearUnlockClock's purchase schedule, which
			// this block does not describe.
			Assert.That(DefconReadoutModel.ShowsNuclear(DefconGameMode.Skirmish, DefconEscalationState.Floor, true), Is.False);
			Assert.That(DefconReadoutModel.ShowsNuclear(DefconGameMode.Sandbox, DefconEscalationState.Floor, true), Is.False);
		}

		[Test]
		public void TheFootLineDescribesTheExchangeRatherThanTheOldSharedLadder()
		{
			// THIS LINE WAS A LIE UNTIL 2026-09-13 and is the reason it is pinned. It read "Both sides
			// are released to the same yield. Each use raises it." -- the shared pressure ladder,
			// where firing raised BOTH sides together. Under the exchange, firing raises the OTHER
			// side, so a player who read the old line and fired to climb handed their opponent the
			// climb instead.
			var shut = DefconReadoutModel.NuclearFootLine(false, false);
			Assert.That(shut, Does.Contain("may be fired yet"));

			// THE READY LINE STATES BOTH COSTS OF FIRING, which is the whole decision the player is
			// being asked to make: your team reloads, and their ceiling goes up for good.
			var open = DefconReadoutModel.NuclearFootLine(true, false);
			Assert.That(open, Is.EqualTo("Firing puts your whole team on cooldown and raises the enemy's level."));
			Assert.That(open, Does.Contain("team"), "the cooldown is side-wide and the line must say so");
			Assert.That(open, Does.Contain("enemy"), "firing raises the ENEMY's level, never your own");

			// TWO DEAD WORDINGS, PINNED AS ABSENCES. "Each use raises it" was decision 06's shared
			// pressure ladder, where firing raised BOTH sides -- a player who read it and fired to
			// climb handed the climb to their opponent. "window" was v1's retaliation grant, which no
			// longer exists at all.
			Assert.That(open, Does.Not.Contain("Each use raises it"), "the shared-ladder wording is back");

			var cooling = DefconReadoutModel.NuclearFootLine(true, true);
			Assert.That(cooling, Is.Not.EqualTo(open), "a reloading side must read differently from a ready one");
			Assert.That(cooling, Does.Contain("reloading"));

			foreach (var line in new[] { shut, open, cooling })
			{
				Assert.That(line, Is.Not.Null.And.Not.Empty);
				Assert.That(line, Does.Not.Contain("window"), "the retaliation window's wording is back");
			}
		}

		[Test]
		public void TheBannerNamesTheCauseOfEveryTransitionThatCanHappen()
		{
			// Verbatim from the mockup.
			Assert.That(DefconReadoutModel.TransitionCause(1), Is.EqualTo("A life has been taken. Autonomous fire is released."));

			// Written to the same pattern -- cause, then what it released.
			Assert.That(DefconReadoutModel.TransitionCause(2), Is.Not.Null.And.Not.Empty);

			// NOTHING TRANSITIONS INTO DEFCON 3. It is the ceiling: the real-world standing posture,
			// with no 4 or 5 to climb down from. A banner for it could only ever be a bug.
			Assert.That(DefconReadoutModel.TransitionCause(DefconEscalationState.Ceiling), Is.Null);
			Assert.That(DefconReadoutModel.TransitionCause(DefconEscalationState.NoLevel), Is.Null);

			// Every level the state machine can actually MOVE to has a cause line, so no transition can
			// ever fire a banner with a blank second line -- which is the line that does the teaching.
			var reachable = Enumerable.Range(DefconEscalationState.Floor, DefconEscalationState.Ceiling - DefconEscalationState.Floor);
			foreach (var level in reachable)
				Assert.That(DefconReadoutModel.TransitionCause(level), Is.Not.Null, $"a transition to DEFCON {level} would draw a blank cause.");
		}

		// ---- §B6: ONE BANNER, NOT A BANNER AND A HALF -------------------------------------------

		[Test]
		public void TwoEdgesInsideTheHoldWindowAreOneBanner()
		{
			const int Hold = 66;

			// THE MEASURED CASE, and the reason this exists: run 260915_012829 went 3 -> 2 at tick
			// 5000 and 2 -> 1 at tick 5001. One tick apart, and the player saw only OPEN WAR.
			Assert.That(DefconReadoutModel.CombinesWithPrevious(5000, 5001, Hold), Is.True);

			// The other measured case: 98 ticks, 5.9 s (run 260920_010605_p1901). OUTSIDE the window,
			// so that match legitimately gets two banners and this change does not touch it.
			Assert.That(DefconReadoutModel.CombinesWithPrevious(5000, 5098, Hold), Is.False);
		}

		[Test]
		public void TheEdgeOfTheHoldWindowIsExclusive()
		{
			const int Hold = 66;

			Assert.That(DefconReadoutModel.CombinesWithPrevious(0, Hold - 1, Hold), Is.True);

			// STRICTLY INSIDE. A second edge landing on the exact tick the first banner comes down
			// gets its own banner: the player has finished reading the first one, and combining them
			// would replace a message they have already had with a longer one they have not.
			Assert.That(DefconReadoutModel.CombinesWithPrevious(0, Hold, Hold), Is.False);
			Assert.That(DefconReadoutModel.CombinesWithPrevious(0, Hold + 1, Hold), Is.False);
		}

		[Test]
		public void NothingCombinesWhenThereIsNoHoldWindowAtAll()
		{
			// BannerHoldTicks returns 0 on a zero timestep. With no window there is no banner on
			// screen to combine WITH, so every edge must stand alone rather than every edge merging.
			Assert.That(DefconReadoutModel.CombinesWithPrevious(0, 0, 0), Is.False);
			Assert.That(DefconReadoutModel.CombinesWithPrevious(10, 10, 0), Is.False);
		}

		[Test]
		public void ABannerRaisedInTheFutureIsNotALiveOne()
		{
			// Nothing in the shipped game produces a negative elapsed -- world ticks only ascend --
			// but `elapsed < hold` alone would read one as a very small gap and combine on it.
			Assert.That(DefconReadoutModel.CombinesWithPrevious(100, 50, 66), Is.False);
		}

		[Test]
		public void TheCombinedCauseNamesBothEdgesAndHowLongThePhaseLasted()
		{
			var line = DefconReadoutModel.CombinedTransitionCause(2, 1, 5);

			Assert.That(line, Is.Not.Null);

			// BOTH EDGES. The 3 -> 2 cause is the holding period; the 2 -> 1 cause is the life taken.
			// A combined banner that named only one of them would be the bug wearing a longer sentence.
			Assert.That(line, Does.Contain("holding period"), "the combined banner dropped the 3 -> 2 cause.");
			Assert.That(line, Does.Contain("life was taken"), "the combined banner dropped the 2 -> 1 cause.");

			// AND HOW LONG IT LASTED -- §B6 asks for "the phase happened, it lasted N seconds, and a
			// life was taken", and the duration is the part that makes the phase a phase.
			Assert.That(line, Does.Contain("5 seconds"));
		}

		[Test]
		public void TheCombinedCauseReadsAsEnglishAtOneSecondAndAtNone()
		{
			Assert.That(DefconReadoutModel.CombinedTransitionCause(2, 1, 1), Does.Contain("one second later"));
			Assert.That(DefconReadoutModel.CombinedTransitionCause(2, 1, 1), Does.Not.Contain("1 seconds"));

			// A SUB-SECOND GAP IS NOT "0 SECONDS LATER". SecondsBetween rounds down, so the one-tick
			// case arrives here as 0 -- and the honest rendering of that is that the two happened
			// together, which is also what the player saw.
			var together = DefconReadoutModel.CombinedTransitionCause(2, 1, 0);
			Assert.That(together, Does.Contain("same moment"));
			Assert.That(together, Does.Not.Contain("0 seconds"));
		}

		[Test]
		public void OnlyTheTwoToOnePairCombines()
		{
			// THE ONLY PAIR THE LADDER CAN PRODUCE BACK TO BACK. Nothing transitions into 3, and the
			// ladder only descends, so any other pair reaching here is a bug and must fall back to the
			// single-edge cause rather than invent a sentence about a transition that cannot happen.
			Assert.That(DefconReadoutModel.CombinedTransitionCause(3, 2, 5), Is.Null);
			Assert.That(DefconReadoutModel.CombinedTransitionCause(1, 2, 5), Is.Null);
			Assert.That(DefconReadoutModel.CombinedTransitionCause(2, 2, 5), Is.Null);
			Assert.That(DefconReadoutModel.CombinedTransitionCause(DefconEscalationState.NoLevel, 1, 5), Is.Null);
		}

		[Test]
		public void TheCombinedCauseNamesNoMechanism()
		{
			// THE SAME BAR THE THREE RULE LINES ARE HELD TO (TheRuleLinesNameNoMechanism above): the
			// copy says what happened and what the player may now do, never the trait that did it.
			foreach (var seconds in new[] { 0, 1, 5, 98 })
			{
				var line = DefconReadoutModel.CombinedTransitionCause(2, 1, seconds);

				Assert.That(line, Does.Not.Contain("DEFCON"), "the cause line restates the title.");
				Assert.That(line, Does.Not.Contain("Defcon"));
				Assert.That(line, Does.Not.Contain("AutoTarget"));
				Assert.That(line, Does.Not.Contain("trait"));
			}
		}

		[Test]
		public void TheCombineDecisionTakesRECORDEDEdgeTicksAndNothingElse()
		{
			// ADDED AFTER RUN 260921_165856, WHICH FAILED ON EXACTLY THIS. The banner widget used to
			// decide by watching: "is a band for the rung above still on my screen". That is a fact
			// about one client's SAMPLING, not about the match -- Ui.Tick runs on Ui.Timestep = 40 ms
			// of wall clock (Widget.cs:30), a different clock from the world's (OrderManager.cs:187),
			// so under any hitch, any fast-forward, and always in a --hidden autotest (where
			// Graphics.CapFramerate=false lets the sim free-run) a whole DEFCON 2 can pass between two
			// UI ticks. The widget would then never see the middle rung and would reproduce §B6's
			// exact defect from a second cause.
			//
			// The decision now takes the two ticks DefconEscalation RECORDED and nothing else. This
			// test is what stops a `now`-dependent or observation-dependent term being reintroduced:
			// the same pair of recorded edges must give the same answer no matter when it is asked.
			const int Hold = 66;
			const int At2 = 5000;

			foreach (var at1 in new[] { At2 + 1, At2 + 40, At2 + 65 })
				Assert.That(DefconReadoutModel.CombinesWithPrevious(At2, at1, Hold), Is.True,
					$"a {at1 - At2}-tick phase stopped combining.");

			foreach (var at1 in new[] { At2 + Hold, At2 + 98, At2 + 5000 })
				Assert.That(DefconReadoutModel.CombinesWithPrevious(At2, at1, Hold), Is.False,
					$"a {at1 - At2}-tick phase started combining.");

			// AND THE ANSWER IS ABSOLUTE, NOT RELATIVE. Shifting both edges by the same amount -- a
			// match that reached the same phase later -- cannot change it.
			foreach (var shift in new[] { 0, 1, 10000, 100000 })
				Assert.That(DefconReadoutModel.CombinesWithPrevious(At2 + shift, At2 + shift + 1, Hold), Is.True,
					$"shifting both edges by {shift} changed the decision.");
		}

		[Test]
		public void SecondsBetweenIsTheTickRateIdentityAgain()
		{
			const int Timestep = 60;

			// 98 ticks is the measured DEFCON 2 (run 260920_010605_p1901) and 98 x 0.06 = 5.88 s,
			// which rounds DOWN to 5. Read at 25 tps it would be 3, the same 1.5x error this repo has
			// made at eleven sites.
			Assert.That(DefconReadoutModel.SecondsBetween(5000, 5098, Timestep), Is.EqualTo(5));
			Assert.That(DefconReadoutModel.SecondsBetween(0, 5000, Timestep), Is.EqualTo(300));

			// ROUNDED DOWN, NOT TO NEAREST: under-claiming next to an event the player watched is
			// honest, over-claiming is not.
			Assert.That(DefconReadoutModel.SecondsBetween(0, 16, Timestep), Is.EqualTo(0));
			Assert.That(DefconReadoutModel.SecondsBetween(0, 17, Timestep), Is.EqualTo(1));

			// Never negative, and never divides by a timestep it was handed.
			Assert.That(DefconReadoutModel.SecondsBetween(100, 50, Timestep), Is.EqualTo(0));
			Assert.That(DefconReadoutModel.SecondsBetween(0, 5000, 0), Is.EqualTo(0));
		}

		[Test]
		public void TheBannerHoldsForAboutFourSeconds()
		{
			// 60 ms per tick: 4000 / 60 = 66 ticks, and 66 x 0.06 s = 3.96 s. READ AS 25 TICKS PER
			// SECOND this would have been 100 ticks = 6.0 s, which is the same 1.5x error this repo
			// has made at eleven sites.
			const int Timestep = 60;
			var ticks = DefconReadoutModel.BannerHoldTicks(Timestep);

			Assert.That(ticks, Is.EqualTo(66));
			Assert.That(ticks * Timestep, Is.EqualTo(3960));
			Assert.That(ticks, Is.Not.EqualTo(100), "the banner hold was computed at 25 ticks per second.");

			// Widget code cannot afford to divide by whatever it was handed.
			Assert.That(DefconReadoutModel.BannerHoldTicks(0), Is.EqualTo(0));
		}

		[Test]
		public void TheTickRateIdentityHolds()
		{
			// THE IDENTITY EVERY DURATION IN THIS FEATURE IS CHECKED AGAINST, asserted rather than
			// written in a comment: the default no-rush period is five minutes at 60 ms per tick.
			//
			//     5 min -> 5000 ticks; 5000 x 0.06 s = 300 s = 5:00
			//
			// At 25 ticks/s the same clock would come out 7500 ticks and read as seven and a half
			// minutes of real time -- the 1.5x error this repo has made at eleven sites.
			//
			// RESTATED AGAINST THE MINUTE DEFAULTS since the pace fields were retired (2026-09-13).
			// The numbers are unchanged because the defaults were chosen to leave them unchanged:
			// Standard was 5000 ticks and the default no-rush period is 5 minutes.
			const int Timestep = 60;
			var info = new DefconEscalationInfo();

			var noRush = info.NoRushTicks(info.NoRushDefault, Timestep);
			var warheads = info.NuclearReleaseDelayTicks(info.FirstWarheadsDefault, Timestep);

			Assert.That(noRush * Timestep / 1000, Is.EqualTo(300));
			Assert.That(warheads * Timestep / 1000, Is.EqualTo(600));
			Assert.That(warheads, Is.EqualTo(2 * noRush));
		}
	}
}
