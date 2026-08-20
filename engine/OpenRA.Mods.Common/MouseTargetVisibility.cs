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

using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.Common
{
	/// <summary>
	/// Whether the render player may click an actor — target it with an order, or select it.
	/// </summary>
	/// <remarks>
	/// This is one predicate with four call sites (UnitOrderGenerator.TargetForInput and
	/// .InputOverridesSelection, SelectionUtils.SelectHighestPriorityActorAtPoint and
	/// .SelectActorsInBoxWithDeadzone). It lived as four copies and drifted twice, so it lives here now:
	/// a rule about who may be clicked has to hold on every path that clicks, or the cursor and the
	/// order disagree.
	/// </remarks>
	public static class MouseTargetVisibility
	{
		/// <summary>
		/// The rule, with its inputs already resolved so it can be pinned without a World.
		/// </summary>
		/// <remarks>
		/// <paramref name="actorIsVisible"/> is the authority — IDefaultVisibility, i.e. every way the
		/// game admits a player may know an actor is there. The remaining three exist only to veto it,
		/// and that veto is the interesting part:
		///
		/// <paramref name="positionIsUnfogged"/> is a defence-in-depth cell check added by 8db9da9e
		/// against a through-fog targeting bug whose root cause was never found ("the exact edge case is
		/// elusive"). It asks a NARROWER question than the authority does — ResolvedVisibility > 1 on one
		/// cell — so every legitimate way of knowing about an actor that does not stamp ResolvedVisibility
		/// is silently vetoed by it. Two have been found: FrozenUnderFog buildings (22a1ec34) and radar
		/// contacts (this). Both are carried here as exemptions rather than by deleting the veto, because
		/// the bug it was added for has never been reproduced and may still be live.
		///
		/// PITFALL for whoever finds the third one: do NOT widen this by relaxing the cell test. The
		/// exemption must name a specific, earned channel of knowledge, or the veto stops constraining
		/// anything and a player can click actors the game never revealed.
		/// </remarks>
		public static bool IsRevealed(bool actorIsVisible, bool isFrozenUnderFog, bool positionIsUnfogged, bool isRadarDetected)
		{
			return actorIsVisible && (isFrozenUnderFog || positionIsUnfogged || isRadarDetected);
		}

		/// <summary>
		/// Whether <paramref name="world"/>'s render player may click <paramref name="a"/>.
		/// Callers add their own control-all and ally shortcuts around this.
		/// </summary>
		public static bool IsRevealedForMouseInput(this Actor a, World world)
		{
			var detectable = a.TraitOrDefault<Detectable>();

			return IsRevealed(
				!world.FogObscures(a),
				a.Info.HasTraitInfo<FrozenUnderFogInfo>(),
				!world.FogObscures(a.CenterPosition),
				detectable != null && detectable.IsRadarDetectedBy(a, world.RenderPlayer));
		}
	}
}
