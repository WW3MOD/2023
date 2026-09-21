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

namespace OpenRA.Mods.Common
{
	// WW3MOD: client-side half of the update notice. WebServices.LatestVersionUrl points at a static
	// file whose only content is the latest released version string; this is what decides what the
	// menu does with it. Kept pure and free of Game/HTTP state so OpenRA.Test can pin the rules --
	// the network half is untestable, so the part that can be wrong quietly must be the testable one.
	public static class ModVersion
	{
		// A remote file that is not a version at all (a 404 body, an HTML error page, an empty
		// commit) must never be read as "you are up to date". Anything past this length is treated
		// as garbage rather than parsed, so a misconfigured URL cannot feed a whole page in.
		const int MaxLength = 64;

		// v0.1.2 -> 0.1.2.0. Release tags carry the leading v; the mod.yaml stamp copies the tag
		// verbatim, so both sides of the comparison normally have it.
		const int Components = 4;

		// \uFEFF is a byte order mark: one on the first line would otherwise make the file unparseable.
		static readonly char[] TrimChars = { '\r', ' ', '\t', '\uFEFF' };

		/// <summary>Decides whether a running mod version is behind the latest released one.</summary>
		// Returns Unknown for anything it cannot answer honestly: an unparseable local version (an
		// unstamped development build), an unparseable or empty remote file. Unknown never shows the
		// update notice, so a development build is never told it is outdated and a broken host is
		// never allowed to nag.
		public static ModVersionStatus Compare(string localVersion, string remoteVersion)
		{
			if (!TryParse(remoteVersion, out var remote))
				return ModVersionStatus.Unknown;

			if (!TryParse(localVersion, out var local))
				return ModVersionStatus.Unknown;

			for (var i = 0; i < Components; i++)
			{
				if (local[i] < remote[i])
					return ModVersionStatus.Outdated;

				if (local[i] > remote[i])
					return ModVersionStatus.Latest;
			}

			return ModVersionStatus.Latest;
		}

		/// <summary>Parses an optionally v-prefixed dot-separated version of up to four integer components.</summary>
		// Only the first non-empty line is considered, so a trailing newline in the hosted file --
		// which every sane editor and git will add -- is not a parse failure.
		public static bool TryParse(string version, out int[] components)
		{
			components = new int[Components];

			if (string.IsNullOrWhiteSpace(version))
				return false;

			var line = FirstNonEmptyLine(version);
			if (line == null || line.Length > MaxLength)
				return false;

			if (line[0] == 'v' || line[0] == 'V')
				line = line[1..];

			if (line.Length == 0)
				return false;

			var parts = line.Split('.');
			if (parts.Length > Components)
				return false;

			for (var i = 0; i < parts.Length; i++)
			{
				// int.TryParse on its own would accept a leading sign and culture group separators;
				// a version component is digits and nothing else.
				if (!IsDigitsOnly(parts[i]) || !int.TryParse(parts[i], out components[i]))
					return false;
			}

			return true;
		}

		static string FirstNonEmptyLine(string text)
		{
			foreach (var line in text.Split('\n'))
			{
				var trimmed = line.Trim(TrimChars);
				if (trimmed.Length > 0)
					return trimmed;
			}

			return null;
		}

		static bool IsDigitsOnly(string value)
		{
			if (value.Length == 0)
				return false;

			foreach (var c in value)
				if (c < '0' || c > '9')
					return false;

			return true;
		}
	}
}
