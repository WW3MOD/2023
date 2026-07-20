#region Copyright & License Information
/*
 * WW3MOD test harness — universal speed multiplier (world trait).
 *
 * Applies Test.SpeedMultiplier to world.Timestep for ALL test-mode runs,
 * not just AI tournaments. Same mechanism as the in-game SpeedControlButton
 * and BotVsBotMatchWatcher:  world.Timestep = max(1, base / multiplier).
 * This is pure wall-clock pacing — it never enters a synced path, so the
 * simulation stays byte-identical (see WORKSPACE/plans/260721_sim_throughput.md).
 *
 * Tournament runs are skipped here: BotVsBotMatchWatcher owns the apply for
 * those because it honors the per-scenario tournament.yaml SpeedMultiplier
 * override. Guarding on an empty TournamentConfigPath avoids double-dividing
 * the timestep.
 */
#endregion

using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[TraitLocation(SystemActors.World)]
	[Desc("Applies Test.SpeedMultiplier to world.Timestep for non-tournament test-mode runs.",
		"No-op unless Test.Mode=true, Test.SpeedMultiplier>1, and no Test.TournamentConfig is set.")]
	public class TestModeSpeedMultiplierInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) { return new TestModeSpeedMultiplier(); }
	}

	public class TestModeSpeedMultiplier : IWorldLoaded
	{
		void IWorldLoaded.WorldLoaded(World world, OpenRA.Graphics.WorldRenderer wr)
		{
			// Tournament runs apply their own (config-overridable) multiplier in
			// BotVsBotMatchWatcher.WorldLoaded — don't double-apply here.
			if (!TestMode.IsActive || TestMode.SpeedMultiplier <= 1 || !string.IsNullOrEmpty(TestMode.TournamentConfigPath))
				return;

			var oldTimestep = world.Timestep;
			world.Timestep = System.Math.Max(1, oldTimestep / TestMode.SpeedMultiplier);
			Log.Write("debug", $"[TestMode] speed multiplier {TestMode.SpeedMultiplier}x — Timestep {oldTimestep} → {world.Timestep} ms/tick");
		}
	}
}
