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
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace OpenRA.Graphics
{
	/// <summary>
	/// Tallies sequences that could not be loaded as written and were silently degraded.
	/// </summary>
	/// <remarks>
	/// WW3MOD's sprite sequence loader deliberately clamps out-of-range frame indices and
	/// carries on where upstream OpenRA throws, so that an incomplete Red Alert installation
	/// still boots. The cost of that leniency is that a missing sprite stops being an error and
	/// becomes a QUIETER wrong answer: the sequence just gets shorter. One debug-log line per
	/// broken sequence is easy to miss among thousands, and two players comparing installs have
	/// no way to see at a glance that they disagree.
	/// <para/>
	/// This turns that scatter into one number and one digest, cheap enough to print in the
	/// sync report header. If two players' reports disagree here, their installed game content
	/// differs - which is checkable in seconds and is otherwise invisible, because both machines
	/// can be on the same commit with the same freshly built binaries.
	/// <para/>
	/// It records that content DIVERGED, not that it caused a desync. Animation length feeding
	/// simulation state is a plausible route (an animation-completion callback firing on a
	/// different tick), not a proven one.
	/// </remarks>
	public static class SequenceIntegrity
	{
		static readonly object SyncObject = new();
		static readonly SortedSet<string> DegradedSequences = new(StringComparer.Ordinal);

		/// <summary>Number of distinct sequences that loaded with missing or clamped frames.</summary>
		public static int DegradedCount
		{
			get { lock (SyncObject) return DegradedSequences.Count; }
		}

		public static void RecordDegraded(string image, string sequence, string detail)
		{
			lock (SyncObject)
				DegradedSequences.Add($"{image}.{sequence}: {detail}");
		}

		/// <summary>
		/// A short hash over every degraded sequence and how it was degraded. Two installs that
		/// are missing the same content produce the same digest; two that differ do not.
		/// </summary>
		public static string Digest()
		{
			lock (SyncObject)
			{
				if (DegradedSequences.Count == 0)
					return "none";

				using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
				{
					foreach (var entry in DegradedSequences)
						hash.AppendData(Encoding.UTF8.GetBytes(entry + "\n"));

					return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant()[..8];
				}
			}
		}

		/// <summary>One line fit for a log header.</summary>
		public static string Summary()
		{
			lock (SyncObject)
			{
				if (DegradedSequences.Count == 0)
					return "Degraded sequences: none (all sprite frames resolved)";

				return $"Degraded sequences: {DegradedSequences.Count} (digest {Digest()}) " +
					$"- game content is incomplete, e.g. {DegradedSequences.Take(3).JoinWith("; ")}";
			}
		}
	}
}
