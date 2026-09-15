#region Copyright & License Information
/*
 * WW3MOD missile strike powers — pins the ONE design claim the US bunker-buster exists to make.
 *
 * The proposal (WORKSPACE/proposals/260904-missile-powers.md §4) chose the GBU-57 MOP over an
 * LRHW "Dark Eagle", which would have been the Kinzhal's YAML with a different name, on a single
 * argument: "Russia buys SPEED, America buys PENETRATION — the US strike hits structures far
 * harder and units far less, which is a real tactical difference rather than a reskin."
 *
 * That claim lives entirely in warhead numbers, and warhead numbers are the easiest thing in the
 * mod to move by accident. If MOPPenetration ever drifts to where a GBU-57 and a Kinzhal do
 * comparable things to the same two targets, the feature still runs, still delivers, still kills —
 * and has silently become the reskin the proposal rejected. Nothing else in the tree would notice.
 * This fixture is what notices.
 *
 * METHOD, stated plainly so the numbers below can be argued with. This is arithmetic over the
 * shipped YAML re-deriving DamageWarhead's own pipeline, not a game run and not the combat-sim
 * (which needs a TypeScript build that does not exist in-tree, and whose committed stats.json is
 * stale in exactly these fields). It models ONE direct hit at the aim point:
 *
 *   applicable  <- warhead.ValidTargets overlaps victim.TargetTypes, and InvalidTargets does not
 *   damage      <- ApplyPenetration(Damage, Penetration, Thickness)      [DamageWarhead.cs:128]
 *   damage      <- ApplyPercentageModifiers(damage, Versus[armourType])  [DamageWarhead.cs:97-109]
 *
 * Both engine helpers are the real ones, called directly. What the model deliberately leaves at
 * 100% is everything that is 100% AT THE EPICENTRE anyway: TargetDamage's centre-proximity scaling,
 * SpreadDamage/Shockwave Falloff[0], and ArmorDirectionPercent (the two victims profiled here carry
 * no Distribution, so no hit-direction discount exists to apply).
 *
 * WHAT IT DOES NOT MODEL, and why that is safe for this comparison: `Inherits@HitEffects:
 * ^HugeExplosionEffects` is not resolved, because a flat text reader cannot follow MiniYaml
 * inheritance. Its only damaging warhead is Warhead@Shrapnel at 200 damage with
 * `ValidTargets: Infantry, Unarmored` (weapons-effects.yaml:598-604), and NEITHER profiled victim
 * carries either type — a Logistics Center is Concrete/Structure and an Abrams is Heavy/Vehicle. So
 * the inherited block contributes exactly zero to both sides of every comparison here, and both
 * weapons inherit the same block regardless. UnmodelledInheritedEffectsAreHarmlessHere pins that.
 */
#endregion

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OpenRA.Mods.Common;
using OpenRA.Mods.Common.Warheads;

namespace OpenRA.Test
{
	[TestFixture]
	public class MissilePowerAsymmetryTest
	{
		// --- the two victim profiles the asymmetry is stated against ----------------------------
		//
		// Deliberately a matched pair BY PRICE rather than by durability, because "which is this
		// good against" is a purchasing question. The Logistics Center costs 3000
		// (structures.yaml, USER RULING 2026-08-22) and the Abrams 2500 (vehicles-america.yaml) —
		// close enough that a player really does choose between hitting one or the other.

		/// <summary>LOGISTICSCENTER: HP 60000, Armor Concrete with no Thickness, target types from
		/// ^BasicBuilding (structures.yaml:27-28).</summary>
		static readonly Victim LogisticsCenter = new(
			"logisticscenter", hp: 60000, armour: "Concrete", thickness: 0,
			targetTypes: new[] { "Ground", "C4", "DetonateAttack", "Structure" });

