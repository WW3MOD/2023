#region Copyright & License Information
/*
 * WW3MOD garrison panel arithmetic (2026-09-15) — the two numbers GARRISON_PANEL gets wrong, pulled
 * out of the widget code so they can be tested without a World or a running UI.
 *
 * Both are layout/readout decisions rather than sim state, so nothing here may depend on anything
 * a client could disagree about: these are pure functions of the chrome's own declared geometry and
 * the occupant counts, and they are called from a selection-change path, never per frame.
 */
#endregion

using System;

namespace OpenRA.Mods.Common.Widgets
{
	public static class GarrisonPanelMath
	{
		/// <summary>
		/// Whether the shelter row at <paramref name="shelterIndex"/> should stop naming one man and
		/// become the "+N more" summary instead.
		/// <para>Only ever the LAST declared row, and only when the occupant count genuinely exceeds
		/// what the rows can show. At exactly <paramref name="reserveRows"/> occupants every man still
		/// gets his own row and nothing is hidden, so the summary must NOT appear — a panel that
		/// replaced a name with "+1 more" at full-but-not-overflowing would be showing less than it
		/// could.</para>
		/// </summary>
		public static bool IsOverflowRow(int shelterIndex, int shelterCount, int reserveRows)
		{
			if (reserveRows <= 0)
				return false;

			return shelterIndex == reserveRows - 1 && shelterCount > reserveRows;
		}

		/// <summary>
		/// How many occupants the overflow row stands in for. It speaks for ITSELF as well as for the
		/// men with no row, which is why this is not <c>shelterCount - reserveRows</c>: the last row
		/// gave up its own name to carry this, so that man is one of the ones being summarised.
		/// </summary>
		public static int HiddenOccupants(int shelterIndex, int shelterCount)
		{
			return Math.Max(shelterCount - shelterIndex, 0);
		}

		/// <summary>
		/// The Y the first shelter row should sit at, given how many firing-port rows are actually on
		/// screen for this actor.
		/// <para>Port rows are declared for the WIDEST garrison — 8, for a civilian house — at fixed Y,
		/// and the surplus is hidden on anything narrower. The shelter rows below them kept their
		/// declared Y regardless, so a PBOX or HBOX (2 ports) drew two rows, then six rows of nothing,
		/// then the shelter; a GTWR (4 ports) drew four and four. This re-seats the block directly
		/// under the last port row that is shown, preserving the deliberate extra
		/// <paramref name="portToReserveGap"/> between the two sections.</para>
		/// <para>Every input is read off the shipped widgets at construction rather than written into
		/// the code, so ingame-player.yaml stays the one place these positions are decided.</para>
		/// </summary>
		public static int ReserveRowTop(int portBaseY, int portPitch, int portToReserveGap, int visiblePortRows)
		{
			// A garrison with no ports at all is not reachable today — all four families declare at
			// least two — but without this the arithmetic runs BACKWARDS off the top of the panel
			// rather than merely looking wrong.
			var rows = Math.Max(visiblePortRows, 1);

			return portBaseY + (portPitch * (rows - 1)) + portToReserveGap;
		}
	}
}
