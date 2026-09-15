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

using System.IO;
using System.Linq;
using NUnit.Framework;
using OpenRA.Mods.Common.Widgets;

namespace OpenRA.Test
{
	/// <summary>
	/// GARRISON_PANEL showed at most 4 of up to 10 shelter occupants, and left a block of dead space
	/// on any garrison narrower than a civilian house.
	///
	/// <para>THE UNDER-REPORT. The shelter loop ran to a hardcoded 4 against ^CivBuilding's
	/// MaxWeight: 10. It fails exactly when the panel matters most: a soldier recalled under fire
	/// goes back to the shelter and cannot re-man a port until his suppression decays below
	/// SuppressionRedeployThreshold, so a garrison being suppressed is the state that holds the most
	/// men in shelter at once — and the player saw four, with nothing saying there were more.</para>
	///
	/// <para>THE GAP. Port rows are declared for the widest garrison (8) at fixed Y and the surplus
	/// is hidden on anything narrower, but the shelter rows kept their declared Y regardless. A PBOX
	/// or HBOX has 2 ports and a GTWR has 4, so those panels drew their port rows, then four or six
	/// rows of nothing, then the shelter.</para>
	///
	/// <para>RED: change IsOverflowRow's comparison to <c>&gt;=</c> and
	/// TheLastRowStaysANameWhenNothingIsHidden fails; make ReserveRowTop ignore visiblePortRows and
	/// ANarrowGarrisonSeatsItsShelterUnderItsLastPort fails.</para>
	/// </summary>
	[TestFixture]
	public class GarrisonPanelReserveRowsTest
	{
		// What ingame-player.yaml declares today. Asserted against the shipped chrome below rather
		// than trusted, so a row added there cannot leave these expectations quietly stale.
		const int ReserveRows = 4;
		const int DeclaredPortRows = 8;

		[Test]
		public void AFullShelterIsNeverReportedAsFourMen()
		{
			// THE DEFECT, stated directly: ten men in a hold that renders four rows.
			Assert.That(GarrisonPanelMath.IsOverflowRow(ReserveRows - 1, 10, ReserveRows), Is.True,
				"with 10 occupants and 4 rows the last row still named a single man, so the panel " +
				"reported 4 of 10 and said nothing about the other 6 — the under-report this fixes.");

			Assert.That(GarrisonPanelMath.HiddenOccupants(ReserveRows - 1, 10), Is.EqualTo(7),
				"the summary row must speak for ITSELF as well as for the men with no row: it gave up " +
				"its own name to carry the count, so that man is one of the ones being summarised. " +
				"Reporting 6 here would leave one occupant in neither the list nor the total.");
		}

		[Test]
		public void TheLastRowStaysANameWhenNothingIsHidden()
		{
			// Guards the off-by-one that makes the fix worse than the bug: at exactly 4 occupants
			// every man already has his own row, and replacing the fourth with "+1 more" would show
			// LESS than the panel could.
			Assert.That(GarrisonPanelMath.IsOverflowRow(ReserveRows - 1, ReserveRows, ReserveRows), Is.False,
				"the last row became a summary at exactly full-but-not-overflowing, hiding a man the " +
				"panel had room to name.");

			for (var count = 0; count <= ReserveRows; count++)
				for (var i = 0; i < ReserveRows; i++)
					Assert.That(GarrisonPanelMath.IsOverflowRow(i, count, ReserveRows), Is.False,
						$"row {i} became a summary at {count} occupants, which fit in {ReserveRows} rows.");
		}

		[Test]
		public void OnlyTheLastRowEverSummarises()
		{
			for (var i = 0; i < ReserveRows - 1; i++)
				Assert.That(GarrisonPanelMath.IsOverflowRow(i, 10, ReserveRows), Is.False,
					$"row {i} summarised instead of naming its man, so the panel lost detail it had " +
					"room for. Only the LAST row may carry the remainder.");
		}

