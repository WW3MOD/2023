#region Copyright & License Information
/*
 * THE EXCHANGE VARIANTS — the two national game-enders as they detonate during the FINAL EXCHANGE,
 * pinned as a selection rule and as YAML reads. No World, no Actor, no game run.
 *
 * WHAT THIS FIXTURE IS FOR. The variants are unreachable by any player action: no power lists them,
 * no cameo draws them, no hotkey slot counts them, and the only route in is
 * MissileStrikePowerInfo.EscalationMissileActor. That makes every one of their properties invisible
 * to play-testing until the one moment they are used, which is the end of a match. A regression here
 * does not present as a bug report; it presents as the ending looking slightly wrong to one person
 * once, or as the ordinary Sarmat quietly losing its fire rings and nobody connecting the two.
 *
 * THE THREE WAYS THIS ROTS, in descending order of how quiet they are:
 *
 * 1. THE SELECTOR STOPS FALLING BACK. Every OTHER power in the mod leaves EscalationMissileActor
 *    null. If MissileActorFor ever reached for the variant on a power that has none, the failure
 *    lands on the tactical nukes and the conventional strikes rather than on the thing being edited.
 * 2. AN EDIT TO A BASE WEAPON DOES NOT REACH ITS VARIANT, or reaches it and should not have. The
 *    variants inherit, so a new warhead on NukeSarmatRV lands on NukeSarmatRVExchange too — which
 *    is right for a scar band and wrong for a new suppression ring, and nothing says which.
 * 3. THE VARIANTS GROW NUMBERS OF THEIR OWN. They declare no radius, curve, delay or falloff today;
 *    every one is the base's and is checked where the base is (NuclearYieldTest.AllNukes, which
 *    reads RAW nodes and would fail on a weapon that restates nothing). The moment a variant states
 *    a physical quantity it is a weapon no roster checks.
 */
