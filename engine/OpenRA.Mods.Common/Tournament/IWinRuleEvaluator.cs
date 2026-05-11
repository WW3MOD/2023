#region Copyright & License Information
/*
 * WW3MOD AI tournament harness — win-rule plug-in interface.
 */
#endregion

using System.Collections.Generic;

namespace OpenRA.Mods.Common.Tournament
{
	/// <summary>
	/// Decides when the match ends and who won. Called once per watcher tick.
	///
	/// Returning null = match continues. Returning a verdict = match ends, that
	/// verdict is persisted to disk and the game exits.
	///
	/// Register implementations in MatchHarness, reference by name from
	/// tournament.yaml's "WinRule:" field.
	/// </summary>
	public interface IWinRuleEvaluator
	{
		MatchVerdict EvaluateEndState(
			World world,
			MatchTrackingState state,
			Dictionary<Player, MatchScoreSnapshot> scores,
			int currentTick,
			int timeLimitTicks);
	}
}