		[Test]
		public void NoRowsDeclaredMeansNoSummary()
		{
			Assert.That(GarrisonPanelMath.IsOverflowRow(0, 10, 0), Is.False,
				"a panel declaring no shelter rows produced a summary row anyway, which would be " +
				"written into a widget that does not exist.");
		}

		[Test]
		public void AWideGarrisonKeepsTheDeclaredLayoutExactly()
		{
			// The 8-port civilian house is what the chrome was laid out for, so re-seating must be a
			// no-op there. If this moves, every existing screenshot of the panel is now wrong.
			const int PortBaseY = 18, PortPitch = 14, ReserveBaseY = 135;
			var gap = ReserveBaseY - (PortBaseY + (PortPitch * (DeclaredPortRows - 1)));

			Assert.That(GarrisonPanelMath.ReserveRowTop(PortBaseY, PortPitch, gap, DeclaredPortRows),
				Is.EqualTo(ReserveBaseY),
				"re-seating moved the shelter block on the actor the layout was designed around. The " +
				"derived gap must reproduce the declared Y exactly at the declared port count.");
		}

		[Test]
		public void ANarrowGarrisonSeatsItsShelterUnderItsLastPort()
		{
			const int PortBaseY = 18, PortPitch = 14, ReserveBaseY = 135;
			var gap = ReserveBaseY - (PortBaseY + (PortPitch * (DeclaredPortRows - 1)));

			// PBOX and HBOX declare 2 ports, GTWR declares 4 (structures-defenses.yaml).
			Assert.That(GarrisonPanelMath.ReserveRowTop(PortBaseY, PortPitch, gap, 2), Is.EqualTo(51),
				"a 2-port garrison still pushed its shelter rows down to the 8-port position, leaving " +
				"six rows of dead space between the ports and the shelter.");

			Assert.That(GarrisonPanelMath.ReserveRowTop(PortBaseY, PortPitch, gap, 4), Is.EqualTo(79),
				"a 4-port garrison still pushed its shelter rows down to the 8-port position.");

			Assert.That(GarrisonPanelMath.ReserveRowTop(PortBaseY, PortPitch, gap, 0), Is.EqualTo(ReserveBaseY - (PortPitch * (DeclaredPortRows - 1))),
				"a garrison declaring no ports produced a position derived from -1 rows, which runs " +
				"backwards off the top of the panel instead of clamping at the first row.");
		}

		static string Chrome()
		{
			var dir = Path.GetDirectoryName(typeof(GarrisonPanelReserveRowsTest).Assembly.Location);
			for (var i = 0; i < 6 && dir != null; i++)
			{
				var candidate = Path.Combine(dir, "mods", "ww3mod", "chrome", "ingame-player.yaml");
				if (File.Exists(candidate))
					return File.ReadAllText(candidate);

				dir = Path.GetDirectoryName(dir);
			}

			return null;
		}

		[Test]
		public void TheShippedChromeStillDeclaresWhatThisFixtureAssumes()
		{
			// The constants above are only meaningful if they describe the real panel. Adding a fifth
			// RESERVE_LABEL would otherwise leave every expectation here quietly testing a panel that
			// no longer exists.
			var chrome = Chrome();
			if (chrome == null)
				Assert.Ignore("ingame-player.yaml not found relative to the test assembly.");

			var reserves = Enumerable.Range(0, 32).Count(i => chrome.Contains($"Label@RESERVE_LABEL_{i}:"));
			var ports = Enumerable.Range(0, 32).Count(i => chrome.Contains($"Label@PORT_LABEL_{i}:"));

			Assert.That(reserves, Is.EqualTo(ReserveRows),
				$"ingame-player.yaml now declares {reserves} shelter rows, not {ReserveRows}. The " +
				"logic derives its row count from the chrome and will follow, but the expectations in " +
				"this fixture are hardcoded and no longer describe the shipped panel — update them.");

			Assert.That(ports, Is.EqualTo(DeclaredPortRows),
				$"ingame-player.yaml now declares {ports} port rows, not {DeclaredPortRows}, so the " +
				"re-seating arithmetic tested here is calibrated against the wrong widest case.");
		}
	}
}
