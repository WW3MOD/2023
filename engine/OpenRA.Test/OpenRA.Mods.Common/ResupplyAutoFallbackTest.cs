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
 * The fixture is built around the distinctions that must NOT be collapsed into each other, because
 * every one of them has already been got wrong once:
 *   * a host that is DRAINED versus one that is ABSENT. Review of the first cut found the call site
 *     computed "no host" from ChooseResupplier, which filters on CurrentSupply > 0 — and since
 *     RearmsUnits appears nowhere in mods/ww3mod, that filter applies to every host in the game. An
 *     emptied Logistics Centre therefore read as "no depot exists" and units were spent against a
 *     condition AbsorbsSupplyCache clears. Case 6 below is that defect.
 *   * a host that is merely FAR versus one that CANNOT MOVE. Only the second is hopeless.
 *   * a unit that can fire NOTHING versus one that has merely lost its defining weapon.
 *   * a unit whose depot is MISSING versus one that never had a Rearmable at all (^CrewMember).
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

		// Whether the actor declares any RearmActors. False for ^CrewMember and every ejected crewman.
		const bool NamesRearmActors = true;
		const bool NamesNoRearmActors = false;

		// The seek trigger, and the ONLY input that reads current stock: a host that can afford a batch
		// we need, inside the leash.
		const bool CanBeServedNow = true;
		const bool NothingCanServeUsNow = false;

		// Hope inputs. Both ignore stock — a drained depot still EXISTS, and a drained truck still moves.
		const bool ADepotIsNearby = true;
		const bool NoDepotNearby = false;
		const bool SomethingCanDriveToUs = true;
		const bool NothingCanDriveToUs = false;

		/// <summary>
		/// THE REPORTED BUG. iskander (vehicles-russia.yaml:945) declares
		/// `RearmActors: logisticscenter` and nothing else, and inherits `InitialResupplyBehavior: Auto`
		/// from defaults.yaml:375. With no Logistics Centre owned at all, nothing exists to serve it,
		/// sit beside, or drive to it.
		/// </summary>
		[Test]
		public void NoRearmActorAtAllEvacuates()
		{
			Assert.That(
				SupplyHuntMath.DecideAutoDisposition(CanMove, WhollyDry, NamesRearmActors, SeekingEnabled,
					NothingCanServeUsNow, NoDepotNearby, NothingCanDriveToUs),
				Is.EqualTo(SupplyHuntMath.DryAutoDisposition.Evacuate),
				"an Auto unit with no rearm actor in the world must leave, not stand still with its hand up");
		}

		/// <summary>
		/// THE DEFECT THE FIRST CUT SHIPPED — the twelfth case, added on review. A Logistics Centre that
		/// exists and is right there but holds no supply is NOT the condition the evacuation was designed
		/// for. It is recoverable: AbsorbsSupplyCache calls SupplyProvider.AddSupply from nearby caches.
		/// And it is the ROUTINE state rather than an edge case — the iskander's pool is
		/// `SupplyValue: 1500` against the LC's `TotalSupply: 2250`, so one LC cannot fill one Iskander
		/// twice and CurrentSupply == 0 is where it normally ends up. Evacuating here spends a 6000-credit
		/// unit permanently against a condition one supply truck clears.
		/// </summary>
		[Test]
		public void DrainedButPresentDepotHoldsRatherThanEvacuating()
		{
			Assert.That(
				SupplyHuntMath.DecideAutoDisposition(CanMove, WhollyDry, NamesRearmActors, SeekingEnabled,
					NothingCanServeUsNow, ADepotIsNearby, NothingCanDriveToUs),
				Is.EqualTo(SupplyHuntMath.DryAutoDisposition.HoldAndFlag),
				"an empty depot we are standing next to is one supply transfer from serving — do not spend the unit");
		}

		/// <summary>
		/// The same distinction from the other side: drained AND far AND static is genuinely hopeless,
		/// so the fix above must not have blunted the feature into never firing.
		/// </summary>
		[Test]
		public void DrainedDistantStaticDepotStillEvacuates()
		{
			Assert.That(
				SupplyHuntMath.DecideAutoDisposition(CanMove, WhollyDry, NamesRearmActors, SeekingEnabled,
					NothingCanServeUsNow, NoDepotNearby, NothingCanDriveToUs),
				Is.EqualTo(SupplyHuntMath.DryAutoDisposition.Evacuate),
				"nothing near, nothing mobile — waiting here never terminates whatever the stock levels do");
		}

		/// <summary>The unchanged happy path: a host that can serve us and is close enough is driven to.</summary>
		[Test]
		public void ReachableSuppliedHostIsStillSought()
		{
			Assert.That(
				SupplyHuntMath.DecideAutoDisposition(CanMove, WhollyDry, NamesRearmActors, SeekingEnabled,
					CanBeServedNow, ADepotIsNearby, NothingCanDriveToUs),
				Is.EqualTo(SupplyHuntMath.DryAutoDisposition.SeekRearm),
				"a Logistics Centre inside the leash with stock in it is still worth driving to");
		}

		/// <summary>
		/// THE REGRESSION GUARD, and the reason mobility is an input at all. Infantry name
		/// `RearmActors: truk, supplycache, logisticscenter` (infantry.yaml:1162) and a truck genuinely
		/// does drive to flagged units. A soldier out of leash from a truck must keep today's
		/// stay-put-and-flag behaviour; turning HIM into an evacuation would throw away a working
		/// mechanism to fix a different unit's bug.
		/// </summary>
		[Test]
		public void DistantMobileHostStillHoldsAndFlags()
		{
			Assert.That(
				SupplyHuntMath.DecideAutoDisposition(CanMove, WhollyDry, NamesRearmActors, SeekingEnabled,
					NothingCanServeUsNow, NoDepotNearby, SomethingCanDriveToUs),
				Is.EqualTo(SupplyHuntMath.DryAutoDisposition.HoldAndFlag),
				"a truck can come to us, so raising NeedsResupply is a real plan and not a stall");
		}

		/// <summary>
		/// FIX 3 — ^CrewMember (crew.yaml:4) inherits ^CamoSoldier → ^Soldier → ^Infantry and NO template
		/// in that chain declares Rearmable, so its candidate set is permanently empty. That is not a unit
		/// whose depot is missing; it is one that was never meant to be rearmed, carrying a one-shot
		/// pistol allowance. Without this guard every ejected crewman who empties his pistol cancels what
		/// he is doing and walks off the map from wherever his vehicle just died.
		/// </summary>
		[Test]
		public void UnitThatNamesNoRearmActorsIsNeverEvacuated()
		{
			foreach (var seeking in new[] { SeekingEnabled, SeekingDisabled })
				foreach (var near in new[] { ADepotIsNearby, NoDepotNearby })
					foreach (var mobile in new[] { SomethingCanDriveToUs, NothingCanDriveToUs })
						Assert.That(
							SupplyHuntMath.DecideAutoDisposition(CanMove, WhollyDry, NamesNoRearmActors, seeking,
								NothingCanServeUsNow, near, mobile),
							Is.EqualTo(SupplyHuntMath.DryAutoDisposition.HoldAndFlag),
							$"a unit with no Rearmable has no depot to be missing (seeking={seeking}, near={near}, mobile={mobile})");
		}

		/// <summary>
		/// ZERO-SEMANTICS GUARD. AmmoPoolInfo.DryRearmLeashCells at 0 or less is documented as "a dry
		/// unit never self-dispatches, only flags" — an instruction not to TRAVEL. It must not be read as
		/// licence to leave the map. Pinned across the no-host case too, which is where the first cut got
		/// it wrong: it checked the absent-host branch BEFORE the disabled-leash branch, so a 0 leash did
		/// escalate to evacuation and the commit message's claim to the contrary was false.
		/// </summary>
		[Test]
		public void DisabledSeekingHoldsRatherThanEvacuating()
		{
			foreach (var near in new[] { ADepotIsNearby, NoDepotNearby })
				foreach (var mobile in new[] { SomethingCanDriveToUs, NothingCanDriveToUs })
					Assert.That(
						SupplyHuntMath.DecideAutoDisposition(CanMove, WhollyDry, NamesRearmActors, SeekingDisabled,
							NothingCanServeUsNow, near, mobile),
						Is.EqualTo(SupplyHuntMath.DryAutoDisposition.HoldAndFlag),
						$"leash<=0 means 'do not travel', not 'leave the map' (near={near}, mobile={mobile})");
		}

		/// <summary>
		/// An immobile actor can reach neither a host nor the map edge; issuing either order would only
		/// cancel whatever it was doing. Mirrors AmmoEvacMath.Decide's canMove guard.
		/// </summary>
		[Test]
		public void ImmobileActorIsNeverSentAnywhere()
		{
			foreach (var served in new[] { CanBeServedNow, NothingCanServeUsNow })
				foreach (var near in new[] { ADepotIsNearby, NoDepotNearby })
					foreach (var mobile in new[] { SomethingCanDriveToUs, NothingCanDriveToUs })
						Assert.That(
							SupplyHuntMath.DecideAutoDisposition(Immobile, WhollyDry, NamesRearmActors, SeekingEnabled,
								served, near, mobile),
							Is.EqualTo(SupplyHuntMath.DryAutoDisposition.HoldAndFlag),
							$"immobile ⇒ leave it alone (served={served}, near={near}, mobile={mobile})");
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
			foreach (var seeking in new[] { SeekingEnabled, SeekingDisabled })
				foreach (var served in new[] { CanBeServedNow, NothingCanServeUsNow })
					foreach (var near in new[] { ADepotIsNearby, NoDepotNearby })
						foreach (var mobile in new[] { SomethingCanDriveToUs, NothingCanDriveToUs })
							Assert.That(
								SupplyHuntMath.DecideAutoDisposition(CanMove, StillArmed, NamesRearmActors, seeking,
									served, near, mobile),
								Is.Not.EqualTo(SupplyHuntMath.DryAutoDisposition.Evacuate),
								$"a unit that can still fire is not spent for a refund (seeking={seeking}, served={served}, near={near}, mobile={mobile})");
		}

		/// <summary>
		/// The other half of the tier: losing the Essential weapon still sends the unit to a host that can
		/// serve it. Only the EVACUATION tier is gated on being wholly dry, not the seek.
		/// </summary>
		[Test]
		public void StillArmedUnitStillSeeksAReachableHost()
		{
			Assert.That(
				SupplyHuntMath.DecideAutoDisposition(CanMove, StillArmed, NamesRearmActors, SeekingEnabled,
					CanBeServedNow, ADepotIsNearby, NothingCanDriveToUs),
				Is.EqualTo(SupplyHuntMath.DryAutoDisposition.SeekRearm),
				"essential-dry with a reachable stocked depot still tops up — the seek tier is unchanged");

			Assert.That(
				SupplyHuntMath.DecideAutoDisposition(CanMove, StillArmed, NamesRearmActors, SeekingEnabled,
					NothingCanServeUsNow, NoDepotNearby, NothingCanDriveToUs),
				Is.EqualTo(SupplyHuntMath.DryAutoDisposition.HoldAndFlag),
				"and with nothing to reach it keeps today's flag-and-stay rather than leaving");
		}

		/// <summary>
		/// TOTALITY over all 128 input combinations. Asserts the two directions that matter: evacuation
		/// happens on EXACTLY the hopeless conjunction and nowhere else, and every input returns one of
		/// the three declared dispositions.
		/// </summary>
		[Test]
		public void EvacuationFiresOnExactlyTheHopelessCase()
		{
			foreach (var canMove in new[] { CanMove, Immobile })
				foreach (var dry in new[] { WhollyDry, StillArmed })
					foreach (var names in new[] { NamesRearmActors, NamesNoRearmActors })
						foreach (var seeking in new[] { SeekingEnabled, SeekingDisabled })
							foreach (var served in new[] { CanBeServedNow, NothingCanServeUsNow })
								foreach (var near in new[] { ADepotIsNearby, NoDepotNearby })
									foreach (var mobile in new[] { SomethingCanDriveToUs, NothingCanDriveToUs })
									{
										var action = SupplyHuntMath.DecideAutoDisposition(
											canMove, dry, names, seeking, served, near, mobile);

										Assert.That(action, Is.AnyOf(
											SupplyHuntMath.DryAutoDisposition.SeekRearm,
											SupplyHuntMath.DryAutoDisposition.HoldAndFlag,
											SupplyHuntMath.DryAutoDisposition.Evacuate));

										var hopeless = canMove && dry && names && seeking && !served && !near && !mobile;
										Assert.That(action == SupplyHuntMath.DryAutoDisposition.Evacuate, Is.EqualTo(hopeless),
											$"evacuate iff hopeless (canMove={canMove}, dry={dry}, names={names}, "
											+ $"seeking={seeking}, served={served}, near={near}, mobile={mobile})");
									}
		}

		/// <summary>
		/// THE TWO-DEPOT CASE, from second-pass review. Testing affordability on the already-chosen
		/// nearest host strands a unit that had a usable depot: two owned Logistics Centres, LC-A at 3
		/// cells holding 750 and LC-B at 8 cells holding 2250, against an iskander whose batch costs
		/// 1500. Pick-then-filter selects LC-A, finds it cannot pay, and concludes nothing can serve us
		/// — while LC-B sits eight cells away fully stocked. Filter-then-pick selects LC-B.
		/// </summary>
		[Test]
		public void NearestAffordableDepotWinsOverNearerPoorOne()
		{
			// Distances are squared, as ClosestToIgnoringPath compares them: 3c and 8c in WDist.
			var lcA = new SupplyHuntMath.Candidate(3L * 1024 * 3 * 1024, 1);
			var lcB = new SupplyHuntMath.Candidate(8L * 1024 * 8 * 1024, 2);

			var chosen = SupplyHuntMath.SelectNearestAffordable(
				new[] { lcA, lcB }, new[] { false, true });

			Assert.That(chosen, Is.EqualTo(1),
				"the stocked depot 8 cells out must beat the 750-supply depot at 3 cells");

			Assert.That(SupplyHuntMath.SelectNearestAffordable(new[] { lcA, lcB }, new[] { false, false }),
				Is.EqualTo(-1), "no affordable depot ⇒ nothing to seek");

			Assert.That(SupplyHuntMath.SelectNearestAffordable(new[] { lcA, lcB }, new[] { true, true }),
				Is.EqualTo(0), "with both affordable the nearer one still wins");
		}

		/// <summary>
		/// Equidistant affordable depots must resolve by ActorID, not by enumeration order, or two
		/// clients can dispatch the same unit to different depots and desync. Mirrors SelectNearest.
		/// </summary>
		[Test]
		public void EquidistantAffordableDepotsBreakOnActorId()
		{
			var high = new SupplyHuntMath.Candidate(5L * 1024 * 5 * 1024, 77);
			var low = new SupplyHuntMath.Candidate(5L * 1024 * 5 * 1024, 12);

			Assert.That(SupplyHuntMath.SelectNearestAffordable(new[] { high, low }, new[] { true, true }),
				Is.EqualTo(1), "lower ActorID wins regardless of order encountered");
			Assert.That(SupplyHuntMath.SelectNearestAffordable(new[] { low, high }, new[] { true, true }),
				Is.EqualTo(0));
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
				SupplyHuntMath.DecideAutoDisposition(CanMove, WhollyDry, NamesRearmActors, SeekingDisabled,
					NothingCanServeUsNow, NoDepotNearby, NothingCanDriveToUs),
				Is.EqualTo(SupplyHuntMath.DryAutoDisposition.HoldAndFlag),
				"and a 0 leash on the unit side holds rather than seeking OR evacuating");
		}
	}
}
