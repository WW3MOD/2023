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
using OpenRA.FileSystem;
using OpenRA.Primitives;

namespace OpenRA.Graphics
{
	public delegate ISpriteFrame AdjustFrame(ISpriteFrame input, int index, int total);

	public sealed class SpriteCache : IDisposable
	{
		public readonly Dictionary<SheetType, SheetBuilder> SheetBuilders;
		readonly ISpriteLoader[] loaders;
		readonly IReadOnlyFileSystem fileSystem;

		readonly Dictionary<
			int,
			(int[] Frames, MiniYamlNode.SourceLocation Location, AdjustFrame AdjustFrame, bool Premultiplied)> spriteReservations = new();
		readonly Dictionary<string, List<int>> reservationsByFilename = new();

		readonly Dictionary<int, Sprite[]> resolvedSprites = new();

		readonly Dictionary<int, (string Filename, MiniYamlNode.SourceLocation Location)> missingFiles = new();

		Sprite blankSprite;

		int nextReservationToken = 1;

		public SpriteCache(
			IReadOnlyFileSystem fileSystem, ISpriteLoader[] loaders, int bgraSheetSize, int indexedSheetSize, int bgraSheetMargin = 1, int indexedSheetMargin = 1)
		{
			SheetBuilders = new Dictionary<SheetType, SheetBuilder>
			{
				{ SheetType.Indexed, new SheetBuilder(SheetType.Indexed, indexedSheetSize, indexedSheetMargin) },
				{ SheetType.BGRA, new SheetBuilder(SheetType.BGRA, bgraSheetSize, bgraSheetMargin) }
			};

			this.fileSystem = fileSystem;
			this.loaders = loaders;
		}

		public int ReserveSprites(string filename, IEnumerable<int> frames, MiniYamlNode.SourceLocation location,
			AdjustFrame adjustFrame = null, bool premultiplied = false)
		{
			var token = nextReservationToken++;
			spriteReservations[token] = (frames?.ToArray(), location, adjustFrame, premultiplied);
			reservationsByFilename.GetOrAdd(filename, _ => new List<int>()).Add(token);
			return token;
		}

		static ISpriteFrame[] GetFrames(IReadOnlyFileSystem fileSystem, string filename, ISpriteLoader[] loaders)
		{
			if (!fileSystem.TryOpen(filename, out var stream))
				return null;

			using (stream)
			{
				foreach (var loader in loaders)
					if (loader.TryParseSprite(stream, filename, out var frames, out _))
						return frames;

				return null;
			}
		}

		public ISpriteFrame[] LoadFramesUncached(string filename)
		{
			return GetFrames(fileSystem, filename, loaders);
		}

		public void LoadReservations(ModData modData)
		{
			var pendingResolve = new List<(
				string Filename,
				int FrameIndex,
				bool Premultiplied,
				AdjustFrame AdjustFrame,
				ISpriteFrame Frame,
				Sprite[] SpritesForToken)>();
			foreach (var (filename, tokens) in reservationsByFilename)
			{
				modData.LoadScreen?.Display();
				var loadedFrames = GetFrames(fileSystem, filename, loaders);
				foreach (var token in tokens)
				{
					if (spriteReservations.TryGetValue(token, out var rs))
					{
						if (loadedFrames != null)
						{
							var resolved = new Sprite[loadedFrames.Length];
							resolvedSprites[token] = resolved;
							if (rs.Frames != null && rs.Frames.Any(i => i >= loadedFrames.Length))
								throw new InvalidOperationException($"{rs.Location}: {filename} does not contain frames: " +
									string.Join(',', rs.Frames.Where(f => f >= loadedFrames.Length)));

							var frames = rs.Frames ?? Enumerable.Range(0, loadedFrames.Length);
							var total = rs.Frames?.Length ?? loadedFrames.Length;

							var j = 0;
							foreach (var i in frames)
							{
								var frame = loadedFrames[i];
								if (rs.AdjustFrame != null)
									frame = rs.AdjustFrame(frame, j++, total);
								pendingResolve.Add((filename, i, rs.Premultiplied, rs.AdjustFrame, frame, resolved));
							}
						}
						else
						{
							resolvedSprites[token] = null;
							missingFiles[token] = (filename, rs.Location);
						}
					}
				}
			}

			spriteReservations.Clear();
			spriteReservations.TrimExcess();
			reservationsByFilename.Clear();
			reservationsByFilename.TrimExcess();

			// When the sheet builder is adding sprites, it reserves height for the tallest sprite seen along the row.
			// We can achieve better sheet packing by keeping sprites with similar heights together.
			var orderedPendingResolve = pendingResolve.OrderBy(x => x.Frame.Size.Height);

			var spriteCache = new Dictionary<(
				string Filename,
				int FrameIndex,
				bool Premultiplied,
				AdjustFrame AdjustFrame),
				Sprite>(pendingResolve.Count);
			foreach (var (filename, frameIndex, premultiplied, adjustFrame, frame, spritesForToken) in orderedPendingResolve)
			{
				// Premultiplied and non-premultiplied sprites must be cached separately
				// to cover the case where the same image is requested in both versions.
				spritesForToken[frameIndex] = spriteCache.GetOrAdd(
					(filename, frameIndex, premultiplied, adjustFrame),
					_ =>
					{
						var sheetBuilder = SheetBuilders[SheetBuilder.FrameTypeToSheetType(frame.Type)];
						return sheetBuilder.Add(frame, premultiplied);
					});

				modData.LoadScreen?.Display();
			}

			foreach (var sb in SheetBuilders.Values)
				sb.Current.ReleaseBuffer();
		}

