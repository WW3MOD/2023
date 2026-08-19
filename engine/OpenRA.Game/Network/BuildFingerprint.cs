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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using OpenRA.FileSystem;

namespace OpenRA.Network
{
	/// <summary>
	/// Identifies the build and content a player is actually running, so two players on
	/// divergent installs find out at the join handshake instead of desyncing a few frames in.
	/// </summary>
	/// <remarks>
	/// Three segments, "engine/rules/assets", each measured where the thing it describes
	/// actually takes effect:
	/// <list type="bullet">
	/// <item><b>engine</b> — a git revision stamped in at BUILD time (see
	/// engine/Directory.Build.targets), because C# is compiled. The running binary is whatever
	/// was on disk when it was compiled, not what is in the working tree now.</item>
	/// <item><b>rules</b> — a content hash of the simulation-defining mod yaml, computed at RUN
	/// time, because MiniYaml is read from disk on load. Here the working tree is exactly what
	/// takes effect.</item>
	/// <item><b>assets</b> — a digest of the mounted content packages, also at run time. This
	/// segment exists because mod.yaml mounts ~^SupportDir|Content/ra/v2/, a Red Alert
	/// installation that lives OUTSIDE the repository. Two players can be on the same commit,
	/// both freshly rebuilt, and still be running different content - and WW3MOD does not fail
	/// loudly when they are: DefaultSpriteSequence clamps out-of-range frame indices and
	/// continues, so a missing sprite silently yields a SHORTER animation rather than an error.
	/// An engine-revision-only fingerprint would wave that straight through.</item>
	/// </list>
	/// Getting the build/run split backwards produces a fingerprint that is confidently wrong:
	/// hashing engine sources at runtime would call two players identical when one of them
	/// edited a trait and never rebuilt, and stamping the mod yaml at build time would miss
	/// every edit made since the last compile.
	/// </remarks>
	public static class BuildFingerprint
	{
		/// <summary>Reported when the engine revision could not be determined at build time.</summary>
		public const string UnknownRevision = "unknown";

		/// <summary>Separates the engine revision, the rules hash and the asset digest.</summary>
		const char Separator = '/';

		/// <summary>Stands in for a segment whose computation threw. See <see cref="ContentHashes"/>.</summary>
		const string ComputeFailed = "error";

		/// <summary>
		/// How many leading segments <see cref="ReplaySegmentsMatch"/> weighs: the engine revision
		/// and the rules hash. The asset digest is deliberately outside this window.
		/// </summary>
		const int ReplaySegmentCount = 2;

		const int ContentHashChars = 8;

		static readonly string Revision = ReadRevision();

		static readonly object SyncObject = new();
		static readonly Dictionary<string, string> ContentHashCache = new();

		/// <summary>
		/// The git revision of the sources that were compiled into this assembly, e.g. "b0fa20d41c",
		/// or "b0fa20d41c+1a2b3c4d" when the tree carried uncommitted engine/mod changes at build time.
		/// <see cref="UnknownRevision"/> when git was unavailable (no git on PATH, or a source zip
		/// rather than a clone).
		/// </summary>
		public static string EngineRevision => Revision;

		static string ReadRevision()
		{
			var value = typeof(BuildFingerprint).Assembly
				.GetCustomAttributes<AssemblyMetadataAttribute>()
				.FirstOrDefault(a => a.Key == "BuildRevision")?.Value;

			return string.IsNullOrEmpty(value) ? UnknownRevision : value;
		}

		/// <summary>
		/// The full fingerprint for a mod, in the form "engineRevision/rulesHash/assetDigest".
		/// The hashes are computed on first use and cached, so this is free after the first call
		/// and costs nothing at all in a session that never opens a network game.
		/// </summary>
		public static string ForMod(ModData modData)
		{
			return EngineRevision + Separator + ContentHashes(modData);
		}

		/// <summary>
		/// Names the segment that differs between two fingerprints, so the player is told what
		/// to go and fix rather than being handed two opaque hashes.
		/// </summary>
		public static string DescribeDifference(string mine, string theirs)
		{
			if (string.IsNullOrEmpty(theirs))
				return "an older build that predates this check";

			var a = mine.Split(Separator);
			var b = theirs.Split(Separator);
			if (a.Length != b.Length)
				return "a different build";

			var differences = new List<string>();
			if (a[0] != b[0])
				differences.Add("engine build");

			if (a.Length > 1 && a[1] != b[1])
				differences.Add("mod rules");

			if (a.Length > 2 && a[2] != b[2])
				differences.Add("game content (the Red Alert files under Content/ra/v2)");

			return differences.Count == 0 ? "a different build" : differences.JoinWith(" and ");
		}

