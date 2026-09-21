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
	/// <b>0</b> — for the most expensive strike in the game, whose real payload is <b>36000</b>.
	/// The armament names <c>HIMARSTargeter</c>, an <c>InstantHit</c> stub whose only job is to
	/// trigger <c>MissileSpawnerMaster</c>; the damage lives on <c>HIMARSExplosion</c>, reached
	/// through the spawned <c>HIMARSMissile</c> actor.</para>
	///
	/// <para>The naive reading produced <b>50</b> until 260921, when the targeters were corrected to
	/// <c>Damage: 0</c>: their <c>Versus</c> table zeroed six armour classes but OMITTED Kevlar, and
	/// an omitted class takes the unmodified 100%, so the stub was dealing that 50 to every soldier
	/// in the game. The reading is wrong either way — the number it reports moved, it did not stop
	/// being wrong. What changed is that the stub dealing damage is no longer available as proof
	/// that anything resolved; see <see cref="ResolvedWarheads"/>.</para>
	///
	/// <para>That failure is invisible in review: the number renders, it is plausible, and it is
	/// wrong by orders of magnitude or by being nothing at all. A comment does not stop it — this
	/// project has already proved that prose is not a countermeasure — so the relationship is
	/// pinned here instead.</para>
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
		/// <para>Warhead nodes of this weapon, FOLLOWING <c>Inherits:</c>. This is the single
		/// resolution walk the fixture owns — <see cref="ResolvedDamage"/> reads its numbers out of
		/// what this returns, so there is no second copy of the inheritance logic to drift.</para>
		///
		/// <para>Resolving inheritance is not a nicety here. <c>HIMARSTargeter</c> declares no warhead
		/// of its own — it inherits <c>IskanderTargeter</c>'s — so a version of this helper that read
		/// only locally-declared nodes returned nothing for the stub, which made the comparison below
		/// <c>payload &gt; 0</c> and therefore true for any payload at all. That version passed with
		/// the payload sabotaged to 50, i.e. it pinned nothing. Caught by running it red.</para>
		///
		/// <para>A weapon that declares warheads of its own does NOT consult its parents: a child that
		/// overrides <c>Damage</c> down to zero must resolve to zero, not to whatever the parent
		/// declared. The earlier helper fell through to the parent whenever the local best was 0, and
		/// so could not have represented the targeters as they stand today.</para>
		/// </summary>
		static MiniYamlNode[] ResolvedWarheads(string weaponName, int depth = 0)
		{
			if (depth > 8)
				return Array.Empty<MiniYamlNode>();

			var weapon = Weapon(weaponName);
			if (weapon == null)
				return Array.Empty<MiniYamlNode>();

			var local = weapon.Value.Nodes
				.Where(n => n.Key.StartsWith("Warhead", StringComparison.Ordinal))
				.ToArray();

			if (local.Length > 0)
				return local;

			foreach (var inherit in weapon.Value.Nodes.Where(n => n.Key.StartsWith("Inherits", StringComparison.Ordinal)))
			{
				var parent = ResolvedWarheads(inherit.Value.Value.Trim(), depth + 1);
				if (parent.Length > 0)
					return parent;
			}

			return Array.Empty<MiniYamlNode>();
		}

		/// <summary>
		/// Largest Damage on any RESOLVED warhead of this weapon. <b>0 is a legitimate answer</b> —
		/// both targeters declare exactly that — so a zero from here is never, on its own, evidence
		/// that resolution ran, and must not be used as one.
		/// </summary>
		static int ResolvedDamage(string weaponName)
		{
			var best = 0;
			foreach (var warhead in ResolvedWarheads(weaponName))
			{
				var dmg = warhead.Value.Nodes.FirstOrDefault(n => n.Key == "Damage");
				if (dmg != null && int.TryParse(dmg.Value.Value.Trim(), out var v) && v > best)
					best = v;
			}

			return best;
		}

		// A payload has to clear this in absolute terms, not merely out-scale the stub. Without it
		// the ratio test alone is satisfiable by shrinking the stub, which is the direction a
		// refactor is most likely to push it — and as of 260921 the stub sits at zero, so the ratio
		// test is `payload > 0` and this floor is the only thing giving that comparison any teeth.
		const int MinimumRealPayloadDamage = 1000;

		// The non-vacuity control for the NUMERIC half of resolution. TankRound.Abrams
		// (weapons-ballistics.yaml:860) declares no warhead of its own; every point of its damage
		// reaches it through `Inherits@Type: ^TankRound`. A resolver that has stopped walking
		// Inherits:, or stopped reading Damage at all, cannot produce a non-zero number for it.
		//
		// The spawner platforms can no longer do this job themselves, which is why this constant
		// exists: their targeters declare Damage: 0 by design, and their payload weapons declare
		// their damage LOCALLY, so neither one exercises the inherited-number path any more.
		const string InheritedDamageControlWeapon = "TankRound.Abrams";

		[Test]
		public void ArmamentWeaponIsNotTheDamageSourceOnSpawnerPlatforms()
		{
			// NON-VACUITY, NUMERIC HALF — asserted once, before the loop, because everything below
			// compares resolved damage numbers and is worthless if the resolver returns zeros.
			//
			// This used to be `stub damage > 0`, on the reasoning that the stub's 50 could only be
			// reached by inheriting. That premise died on 260921 when the targeters were corrected to
			// Damage: 0: the old guard now fails BY DESIGN rather than on breakage, and no assertion
			// about the stub's magnitude can replace it, because the value it would have to assert is
			// the same 0 a totally broken resolver returns. The witness therefore has to be a weapon
			// that is NOT part of the chain under test.
			var control = Weapon(InheritedDamageControlWeapon);
			Assert.That(control, Is.Not.Null,
				$"{InheritedDamageControlWeapon} not found. It is this fixture's proof that inheritance " +
				"resolution still works at all; if it was renamed, point this at another weapon that " +
				"declares no warhead of its own and inherits a non-zero one. Do not delete the check.");

			Assert.That(control.Value.Nodes.Any(n => n.Key.StartsWith("Warhead", StringComparison.Ordinal)), Is.False,
				$"{InheritedDamageControlWeapon} has gained a warhead of its own, so resolving it no " +
				"longer walks Inherits: and it has quietly stopped being a control — it would now pass " +
				"against the very resolver breakage it is here to catch. Pick another inherits-only weapon.");

			Assert.That(ResolvedDamage(InheritedDamageControlWeapon), Is.GreaterThan(0),
				$"{InheritedDamageControlWeapon} resolved to 0 damage, and it can only get damage " +
				"through Inherits:. Inheritance resolution is broken and every comparison below is " +
				"vacuous. Fix the helper, not the assertion.");

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

				// NON-VACUITY, STRUCTURAL HALF — per platform, and the half that covers THIS chain.
				// HIMARSTargeter declares no warhead of its own, so a non-empty set here can only have
				// arrived through Inherits: from IskanderTargeter. That is the precise step the first
				// version of the helper got wrong, and it is provable without reading a single number.
				var stubWarheads = ResolvedWarheads(p.ArmamentWeapon);
				Assert.That(stubWarheads, Is.Not.Empty,
					$"{p.ArmamentWeapon}: resolved to no warheads at all, so the damage comparisons below " +
					"are reading nothing. Either the weapon stopped declaring or inheriting a warhead, or " +
					"resolution is broken. Fix the helper, not the assertion.");

				var stubDamage = ResolvedDamage(p.ArmamentWeapon);
				var payloadDamage = ResolvedDamage(p.PayloadWeapon);

				// The declared value, pinned. 0 is the whole point: the targeter is a trigger, and the
				// 50 that sat here until 260921 was landing in full on every soldier in the game because
				// the Versus table omits Kevlar. A non-zero here means someone gave the stub real damage
				// and made the naive Armament.Weapon reading look "correct" for the wrong reason.
				Assert.That(stubDamage, Is.EqualTo(0),
					$"{p.ArmamentWeapon}: resolved to {stubDamage} damage, but a targeter stub must declare " +
					"Damage: 0 — its Versus table is deliberately incomplete, so any non-zero value lands " +
					"in full on every armour class the table omits. The real payload is " +
					$"{p.PayloadWeapon}; put the damage there.");

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
		public void StubsWithNoWarheadOfTheirOwnResolveToTheirParentsWarheads()
		{
			// Inheritance proven STRUCTURALLY, with no reference to damage magnitude — the property the
			// old `stub damage > 0` guard was standing in for, now that the stub legitimately declares
			// zero. If this stops holding, either the chain was re-pointed or the resolver stopped
			// walking Inherits:, and both make the tooltip guard above meaningless.
			var exercised = 0;
			foreach (var p in SpawnerPlatforms)
			{
				var stub = Weapon(p.ArmamentWeapon);
				if (stub == null)
					continue;

				// Only the inheriting stubs can witness anything here. IskanderTargeter declares its own
				// warhead and is skipped; HIMARSTargeter declares none and is the live case.
				if (stub.Value.Nodes.Any(n => n.Key.StartsWith("Warhead", StringComparison.Ordinal)))
					continue;

				var parents = stub.Value.Nodes
					.Where(n => n.Key.StartsWith("Inherits", StringComparison.Ordinal))
					.Select(n => n.Value.Value.Trim())
					.ToArray();

				Assert.That(parents, Is.Not.Empty,
					$"{p.ArmamentWeapon} declares no warhead AND inherits nothing, so it cannot have a " +
					"warhead at all. The armament on this platform now fires a weapon that does nothing.");

				var resolved = ResolvedWarheads(p.ArmamentWeapon).Select(n => n.Key).ToArray();
				var fromParents = parents
					.Select(parent => ResolvedWarheads(parent).Select(n => n.Key).ToArray())
					.FirstOrDefault(keys => keys.Length > 0);

				Assert.That(fromParents, Is.Not.Null,
					$"{p.ArmamentWeapon} inherits {string.Join(", ", parents)}, none of which resolves to " +
					"any warhead. The stub's only warhead came from there.");

				Assert.That(resolved, Is.EqualTo(fromParents),
					$"{p.ArmamentWeapon} resolved to [{string.Join(", ", resolved)}] but its parent " +
					$"resolves to [{string.Join(", ", fromParents)}]. It declares no warhead of its own, " +
					"so those sets must be the same set; if they are not, inheritance resolution is broken.");

				exercised++;
			}

			// Without this the loop is satisfiable by skipping every row — which is exactly what happens
			// if someone gives HIMARSTargeter a warhead of its own, at which point this test would go on
			// passing while testing nothing.
			Assert.That(exercised, Is.GreaterThan(0),
				"no spawner stub inherits its warhead any more, so this test asserted nothing. If the " +
				"chain was flattened deliberately, delete this test; do not leave it passing vacuously.");
		}

		[Test]
		public void TargeterStubsCannotHurtAnythingThroughTheirVersusTable()
		{
			// What this does and does NOT prove. It pins that every class the table LISTS is zeroed —
			// nothing more. The table is deliberately incomplete: it omits Kevlar, Unarmored and
			// Indestructable, and DamageWarhead.DamageVersus filters the victim's armours by
			// Versus.ContainsKey, so an omitted class matches nothing and takes the unmodified 100%.
			// Omission is the OPPOSITE of a zero, which is how the 50 that used to sit on these
			// warheads reached every soldier in the game until 260921.
			//
			// So this test is not, and never was, the reason the stubs are harmless. `Damage: 0` is.
			// Do NOT "complete" the table to make it one: ArmorInfo.ProvideTooltipDescription
			// (Traits/Armor.cs:73) asks whether ANY warhead in the ruleset names a type, so naming
			// Kevlar even at 0 flips every infantry tooltip from "None" to "Kevlar".
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
