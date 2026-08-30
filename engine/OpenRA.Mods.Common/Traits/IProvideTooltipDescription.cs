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
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	/// <summary>
	/// <para>Implemented on a TraitInfo to contribute auto-generated rows to the
	/// production tooltip (and any future tooltip surface). Implementations
	/// run at rules-load time, not on a live actor, so they only depend on
	/// static info. Runs AFTER the static <see cref="BuildableInfo.Description"/>
	/// is rendered.</para>
	///
	/// <para>Conventional priorities (lower = earlier in the block):
	///   100 — weapons / ammo
	///   200 — armor / health
	///   300 — speed / mobility
	///   400 — capabilities (cargo capacity, special abilities)</para>
	/// </summary>
	public interface IProvideTooltipDescription : ITraitInfoInterface
	{
		/// <summary>
		/// <para>Returns the rows this trait contributes, and a rendering priority.
		/// Return null or an empty sequence to skip.</para>
		///
		/// <para>Returns typed <see cref="TooltipElement"/>s rather than a formatted string
		/// so that styling is a property of each row's kind. While this returned a string,
		/// every contributor was concatenated into one single-font label and a contributor
		/// could only influence its appearance by choosing characters — which is how the
		/// same supply figure came to be rendered in two different notations.</para>
		/// </summary>
		IEnumerable<TooltipElement> ProvideTooltipDescription(ActorInfo ai, Ruleset rules, out int priority);
	}
}
