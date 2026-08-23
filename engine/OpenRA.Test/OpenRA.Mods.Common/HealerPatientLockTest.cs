#region Copyright & License Information
/*
 * WW3MOD explicit heal order — contract pins for the patient lock.
 *
 * WHAT THESE CAN AND CANNOT PROVE. The defect being fixed is that ordering a medic onto a specific
 * ally chose where he STOOD and not who he TREATED: AttendAlly wraps a follow in an AttackMoveActivity,
 * which re-scans every 10 ticks and hands the attack layer whatever HealerAutoTarget's ranking names.
 * Ordering a man at 70% while someone at 55% was within a cell treated the other man.
 *
 * These pin the ARBITRATION RULE, which is the part that can be stated without a World. They do NOT
 * prove the rule is wired to the order, that the lock is released when the order ends, or that a
 * walking medic keeps his patient. Those need a game.
 *
 * A NOTE ON WHAT A GREEN HERE IS WORTH. Treating is a top-level Attack activity that keeps the medic
 * non-idle for the entire treatment and cannot be preempted, so a test that merely watched a medic
 * finish a patient would pass whether or not a lock existed at all — the incidental behaviour, not the
 * feature. That is why these assert the decision function directly, and why RunsRanking is asserted
 * separately from the decision: the ranking must not RUN, not merely be ignored (SelectPatient
 * reassigns currentTarget as a side effect, which is how a marching medic gets re-pointed by a scan
 * whose answer was discarded).
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class HealerPatientLockTest
	{
		[Test]
		public void AnOrderedPatientIsTreatedRatherThanTheBestRankedOne()
		{
			Assert.That(HealerPatientLock.Resolve(lockHeld: true, patientGone: false, patientNeedsTreatment: true),
				Is.EqualTo(HealerPatientLockDecision.TreatLockedPatient),
				"A player who clicked a specific ally must get that ally treated. Falling through to Rank " +
				"here is the original defect: the order picks where the medic stands and the ranking picks " +
				"who he treats, so a man at 70% is ordered and a man at 55% one cell away is the one healed.");
		}

		[Test]
		public void TheRankingDoesNotEvenRunWhileAPatientIsLocked()
		{
			var decision = HealerPatientLock.Resolve(lockHeld: true, patientGone: false, patientNeedsTreatment: true);

			Assert.That(HealerPatientLock.RunsRanking(decision), Is.False,
				"The lock must suppress the ranking SCAN, not just its result. HealerAutoTarget.SelectPatient " +
				"reassigns currentTarget as a side effect, so ranking-and-discarding still re-points the " +
				"healer — that is exactly the walk-phase bug where a medic arrives at A, goes idle, and is " +
				"walked off to B without ever firing a pulse.");
		}

		[TestCase(false, false)]
		[TestCase(false, true)]
		[TestCase(true, false)]
		[TestCase(true, true)]
		public void AHealerUnderNoOrderRanksExactlyAsBefore(bool patientGone, bool patientNeedsTreatment)
		{
			var decision = HealerPatientLock.Resolve(lockHeld: false, patientGone, patientNeedsTreatment);

			Assert.That(decision, Is.EqualTo(HealerPatientLockDecision.Rank),
				"The lock is an override, not a replacement. A medic nobody has ordered must behave " +
				"identically to the shipped build, or every unattended medic and every bot medic changes.");
			Assert.That(HealerPatientLock.RunsRanking(decision), Is.True);
		}

		[Test]
		public void AnOrderedPatientAtFullHealthFreesTheHealerToTreatOthers()
		{
			var decision = HealerPatientLock.Resolve(lockHeld: true, patientGone: false, patientNeedsTreatment: false);

			Assert.That(decision, Is.EqualTo(HealerPatientLockDecision.Rank),
				"An escort who is not hurt must not block treatment of a casualty at the medic's feet. " +
				"The follow half of the order still holds the medic with his man; only the claim on his " +
				"attention lapses, and it reasserts the moment he is hit again.");
			Assert.That(HealerPatientLock.RunsRanking(decision), Is.True);
		}

		[TestCase(false)]
		[TestCase(true)]
		public void ALockOnAManWhoIsDeadOrBoardedIsDropped(bool patientNeedsTreatment)
		{
			var decision = HealerPatientLock.Resolve(lockHeld: true, patientGone: true, patientNeedsTreatment);

			Assert.That(decision, Is.EqualTo(HealerPatientLockDecision.DropLock),
				"Nothing may leave the medic holding a patient he can never treat. Dead, disposed and " +
				"boarded-a-transport all arrive here, and all must release rather than stall.");
			Assert.That(HealerPatientLock.RunsRanking(decision), Is.True,
				"Dropping the lock must hand the medic straight back to the automatic path — a released " +
				"lock that also suppressed ranking would idle him next to a casualty.");
		}
	}
}
