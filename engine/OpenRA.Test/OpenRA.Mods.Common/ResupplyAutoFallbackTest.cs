#region Copyright & License Information
/*
 * Pins the Auto-stance out-of-ammo fallback (SupplyHuntMath.DecideAutoDisposition).
 * Pure-math test; no Actor / World.
 *
 * Reported from playtest 260827: "When my iskander fires its last missile, by default it just
 * holds position when I have no logistics center." USER RULING the same day: "'Auto' should mean
 * that they evacuate if no rearm actor exists, and 'Evacuate' just means they evacuate no matter
 * what" — leaving immediately, with no grace period.
 *
 * The fixture is built around the distinctions that must NOT be collapsed into each other: a host
 * that is merely far away (someone may still drive to us) versus one that cannot move (nobody ever
 * will), and a unit that can fire nothing versus one that has merely lost its defining weapon.
 * Confusing either pair, in either direction, is the whole risk here — the first stalls units
 * forever, the second refunds away units that could still fight.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Mods.Common.Test
{
	[TestFixture]
	public class ResupplyAutoFallbackTest
	{
		// AmmoPoolInfo.DryRearmLeashCells ships 30; anything positive stands for "seeking enabled".
		const bool SeekingEnabled = true;
		const bool SeekingDisabled = false;

		const bool CanMove = true;
		const bool Immobile = false;

		// AmmoPool.AllPoolsEmpty — "can fire nothing at all". The ENCLOSING path triggers on the wider
		// OutOfEssentialAmmo, so StillArmed is a unit that has lost its defining weapon but not every
		// weapon: a rifleman holding an unfired RPG round, a tunguska out of SAMs with a full cannon.
		const bool WhollyDry = true;
		const bool StillArmed = false;

		const bool HostExists = true;
		const bool NoHost = false;

		const bool WithinLeash = true;
		const bool BeyondLeash = false;

		// "Can the host close the gap itself?" In the shipped corpus this is mobility: truk and
		// supplycache move, logisticscenter is a building.
		const bool HostIsMobile = true;
		const bool HostIsStatic = false;

		/// <summary>
		/// THE REPORTED BUG. iskander (vehicles-russia.yaml:945) declares
		/// `RearmActors: logisticscenter` and nothing else, and inherits `InitialResupplyBehavior: Auto`
		/// from defaults.yaml:375. With no Logistics Centre owned, ChooseResupplier returns null.
		/// </summary>
		[Test]
		public void NoRearmActorAtAllEvacuates()
		{
			Assert.That(
				SupplyHuntMath.DecideAutoDisposition(CanMove, WhollyDry, NoHost, SeekingEnabled, BeyondLeash, HostIsStatic),
				Is.EqualTo(SupplyHuntMath.DryAutoDisposition.Evacuate),
				"an Auto unit with no rearm actor in the world must leave, not stand still with its hand up");
		}

		/// <summary>
		/// The leash is irrelevant when there is nothing to measure to: with no host, "how far" has no
		/// answer, so every leash reading must still evacuate. Guards against a future refactor that
		/// reaches for the distance before checking existence.
		/// </summary>
		[Test]
		public void NoRearmActorEvacuatesWhateverTheLeashSays()
		{
			foreach (var seeking in new[] { SeekingEnabled, SeekingDisabled })
				foreach (var within in new[] { WithinLeash, BeyondLeash })
					foreach (var mobile in new[] { HostIsMobile, HostIsStatic })
						Assert.That(
							SupplyHuntMath.DecideAutoDisposition(CanMove, WhollyDry, NoHost, seeking, within, mobile),
							Is.EqualTo(SupplyHuntMath.DryAutoDisposition.Evacuate),
							$"no host ⇒ evacuate (seeking={seeking}, within={within}, mobile={mobile})");
		}

		/// <summary>The unchanged happy path: a host we can reach is still driven to.</summary>
		[Test]
		public void ReachableHostIsStillSought()
		{
			Assert.That(
				SupplyHuntMath.DecideAutoDisposition(CanMove, WhollyDry, HostExists, SeekingEnabled, WithinLeash, HostIsStatic),
				Is.EqualTo(SupplyHuntMath.DryAutoDisposition.SeekRearm),
				"a Logistics Centre inside the leash is still worth driving to");
		}

		/// <summary>
		/// A static depot beyond the leash can never close the gap, so NeedsResupply has no possible
		/// reader — the flag's ONLY consumer is SupplyProvider.FindNeedsResupplyTarget, swept by a
		/// Hunt-stance provider that then drives to us. This is the manager's named failure mode:
		/// "a unit that refuses to evacuate because a Logistics Centre technically exists on the far
		/// side of an unreachable map".
		/// </summary>
		[Test]
		public void DistantStaticHostEvacuates()
		{
			Assert.That(
				SupplyHuntMath.DecideAutoDisposition(CanMove, WhollyDry, HostExists, SeekingEnabled, BeyondLeash, HostIsStatic),
				Is.EqualTo(SupplyHuntMath.DryAutoDisposition.Evacuate),
				"an out-of-leash building cannot come to us, so waiting for it never terminates");
		}

		/// <summary>
		/// THE REGRESSION GUARD, and the reason mobility is a parameter at all. Infantry name
		/// `RearmActors: truk, supplycache, logisticscenter` (infantry.yaml:1162) and a truck genuinely
		/// does drive to flagged units. A soldier out of leash from a truck must keep today's
		/// stay-put-and-flag behaviour; turning HIM into an evacuation would throw away a working
		/// mechanism to fix a different unit's bug.
		/// </summary>
		[Test]
		public void DistantMobileHostStillHoldsAndFlags()
		{
			Assert.That(
				SupplyHuntMath.DecideAutoDisposition(CanMove, WhollyDry, HostExists, SeekingEnabled, BeyondLeash, HostIsMobile),
				Is.EqualTo(SupplyHuntMath.DryAutoDisposition.HoldAndFlag),
				"a truck can come to us, so raising NeedsResupply is a real plan and not a stall");
		}

		/// <summary>
		/// ZERO-SEMANTICS GUARD. AmmoPoolInfo.DryRearmLeashCells at 0 or less is documented as "a dry
		/// unit never self-dispatches, only flags" — an instruction not to TRAVEL. It must not be read
		/// as licence to leave the map, which would convert an opt-out of one behaviour into an opt-in
		/// to a louder one.
		/// </summary>
		[Test]
		public void DisabledSeekingHoldsRatherThanEvacuating()
		{
			foreach (var mobile in new[] { HostIsMobile, HostIsStatic })
				Assert.That(
					SupplyHuntMath.DecideAutoDisposition(CanMove, WhollyDry, HostExists, SeekingDisabled, BeyondLeash, mobile),
					Is.EqualTo(SupplyHuntMath.DryAutoDisposition.HoldAndFlag),
					$"leash<=0 means 'do not travel', not 'leave the map' (mobile={mobile})");
		}

		/// <summary>
		/// An immobile actor can reach neither a host nor the map edge; issuing either order would only
		/// cancel whatever it was doing. Mirrors AmmoEvacMath.Decide's canMove guard.
		/// </summary>
		[Test]
		public void ImmobileActorIsNeverSentAnywhere()
		{
			foreach (var host in new[] { HostExists, NoHost })
				foreach (var within in new[] { WithinLeash, BeyondLeash })
					foreach (var mobile in new[] { HostIsMobile, HostIsStatic })
						Assert.That(
							SupplyHuntMath.DecideAutoDisposition(Immobile, WhollyDry, host, SeekingEnabled, within, mobile),
							Is.EqualTo(SupplyHuntMath.DryAutoDisposition.HoldAndFlag),
							$"immobile ⇒ leave it alone (host={host}, within={within}, mobile={mobile})");
		}

		/// <summary>
		/// THE OVER-REACH GUARD. The enclosing path fires on OutOfEssentialAmmo, which is TRUE for a
		/// unit that can still shoot something — WORKSPACE/balance/260821-essential-ammo-pools.md rules
		/// the rifleman's rifle Essential and his RPG not, and the tunguska's SAMs Essential and its
		/// cannon not. Seeking is recoverable; evacuation is TERMINAL and refunds the unit away. A unit
		/// that can still fire must never be spent that way, whatever the host situation.
		/// </summary>
		[Test]
		public void StillArmedUnitIsNeverEvacuated()
		{
			foreach (var host in new[] { HostExists, NoHost })
				foreach (var seeking in new[] { SeekingEnabled, SeekingDisabled })
					foreach (var within in new[] { WithinLeash, BeyondLeash })
						foreach (var mobile in new[] { HostIsMobile, HostIsStatic })
							Assert.That(
								SupplyHuntMath.DecideAutoDisposition(CanMove, StillArmed, host, seeking, within, mobile),
								Is.Not.EqualTo(SupplyHuntMath.DryAutoDisposition.Evacuate),
								$"a unit that can still fire is not spent for a refund (host={host}, seeking={seeking}, within={within}, mobile={mobile})");
		}

		/// <summary>
		/// The other half of the tier: losing the Essential weapon still sends the unit to a host it can
		/// reach. Only the EVACUATION tier is gated on being wholly dry, not the seek.
		/// </summary>
		[Test]
		public void StillArmedUnitStillSeeksAReachableHost()
		{
			Assert.That(
				SupplyHuntMath.DecideAutoDisposition(CanMove, StillArmed, HostExists, SeekingEnabled, WithinLeash, HostIsStatic),
				Is.EqualTo(SupplyHuntMath.DryAutoDisposition.SeekRearm),
				"essential-dry with a reachable depot still tops up — the seek tier is unchanged");

			Assert.That(
				SupplyHuntMath.DecideAutoDisposition(CanMove, StillArmed, NoHost, SeekingEnabled, BeyondLeash, HostIsStatic),
				Is.EqualTo(SupplyHuntMath.DryAutoDisposition.HoldAndFlag),
				"and with no host it keeps today's flag-and-stay rather than leaving");
		}

		/// <summary>
		/// TOTALITY. Every one of the 64 input combinations returns one of the three declared
		/// dispositions, and a mobile unit is never left in HoldAndFlag when nothing at all can serve
		/// it — the precise shape of the original complaint.
		/// </summary>
		[Test]
		public void EveryCombinationDecidesAndNeverStrandsAMobileUnit()
		{
			foreach (var canMove in new[] { CanMove, Immobile })
				foreach (var dry in new[] { WhollyDry, StillArmed })
					foreach (var host in new[] { HostExists, NoHost })
						foreach (var seeking in new[] { SeekingEnabled, SeekingDisabled })
							foreach (var within in new[] { WithinLeash, BeyondLeash })
								foreach (var mobile in new[] { HostIsMobile, HostIsStatic })
								{
									var action = SupplyHuntMath.DecideAutoDisposition(canMove, dry, host, seeking, within, mobile);

									Assert.That(action, Is.AnyOf(
										SupplyHuntMath.DryAutoDisposition.SeekRearm,
										SupplyHuntMath.DryAutoDisposition.HoldAndFlag,
										SupplyHuntMath.DryAutoDisposition.Evacuate));

									if (canMove && dry && !host)
										Assert.That(action, Is.EqualTo(SupplyHuntMath.DryAutoDisposition.Evacuate),
											$"a mobile, wholly dry unit with no host must always leave (seeking={seeking}, within={within}, mobile={mobile})");
								}
		}

		/// <summary>
		/// Pins the DELIBERATE divergence from AmmoEvacMath.Decide, which answers a near-identical
		/// question for the bot module. Its budget parameter reads 0 as UNLIMITED
		/// (PoiOffensiveBotModule.OutOfAmmoRearmSeekRadiusCells); the unit-side leash reads 0 as
		/// "admits nothing". Two opposite conventions for one idea already exist in this codebase, and
		/// this test exists so that anyone who tries to "unify" the two functions fails here first and
		/// reads why.
		/// </summary>
		[Test]
		public void BotAndUnitSideZeroSemanticsStayOpposite()
		{
			Assert.That(AmmoEvacMath.Decide(true, true, true, 500, 0), Is.EqualTo(AmmoEvacAction.SeekRearm),
				"bot side: a 0 budget means UNLIMITED, so a distant source is still sought");

			Assert.That(SupplyHuntMath.WithinCellBudget(500, 0, 0), Is.False,
				"unit side: a 0 leash admits nothing");

			Assert.That(
				SupplyHuntMath.DecideAutoDisposition(CanMove, WhollyDry, HostExists, SeekingDisabled, BeyondLeash, HostIsStatic),
				Is.EqualTo(SupplyHuntMath.DryAutoDisposition.HoldAndFlag),
				"and a 0 leash on the unit side holds rather than seeking OR evacuating");
		}
	}
}
