#region Copyright & License Information
/*
 * WW3MOD inline force-start confirm — the state machine behind the lobby's Start button.
 *
 * Extracted from LobbyLogic as a pure static so it can be pinned without a live widget. The
 * force-start path cannot be reached in a solo skirmish (the trigger needs a client that is in a
 * slot, not the admin, not a bot, and not ready — and solo, the host is the admin), so the only
 * way to exercise this logic below a two-human multiplayer lobby is to test it directly.
 *
 * That matters more than the usual "nice to have tests" argument: this replaced a modal dialog
 * whose defect was purely cosmetic. If the arm/commit/expire transitions are wrong, the host
 * cannot force start at all — a functional regression strictly worse than the bug it replaced.
 * Pinned in ForceStartConfirmTest.
 */
#endregion

namespace OpenRA.Mods.Common.Widgets.Logic
{
	public enum ForceStartClickResult
	{
		/// <summary>Start the game immediately.</summary>
		StartNow,

		/// <summary>Hold, relabel the button and show the reminder; a second click commits.</summary>
		Arm,
	}

	public static class ForceStartConfirm
	{
		/// <summary>
		/// Whether a confirm armed at <paramref name="armedUntil"/> is still live at
		/// <paramref name="now"/>. Exclusive: exactly at the deadline the confirm has lapsed.
		/// </summary>
		public static bool IsArmed(long armedUntil, long now)
		{
			return now < armedUntil;
		}

		/// <summary>
		/// Resolves one click of the Start button. <paramref name="nextArmedUntil"/> is the deadline
		/// to store back; zero means disarmed.
		/// </summary>
		public static ForceStartClickResult Resolve(long armedUntil, long now, bool anyPlayerUnready,
			int confirmMilliseconds, out long nextArmedUntil)
		{
			// Only an unready lobby ever arms. Everything else — an armed confirm being committed,
			// or a lobby where everyone is ready — starts on the first click, so the confirm never
			// appears on the common path.
			if (!IsArmed(armedUntil, now) && anyPlayerUnready)
			{
				nextArmedUntil = now + confirmMilliseconds;
				return ForceStartClickResult.Arm;
			}

			nextArmedUntil = 0;
			return ForceStartClickResult.StartNow;
		}
	}
}