		public Sprite[] ResolveSprites(int token)
		{
			if (!resolvedSprites.Remove(token, out var resolved))
				throw new InvalidOperationException($"{nameof(token)} {token} has either already been resolved, or was never reserved via {nameof(ReserveSprites)}");

			resolvedSprites.TrimExcess();

			if (missingFiles.TryGetValue(token, out var r))
			{
				Log.Write("debug", $"Missing sprite file: {r.Location}: {r.Filename} not found");
				return new[] { BlankSprite };
			}

			return resolved;
		}

		// PITFALL: returning instead of throwing is WW3MOD-specific - upstream throws
		// FileNotFoundException here. That divergence keeps an incomplete install bootable, but it
		// used to return an EMPTY array, and every consumer indexes sprites as
		// facings * length + frame without checking. A missing file therefore did not fail at load;
		// it crashed the process on the first frame anything tried to DRAW it, which is invisible to
		// anyone whose own install has the file. One frame keeps the array non-empty so the
		// arithmetic downstream stays in range and the unit simply renders as nothing.
		//
		// A zero Size deliberately takes SheetBuilder.Add's "don't bother allocating empty sprites"
		// path: no sheet space, no buffer write, so this is safe to build lazily even after
		// LoadReservations has released the sheet buffers.
		Sprite BlankSprite => blankSprite ??=
			SheetBuilders[SheetType.BGRA].Add(Array.Empty<byte>(), SpriteFrameType.Bgra32, new Size(0, 0));

		public IEnumerable<(string Filename, MiniYamlNode.SourceLocation Location)> MissingFiles => missingFiles.Values.ToHashSet();

		/// <summary>
		/// Reserved sprite filenames that the file system cannot open, without decoding anything.
		/// </summary>
		/// <remarks>
		/// <para><see cref="MissingFiles"/> only fills in during <see cref="LoadReservations"/>, which decodes
		/// every sprite in the mod and packs it into sheets - far too slow to run from a lint pass, which
		/// is invoked once per tileset and again per map. This reports the same "file not found" class by
		/// asking the file system directly, so the check costs an Exists call per referenced filename.</para>
		///
		/// <para>Only valid BEFORE <see cref="LoadReservations"/>, which clears the reservation tables. That
		/// suits lint, which never loads sprites, and is why this does not simply reuse missingFiles.
		/// Unlike <see cref="MissingFiles"/> this does NOT catch a file that exists but no loader can
		/// parse - that case still surfaces only at load time.</para>
		/// </remarks>
		public IEnumerable<(string Filename, MiniYamlNode.SourceLocation Location)> UnreadableReservedFiles
		{
			get
			{
				foreach (var (filename, tokens) in reservationsByFilename)
				{
					if (fileSystem.Exists(filename))
						continue;

					foreach (var token in tokens)
						if (spriteReservations.TryGetValue(token, out var rs))
							yield return (filename, rs.Location);
				}
			}
		}

		public void Dispose()
		{
			foreach (var sb in SheetBuilders.Values)
				sb.Dispose();
		}
	}
}
