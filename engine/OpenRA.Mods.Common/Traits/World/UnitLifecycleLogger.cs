#region Copyright & License Information
/*
 * WW3MOD behavior-lint pipeline — unit lifecycle logger (world trait).
 *
 * Off-by-default diagnostic. Emits a per-unit JSONL event stream during a
 * test/tournament run so the Python analyzer (tools/behavior-lint/) can flag
 * anti-patterns (idle units, call-in-and-forget, units that die untasked)
 * from logs instead of a human watching a match.
 *
 * Active ONLY when both:
 *   (a) TestMode.IsActive              (launched with Test.Mode=true), AND
 *   (b) TestMode.UnitLifecycleLogPath  (Test.UnitLifecycleLog=<true|path>).
 * Without both the trait no-ops: no file, no subscriptions, no per-tick work —
 * so its unconditional presence in world.yaml never changes normal play.
 *
 * First slice (WORKSPACE/behavior-lint-spec.md §2d): emits meta, spawn, order,
 * idle_start/idle_end (edge-triggered), death (minimal — no attacker), and an
 * end-of-game census. No `sample`, no `damage`, no INotifyKilled companion tap.
 *
 * Determinism: reads sim state and writes a file only. Draws no RNG, issues no
 * orders, mutates no actor/player/trait state — byte-identical to a non-logged
 * run (same discipline BotVsBotMatchWatcher documents). Iteration order over
 * world.Actors / the tracked dictionary can only change log line order, never a
 * simulation decision.
 */
