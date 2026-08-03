#region Copyright & License Information
/*
 * WW3MOD careful-scout recon-safety test.
 *
 * Pins ReconSafetyMath — the gate that stops the fragile littlebird scout diving deep into unscouted
 * territory or flying through a believed anti-air envelope and getting shot down. A candidate recon
 * cell is accepted iff it is within the penetration cap, its destination is outside believed AA, and
 * the straight flight path to it does not cross believed AA. Fog-legal by construction: the caller only
 * ever feeds it readings sampled from the belief-derived danger field (0 for unscouted cells), never an
 * omniscient read. Pure integer math; no world mounted; deterministic.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class ReconSafetyMathTest
	{
		const int Safe = 0;            // strictly outside every believed AA envelope
		const long Cap = 40L * 40L;    // 40-cell penetration cap, squared

		[Test]
		public void SafeCellWithinCap_Accepted()
		{
			// Unscouted / AA-free destination and path, comfortably within the cap ⇒ a survivable recon leg.
			Assert.That(
				ReconSafetyMath.Acceptable(destAirDanger: 0, pathMaxAirDanger: 0, safeThreshold: Safe,
					distFromHomeSq: 20L * 20L, maxReconRadiusSq: Cap),
				Is.True);
		}

		[Test]
		public void BeyondPenetrationCap_Rejected()
		{
			// Even a totally AA-free far cell is rejected: a lone scout must not blindly penetrate deep into
			// the unscouted enemy backfield (the geometry bound before contact).
			Assert.That(
				ReconSafetyMath.Acceptable(0, 0, Safe, distFromHomeSq: 41L * 41L, maxReconRadiusSq: Cap),
				Is.False);
		}

		[Test]
		public void DangerousDestination_Rejected()
		{
			// Destination sits inside a believed AA envelope ⇒ do not recon INTO it, however near it is.
			Assert.That(
				ReconSafetyMath.Acceptable(destAirDanger: 30, pathMaxAirDanger: 0, safeThreshold: Safe,
					distFromHomeSq: 10L * 10L, maxReconRadiusSq: Cap),
				Is.False);
		}

		[Test]
		public void DangerousPath_Rejected()
		{
			// Destination is clear but the straight flight would cross believed AA ⇒ reject the route.
			Assert.That(
				ReconSafetyMath.Acceptable(destAirDanger: 0, pathMaxAirDanger: 50, safeThreshold: Safe,
					distFromHomeSq: 10L * 10L, maxReconRadiusSq: Cap),
				Is.False);
		}

		[Test]
		public void ThresholdIsInclusive()
		{
			// Danger exactly AT the threshold is treated as safe (the code rejects only strictly-above), so a
			// non-zero threshold admits grazing an envelope edge.
			Assert.That(
				ReconSafetyMath.Acceptable(destAirDanger: 10, pathMaxAirDanger: 10, safeThreshold: 10,
					distFromHomeSq: 10L * 10L, maxReconRadiusSq: Cap),
				Is.True);
		}

		[Test]
		public void CapDisabled_AllowsAnyDistance()
		{
			// maxReconRadiusSq = 0 disables the geometry gate: a far but AA-free cell is then accepted, leaving
			// only the danger gates. (The frozen path never calls this at all — the module skips it lever-off.)
			Assert.That(
				ReconSafetyMath.Acceptable(0, 0, Safe, distFromHomeSq: 1000L * 1000L, maxReconRadiusSq: 0),
				Is.True);
		}

		[Test]
		public void AllThreeGatesMustPass()
		{
			// A cell that fails every gate is rejected; flip each to safe in turn and it stays rejected until
			// ALL three pass — the acceptance is a conjunction.
			Assert.Multiple(() =>
			{
				Assert.That(ReconSafetyMath.Acceptable(30, 30, Safe, 41L * 41L, Cap), Is.False);
				Assert.That(ReconSafetyMath.Acceptable(30, 30, Safe, 10L * 10L, Cap), Is.False, "danger still fails");
				Assert.That(ReconSafetyMath.Acceptable(0, 30, Safe, 10L * 10L, Cap), Is.False, "path still fails");
				Assert.That(ReconSafetyMath.Acceptable(0, 0, Safe, 10L * 10L, Cap), Is.True, "all pass");
			});
		}

		[Test]
		public void DecisionsAreDeterministic()
		{
			Assert.That(
				ReconSafetyMath.Acceptable(0, 0, Safe, 20L * 20L, Cap),
				Is.EqualTo(ReconSafetyMath.Acceptable(0, 0, Safe, 20L * 20L, Cap)));
		}
	}
}
