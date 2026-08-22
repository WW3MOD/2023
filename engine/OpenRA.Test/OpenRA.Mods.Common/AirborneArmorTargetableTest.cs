#region Copyright & License Information
/*
 * WW3MOD airborne armour-class targetability — corpus pin.
 *
 * Reported from live play 2026-08-22: "Grenadiers can target helicopters it seems, and they should
 * not be able to target anything flying."
 *
 * GrenadeLauncher (weapons-ballistics.yaml:448) declares `ValidTargets: Infantry, Unarmored, Light`
 * — no `Air`, no `Helicopter`. It reached the helicopter through `Light`: each airframe carried a
 * `Targetable@Armor` advertising its armour class with NO RequiresCondition, so an airborne
 * littlebird permanently announced `Light` and every ground weapon naming an armour class could
 * reach it. The shared ^NeutralAirborne template is careful about exactly this (Targetable@Ground
 * is gated `!airborne`, Targetable@Airborne `airborne`); the per-airframe armour entry was the one
 * that forgot.
 *
 * The fix gates every `Targetable@Armor` on `!airborne`, so an armour class is a GROUND fact only.
 * Airborne actors are reachable via `Air` (all AA) and, for rotorcraft, `Helicopter` (small arms).
 *
 * Three shipped comments independently assert that armour classes are meant to be ground-only, and
 * all three were silently false before the fix:
 *   - 12.7mm.Hind (weapons-ballistics.yaml:368) "GROUND ONLY as of 260815 — Helicopter moved to
 *     12.7mm.Hind.AA" — yet it still reached Light helis via `Light`.
 *   - ^5.56mm (weapons-ballistics.yaml:81) names `Helicopter` in ValidTargets while excluding
 *     `Light, Medium, Heavy` — every helicopter in the mod is Light or Heavy, so the rifle's
 *     declared anti-helicopter capability was 100% dead.
 *   - ^NeutralAirborne's own gating of the other three targetables.
 *
 * Reads the shipped YAML rather than a fixture: the thing being protected is the corpus, and the
 * recurrence guard has to cover airframes that do not exist yet.
 */
