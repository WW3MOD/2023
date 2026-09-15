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
	/// A drone operator must be able to send its drone to a contact that is actually LOST.
	///
	/// Those two words carry the whole invariant. A contact only becomes lost if it is outside the
	/// OPERATOR's own verifying radius — inside it, the operator itself makes the cell visible and
	/// BeliefStore resolves the record the moment the observer dies. So the drone's hover ceiling
	/// must REACH PAST that radius, or the lost tier is unreachable by construction: every contact
	/// the module could fly to is one that never became lost, and every contact that did become lost
	/// is one it cannot fly to.
	///
	/// It shipped that way. Until 2026-09-15 the ceiling was 22 cells against a 28-cell radius, so
	/// the closest the drone could get to any lost contact was 6 cells, IntelFalloff discounted the
	/// term by that distance before any comparison, and test-drone-lost-track measured the hunt cell
	/// scoring 178 of a possible 247 and losing at every launch in BOTH arms. Nothing failed. The
	/// feature was configured, tested, logged and inert — the same shape as the ContactBonus defect
	/// that Score_IntelIsInTheSameCurrencyAsRevealedArea exists to prevent, one level up.
	///
	/// WHY THIS READS YAML INSTEAD OF C# DEFAULTS. Every number here is overridden in mod YAML, and
	/// three of them live in three different files that nothing links together: the ceiling is
	/// min(DroneTargeter's range, LeashCells - LeashMarginCells), the ENFORCED leash it models is
	/// CarrierSlave.MaxDistance on the drone ACTOR, and the radius it must clear comes from
	/// ^StandardVision's band table. A test against the C# defaults would have passed throughout the
	/// period the shipped game was broken.
	///
	/// BOTH PROFILES ARE ASSERTED, and that is a change of status for @stable. The numbers it
	/// mirrors are actor and weapon rules with no profile-scoped form, so @stable could not have
	/// been held at the old value even if that had been wanted: holding it would have left its MODEL
	/// disagreeing with the leash the engine enforces, which is its own defect rather than a
	/// preserved baseline.
	/// </summary>
	[TestFixture]
	public class DroneLeashCoherenceTest
	{
		/// <summary>
		/// Margin the ceiling must clear the verifying radius by. One cell would satisfy the letter of
		/// the invariant and leave the only reachable lost contact sitting exactly on the rim, where
		/// IntelFalloff is at its weakest and a one-cell operator nudge erases the record.
		/// </summary>
		const int RequiredMarginCells = 2;

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

		static List<MiniYamlNode> TopLevel(params string[] relativePath)
		{
			var path = Path.Combine(new[] { RulesDir().FullName }.Concat(relativePath).ToArray());
			if (!File.Exists(path))
				throw new FileNotFoundException($"expected rules file is missing: {path}");

			return MiniYaml.FromFile(path).ToList();
		}

		static MiniYaml Node(IEnumerable<MiniYamlNode> nodes, string key)
		{
			var hit = nodes.FirstOrDefault(n => n.Key == key)?.Value;
			Assert.That(hit, Is.Not.Null, $"'{key}' is not defined where this fixture expects it — " +
				"the invariant is unverified rather than satisfied. Fix the lookup, do not delete the assert.");
			return hit;
		}

		static string Value(MiniYaml parent, string key)
		{
			return parent.Nodes.FirstOrDefault(n => n.Key == key)?.Value.Value?.Trim();
		}

		static int Int(MiniYaml parent, string key)
		{
			var raw = Value(parent, key);
			Assert.That(raw, Is.Not.Null, $"'{key}' has no value on this node.");
			Assert.That(int.TryParse(raw, out var v), Is.True, $"'{key}' is not an integer: '{raw}'.");
			return v;
		}

		/// <summary>A WDist-shaped YAML value ("33c0") in whole cells, matching the engine's own
		/// truncation: DroneOperatorBotModule does `armament.MaxRange().Length / 1024`.</summary>
		static int Cells(MiniYaml parent, string key)
		{
			var raw = Value(parent, key);
			Assert.That(raw, Is.Not.Null, $"'{key}' has no value on this node.");
			Assert.That(WDist.TryParse(raw, out var d), Is.True, $"'{key}' is not a WDist: '{raw}'.");
			return d.Length / 1024;
		}

		/// <summary>
		/// The radius at which a standard ground unit VERIFIES a cell, read from ^StandardVision's
		/// bands rather than hardcoded.
		///
		/// The threshold is not arbitrary and is the single easiest thing here to get wrong: the
		/// consumer is MapLayers.IsVisible(cell, 1), whose comparison at MapLayers.cs:579 is
		/// `ResolvedVisibility[puv] &gt; visibility` — STRICT. So a band of Strength 1 does NOT verify,
		/// and the verifying radius is the furthest Range among bands of Strength 2 or more. Reading
		/// the outermost band instead gives 32 and silently weakens this whole fixture by four cells.
		/// (There is a second, NON-strict comparison twenty lines below that one, for concealment
		/// detection. They are different questions; do not swap the readings.)
		/// </summary>
		static int VerifyingRadiusCells()
		{
			var vision = Node(TopLevel("defaults.yaml"), "^StandardVision");

			var verifying = vision.Nodes
				.Where(n => n.Key == "Vision" || n.Key.StartsWith("Vision@", StringComparison.Ordinal))
				.Where(n => Int(n.Value, "Strength") > 1)
				.Select(n => Cells(n.Value, "Range"))
				.ToList();

			Assert.That(verifying, Is.Not.Empty,
				"^StandardVision declares no band above Strength 1 — the reading is broken, not the data.");

			return verifying.Max();
		}

		static (int Ceiling, int Leash, int Margin, int Weapon) Ceiling(string profileKey)
		{
			var player = Node(TopLevel("ai", "ai.yaml"), "Player");
			var module = Node(player.Nodes, profileKey);

			var leash = Int(module, "LeashCells");
			var margin = Int(module, "LeashMarginCells");
			var weapon = Cells(Node(TopLevel("weapons", "weapons-other.yaml"), "DroneTargeter"), "Range");

			// DroneTaskingMath.MaxHoverDistanceCells, transcribed rather than referenced: this fixture
			// is about the CONFIGURED numbers agreeing, so it must not inherit a bug from the function
			// whose inputs it is checking.
			var effectiveLeash = Math.Max(0, leash - margin);
			return (Math.Min(weapon, effectiveLeash), leash, margin, weapon);
		}

		[TestCase("DroneOperatorBotModule@experimental")]
		[TestCase("DroneOperatorBotModule@stable")]
		public void HoverCeilingReachesPastTheOperatorSOwnVerifyingRadius(string profileKey)
		{
			var radius = VerifyingRadiusCells();
			var c = Ceiling(profileKey);

			Assert.That(c.Ceiling, Is.GreaterThanOrEqualTo(radius + RequiredMarginCells),
				$"{profileKey}: hover ceiling {c.Ceiling} cells does not clear the {radius}-cell verifying " +
				$"radius by {RequiredMarginCells}. A contact is only LOST outside that radius, so the drone " +
				$"can no longer be sent to one — the lost-track term is inert however it is weighted. " +
				$"Ceiling = min(DroneTargeter range {c.Weapon}, LeashCells {c.Leash} - LeashMarginCells " +
				$"{c.Margin}). Raise whichever of the two binds, AND quadcopterdrone's " +
				$"CarrierSlave.MaxDistance with it.");
		}

		[Test]
		public void TheBotSModelMatchesTheLeashTheEngineActuallyEnforces()
		{
			// LeashCells is a MIRROR of CarrierSlave.MaxDistance, not an independent setting, and the
			// two failure directions are not symmetric. Too HIGH and the bot tasks drones past the real
			// leash, where ReturnWithinDistance drags them 10% back and grants `lost-connection`, which
			// VisionModifier@OperatorLostContact turns into Modifier: 0 — a blind drone the owner still
			// believes is scouting. Too LOW and reach is silently given away. Only equality is correct.
			var drone = Node(TopLevel("ingame", "aircraft.yaml"), "quadcopterdrone");
			var enforced = Int(Node(drone.Nodes, "CarrierSlave"), "MaxDistance");

			var player = Node(TopLevel("ai", "ai.yaml"), "Player");
			foreach (var profileKey in new[] { "DroneOperatorBotModule@experimental", "DroneOperatorBotModule@stable" })
			{
				var leash = Int(Node(player.Nodes, profileKey), "LeashCells");
				Assert.That(leash, Is.EqualTo(enforced),
					$"{profileKey}: LeashCells {leash} disagrees with quadcopterdrone's enforced " +
					$"CarrierSlave.MaxDistance {enforced}.");
			}
		}
	}
}
