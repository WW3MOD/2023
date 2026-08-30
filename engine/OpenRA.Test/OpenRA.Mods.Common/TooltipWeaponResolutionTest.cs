#region Copyright & License Information
/*
 * Copyright 2007-2022 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace OpenRA.Test
{
	/// <summary>
	/// <para>Pins the SPAWNER-WEAPON TRAP for anything that renders per-weapon stats in a tooltip.</para>
	///
	/// <para>The obvious implementation of "show this weapon's damage" reads
	/// <c>Armament.Weapon</c> and takes its warhead's <c>Damage</c>. On the HIMARS that produces
	/// <b>50</b> — for the most expensive strike in the game, whose real payload is <b>36000</b>.
	/// The armament names <c>HIMARSTargeter</c>, an <c>InstantHit</c> stub with an all-zero
	/// <c>Versus</c> table whose only job is to trigger <c>MissileSpawnerMaster</c>; the damage
	/// lives on <c>HIMARSExplosion</c>, reached through the spawned <c>HIMARSMissile</c> actor.</para>
	///
	/// <para>That failure is invisible in review: the number renders, it is plausible, and it is
	/// wrong by nearly three orders of magnitude. A comment does not stop it — this project has
	/// already proved that prose is not a countermeasure — so the relationship is pinned here
	/// instead.</para>
	///
	/// <para>These tests read the shipped YAML rather than a fixture, so they fail if the chain is
	/// renamed, re-pointed, or if someone gives the targeter real damage and quietly makes the naive
	/// reading "correct" for the wrong reason.</para>
	/// </summary>
	[TestFixture]
	public class TooltipWeaponResolutionTest
	{
		// Every actor whose armament names a trigger stub rather than the weapon that hurts anything.
		// Add a row here when a new spawner platform ships.
		static readonly (string Actor, string ActorFile, string ArmamentWeapon, string SpawnedActor, string PayloadWeapon)[] SpawnerPlatforms =
		{
			("HIMARS", "vehicles-america.yaml", "HIMARSTargeter", "HIMARSMissile", "HIMARSExplosion"),
			("iskander", "vehicles-russia.yaml", "IskanderTargeter", "IskanderMissile", "IskanderExplosion"),
		};

		static string FindRules(params string[] relative)
		{
			var dir = new DirectoryInfo(AppContext.BaseDirectory);
			for (var i = 0; i < 10 && dir != null; i++, dir = dir.Parent)
			{
				var parts = new[] { dir.FullName, "mods", "ww3mod", "rules" }.Concat(relative).ToArray();
				var candidate = Path.Combine(parts);
				if (File.Exists(candidate))
					return candidate;
			}

			throw new FileNotFoundException("could not locate mods/ww3mod/rules/" + string.Join("/", relative));
		}

		static MiniYamlNode Weapon(string name)
		{
			foreach (var file in new[] { "weapons-missiles.yaml", "weapons-ballistics.yaml", "weapons-explosions.yaml", "weapons-other.yaml" })
			{
				var node = MiniYaml.FromFile(FindRules("weapons", file)).FirstOrDefault(n => n.Key == name);
				if (node != null)
					return node;
			}

			return null;
		}

		/// <summary>
		/// Largest Damage on any warhead of this weapon, FOLLOWING <c>Inherits:</c>.
		///
		/// Resolving inheritance is not a nicety here. `HIMARSTargeter` declares no warhead of its
		/// own — it inherits `IskanderTargeter`'s — so a version of this helper that read only
		/// locally-declared nodes returned 0 for the stub, which made the comparison below
		/// `payload > 0` and therefore true for any payload at all. That version passed with the
		/// payload sabotaged to 50, i.e. it pinned nothing. Caught by running it red.
		/// </summary>
		static int ResolvedDamage(string weaponName, int depth = 0)
		{
			if (depth > 8)
				return 0;

			var weapon = Weapon(weaponName);
			if (weapon == null)
				return 0;

			var best = 0;
			foreach (var warhead in weapon.Value.Nodes.Where(n => n.Key.StartsWith("Warhead", StringComparison.Ordinal)))
			{
				var dmg = warhead.Value.Nodes.FirstOrDefault(n => n.Key == "Damage");
				if (dmg != null && int.TryParse(dmg.Value.Value.Trim(), out var v) && v > best)
					best = v;
			}

			if (best > 0)
				return best;

			foreach (var inherit in weapon.Value.Nodes.Where(n => n.Key.StartsWith("Inherits", StringComparison.Ordinal)))
			{
				var parent = ResolvedDamage(inherit.Value.Value.Trim(), depth + 1);
				if (parent > best)
					best = parent;
			}

			return best;
		}

		// A payload has to clear this in absolute terms, not merely out-scale the stub. Without it
		// the ratio test alone is satisfiable by shrinking the stub, which is the direction a
		// refactor is most likely to push it.
		const int MinimumRealPayloadDamage = 1000;

		[Test]
		public void ArmamentWeaponIsNotTheDamageSourceOnSpawnerPlatforms()
		{
			foreach (var p in SpawnerPlatforms)
			{
				var stub = Weapon(p.ArmamentWeapon);
				Assert.That(stub, Is.Not.Null,
					$"{p.ArmamentWeapon} not found — this test is scanning nothing. If the weapon was " +
					"renamed, update SpawnerPlatforms rather than deleting the row.");

				var payload = Weapon(p.PayloadWeapon);
				Assert.That(payload, Is.Not.Null,
					$"{p.PayloadWeapon} not found — the payload this platform actually fires. " +
					"If it was renamed, update SpawnerPlatforms.");

				var stubDamage = ResolvedDamage(p.ArmamentWeapon);
				var payloadDamage = ResolvedDamage(p.PayloadWeapon);

				Assert.That(stubDamage, Is.GreaterThan(0),
					$"{p.ArmamentWeapon}: resolved to 0 damage, which means inheritance resolution is " +
					"broken and every comparison below is vacuous. Fix the helper, not the assertion.");

				Assert.That(payloadDamage, Is.GreaterThanOrEqualTo(MinimumRealPayloadDamage),
					$"{p.Actor}: the payload weapon {p.PayloadWeapon} declares {payloadDamage} damage, " +
					$"below the {MinimumRealPayloadDamage} floor for a weapon that is supposed to be the " +
					"real munition. Either the chain now points somewhere else, or a tooltip reading it " +
					"would report a number nobody sanity-checked.");

				Assert.That(payloadDamage, Is.GreaterThan(stubDamage * 10),
					$"{p.Actor}: the payload weapon {p.PayloadWeapon} ({payloadDamage}) is no longer " +
					$"far larger than the armament weapon {p.ArmamentWeapon} ({stubDamage}). Either the " +
					"chain changed, or the targeter gained real damage. A tooltip that reads " +
					"Armament.Weapon would now be reporting a number nobody sanity-checked.");
			}
		}

		[Test]
		public void SpawnerPlatformArmamentStillNamesTheStub()
		{
			// The trap only exists while the armament genuinely points at the trigger weapon.
			// If this ever stops being true the chain was reworked and the guard above needs rethinking
			// rather than silently continuing to pass.
			foreach (var p in SpawnerPlatforms)
			{
				var text = File.ReadAllText(FindRules("ingame", p.ActorFile));
				Assert.That(text, Does.Contain($"Weapon: {p.ArmamentWeapon}"),
					$"{p.Actor}: no armament in {p.ActorFile} names {p.ArmamentWeapon}. The spawner chain " +
					"changed shape; re-derive what a tooltip should read before trusting this fixture.");

				Assert.That(text, Does.Contain($"Actors: {p.SpawnedActor}"),
					$"{p.Actor}: no spawner in {p.ActorFile} names {p.SpawnedActor}, so the documented " +
					"route from trigger to payload is broken.");
			}
		}

		[Test]
		public void TargeterStubsCannotHurtAnythingThroughTheirVersusTable()
		{
			// The other half of why the stub's damage is meaningless: its Versus table zeroes every
			// armour class the mod uses, so even the 50 it declares lands as 0. A tooltip printing
			// "Damage 50" would be overstating a weapon that does literally nothing on impact.
			foreach (var p in SpawnerPlatforms)
			{
				var stub = Weapon(p.ArmamentWeapon);
				var versus = stub.Value.Nodes
					.Where(n => n.Key.StartsWith("Warhead", StringComparison.Ordinal))
					.SelectMany(n => n.Value.Nodes)
					.FirstOrDefault(n => n.Key == "Versus");

				// IskanderTargeter declares it; HIMARSTargeter inherits it. Only assert where declared.
				if (versus == null)
					continue;

				foreach (var armour in versus.Value.Nodes)
					Assert.That(armour.Value.Value.Trim(), Is.EqualTo("0"),
						$"{p.ArmamentWeapon}: Versus[{armour.Key}] is no longer 0. This weapon is a " +
						"trigger stub; if it has started doing real damage, the tooltip story for " +
						$"{p.Actor} changes and this fixture is out of date.");
			}
		}
	}
}