#endregion

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OpenRA.GameRules;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class AirborneArmorTargetableTest
	{
		const string ArmorTargetable = "Targetable@Armor";
		const string GroundOnly = "!airborne";

		static readonly string[] AircraftRules = { "aircraft.yaml", "aircraft-america.yaml", "aircraft-russia.yaml" };

		static string FindRules(params string[] parts)
		{
			var dir = new DirectoryInfo(AppContext.BaseDirectory);
			for (var i = 0; i < 10 && dir != null; i++, dir = dir.Parent)
			{
				var candidate = Path.Combine(new[] { dir.FullName, "mods", "ww3mod", "rules" }.Concat(parts).ToArray());
				if (File.Exists(candidate))
					return candidate;
			}

			throw new FileNotFoundException($"could not locate mods/ww3mod/rules/{string.Join("/", parts)}");
		}

		/// <summary>Every actor defined across the three aircraft rules files, keyed by name.</summary>
		static Dictionary<string, MiniYaml> Airframes()
		{
			var actors = new Dictionary<string, MiniYaml>();
			foreach (var file in AircraftRules)
				foreach (var node in MiniYaml.FromFile(FindRules("ingame", file)))
					actors[node.Key] = node.Value;

			Assert.That(actors, Is.Not.Empty, "no aircraft actors parsed — this test is scanning nothing");
			return actors;
		}

		static IEnumerable<MiniYamlNode> Inherited(Dictionary<string, MiniYaml> actors, string name, HashSet<string> seen = null)
		{
			seen ??= new HashSet<string>();
			if (!seen.Add(name) || !actors.TryGetValue(name, out var actor))
				yield break;

			foreach (var node in actor.Nodes)
			{
				if (node.Key == "Inherits" || node.Key.StartsWith("Inherits@", StringComparison.Ordinal))
					foreach (var n in Inherited(actors, node.Value.Value, seen))
						yield return n;
				else
					yield return node;
			}
		}

		/// <summary>
		/// Evaluates the RequiresCondition shapes that actually occur on aircraft Targetables. Anything
		/// else throws rather than being silently treated as satisfied — a new shape must be read by a
		/// human, not guessed at by this test.
		/// </summary>
		static bool Holds(string requiresCondition, bool airborne)
		{
			switch (requiresCondition)
			{
				case null: return true;
				case "airborne": return airborne;
				case "!airborne": return !airborne;
				case "!airborne && damaged": return !airborne;  // evaluated undamaged
				default:
					throw new InvalidOperationException(
						$"unrecognised Targetable RequiresCondition '{requiresCondition}' on an aircraft. " +
						"Teach AirborneArmorTargetableTest.Holds about it — do not let it evaluate as true by default.");
			}
		}

		/// <summary>The target types an airframe actually advertises, honouring RequiresCondition exactly as
		/// Actor.GetEnabledTargetTypes (Actor.cs:646) does.</summary>
		static BitSet<TargetableType> EnabledTargetTypes(Dictionary<string, MiniYaml> actors, string name, bool airborne)
		{
			var types = new List<string>();
			foreach (var node in Inherited(actors, name))
			{
				if (!node.Key.StartsWith("Targetable", StringComparison.Ordinal))
					continue;

				var condition = node.Value.Nodes.FirstOrDefault(n => n.Key == "RequiresCondition")?.Value.Value;
				if (!Holds(condition, airborne))
					continue;

				var declared = node.Value.Nodes.FirstOrDefault(n => n.Key == "TargetTypes")?.Value.Value;
				if (!string.IsNullOrWhiteSpace(declared))
					types.AddRange(declared.Split(',').Select(t => t.Trim()).Where(t => t.Length > 0));
			}

			Assert.That(types, Is.Not.Empty, $"{name} advertises no target types at all — this test is scanning nothing");
			return new BitSet<TargetableType>(types.Distinct().ToArray());
		}

		/// <summary>A WeaponInfo carrying the shipped ValidTargets/InvalidTargets of the named weapon,
		/// following weapon Inherits to whichever ancestor actually declares them.</summary>
		static WeaponInfo Weapon(string name)
		{
			var weapons = new Dictionary<string, MiniYaml>();
			foreach (var file in new[] { "weapons-ballistics.yaml", "weapons-missiles.yaml", "weapons-other.yaml" })
				foreach (var node in MiniYaml.FromFile(FindRules("weapons", file)))
					weapons[node.Key] = node.Value;

			string Resolve(string weapon, string field, HashSet<string> seen)
			{
				if (!seen.Add(weapon) || !weapons.TryGetValue(weapon, out var w))
					return null;

				var own = w.Nodes.FirstOrDefault(n => n.Key == field)?.Value.Value;
				if (own != null)
					return own;

				foreach (var parent in w.Nodes.Where(n => n.Key == "Inherits" || n.Key.StartsWith("Inherits@", StringComparison.Ordinal)).Reverse())
				{
					var inheritedValue = Resolve(parent.Value.Value, field, seen);
					if (inheritedValue != null)
						return inheritedValue;
				}

				return null;
			}

			Assert.That(weapons.ContainsKey(name), Is.True, $"weapon '{name}' not found — this test is scanning nothing");

			var nodes = new List<MiniYamlNode>();
			var valid = Resolve(name, "ValidTargets", new HashSet<string>());
			var invalid = Resolve(name, "InvalidTargets", new HashSet<string>());
			if (valid != null)
				nodes.Add(new MiniYamlNode("ValidTargets", valid));
			if (invalid != null)
				nodes.Add(new MiniYamlNode("InvalidTargets", invalid));

			return new WeaponInfo(new MiniYaml("", nodes));
		}

		[Test]
		public void EveryArmorClassTargetableIsGroundOnly()
		{
			var actors = Airframes();
			var offenders = new List<string>();
			var scanned = 0;

			foreach (var (name, actor) in actors)
			{
				var armor = actor.Nodes.FirstOrDefault(n => n.Key == ArmorTargetable);
				if (armor == null)
					continue;

				scanned++;
				var condition = armor.Value.Nodes.FirstOrDefault(n => n.Key == "RequiresCondition")?.Value.Value;
				if (condition != GroundOnly)
					offenders.Add($"{name} (RequiresCondition: {condition ?? "<none>"})");
			}

			Assert.That(scanned, Is.GreaterThan(0), $"no {ArmorTargetable} found on any airframe — this test is scanning nothing");

			Assert.That(offenders, Is.Empty,
				$"{ArmorTargetable} must carry `RequiresCondition: {GroundOnly}` on every airframe. An armour class " +
				"is a GROUND fact: leaving it ungated makes an airborne actor permanently advertise Light/Medium/Heavy, " +
				"so every ground weapon naming an armour class (grenade launchers, mortars) can shoot it out of the sky. " +
				"Offenders: " + string.Join("; ", offenders));
		}

		[Test]
		public void GrenadierCannotReachAnAirborneHelicopter()
		{
			var actors = Airframes();
			var airborne = EnabledTargetTypes(actors, "littlebird", airborne: true);

			Assert.That(Weapon("GrenadeLauncher").IsValidTarget(airborne), Is.False,
				"a grenadier can still target an airborne littlebird. GrenadeLauncher names no `Air` and no " +
				"`Helicopter`, so the only way it reaches one is an ungated armour class leaking `Light` into the " +
				"airframe's airborne target types.");

			Assert.That(Weapon("GrenadeLauncher.5mag").IsValidTarget(airborne), Is.False,
				"GrenadeLauncher.5mag inherits GrenadeLauncher's ValidTargets and must be gated with it");

			Assert.That(Weapon("60mm_Mortar").IsValidTarget(airborne), Is.False,
				"a mortar can still target an airborne littlebird — same ungated-armour-class leak as the grenadier");

			Assert.That(Weapon("12.7mm.Hind").IsValidTarget(airborne), Is.False,
				"12.7mm.Hind is commented GROUND ONLY as of 260815, with the air half moved to 12.7mm.Hind.AA. " +
				"It can still reach a Light helicopter, so that re-gate never actually took effect.");
		}

		[Test]
		public void GrenadeSplashCannotDamageAnAirborneHelicopter()
		{
			// Targetability was only half the path. GrenadeLauncher's `Warhead@Target: TargetDamage` declares
			// no ValidTargets, so it takes the Warhead default {Ground, Water} (Warhead.cs:30) and never
			// touched an airborne actor even before the fix. The damage that actually landed came from
			// `Warhead@Spread: SpreadDamage`, which names `Light` explicitly — so a grenade detonating near a
			// hovering heli hurt it through the same leaked armour class. Warheads run their own
			// ValidTargets check against GetEnabledTargetTypes (Warhead.cs:74), independent of the weapon's.
			var actors = Airframes();
			var airborne = EnabledTargetTypes(actors, "littlebird", airborne: true);

			var grenade = MiniYaml.FromFile(FindRules("weapons", "weapons-ballistics.yaml"))
				.First(n => n.Key == "GrenadeLauncher").Value;

			var spread = grenade.Nodes.FirstOrDefault(n => n.Key == "Warhead@Spread");
			Assert.That(spread, Is.Not.Null, "GrenadeLauncher has no Warhead@Spread — this test is scanning nothing");

			var declared = spread.Value.Nodes.FirstOrDefault(n => n.Key == "ValidTargets")?.Value.Value;
			Assert.That(declared, Is.Not.Null, "Warhead@Spread declares no ValidTargets — this test is scanning nothing");

			var valid = new BitSet<TargetableType>(declared.Split(',').Select(t => t.Trim()).Where(t => t.Length > 0).ToArray());

			Assert.That(valid.Overlaps(airborne), Is.False,
				"GrenadeLauncher's splash warhead can still damage an airborne littlebird. This is the vector that " +
				"actually did the damage in the report — blocking the weapon's targeting without blocking the " +
				$"warhead would leave the heli taking splash from grenades landing under it. Warhead ValidTargets: {declared}");
		}

		[Test]
		public void TheArmorGateIsWhatDoesTheWork()
		{
			// Guard the guard. If the grenadier were unable to reach the helicopter for some reason OTHER
			// than the armour gate, the assertions above would pass while measuring nothing. Put `Light`
			// back and the grenadier MUST reach it again.
			var actors = Airframes();
			var leaked = EnabledTargetTypes(actors, "littlebird", airborne: true).Union(new BitSet<TargetableType>("Light"));

			Assert.That(Weapon("GrenadeLauncher").IsValidTarget(leaked), Is.True,
				"re-adding `Light` did NOT make the grenadier able to target the airborne littlebird, so the armour " +
				"class is not what this test is measuring and it proves nothing about the fix");
		}

		[Test]
		public void AntiAirIsUntouched()
		{
			// The failure mode worth more than the bug: gating armour classes must not disarm anything that
			// was shooting aircraft on purpose. Every AA weapon in the mod reaches air via `Air` or
			// `Helicopter`, never via an armour class — pin a representative spread of both factions.
			var actors = Airframes();

			foreach (var frame in new[] { "littlebird", "TRAN", "HALO", "HELI", "HIND", "MI28", "A10", "F16", "MIG", "FROG" })
			{
				var airborne = EnabledTargetTypes(actors, frame, airborne: true);
				foreach (var aa in new[] { "Stinger", "MANPAD", "SurfaceToAirMissile", "9M311", "AACannon", "AirToAirMissile" })
					Assert.That(Weapon(aa).IsValidTarget(airborne), Is.True,
						$"{aa} can no longer engage an airborne {frame}. Gating armour classes has disarmed anti-air, " +
						"which is far worse than the bug it was fixing.");
			}
		}

		[Test]
		public void SmallArmsKeepTheirHelicopterCounterplay()
		{
			// Helicopters are meant to stay vulnerable to infantry. That counterplay rides on the
			// `Helicopter` target type (^Helicopter, aircraft.yaml:172), which is deliberately ungated —
			// unlike the armour class. Gating armour must not take rotorcraft out of small-arms reach.
			var actors = Airframes();

			foreach (var heli in new[] { "littlebird", "TRAN", "HALO", "HELI", "HIND", "MI28" })
			{
				var airborne = EnabledTargetTypes(actors, heli, airborne: true);
				foreach (var arm in new[] { "7.62mm.MG", "12.7mm.MG", "5.56mm.AR" })
					Assert.That(Weapon(arm).IsValidTarget(airborne), Is.True,
						$"{arm} can no longer engage an airborne {heli}, so infantry have lost their counterplay " +
						"against rotorcraft. It names `Helicopter` in ValidTargets and that must keep binding.");
			}
		}

		[Test]
		public void LandedAircraftStayVulnerableToGroundFire()
		{
			// The inverse failure: an aircraft on the ground must remain an ordinary ground target, armour
			// class and all. `airborne` is purely altitude >= MinAirborneAltitude (Aircraft.cs:988), so a
			// landed or crash-disabled heli is !airborne and the gate re-enables its armour class.
			var actors = Airframes();
			var landed = EnabledTargetTypes(actors, "littlebird", airborne: false);

			Assert.That(Weapon("GrenadeLauncher").IsValidTarget(landed), Is.True,
				"a grenadier can no longer hit a LANDED littlebird. The gate was supposed to remove the armour " +
				"class only while airborne; removing it on the ground deletes intended counterplay.");
		}
	}
}
