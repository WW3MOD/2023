#region Copyright & License Information
/*
 * EVERY BallisticMissile ACTOR RESOLVES A SPRITE IMAGE THAT HAS SEQUENCES.
 *
 * This fixture exists because the failure it catches is a CRASH, at SPAWN, on the synced order
 * path, and every gate in the repo is green through it.
 *
 * WHAT HAPPENS. RenderSprites.GetImage is `(Image ?? actor.Name).ToLowerInvariant()`
 * (RenderSprites.cs:110-115), so an actor that does not set RenderSprites.Image resolves its
 * sprite from its OWN NAME. Add a missile body by inheriting an existing one — which is the
 * right way to add a missile body, since the flight model and the visuals should not be
 * restated — and it inherits the RenderSprites node but NOT the name, so it silently asks
 * sequences.yaml for an image nobody defined. WithFacingSpriteBodyInfo.Create then calls
 * Animation.PlayRepeating, and SequenceSet.GetSequence throws
 *
 *     InvalidOperationException: Image '<actor>' does not have any sequences defined
 *
 * the first time the actor is CREATED. For a missile that is inside
 * SupportPowerManager.ResolveOrder, at the first launch — so the match dies at the moment the
 * weapon is used, and only then.
 *
 * WHY NOTHING ELSE SEES IT. The mod loads: sequences are resolved lazily, per image, on first
 * draw. The YAML lints: `RenderSprites.Image` is a plain string with no [SequenceReference] on
 * it, so nothing cross-checks it against the sequence files. `make.ps1 smoke` constructs a World
 * per map, and no shipped map spawns a strategic missile. Every scenario that does fire one is an
 * autotest, which is minutes long and not run per commit.
 *
 * TWICE IN ONE AFTERNOON, 2026-09-20: `tridentmissile` and then `sarmatmissileexchange`, by two
 * different people, neither of whom had done anything wrong except add a missile the same way the
 * last one was added. That is the definition of a coupling that should be a test rather than a
 * thing to remember — it is the FIFTH hand-maintained list a new missile body touches.
 *
 * SCOPE, STATED SO A GREEN RUN IS NOT OVER-READ: this checks actors defined in mod.yaml's Rules
 * against images defined in mod.yaml's Sequences. An actor introduced by a MAP or an autotest
 * scenario's own rules.yaml is not seen here, and neither is a sequence file a map adds.
 */