		/// <summary>ABRAMS: HP 28000, Armor Heavy, Thickness 700 — the thickest vehicle armour in
		/// the mod (vehicles-america.yaml:464).</summary>
		static readonly Victim Abrams = new(
			"abrams", hp: 28000, armour: "Heavy", thickness: 700,
			targetTypes: new[] { "Ground", "Vehicle", "Heavy" });

		// --- the claim ---------------------------------------------------------------------------

		[Test]
		public void TheBunkerBusterHitsStructuresFarHarderThanUnits()
		{
			var mop = Warheads("weapons-superweapons.yaml", "MOPPenetration");

			var onStructure = Deliver(mop, LogisticsCenter);
			var onTank = Deliver(mop, Abrams);

			Assert.That(onStructure, Is.GreaterThan(onTank * 10),
				$"the GBU-57 is supposed to be a PENETRATOR: {onStructure} to a Logistics Center " +
				$"against {onTank} to an Abrams is only {(double)onStructure / onTank:F1}x. Below " +
				"about 10x the two factions' strikes stop playing differently and the proposal's " +
				"reason for choosing the MOP over a Kinzhal reskin (§4b) has been undone.");
		}

		[Test]
		public void TheKinzhalIsTheGeneralistThatMakesThatAsymmetryMeanSomething()
		{
			// THE CONTROL. "Structures harder than units" is only a design statement if the OTHER
			// strike does not also do it. IskanderExplosion — reused unchanged by the Kinzhal — is
			// close to even, which is what makes the MOP's ratio a deliberate difference rather
			// than a property every missile in the mod happens to have.
			var iskander = Warheads("weapons-explosions.yaml", "IskanderExplosion");

			var onStructure = Deliver(iskander, LogisticsCenter);
			var onTank = Deliver(iskander, Abrams);

			var ratio = (double)onStructure / onTank;
			Assert.That(ratio, Is.InRange(0.7, 1.4),
				$"the Kinzhal should be roughly even-handed ({onStructure} vs {onTank}, {ratio:F2}x). " +
				"If it has drifted into being structure- or armour-specialised, the GBU-57 is no " +
				"longer the mod's answer to hardened targets and this whole comparison needs redoing.");
		}

		[Test]
		public void OneBunkerBusterEndsAHardenedStructure()
		{
			var delivered = Deliver(Warheads("weapons-superweapons.yaml", "MOPPenetration"), LogisticsCenter);

			Assert.That(delivered, Is.GreaterThan(LogisticsCenter.Hp),
				$"a 4000-credit strike whose entire purpose is killing buildings put {delivered} on " +
				$"a {LogisticsCenter.Hp} HP Logistics Center and left it standing");

			// Also has to out-penetrate the thickest armour in the game, or the one weapon in the
			// mod named for defeating armour would be stopped by it. mslo is Concrete 2000 and
			// carries no Distribution, so nothing ever discounts that number.
			foreach (var w in Warheads("weapons-superweapons.yaml", "MOPPenetration").Where(w => w.Damage > 0))
				Assert.That(w.Penetration, Is.GreaterThanOrEqualTo(2000),
					$"MOPPenetration's {w.Name} has Penetration {w.Penetration}, which does not clear " +
					"mslo's 2000mm — a bunker buster stopped by a bunker");
		}

		[Test]
		public void OneBunkerBusterDoesNotKillATankParkedOnTheAimPoint()
		{
			// The other half of the asymmetry, and the half that is easy to lose: it is tempting to
			// "fix" a strike that cannot kill a tank. This is that behaviour being correct.
			var delivered = Deliver(Warheads("weapons-superweapons.yaml", "MOPPenetration"), Abrams);

			Assert.That(delivered, Is.LessThan(Abrams.Hp),
				$"the GBU-57 put {delivered} on a {Abrams.Hp} HP Abrams standing exactly on the aim " +
				"point and destroyed it. A penetrator that also one-shots armour is a Kinzhal with a " +
				"different cameo — which is precisely the option (§4a) the proposal rejected.");

			Assert.That(delivered, Is.GreaterThan(Abrams.Hp / 10),
				$"the GBU-57 put only {delivered} on an Abrams it landed on top of. 'Weak against " +
				"armour' should still be a crater, not a miss.");
		}

