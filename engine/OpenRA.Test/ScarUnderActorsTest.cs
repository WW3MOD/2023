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
	/// A nuclear blast used to leave clean, unburnt rectangles wherever an actor was standing, because
	/// every Scar warhead carried `InvalidTargets: Vehicle, Structure, Wall`. THAT WAS DELIBERATE AND IS
	/// NOW DELIBERATELY REVERSED (2026-09-09) — the holes being present was the pass criterion for the
	/// scar work of the same morning, and the user then ruled the other way: a destroyed building must
	/// not mean the ground under it was undisturbed. This fixture exists so the reversal cannot be
	/// quietly undone by someone reading the older intent and "restoring" it.
	///
	/// <para>The reversal is NOT the removal of `InvalidTargets` on its own, and that is the part worth
	/// pinning. `Warhead.IsValidAgainst` is `ValidTargets.Overlaps(t) AND NOT InvalidTargets.Overlaps(t)`,
	/// so a cell is skipped when EITHER half rejects the actor in it. Dropping the exclusion fixes only
	/// the half that names Vehicle/Structure/Wall — all three of which do carry `Ground` in this mod, so
	/// they would pass. Two cases fail the other half and are untouchable from YAML:</para>
	///
	/// <para>* A vehicle husk advertises `NoAutoTarget, Husk` (husks/husks-vehicles.yaml) with no
	/// `Ground`, `Water` or `Trees`, so ValidTargets never overlaps it. This is the COMMON case, not an
	/// exotic one: the damage warheads on a nuke land at Delay 0-1 and the Scar warheads at Delay 2-6,
	/// so the vehicles inside a blast are already husks by the time the smudge is placed. A YAML-only
	/// fix would still have left a clean rectangle under every tank the nuke killed.</para>
	///
	/// <para>* A crate has no Targetable at all, so its target-type set is empty and `BitSet.Overlaps`
	/// is false against any ValidTargets whatsoever.</para>
	///
	/// <para>Hence `IgnoreActors`, which skips the per-cell actor test outright. It defaults to false, so
	/// the two non-Scar LeaveSmudge warheads in these same files (EmpBomb's Scorch, MOPPenetration's
	/// Crater) and every ordinary weapon in the mod keep the stock behaviour untouched — deliberately,
	/// because the ruling was about nuclear scarring and those use the RA smudge types shared mod-wide.</para>
	///
	/// <para>Fields are NOT part of this and never were: they are `GroundCover`, which `BlockingActorsAt`
	/// already filters out, so their cells have always been marked. What hides a scar on farmland is
	/// draw order, not this test — see the notes on ^CivField in ingame/civilian.yaml.</para>
	/// </summary>
	[TestFixture]
	public class ScarUnderActorsTest
	{
		static readonly string[] WeaponFiles = { "weapons-nuclear-arsenal.yaml", "weapons-superweapons.yaml" };

		/// <summary>The five smudge types that make up a nuclear scar.</summary>
		static readonly string[] ScarTypes = { "ScarCore", "ScarCrater", "ScarChar", "ScarBurn", "ScarRim" };

		/// <summary>
		/// The shipped count, split per file. Pinned as a number because the failure this guards is a
		/// SILENT one: a Scar warhead that quietly loses `IgnoreActors` punches its hole back and
		/// nothing else in the suite, the build or the YAML lint says a word about it. If you add or
		/// remove nuclear weapons, update these and say so in the commit message.
		/// </summary>
		static readonly (string File, int Expected)[] ScarWarheadCounts =
		{
			("weapons-nuclear-arsenal.yaml", 51),
			("weapons-superweapons.yaml", 10),
		};

		static string FindRules(params string[] parts)
		{
			var dir = new DirectoryInfo(AppContext.BaseDirectory);
			for (var i = 0; i < 10 && dir != null; i++, dir = dir.Parent)
			{
				var candidate = Path.Combine(new[] { dir.FullName, "mods", "ww3mod", "rules" }.Concat(parts).ToArray());
				if (File.Exists(candidate))
					return candidate;
			}

			throw new FileNotFoundException($"could not locate mods/ww3mod/rules/{string.Join("/", parts)}");
		}

		static string Field(MiniYaml node, string key)
		{
			return node.Nodes.FirstOrDefault(n => n.Key == key)?.Value.Value?.Trim();
		}

		/// <summary>Every LeaveSmudge warhead in the two nuclear weapon files, with its owning weapon.</summary>
		static List<(string Weapon, string Warhead, MiniYaml Node)> SmudgeWarheads(string file)
		{
			var found = new List<(string, string, MiniYaml)>();
			foreach (var weapon in MiniYaml.FromFile(FindRules("weapons", file)))
				foreach (var wh in weapon.Value.Nodes.Where(n => n.Value.Value == "LeaveSmudge"))
					found.Add((weapon.Key, wh.Key, wh.Value));

			return found;
		}

		static List<(string Weapon, string Warhead, MiniYaml Node)> ScarWarheads(string file)
		{
			return SmudgeWarheads(file).Where(w => ScarTypes.Contains(Field(w.Node, "SmudgeType"))).ToList();
		}

		// ---- the reversal ---------------------------------------------------------------------

		/// <summary>The headline. Every Scar warhead marks its cells regardless of what is standing in them.</summary>
		[Test]
		public void EveryScarWarheadMarksTheGroundUnderActors()
		{
			foreach (var file in WeaponFiles)
			{
				foreach (var (weapon, warhead, node) in ScarWarheads(file))
				{
					var what = $"{file} {weapon} {warhead}";

					Assert.That(Field(node, "IgnoreActors"), Is.EqualTo("true"),
						$"{what} lost IgnoreActors. The ground under every vehicle, structure, wall, husk " +
						"and crate in this weapon's blast is now left clean and unburnt, which is the exact " +
						"appearance the 2026-09-09 ruling reversed. This is not a bug to re-fix: read the " +
						"fixture summary before changing it.");

					Assert.That(Field(node, "InvalidTargets"), Is.Null,
						$"{what} has regained an InvalidTargets exclusion. On a warhead that sets " +
						"IgnoreActors this line has NO READER AT ALL — it is inert, not merely permissive — " +
						"so it will read to the next person as protection that is in force when it is not.");
				}
			}
		}

		/// <summary>
		/// The count, per file. Guards the case the per-warhead test cannot see: a Scar warhead deleted
		/// outright, or a new nuclear weapon added whose scar bands nobody remembered to opt in.
		/// </summary>
		[Test]
		public void TheScarWarheadRosterIsTheShippedOne()
		{
			foreach (var (file, expected) in ScarWarheadCounts)
			{
				Assert.That(ScarWarheads(file).Count, Is.EqualTo(expected),
					$"{file} no longer holds {expected} Scar warheads. If you added a nuclear weapon, give " +
					"its Scar bands IgnoreActors: true and update this count; if you removed one, just " +
					"update the count.");
			}
		}

		/// <summary>
		/// The other half of the ruling's scope, pinned from the opposite side. IgnoreActors defaults to
		/// false, and the two non-Scar smudge warheads that share these files were deliberately left
		/// alone: they use the RA smudge types every ordinary weapon in the mod shares, and widening the
		/// ruling to them would change conventional craters nobody asked about. Neither sits on a weapon
		/// that also draws a Scar, so no single blast is inconsistent with itself.
		/// </summary>
		[Test]
		public void NonScarSmudgeWarheadsWereLeftOnStockBehaviour()
		{
			var touched = new List<string>();
			foreach (var file in WeaponFiles)
			{
				var scarWeapons = ScarWarheads(file).Select(s => s.Weapon).ToHashSet();
				foreach (var (weapon, warhead, node) in SmudgeWarheads(file))
				{
					var smudgeType = Field(node, "SmudgeType");
					if (ScarTypes.Contains(smudgeType))
						continue;

					if (Field(node, "IgnoreActors") != null)
						touched.Add($"{file} {weapon} {warhead}");

					// If a non-Scar smudge ever lands on a weapon that also draws a Scar, one blast would
					// scar under a tank and crater around it — the inconsistency the ruling was about.
					Assert.That(scarWeapons.Contains(weapon), Is.False,
						$"{file} {weapon} {warhead} draws a stock {smudgeType} smudge on a weapon that also " +
						"draws a nuclear scar. That single blast is now internally inconsistent: the scar " +
						"bands mark the ground under actors and this one does not. Give it " +
						"IgnoreActors: true and update the Scar roster count above.");
				}
			}

			Assert.That(touched, Is.Empty,
				"a non-Scar smudge warhead has been opted in to IgnoreActors. That widens a ruling that " +
				"was explicitly about nuclear scarring onto the RA smudge types shared with every " +
				"ordinary weapon in the mod. Deliberate? Say so in the commit message and move it into " +
				"the Scar roster.");
		}
	}
}