#endregion

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	// Pure idle-span bookkeeping: folds a stream of idle start/stop transitions into
	// running total-idle and longest-single-span figures, and can snapshot both as
	// if an open span were closed at a given tick (the end-of-game census case).
	// Extracted from the logger so the edge cases (open span at exit, out-of-order
	// or negative durations) are unit-testable without a World — see
	// engine/OpenRA.Test/OpenRA.Mods.Common/IdleSpanMathTest.cs.
	public struct IdleSpanAccumulator
	{
		int startTick;

		public bool Idle { get; private set; }
		public int TotalIdle { get; private set; }
		public int LongestIdle { get; private set; }

		// Begin an idle span at tick. No-op if already idle (idempotent edge).
		public void Start(int tick)
		{
			if (Idle)
				return;
			Idle = true;
			startTick = tick;
		}

		// Close the open span at tick, folding it into the totals. Returns the span
		// duration (0 if not idle or the tick precedes the start).
		public int End(int tick)
		{
			if (!Idle)
				return 0;
			Idle = false;
			var dur = tick - startTick;
			if (dur < 0)
				dur = 0;
			TotalIdle += dur;
			if (dur > LongestIdle)
				LongestIdle = dur;
			return dur;
		}

		// Totals as if an open span were closed at closeTick, WITHOUT mutating — used
		// for the end census so a survivor still idle at match end is counted.
		public (int Total, int Longest) Snapshot(int closeTick)
		{
			var total = TotalIdle;
			var longest = LongestIdle;
			if (Idle)
			{
				var dur = closeTick - startTick;
				if (dur < 0)
					dur = 0;
				total += dur;
				if (dur > longest)
					longest = dur;
			}

			return (total, longest);
		}
	}

	[TraitLocation(SystemActors.World)]
	[Desc("Behavior-lint unit lifecycle logger. Active only when both Test.Mode=true and Test.UnitLifecycleLog=<true|path> launch args are present.",
		"Emits a per-unit JSONL event stream (meta/spawn/order/idle/death/end) for the tools/behavior-lint analyzer. Observation-only — never affects the simulation.")]
	public class UnitLifecycleLoggerInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) { return new UnitLifecycleLogger(this); }
	}

	public class UnitLifecycleLogger : IWorldLoaded, ITick
	{
		// Per-unit running state. Held entirely by this trait (no sim coupling) so
		// the end-of-game census can be flushed from Game.OnQuit after the world
		// has been torn down.
		sealed class UnitTrack
		{
			public Actor Actor;
			public string Type;
			public int Owner;
			public int OrderCount;
			public IdleSpanAccumulator Idle;
			public CPos LastCell;
			public string LastTerr = "unknown";
		}

		World world;
		bool enabled;
		string path;
		StreamWriter writer;

		bool seeded;
		bool flushed;

		readonly Dictionary<uint, UnitTrack> tracks = new();
		readonly StringBuilder sb = new();

		// Per-(owner, tick) influence-grid cache so repeated terr lookups on the
		// same tick (many idle edges can fire together) reuse one allocation.
		InfluenceMap influence;
		Player terrCacheOwner;
		int terrCacheTick = -1;
		int[,] terrCacheEnemy;
		int[,] terrCacheFriendly;

		public UnitLifecycleLogger(UnitLifecycleLoggerInfo info) { }

		public bool Enabled => enabled;

		void IWorldLoaded.WorldLoaded(World w, OpenRA.Graphics.WorldRenderer wr)
		{
			if (!TestMode.IsActive || string.IsNullOrEmpty(TestMode.UnitLifecycleLogPath))
				return;

			world = w;
			path = TestMode.UnitLifecycleLogPath;

			try
			{
				var dir = Path.GetDirectoryName(path);
				if (!string.IsNullOrEmpty(dir))
					Directory.CreateDirectory(dir);

				// Truncate on open (one file per match). AutoFlush off — flushed once
				// per tick in ITick and finally in Flush(), so a timeout-kill loses at
				// most the current tick's lines rather than the whole stream.
				writer = new StreamWriter(path, append: false) { AutoFlush = false };
				enabled = true;
			}
			catch (Exception e)
			{
				Log.Write("debug", $"[UnitLifecycleLogger] disabled — could not open '{path}': {e.Message}");
				enabled = false;
				return;
			}

			influence = w.WorldActor.TraitOrDefault<InfluenceMap>();

			// Flush the end-of-game census when the process quits. Built purely from
			// the tracked dictionary (no world reads), so it is safe even though
			// Game.OnQuit fires after the world/renderer have been disposed.
			Game.OnQuit += Flush;

			Log.Write("debug", $"[UnitLifecycleLogger] active — writing {path}");
		}

		void ITick.Tick(Actor self)
		{
			if (!enabled)
				return;

			EnsureSeeded();

			var tick = world.WorldTick;

			// Edge-triggered idle detection over live tracked actors. Also keeps
			// LastCell fresh so the end census reflects final positions.
			foreach (var tr in tracks.Values)
			{
				var a = tr.Actor;
				if (a == null || !a.IsInWorld || a.IsDead)
					continue;

				tr.LastCell = a.Location;

				var idleNow = a.IsIdle;
				if (idleNow && !tr.Idle.Idle)
				{
					tr.Idle.Start(tick);
					var terr = ClassifyTerritory(a);
					tr.LastTerr = terr;
					BeginLine("idle_start", a.ActorID);
					Field("x", a.Location.X);
					Field("y", a.Location.Y);
					Field("terr", terr);
					EndLine();
				}
				else if (!idleNow && tr.Idle.Idle)
				{
					var dur = tr.Idle.End(tick);
					BeginLine("idle_end", a.ActorID);
					Field("x", a.Location.X);
					Field("y", a.Location.Y);
					Field("dur", dur);
					EndLine();
				}
			}

			writer.Flush();
		}

		// Seed on the first tick or the first order (whichever comes first) so the
		// meta line and initial spawns precede any order line. Deferred like the
		// watcher's SR discovery because IWorldLoaded fires before SpawnMapActors.
		void EnsureSeeded()
		{
			if (seeded)
				return;

			seeded = true;

			world.ActorAdded += OnActorAdded;
			world.ActorRemoved += OnActorRemoved;

			EmitMeta();

			foreach (var a in world.Actors)
				if (IsInteresting(a))
					Track(a);
		}

		void EmitMeta()
		{
			sb.Clear();
			sb.Append("{\"ev\":\"meta\",\"schema\":1");
			AppendKV("scenario", TestMode.Name ?? "");
			sb.Append($",\"seed\":{world.LobbyInfo.GlobalSettings.RandomSeed}");
			sb.Append($",\"timestep\":{world.Timestep}");
			sb.Append(",\"players\":[");
			var first = true;
			foreach (var p in world.Players)
			{
				if (p.NonCombatant)
					continue;

				if (!first)
					sb.Append(',');
				first = false;
				sb.Append($"{{\"ci\":{p.ClientIndex}");
				AppendKV("bot_type", p.BotType ?? "");
				AppendKV("faction", p.Faction?.InternalName ?? "");
				sb.Append('}');
			}

			sb.Append("]}");
			writer.WriteLine(sb.ToString());
		}

		static bool IsInteresting(Actor a)
		{
			if (a.Owner == null || a.Owner.NonCombatant)
				return false;

			// Real units the composition telemetry counts: anything positionable
			// (mobile/aircraft) or explicitly tracked by UpdatesPlayerStatistics —
			// excludes projectiles, effects, smudges, and system actors.
			return a.Info.HasTraitInfo<IPositionableInfo>()
				|| a.Info.HasTraitInfo<UpdatesPlayerStatisticsInfo>();
		}

		void Track(Actor a)
		{
			if (tracks.ContainsKey(a.ActorID))
				return;

			var cost = a.Info.TraitInfoOrDefault<ValuedInfo>()?.Cost ?? 0;

			// Movers (Mobile/Aircraft) implement IPositionableInfo; structures are
			// caught by IsInteresting only via UpdatesPlayerStatisticsInfo and lack
			// it. The analyzer keys "forgotten unit" rules (R1/R2/R6) off this so a
			// stationary structure being idle/untasked isn't flagged as a pathology.
			var mobile = a.Info.HasTraitInfo<IPositionableInfo>();

			tracks[a.ActorID] = new UnitTrack
			{
				Actor = a,
				Type = a.Info.Name,
				Owner = a.Owner.ClientIndex,
				LastCell = a.Location,
			};

			BeginLine("spawn", a.ActorID);
			Field("type", a.Info.Name);
			Field("owner", a.Owner.ClientIndex);
			Field("x", a.Location.X);
			Field("y", a.Location.Y);
			Field("cost", cost);
			Field("mobile", mobile ? 1 : 0);
			EndLine();
		}

		void OnActorAdded(Actor a)
		{
			if (!enabled || !IsInteresting(a))
				return;

			Track(a);
		}

		void OnActorRemoved(Actor a)
		{
			if (!enabled || !tracks.TryGetValue(a.ActorID, out var tr))
				return;

			// Close any open idle span so totals stay consistent with the analyzer.
			tr.Idle.End(world.WorldTick);

			var terr = ClassifyTerritory(a);
			BeginLine("death", a.ActorID);
			Field("x", a.Location.X);
			Field("y", a.Location.Y);
			Field("orders", tr.OrderCount);
			Field("terr", terr);
			EndLine();

			tracks.Remove(a.ActorID);
		}

		// Called from ModularBot.QueueOrder for every bot-issued order. Guarded so
		// it costs nothing (not even the seed) when logging is off.
		public void LogOrder(Player owner, string moduleTag, Order order)
		{
			if (!enabled)
				return;

			EnsureSeeded();

			long subj = -1;
			if (order.Subject != null)
			{
				subj = order.Subject.ActorID;
				if (tracks.TryGetValue(order.Subject.ActorID, out var tr))
				{
					tr.OrderCount++;
				}
			}

			long tx = -1, ty = -1, tactor = -1;
			var target = order.Target;
			switch (target.Type)
			{
				case TargetType.Actor:
					if (target.Actor != null)
					{
						tx = target.Actor.Location.X;
						ty = target.Actor.Location.Y;
						tactor = target.Actor.ActorID;
					}

					break;
				case TargetType.FrozenActor:
				case TargetType.Terrain:
					var cell = world.Map.CellContaining(target.CenterPosition);
					tx = cell.X;
					ty = cell.Y;
					break;
			}

			BeginLine("order", 0, includeAid: false);
			Field("owner", owner?.ClientIndex ?? -1);
			Field("mod", moduleTag ?? "");
			Field("ord", order.OrderString ?? "");
			Field("subj", subj);
			Field("tx", tx);
			Field("ty", ty);
			Field("tactor", tactor);
			Field("queued", order.Queued ? 1 : 0);
			EndLine();
		}

		// Called from ModularBot on a tick-stamped window: an AGGREGATE of funnel-gate suppressions,
		// one line per (issuing module, reason) pair per window rather than one per suppression, so the
		// stream stays bounded while still answering "how much churn did the gate remove, and whose".
		public void LogOrderGate(Player owner, string moduleTag, string reason, int count, int standing)
		{
			if (!enabled)
				return;

			EnsureSeeded();

			BeginLine("ordgate", 0, includeAid: false);
			Field("owner", owner?.ClientIndex ?? -1);
			Field("mod", moduleTag ?? "");
			Field("reason", reason ?? "");
			Field("count", count);
			Field("standing", standing);
			EndLine();
		}

		// End-of-game census: one `end` line per surviving tracked actor, built from
		// the tracked dictionary alone (no world reads) so it is valid post-teardown.
		void Flush()
		{
			if (!enabled || flushed)
				return;

			flushed = true;
			Game.OnQuit -= Flush;

			try
			{
				foreach (var tr in tracks.Values)
				{
					var (totalIdle, longestIdle) = tr.Idle.Snapshot(world.WorldTick);

					BeginLine("end", tr.Actor?.ActorID ?? 0);
					Field("type", tr.Type);
					Field("owner", tr.Owner);
					Field("x", tr.LastCell.X);
					Field("y", tr.LastCell.Y);
					Field("idle", tr.Idle.Idle ? 1 : 0);
					Field("terr", tr.LastTerr);
					Field("orders", tr.OrderCount);
					Field("total_idle", totalIdle);
					Field("longest_idle", longestIdle);
					EndLine();
				}

				writer.Flush();
				writer.Dispose();
				writer = null;
			}
			catch (Exception e)
			{
				Log.Write("debug", $"[UnitLifecycleLogger] flush failed: {e.Message}");
			}
		}

		// Omniscient territory classifier (spec §1.5) — valid at log time because the
		// logger is diagnostics, not simulation. Reads InfluenceMap's fog-free
		// friendly/enemy grids at the actor's cell. Returns "unknown" if the map is
		// absent so the analyzer degrades gracefully.
		string ClassifyTerritory(Actor a)
		{
			if (influence == null || a.Owner == null)
				return "unknown";

			if (terrCacheTick != world.WorldTick || terrCacheOwner != a.Owner)
			{
				terrCacheTick = world.WorldTick;
				terrCacheOwner = a.Owner;
				terrCacheEnemy = influence.GetEnemyInfluence(a.Owner);
				terrCacheFriendly = influence.GetFriendlyInfluence(a.Owner);
			}

			var (gx, gy) = influence.MapCellToGridCell(a.Location);
			if (gx < 0 || gy < 0 || gx >= influence.GridWidth || gy >= influence.GridHeight)
				return "neutral";

			var enemy = terrCacheEnemy[gx, gy];
			var friendly = terrCacheFriendly[gx, gy];

			if (enemy == 0 && friendly == 0)
				return "neutral";
			if (friendly > 0 && enemy == 0)
				return "own";
			if (enemy > 0 && friendly == 0)
				return "enemy";
			return "contested";
		}

		// ---- compact JSONL writers (manual, no System.Text.Json dependency) ----

		void BeginLine(string ev, uint aid, bool includeAid = true)
		{
			sb.Clear();
			sb.Append($"{{\"t\":{world.WorldTick},\"ev\":\"{ev}\"");
			if (includeAid)
				sb.Append($",\"aid\":{aid}");
		}

		void EndLine()
		{
			sb.Append('}');
			writer.WriteLine(sb.ToString());
		}

		void Field(string key, long value)
		{
			sb.Append($",\"{key}\":{value}");
		}

		void Field(string key, string value)
		{
			sb.Append($",\"{key}\":\"{Escape(value)}\"");
		}

		void AppendKV(string key, string value)
		{
			sb.Append($",\"{key}\":\"{Escape(value)}\"");
		}

		static string Escape(string s)
		{
			if (string.IsNullOrEmpty(s))
				return "";
			return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");
		}
	}
}