		[Test]
		public void TheKinzhalKillsBothProfilesAndTheBunkerBusterOnlyOne()
		{
			// Stated as one assertion because it is the sentence a player would say out loud:
			// the Kinzhal is the one you fire at anything, the GBU-57 is the one you fire at
			// buildings.
			var iskander = Warheads("weapons-explosions.yaml", "IskanderExplosion");
			var mop = Warheads("weapons-superweapons.yaml", "MOPPenetration");

			Assert.That(Deliver(iskander, LogisticsCenter), Is.GreaterThan(LogisticsCenter.Hp));
			Assert.That(Deliver(iskander, Abrams), Is.GreaterThan(Abrams.Hp));
			Assert.That(Deliver(mop, LogisticsCenter), Is.GreaterThan(LogisticsCenter.Hp));
			Assert.That(Deliver(mop, Abrams), Is.LessThan(Abrams.Hp));
		}

		// --- the Explodes-payload invariants, restated for the new warhead -----------------------

		[Test]
		public void EveryDamagingWarheadOnTheNewPayloadObeysTheExplodesRules()
		{
			// MOPPenetration is delivered by Explodes on gbu57bomb, exactly as IskanderExplosion is
			// delivered by Explodes on kinzhalmissile — so all three defects BallisticPenetrationTest
			// was written for apply to it verbatim, and a fresh warhead is exactly where they come
			// back. Penetration unset defaults to 1 and divides damage by armour thickness;
			// DamageAtMaxRange other than 100 divides by a Range that is zero on an Explodes payload
			// and yields NaN rather than a falloff.
			foreach (var w in Warheads("weapons-superweapons.yaml", "MOPPenetration").Where(w => w.Damage > 0))
			{
				Assert.That(w.Fields.ContainsKey("Penetration"), Is.True,
					$"MOPPenetration's {w.Name} sets no Penetration, so it inherits the default of 1 " +
					"and delivers damage/thickness against anything armoured");
				Assert.That(w.Fields.GetValueOrDefault("DamageAtMaxRange", "100"), Is.EqualTo("100"),
					$"MOPPenetration's {w.Name} has DamageAtMaxRange != 100. Its weapon is never fired " +
					"from an armament, so Range is 0 and RangeDamageFactor divides by zero.");
			}
		}

		[Test]
		public void TheStructureUnitSplitIsATargetTypeFilterNotAnArmourTable()
		{
			// WHY THIS IS PINNED. `Versus` is per-ARMOUR-TYPE, and armour type does NOT partition
			// structures from units in this mod: gtwr is a structure typed `Unarmored`
			// (structures-defenses.yaml:104) and mslo is a structure typed `Concrete`. Worse,
			// DamageWarhead.DamageVersus only applies rows a table NAMES (DamageWarhead.cs:102-109),
			// so any armour type left out — Kevlar on all 28 infantry, for one — silently takes the
			// full 100%. A Versus-based split would therefore be both wrong and quietly wrong.
			var penetrate = Warheads("weapons-superweapons.yaml", "MOPPenetration")
				.SingleOrDefault(w => w.Name == "Warhead@Penetrate");

			Assert.That(penetrate, Is.Not.Null, "MOPPenetration lost its Warhead@Penetrate block");
			Assert.That(penetrate.ValidTargets, Is.EquivalentTo(new[] { "Structure" }),
				"the penetrating charge must be gated on the `Structure` target type, which " +
				"^BasicBuilding grants to every building and no unit template carries. It currently " +
				$"reads [{string.Join(", ", penetrate.ValidTargets)}].");
		}