#endregion

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace OpenRA.Test
{
	[TestFixture]
	public class BallisticMissileImageTest
	{
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

		/// <summary>The files mod.yaml lists under <paramref name="section"/>, in its own order.
		/// Read rather than hardcoded: both rules and sequences resolve `Inherits:`, which is
		/// order-sensitive, so the fixture must see the order the game does.</summary>
		static List<string> ModFiles(string section)
		{
			var mod = MiniYaml.FromFile(Path.Combine(ModDir().FullName, "mod.yaml"));
			var node = mod.FirstOrDefault(n => n.Key == section);
			Assert.That(node, Is.Not.Null, $"mod.yaml has no {section}: section — this fixture is reading nothing.");

			var files = node.Value.Nodes
				.Select(n => n.Key.Split('|').Last().Trim())
				.Select(rel => Path.Combine(ModDir().FullName, rel.Replace('/', Path.DirectorySeparatorChar)))
				.ToList();

			foreach (var f in files)
				Assert.That(File.Exists(f), Is.True, $"mod.yaml lists {f}, which does not exist.");

			return files;
		}

		static List<MiniYamlNode> Resolved(string section)
		{
			return MiniYaml.Merge(ModFiles(section)
				.Select(f => (IReadOnlyCollection<MiniYamlNode>)MiniYaml.FromFile(f))).ToList();
		}

		static MiniYamlNode Child(MiniYaml node, string key)
		{
			return node.Nodes.FirstOrDefault(n => n.Key == key);
		}

		/// <summary>The images an actor can draw itself from: its RenderSprites.Image, or its own
		/// lowercased name when that is unset, plus every FactionImages entry. Mirrors
		/// RenderSprites.GetImage (RenderSprites.cs:110-115) — including the ToLowerInvariant, which
		/// is why `Image: SarmatMissile` would work and is not something this fixture should forbid.</summary>
		static List<string> ImagesFor(MiniYamlNode actor)
		{
			var render = Child(actor.Value, "RenderSprites");
			var ret = new List<string>();

			var image = render == null ? null : Child(render.Value, "Image")?.Value.Value?.Trim();
			ret.Add((string.IsNullOrEmpty(image) ? actor.Key : image).ToLowerInvariant());

			var factionImages = render == null ? null : Child(render.Value, "FactionImages");
			if (factionImages != null)
				foreach (var n in factionImages.Value.Nodes)
					if (!string.IsNullOrEmpty(n.Value.Value))
						ret.Add(n.Value.Value.Trim().ToLowerInvariant());

			return ret;
		}

		/// <summary>Actors carrying a BallisticMissile trait, templates excluded. A `^`-prefixed node
		/// is never instantiated, so it has no name to resolve an image from and asking it the
		/// question is meaningless.</summary>
		static List<MiniYamlNode> BallisticMissileActors()
		{
			var actors = Resolved("Rules")
				.Where(n => !n.Key.StartsWith("^", StringComparison.Ordinal)
					&& n.Value.Nodes.Any(c => c.Key == "BallisticMissile"
						|| c.Key.StartsWith("BallisticMissile@", StringComparison.Ordinal)))
				.ToList();

			// A FLOOR, NOT A PINNED COUNT. The point is that this fixture is looking at the real
			// roster rather than at an empty list after a rename; the exact number is expected to
			// grow and is nobody's invariant.
			Assert.That(actors.Count, Is.GreaterThanOrEqualTo(10),
				$"only {actors.Count} BallisticMissile actors found — the trait has probably been " +
				"renamed and this fixture is now checking almost nothing.");

			return actors;
		}

		/// <summary>Image name to the number of sequences defined on it.</summary>
		static Dictionary<string, int> SequenceCounts()
		{
			var ret = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
			foreach (var node in Resolved("Sequences"))
			{
				// `Inherits` and `Defaults` are bookkeeping on the image node, not sequences a
				// SequenceSet would hand out.
				var count = node.Value.Nodes.Count(n =>
					!n.Key.StartsWith("Inherits", StringComparison.Ordinal) && n.Key != "Defaults");
				ret[node.Key] = count;
			}

			Assert.That(ret.Count, Is.GreaterThan(100),
				$"only {ret.Count} images across mod.yaml's Sequences files — this fixture is " +
				"scanning nothing.");

			return ret;
		}

		[Test]
		public void EveryBallisticMissileActorResolvesAnImageThatHasSequences()
		{
			var sequences = SequenceCounts();
			var broken = new List<string>();

			foreach (var actor in BallisticMissileActors())
			{
				foreach (var image in ImagesFor(actor))
				{
					if (!sequences.TryGetValue(image, out var count))
					{
						broken.Add($"{actor.Key} -> `{image}` (no such image)");
						continue;
					}

					if (count == 0)
						broken.Add($"{actor.Key} -> `{image}` (image exists but defines no sequences)");
				}
			}

			// ONE FAILURE LISTING EVERY OFFENDER, not one assertion per actor: the two cases that
			// produced this fixture arrived a few hours apart, and a run that names only the first
			// of them sends the next person round the loop again.
			Assert.That(broken, Is.Empty,
				"these actors would throw `Image '<name>' does not have any sequences defined` the " +
				"first time they are SPAWNED — for a missile, inside SupportPowerManager.ResolveOrder " +
				"at the first launch, killing the match at the moment the weapon is used:" +
				Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", broken) +
				Environment.NewLine +
				"Fix by pointing the body at the image it should share -- `RenderSprites: Image: " +
				"<base>` -- rather than by copying sequence entries, which makes the two identical " +
				"today and silently divergent the next time the original is retuned.");
		}

		[Test]
		public void AMissileThatInheritsAnotherBodyDrawsTheSameImageAsIt()
		{
			// The stronger claim, and the one `Image:` is set FOR. Resolving *an* image with
			// sequences is not enough: a variant that inherited its flight model and then drew a
			// different sprite would pass the test above and still be wrong, because the whole
			// argument for inheriting is that the player cannot tell the two apart in flight.
			//
			// Derived from the Inherits chain rather than from a list of pairs, so a body added
			// tomorrow is covered without an edit here.
			// The RAW nodes, because `Inherits` is consumed by the resolver and is gone from the
			// merged tree. Last definition wins, matching the merge order mod.yaml declares.
			var raw = new Dictionary<string, MiniYaml>(StringComparer.Ordinal);
			foreach (var file in ModFiles("Rules"))
				foreach (var node in MiniYaml.FromFile(file))
					raw[node.Key] = node.Value;

			var missiles = new HashSet<string>(
				BallisticMissileActors().Select(n => n.Key), StringComparer.Ordinal);
			var resolved = Resolved("Rules").ToDictionary(n => n.Key, n => n, StringComparer.Ordinal);
			var checkedAny = false;

			foreach (var name in missiles)
			{
				if (!raw.TryGetValue(name, out var own))
					continue;

				var inherits = Child(own, "Inherits")?.Value.Value;
				if (inherits == null)
					continue;

				var parents = new List<string>();
				foreach (var candidate in inherits.Split(','))
				{
					var trimmed = candidate.Trim();
					if (trimmed.Length > 0 && missiles.Contains(trimmed))
						parents.Add(trimmed);
				}

				foreach (var parent in parents)
				{
					checkedAny = true;
					var mine = ImagesFor(resolved[name]).First();
					var theirs = ImagesFor(resolved[parent]).First();
					Assert.That(mine, Is.EqualTo(theirs),
						$"{name} inherits {parent}'s flight model but draws `{mine}` where {parent} " +
						$"draws `{theirs}`. Either set `RenderSprites: Image: {theirs}` so the two " +
						"stay identical by construction, or stop inheriting -- a body that shares a " +
						"trajectory and not a sprite is two decisions pretending to be one.");
				}
			}

			Assert.That(checkedAny, Is.True,
				"no BallisticMissile actor inherits another one — either the exchange variants are " +
				"gone or they stopped inheriting, and this test is checking nothing.");
		}
	}
}
