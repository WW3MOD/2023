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

/*
 * THE ONE CONSISTENCY RULE LEFT BETWEEN THE PHASE CLOCKS, and it is short because the rework
 * removed the others rather than because nobody looked.
 *
 * ==== WHY THERE IS ONLY ONE ====
 * The first warheads clock is measured FROM THE FIRST KILL (DEFCON 1), not from the match clock, so
 * there is no ordering between it and the no-rush period at all: the peace phase between them ends
 * on an EVENT, and an event has no duration to be shorter than. The old timeline needed a
 * T1 < T2 < T3 chain; this one needs T3 vs T1 and nothing else.
 *
 * What survives is the case where the match would be OVER before the border ever opens: a time
 * limit at or below the no-rush period means every second of the match is spent unable to cross the
 * line. That is reachable from the shipped value sets (a 10-minute limit against a 10- or 15-minute
 * no-rush period) and it is not obviously wrong from either dropdown alone, which is exactly the
 * shape that wants flagging.
 *
 * ==== IT FLAGS AND NEVER CLAMPS ====
 * Nothing here writes an option. Silently moving a host's other setting to make one of them legal
 * is the behaviour the user ruled out: "a window that would collapse to zero is flagged, never
 * silently clamped". Both values stay exactly as the host set them and the lobby says so.
 *
 * ==== IT READS THE WIRE, NOT THE TRAITS ====
 * Every value is an enumerated string in Session.Global, which is what both callers already hold --
 * the timeline's ChromeLogic and LobbyOptionsLogic's tooltip delegates. Reading the traits instead
 * would need a resolved MapPreview in both, and would answer with the DEFAULTS rather than with
 * what the host has actually chosen.
 */

using System.Globalization;
using OpenRA.Network;

namespace OpenRA.Mods.Common.Traits
{
	public static class LobbyPhaseConsistency
	{
		// The ids live here rather than on the traits because this class is read by BOTH sides of the
		// rule and one of them (`timelimit`) is a different trait again. DefconEscalationInfo's own
		// constants alias these two so a caller may use either name.
		public const string NoRushOptionId = "no-rush-period";
		public const string FirstWarheadsOptionId = "first-warheads";
		public const string TimeLimitOptionId = "timelimit";
		public const string ModeOptionId = "defcon-mode";
		public const string UnlockIntervalOptionId = "nuclear-unlock-interval";

		/// <summary>Shown on the timeline and appended to both offending dropdowns' tooltips.</summary>
		public const string MatchEndsDuringNoRushText = "Match ends during no-rush";

		public const string MatchEndsDuringNoRushDetail =
			"The time limit is at or before the end of the no-rush period, so the match would finish " +
			"without the border ever opening. Raise the time limit or shorten the no-rush period.";

		public const string NukesNeverUnlockText = "Match ends before the first nukes";

		public const string NukesNeverUnlockDetail =
			"The first nuclear tier comes up for sale at or after the time limit, so nothing nuclear " +
			"is ever purchasable. Raise the time limit or shorten the unlock interval.";

		/// <summary>A minute-valued lobby option, or <paramref name="fallback"/> if it is absent or unparsable.</summary>
		public static int Minutes(Session.Global settings, string id, int fallback)
		{
			if (settings == null)
				return fallback;

			var raw = settings.OptionOrDefault(id, null);
			if (raw == null || !int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes))
				return fallback;

			return minutes;
		}

		public static bool IsEscalation(Session.Global settings)
		{
			var mode = settings?.OptionOrDefault(ModeOptionId, null);
			return string.Equals(mode, nameof(DefconGameMode.Escalation), System.StringComparison.OrdinalIgnoreCase);
		}

		/// <summary>The warning this lobby is currently in, or null. See the file header.</summary>
		// THE CHECK IS THE SAME SHAPE IN BOTH MODES and is written twice rather than folded together,
		// because the two are different facts: in Escalation the clock that never finishes is the
		// BORDER, and in Skirmish it is the nuclear SHOP. A host who has hit one wants to be told
		// which, and merging them would produce a sentence that names neither.
		public static string Warning(Session.Global settings)
		{
			if (settings == null)
				return null;

			// 0 is "No limit" for `timelimit`, and a match with no limit cannot end during anything.
			var limit = Minutes(settings, TimeLimitOptionId, 0);
			if (limit <= 0)
				return null;

			if (IsEscalation(settings))
				return limit <= Minutes(settings, NoRushOptionId, 0) ? MatchEndsDuringNoRushText : null;

			// Skirmish. An interval of 0 is the no-wait opt-out -- everything is on sale from the
			// first second -- so the shop is never late and there is nothing to warn about.
			var interval = Minutes(settings, UnlockIntervalOptionId, 0);
			if (interval <= 0)
				return null;

			return interval >= limit ? NukesNeverUnlockText : null;
		}

		/// <summary>The long form of <see cref="Warning"/>, for a tooltip.</summary>
		public static string WarningDetail(Session.Global settings)
		{
			var warning = Warning(settings);
			if (warning == null)
				return null;

			return warning == MatchEndsDuringNoRushText ? MatchEndsDuringNoRushDetail : NukesNeverUnlockDetail;
		}

		/// <summary>Whether this option is one of the two the current warning is about.</summary>
		// Only the offending pair gets the tooltip. Hanging it on every dropdown would make it
		// wallpaper, and the host would stop reading the one place it means something.
		public static bool WarningAppliesTo(Session.Global settings, string optionId)
		{
			var warning = Warning(settings);
			if (warning == null)
				return false;

			if (optionId == TimeLimitOptionId)
				return true;

			return warning == MatchEndsDuringNoRushText
				? optionId == NoRushOptionId
				: optionId == UnlockIntervalOptionId;
		}
	}
}