		// --- what gates the two event-tier nukes, now that the lobby checkboxes are gone -------
		//
		// THESE FOUR TESTS USED TO CHECK A LOBBY GATE'S FAIL-SAFE POLARITY and were retired with the
		// gate on 2026-09-15. `tactical-nuke` and `high-yield-nuke` each gated exactly one power, and
		// both of those powers are EVENT TIER -- `Prerequisites: powers.event`, provided by no faction
		// (player.yaml:144) -- so outside `powers-sandbox` neither checkbox ever decided anything, and
		// inside it a host had to tick two boxes to reach one weapon.
		//
		// WHAT REPLACES THEM IS NOT A WEAKER CHECK, and that is the thing worth being sure of, because
		// the retired pair were explicitly the guard on "an absent lobby option must not hand a player
		// a 6 Mt warhead". That property is now structural rather than defended: there is no option to
		// be absent. The two tests below pin the gates that are actually left, and they pin the
		// RequiresCondition EXACTLY rather than by Contains -- which is the stricter form the old pair
		// deliberately could not use, and is what makes a half-revert (the grant trait restored while
		// the option stays unregistered, which would fall back to false and silently delete the power
		// from the sandbox) fail here instead of shipping.

		[Test]
		public void TheTacticalNukeIsGatedByTheLadderBandAndTheEventTierAlone()
		{
			var power = ReadBlock(Path.Combine(ModRulesDir(), "player.yaml"), "MissileStrikePower@TacNuke");

			// EXACTLY the band, with no lobby conjunct beside it. `Atomic` is 20 kt, so the rung that
			// releases it is the 20 kt one. Outside Escalation every band is granted on the first tick,
			// so in Skirmish this conjunct is inert and the TIER below is the whole gate.
			Assert.That(power.GetValueOrDefault("RequiresCondition"), Is.EqualTo("nuclear-release-20kt"),
				"the retired `tactical-nuke` checkbox must not come back as a conjunct here: it would be " +
				"granted by nothing, so it would read as permanently satisfied and mean nothing -- or, if " +
				"its GrantConditionOnLobbyOption were restored too, it would fall back to FALSE for an " +
				"unregistered option and delete the power from the sandbox instead");

			// THE GATE THAT ACTUALLY HOLDS IT SHUT. `powers.event` is provided by no faction; only
			// ProvidesPrerequisite@SandboxEvent grants it, on `!powers-sandbox-disabled`.
			Assert.That(power.GetValueOrDefault("Prerequisites"), Is.EqualTo("powers.event"),
				"the event tier is the only thing keeping a 20 kt warhead off the shop floor now that " +
				"the lobby checkbox is gone; naming a faction here would put it on sale");

			AssertLobbyGateTraitIsGone("GrantConditionOnLobbyOption@tacnuke");
		}

		[Test]
		public void TheHighYieldNukeIsGatedByTheTopRungAndItsMissingOwnerAlone()
		{
			var power = ReadBlock(Path.Combine(ModRulesDir(), "player.yaml"), "MissileStrikePower@HighYieldNuke");

			Assert.That(power.GetValueOrDefault("RequiresCondition"), Is.EqualTo("nuclear-release-gameender"),
				"see the tactical nuke above; the same half-revert would be worse here, because this " +
				"checkbox was the one option in the mod that shipped defaulting ON -- restoring the grant " +
				"trait without the option would flip a reachable weapon to unreachable, not the reverse");

			// AND THE PREREQUISITE IS WHY THIS 6 Mt MAP-ENDER IS STILL SAFE AT THE TOP RUNG, where
			// NuclearExchangeInfo.OverriddenPrerequisites DOES waive `powers.event`. Strip the waived
			// term and NuclearGameEnders.ArmableBy is left with an EMPTY owner set, and an empty owner
			// set returns false -- the "national ender only" ruling of 2026-09-14. Add `player.america`
			// or `player.russia` here and this power becomes a second END cameo for that side.
			Assert.That(power.GetValueOrDefault("Prerequisites"), Is.EqualTo("powers.event"),
				"this power must name NO nation: NuclearGameEnders.ArmableBy subtracts the overridden " +
				"`powers.event` and refuses an empty remainder, which is the whole of what stops the " +
				"exchange arming a 6 Mt warhead for somebody at the END level");

			AssertLobbyGateTraitIsGone("GrantConditionOnLobbyOption@highyieldnuke");
		}