		/// <summary>
		/// Whether two fingerprints agree closely enough that a replay recorded under one will
		/// re-simulate identically under the other. Only the engine revision and the rules hash are
		/// weighed; an empty fingerprint never matches, because a replay that carries no stamp
		/// cannot be shown to agree with anything.
		/// </summary>
		/// <remarks>
		/// The asset digest is excluded, and that is a judgement call rather than an oversight.
		/// It is machine-local by construction — it digests the Red Alert installation under
		/// ^SupportDir, which differs between two computers that are running the identical build
		/// (see <see cref="ComputeAssetDigest"/>). Weighing it would refuse the ordinary case of
		/// recording a replay on one machine and watching it on another, which is a thing people
		/// do on purpose. The cost of excluding it is real and worth stating: a missing sprite
		/// shortens an animation rather than erroring, and WithMakeAnimation grants a condition for
		/// exactly as long as its sequence plays, so a content difference CAN move the simulation.
		/// That case is left to the sync check rather than refused up front.
		/// <para/>
		/// A segment that failed to compute (<see cref="ComputeFailed"/>) is skipped rather than
		/// treated as a difference. A transient file system error while hashing must not be the
		/// reason a replay will not open — the same principle <see cref="ContentHashes"/> applies
		/// to the join path.
		/// </remarks>
		public static bool ReplaySegmentsMatch(string mine, string theirs)
		{
			if (string.IsNullOrEmpty(mine) || string.IsNullOrEmpty(theirs))
				return false;

			var a = mine.Split(Separator);
			var b = theirs.Split(Separator);

			for (var i = 0; i < ReplaySegmentCount; i++)
			{
				if (i >= a.Length || i >= b.Length)
					return false;

				if (a[i] == ComputeFailed || b[i] == ComputeFailed)
					continue;

				if (a[i] != b[i])
					return false;
			}

			return true;
		}

		/// <summary>
		/// Names what differs between two fingerprints, considering only the segments
		/// <see cref="ReplaySegmentsMatch"/> weighs, so the reason given to the player matches the
		/// reason the replay was actually refused.
		/// </summary>
		public static string DescribeReplayDifference(string mine, string theirs)
		{
			if (string.IsNullOrEmpty(theirs))
				return "an older build that predates this check";

			return DescribeDifference(ReplaySegments(mine), ReplaySegments(theirs));
		}

		static string ReplaySegments(string fingerprint)
		{
			return string.Join(Separator, fingerprint.Split(Separator).Take(ReplaySegmentCount));
		}

		/// <summary>
		/// Formats a fingerprint for a human. Only used in warning paths, so it spells out the
		/// cases a bare hash cannot explain by itself.
		/// </summary>
		public static string Describe(string fingerprint)
		{
			if (string.IsNullOrEmpty(fingerprint))
				return "unknown (predates this check)";

			if (fingerprint.StartsWith(UnknownRevision + Separator, StringComparison.Ordinal))
				return fingerprint + " (no git revision: built from a source zip, or git was not on PATH)";

			return fingerprint;
		}

		/// <summary>
		/// The packages the asset digest actually compares. Diagnostic only - these are absolute
		/// local paths and must never be hashed.
		/// </summary>
		public static IEnumerable<string> ExternalContentPackages(ModData modData)
		{
			return modData.ModFiles.MountedPackages.Where(IsExternalContent).Select(p => p.Name);
		}

		static string ContentHashes(ModData modData)
		{
			var id = modData.Manifest.Id;
			lock (SyncObject)
			{
				if (ContentHashCache.TryGetValue(id, out var cached))
					return cached;

				try
				{
					var hashes = ComputeContentHash(modData) + Separator + ComputeAssetDigest(modData);

					// Only a successful result is cached. The failure below is usually
					// transient - a locked archive during a virus scan, a content folder busy
					// for a moment - and caching it would leave the fingerprint useless for the
					// rest of the session over a hiccup that has already passed.
					ContentHashCache[id] = hashes;
					return hashes;
				}
				catch (Exception e)
				{
					// This runs inside Server.ValidateClient, whose catch-all drops the
					// connection - so an exception here would make the game unjoinable rather
					// than merely unfingerprinted. Reading the file system can fail for reasons
					// that have nothing to do with the match. A diagnostic must never be the
					// reason nobody can play.
					Log.Write("debug", $"Could not compute the build fingerprint: {e}");
					return ComputeFailed + Separator + ComputeFailed;
				}
			}
		}

