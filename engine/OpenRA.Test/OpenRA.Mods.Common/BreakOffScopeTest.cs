#region Copyright & License Information
/*
 * WW3MOD break-off scope pins.
 *
 * AutoTargetInfo.BreakOffCondition ("critical-damage") deprioritises a target that is already
 * finished. It is consulted in three places, and one of them was reading it as a VALIDITY rule
 * rather than a preference: Attack.TickAttack returned UnableToAttack for any non-force attack on
 * such a target, and a player's ordinary attack order arrives there as
 * AttackSource.Default/forceAttack=false. Net effect in live play: the order was accepted by the
 * targeting layer, the soldier walked into range, and then never fired a shot — indistinguishable
 * on screen from the unit ignoring the player. Measured in test-aa-breakoff-critical on 2026-08-10
 * ("auto ----, normal manual ----, force attack FIRE").
 *
 * The load-bearing property: BREAK-OFF MAY ONLY CANCEL AN ENGAGEMENT THE UNIT CHOSE FOR ITSELF.
 * Deprioritisation stays where it belongs, in AutoTarget.ChooseTarget, which still skips these
 * targets so healthy units are preferred.
 *
 * Pure over the predicate, following the project idiom (StancePositioningFireStanceTest); no world.
 */
#endregion

using System;
using System.Linq;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class BreakOffScopeTest
	{
		[Test]
		public void APlayerIssuedOrderIsNeverBrokenOff()
		{
			// The user-visible bug, stated as the predicate: an ordinary attack order (no Ctrl) on a
			// critically wounded soldier. AttackBase.ResolveOrder issues this as Default/false.
			Assert.That(AttackBase.BreakOffApplies(AttackSource.Default, false), Is.False,
				"an ordinary player attack order must still fire on a critically wounded target — " +
				"deprioritisation is AutoTarget.ChooseTarget's job, not a refusal of an explicit order");
		}

		[Test]
		public void SelfChosenEngagementsStillBreakOff()
		{
			// This is the behaviour the user explicitly wants KEPT: units left to their own devices
			// prefer healthy targets and drop one that has gone critical.
			Assert.That(AttackBase.BreakOffApplies(AttackSource.AutoTarget, false), Is.True,
				"an auto-acquired engagement must still break off — this is the deprioritisation");
			Assert.That(AttackBase.BreakOffApplies(AttackSource.AttackMove, false), Is.True,
				"attack-move engagements are self-chosen too");
		}

		[Test]
		public void ForceAttackIsExemptFromEverySource()
		{
			// Pre-existing contract, unchanged: Ctrl+click always fires.
			foreach (var source in Enum.GetValues(typeof(AttackSource)).Cast<AttackSource>())
				Assert.That(AttackBase.BreakOffApplies(source, true), Is.False,
					$"force-attack must be exempt from break-off (source {source})");
		}

		[Test]
		public void DefaultIsTheOnlyNonForceSourceThatIsExempt()
		{
			// Pinned as an exact set rather than "Default is exempt": a new AttackSource added later
			// would otherwise silently inherit the player-order exemption, which must be a deliberate
			// decision. AttackSource.Default is what AttackBase.ResolveOrder and the Lua Actor.Attack
			// binding both pass; everything automatic passes AutoTarget or AttackMove.
			var exempt = Enum.GetValues(typeof(AttackSource)).Cast<AttackSource>()
				.Where(s => !AttackBase.BreakOffApplies(s, false))
				.ToArray();

			Assert.That(exempt, Is.EqualTo(new[] { AttackSource.Default }),
				"exactly one source may be exempt from break-off without a force flag");
		}
	}
}
