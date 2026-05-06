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

namespace OpenRA.Mods.Common.Traits
{
	/// <summary>
	/// Implemented on a TraitInfo to contribute auto-generated text to the
	/// production tooltip (and any future tooltip surface). Implementations
	/// run at rules-load time, not on a live actor, so they only depend on
	/// static info. Runs AFTER the static <see cref="BuildableInfo.Description"/>
	/// is rendered.
	///
	/// Conventional priorities (lower = earlier in the block):
	///   100 — weapons / ammo
	///   200 — armor / health
	///   300 — speed / mobility
	///   400 — capabilities (cargo capacity, special abilities)
	/// </summary>
	public interface IProvideTooltipDescription : ITraitInfoInterface
	{
		/// <summary>
		/// Returns the formatted description block (no trailing newline) and a
		/// rendering priority. Return null or an empty string to skip.
		/// </summary>
		string ProvideTooltipDescription(ActorInfo ai, Ruleset rules, out int priority);
	}
}
