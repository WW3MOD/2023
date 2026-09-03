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
	/// <para>Guards the two prose/data contradictions found in the tooltip audit, at the level each
	/// one actually lives at.</para>
	///
	/// <para>The pool-heading fallback itself is exercised through the shipped rules rather than by
	/// calling the private formatter: what matters is that the three pools which really do reach it
	/// keep reaching it, since the branch was previously documented as unreachable.</para>
	/// </summary>
	[TestFixture]
	public class PoolLabelFallbackTest
	{
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

		static MiniYamlNode Node(string name, string file)
		{
			var n = MiniYaml.FromFile(FindRules("ingame", file)).FirstOrDefault(x => x.Key == name);
			Assert.That(n, Is.Not.Null, $"{name} is gone from {file}.");
			return n;
		}

		[Test]
		public void TheMinelayerPoolStillHasNoArmamentToNameItself()
		{
			// The premise of the fallback fix. mines-ammo declares Armaments: None and the actor
			// carries no Armament at all — the mines are spent by Minelayer. If that ever changes the
			// heading comes from a weapon name instead and the suffix strip stops being reachable.
			var mnly = Node("MNLY", "vehicles.yaml");

			Assert.That(mnly.Value.Nodes.Any(n => n.Key == "Armament" || n.Key.StartsWith("Armament@", StringComparison.Ordinal)),
				Is.False, "MNLY now declares an Armament. The mines pool would bind to it and the " +
				"tooltip heading would come from a weapon name.");

			var pool = mnly.Value.Nodes.FirstOrDefault(n => n.Key == "AmmoPool");
			Assert.That(pool, Is.Not.Null, "MNLY no longer declares its mines pool.");

			var name = pool.Value.Nodes.FirstOrDefault(n => n.Key == "Name")?.Value.Value.Trim();
			Assert.That(name, Does.EndWith("-ammo"),
				"The heading is built by stripping the '-ammo' suffix off this name. A pool named " +
				"without it renders whatever the name says, verbatim.");
		}

		[Test]
		public void TheSniperMakesNoNumericDetectionClaim()
		{
			// It said "Hard to detect (-2 detection)". The sign was backwards: Detectable.Vision is
			// "what level of vision is required to detect this actor" (Detectable.cs:24), the sniper
			// sets 5 and every other soldier sets 3 — so it is TWO HARDER, not two lower. The -2 in
			// the ruleset belongs to DetectableAddativeModifier@Firing, which applies to all infantry.
			// The qualitative claim is true and is what the description now carries.
			var sn = Node("^SN", "infantry.yaml");
			var desc = sn.Value.Nodes.FirstOrDefault(n => n.Key == "Buildable")
				?.Value.Nodes.FirstOrDefault(n => n.Key == "Description")?.Value.Value ?? "";

			Assert.That(desc, Does.Not.Contain("-2"),
				"The sniper's description states a detection number again. There is no generated " +
				"detection row to check it against, so a hand-authored one cannot be kept honest.");
			Assert.That(desc, Does.Contain("Hard to detect"),
				"The true half of the claim should survive — the sniper really is the stealthiest " +
				"soldier on the roster.");
		}

		[Test]
		public void TheSniperIsStillTheOneSoldierThatIsHarderToSee()
		{
			// Backs the surviving prose. If every soldier ends up at the same Vision, "hard to
			// detect" stops distinguishing the sniper and the sentence should go too.
			int Vision(string node)
			{
				var d = Node(node, "infantry.yaml").Value.Nodes.FirstOrDefault(n => n.Key == "Detectable");
				var v = d?.Value.Nodes.FirstOrDefault(n => n.Key == "Vision")?.Value.Value.Trim();
				return v != null && int.TryParse(v, out var i) ? i : -1;
			}

			Assert.That(Vision("^SN"), Is.GreaterThan(Vision("^E3")),
				"The sniper must need more vision to spot than a rifleman does, or 'hard to detect' " +
				"is not a fact about the sniper.");
		}
	}
}
