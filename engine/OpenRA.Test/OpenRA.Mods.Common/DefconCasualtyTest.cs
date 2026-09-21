#region Copyright & License Information
/*
 * THE BURNOUT HOLE IN THE DEFCON 2 CASUALTY RULE -- found by a scenario on 2026-09-21 and fixed the
 * same day.
 *
 * Every vehicle in this mod inherits `^EffectsWhenDamagedVehicles`, whose ChangesHealth@CriticalDamage
 * takes 1% of maximum health every 5 ticks once the vehicle is below half (vehicles.yaml:183-188).
 * ChangesHealth.cs:86 inflicts that as `self.InflictDamage(self, ...)` -- SELF-INFLICTED -- so unless
 * a further enemy round lands first, the blow that finishes a damaged vehicle names the victim as its
 * own killer, and DefconCasualtyObserver's "enemy action only" rule threw it away.
 *
 * MEASURED, and the pair is what makes it undeniable. Two runs of the same scenario differing only in
 * the target's hit points:
 *     run 260921_181057, t90 at  8000 hp -> `killed by abrams(USA) -- qualifies`
 *     run 260921_181456, t90 at 40000 hp -> `killed by t90(Russia) -- REJECTED: self-inflicted`
 * One round from full health overkills 8000 and the burn never starts; 40000 survives the first hit,
 * ignites, and bleeds out. So whether a deliberate kill ended DEFCON 2 depended on whether the victim
 * happened to survive the opening round.
 *
 * WHAT IS PINNED HERE is the decision, not the plumbing: nothing in OpenRA.Test can construct a World
 * and therefore not an Actor or an AttackInfo either, so IsQualifyingCasualty (which takes both) stays
 * verified by reading and the rule that sits on top of it lives in a pure overload that does not.
 * Same split, and the same honest limit, as CombatantSides.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class DefconCasualtyTest
	{
		[Test]
		public void ABurnoutAfterEnemyDamageCountsAsACasualty()
		{
			// THE DEFECT, STATED AS A TEST. The direct rule rejects it (the finishing blow is the
			// victim's own burn), it IS self-inflicted, and an enemy had shot it first.
			Assert.That(
				DefconCasualtyObserver.QualifiesByPriorEnemyDamage(
					directlyQualifies: false, selfInflicted: true, hadPriorEnemyDamage: true),
				Is.True,
				"a tank shot below half and left to burn out still does not end DEFCON 2.");
		}

		[Test]
		public void AnUntouchedSelfInflictedDeathIsStillNotACasualty()
		{
			// The whole self-inflicted class the rule was written to exclude -- helicopter crashes,
			// FallToEarth, scuttles, the sacrificial capture and demolish kills -- arrives with
			// attacker == victim and NO prior enemy damage. None of it may end the phase.
			Assert.That(
				DefconCasualtyObserver.QualifiesByPriorEnemyDamage(
					directlyQualifies: false, selfInflicted: true, hadPriorEnemyDamage: false),
				Is.False);
		}

		[Test]
		public void FriendlyFireIsStillNotACasualtyHoweverMuchEnemyDamageCameFirst()
		{
			// THE NARROWNESS IS THE ARGUMENT. This path fires ONLY on a self-inflicted finishing blow.
			// A unit an ALLY finishes off is not self-inflicted, so clause 2 of the direct rule --
			// "enemy action only", which the user ruled on explicitly -- is untouched by this change.
			Assert.That(
				DefconCasualtyObserver.QualifiesByPriorEnemyDamage(
					directlyQualifies: false, selfInflicted: false, hadPriorEnemyDamage: true),
				Is.False,
				"a friendly-fire kill started counting because the victim had been shot at earlier.");
		}

		[Test]
		public void ADeathTheDirectRuleAlreadyAcceptsIsNotReAttributed()
		{
			// The ordinary kill: the enemy round that lands the finishing blow IS the attacker, and
			// this path must not fire and hand the record a different name.
			foreach (var selfInflicted in new[] { true, false })
				foreach (var hadPrior in new[] { true, false })
					Assert.That(
						DefconCasualtyObserver.QualifiesByPriorEnemyDamage(true, selfInflicted, hadPrior),
						Is.False,
						$"re-attributed an already-qualifying death (selfInflicted={selfInflicted}, prior={hadPrior}).");
		}

		[Test]
		public void NoPriorDamageAndNoSelfInflictionLeavesTheDirectRuleAlone()
		{
			// The neutral-killed and no-attacker cases: the direct rule rejects them and this adds
			// nothing, which is what keeps "a mine is not somebody's shot" true.
			Assert.That(DefconCasualtyObserver.QualifiesByPriorEnemyDamage(false, false, false), Is.False);
		}
	}
}
