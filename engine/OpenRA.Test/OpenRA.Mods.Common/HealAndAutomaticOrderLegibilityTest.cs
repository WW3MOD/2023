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

using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OpenRA.Mods.Common;
using OpenRA.Mods.Common.Traits;
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

		static string FindModFile(params string[] parts)
		{
			var dir = new DirectoryInfo(AppContext.BaseDirectory);
			for (var i = 0; i < 10 && dir != null; i++, dir = dir.Parent)
			{
				var candidate = Path.Combine(new[] { dir.FullName, "mods", "ww3mod" }.Concat(parts).ToArray());
				if (File.Exists(candidate))
					return candidate;
			}

			throw new FileNotFoundException($"could not locate mods/ww3mod/{string.Join("/", parts)}");
		}

		static MiniYaml Child(MiniYaml parent, string key, string where)
		{
			var node = parent.Nodes.FirstOrDefault(n => n.Key == key);
			Assert.That(node, Is.Not.Null, $"{key} is no longer defined in {where} — this test is pinning nothing");
			return node.Value;
		}

		static MiniYaml Top(string where, string key, params string[] parts)
		{
			var node = MiniYaml.FromFile(FindModFile(parts)).FirstOrDefault(n => n.Key == key);
			Assert.That(node, Is.Not.Null, $"{key} is no longer defined in {where} — this test is pinning nothing");
			return node.Value;
		}

		/// <summary>
		/// The gap between successive heal IMPACTS on one patient, in ticks — the medic's Heal weapon
		/// fires Burst 1, so its BurstWait IS the impact-to-impact spacing. Read from the shipped weapon
		/// rather than restated, because the whole defect this test replaces was an assertion about two
		/// DEFAULTS that had drifted away from the numbers actually in force.
		/// </summary>
		static int HealImpactGapTicks()
		{
			var heal = Top("weapons-other.yaml", "Heal", "rules", "weapons", "weapons-other.yaml");
			return FieldLoader.GetValue<int>("BurstWait", Child(heal, "BurstWait", "the Heal weapon").Value);
		}

		/// <summary>
		/// The shipped GrantConditionOnHealed, loaded through the real FieldLoader off ^ExistsInWorld —
		/// so an explicit Duration in YAML is honoured and its absence correctly yields the Info default.
		/// </summary>
		static GrantConditionOnHealedInfo ShippedHealCondition()
		{
			var existsInWorld = Top("defaults.yaml", "^ExistsInWorld", "rules", "defaults.yaml");
			var info = new GrantConditionOnHealedInfo();
			FieldLoader.Load(info, Child(existsInWorld, "GrantConditionOnHealed", "^ExistsInWorld"));
			return info;
		}

		[Test]
		public void TheHealConditionOutlivesTheGapBetweenHealImpacts()
		{
			// The "being treated" pip is a DURATIVE readout driven by a condition that each heal impact
			// refreshes for Duration ticks. If Duration is shorter than the spacing between impacts, the
			// condition lapses in every gap and the pip strobes on a patient who is still being treated —
			// the exact failure GrantConditionOnHealedInfo.Duration's own [Desc] warns about.
			//
			// WHAT THIS REPLACES, because the replacement is the point: this assertion used to compare
			// GrantConditionOnHealedInfo.Duration against WithHealFlashInfo.Cooldown. Both were DEFAULTS,
			// neither was the number in force (YAML ships Cooldown 20, not the default 25), and the flash
			// cooldown is not the impact spacing in the first place — BurstWait is, and the old test never
			// read it. It passed at a 50-tick spacing that was strobing 20 ticks dark in every 50.
			var gap = HealImpactGapTicks();
			Assert.That(ShippedHealCondition().Duration, Is.GreaterThan(gap),
				$"the heal condition lapses between impacts that are still arriving ({gap}-tick spacing), " +
				"so a patient under continuous treatment shows a strobing pip instead of a steady one.");
		}
	}
}
