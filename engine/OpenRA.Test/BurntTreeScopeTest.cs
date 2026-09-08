#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace OpenRA.Test
{
	/// <summary>
	/// A nuke leaves the trees inside its thermal radius permanently burnt (2026-09-08). The tree is
	/// NOT killed, replaced, damaged or removed — it swaps to frame 1 of its own shp and takes a
	/// colour wash, both on a condition. Everything simulation-side is untouched, which is the entire
	/// reason the feature is affordable at all: vaporising trees was costed and rejected because
	/// removing an actor invalidates the map-load ShadowLayer bake, and a Tsar Bomba crater touches
	/// ~90% of that bake in one tick (WORKSPACE/reports/vaporize-scope-260908.md).
	///
	/// Three ways this can rot, none of which any screenshot would catch:
	///
	/// 1. THE SEQUENCE SILENTLY FALLS BACK. `Sequence: burnt` is resolved by name out of the tree's
	///    own image, and image names are lowercased from the actor name (RenderSprites.cs:115) while
	///    MiniYaml top-level keys merge CASE-SENSITIVELY. A `burnt` written under `T03:` instead of
	///    `t03:` defines a sequence on an image nothing renders; the failure surfaces as an
	///    exception on the first nuke, not at load, and only for the species that got it wrong.
	///    Hence: every one of the 22 species is checked by name, and by the frame it starts at.
	///
	/// 2. THE LIVING BODY STOPS BEING GATED OFF. Two WithSpriteBody traits on one actor with
	///    complementary conditions is the whole swap. Drop `RequiresCondition: !scorched` from the
	///    first and both draw — a green tree and a black one in the same cell — which reads as a
	///    rendering glitch rather than as a missing gate.
	///
	/// 3. SOMEONE MAKES IT REAL. The obvious "improvement" is to let the tree die properly. That
	///    reverses the 2026-09-02 indestructibility ruling AND reintroduces the shadow-bake cost,
	///    and it is one `Vaporizable:` or one `Modifier: 100` away. Both are pinned below.
	///
	/// Nothing here launches anything: it asks what the shipped YAML SAYS, and — for the trait merge,
	/// which no amount of reading a template proves — what it RESOLVES TO.
	/// </summary>
	[TestFixture]
	public class BurntTreeScopeTest
	{
		const string Marker = "^TreeIndestructible";
		const string Condition = "scorched";
		const string Sequence = "burnt";

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

		/// <summary>The rules files, in the order mod.yaml loads them. Read rather than hardcoded:
		/// inheritance resolution is order-sensitive, so a reordering is a real change and this
		/// fixture should see the same order the game does.</summary>
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

			foreach (var f in files)
				Assert.That(File.Exists(f), Is.True, $"mod.yaml lists {f}, which does not exist.");

			return files;
		}

		static List<MiniYamlNode> Sequences()
		{
			return MiniYaml.FromFile(Path.Combine(
				ModDir().FullName, "sequences", "sequences-decorations.yaml"));
		}

		static MiniYamlNode Child(MiniYamlNode node, string key)
		{
			return node.Value.Nodes.FirstOrDefault(n => n.Key == key);
		}

		static string Value(MiniYamlNode node, string key)
		{
			return Child(node, key)?.Value.Value?.Trim();
		}

		/// <summary>The 22 tree species, derived the same way TreeIndestructibleScopeTest derives them
		/// (inherits ^Tree and spawns a husk) rather than hardcoded, so the two fixtures cannot
		/// disagree about what a tree is.</summary>
		static List<string> TreeActors()
		{
			var all = new List<MiniYamlNode>();
			foreach (var f in RuleFiles())
				all.AddRange(MiniYaml.FromFile(f));

			var trees = all
				.Where(n => n.Value.Nodes.Any(c =>
					(c.Key == "Inherits" || c.Key.StartsWith("Inherits@", StringComparison.Ordinal))
					&& c.Value.Value?.Trim() == Marker))
				.Select(n => n.Key)
				.Distinct()
				.OrderBy(n => n, StringComparer.Ordinal)
				.ToList();

			Assert.That(trees.Count, Is.EqualTo(22),
				$"expected 22 actors inheriting {Marker} (T01-T17, TC01-TC05); found {trees.Count}: " +
				$"{string.Join(", ", trees)}. If a tree species was added, it needs a `{Sequence}` " +
				"sequence too — that is what the next test checks, and it is why this count is pinned.");

			return trees;
		}

		[Test]
		public void EverySpeciesHasABurntSequenceAtTheHuskFrame()
		{
			var seq = Sequences();

			foreach (var actor in TreeActors())
			{
				// RenderSprites resolves a living T01's image as `t01` (RenderSprites.cs:115). No tree
				// carries an Image: override, so the actor name lowercased IS the sequence node key.
				var image = actor.ToLowerInvariant();

				var node = seq.FirstOrDefault(n => n.Key == image);
				Assert.That(node, Is.Not.Null,
					$"no sequence node `{image}` for actor {actor}. Note the case: a node written " +
					$"`{actor}:` would NOT serve this actor — MiniYaml merges top-level keys " +
					"case-sensitively and the lowercasing happens afterwards, in RenderSprites.");

				var burnt = Child(node, Sequence);
				Assert.That(burnt, Is.Not.Null,
					$"sequence node `{image}` has no `{Sequence}`, so WithSpriteBody@Scorched on {actor} " +
					"names a sequence that does not exist. This throws on the first nuke that reaches " +
					"this species — not at load, and not for any other species.");

				Assert.That(Value(burnt, "Start"), Is.EqualTo("1"),
					$"`{image}.{Sequence}` does not start at frame 1. Frame 0 is the LIVING tree, so a " +
					"burnt tree would be pixel-identical to an untouched one and the whole feature " +
					"would look like it silently did nothing — which is exactly the failure this " +
					"assertion exists to tell apart from 'the swap never fired'.");
			}
		}

		[Test]
		public void TheBurntSequenceIsTheSameFrameTheHuskActorAlreadyUses()
		{
			// The burnt frame is not new art: it is the frame the dormant T##.Husk actors have always
			// drawn. If someone re-points one of the two, they must re-point both, or a burnt tree and
			// the husk of that same tree stop matching.
			var seq = Sequences();

			foreach (var actor in TreeActors())
			{
				var image = actor.ToLowerInvariant();
				var husk = seq.FirstOrDefault(n => n.Key == image + ".husk");
				Assert.That(husk, Is.Not.Null, $"no `{image}.husk` sequence node — this fixture is pinning nothing.");

				var huskIdle = Child(husk, "idle");
				var burnt = Child(seq.First(n => n.Key == image), Sequence);

				Assert.That(Value(burnt, "Start"), Is.EqualTo(Value(huskIdle, "Start")),
					$"`{image}.{Sequence}` and `{image}.husk.idle` no longer name the same frame. They are " +
					"the same thing seen two ways — a living tree that has burnt, and the husk actor that " +
					"replaces a tree that died. Keep them equal or say in the commit why they diverged.");
			}
		}

		[Test]
		public void TheMarkerCarriesTheWholeScorchMechanism()
		{
			var all = new List<MiniYamlNode>();
			foreach (var f in RuleFiles())
				all.AddRange(MiniYaml.FromFile(f));

			var marker = all.Where(n => n.Key == Marker).ToArray();
			Assert.That(marker.Length, Is.EqualTo(1), $"expected exactly one definition of {Marker}.");
			var m = marker[0];

			// The grant point. Without this the warheads find no matching ExternalCondition and
			// GrantExternalConditionWarhead silently does nothing at all — no error, no effect.
			var ext = m.Value.Nodes.FirstOrDefault(n =>
				n.Key.StartsWith("ExternalCondition", StringComparison.Ordinal)
				&& n.Value.Nodes.Any(c => c.Key == "Condition" && c.Value.Value?.Trim() == Condition));
			Assert.That(ext, Is.Not.Null,
				$"{Marker} declares no ExternalCondition granting `{Condition}`. " +
				"GrantExternalConditionWarhead.DoImpact looks for a trait whose Info.Condition matches and " +
				"does nothing when it finds none — so every nuke in the game would leave the forest green " +
				"and nothing would report a problem.");

			// The two bodies, and the gate that keeps exactly one of them drawing.
			var living = m.Value.Nodes.FirstOrDefault(n => n.Key == "WithSpriteBody");
			Assert.That(living, Is.Not.Null, $"{Marker} no longer overrides WithSpriteBody.");
			Assert.That(Value(living, "RequiresCondition"), Is.EqualTo("!" + Condition),
				$"the living body on {Marker} is not gated on !{Condition}. Both bodies would then draw " +
				"once a tree is scorched: the green sprite and the black one in the same cell, one over " +
				"the other. That reads as a rendering bug, so it will be reported as one.");

			var scorched = m.Value.Nodes.FirstOrDefault(n =>
				n.Key.StartsWith("WithSpriteBody@", StringComparison.Ordinal));
			Assert.That(scorched, Is.Not.Null, $"{Marker} has no second WithSpriteBody — nothing draws the burnt frame.");
			Assert.That(Value(scorched, "RequiresCondition"), Is.EqualTo(Condition));
			Assert.That(Value(scorched, "Sequence"), Is.EqualTo(Sequence),
				$"the scorched body does not play `{Sequence}`. If it plays `idle` the tree renders its " +
				"LIVING frame while flagged as burnt, which is indistinguishable from the condition never " +
				"having been granted.");

			// The wash. It is not decoration: on SNOW the burnt frame is luminance ~97 against scar
			// bands at 20-76, i.e. brighter than the scorched ground it stands on.
			var overlay = m.Value.Nodes.FirstOrDefault(n =>
				n.Key.StartsWith("WithColoredOverlay", StringComparison.Ordinal));
			Assert.That(overlay, Is.Not.Null,
				$"{Marker} lost its WithColoredOverlay. The frame swap alone is correct on TEMPERAT and " +
				"WRONG on SNOW, where the burnt frame is a flat ~97 luminance while the snow scar bands " +
				"are 20-76 — a pale tree on dark scorched ground, which is the complaint this feature was " +
				"built to fix, reproduced on three of the ten shipped maps. Re-measure snow before " +
				"removing this.");
			Assert.That(Value(overlay, "RequiresCondition"), Is.EqualTo(Condition));

			// And the ruling this must not reverse.
			var mult = m.Value.Nodes.FirstOrDefault(n => n.Key.StartsWith("DamageMultiplier", StringComparison.Ordinal));
			Assert.That(Value(mult, "Modifier"), Is.EqualTo("0"),
				$"{Marker} no longer zeroes damage. Burning a tree in place is only sound because the tree " +
				"cannot die; a destructible tree brings back both the husk artifact the 2026-09-02 ruling " +
				"removed and the shadow-bake recompute that ruled out vaporising them.");

			Assert.That(m.Value.Nodes.Any(n => n.Key.StartsWith("Vaporizable", StringComparison.Ordinal)), Is.False,
				$"{Marker} has gained a Vaporizable. VaporizeWarhead ignores target types and REMOVES the " +
				"actor, which is precisely the outcome burning-in-place exists to avoid: the crater would " +
				"keep concealing units and blocking line of fire on ground where visibly nothing stands.");
		}

		[Test]
		public void ScorchAddsNothingThatReplacesOrRemovesTheActor()
		{
			var all = new List<MiniYamlNode>();
			foreach (var f in RuleFiles())
				all.AddRange(MiniYaml.FromFile(f));

			var marker = all.First(n => n.Key == Marker);

			// A trait that ends the actor's life defeats the point even if damage stays at zero.
			foreach (var forbidden in new[] { "SpawnActorOnDeath", "Transforms", "TransformOnCondition", "KillsSelf" })
				Assert.That(marker.Value.Nodes.Any(n => n.Key.StartsWith(forbidden, StringComparison.Ordinal)), Is.False,
					$"{Marker} declares {forbidden}. The scorch must be an OVERLAY on the living actor, never " +
					"a replacement: the tree's entry in the map-load ShadowLayer bake is what gives cover and " +
					"blocks line of fire, and it is not recomputed when an actor leaves the world.");
		}

		[Test]
		public void TheWarheadsGrantTheConditionTheTreesListenFor()
		{
			// THE ONE JOINT NO GATE CHECKS. CheckConditions walks rules.Actors only, and
			// GrantExternalConditionWarhead.Condition carries no [GrantedConditionReference] attribute
			// at all — so the string on the warhead is checked by nothing, anywhere. Misspell it and
			// the mod loads clean, every lint passes, every nuke fires normally, and the forest simply
			// stays green forever. That is the quietest way this feature can die, so it is pinned by
			// comparing the two strings directly.
			var granted = new List<MiniYamlNode>();
			foreach (var f in RuleFiles())
				granted.AddRange(MiniYaml.FromFile(f));

			var condition = granted.First(n => n.Key == Marker).Value.Nodes
				.First(n => n.Key.StartsWith("ExternalCondition", StringComparison.Ordinal))
				.Value.Nodes.First(n => n.Key == "Condition").Value.Value.Trim();

			var weaponsDir = Path.Combine(ModDir().FullName, "rules", "weapons");
			var burns = new List<(string Weapon, MiniYamlNode Warhead)>();
			foreach (var file in Directory.GetFiles(weaponsDir, "*.yaml", SearchOption.AllDirectories))
				foreach (var weapon in MiniYaml.FromFile(file))
					foreach (var wh in weapon.Value.Nodes.Where(n =>
						n.Key.StartsWith("Warhead@TreeBurn", StringComparison.Ordinal)))
						burns.Add((weapon.Key, wh));

			Assert.That(burns.Count, Is.EqualTo(14),
				$"expected 14 Warhead@TreeBurn (12 arsenal weapons plus Atomic and AtomicHighYield); " +
				$"found {burns.Count}. A nuke without one leaves its own crater full of green trees.");

			foreach (var (weapon, wh) in burns)
				Assert.That(wh.Value.Nodes.FirstOrDefault(n => n.Key == "Condition")?.Value.Value?.Trim(),
					Is.EqualTo(condition),
					$"{weapon}'s TreeBurn grants a condition that is not the one {Marker} listens for " +
					$"(`{condition}`). Nothing in the engine will tell you: the warhead's Condition field " +
					"is not a [GrantedConditionReference], CheckConditions never looks at weapons, and " +
					"GrantExternalConditionWarhead.DoImpact silently does nothing when no trait matches.");
		}

		[Test]
		public void TheTraitsSurviveInheritanceResolutionOntoARealTree()
		{
			// The one thing reading the template cannot tell you. T01 inherits ^Tree (which declares
			// WithSpriteBody) and then ^TreeIndestructible (which declares WithSpriteBody again, with the
			// gate). Whether those merge or the second is dropped is a property of MiniYaml's resolver
			// and of the ORDER of the two Inherits lines, not of either file read alone.
			var resolved = MiniYaml.Merge(RuleFiles().Select(f => (IReadOnlyCollection<MiniYamlNode>)MiniYaml.FromFile(f)));

			foreach (var actor in TreeActors())
			{
				var node = resolved.FirstOrDefault(n => n.Key == actor);
				Assert.That(node, Is.Not.Null, $"{actor} vanished from the resolved rules.");

				var bodies = node.Value.Nodes
					.Where(n => n.Key == "WithSpriteBody" || n.Key.StartsWith("WithSpriteBody@", StringComparison.Ordinal))
					.ToArray();

				Assert.That(bodies.Length, Is.EqualTo(2),
					$"{actor} resolves to {bodies.Length} WithSpriteBody trait(s), not 2 " +
					$"({string.Join(", ", bodies.Select(b => b.Key))}). ^Tree declares one and " +
					$"{Marker} declares the gated pair; if they collapsed to one, the Inherits order " +
					"changed or a key was renamed, and the tree either never burns or draws both sprites.");

				var gate = bodies.Select(b => b.Value.Nodes.FirstOrDefault(c => c.Key == "RequiresCondition")?.Value.Value?.Trim())
					.OrderBy(s => s, StringComparer.Ordinal)
					.ToArray();

				Assert.That(gate, Is.EqualTo(new[] { "!" + Condition, Condition }),
					$"{actor}'s two sprite bodies resolve to conditions [{string.Join(", ", gate)}] rather than " +
					$"exactly one gated on `{Condition}` and one on `!{Condition}`. The merge of ^Tree's " +
					$"ungated WithSpriteBody with {Marker}'s gated one did not land — this is the failure " +
					"that Inherits ORDER causes, and it cannot be seen in either file on its own.");

				Assert.That(node.Value.Nodes.Any(n =>
						n.Key.StartsWith("ExternalCondition", StringComparison.Ordinal)
						&& n.Value.Nodes.Any(c => c.Key == "Condition" && c.Value.Value?.Trim() == Condition)),
					Is.True,
					$"{actor} resolves without an ExternalCondition for `{Condition}`, so no warhead can " +
					"reach it however correct the weapon YAML is.");
			}
		}
	}
}
