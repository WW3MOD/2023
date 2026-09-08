#region Copyright & License Information
/*
 * WW3MOD post-detonation victim filter (2026-09-08).
 *
 * A persistent area effect -- ShockwaveEffect, ThermalRadiationEffect -- outlives the animation that
 * announces it once the yield is large enough. AtomicHighYield's blast wave runs ~645 ticks against
 * a 394-tick cloud; Tsar Bomba's runs ~1560 against 510. Both re-sweep a disc every tick and neither
 * draws anything a player can read once the cloud sprite has finished, so the effect is invisible and
 * still lethal, and any unit that arrives in that window is killed by a weapon that as far as the
 * player is concerned went off a minute ago. That is what "I build new units and they come in and
 * die instantly" is.
 *
 * The rule is: a weapon may only hurt what was on the map when it detonated.
 *
 * WHY THIS IS SHAPED THE WAY IT IS, rather than a bare `uint` and an `if` at each call site. An
 * adversarial review of the first version named two one-token mutations that reintroduce the bug
 * with the whole test suite green -- inverting the guard, and transposing its two `uint` arguments.
 * Neither is reachable now, and that is a property of the types rather than of a test:
 *
 *   - The stamp is its own readonly struct and the comparison is an INSTANCE method on it, so there
 *     are no two same-typed operands to transpose. `stamp.Predates(id)` has one argument.
 *   - Callers get an ALREADY-FILTERED sequence, so there is no branch at a call site to invert.
 *     PreDetonationOnCircle also swallows the FindActorsOnCircle call, which is what lets
 *     PostDetonationSpawnTest assert that neither Tick reaches the unfiltered sweep at all.
 *   - Exactly one comparison exists in the whole feature -- Predates(uint) below -- and it is the
 *     one thing pinned exhaustively at its boundary.
 */
#endregion

using System.Collections.Generic;
using System.Linq;

namespace OpenRA.Mods.Common.Effects
{
	/// <summary>The actor population as it stood at one instant, as a filter on later sweeps.</summary>
	// Carries World.NextActorID as of the tick it was taken. ActorIDs come from a strictly monotonic
	// counter that is never reset and never reuses a value a dead actor gave up (World.NextAID), and
	// Actor.ActorID is readonly, so this is a total order on creation time that nothing can
	// invalidate after the fact.
	public readonly struct DetonationStamp
	{
		readonly uint firstActorIDAfterDetonation;

		public DetonationStamp(uint firstActorIDAfterDetonation)
		{
			this.firstActorIDAfterDetonation = firstActorIDAfterDetonation;
		}

		/// <summary>Stamps the world as it is right now.</summary>
		public static DetonationStamp Now(World world)
		{
			return new DetonationStamp(world.NextActorID);
		}

		/// <summary>True if an actor with this ID already existed when the stamp was taken.</summary>
		// THE ONLY COMPARISON IN THIS FEATURE. Everything else delegates here, so this is the single
		// line a boundary test has to cover. The stamp is the ID the NEXT actor would have been
		// given, hence `<` -- an actor holding exactly that value is the first one created after.
		public bool Predates(uint actorID)
		{
			return actorID < firstActorIDAfterDetonation;
		}

		/// <summary>True if this actor already existed when the stamp was taken.</summary>
		// Deliberately a bare delegation with no comparison of its own: it exists so that a method
		// group can be handed to Where below without the caller writing a lambda, and a lambda is
		// where a stray negation would go unnoticed.
		public bool Predates(Actor actor)
		{
			return Predates(actor.ActorID);
		}
	}

	/// <summary>Sweeps for area-effect victims, pre-filtered to actors that predate the detonation.</summary>
	public static class AreaEffectVictims
	{
		/// <summary>Every actor on the circle that already existed when the stamp was taken.</summary>
		// This wraps FindActorsOnCircle rather than taking its result so that the unfiltered sweep is
		// not reachable from a Tick at all -- PostDetonationSpawnTest asserts on exactly that, and an
		// assertion that a filter is CALLED is worth much less than one that the raw sweep is ABSENT.
		// No branch of its own; the filtering is the method group, so there is nothing here to invert.
		public static IEnumerable<Actor> PreDetonationOnCircle(World world, WPos center, WDist radius, DetonationStamp stamp)
		{
			return world.FindActorsOnCircle(center, radius).Where(stamp.Predates);
		}
	}
}
