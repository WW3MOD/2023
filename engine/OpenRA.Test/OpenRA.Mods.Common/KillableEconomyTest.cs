#region Copyright & License Information
/*
 * WW3MOD killable economy — corpus pin for the heavy/light split and for husk permanence.
 *
 * User request 2026-09-06: "I want to make money structures destroyable by powerful weapons, like
 * iskanders, strike powers and by nukes of course", amended the same day with the half that turned
 * out to matter more: "the destruction of money structures are not fully destroyed, they leave the
 * repairable wreck behind that can be repaired by engineer."
 *
 * TWO RULES, AND THIS FIXTURE EXISTS BECAUSE BOTH FAIL SILENTLY.
 *
 *   1. DAMAGEABILITY. ^TechBuilding drops `Ground` and `Structure` from its Targetable and carries
 *      `TechStructure` instead, so the ONLY weapons that can hurt an oil derrick are the ones that
 *      name that type (weapons-heavy-ordnance.yaml) plus engineer C4. Get this wrong in the
 *      permissive direction and every rifle in the game can raze the economy; get it wrong in the
 *      restrictive direction and a nuke lands on a derrick and does nothing. NEITHER IS VISIBLE
 *      without firing the weapon, and the restrictive failure looks exactly like a missile that
 *      missed.
 *
 * AMENDED 2026-09-11, and rule 2 below now applies to the NUCLEAR TIER ONLY: "Conventional strike
 * powers should not completely obliterate structures, like the derrick. It should destroy them in
 * the same way as C4 does, so that it spawns the wrecked actor that can then be repaired with an
 * engineer and captured again by a technician. Nukes should be the only thing that can obliterate
 * such a structure within a small area."
 *
 * So reach and permanence, which this fixture was originally written to pin TOGETHER, are now
 * deliberately INDEPENDENT: every weapon on the roster still reaches, and only the two nuclear ones
 * still suppress the wreck. TechStructureWarheadsSplitPermanenceByTier is where that is enforced,
 * and it fails in both directions. Read the original rule 2 below as the statement of what the
 * nuclear tier still does, not as a property of the whole roster.
 *
 *   2. PERMANENCE. A heavy kill must leave nothing behind. The husk (husks-neutral.yaml) inherits
 *      ^TechBuildingHusk, which carries InfiltrateForTransform against ^E6's
 *      Infiltrates@RestoreTechHusk — one engineer restores the whole structure at 10% HP. So a
 *      Warhead@TechStructure that forgot its `HeavyOrdnanceDeath` damage type would produce a
 *      weapon that DOES damage and does not DESTROY: the nuke kills the derrick, the defender walks
 *      an engineer in, and the money is back. That is precisely the defect reported, and it is
 *      invisible from the attacker's side — the building blew up.
 *
 * The two properties were therefore pinned TOGETHER, and that pairing was the single most
 * load-bearing assertion here. IT IS NOW A SPLIT rather than a pairing — see the 2026-09-11
 * amendment below — and the assertion that carries it is TechStructureWarheadsSplitPermanenceByTier,
 * which enforces reach for the whole roster and permanence for the nuclear tier alone.
 *
 * Reads the shipped YAML through the real manifest merge rather than a fixture, for the reason
 * AirborneArmorTargetableTest gives: the thing being protected is the corpus, and the guard has to
 * cover weapons that do not exist yet.
 */
