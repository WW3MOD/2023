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
	/// THE PLAYER ACTOR CARRIES MORE THAN ONE ModularBot, so no consumer may resolve it with a
	/// single-instance lookup. `TraitDictionary.GetOrDefault` throws
	/// "Actor player has multiple traits of type ModularBot" when there are two, and the traits sit on
	/// the PLAYER actor rather than on the bot — so the throw reaches HUMAN players too, not just the
	/// AI slots a caller was thinking about.
	///
	/// <para>This is pinned rather than commented because the constraint is invisible at the call site:
	/// `player.PlayerActor.TraitOrDefault&lt;ModularBot&gt;()` reads as obviously-correct C#, compiles,
	/// passes every analyzer, and dies only in a live match with a bot present. It shipped exactly that
	/// way in Test.BotOrdersQueued and killed test-bot-defcon-wall on its first run — in both the GREEN
	/// and the RED arm, before any assertion evaluated, which is the failure mode that wastes a slot.</para>
	///
	/// <para>WHAT THIS FIXTURE IS FOR, precisely. It does NOT assert that some particular C# uses the
	/// right lookup — nothing here can construct a World to check that. It pins the PREMISE that makes
	/// the right lookup necessary: that the mod really does declare several. If someone later collapses
	/// the profiles to one ModularBot, this test fails and tells them the constraint has changed, which
	/// is the moment the `IsEnabled` selector could be simplified. Until then, any consumer must use
	/// `TraitsImplementing&lt;ModularBot&gt;()` and pick.</para>
	/// </summary>
	[TestFixture]
	public class MultiInstanceBotTraitTest
	{
		static DirectoryInfo RulesDir()
		{
			var dir = new DirectoryInfo(AppContext.BaseDirectory);
			for (var i = 0; i < 10 && dir != null; i++, dir = dir.Parent)
			{
				var candidate = new DirectoryInfo(Path.Combine(dir.FullName, "mods", "ww3mod", "rules"));
				if (candidate.Exists)
					return candidate;
			}

			throw new DirectoryNotFoundException("could not locate mods/ww3mod/rules");
		}

		/// <summary>Every `ModularBot`/`ModularBot@Suffix` node declared under a `Player:` block, as
		/// (file, key) pairs. Walks every rules file rather than ai.yaml alone, because the third
		/// instance is contributed by the campaign rules and a fixture that read one file would have
		/// under-counted and passed for the wrong reason.</summary>
		static List<(string File, string Key)> ModularBotDeclarations()
		{
			var found = new List<(string, string)>();
			var files = RulesDir().GetFiles("*.yaml", SearchOption.AllDirectories);

			Assert.That(files.Length, Is.GreaterThan(10),
				$"only {files.Length} rules files found — this fixture is scanning nothing, not passing.");

			foreach (var file in files)
			{
				foreach (var node in MiniYaml.FromFile(file.FullName))
				{
					if (node.Key != "Player")
						continue;

					foreach (var trait in node.Value.Nodes)
					{
						var key = trait.Key;

						// A removal (`-ModularBot@x:`) is not a declaration.
						if (key.StartsWith("-", StringComparison.Ordinal))
							continue;

						if (key == "ModularBot" || key.StartsWith("ModularBot@", StringComparison.Ordinal))
							found.Add((file.Name, key));
					}
				}
			}

			return found;
		}

		[Test]
		public void ThePlayerActorDeclaresMoreThanOneModularBot()
		{
			var declared = ModularBotDeclarations();

			Assert.That(declared.Count, Is.GreaterThan(1),
				"only " + declared.Count + " ModularBot declaration(s) found. If the profiles really have "
				+ "been collapsed to one, this constraint is gone and Test.BotOrdersQueued's IsEnabled "
				+ "selector can be simplified — but until then a single-instance TraitOrDefault<ModularBot> "
				+ "throws on every player. Found: "
				+ declared.Select(d => d.Key + " (" + d.File + ")").JoinWith(", "));
		}

		[Test]
		public void TheTwoBotProfilesAreBothDeclaredOnThePlayerActor()
		{
			// Named explicitly, because "more than one" would also be satisfied by two campaign bots on
			// some other actor and would then be true for a reason that has nothing to do with the seam.
			var keys = ModularBotDeclarations().Select(d => d.Key).ToList();

			Assert.That(keys, Contains.Item("ModularBot@experimental"));
			Assert.That(keys, Contains.Item("ModularBot@stable"));
		}

		[Test]
		public void EveryDeclarationIsDistinctlySuffixedSoTheyCoexist()
		{
			// An unsuffixed `ModularBot:` alongside a suffixed one would MERGE rather than add, which
			// would quietly change the instance count this fixture exists to assert.
			var keys = ModularBotDeclarations().Select(d => d.Key).ToList();

			Assert.That(keys.Distinct().Count(), Is.EqualTo(keys.Count),
				"the same ModularBot key is declared twice: " + keys.JoinWith(", "));

			Assert.That(keys.Any(k => k == "ModularBot"), Is.False,
				"an unsuffixed ModularBot: merges with nothing and cannot coexist as a second instance.");
		}
	}
}