		/// <summary>
		/// Assert a retired <c>GrantConditionOnLobbyOption</c> block is absent from player.yaml.
		/// Deliberately NOT via <see cref="ReadBlock"/>, which asserts the block IS found and so can
		/// only ever be used to check a trait's contents, never its absence.
		/// </summary>
		static void AssertLobbyGateTraitIsGone(string trait)
		{
			var found = File.ReadAllLines(Path.Combine(ModRulesDir(), "player.yaml"))
				.Select(l => l.Split('#')[0].Trim())
				.Any(l => l == trait + ":");

			Assert.That(found, Is.False,
				$"{trait} was retired on 2026-09-15 along with the lobby option it reads. Restoring it " +
				"without also registering that option in PowersLobbyOptions would grant its disabling " +
				"condition unconditionally (the fallback is `!GrantWhenOptionDisabled`, not the trait's " +
				"default field) and remove the power from the sandbox.");
		}

		// --- reading the mod ---------------------------------------------------------------------

		sealed class Victim
		{
			public readonly string Name;
			public readonly int Hp;
			public readonly string Armour;
			public readonly int Thickness;
			public readonly string[] TargetTypes;

			public Victim(string name, int hp, string armour, int thickness, string[] targetTypes)
			{
				Name = name;
				Hp = hp;
				Armour = armour;
				Thickness = thickness;
				TargetTypes = targetTypes;
			}
		}

		sealed class ParsedWarhead
		{
			public string Name;
			public Dictionary<string, string> Fields = new();
			public Dictionary<string, int> Versus = new();

			public int Damage => int.Parse(Fields.GetValueOrDefault("Damage", "0"));

			// Warhead.ValidTargets defaults to Ground+Water when the block does not set its own
			// (Warhead.cs:30). Note this is the WARHEAD default and not the weapon's ValidTargets —
			// the two are separate fields serving separate call sites.
			public string[] ValidTargets => Split(Fields.GetValueOrDefault("ValidTargets", "Ground, Water"));
			public string[] InvalidTargets => Split(Fields.GetValueOrDefault("InvalidTargets", ""));
			public int Penetration => int.Parse(Fields.GetValueOrDefault("Penetration", "1"));

			static string[] Split(string v)
			{
				return v.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
			}
		}

		/// <summary>One direct hit at the aim point, summed over every damaging warhead that
		/// applies. See the file header for what the model does and does not include.</summary>
		static int Deliver(IEnumerable<ParsedWarhead> warheads, Victim victim)
		{
			var total = 0;
			foreach (var w in warheads)
			{
				if (w.Damage <= 0)
					continue;

				if (!w.ValidTargets.Intersect(victim.TargetTypes).Any())
					continue;

				if (w.InvalidTargets.Intersect(victim.TargetTypes).Any())
					continue;

				var damage = DamageWarhead.ApplyPenetration(w.Damage, w.Penetration, victim.Thickness);

				if (w.Versus.TryGetValue(victim.Armour, out var versus))
					damage = Util.ApplyPercentageModifiers(damage, new[] { versus });

				total += damage;
			}

			return total;
		}

		static string ModRulesDir()
		{
			var dir = new DirectoryInfo(AppContext.BaseDirectory);
			while (dir != null)
			{
				var candidate = Path.Combine(dir.FullName, "mods", "ww3mod", "rules");
				if (Directory.Exists(candidate))
					return candidate;

				dir = dir.Parent;
			}

			throw new DirectoryNotFoundException("could not locate mods/ww3mod/rules");
		}

