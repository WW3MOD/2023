#region Copyright & License Information
/*
 * WW3MOD capture-escort right-sizing (@experimental) — escort-tier bucketing test.
 *
 * Pins the decision CaptureCoordinatorBotModule.DispatchEscort turns into an escort count when
 * EscortTierSizingEnabled is on, so "safe near-SR derricks are captured with no/minimal escort while
 * contested ones keep the full party" can't silently regress:
 *   (1) FULL-FIRST — a believed weapon envelope OR a deep-enemy ring reads FULL even when the other
 *       signals look calm, so a hot target is never shrunk.
 *   (2) NONE — strongly-ours ring + low danger + near SR sends the technician alone.
 *   (3) DISTANCE GATE — a far-but-safe target is LIGHT, not NONE; the gate can be disabled with <= 0.
 *   (4) LIGHT — the in-between band, and unknown distance never grants NONE.
 * Pure integer bucketing; no world mounted.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class EscortSizingMathTest
	{
		// Threshold set reused across the pins (mirrors the @experimental tuning shape):
		//   strongly-ours ring >= 300, low danger <= 40, near SR <= 24 cells,
		//   deep-enemy band = GrayBand 150 (ring < -150 ⇒ FULL), contested danger > 40.
		const int SafeControl = 300;
		const int SafeDanger = 40;
		const int SafeDist = 24;
		const int Band = 150;
		const int ContestedDanger = 40;

		static EscortSizingMath.EscortTier Resolve(int control, int danger, int dist)
			=> EscortSizingMath.Resolve(control, danger, dist,
				SafeControl, SafeDanger, SafeDist, Band, ContestedDanger);

		[Test]
		public void Full_WhenBelievedWeaponEnvelopeReachesTheCell()
		{
			// Danger over the contested threshold ⇒ FULL, even though the ring reads strongly ours and it's near SR.
			Assert.That(Resolve(800, ContestedDanger + 1, 2), Is.EqualTo(EscortSizingMath.EscortTier.Full),
				"a believed weapon envelope forces the full escort regardless of ownership/distance");
		}

		[Test]
		public void Full_WhenRingReadsDeepEnemy()
		{
			// Ring average strictly below -GrayBand ⇒ deep believed-enemy surroundings ⇒ FULL.
			Assert.That(Resolve(-(Band + 1), 0, 40), Is.EqualTo(EscortSizingMath.EscortTier.Full),
				"deep-enemy surroundings force the full escort even with zero sampled danger");

			// Exactly -GrayBand is NOT deep-enemy (strict test) — contested front, falls through to LIGHT here.
			Assert.That(Resolve(-Band, 0, 40), Is.EqualTo(EscortSizingMath.EscortTier.Light),
				"ring at exactly -GrayBand is contested-front, not deep-enemy");
		}

		[Test]
		public void None_WhenStronglyOursLowDangerNearSR()
		{
			Assert.That(Resolve(SafeControl, SafeDanger, SafeDist), Is.EqualTo(EscortSizingMath.EscortTier.None),
				"boundary values (>= control, <= danger, <= distance) all count as safe ⇒ technician alone");

			Assert.That(Resolve(1000, 0, 0), Is.EqualTo(EscortSizingMath.EscortTier.None),
				"a derrick on our doorstep in fully-held territory is NONE");
		}

		[Test]
		public void Light_WhenOursButFarFromSR()
		{
			// Strongly ours + calm, but beyond the near-SR distance ⇒ not NONE, a small escort still goes.
			Assert.That(Resolve(SafeControl, 0, SafeDist + 1), Is.EqualTo(EscortSizingMath.EscortTier.Light),
				"a safe-but-distant capture keeps a light escort (the near-SR gate)");
		}

		[Test]
		public void Light_WhenOwnershipContestedButNotDeepEnemy()
		{
			// Ring in the gray band (|score| <= GrayBand), danger calm ⇒ neither NONE nor FULL ⇒ LIGHT.
			Assert.That(Resolve(0, 0, 2), Is.EqualTo(EscortSizingMath.EscortTier.Light),
				"a contested-front derrick with no sampled danger is a light-escort target");

			// Mildly ours but below the strongly-ours bar ⇒ LIGHT.
			Assert.That(Resolve(SafeControl - 1, 0, 2), Is.EqualTo(EscortSizingMath.EscortTier.Light),
				"ownership just short of the strongly-ours bar is LIGHT, not NONE");
		}

		[Test]
		public void UnknownDistance_NeverGrantsNone()
		{
			// Negative distance = unknown (legacy no-PoiMap path). Strongly ours + calm but distance unknown ⇒ LIGHT.
			Assert.That(Resolve(1000, 0, -1), Is.EqualTo(EscortSizingMath.EscortTier.Light),
				"unknown distance fails the near-SR gate ⇒ LIGHT, so we never send a lone capturer on a guess");
		}

		[Test]
		public void EscortCount_IsReductionOnly()
		{
			Assert.Multiple(() =>
			{
				// None sends the technician alone; Full leaves the pre-lever want untouched.
				Assert.That(EscortSizingMath.ResolveEscortCount(2, EscortSizingMath.EscortTier.None, 2), Is.EqualTo(0),
					"NONE reserves no combat units");
				Assert.That(EscortSizingMath.ResolveEscortCount(3, EscortSizingMath.EscortTier.Full, 1), Is.EqualTo(3),
					"FULL keeps the (possibly contested-larger) escort");

				// Light clamps to min(want, lightSize) — and CRUCIALLY never rises above the pre-lever want even if
				// LightEscortSize is mis-tuned above it. This is the reduction-only guarantee the Math.Min encodes.
				Assert.That(EscortSizingMath.ResolveEscortCount(2, EscortSizingMath.EscortTier.Light, 1), Is.EqualTo(1),
					"LIGHT shrinks toward the small size");
				Assert.That(EscortSizingMath.ResolveEscortCount(1, EscortSizingMath.EscortTier.Light, 5), Is.EqualTo(1),
					"LIGHT never RAISES: min clamps to the pre-lever want even when LightEscortSize exceeds it");

				// The invariant, exhaustively across every tier and a range of sizes: the resolved count never
				// exceeds the pre-lever want — the lever is provably incapable of enlarging an escort.
				foreach (var tier in new[] { EscortSizingMath.EscortTier.None, EscortSizingMath.EscortTier.Light, EscortSizingMath.EscortTier.Full })
					for (var want = 0; want <= 4; want++)
						for (var light = 0; light <= 6; light++)
							Assert.That(EscortSizingMath.ResolveEscortCount(want, tier, light), Is.LessThanOrEqualTo(want),
								$"tier={tier} want={want} light={light} must never raise the escort");
			});
		}

		[Test]
		public void DistanceGateDisabled_AllowsNoneAtAnyDistance()
		{
			// safeMaxDistanceFromSRCells <= 0 disables the distance gate entirely.
			var tier = EscortSizingMath.Resolve(1000, 0, 999,
				SafeControl, SafeDanger, safeMaxDistanceFromSRCells: 0,
				contestedControlBand: Band, contestedDangerThreshold: ContestedDanger);
			Assert.That(tier, Is.EqualTo(EscortSizingMath.EscortTier.None),
				"with the distance gate off, a strongly-ours calm target is NONE at any distance");
		}

		// ---------- AtLeast (the reclaim escort floor) ----------

		[Test]
		public void AtLeast_RaisesNoneToTheFloor()
		{
			// THE RECLAIM CASE. A just-evicted structure reads verified-safe precisely BECAUSE it was evicted —
			// the building was our vision source, so losing it blinds us, and believed danger decays away long
			// before the believed control anchoring the cell does. Resolve honestly returns None on that read;
			// the reclaim caller must refuse it rather than walk a lone technician into the raid.
			Assert.That(
				EscortSizingMath.AtLeast(EscortSizingMath.EscortTier.None, EscortSizingMath.EscortTier.Light),
				Is.EqualTo(EscortSizingMath.EscortTier.Light));
		}

		[Test]
		public void AtLeast_NeverLowersAnAlreadyHigherTier()
		{
			// A genuinely contested reclaim target keeps its FULL escort — the floor raises, it never caps.
			Assert.That(
				EscortSizingMath.AtLeast(EscortSizingMath.EscortTier.Full, EscortSizingMath.EscortTier.Light),
				Is.EqualTo(EscortSizingMath.EscortTier.Full));
			Assert.That(
				EscortSizingMath.AtLeast(EscortSizingMath.EscortTier.Light, EscortSizingMath.EscortTier.Light),
				Is.EqualTo(EscortSizingMath.EscortTier.Light));
		}

		[Test]
		public void AtLeast_ResultIsNeverLessProtectiveThanEitherInput()
		{
			// Exhaustive over every pair. AtLeast leans on the enum being ordered by protection
			// (None < Light < Full); this is the assertion that goes red if someone reorders it.
			var tiers = new[]
			{
				EscortSizingMath.EscortTier.None, EscortSizingMath.EscortTier.Light, EscortSizingMath.EscortTier.Full
			};

			Assert.Multiple(() =>
			{
				foreach (var tier in tiers)
					foreach (var floor in tiers)
					{
						var result = EscortSizingMath.AtLeast(tier, floor);
						Assert.That(result, Is.GreaterThanOrEqualTo(tier), $"tier={tier} floor={floor}");
						Assert.That(result, Is.GreaterThanOrEqualTo(floor), $"tier={tier} floor={floor}");
					}
			});
		}
	}
}
