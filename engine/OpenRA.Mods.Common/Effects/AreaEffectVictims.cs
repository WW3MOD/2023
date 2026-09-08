#region Copyright & License Information
/*
 * WW3MOD post-detonation victim filter (2026-09-08).
 *
 * A persistent area effect -- ShockwaveEffect, ThermalRadiationEffect -- outlives the animation that
 * announces it by a wide margin. AtomicHighYield's blast wave lives ~645 ticks and its thermal field
 * 413; both re-sweep a disc every tick and neither draws anything a player can read once the cloud
 * sprite has finished. So the effect is invisible and still lethal for most of its life, and any
 * unit that arrives in that window is killed by a weapon that, as far as the player is concerned,
 * went off a minute ago. That is what "I build new units and they come in and die instantly" is.
 *
 * The rule below is the whole fix: a weapon can only hurt what was on the map when it detonated.
 * It lives here rather than inline in each effect because there are two effects with the same hole
 * and no shared base class -- a copy in each is the point at which one of them silently loses it,
 * which is exactly what PostDetonationSpawnTest's IL scan exists to prevent.
 */
#endregion

namespace OpenRA.Mods.Common.Effects
{
	/// <summary>Shared eligibility rule for area effects that keep damaging after their impact tick.</summary>
	public static class AreaEffectVictims
	{
		/// <summary>True if this victim already existed when the effect snapshotted World.NextActorID.</summary>
		// ActorIDs are handed out by a strictly monotonic counter that is never reset and never
		// reuses a value (World.NextAID), and ActorID is readonly on Actor, so the comparison is a
		// total order on creation time and cannot be invalidated after the fact. The snapshot is the
		// ID the NEXT actor would get, hence >= rather than > -- an actor holding exactly that value
		// is the first one created after the detonation.
		public static bool ExistedAtDetonation(uint victimActorID, uint firstActorIDAfterDetonation)
		{
			return victimActorID < firstActorIDAfterDetonation;
		}
	}
}
