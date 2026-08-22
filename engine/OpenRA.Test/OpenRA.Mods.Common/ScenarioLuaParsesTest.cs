#region Copyright & License Information
/*
 * WW3MOD autotest scenario Lua syntax gate.
 *
 * A syntax error in a scenario script is not caught by anything before launch: `make test` /
 * `utility.sh --check-yaml` lint MiniYaml and never open a .lua, and the failure mode in-game is a
 * TIMEOUT-FAIL or NO-RESULT that looks exactly like a scenario that simply did not reach its
 * assertion. Autotest runs are minutes long and serialized behind a granted slot, so a typo costs a
 * slot to discover.
 *
 * Nothing on the box can parse Lua — no `luac`, no interpreter on PATH, and engine/lua/ holds only
 * helper scripts. But OpenRA.Game pulls in OpenRA-Eluant and engine/bin ships lua51.dylib, so the
 * real 5.1 parser IS reachable from a unit test.
 *
 * The chunk is wrapped as a function body rather than run. DoBuffer would EXECUTE the script, and
 * scenario top-levels assign globals and touch harness tables that do not exist outside a game;
 * wrapping compiles the same tokens while guaranteeing none of it runs. That makes this a pure
 * syntax check — it cannot tell you a scenario is correct, only that it parses.
 *
 * PITFALL this exists to catch: OpenRA runs Lua 5.1 (engine/lua/sandbox.lua uses setfenv,
 * loadstring and bare unpack, all removed or moved in 5.2). So `//` integer division, goto labels
 * and bitwise operators are all syntax errors here despite being ordinary modern Lua.
 */
#endregion

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Eluant;
using NUnit.Framework;

namespace OpenRA.Test
{
	[TestFixture]
	public class ScenarioLuaParsesTest
	{
		static string FindScenarioRoot()
		{
			var dir = new DirectoryInfo(AppContext.BaseDirectory);
			for (var i = 0; i < 10 && dir != null; i++, dir = dir.Parent)
			{
				var candidate = Path.Combine(dir.FullName, "tools", "autotest", "scenarios");
				if (Directory.Exists(candidate))
					return candidate;
			}

			throw new DirectoryNotFoundException("could not locate tools/autotest/scenarios");
		}

		[Test]
		public void EveryScenarioScriptParsesAsLua51()
		{
			var scripts = Directory.GetFiles(FindScenarioRoot(), "*.lua", SearchOption.AllDirectories)
				.OrderBy(f => f, StringComparer.Ordinal)
				.ToArray();

			// Non-vacuity: if the scan resolves nothing, the assertion below passes while checking
			// nothing at all.
			Assert.That(scripts, Is.Not.Empty, "found no scenario .lua files — the scan is broken, not the scripts");

			var failures = new List<string>();

			using (var runtime = new LuaRuntime())
			{
				foreach (var path in scripts)
				{
					// Wrapping as a function body compiles without executing. The trailing newline
					// matters: a script ending in a line comment would otherwise swallow the `end`.
					var chunk = "return function()\n" + File.ReadAllText(path) + "\nend";

					try
					{
						runtime.DoBuffer(chunk, Path.GetFileName(path)).Dispose();
					}
					catch (LuaException e)
					{
						failures.Add(Path.GetFileName(path) + ": " + e.Message);
					}
				}
			}

			Assert.That(failures, Is.Empty,
				"scenario Lua failed to parse under the engine's own Lua 5.1 runtime. These would launch, " +
				"run nothing and report TIMEOUT-FAIL or NO-RESULT, burning an autotest slot to discover:" +
				Environment.NewLine + string.Join(Environment.NewLine, failures));
		}
	}
}
