#region Copyright & License Information
/*
 * WW3MOD combined arms — shared rendezvous between the offensive reserve and the mounted transport (pure).
 *
 * PERCEIVED BEHAVIOUR: infantry ferried out of the Supply Route arrive WHERE THE ARMOUR IS, so a squad
 * reaches the front mounted and under the guns of the tanks that mustered ahead of it — instead of being
 * dropped at a cell the armour was never going to.
 *
 * THE DEFECT THIS EXISTS TO CLOSE. The two halves of a combined-arms body each computed a destination
 * privately, with different arithmetic, and neither could read the other:
 *   - armour: PoiOffensiveBotModule.StageFreePool walks the free pool to `stagingAnchor`, a steepest-descent
 *     on the ControlField frontier-distance field, halted at a standoff behind the believed front.
 *   - infantry: MountedTransportBotModule.PreContactStagingCell took a 50% LINEAR LERP from the own SR toward
 *     the top-ranked offensive POI, then applied a standoff.
 * Different inputs, different maths, different cells. So even a ferry that worked perfectly delivered its
 * passengers away from the force they were meant to join. The gap was never escorting behaviour — it was that
 * nobody published a rendezvous. This module is that channel, in its minimal form: one cell, one direction.
 *
 * WHY A PREFERENCE AND NOT A COUPLING. Nothing here makes any unit WAIT for any other. The armour does not
 * block on the transport and the transport does not block on the armour; only the transport's DESTINATION
 * changes. There is therefore no wait state that can deadlock — the failure mode this project hit in
 * bd3abacf (SectorPostureHold, where a coupling that looked like caution read as paralysis) has no analogue
 * here. When the anchor is absent or rejected the caller falls back to the legacy lerp and behaves exactly as
 * it did before, which is also the escape hatch: a rendezvous that cannot be resolved degrades to today.
 *
 * BOUNDED DIVERGENCE (the one safety term). The staging anchor ADVANCES as the believed front moves, so a
 * transport that chased it unconditionally could be walked steadily deeper — and the standing user constraint
 * on this pair is that transports must not drive into enemy territory, because one AA/AT hit takes the
 * carrier, its passengers and the tempo together. So the anchor is accepted only while it is not further from
 * our own SR than the fallback already was, plus a margin.
 *
 * Deliberately expressed as a DISTANCE RATIO against the caller's own fallback, never as a danger threshold.
 * The danger field is currently mis-scaled (PIPELINE item 40: evacLevel 1,706 against live median cells of
 * 27,919 / 94,010), so any absolute danger cutoff tuned today would have to be tuned again once that lands.
 * A comparison of two distances from our own SR is invariant to that rescaling — it cannot rot when item 40
 * changes the units of the danger field, because it never reads the danger field.
 *
 * DETERMINISM (influence-stack invariant): ZERO random draws, pure integer Chebyshev arithmetic. Two clients
 * over the same synced belief state resolve the same rendezvous.
 *
 * v3-portable: engine-free static math (NUnit-pinned in RendezvousMathTest); only the plumbing that reads the
 * offensive module's published anchor (MountedTransportBotModule) is engine-specific.
 */
#endregion

using System;

namespace OpenRA.Mods.Common.Traits
{
	public static class RendezvousMath
	{
		/// <summary>
		/// Chebyshev (king-move) cell distance — the metric the ground locomotors actually move in, and the
		/// same one TransportDropSiteMath.CellDistance uses, so "further from the SR" means the same thing on
		/// both transport paths.
		/// </summary>
		public static int CellDistance(int ax, int ay, int bx, int by)
		{
			return Math.Max(Math.Abs(ax - bx), Math.Abs(ay - by));
		}

		/// <summary>
		/// <para>True if the offensive reserve's staging anchor is an acceptable rendezvous for a transport starting
		/// from <paramref name="srX"/>,<paramref name="srY"/>, given the destination the transport would
		/// otherwise have picked for itself.</para>
		///
		/// <para>The test is purely comparative: the anchor may sit up to <paramref name="marginCells"/> further out
		/// than the fallback, and no further. A negative margin is clamped to zero rather than rejected, so a
		/// mis-set config can only make the rendezvous MORE conservative, never inverted.</para>
		/// </summary>
		public static bool AnchorAcceptable(int srX, int srY, int anchorX, int anchorY, int fallbackX, int fallbackY, int marginCells)
		{
			var margin = Math.Max(0, marginCells);
			var anchorReach = CellDistance(srX, srY, anchorX, anchorY);
			var fallbackReach = CellDistance(srX, srY, fallbackX, fallbackY);

			return anchorReach <= fallbackReach + margin;
		}

		/// <summary>
		/// <para>Resolve the cell a mounted transport should deliver its passengers to before contact.</para>
		///
		/// <para>Returns the offensive reserve's staging anchor when the rendezvous is enabled, an anchor was
		/// published, and it passes <see cref="AnchorAcceptable"/>; otherwise the caller's own fallback.
		/// <paramref name="hasAnchor"/> is passed separately rather than using a nullable so the function stays
		/// engine-free and trivially portable.</para>
		///
		/// <para>Every rejection path returns the fallback unchanged — with <paramref name="enabled"/> false this is
		/// the identity function on the fallback, which is what keeps the frozen profile byte-identical.</para>
		/// </summary>
		public static void ResolveDropOff(
			bool enabled, bool hasAnchor,
			int srX, int srY, int anchorX, int anchorY, int fallbackX, int fallbackY, int marginCells,
			out int cellX, out int cellY)
		{
			cellX = fallbackX;
			cellY = fallbackY;

			if (!enabled || !hasAnchor)
				return;

			if (!AnchorAcceptable(srX, srY, anchorX, anchorY, fallbackX, fallbackY, marginCells))
				return;

			cellX = anchorX;
			cellY = anchorY;
		}
	}
}
