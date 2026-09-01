using System;
using System.IO;
using Eluant;
using NUnit.Framework;

namespace OpenRA.Test
{
	/// <summary>
	/// Pins TestHarness.HoldsAttackActivity, the predicate two scenarios now assert on in place of
	/// Actor.IsIdle.
	///
	/// WHY IT NEEDS PINNING AT ALL. It is string matching over Test.ActivityChain's output, and string
	/// matching over a format defined somewhere else is exactly the kind of thing that silently starts
	/// returning false for everything. A predicate stuck at false makes both scenarios pass
	/// unconditionally — the failure mode is a GREEN, which no run would report and no reader would
	/// question. lua-gate cannot catch it either: it checks that binding NAMES exist, not what a pure
	/// Lua function computes.
	///
	/// The inputs below are not invented. They are the chains recorded by the instrumented run
	/// 260901_073202_p95073 that settled the diagnosis (WORKSPACE/bugs/discovered.md 2026-09-01):
	/// the attack chains observed at the drain, and the RotateToEdge chains observed after the ammo
	/// guard released the unit. So this fixture asserts the predicate separates the two states that
	/// actually occurred, rather than two states imagined here.
	/// </summary>
	[TestFixture]
	public class HoldsAttackActivityTest
	{
		const string HelperRelativePath = "mods/ww3mod/scripts/test-helpers.lua";

		static string FindHelperScript()
		{
			var dir = new DirectoryInfo(AppContext.BaseDirectory);
			for (var i = 0; i < 10 && dir != null; i++, dir = dir.Parent)
			{
				var candidate = Path.Combine(dir.FullName, "mods", "ww3mod", "scripts", "test-helpers.lua");
				if (File.Exists(candidate))
					return candidate;
			}

			return null;
		}

		/// <summary>
		/// Run HoldsAttackActivity against the REAL helper file, with Test.ActivityChain stubbed to
		/// return <paramref name="chain"/>. Lua resolves globals at call time, so the helper file loads
		/// without a world and without the engine bindings it never reaches on this path.
		/// </summary>
		static bool Holds(string chain)
		{
			var helper = FindHelperScript();
			if (helper == null)
				Assert.Ignore($"{HelperRelativePath} not reachable from the test assembly — check skipped, not passed");

			using (var runtime = new LuaRuntime())
			{
				// TestHarness must exist before the helper file assigns into it, mirroring the engine,
				// which creates the table via the ScriptGlobal before any scenario script runs.
				runtime.DoBuffer("TestHarness = TestHarness or {}", "prelude").Dispose();
				runtime.DoBuffer(File.ReadAllText(helper), HelperRelativePath).Dispose();

				var script =
					"Test = { ActivityChain = function() return [[" + chain + "]] end }\n" +
					"return TestHarness.HoldsAttackActivity({})";

				using (var results = runtime.DoBuffer(script, "holds-attack-check"))
				{
					Assert.That(results.Count, Is.GreaterThan(0), "the check script returned nothing");
					return results[0].ToString() == "True" || results[0].ToString() == "true";
				}
			}
		}

		[TestCase("Attack>SmartMoveActivity>MoveWithinRange>Move>MoveFirstHalf",
			TestName = "Shooter's direct attack, as recorded at the drain")]
		[TestCase("AttackMoveActivity>SmartMoveActivity>Move>MoveFirstHalf",
			TestName = "Hunter's attack-move parent, whose child is an ordinary Move")]
		[TestCase("AttackActivity>Move",
			TestName = "AttackFollow.AttackActivity, the turreted path the abrams runs on")]
		public void HoldingAnAttackActivityIsDetected(string chain)
		{
			Assert.That(Holds(chain), Is.True,
				$"HoldsAttackActivity returned false for `{chain}`, which IS a held attack order. "
				+ "Both dry-breaks-off scenarios would then pass unconditionally.");
		}

		[TestCase("RotateToEdge>SmartMoveActivity>Move>Turn",
			TestName = "post-guard evacuation, as recorded after the guard fired")]
		[TestCase("RotateToEdge>SmartMoveActivity>Move>MoveSecondHalf",
			TestName = "same evacuation, later leg")]
		[TestCase("(idle)", TestName = "genuinely idle — the state the old assertion looked for")]
		[TestCase("", TestName = "empty, i.e. Test.ActivityChain outside test mode")]
		[TestCase("SmartMoveActivity>Move>MoveFirstHalf", TestName = "a plain move order")]
		public void NotHoldingAnAttackActivityIsDetected(string chain)
		{
			Assert.That(Holds(chain), Is.False,
				$"HoldsAttackActivity returned true for `{chain}`, which is NOT a held attack order. "
				+ "Both dry-breaks-off scenarios would then fail even though the guard works — the "
				+ "exact false accusation this whole change exists to remove.");
		}

		[Test]
		public void AQueuedAttackCountsAsHeld()
		{
			// The '|' separator carries NextActivity entries. An attack sitting behind the current
			// activity is still an attack order the unit is holding, and normalising only '>' would
			// miss it — the unit would read as released while an attack waits one slot back.
			Assert.That(Holds("SmartMoveActivity>Move | Attack>MoveWithinRange"), Is.True,
				"a queued (NextActivity) attack was not detected; the ' | ' separator is not being "
				+ "normalised, so anything past the first queue entry is invisible to the predicate");
		}

		[Test]
		public void ASubstringMatchDoesNotCountAsHeld()
		{
			// The predicate is a prefix test at a component boundary, NOT a substring search. An
			// activity merely CONTAINING "Attack" mid-name must not register, or unrelated activities
			// would hold these scenarios red forever.
			Assert.That(Holds("CounterAttackWatcher>Move"), Is.False,
				"a component whose name merely contains 'Attack' was treated as an attack activity; "
				+ "the match is not anchored to a component boundary");
		}
	}
}
