#region Copyright & License Information
/*
 * WW3MOD — wiring pins for the @experimental helicopter economy gate (MinGroundArmyValue).
 *
 * WHY THIS IS A SOURCE SCAN AND NOT AN ARITHMETIC PIN. The gate is
 * UnitBuilderBotModule.RefuseUntilGroundArmy, whose denominator walks world.Actors and Cargo. It cannot be
 * constructed without a World, so there is no pure function here to pin — and pinning the comparison
 * `value < threshold` in isolation would assert nothing a broken call site could not also satisfy.
 *
 * The real risk this change introduces is not arithmetic, it is the SEAM. UnitBuilderBotModule applies its
 * post-pick gates at TWO places that must agree exactly:
 *
 *   * BuildUnit — the buy path, which `return`s without ordering; and
 *   * IsCompositionCandidateEligible — the composition-directed picker's eligibility test.
 *
 * The file states the hazard itself, above the transport gate: "Same predicate as BuildUnit's, through the
 * same helper — these two drifting apart is how a slot becomes 'eligible' for a buy that the buy path then
 * refuses, wasting the cycle." A gate applied at only ONE of the two is silently half-installed: applied at
 * the buy path alone it burns a production cycle every time; applied at eligibility alone it does nothing at
 * all on the heli twins, which do not set CompositionDirected and therefore never reach that path.
 *
 * That second case is what makes this test load-bearing rather than ceremonial: the heli lane's LIVE path is
 * BuildUnit, so a wiring mistake that dropped the BuildUnit call would leave the whole feature inert while
 * every unit test about it still passed.
 *
 * STATED GAP: this proves the helper is CALLED from both sites. It does not prove the denominator counts the
 * right actors — GroundArmyValue's membership predicate is unpinned by construction and is verifiable only in
 * a match.
 */
#endregion

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace OpenRA.Test
{
	[TestFixture]
	public class HeliEconomyGateTest
	{
		// The call, not the declaration: a call is followed by `(` and is not preceded by `bool `.
		static readonly Regex GateCall = new(@"RefuseUntilGroundArmy\(", RegexOptions.Compiled);
		static readonly Regex GateDeclaration = new(@"\bbool\s+RefuseUntilGroundArmy\(", RegexOptions.Compiled);

		// Method headers, matched on the signature rather than on a comment mentioning the name.
		static readonly Regex BuyPath = new(@"\bvoid\s+BuildUnit\(", RegexOptions.Compiled);
		static readonly Regex EligibilityPath = new(@"\bbool\s+IsCompositionCandidateEligible\(", RegexOptions.Compiled);

		static string FindUnitBuilderSource()
		{
			var dir = new DirectoryInfo(AppContext.BaseDirectory);
			for (var i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
			{
				var candidate = Path.Combine(dir.FullName, "OpenRA.Mods.Common", "Traits", "BotModules",
					"UnitBuilderBotModule.cs");
				if (File.Exists(candidate))
					return candidate;
			}

			return null;
		}

		/// <summary>The enclosing method for a line, found by walking BACK to the nearest signature we
		/// recognise. Cheaper and far more robust than brace counting, which trips over the string literals
		/// and lambdas this file is full of.</summary>
		static string EnclosingMethod(string[] lines, int index)
		{
			for (var i = index; i >= 0; i--)
			{
				if (BuyPath.IsMatch(lines[i]))
					return "BuildUnit";

				if (EligibilityPath.IsMatch(lines[i]))
					return "IsCompositionCandidateEligible";
			}

			return "(neither)";
		}

		[Test]
		public void SourceScan_GroundArmyGateIsAppliedAtBothPostPickSites()
		{
			var file = FindUnitBuilderSource();
			if (file == null)
				Assert.Ignore("source tree not reachable from the test assembly — scan skipped, not passed");

			var lines = File.ReadAllLines(file);
			var callSites = new List<string>();
			for (var i = 0; i < lines.Length; i++)
			{
				if (!GateCall.IsMatch(lines[i]) || GateDeclaration.IsMatch(lines[i]))
					continue;

				callSites.Add(EnclosingMethod(lines, i) + " (line " + (i + 1) + ")");
			}

			Assert.That(callSites, Has.Exactly(1).StartsWith("BuildUnit"),
				"MinGroundArmyValue must be applied on the BUY path. This is the heli lane's LIVE path — the "
				+ "@experimental heli twins do not set CompositionDirected, so BuildUnit is the only place the "
				+ "gate can actually stop a purchase. Without it the whole feature is inert. Found: "
				+ string.Join(", ", callSites));

			Assert.That(callSites, Has.Exactly(1).StartsWith("IsCompositionCandidateEligible"),
				"MinGroundArmyValue must also be applied on the ELIGIBILITY path, or a composition-directed "
				+ "profile calls a gated type buyable and BuildUnit then refuses it, burning the production "
				+ "cycle — the exact drift the transport gate is commented against. Found: "
				+ string.Join(", ", callSites));

			Assert.That(callSites.Count, Is.EqualTo(2),
				"Expected exactly two call sites, one per post-pick path. A third means a gate was added "
				+ "somewhere the other post-pick path does not mirror. Found: " + string.Join(", ", callSites));
		}
	}
}
