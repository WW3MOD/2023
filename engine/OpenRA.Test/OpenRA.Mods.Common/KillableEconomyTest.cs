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
 * AMENDED TWICE ON 2026-09-11, and the second amendment REPLACED the first rather than refining it.
 * The first drew a conventional/nuclear line -- conventional leaves a wreck, nuclear obliterates --
 * and lasted a few hours. The second, quoted below rule 2, rejected its premise: nothing obliterates
 * an economy structure at all. Both rule 2 below and the reach/permanence pairing it describes are
 * therefore HISTORY, kept only because this fixture's shape still bears their marks. No test in this
 * file asserts a tier split, and if you find one it predates that ruling.
 *
 *   2. PERMANENCE. A heavy kill must leave nothing behind. The husk (husks-neutral.yaml) inherits
 *      ^TechBuildingHusk, which carries InfiltrateForTransform against ^E6's
 *      Infiltrates@RestoreTechHusk — one engineer restores the whole structure at 10% HP. So a
 *      Warhead@TechStructure that forgot its `HeavyOrdnanceDeath` damage type would produce a
 *      weapon that DOES damage and does not DESTROY: the nuke kills the derrick, the defender walks
 *      an engineer in, and the money is back. That is precisely the defect reported, and it is
 *      invisible from the attacker's side — the building blew up.
 *
 * RULE 2 ABOVE IS HISTORY AND IS KEPT ONLY TO EXPLAIN WHAT THE FILE USED TO GUARD. It was pinned
 * together with rule 1; then a 2026-09-11 ruling split them by tier; then, the same day, the user
 * rejected the split's premise outright:
 *
 *   "Actually, I think it is a bad game mechanic that money structures can be destroyed. Maybe
 *    nothing can fully obliterate them, so nuke also only destroys them, and can do so further out
 *    than the inner fireball but not beyond 2-3x the fireball or so."
 *
 * So there is no longer any weapon, tier or partition for which "the loss is permanent" is true.
 * NoWarheadMayObliterateATechStructure enforces that from the weapon side, NoTechBuildingSuppresses-
 * ItsHusk from the actor side, and VaporizeScopeTest.TechBuildingsOptOutOfVaporisation closes the
 * fireball path, which shares no code with either. A test asserting a conventional/nuclear
 * permanence split USED to live here; it was deleted rather than adjusted, because a test encoding a
 * rejected goal reads to the next person as settled policy.
 *
 * What survives of rule 2 is the REACH cap that replaced it: the nuclear tier may destroy these
 * structures further out than the conventional tier, but not past ~3x its own fireball radius.
 * NuclearReachIsCappedAtAboutThreeFireballRadii is where that number is checked.
 *
 * Reads the shipped YAML through the real manifest merge rather than a fixture, for the reason
 * AirborneArmorTargetableTest gives: the thing being protected is the corpus, and the guard has to
 * cover weapons that do not exist yet.
 */