		/// <summary>
		/// Hashes the mod definitions that feed the deterministic simulation.
		/// </summary>
		/// <remarks>
		/// Included, and why:
		/// <list type="bullet">
		/// <item>mod.yaml — carries MapGrid and GameSpeeds (the tick length itself) plus the file
		/// lists below, so a change here can move the simulation without touching any rule file.</item>
		/// <item>Rules — actor and trait definitions. The core of the simulation.</item>
		/// <item>Weapons — damage, ranges, projectile behaviour.</item>
		/// <item>TileSets — terrain types, which set movement costs and therefore pathfinding.</item>
		/// <item>Sequences — animation frame counts are NOT purely cosmetic here: WithMakeAnimation
		/// grants a condition for exactly as long as its sequence plays
		/// (Traits/Render/WithMakeAnimation.cs:62-76), and conditions gate simulation traits. A
		/// sequence length difference is therefore a condition duration difference.</item>
		/// </list>
		/// Deliberately excluded, and why:
		/// <list type="bullet">
		/// <item>Voices, Notifications, Music — playback is client-local and never enters the
		/// synced state, so a player who edited an audio definition should not be locked out.</item>
		/// <item>Chrome, ChromeLayout, ChromeMetrics, Cursors, FluentMessages, Missions — UI and
		/// text. Same reasoning.</item>
		/// <item>Hotkeys — per-user configuration. Hashing it would refuse every player who
		/// rebound a key.</item>
		/// <item>Every binary asset (sprites, .mix, audio, fonts). These are extracted by each
		/// player from their own Red Alert installation, which may legitimately be a different
		/// release (Origin vs. The First Decade vs. the 2008 freeware mirror). Hashing them would
		/// guarantee a permanent mismatch between two installs that play together perfectly.</item>
		/// <item>Maps — already content-hashed independently by Map.ComputeUID.</item>
		/// </list>
		/// </remarks>
		static string ComputeContentHash(ModData modData)
		{
			var manifest = modData.Manifest;
			using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
			{
				void AppendLabel(string label)
				{
					hash.AppendData(Encoding.UTF8.GetBytes("\0" + label + "\0"));
				}

				void AppendStream(Stream stream)
				{
					using (var ms = new MemoryStream())
					{
						stream.CopyTo(ms);
						hash.AppendData(Normalize(ms.ToArray()));
					}
				}

				AppendLabel("mod.yaml");
				using (var stream = manifest.Package.GetStream("mod.yaml"))
					if (stream != null)
						AppendStream(stream);

				foreach (var (name, files) in SimulationSections(manifest))
				{
					AppendLabel(name);
					foreach (var file in files)
					{
						// The file list is itself part of the fingerprint: adding, removing or
						// reordering a rules file changes the resolved ruleset even when every
						// individual file is untouched.
						AppendLabel(file);

						if (modData.DefaultFileSystem.TryOpen(file, out var stream))
							using (stream)
								AppendStream(stream);
					}
				}

				return Convert.ToHexString(hash.GetHashAndReset())
					.ToLowerInvariant()[..ContentHashChars];
			}
		}