#endregion

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class KillableEconomyTest
	{
		const string TargetType = "TechStructure";
		const string DeathType = "HeavyOrdnanceDeath";
		const string Template = "^TechBuilding";

		/// <summary>
		/// The roster, restated independently of the file under test. Duplicating it here is the
		/// point: if someone adds a weapon to weapons-heavy-ordnance.yaml the balance question
		/// ("should THIS be able to delete an economy structure?") gets asked once, by a human,
		/// rather than sliding in behind a green test run.
		/// </summary>
		static readonly string[] ExpectedHeavyWeapons =
		{
			"Atomic",              // tac nuke (MissileStrikePower@TacNuke) + mslo's NukePower
			"AtomicHighYield",     // MissileStrikePower@HighYieldNuke
			"IskanderExplosion",   // the iskander launcher, the Kinzhal strike, and a loaded cook-off
			"MOPPenetration",      // GBU-57, MissileStrikePower@GBU57

			// ADDED 2026-09-09 (wt/oreshnik), and the ruling this fixture exists to force was made
			// rather than dodged. OreshnikRVExplosion is the conventional payload of
			// MissileStrikePower@Oreshnik -- Russia's 18000-credit six-warhead conventional strike.
			//
			// LISTED, because its whole class already is: every purchased, one-shot, off-map strike
			// above is here. The exclusion weapons-heavy-ordnance.yaml argues for is HIMARSExplosion,
			// on the grounds that a HIMARS is a rearmable battlefield system firing repeatedly, so
			// listing it would make the economy raidable by ordinary manoeuvre. An Oreshnik is not
			// manoeuvre; it is a bought shot.
			//
			// WHAT IT BUYS, SAID OUT LOUD BECAUSE IT IS THE STRONGEST ENTRY ON THE ROSTER BY REACH:
			// AimPoints is 6, so one activation can permanently remove SIX neutral tech buildings --
			// more economy than anything else here takes in one go. Nothing else on this list is
			// multi-aim-point. If the power turns out too strong this block is the first thing to
			// delete, and doing so costs the weapon nothing else.
			"OreshnikRVExplosion",

			// NOT a roster entry -- an inheritance consequence, and it is listed here because
			// this fixture is what discovered it. `IskanderExplosionAirborne: Inherits:
			// IskanderExplosion` (weapons-explosions.yaml:620), so it picks up Warhead@TechStructure
			// for free, and MiniYaml.Merge resolves that before the corpus is read. Correct on the
			// merits -- it is the same warhead, and an airburst Iskander should do what a ground one
			// does -- but worth knowing it is reachable only through inheritance: NOTHING in the mod
			// fires this weapon today (grep returns only its own definition), so it is currently
			// inert either way.
			"IskanderExplosionAirborne",
		};

		/// <summary>
		/// <para>The NUCLEAR tier, and the ONLY weapons whose kills may leave nothing behind. User
		/// ruling 2026-09-11: "Conventional strike powers should not completely obliterate
		/// structures, like the derrick. It should destroy them in the same way as C4 does, so that
		/// it spawns the wrecked actor that can then be repaired with an engineer and captured
		/// again by a technician. Nukes should be the only thing that can obliterate such a
		/// structure within a small area."</para>
		///
		/// <para>This is a strict subset of <see cref="ExpectedHeavyWeapons"/>: every weapon here
		/// can also REACH a tech structure, and the two properties are now independent. The
		/// complement — IskanderExplosion (+Airborne), MOPPenetration, OreshnikRVExplosion — must
		/// NOT carry the death type, which is what makes a Kinzhal, a GBU-57 or an Oreshnik leave
		/// the restorable husk that engineer C4 has always left.</para>
		///
		/// <para>NOTE the nuclear tier has a SECOND and independent way to obliterate that no list
		/// here governs: VaporizeWarhead ignores ValidTargets outright (VaporizeWarhead.cs:83-98),
		/// so `Warhead@VaporizeRemoval` removes a tech building inside the fireball whatever its
		/// damage types say. That is the "within a small area" half of the ruling, and it is why
		/// the ten warheads in weapons-nuclear-arsenal.yaml can erase a derrick while carrying no
		/// Warhead@TechStructure at all.</para>
		/// </summary>
		static readonly string[] ExpectedObliteratingWeapons =
		{
			"Atomic",           // tac nuke (MissileStrikePower@TacNuke) + mslo's NukePower
			"AtomicHighYield",  // MissileStrikePower@HighYieldNuke
		};

		/// <summary>
		/// Weapons that must NOT reach a tech structure, chosen to span the ways a mistake could be
		/// made: small arms, a tank main gun, tube artillery, and — the one that matters —
		/// HIMARSExplosion, the tactical twin of IskanderExplosion. That last one is a deliberate
		/// balance ruling (a rearmable battlefield MLRS must not raze the economy), not an oversight,
		/// so it is pinned rather than left to be "fixed" by the next reader.
		/// </summary>
		static readonly string[] ExpectedLightWeapons =
		{
			"HIMARSExplosion", "ArtilleryExplode", "BuildingExplode", "BarrelExplode",
		};

		/// <summary>Warhead types that actually inflict damage. Everything else on a weapon — screen
		/// shake, smudges, flashes, fire conditions, suppression — cannot kill a building however its
		/// ValidTargets read, so admitting a TechStructure from one of those proves nothing.</summary>
		static readonly HashSet<string> DamagingWarheads = new()
		{
			"SpreadDamage", "TargetDamage", "ShockwaveDamage", "ThermalRadiation",
		};

		static DirectoryInfo ModDir()
		{
			var dir = new DirectoryInfo(AppContext.BaseDirectory);
			for (var i = 0; i < 10 && dir != null; i++, dir = dir.Parent)
			{
				var candidate = new DirectoryInfo(Path.Combine(dir.FullName, "mods", "ww3mod"));
				if (candidate.Exists)
					return candidate;
			}

			throw new DirectoryNotFoundException("could not locate mods/ww3mod");
		}

		/// <summary>
		/// Every weapon, merged in the order mod.yaml actually lists the files. Going through the
		/// manifest rather than globbing the directory is load-bearing twice over: the ADD-a-warhead
		/// trick in weapons-heavy-ordnance.yaml only works if that file merges LAST, and a new weapon
		/// file that someone forgets to register would otherwise be silently included here and
		/// silently absent from the game.
		/// </summary>
		static Dictionary<string, MiniYaml> Weapons()
		{
			var mod = ModDir();
			var manifest = MiniYaml.FromFile(Path.Combine(mod.FullName, "mod.yaml"));
			var weaponsNode = manifest.FirstOrDefault(n => n.Key == "Weapons");
			Assert.That(weaponsNode, Is.Not.Null, "mod.yaml has no Weapons section");

			var files = weaponsNode.Value.Nodes
				.Select(n => n.Key.Split('|').Last())
				.ToArray();

			Assert.That(files, Does.Contain("rules/weapons/weapons-heavy-ordnance.yaml"),
				"the heavy-ordnance roster is not registered in mod.yaml — every weapon in it is inert.");
			Assert.That(files.Last(), Is.EqualTo("rules/weapons/weapons-heavy-ordnance.yaml"),
				"weapons-heavy-ordnance.yaml must be listed LAST: it merges Warhead@TechStructure nodes " +
				"onto weapons defined in the files above it.");

			var sources = files
				.Select(f => (IReadOnlyCollection<MiniYamlNode>)MiniYaml.FromFile(Path.Combine(mod.FullName, f)))
				.ToArray();

			var merged = MiniYaml.Merge(sources).ToDictionary(n => n.Key, n => n.Value);

			Assert.That(merged.Count, Is.GreaterThan(100),
				$"only {merged.Count} weapons parsed — this fixture is scanning nothing, not passing.");

			return merged;
		}

		static Dictionary<string, MiniYaml> RuleNodes(params string[] parts)
		{
			var path = Path.Combine(new[] { ModDir().FullName, "rules" }.Concat(parts).ToArray());
			return MiniYaml.FromFile(path).ToDictionary(n => n.Key, n => n.Value);
		}

		static BitSet<TargetableType> TargetTypes(string csv)
		{
			return new BitSet<TargetableType>(csv.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToArray());
		}

		static string Field(MiniYaml node, string key)
		{
			return node.Nodes.FirstOrDefault(n => n.Key == key)?.Value.Value;
		}

		/// <summary>
		/// The target types a live neutral tech building advertises, read off the shared template
		/// rather than hardcoded, so the fixture tracks the YAML instead of a copy of it.
		/// </summary>
		static BitSet<TargetableType> TechBuildingTargetTypes()
		{
			var structures = RuleNodes("ingame", "structures.yaml");
			Assert.That(structures.ContainsKey(Template), Is.True, $"{Template} not found in structures.yaml");

			var targetable = structures[Template].Nodes.FirstOrDefault(n => n.Key == "Targetable");
			Assert.That(targetable, Is.Not.Null, $"{Template} has no unsuffixed Targetable");

			return TargetTypes(Field(targetable.Value, "TargetTypes"));
		}

		/// <summary>All damaging warheads on a weapon that would land on the given target types.</summary>
		static List<MiniYamlNode> ReachingWarheads(MiniYaml weapon, BitSet<TargetableType> victim)
		{
			var reaching = new List<MiniYamlNode>();
			foreach (var wh in weapon.Nodes.Where(n => n.Key.StartsWith("Warhead", StringComparison.Ordinal)))
			{
				if (!DamagingWarheads.Contains(wh.Value.Value?.Trim() ?? ""))
					continue;

				// Warhead.cs:57 exactly: valid if ValidTargets overlaps and InvalidTargets does not.
				// The engine default when the field is absent is Ground|Water (Warhead.cs:30).
				var valid = TargetTypes(Field(wh.Value, "ValidTargets") ?? "Ground, Water");
				var invalid = TargetTypes(Field(wh.Value, "InvalidTargets") ?? "");

				if (valid.Overlaps(victim) && !invalid.Overlaps(victim))
					reaching.Add(wh);
			}

			return reaching;
		}

		// ---------------------------------------------------------------------------------------
		// 1. Damageability
		// ---------------------------------------------------------------------------------------

		/// <summary>
		/// The mechanism itself. These buildings are protected by being UNTARGETABLE, not by armour
		/// or by a damage multiplier — so the presence of `TechStructure` and the absence of `Ground`
		/// and `Structure` is the whole feature, and a well-meaning "let's make tech buildings behave
		/// like other buildings" edit would silently undo it.
		///
		/// `Structure` in particular must stay off: the Atomic/AtomicHighYield Warhead@Fire1-10 blocks
		/// target it to grant `onfire`, and ^BuildingAffectedByFire's ChangesHealth@BurnDamage carries
		/// no DamageTypes at all — so a burn tick could kill a derrick with an un-attributable death
		/// that spawns a restorable husk, defeating rule 2 below without touching it.
		/// </summary>
		[Test]
		public void TechBuildingsAreUntargetableExceptToTheDeclaredClass()
		{
			var types = TechBuildingTargetTypes();

			Assert.That(types.Contains(TargetType), Is.True,
				$"{Template} no longer advertises {TargetType}: every heavy weapon is now inert against it.");
			Assert.That(types.Contains("Ground"), Is.False,
				$"{Template} advertises Ground — every weapon in the game can now damage the economy.");
			Assert.That(types.Contains("Structure"), Is.False,
				$"{Template} advertises Structure — nuclear fire ticks can now kill a derrick with an " +
				"un-attributable death, which spawns a restorable husk and defeats the permanence rule.");
			Assert.That(types.Contains("NoAutoTarget"), Is.True,
				$"{Template} lost NoAutoTarget: AutoTargetPriority.InvalidTargets defaults to exactly " +
				"that (AutoTargetPriority.cs:27), so units and bots will now shoot neutral buildings unasked.");
			Assert.That(types.Contains("C4"), Is.True,
				$"{Template} lost C4: engineer demolition was the ONLY kill path before this feature " +
				"and is the recoverable one it deliberately keeps.");
		}

		/// <summary>A heavy warhead damages one; a rifle does not. The headline requirement.</summary>
		[Test]
		public void OnlyTheRosteredWeaponsCanDamageATechStructure()
		{
			var victim = TechBuildingTargetTypes();
			var weapons = Weapons();

			var reaching = weapons
				.Where(kv => ReachingWarheads(kv.Value, victim).Count > 0)
				.Select(kv => kv.Key)
				.OrderBy(k => k, StringComparer.Ordinal)
				.ToArray();

			Assert.That(reaching, Is.EquivalentTo(ExpectedHeavyWeapons),
				"the set of weapons that can damage a neutral tech building has changed. This is a " +
				"balance ruling, not an implementation detail — update ExpectedHeavyWeapons only if " +
				"the change was deliberate, and say so in the commit message.");
		}

		/// <summary>
		/// The negative direction, named weapon by weapon so a failure says which one leaked. Kept
		/// alongside the set-equality test above because that one fails identically for "a rifle got
		/// in" and "a nuke fell out", and those are opposite emergencies.
		/// </summary>
		[Test]
		public void OrdinaryWeaponsCannotScratchATechStructure()
		{
			var victim = TechBuildingTargetTypes();
			var weapons = Weapons();

			foreach (var name in ExpectedLightWeapons)
			{
				Assert.That(weapons.ContainsKey(name), Is.True, $"{name} no longer exists — update this fixture");
				Assert.That(ReachingWarheads(weapons[name], victim), Is.Empty,
					$"{name} can now damage neutral tech buildings. If that is intended it also needs a " +
					$"{DeathType} damage type, or it will damage them without being able to destroy them.");
			}
		}

		/// <summary>
		/// HIMARSTargeter inherits IskanderTargeter, which gained `TechStructure` so an Iskander can be
		/// clicked straight onto a derrick. Without the explicit re-statement in the roster file that
		/// affordance leaks, and a player gets to ORDER a HIMARS strike on a derrick that then lands
		/// and does nothing — the worst failure shape available here, because it looks like a bug in
		/// the missile rather than in the targeting.
		/// </summary>
		[Test]
		public void TheAimingAffordanceDoesNotLeakToTheTacticalLauncher()
		{
			var weapons = Weapons();
			var victim = TechBuildingTargetTypes();

			var iskander = TargetTypes(Field(weapons["IskanderTargeter"], "ValidTargets"));
			Assert.That(iskander.Overlaps(victim), Is.True,
				"IskanderTargeter can no longer be aimed at a tech building; the player must force-fire " +
				"the ground under it, which is a usability regression rather than a balance change.");

			var himars = TargetTypes(Field(weapons["HIMARSTargeter"], "ValidTargets"));
			Assert.That(himars.Overlaps(victim), Is.False,
				"HIMARSTargeter inherited the Iskander's aiming affordance. HIMARSExplosion has no " +
				"Warhead@TechStructure, so this lets a player order a strike that lands and does nothing.");
		}

		// ---------------------------------------------------------------------------------------
		// 2. Permanence
		// ---------------------------------------------------------------------------------------

		/// <summary>
		/// <para>The tier split, and the most valuable assertion in the file. It fails in BOTH
		/// directions on purpose, because the two failures are opposite emergencies:</para>
		///
		/// <para>A CONVENTIONAL weapon that GAINS <see cref="DeathType"/> silently restores the
		/// behaviour the 2026-09-11 ruling removed — a Kinzhal or an Oreshnik erasing an economy
		/// structure for good, with no wreck for an engineer to restore and nothing for a
		/// technician to capture afterwards.</para>
		///
		/// <para>A NUCLEAR weapon that LOSES it re-opens the original 2026-09-06 defect in the one
		/// place that ruling deliberately kept: the attacker spends a nuclear warhead and the
		/// defender spends one engineer.</para>
		///
		/// <para>Neither is visible from a build, from the sidebar, or from watching the strike
		/// land — in both cases the building blows up exactly as it should, and the difference
		/// appears a frame later as a husk that is or is not there.</para>
		/// </summary>
		[Test]
		public void TechStructureWarheadsSplitPermanenceByTier()
		{
			var victim = TechBuildingTargetTypes();
			var weapons = Weapons();
			var checkedAny = false;

			foreach (var (name, weapon) in weapons)
			{
				var obliterates = ExpectedObliteratingWeapons.Contains(name);

				foreach (var wh in ReachingWarheads(weapon, victim))
				{
					checkedAny = true;
					var damageTypes = (Field(wh.Value, "DamageTypes") ?? "")
						.Split(',').Select(s => s.Trim()).ToArray();

					if (obliterates)
						Assert.That(damageTypes, Does.Contain(DeathType),
							$"{name}/{wh.Key} is on the NUCLEAR tier but no longer carries {DeathType}. " +
							"It can therefore KILL a tech building without DESTROYING it: SpawnActorOnDeath " +
							"still fires, the husk spawns, and one engineer infiltrating it restores the " +
							"whole structure for the price of a nuclear warhead.");
					else
						Assert.That(damageTypes, Does.Not.Contain(DeathType),
							$"{name}/{wh.Key} is CONVENTIONAL but carries {DeathType}, so its kills leave " +
							"no wreck. User ruling 2026-09-11: only nuclear weapons may obliterate a tech " +
							"structure; a conventional strike must destroy it the way engineer C4 does, " +
							"leaving the husk that an engineer restores and a technician then captures. " +
							$"If this weapon really should obliterate, add it to {nameof(ExpectedObliteratingWeapons)} " +
							"and say so in the commit message — it is a balance ruling, not a detail.");
				}
			}

			Assert.That(checkedAny, Is.True, "no warhead reaches a tech structure at all — the feature is inert.");
		}

		/// <summary>
		/// The obliterating tier must be a strict subset of the roster. A weapon that may leave no
		/// wreck but cannot reach a tech building in the first place is a contradiction, and the
		/// most likely way to write one is to add a name to the nuclear list and forget the
		/// Warhead@TechStructure block that makes it mean anything.
		/// </summary>
		[Test]
		public void EveryObliteratingWeaponIsAlsoOnTheRoster()
		{
			foreach (var name in ExpectedObliteratingWeapons)
				Assert.That(ExpectedHeavyWeapons, Does.Contain(name),
					$"{name} is listed as obliterating but is not on the heavy roster, so it cannot " +
					"damage a tech structure at all and its permanence is unreachable.");
		}

		/// <summary>
		/// The other end of the same wire. The damage type is worthless unless the structures
		/// actually read it, and this is a per-actor field with no template to inherit from — a sixth
		/// tech building added later would silently ship with a restorable wreck.
		/// </summary>
		[Test]
		public void EveryTechBuildingSuppressesItsHuskOnHeavyOrdnanceDeath()
		{
			var neutral = RuleNodes("ingame", "structures-neutral.yaml");
			Assert.That(neutral, Is.Not.Empty, "structures-neutral.yaml parsed empty");

			var spawners = 0;
			foreach (var (name, actor) in neutral)
			{
				var spawn = actor.Nodes.FirstOrDefault(n => n.Key == "SpawnActorOnDeath");
				if (spawn == null)
					continue;

				spawners++;
				var excluded = (Field(spawn.Value, "ExcludedDeathTypes") ?? "")
					.Split(',').Select(s => s.Trim()).ToArray();

				Assert.That(excluded, Does.Contain(DeathType),
					$"{name} spawns {Field(spawn.Value, "Actor")} on death without excluding {DeathType}. " +
					"That husk carries InfiltrateForTransform via ^TechBuildingHusk, so a nuclear strike " +
					"on this building can be undone by walking one engineer into the rubble.");
			}

			Assert.That(spawners, Is.EqualTo(5),
				$"expected 5 husk-spawning neutral tech buildings, found {spawners} — a new one has been " +
				"added or an old one changed shape; check it opts into the permanence rule.");
		}

		/// <summary>
		/// Repair must keep working. The change is that ONE class of destruction is unrecoverable, not
		/// that engineers stop being useful — so both the living-structure repair trait and the
		/// husk-restore path for ordinary deaths are pinned present.
		///
		/// AS OF 2026-09-11 this covers far more than it used to. "Ordinary" was engineer C4 and
		/// nothing else when this was written; it is now C4 PLUS every conventional strike power,
		/// since those no longer carry HeavyOrdnanceDeath. The traits pinned here are what the whole
		/// conventional tier now depends on, so a failure is a bigger deal than it once was.
		/// </summary>
		[Test]
		public void ConventionalDestructionIsStillRecoverable()
		{
			var neutral = RuleNodes("ingame", "structures-neutral.yaml");
			foreach (var (name, actor) in neutral)
				Assert.That(actor.Nodes.Any(n => n.Key == "EngineerRepairable"), Is.True,
					$"{name} lost EngineerRepairable — repairing a damaged tech building is unrelated to " +
					"this feature and must keep working.");

			var husks = RuleNodes("husks", "husks-neutral.yaml");
			Assert.That(husks, Is.Not.Empty, "husks-neutral.yaml parsed empty");
			foreach (var (name, husk) in husks)
				Assert.That(husk.Nodes.Any(n => n.Key == "InfiltrateForTransform"), Is.True,
					$"{name} lost InfiltrateForTransform. The wreck left by a NON-heavy death (engineer " +
					"demolition) is deliberately still restorable; only heavy ordnance leaves no wreck.");
		}

		/// <summary>
		/// The engine field the whole permanence rule rides on, exercised through the real FieldLoader
		/// rather than trusted. A BitSet that silently parsed empty would disable the feature while
		/// every YAML-shaped assertion above still passed.
		/// </summary>
		[Test]
		public void ExcludedDeathTypesParsesAndMatchesTheDamageTypeWeShip()
		{
			var yaml = MiniYaml.FromString(
				$"SpawnActorOnDeath:\n\tActor: OILB.Husk\n\tExcludedDeathTypes: {DeathType}\n", "test")
				.First().Value;

			var info = new OpenRA.Mods.Common.Traits.SpawnActorOnDeathInfo();
			FieldLoader.Load(info, yaml);

			Assert.That(info.ExcludedDeathTypes.IsEmpty, Is.False, "ExcludedDeathTypes parsed empty");
			Assert.That(info.ExcludedDeathTypes.Overlaps(new BitSet<DamageType>(DeathType)), Is.True);
			Assert.That(info.ExcludedDeathTypes.Overlaps(new BitSet<DamageType>("ExplosionDeath")), Is.False,
				"an ordinary explosion death must NOT suppress the husk — only heavy ordnance does.");

			var untouched = new OpenRA.Mods.Common.Traits.SpawnActorOnDeathInfo();
			Assert.That(untouched.ExcludedDeathTypes.IsEmpty, Is.True,
				"ExcludedDeathTypes must default to empty so every existing husk-spawner is unaffected.");
		}
	}
}
