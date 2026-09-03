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

using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits.Render
{
	/// <summary>How exposed a unit is, coarsest to most exposed. Ordered — consumers compare with &gt;=.</summary>
	public enum DetectabilityGrade
	{
		Concealed = 0,
		Low = 1,
		Moderate = 2,
		High = 3,
		Spotted = 4,
	}

	/// <summary>
	/// Turns a unit's OWN concealment tier into a readout grade. Pure: ints and bools in, an enum out —
	/// no World, no Actor, no RNG, nothing synced.
	/// </summary>
	/// <remarks>
	/// <para>WHAT IS GRADED, AND WHY IT IS NOT ENEMY OBSERVATION. The grade is a property of the unit's own
	/// posture — Detectable.CurrentVisibility, composed from cover (object-proximity), prone, dug-in,
	/// firing, moving and rank (mods/ww3mod/rules/ingame/infantry.yaml:758-787,
	/// mods/ww3mod/rules/defaults.yaml:278-289). It uses no information about where enemies are.</para>
	///
	/// <para>That is deliberate and it is what keeps WithSpottedDecoration's asymmetry rule intact. A readout
	/// driven by who can currently see us would announce "someone you cannot see is watching you", which is
	/// a wallhack. Own-exposure is knowledge the soldier plainly has: he knows he is lying down in a treeline,
	/// or standing in a road firing. The single enemy-derived input is <c>spotted</c>, which is the existing
	/// mark's already-shipped predicate and already carries the asymmetry gate — it only reports enemies the
	/// viewing player has themselves spotted. Nothing else here can raise the grade.</para>
	///
	/// <para>CONCEALMENT RUNS THE OPPOSITE WAY TO THE READOUT. Detectable.CurrentVisibility is the observer
	/// strength required to reveal the unit, so HIGH concealment means HARD to see. Exposure inverts it, so
	/// the grade and the number both climb as the unit becomes more visible. Read Exposure, not concealment,
	/// when tuning the ceilings.</para>
	/// </remarks>
	public static class Detectability
	{
		/// <summary>Floor Detectable.ClampConcealment enforces: 0 is shroud's level and is not a concealment value.</summary>
		public const int MinimumConcealment = 1;

		/// <summary>
		/// Ceiling Detectable.ClampConcealment enforces (Detectable.cs:118-125) — one band BELOW the top so
		/// the strongest observer still strictly exceeds the best concealment. Derived rather than pinned so
		/// this does not silently disagree with the clamp if VisionLayers ever moves.
		/// </summary>
		public static int MaximumConcealment => MapLayers.VisionLayers - 2;

		/// <summary>
		/// How exposed the unit is, on the same 1..MaximumConcealment scale the concealment tier uses but
		/// running the other way: concealment 1 (nothing hiding him) is maximum exposure.
		/// </summary>
		public static int Exposure(int concealment)
		{
			var clamped = concealment < MinimumConcealment ? MinimumConcealment
				: concealment > MaximumConcealment ? MaximumConcealment
				: concealment;

			return MinimumConcealment + MaximumConcealment - clamped;
		}

		/// <summary>
		/// The band an exposure level falls in. Each ceiling is the highest exposure that still reads as that
		/// band; anything above the last ceiling is High. <paramref name="spotted"/> overrides every band —
		/// being seen by an enemy we know about is the top of the scale whatever the posture says.
		/// </summary>
		public static DetectabilityGrade Grade(int concealment, bool spotted,
			int concealedCeiling, int lowCeiling, int moderateCeiling)
		{
			if (spotted)
				return DetectabilityGrade.Spotted;

			var exposure = Exposure(concealment);

			if (exposure <= concealedCeiling)
				return DetectabilityGrade.Concealed;

			if (exposure <= lowCeiling)
				return DetectabilityGrade.Low;

			if (exposure <= moderateCeiling)
				return DetectabilityGrade.Moderate;

			return DetectabilityGrade.High;
		}
	}
}
