#region Copyright & License Information
/*
 * WW3MOD AirframeEvacMath tests — player-side out-of-ammo disposition for airframes.
 *
 * User ruling (2026-08-19): "Airplanes uses the airfield, helicopters use helipad, if those do not exist they
 * must evacuate (They cannot be rearmed in that case)." These pin the decision half of that ruling.
 *
 * The load-bearing property is the HOST TERM: an airframe that HAS a rearm host must never be evacuated, because
 * ReturnToBase owns that case and will fly it to the pad. Evacuation is the disposition for the airframe whose
 * ammunition is one-way — nothing else.
 *
 * The second property is TRANSPORT SAFETY: an airframe with no ammo pools at all (Chinook, Mi-8 — no Rearmable)
 * reads "no rearm host" for free, because RearmableInfo is absent. Keying evacuation on the host term alone would
 * therefore fly every transport in the game off the map. Pool count is what separates "cannot rearm" from
 * "has nothing to rearm".
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class AirframeEvacMathTest
	{
		[Test]
		public void SpentAndUnhosted_Evacuates()
		{
			Assert.That(AirframeEvacMath.Decide(2, 0, true, false, false), Is.EqualTo(AirframeEvacAction.Evacuate),
				"a spent airframe with no rearm host has one-way ammunition and no combat value left — it must leave");
		}

		[Test]
		public void SpentButHosted_DoesNotEvacuate()
		{
			Assert.That(AirframeEvacMath.Decide(2, 0, true, true, false), Is.EqualTo(AirframeEvacAction.None),
				"a rearm host exists ⇒ ReturnToBase owns this airframe and flies it to the pad; evacuating would " +
				"throw away a helicopter that a captured helipad could have refilled");
		}

		[Test]
		public void StillCarryingRounds_DoesNotEvacuate()
		{
			Assert.Multiple(() =>
			{
				Assert.That(AirframeEvacMath.Decide(2, 1, true, false, false), Is.EqualTo(AirframeEvacAction.None),
					"one loaded pool still shoots — a dry SECONDARY must not retire the airframe");
				Assert.That(AirframeEvacMath.Decide(2, 2, true, false, false), Is.EqualTo(AirframeEvacAction.None));
			});
		}

		[Test]
		public void NoAmmoPoolsNeverEvacuates()
		{
			// THE LIVE TRANSPORT GUARD. Both shipped transports (TRAN Chinook, HALO Mi-8) inherit
			// ^Helicopter and carry no AmmoPool and no Rearmable — Cargo airframes with no armament. This
			// is the term that actually refuses them today.
			Assert.Multiple(() =>
			{
				Assert.That(AirframeEvacMath.Decide(0, 0, true, false, false), Is.EqualTo(AirframeEvacAction.None),
					"an airframe with no ammo pools is not 'out of ammo' — it is unarmed, and must be left alone");
				Assert.That(AirframeEvacMath.Decide(0, 0, true, true, false), Is.EqualTo(AirframeEvacAction.None));
			});
		}

		[Test]
		public void ArmedTransportWithoutRearmableNeverEvacuates()
		{
			// FORWARD GUARD — no shipped actor reaches this row today (both transports also have zero pools,
			// so the pool term refuses them first). It is pinned because it, not the pool count, is what the
			// ruling says: an ARMED transport would be spent-able and permanently hostless at the same time —
			// RearmableInfo being null makes AnyResupplierExists false forever — so it would read identically
			// to a dry Apache on every other term and the pool count would wave it through, flying a loaded
			// troop transport off the map the first time its door gun emptied.
			Assert.Multiple(() =>
			{
				Assert.That(AirframeEvacMath.Decide(2, 0, false, false, false), Is.EqualTo(AirframeEvacAction.None),
					"an airframe with no Rearmable was never meant to rearm — running dry costs it nothing it had");
				Assert.That(AirframeEvacMath.Decide(2, 0, false, true, false), Is.EqualTo(AirframeEvacAction.None));
				Assert.That(AirframeEvacMath.Decide(0, 0, false, false, false), Is.EqualTo(AirframeEvacAction.None));
			});
		}

		[Test]
		public void AlreadyEvacuating_IsNotReissued()
		{
			Assert.That(AirframeEvacMath.Decide(2, 0, true, false, true), Is.EqualTo(AirframeEvacAction.None),
				"re-issuing cancels the running RotateToEdge and restarts the exit every tick, so the airframe " +
				"never reaches the edge and never refunds");
		}

		[Test]
		public void HostTermOutranksSpentTerm()
		{
			// Ordering pin: the two terms are not symmetric. Whatever else is true, a host means "do not evacuate".
			Assert.Multiple(() =>
			{
				Assert.That(AirframeEvacMath.Decide(1, 0, true, true, false), Is.EqualTo(AirframeEvacAction.None));
				Assert.That(AirframeEvacMath.Decide(3, 0, true, true, false), Is.EqualTo(AirframeEvacAction.None));
			});
		}

		[Test]
		public void LoadedPoolsAreClampedNotTrusted()
		{
			// Defensive: loadedPools can only be counted from the same enumeration that produced totalPools,
			// but a caller that ever passes an inconsistent pair must not be handed Evacuate by accident.
			Assert.That(AirframeEvacMath.Decide(1, 5, true, false, false), Is.EqualTo(AirframeEvacAction.None),
				"more loaded pools than total is nonsense input, but it unambiguously means 'has rounds'");
		}
	}
}
