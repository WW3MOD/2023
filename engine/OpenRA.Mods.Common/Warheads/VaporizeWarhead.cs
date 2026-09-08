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

using OpenRA.GameRules;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Warheads
{
	// SCOPE. This is deliberately a SMALL-RADIUS warhead and should never be given the weapon's blast radius.
	// Vaporisation is what happens inside a fireball; outside it things are wrecked, burned and thrown, and
	// those should keep leaving husks exactly as they do now. Sizing it to the fireball is the caller's job -
	// there is no default Radius for that reason.
	// TARGET TYPES ARE DELIBERATELY NOT CONSULTED - see IsValidAgainst below. That is the whole point of
	// the warhead: what is inside a fireball is gone whether or not a weapon designer remembered to list
	// its target type, and the things this is FOR - a neutral tech building, an unlisted decoration - are
	// exactly the things a ValidTargets list misses. ValidTargets and InvalidTargets are therefore INERT
	// on this warhead type. The opt-out is `-Vaporizable:` on the actor, per-actor and authoritative.
	[Desc("Removes actors near the impact point entirely: they flash white, dissolve, and die leaving no husk,",
		"no cook-off, no corpse and no ejected pilot. Requires the Vaporizable trait on the victim.",
		"Radius-bounded on purpose - give it the weapon's FIREBALL radius, not its blast radius.",
		"IGNORES ValidTargets and InvalidTargets: anything with a health trait inside the radius is",
		"removed. To spare an actor, take the Vaporizable trait off it with `-Vaporizable:`.")]
	public class VaporizeWarhead : Warhead
	{
		[FieldLoader.Require]
		[Desc("Horizontal radius within which actors are vaporised. Measured from the impact point to the",
			"actor's CENTRE, so a building whose centre falls outside survives even if part of it is inside.",
			"There is no default: this must be set to match the firing weapon's fireball.")]
		public readonly WDist Radius = WDist.Zero;

		[Desc("Ticks between the actor being MARKED and the fade starting. Distinct from the inherited Delay,",
			"which defers this whole warhead: marking early and fading late is what lets the suppression beat",
			"a damage warhead declared ahead of it while still landing the dissolve on the flash's peak.")]
		public readonly int FadeDelay = 0;

		[Desc("Ticks from the start of the effect to the actor being killed. The actor is still alive and still",
			"targetable for this long, but is already leaving no remains whatever kills it.")]
		public readonly int Duration = 12;

		[Desc("Peak brightness multiplier the sprite reaches before it becomes a silhouette.",
			"1 is no lift at all; 6 is blown out well past white.")]
		public readonly float Brightness = 6f;

		[Desc("Fraction of Duration spent heating up before the sprite is replaced by a dissolving silhouette.",
			"0 dissolves immediately with no heating; 1 heats for the whole duration and then vanishes outright,",
			"which is the right setting when the removal is meant to be hidden behind a flash rather than seen.")]
		public readonly float WhiteoutFraction = 0.45f;

		[Desc("DamageTypes reported on the resulting kill. Death animations are suppressed either way; this is",
			"for anything else that reads the damage type, and for the statistics.")]
		public readonly BitSet<DamageType> DamageTypes = default;

		[Desc("Centre the sphere at ground level under the impact rather than at the impact altitude.",
			"The radius test is horizontal-only, so this changes nothing about which actors are caught - it is",
			"here for clarity when reading a weapon that airbursts.")]
		public readonly bool ForceGroundLevel = false;

		/// <summary>
		/// Everything with health inside the radius, of a valid relationship, that is not the firer.
		/// One sentence, on purpose.
		///
		/// This REPLACES the base implementation rather than extending it, because the base's third
		/// clause - the ValidTargets/InvalidTargets overlap test (Warhead.cs:73-75) - is the one thing
		/// that must not apply. WW3MOD protects several actors by giving them a target type no weapon
		/// lists rather than by making them tough (`NoAutoTarget` on ^TechBuilding and SUPPLYROUTE,
		/// `Hypersonic` on every in-flight missile), and a fireball is not a targeting decision.
		/// The AffectsParent and relationship clauses are kept verbatim from the base.
		///
		/// The health predicate is Vaporizable.CanVaporize, shared with the trait so the warhead and
		/// the victim cannot disagree; the shape is DamageWarhead.cs:57-64 and the semantics are
		/// DoomsdayStrike.Annihilate's, which already filters the whole map on exactly this test.
		/// </summary>
		public override bool IsValidAgainst(Actor victim, Actor firedBy)
		{
			// Cannot be killed without a health trait - and starting the fade on one that cannot die is
			// the silent failure Vaporizable.CanVaporize documents.
			if (!Vaporizable.CanVaporize(victim.Info))
				return false;

			if (!AffectsParent && victim == firedBy)
				return false;

			var relationship = firedBy.Owner.RelationshipWith(victim.Owner);
			if (!ValidRelationships.HasRelationship(relationship))
				return false;

			return true;
		}

		public override void DoImpact(in Target target, WarheadArgs args)
		{
			if (target.Type == TargetType.Invalid || Radius.Length <= 0)
				return;

			var firedBy = args.SourceActor;
			var world = firedBy.World;

			var pos = target.CenterPosition;
			if (ForceGroundLevel)
				pos -= new WVec(0, 0, world.Map.DistanceAboveTerrain(pos).Length);

			var settings = new VaporizeParams(FadeDelay, Duration, Brightness, WhiteoutFraction);

			// FindActorsInCircle measures HORIZONTALLY and ignores height (WorldUtils.cs:79). That is the right
			// semantic here and it is load-bearing: a fireball 0.7 cells across bursting 15 cells up would
			// contain nothing at all under a 3D test, and "directly under the fireball" is what was asked for.
			foreach (var victim in world.FindActorsInCircle(pos, Radius))
			{
				if (!IsValidAgainst(victim, firedBy))
					continue;

				victim.TraitOrDefault<Vaporizable>()?.Begin(victim, settings, firedBy, DamageTypes);
			}
		}
	}
}