		/// <summary>
		/// Digests the content packages the mod actually has mounted, to catch the case the
		/// rules hash and the git revision are both blind to: a Red Alert installation under
		/// ^SupportDir that is incomplete, or from a different release, on one machine only.
		/// </summary>
		/// <remarks>
		/// What it covers: which files each mounted package offers, and how many. That is
		/// exactly the observed failure mode - an install missing b2bomb.shp / pip-cloak.shp /
		/// pip-cover.shp, where the sequence clamp turns the absence into a shorter animation
		/// instead of an error.
		/// <para/>
		/// What it deliberately does NOT cover:
		/// <list type="bullet">
		/// <item>File CONTENT. Two installs whose archives hold the same filenames but different
		/// bytes (say, two different Red Alert releases that happen to agree on their file list)
		/// read as identical here. Closing that would mean hashing every byte of every .mix,
		/// tens to hundreds of megabytes, on a code path that runs while a player is waiting on
		/// a join. Filenames are the cheap 90%.</item>
		/// <item>Package NAMES. Folder.Name is the absolute path on disk
		/// (FileSystem/Folder.cs:25), which differs between two machines by construction - one
		/// player's install lives somewhere the other player's does not. Hashing it would make
		/// every pair of installs look different forever, which is worse than useless. The path
		/// is used to FILTER only; only the leaf names INSIDE each package are hashed, and those
		/// are machine-independent.</item>
		/// <item>Anything inside the repository. Repo content is already covered by the engine
		/// segment, and including it actively misleads: mod.yaml mounts ^EngineDir, whose top
		/// level holds the gitignored IP2LOCATION-LITE-DB1.IPV6.BIN.ZIP (engine/.gitignore:20,
		/// written by fetch-geoip.sh). That file is present on any machine that has run a build
		/// and absent on a fresh checkout, so hashing it produced a guaranteed mismatch - and
		/// DescribeDifference would have blamed the third segment, telling both players to
		/// re-extract Red Alert because of a geoip database.</item>
		/// </list>
		/// </remarks>
		static string ComputeAssetDigest(ModData modData)
		{
			using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
			{
				var entries = new List<string>();
				foreach (var package in modData.ModFiles.MountedPackages)
				{
					if (!IsExternalContent(package))
						continue;

					foreach (var file in package.Contents)
						entries.Add(file.ToLowerInvariant());
				}

				// Mount order is not guaranteed to be stable across machines; the set of files
				// is. Case is normalized first because two players may have extracted their
				// content with different tools - MAIN.MIX and main.mix are the same file, and
				// an ordinal sort would otherwise call them different installs.
				entries.Sort(StringComparer.Ordinal);

				hash.AppendData(Encoding.UTF8.GetBytes(entries.Count.ToStringInvariant() + "\n"));
				foreach (var entry in entries)
					hash.AppendData(Encoding.UTF8.GetBytes(entry + "\n"));

				return Convert.ToHexString(hash.GetHashAndReset())
					.ToLowerInvariant()[..ContentHashChars];
			}
		}

		/// <summary>
		/// True for the packages that live outside the repository - the Red Alert installation
		/// under ^SupportDir, which is the only mounted content the engine segment cannot see.
		/// </summary>
		/// <remarks>
		/// Two shapes qualify, both verified against the live mount list:
		/// the content FOLDERS carry a rooted path under Platform.SupportDir
		/// ("...\AppData\Roaming\OpenRA\Content/ra/v2/"), while the .mix ARCHIVES mounted out of
		/// them carry only their leaf name ("conquer.mix") because that is what they were
		/// mounted by. Everything in the repo - ^EngineDir, engine/mods/*, mods/ww3mod/* - is a
		/// rooted path that is not under SupportDir, and is excluded.
		/// </remarks>
		static bool IsExternalContent(IReadOnlyPackage package)
		{
			var name = package.Name;
			if (!Path.IsPathRooted(name))
				return true;

			return Slashes(name).StartsWith(Slashes(Platform.SupportDir), StringComparison.OrdinalIgnoreCase);

			static string Slashes(string path) => path.Replace('\\', '/');
		}

		static IEnumerable<(string Name, string[] Files)> SimulationSections(Manifest manifest)
		{
			yield return ("Rules", manifest.Rules);
			yield return ("Weapons", manifest.Weapons);
			yield return ("Sequences", manifest.Sequences);
			yield return ("TileSets", manifest.TileSets);
		}

		/// <summary>
		/// Strips a UTF-8 BOM and every CR byte before hashing.
		/// </summary>
		/// <remarks>
		/// .gitattributes pins the working tree to LF, but that only binds clones made after it
		/// landed, and a stray editor or a checkout made with core.autocrlf=true will hand back
		/// CRLF. Line endings are introduced by the toolchain rather than the author, so treating
		/// them as a build difference would refuse two players whose yaml is semantically
		/// identical. Nothing else is normalized: a whitespace or comment change IS an edit, and
		/// pretending otherwise would hide a real divergence.
		/// </remarks>
		static byte[] Normalize(byte[] content)
		{
			var start = 0;
			if (content.Length >= 3 && content[0] == 0xEF && content[1] == 0xBB && content[2] == 0xBF)
				start = 3;

			var result = new byte[content.Length - start];
			var length = 0;
			for (var i = start; i < content.Length; i++)
				if (content[i] != (byte)'\r')
					result[length++] = content[i];

			Array.Resize(ref result, length);
			return result;
		}
	}
}
