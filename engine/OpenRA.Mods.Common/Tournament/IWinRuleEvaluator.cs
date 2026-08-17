#region Copyright & License Information
/*
 * WW3MOD AI tournament harness — win-rule plug-in interface.
 */
#endregion

using System.Collections.Generic;

namespace OpenRA.Mods.Common.Tournament
{
	/// <summary>
	/// <para>Decides when the match ends and who won. Called once per watcher tick.</para>
	///
	/// <para>Returning null = match continues. Returning a verdict = match ends, that
	/// verdict is persisted to disk and the game exits.</para>
	///
	/// <para>Register implementations in MatchHarness, reference by name from
	/// tournament.yaml's "WinRule:" field.</para>
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
