#region Copyright & License Information
/*
 * WW3MOD developer test harness — Lua scripting bindings.
 * Activated only when TestMode.IsActive (i.e. the game was launched with
 * Test.Mode=true). All methods are no-ops outside test mode so accidental
 * calls from a regular map don't write result files or quit the game.
 */
#endregion

using OpenRA.Scripting;

namespace OpenRA.Mods.Common.Scripting.Global
{
	[ScriptGlobal("Test")]
	public class TestGlobal : ScriptGlobal
	{
		public TestGlobal(ScriptContext context)
			: base(context) { }

		[Desc("Mark the current test as passed and exit the game. " +
			"No-op outside test mode.")]
		public void Pass()
		{
			if (!TestMode.IsActive)
				return;

			TestMode.WriteResult("pass", "");
			Game.Exit();
		}

		[Desc("Mark the current test as failed (with a reason) and exit the game. " +
			"No-op outside test mode.")]
		public void Fail(string reason = "")
		{
			if (!TestMode.IsActive)
				return;

			TestMode.WriteResult("fail", reason ?? "");
			Game.Exit();
		}

		[Desc("Mark the current test as skipped (with a reason) and exit the game. " +
			"No-op outside test mode.")]
		public void Skip(string reason = "")
		{
			if (!TestMode.IsActive)
				return;

			TestMode.WriteResult("skip", reason ?? "");
			Game.Exit();
		}
	}
}
