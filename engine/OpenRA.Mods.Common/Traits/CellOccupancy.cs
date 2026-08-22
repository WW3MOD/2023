#region Copyright & License Information
/*
 * Copyright 2007-2022 The OpenRA Developers (see AUTHORS)
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
	/// <summary>Cell-occupancy queries that skip cosmetic ground cover.
	///
	/// <para>ww3mod tiles crop fields (<c>^CivField</c>) as ordinary 1x1 <see cref="Building"/> actors, densely
	/// enough that they are the majority of actors on a map — 3187 of river-zeta's 4544. Movement over them
	/// already works, because <see cref="Passable"/> is consulted by <see cref="Locomotor.IsBlockedBy"/> and every
	/// locomotor lists <c>field</c> under <c>Passes</c>. But <see cref="Passable"/> is a movement-layer concept:
	/// every OTHER "is this cell free" test hand-rolls its own <c>ActorMap.GetActorsAt</c> query, sees the field
	/// actor, and refuses. That is why a supply truck could not drop its cache and a helicopter could not land to
	/// unload on ground it could drive and fly straight over.</para>
	///
	/// <para>Use <see cref="BlockingActorsAt"/> in place of <c>ActorMap.GetActorsAt</c> for any test that asks
	/// whether a cell is free to *do something in*, as opposed to move through. Leave the movement path
	/// (<see cref="Locomotor"/>) alone — it has its own richer per-locomotor pass-class rules, and routing it
	/// through here would change pathfinding.</para>
	///
	/// <para>The same blindness is needed by anything that scans actors at a WPos rather than a cell.
	/// Warhead impact classification is one: a field has a full-cell HitShape but no <c>Targetable</c>, so
	/// it counted as an *invalid* actor under the shell and suppressed the explosion and the impact sound
	/// entirely. Those call sites use <see cref="IsGroundCover"/> directly, since they iterate
	/// <c>FindActorsOnCircle</c> rather than a cell.</para>
	///
	/// <para>PITFALL — the ActorMap is not the only occupancy index. Because fields are <see cref="Building"/>
	/// actors they are ALSO registered in <see cref="BuildingInfluence"/> (<c>Building.AddedToWorld</c> calls
	/// <c>AddInfluence</c> over every footprint tile), which is a wholly separate layer that
	/// <see cref="BlockingActorsAt"/> does not touch. A caller that consults both — as
	/// <see cref="BuildingUtils.IsCellBuildable"/> does — is not fixed by swapping only the
	/// <c>GetActorsAt</c> half: the <c>AnyBuildingAt</c> half re-imposes the block on the very actors just
	/// filtered out, and the swap silently becomes a no-op. Use <see cref="AnyBlockingBuildingAt"/> for that
	/// half. The two layers exist in parallel only because building bibs and pathable footprint cells are
	/// absent from the ActorMap; see the TODO in <c>BuildingInfo.IsCloseEnoughToBase</c>.</para></summary>
	public static class CellOccupancy
	{
		/// <summary>Whether this actor is purely cosmetic ground cover and so never occupies its cell.</summary>
		public static bool IsGroundCover(this Actor a)
		{
			// PITFALL: read the flag off ActorInfo, not the trait dictionary. Callers iterate GetActorsAt results
			// that can contain an actor destroyed earlier in the same tick, and any trait read on a disposed actor
			// throws (TraitDictionary.CheckDestroyed) — TraitOrDefault included.
			var passable = a.Info.TraitInfoOrDefault<PassableInfo>();
			return passable != null && passable.GroundCover;
		}

		/// <summary>The actors in a cell that actually occupy it, ignoring cosmetic ground cover.</summary>
		public static IEnumerable<Actor> BlockingActorsAt(this World world, CPos cell)
		{
			foreach (var a in world.ActorMap.GetActorsAt(cell))
				if (!a.IsGroundCover())
					yield return a;
		}

		/// <summary>Whether the BuildingInfluence layer holds a building in this cell that actually occupies
		/// it, ignoring cosmetic ground cover. The counterpart to <see cref="BlockingActorsAt"/> for the
		/// second occupancy index — see the PITFALL on the class.</summary>
		public static bool AnyBlockingBuildingAt(this World world, CPos cell)
		{
			foreach (var a in world.WorldActor.Trait<BuildingInfluence>().GetBuildingsAt(cell))
				if (!a.IsGroundCover())
					return true;

			return false;
		}

		/// <summary>A free subcell of this cell, ignoring cosmetic ground cover.
		///
		/// <para>PITFALL — <c>ActorMap.FreeSubCell(cell)</c> cannot be filtered by taking its result apart,
		/// because ground cover does not occupy one subcell: a field is a <see cref="Building"/> registered as
		/// <c>SubCell.FullCell</c>, and <c>ActorMap.AnyActorsAt</c> matches a FullCell occupant against EVERY
		/// subcell query (<c>ActorMap.cs:353</c>). So one field makes the whole cell report full and the plain
		/// overload returns <see cref="SubCell.Invalid"/>. The predicate overload is the only way through.</para></summary>
		public static SubCell FreeBlockingSubCell(this World world, CPos cell, SubCell preferredSubCell = SubCell.Any)
		{
			return world.ActorMap.FreeSubCell(cell, preferredSubCell, a => !a.IsGroundCover());
		}
	}
}