		/// <summary>Every `Warhead@...` block of one top-level weapon, with its fields and its
		/// `Versus` sub-block. Flat text, the same approach BallisticPenetrationTest takes and for
		/// the same reason: no World, no ModData, no game run.</summary>
		static List<ParsedWarhead> Warheads(string file, string weapon)
		{
			var path = Path.Combine(ModRulesDir(), "weapons", file);
			var result = new List<ParsedWarhead>();
			var inWeapon = false;
			ParsedWarhead current = null;
			var inVersus = false;

			foreach (var raw in File.ReadLines(path))
			{
				var line = raw.Split('#')[0].TrimEnd();
				if (line.Trim().Length == 0)
					continue;

				var indent = line.TakeWhile(c => c == '\t').Count();
				var body = line.Trim();

				if (indent == 0)
				{
					if (inWeapon)
						break;

					inWeapon = body == weapon + ":";
					current = null;
					continue;
				}

				if (!inWeapon)
					continue;

				if (indent == 1)
				{
					inVersus = false;
					if (body.StartsWith("Warhead@", StringComparison.Ordinal))
					{
						current = new ParsedWarhead { Name = body.Split(':')[0].Trim() };
						result.Add(current);
					}
					else
						current = null;
				}
				else if (indent == 2 && current != null)
				{
					if (body == "Versus:")
					{
						inVersus = true;
						continue;
					}

					inVersus = false;
					var parts = body.Split(new[] { ':' }, 2);
					if (parts.Length == 2)
						current.Fields[parts[0].Trim()] = parts[1].Trim();
				}
				else if (indent == 3 && current != null && inVersus)
				{
					var parts = body.Split(new[] { ':' }, 2);
					if (parts.Length == 2)
						current.Versus[parts[0].Trim()] = int.Parse(parts[1].Trim());
				}
			}

			Assert.That(result, Is.Not.Empty, $"{weapon} not found (or has no warheads) in {file}");
			return result;
		}

		/// <summary>Flat read of one indent-1 trait block from a rules file.</summary>
		static Dictionary<string, string> ReadBlock(string path, string trait)
		{
			var fields = new Dictionary<string, string>();
			var inBlock = false;

			foreach (var raw in File.ReadLines(path))
			{
				var line = raw.Split('#')[0].TrimEnd();
				if (line.Trim().Length == 0)
					continue;

				var indent = line.TakeWhile(c => c == '\t').Count();
				var body = line.Trim();

				if (indent <= 1)
				{
					if (inBlock)
						break;

					inBlock = indent == 1 && body == trait + ":";
				}
				else if (indent == 2 && inBlock)
				{
					var parts = body.Split(new[] { ':' }, 2);
					if (parts.Length == 2)
						fields[parts[0].Trim()] = parts[1].Trim();
				}
			}

			Assert.That(inBlock, Is.True, $"{trait} not found in {Path.GetFileName(path)}");
			return fields;
		}

		[Test]
		public void UnmodelledInheritedEffectsAreHarmlessHere()
		{
			// The header claims ^HugeExplosionEffects contributes nothing to either profiled victim.
			// That claim is load-bearing for every ratio above, so it is checked rather than
			// asserted in prose — if someone adds a damaging warhead to that shared block with
			// broader target types, this goes red and the ratios above become untrustworthy at the
			// same moment.
			foreach (var w in Warheads("weapons-effects.yaml", "^HugeExplosionEffects").Where(w => w.Damage > 0))
			{
				Assert.That(w.ValidTargets.Intersect(LogisticsCenter.TargetTypes), Is.Empty,
					$"^HugeExplosionEffects.{w.Name} now reaches a Logistics Center, so the damage " +
					"model in this fixture no longer accounts for everything that lands");
				Assert.That(w.ValidTargets.Intersect(Abrams.TargetTypes), Is.Empty,
					$"^HugeExplosionEffects.{w.Name} now reaches an Abrams, same problem");
			}
		}
	}
}