#endregion

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class ExchangeVariantTest
	{
		static readonly (string Base, string Variant)[] WeaponPairs =
		{
			("NukeSarmatRV", "NukeSarmatRVExchange"),
			("NukeW88", "NukeW88Exchange"),
		};

		static readonly (string Base, string Variant)[] ActorPairs =
		{
			("SarmatMissile", "SarmatMissileExchange"),
			("TridentMissile", "TridentMissileExchange"),
		};

		// The aftermath set, and the definition is BEHAVIOURAL rather than by warhead name: every one
		// of these inflicts a STATE ON A SURVIVOR — burn it, disable it, pin it down — and the final
		// exchange has no survivors and no next minute to spend in one.
		//
		// Warhead@TreeBurn is deliberately NOT here even though its [Desc] files it with the thermal
		// pulse. It grants `scorched`, which is permanent, cosmetic, and not a state anything is IN;
		// it belongs with the scars, and it is asserted PRESENT further down.
		static readonly string[] AftermathWarheads =
		{
			"Warhead@ThermalRadiation",
			"Warhead@Fire1", "Warhead@Fire2", "Warhead@Fire3", "Warhead@Fire4", "Warhead@Fire5",
			"Warhead@Fire6", "Warhead@Fire7", "Warhead@Fire8", "Warhead@Fire9", "Warhead@Fire10",
			"Warhead@EMP",
			"Warhead@Suppression1", "Warhead@Suppression2", "Warhead@Suppression3",
			"Warhead@Suppression4", "Warhead@Suppression5",
		};

		// The visible detonation, which is the half that must survive intact. The ending has to LOOK
		// like the ending: these are not a cheap version of the weapon. Shake4 and Shake5 are left
		// off because the stack is yield-dependent — the W88 carries four stages and the Sarmat five
		// — and this list is asserted of both.
		static readonly string[] VisibleWarheads =
		{
			"Warhead@Flash1", "Warhead@Shake1", "Warhead@Shake2", "Warhead@Shake3",
			"Warhead@FireballLight", "Warhead@Fireball",
			"Warhead@VaporizeRemoval", "Warhead@Vaporize",
			"Warhead@TreeBurn", "Warhead@BlastWave",
			"Warhead@Scar1Core", "Warhead@Scar2Crater", "Warhead@Scar3Char",
			"Warhead@Scar4Burn", "Warhead@Scar5Rim",
		};

		const string RefreshInterval = "Light.TerrainRefreshInterval";
		const string ExchangeRefreshInterval = "16";

		// ---- file plumbing -------------------------------------------------------------------

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

		static string ArsenalWeaponsFile()
		{
			return Path.Combine(ModDir().FullName, "rules", "weapons", "weapons-nuclear-arsenal.yaml");
		}

		/// <summary>The rules files in mod.yaml's own order. Inheritance resolution is order-sensitive,
		/// so the fixture must see the order the game does rather than a hardcoded list.</summary>
		static List<string> RuleFiles()
		{
			var mod = MiniYaml.FromFile(Path.Combine(ModDir().FullName, "mod.yaml"));
			var rules = mod.FirstOrDefault(n => n.Key == "Rules");
			Assert.That(rules, Is.Not.Null, "mod.yaml has no Rules: section — this fixture is reading nothing.");

			var files = rules.Value.Nodes
				.Select(n => n.Key.Split('|').Last().Trim())
				.Select(rel => Path.Combine(ModDir().FullName, rel.Replace('/', Path.DirectorySeparatorChar)))
				.ToList();

			Assert.That(files.Count, Is.GreaterThan(20),
				$"only {files.Count} rules files listed in mod.yaml — this fixture is scanning nothing.");

			return files;
		}

		/// <summary>The arsenal weapons with `Inherits:` RESOLVED. Reading the template is not enough
		/// here: whether a `-Warhead@...:` removal actually bites, and whether a child node written
		/// with no value keeps its parent's trait type, are properties of MiniYaml's resolver rather
		/// than of anything visible in the file.</summary>
		static Dictionary<string, MiniYaml> ResolvedWeapons()
		{
			var nodes = (IReadOnlyCollection<MiniYamlNode>)MiniYaml.FromFile(ArsenalWeaponsFile());
			return MiniYaml.Merge(new[] { nodes }).ToDictionary(n => n.Key, n => n.Value);
		}

		static Dictionary<string, MiniYaml> ResolvedActors()
		{
			return MiniYaml.Merge(RuleFiles()
					.Select(f => (IReadOnlyCollection<MiniYamlNode>)MiniYaml.FromFile(f)))
				.ToDictionary(n => n.Key, n => n.Value);
		}

		static MiniYaml Node(Dictionary<string, MiniYaml> tree, string key)
		{
			Assert.That(tree.ContainsKey(key), Is.True, $"{key} is not defined — this fixture is checking nothing.");
			return tree[key];
		}

		/// <summary>Every leaf of a node as `A.B.C` to its value, so two nodes can be diffed wholesale
		/// rather than field by field. A field-by-field comparison only pins the fields someone
		/// thought to list, which is exactly the check that misses the field added later.</summary>
		static Dictionary<string, string> Flatten(MiniYaml node, string prefix = "")
		{
			var ret = new Dictionary<string, string>();
			foreach (var child in node.Nodes)
			{
				var key = prefix + child.Key;
				ret[key] = child.Value.Value?.Trim() ?? "";
				foreach (var kv in Flatten(child.Value, key + "."))
					ret[kv.Key] = kv.Value;
			}

			return ret;
		}

		static List<string> DiffKeys(Dictionary<string, string> a, Dictionary<string, string> b)
		{
			return a.Keys.Union(b.Keys)
				.Where(k => (a.TryGetValue(k, out var av) ? av : null) != (b.TryGetValue(k, out var bv) ? bv : null))
				.OrderBy(k => k, StringComparer.Ordinal)
				.ToList();
		}

		static MissileStrikePowerInfo PowerInfo(string missileActor, string escalationActor)
		{
			var nodes = new List<MiniYamlNode> { new("MissileActor", missileActor) };
			if (escalationActor != null)
				nodes.Add(new MiniYamlNode("EscalationMissileActor", escalationActor));

			var info = new MissileStrikePowerInfo();
			FieldLoader.Load(info, new MiniYaml(null, nodes));
			return info;
		}

		// ---- (1) the selection rule ----------------------------------------------------------

		[Test]
		public void ACascadeLaunchFliesTheVariantAndEverythingElseFliesTheOrdinaryBody()
		{
			var info = PowerInfo("sarmatmissile", "sarmatmissileexchange");

			Assert.That(MissileStrikePower.MissileActorFor(info, true), Is.EqualTo("sarmatmissileexchange"),
				"a launch the final-exchange cascade has slotted must fly the exchange variant");

			// THE SECOND CASE IS THE ONE THE FEATURE EXISTS FOR, and it covers every non-cascade
			// launch there is in a single assertion because the function has no mode, player or yield
			// to vary. A game-ender bought at the top rung and fired before the window opens in
			// Escalation, and one granted by Skirmish's `Nuclear Unlock = No wait` with no exchange
			// anywhere in the match, are both exactly this call — which is the whole reason the
			// selector could not be a YAML swap keyed on the game mode.
			Assert.That(MissileStrikePower.MissileActorFor(info, false), Is.EqualTo("sarmatmissile"),
				"a game-ender fired outside the cascade is an ORDINARY strike, in every mode, and " +
				"must fly the ordinary body with its full warhead set");
		}

		[Test]
		public void AnUnsetEscalationActorFliesTheOrdinaryBodyEvenOnACascadeLaunch()
		{
			// Every power in the mod but the two national game-enders is this case, so this is the
			// byte-identity guarantee rather than an edge case: an exchange running somewhere in the
			// world may not change what a tactical nuke or a conventional strike does.
			var info = PowerInfo("b61lowmissile", null);

			Assert.That(info.EscalationMissileActor, Is.Null,
				"EscalationMissileActor must default to null — a non-null default would silently " +
				"re-point every power in the mod");
			Assert.That(MissileStrikePower.MissileActorFor(info, false), Is.EqualTo("b61lowmissile"));
			Assert.That(MissileStrikePower.MissileActorFor(info, true), Is.EqualTo("b61lowmissile"),
				"a power with no variant must fall back to its ordinary body even inside an exchange");
		}

		[Test]
		public void TheTwoGameEnderPowersNameTheirVariants()
		{
			var player = Node(ResolvedActors(), "Player");
			var expected = new (string Power, string Missile, string Variant)[]
			{
				("MissileStrikePower@Sarmat", "sarmatmissile", "sarmatmissileexchange"),
				("MissileStrikePower@TridentW88", "tridentmissile", "tridentmissileexchange"),
			};

			foreach (var (power, missile, variant) in expected)
			{
				var node = player.Nodes.FirstOrDefault(n => n.Key == power);
				Assert.That(node, Is.Not.Null, $"{power} is gone — this fixture is checking nothing.");

				var fields = Flatten(node.Value);
				Assert.That(fields.TryGetValue("MissileActor", out var m) ? m : null, Is.EqualTo(missile));
				Assert.That(fields.TryGetValue("EscalationMissileActor", out var e) ? e : null, Is.EqualTo(variant),
					$"{power} no longer names its exchange variant, so the final exchange would fire " +
					"the ordinary warhead with its full aftermath set and nothing would say so");
			}
		}

		// ---- (2) the payload -----------------------------------------------------------------

		[Test]
		public void TheVariantsCarryNoThermalFireEmpOrSuppressionWarhead()
		{
			var weapons = ResolvedWeapons();
			foreach (var (baseName, variantName) in WeaponPairs)
			{
				var baseKeys = Node(weapons, baseName).Nodes.Select(n => n.Key).ToList();
				var variantKeys = Node(weapons, variantName).Nodes.Select(n => n.Key).ToList();

				foreach (var warhead in AftermathWarheads)
				{
					Assert.That(baseKeys, Contains.Item(warhead),
						$"{baseName} no longer carries {warhead} — the variant's removal of it is now " +
						"a `-Key:` with nothing to bite, which throws at rules load");
					Assert.That(variantKeys, Does.Not.Contain(warhead),
						$"{variantName} still carries {warhead}: a state inflicted on a survivor, in " +
						"the one event that leaves none");
				}

				// AND NOTHING ELSE LEFT. Stated as a set difference rather than as a count so a
				// failure names the warhead rather than an integer.
				Assert.That(baseKeys.Except(variantKeys).OrderBy(k => k, StringComparer.Ordinal).ToList(),
					Is.EqualTo(AftermathWarheads.OrderBy(k => k, StringComparer.Ordinal).ToList()),
					$"{variantName} differs from {baseName} by warheads beyond the aftermath set");
				Assert.That(variantKeys.Except(baseKeys).ToList(), Is.Empty,
					$"{variantName} has grown a warhead of its own — every number it states is a " +
					"number NuclearYieldTest.AllNukes does not check, because that list reads raw nodes");
			}
		}

		[Test]
		public void TheVariantsKeepTheVisibleDetonationIntact()
		{
			var weapons = ResolvedWeapons();
			foreach (var (baseName, variantName) in WeaponPairs)
			{
				var baseNode = Node(weapons, baseName);
				var variantNode = Node(weapons, variantName);

				foreach (var warhead in VisibleWarheads)
				{
					var b = baseNode.Nodes.FirstOrDefault(n => n.Key == warhead);
					var v = variantNode.Nodes.FirstOrDefault(n => n.Key == warhead);
					Assert.That(b, Is.Not.Null, $"{baseName} has no {warhead} — this fixture is checking nothing.");
					Assert.That(v, Is.Not.Null,
						$"{variantName} has lost {warhead}. The ending has to LOOK like the ending; " +
						"the variants are the same detonation with the aftermath removed, not a cheap one");

					// The TRAIT TYPE survives the merge. A child node written `Warhead@X:` with no
					// value keeps its parent's (MiniYaml.cs:538, `overrideNodes.Value ?? existing`) —
					// a property of the resolver, not something the file shows.
					Assert.That(v.Value.Value, Is.EqualTo(b.Value.Value),
						$"{variantName}'s {warhead} resolved to a different warhead TYPE than {baseName}'s");

					// FireballLight is the one node that legitimately differs, and only in the one
					// field; it has its own test below. Everything else must be identical.
					if (warhead == "Warhead@FireballLight")
						continue;

					Assert.That(DiffKeys(Flatten(v.Value), Flatten(b.Value)), Is.Empty,
						$"{variantName}'s {warhead} differs from {baseName}'s. Vaporize radius, blast " +
						"wave, scars and the scorched-tree pulse are all unchanged by design");
				}
			}
		}

		[Test]
		public void TheVariantsChangeTheLightsCadenceAndNothingElseAboutIt()
		{
			var weapons = ResolvedWeapons();
			foreach (var (baseName, variantName) in WeaponPairs)
			{
				var b = Flatten(Node(weapons, baseName).Nodes.First(n => n.Key == "Warhead@FireballLight").Value);
				var v = Flatten(Node(weapons, variantName).Nodes.First(n => n.Key == "Warhead@FireballLight").Value);

				Assert.That(v[RefreshInterval], Is.EqualTo(ExchangeRefreshInterval),
					$"{variantName}'s relight cadence is not {ExchangeRefreshInterval} — the Tsar " +
					"Bomba's shipped value, and the one line of this feature that is about cost");
				Assert.That(b[RefreshInterval], Is.Not.EqualTo(v[RefreshInterval]),
					$"{baseName} now refreshes at the same interval as its variant, so the variant " +
					"buys nothing and the A/B on demo-nuke-perf would read as a fix of zero");

				// RADII, INTENSITIES, TIMES, TINTS AND DURATION ARE THE BASE'S, and it matters that
				// this is a whole-node diff: the light is what the player SEES of a 750 kt fireball,
				// and the variants are not allowed to make the ending dimmer or smaller to go faster.
				Assert.That(DiffKeys(v, b), Is.EqualTo(new List<string> { RefreshInterval }),
					$"{variantName}'s fireball light differs from {baseName}'s in more than its " +
					"refresh interval");
			}
		}

		[Test]
		public void TheVariantBodiesDifferFromTheirBaseInTheirPayloadAlone()
		{
			var actors = ResolvedActors();
			foreach (var (baseName, variantName) in ActorPairs)
			{
				// LOAD-BEARING, NOT TIDINESS. MissileStrikePower reads BallisticMissileInfo off the
				// RESOLVED actor to compute the flight time, so a variant that quietly flew faster
				// would arrive on a different tick from its base — and demo-nuke-perf's two arms
				// would stop sharing the schedule that makes them subtractable.
				var differing = DiffKeys(Flatten(Node(actors, baseName)), Flatten(Node(actors, variantName)));
				Assert.That(differing, Is.EqualTo(new List<string> { "Explodes.Weapon" }),
					$"{variantName} differs from {baseName} in more than its payload: " +
					$"{string.Join(", ", differing)}");
			}
		}

		[Test]
		public void TheVariantsAreNeverPurchasableOrOrderable()
		{
			var actors = ResolvedActors();
			foreach (var (_, variantName) in ActorPairs)
			{
				var fields = Flatten(Node(actors, variantName));

				Assert.That(fields.Keys.Any(k => k.StartsWith("Buildable", StringComparison.Ordinal)), Is.False,
					$"{variantName} has become buildable. These bodies are reachable only through " +
					"EscalationMissileActor and must never appear in a queue.");
				Assert.That(fields.TryGetValue("Valued.Cost", out var cost) ? cost : null, Is.EqualTo("0"),
					$"{variantName} has a price, which means something is meant to buy it");
			}

			// NO SECOND POWER, NO SECOND CAMEO, NO EXTRA HOTKEY SLOT. The variants are a payload swap
			// on two existing powers; a MissileStrikePower firing one as its ORDINARY missile would
			// make the variant what a player buys and leave the full-aftermath weapon unreachable.
			var player = Node(actors, "Player");
			foreach (var power in player.Nodes.Where(n => n.Key.StartsWith("MissileStrikePower@", StringComparison.Ordinal)))
			{
				var missile = power.Value.Nodes.FirstOrDefault(n => n.Key == "MissileActor")?.Value.Value?.Trim();
				var isVariant = ActorPairs.Any(p => string.Equals(p.Variant, missile, StringComparison.OrdinalIgnoreCase));
				Assert.That(isVariant, Is.False,
					$"{power.Key} fires an exchange body as its ordinary missile");
			}
		}

		// ---- (3) the four rosters the variants deliberately do not join ----------------------

		[Test]
		public void TheVariantsStateNoPhysicsOfTheirOwnAndSoJoinNoRoster()
		{
			// A new nuclear warhead normally moves four hand-maintained lists: NuclearYieldTest's
			// AllNukes, BurntTreeScopeTest's TreeBurn count, ScarUnderActorsTest's per-file smudge
			// count, and the two generators' YIELDS dicts. The variants move NONE of them, and the
			// reason is mechanical rather than a judgement: all four scan RAW nodes, or write the
			// block they own into the weapon they name, and the variants declare nothing but
			// removals and one cadence knob.
			//
			// PINNING IT HERE IS WHAT KEEPS THAT TRUE. The moment a variant states a radius, a
			// falloff or a duration of its own it is a weapon no roster checks — which is exactly
			// how the first eight arsenal weapons shipped broken (see AllNukes' own comment).
			var raw = MiniYaml.FromFile(ArsenalWeaponsFile()).ToDictionary(n => n.Key, n => n.Value);

			foreach (var (_, variantName) in WeaponPairs)
			{
				Assert.That(raw.ContainsKey(variantName), Is.True, $"{variantName} is gone from the arsenal file.");

				var declared = raw[variantName].Nodes.Select(n => n.Key).ToList();
				var kept = declared.Where(k => !k.StartsWith("-", StringComparison.Ordinal))
					.OrderBy(k => k, StringComparer.Ordinal).ToList();

				Assert.That(kept, Is.EqualTo(new List<string> { "Inherits", "Warhead@FireballLight" }),
					$"{variantName} declares more than its inheritance and its relight cadence: " +
					$"{string.Join(", ", kept)}");

				var light = raw[variantName].Nodes.First(n => n.Key == "Warhead@FireballLight");
				Assert.That(Flatten(light.Value).Keys.OrderBy(k => k, StringComparer.Ordinal).ToList(),
					Is.EqualTo(new List<string> { "Light", RefreshInterval }),
					$"{variantName} restates a field of the generated light block. " +
					"gen_fireball_light.py writes that block into the weapon it NAMES, so a second " +
					"copy here would have to be regenerated in step by hand and silently would not be");

				Assert.That(declared.Any(k => k.StartsWith("Warhead@TreeBurn", StringComparison.Ordinal)), Is.False,
					$"{variantName} declares its own TreeBurn — BurntTreeScopeTest counts RAW nodes, " +
					"so its hand-maintained total has just gone stale");
				Assert.That(declared.Any(k => k.StartsWith("Warhead@Scar", StringComparison.Ordinal)), Is.False,
					$"{variantName} declares its own scar bands — ScarUnderActorsTest counts RAW " +
					"nodes, so its per-file total has just gone stale");
			}
		}
	}
}
