#region Copyright & License Information
/*
 * WW3MOD heal/automatic-order legibility — contract pins.
 *
 * WHAT THESE CAN AND CANNOT PROVE. Both features under test are RENDERING, and rendering is not
 * assertable here. What IS assertable is the two premises the rendering rests on, and both are
 * premises that fail SILENTLY and INVISIBLY if broken — which is exactly the shape of bug that
 * shipped WithHealFlash in a state nobody could see for five months:
 *
 *   1. Healing is NEGATIVE damage, and zero is not healing. This mod has a live zero-damage warhead
 *      (ReplenishSoldiersTargeter, weapons-other.yaml:367, DamagePercent: 0), so the >=/> boundary
 *      is not hypothetical: get it wrong and every soldier-replenish tick lights a heal pip.
 *   2. AutomaticOrder.LineColor is used by NOTHING ELSE. The automatic-order feature deliberately
 *      carries provenance in the colour value rather than threading a flag through 29 call sites
 *      (see AutomaticOrder.cs). That trade is only sound while the value stays unique — if some
 *      other trait is ever retuned onto the same ARGB, automatic lines silently stop timing out for
 *      orders the player DID give, and nothing anywhere would report it.
 *
 * These do NOT prove the flash is visible, the pip is positioned somewhere sensible, or that blue
 * reads as "the game did this" on screen. Those need eyes; the report names the capture to take.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Common.Traits.Render;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class HealAndAutomaticOrderLegibilityTest
	{
		static AttackInfo Dealt(int damage)
		{
			return new AttackInfo { Damage = new Damage(damage) };
		}

		[TestCase(-1)]
		[TestCase(-5)]
		[TestCase(-1000)]
		public void NegativeDamageIsHealing(int damage)
		{
			Assert.That(HealEvent.IsHealing(Dealt(damage)), Is.True,
				"Healing arrives as negative damage — the Heal and Repair weapons are SpreadDamage " +
				"warheads with a negative DamagePercent. A reader who assumes 'heal' is its own event " +
				"type, or who copies this predicate and flips the sign, silently gets no readout.");
		}

		[TestCase(0)]
		[TestCase(1)]
		[TestCase(5000)]
		public void ZeroAndPositiveDamageAreNotHealing(int damage)
		{
			Assert.That(HealEvent.IsHealing(Dealt(damage)), Is.False,
				"Zero is the boundary that matters: ReplenishSoldiersTargeter fires DamagePercent: 0 " +
				"at allies, so a `<= 0` predicate would light the heal readout on every replenish tick " +
				"with nobody being treated.");
		}

		[Test]
		public void TheAutomaticColourIsRecognisedAsAutomatic()
		{
			Assert.That(AutomaticOrder.IsAutomatic(AutomaticOrder.LineColor), Is.True);
		}

		// Every colour a target line is drawn in anywhere in the mod. If one of these ever equals the
		// automatic colour, the provenance bit this feature encodes in the colour is destroyed.
		[TestCase("Mobile move")]
		[TestCase("Aircraft move")]
		[TestCase("AttackBase attack")]
		[TestCase("AttendAlly")]
		[TestCase("Patrol")]
		[TestCase("Capture/board")]
		[TestCase("Rally force-move")]
		public void NoOtherTargetLineColourCollidesWithTheAutomaticColour(string which)
		{
			var other = which switch
			{
				"Mobile move" => new MobileInfo().TargetLineColor,
				"Aircraft move" => new AircraftInfo().TargetLineColor,
				"AttackBase attack" => new AttackFrontalInfo().TargetLineColor,
				"AttendAlly" => new AttendAllyInfo().TargetLineColor,

				// Patrol's colour is a private field on the activity (Patrol.cs:30) and the
				// capture/board pink is authored in YAML (infantry.yaml:92, :901), so both are
				// restated here rather than read. They are checked precisely BECAUSE they are the
				// ones a refactor would not notice.
				"Patrol" => Color.Cyan,
				"Capture/board" => Color.FromArgb(0xFF, 0xC8, 0x50, 0xB4),
				"Rally force-move" => Color.DeepSkyBlue,
				_ => throw new AssertionException("unhandled case " + which)
			};

			Assert.That(AutomaticOrder.IsAutomatic(other), Is.False,
				$"{which} draws in the same colour as an automatic order, so the player can no longer " +
				"tell an order they gave from one the game gave — and automatic lines' exemption from " +
				"the display timeout would start applying to it too.");
		}

		[Test]
		public void TheHealConditionOutlivesTheFlashRetriggerWindow()
		{
			// The two readouts are complementary only while the pip spans the gaps between flashes.
			// If the condition expired sooner than the flash can re-fire, continuous treatment would
			// show a strobing pip instead of a steady one — the exact illegibility being fixed.
			Assert.That(new GrantConditionOnHealedInfo().Duration,
				Is.GreaterThan(new WithHealFlashInfo().Cooldown),
				"GrantConditionOnHealed.Duration must exceed WithHealFlash.Cooldown, or the durative " +
				"pip goes dark between heal impacts that are still arriving.");
		}
	}
}