#endregion

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OpenRA.Mods.Common.Warheads;
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
		/// The two NUCLEAR entries on the roster. This is NOT a permanence list — nothing obliterates
		/// an economy structure any more — it is the pair whose REACH is capped against their own
		/// fireball radii by <see cref="NuclearReachIsCappedAtAboutThreeFireballRadii"/>. The
		/// conventional three are point weapons and need no cap.
		/// </summary>
		static readonly string[] NuclearRosterWeapons =
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
		/// <para>NOTHING may obliterate an economy structure — the whole of the 2026-09-11 ruling,
		/// from the weapon side. Any warhead that can reach a tech building must leave the wreck,
		/// whatever tier it belongs to, so no reaching warhead may carry <see cref="DeathType"/>.</para>
		///
		/// <para>THIS REPLACED A TEST THAT ASSERTED THE OPPOSITE FOR NUCLEAR WEAPONS, and the
		/// replacement was deliberate rather than a patch: the old one encoded a conventional/nuclear
		/// permanence split that the user rejected hours after it shipped, and a test encoding a
		/// rejected goal is read by the next person as settled policy.</para>
		///
		/// <para>It is invisible without this guard. A weapon that regains the damage type still
		/// builds, still lints, and still blows the building up on screen exactly as before; the
		/// difference appears one frame later as a husk that is not there, and then only to someone
		/// who walks an engineer over to look.</para>
		/// </summary>
		[Test]
		public void NoWarheadMayObliterateATechStructure()
		{
			var victim = TechBuildingTargetTypes();
			var weapons = Weapons();
			var checkedAny = false;

			foreach (var (name, weapon) in weapons)
			{
				foreach (var wh in ReachingWarheads(weapon, victim))
				{
					checkedAny = true;
					var damageTypes = (Field(wh.Value, "DamageTypes") ?? "")
						.Split(',').Select(s => s.Trim()).ToArray();

					Assert.That(damageTypes, Does.Not.Contain(DeathType),
						$"{name}/{wh.Key} carries {DeathType}, so its kills leave no wreck. User ruling " +
						"2026-09-11: \"I think it is a bad game mechanic that money structures can be " +
						"destroyed. Maybe nothing can fully obliterate them, so nuke also only destroys " +
						"them.\" There is no tier for which this is allowed — not the nuclear one, which " +
						"is where this token last lived. The structures no longer declare " +
						"ExcludedDeathTypes either, so re-adding it here alone would be inert; if you " +
						"mean to restore obliteration, both ends have to change and the ruling above has " +
						"to be revisited first.");
				}
			}

			Assert.That(checkedAny, Is.True, "no warhead reaches a tech structure at all — the feature is inert.");
		}

		/// <summary>
		/// <para>The REACH cap that replaced permanence. The user allowed the nuclear tier to destroy
		/// these structures "further out than the inner fireball but not beyond 2-3x the fireball or
		/// so", and both halves of that are checked here against the weapon's OWN fireball — the
		/// Radius on its Warhead@VaporizeRemoval, which is the same number as its blast wave's
		/// StartRadius by construction.</para>
		///
		/// <para>The lethal radius is computed through <see cref="SpreadDamageWarhead.DamageFalloff"/>
		/// — the shipped curve, not a restatement of it — against the toughest tech building's HP.
		/// Distance is horizontal from the hitshape edge: every tech building is a Rectangle, whose
		/// DistanceFromEdge discards Z, so an airburst costs these warheads nothing and no slant-range
		/// correction belongs here (MissileStrikeArrivalTest.RectangleHitShapesIgnoreDetonationAltitude-
		/// Entirely). A previous comment in weapons-heavy-ordnance.yaml got exactly that wrong.</para>
		/// </summary>
		[Test]
		public void NuclearReachIsCappedAtAboutThreeFireballRadii()
		{
			var weapons = Weapons();
			var superweapons = MiniYaml.FromFile(
				Path.Combine(ModDir().FullName, "rules", "weapons", "weapons-superweapons.yaml"))
				.ToDictionary(n => n.Key, n => n.Value);

			var toughest = ToughestTechBuildingHp();

			foreach (var name in NuclearRosterWeapons)
			{
				Assert.That(weapons.ContainsKey(name), Is.True, $"{name} no longer exists — update this fixture");

				var techWarhead = weapons[name].Nodes.FirstOrDefault(n => n.Key == "Warhead@TechStructure");
				Assert.That(techWarhead, Is.Not.Null, $"{name} lost its Warhead@TechStructure block");

				var damage = int.Parse(Field(techWarhead.Value, "Damage"), NumberFormatInfo.InvariantInfo);
				var spread = FieldLoader.GetValue<WDist>("Spread", Field(techWarhead.Value, "Spread"));
				var falloff = FieldLoader.GetValue<int[]>("Falloff", Field(techWarhead.Value, "Falloff"));
				var ranges = Exts.MakeArray(falloff.Length, i => i * spread);

				// The outermost distance at which this warhead still kills the toughest structure.
				var lethal = 0;
				for (var d = 0; d <= ranges[^1].Length; d++)
					if (damage * SpreadDamageWarhead.DamageFalloff(d, falloff, ranges) / 100 >= toughest)
						lethal = d;

				Assert.That(superweapons.ContainsKey(name), Is.True,
					$"{name} is not defined in weapons-superweapons.yaml, so its fireball cannot be read");

				var vaporize = superweapons[name].Nodes.FirstOrDefault(n => n.Key == "Warhead@VaporizeRemoval");
				Assert.That(vaporize, Is.Not.Null,
					$"{name} has no Warhead@VaporizeRemoval, so there is no fireball radius to cap against.");

				var fireball = FieldLoader.GetValue<WDist>("Radius", Field(vaporize.Value, "Radius")).Length;
				Assert.That(fireball, Is.GreaterThan(0), $"{name}'s fireball radius is zero");

				Assert.That(lethal, Is.GreaterThan(fireball),
					$"{name} kills a tech building only out to {lethal} wdist, which is inside its own " +
					$"{fireball} wdist fireball. The ruling says a nuke may destroy these \"further out than " +
					"the inner fireball\", so this weapon no longer reaches past the circle that used to " +
					"vaporize them — it has been capped into uselessness against the economy.");

				Assert.That(lethal, Is.LessThanOrEqualTo(3 * fireball),
					$"{name} kills a tech building out to {lethal} wdist, more than 3x its {fireball} wdist " +
					"fireball. User ruling 2026-09-11: \"not beyond 2-3x the fireball or so\". Raising Spread " +
					"or flattening Falloff on Warhead@TechStructure is what does this; the derivation for the " +
					"shipped numbers is written above each block in weapons-heavy-ordnance.yaml.");
			}
		}

		/// <summary>
		/// The toughest tech building, read from the YAML rather than hardcoded so the cap above
		/// tracks a future HP change. Actors inherit ^TechBuilding's Health and may override it.
		/// </summary>
		static int ToughestTechBuildingHp()
		{
			var structures = RuleNodes("ingame", "structures.yaml");
			var template = structures["^TechBuilding"].Nodes.FirstOrDefault(n => n.Key == "Health");
			Assert.That(template, Is.Not.Null, "^TechBuilding declares no Health");
			var fallback = int.Parse(Field(template.Value, "HP"), NumberFormatInfo.InvariantInfo);

			var toughest = 0;
			foreach (var (_, actor) in RuleNodes("ingame", "structures-neutral.yaml"))
			{
				if (!actor.Nodes.Any(n => n.Key == "SpawnActorOnDeath"))
					continue;

				var health = actor.Nodes.FirstOrDefault(n => n.Key == "Health");
				var hp = health == null ? fallback : int.Parse(Field(health.Value, "HP"), NumberFormatInfo.InvariantInfo);
				if (hp > toughest)
					toughest = hp;
			}

			Assert.That(toughest, Is.GreaterThan(0), "no tech building HP could be read");
			return toughest;
		}

		/// <summary>
		/// <para>The actor side of the same rule, and it is INVERTED from what this fixture asserted
		/// until 2026-09-11. Every tech building must spawn its husk on EVERY death, so none of them
		/// may declare ExcludedDeathTypes at all.</para>
		///
		/// <para>The lines were removed rather than emptied, and that is the point of testing for
		/// their absence: an exclusion list that is present but currently matches nothing is an
		/// armed-but-unfired mechanism, and the next reader reconnects it by adding one token to one
		/// warhead. With no list on either side, "nothing obliterates a money structure" is true by
		/// construction rather than by convention.</para>
		/// </summary>
		[Test]
		public void NoTechBuildingSuppressesItsHusk()
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

				Assert.That(Field(spawn.Value, "ExcludedDeathTypes"), Is.Null,
					$"{name} declares ExcludedDeathTypes on its SpawnActorOnDeath. User ruling 2026-09-11: " +
					"nothing may obliterate a money structure, so the wreck must appear whatever killed it. " +
					"Any exclusion here can silently delete the husk for some class of death, which is the " +
					"behaviour that ruling removed.");
			}

			Assert.That(spawners, Is.EqualTo(5),
				$"expected 5 husk-spawning neutral tech buildings, found {spawners} — a new one has been " +
				"added or an old one changed shape; check it still leaves a restorable wreck.");
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
		/// <para>The engine default is what the whole rule now rides on, so it is exercised through
		/// the real FieldLoader rather than trusted. Since 2026-09-11 NO actor in the mod sets
		/// ExcludedDeathTypes; the tech buildings leave their wreck because the field is absent and
		/// therefore empty, which means a non-empty DEFAULT would silently suppress husks across the
		/// whole mod with nothing in any YAML file to point at.</para>
		///
		/// <para>The field itself is kept in the engine as upstream-general functionality with no
		/// current consumer — see the Desc on SpawnActorOnDeathInfo. This test deliberately no longer
		/// asserts anything about which damage types the mod ships, because the mod ships none.</para>
		/// </summary>
		[Test]
		public void ExcludedDeathTypesDefaultsToEmptySoEveryHuskSpawns()
		{
			var untouched = new OpenRA.Mods.Common.Traits.SpawnActorOnDeathInfo();
			Assert.That(untouched.ExcludedDeathTypes.IsEmpty, Is.True,
				"ExcludedDeathTypes no longer defaults to empty. Every husk-spawner in the mod relies on " +
				"that default — none of them sets the field — so a non-empty default would delete wrecks " +
				"mod-wide with nothing in any YAML file to explain it.");

			// The field must still WORK, so that the choice not to use it stays a choice.
			var yaml = MiniYaml.FromString(
				$"SpawnActorOnDeath:\n\tActor: OILB.Husk\n\tExcludedDeathTypes: {DeathType}\n", "test")
				.First().Value;

			var info = new OpenRA.Mods.Common.Traits.SpawnActorOnDeathInfo();
			FieldLoader.Load(info, yaml);

			Assert.That(info.ExcludedDeathTypes.Overlaps(new BitSet<DamageType>(DeathType)), Is.True,
				"ExcludedDeathTypes no longer parses. Nothing in the mod sets it today, but a silently " +
				"broken field would make a future re-introduction fail open rather than loudly.");
			Assert.That(info.ExcludedDeathTypes.Overlaps(new BitSet<DamageType>("ExplosionDeath")), Is.False,
				"a damage type that was not listed must not match.");
		}
	}
}
