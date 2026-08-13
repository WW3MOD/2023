#region Copyright & License Information
/*
 * WW3MOD missile audit — Phase 0 diagnostic trace for Missile.cs.
 *
 * Off-by-default observation sink. Emits (a) one JSONL record per missile per
 * tick and (b) one summary record per missile when it ends. Exists because a
 * previous missile diagnosis was argued from hand-integrated flight geometry
 * and reached a confidently wrong conclusion — this logs what the missile
 * actually did instead.
 *
 * Active ONLY when either:
 *   (a) TestMode.IsActive AND the Test.MissileTraceLog=<true|path> launch arg, or
 *   (b) a scenario calls Test.EnableMissileTrace(path?) before any missile flies.
 * With neither, Enabled stays false and Missile.cs pays a single static bool
 * test per missile construction and per tick — no allocation, no file, no work.
 *
 * Determinism: reads sim state and writes a file / in-memory list only. Draws
 * no RNG, mutates no actor, trait or projectile field that the simulation
 * reads, and reorders nothing. Same discipline as UnitLifecycleLogger. The
 * per-missile id counter is a static int that no synced code ever reads.
 */
#endregion

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using OpenRA.GameRules;

namespace OpenRA.Mods.Common.Projectiles
{
	// WHICH code path ended the missile. Deliberately one value per individual
	// `shouldExplode` clause rather than a single "expired" bucket: the user
	// reports "plenty of missiles miss and never explode", and collapsing these
	// is exactly what would make that unanswerable.
	public enum MissileEndReason
	{
		None,           // Explode() reached without any classifier firing (should not happen — a bug signal)
		Blocked,        // BlocksProjectiles.AnyBlockingActorsBetween      (Missile.cs, pre-move block check)
		Ground,         // DistanceAboveTerrain(pos) < 0
		CloseEnough,    // relTarDist < CloseEnough (the per-tick point sample)
		FuelOut,        // ExplodeWhenEmpty && distanceCovered > rangeLimit
		OffMap,         // !world.Map.Contains(cell)
		TerrainBound,   // left BoundToTerrainType
		Airburst,       // AirburstAltitude proximity fuse
		SegmentClosest, // the WW3MOD segment closest-approach check (catches Speed > CloseEnough straddle)
		JammedAps,      // JamsMissiles with ActiveProtection shot it down
		Unterminated,   // still aloft when the match ended — never detonated, never removed
	}

	public enum MissileOutcome
	{
		Detonated,   // Explode() ran past the Arm gate and called Weapon.Impact
		DudPreArm,   // Explode() ran but returned at `ticks <= info.Arm` — removed, NO warhead applied
		Unterminated // never reached Explode() at all
	}

	public sealed class MissileVictim
	{
		public string Type;
		public uint ActorId;
		public int Damage;
	}

	// Per-missile state. Doubles as (a) the accumulating summary and (b) the
	// scratch buffer Missile.cs fills with this tick's sample before EmitTick.
	public sealed class MissileTraceRecord
	{
		public int Id;

		// ---- launch context (immutable after Begin) ----
		public string LauncherType;
		public uint LauncherId;
		public int OwnerClientIndex;
		public string Weapon;
		public string TargetType;
		public uint TargetId;
		public WPos LaunchPos;
		public int LaunchAltitude;      // DistanceAboveTerrain at the muzzle
		public WPos LaunchTargetPos;
		public int LaunchRange;         // 3D straight line muzzle → target at launch
		public int LaunchHorRange;
		public int ArmTick;             // first tick at which Explode() would apply a warhead (info.Arm + 1)
		public int RangeLimit;
		public int MaxSpeed;
		public int CloseEnough;

		// Smallest Warhead.AirThreshold across this weapon's warheads, -1 if none is
		// a Mods.Common Warhead. CreateEffectWarhead resolves an impact with
		// DistanceAboveTerrain > AirThreshold to the Air target type
		// (CreateEffectWarhead.cs:156), so a weapon with no air-valid effect warhead
		// renders NO sprite and NO sound when it detonates above this. Recorded per
		// missile so the audit can bucket EndDistanceAboveTerrain against the
		// weapon's real threshold instead of assuming the 128 default.
		public int AirThreshold = -1;

		// ---- accumulated over flight ----
		public int HomingTick = -1;     // tick the Freefall → Homing switch fired, -1 if never
		public int MinDist = int.MaxValue;      // closest 3D approach to targetPosition (segment-exact)
		public int MinDistTick = -1;
		public int MinAimDist = int.MaxValue;   // closest approach to the aim point (targetPosition + lead + offset)
		public int MinAimDistTick = -1;
		public int ExplodeCalls;        // >1 means Explode() ran more than once for this missile

		// The FlyStraightIfMiss latch (Missile.cs:851-853), captured on its first
		// false → true edge with the two distances the predicate compared. This is
		// the whole W1 question: whether the miss-test fires early because
		// minDistanceToTarget was still at its sentinel / a stale large value.
		public int FlyStraightTick = -1;
		public int FlyStraightHorDist = -1;   // relTarHorDist at the latch
		public int FlyStraightMinDist = -1;   // minDistanceToTarget at the latch
		public string FlyStraightState = "";  // missile state at the latch
		public int FlyStraightLatches;        // counts re-latches (the latch can release, Missile.cs:855-856)

		// ---- termination ----
		public MissileEndReason PendingReason = MissileEndReason.None;
		public MissileEndReason EndReason = MissileEndReason.None;
		public MissileOutcome Outcome = MissileOutcome.Unterminated;
		public bool Finished;
		public int EndTick;
		public WPos EndPos;
		public int EndDistanceAboveTerrain;   // DistanceAboveTerrain(pos) at the Explode() that ended it
		public int DamageTotal;

		// Damage attributed to the actor this missile was launched at. Distinct from
		// DamageTotal, which includes splash onto everything else — a warhead that
		// detonates near the target and damages only its neighbours is a MISS, and
		// the range sweep needs to see that as one.
		public int DamageToTarget;
		public bool DamageUnattributed;  // weapon has a Delay > 0 warhead: damage cannot be attributed synchronously
		public readonly List<MissileVictim> Victims = new();

		// ---- this tick's sample, written by Missile.Tick before EmitTick ----
		public int Tick;
		public WPos Pos;
		public WPos TargetPos;
		public WPos AimPos;
		public string State;
		public int HFacing, VFacing, DesiredHFacing, DesiredVFacing;
		public bool AllowPassBy, FlyStraight, LockOn, TargetPassedBy;
		public int RelTarDist, RelTarHorDist, MinDistanceToTargetField;
		public int Speed, LoopRadius, DistanceCovered, DistanceAboveTerrain;
	}

	public static class MissileTrace
	{
		public const int Schema = 1;

		// Hard ceilings so a long tournament run cannot exhaust memory/disk. Both
		// overflow counters are emitted in the closing meta line, so a truncated
		// stream is never silently mistaken for a complete one.
		const int MaxRecords = 50000;
		const int MaxVictimsPerMissile = 16;

		public static bool Enabled { get; private set; }

		// True only for the duration of one Weapon.Impact() call made by a traced
		// missile. Health.InflictDamage tests this to attribute damage. A single
		// static bool read on the damage path when tracing is off.
		public static bool CapturingImpact { get; private set; }

		// When false only summary records are written (per-tick lines suppressed).
		// The distance sweep in a later phase produces thousands of missiles and
		// only needs the summaries.
		public static bool TickRecords { get; private set; } = true;

		static bool initialized;
		static StreamWriter writer;
		static readonly StringBuilder Sb = new();
		static readonly List<MissileTraceRecord> Completed = new();
		static readonly List<MissileTraceRecord> Live = new();
		static readonly Dictionary<WeaponInfo, string> WeaponNames = new();
		static MissileTraceRecord capturing;
		static int nextId;
		static int droppedRecords;
		static int droppedTickLines;
		static bool metaWritten;
		static bool flushed;

		public static IReadOnlyList<MissileTraceRecord> Records => Completed;

		// Called from the Missile constructor. Resolves the launch-arg gate exactly
		// once per process; a scenario that called Enable() first wins.
		public static void EnsureInitialized()
		{
			if (initialized)
				return;

			initialized = true;

			if (!TestMode.IsActive || string.IsNullOrEmpty(TestMode.MissileTraceLogPath))
				return;

			Enable(TestMode.MissileTraceLogPath, TestMode.MissileTraceTicks);
		}

		// path == null/empty keeps records in memory only (Lua-assertion-only use).
		public static void Enable(string path, bool tickRecords = true)
		{
			initialized = true;
			TickRecords = tickRecords;

			// A second Enable (scenario calling it twice) must not orphan the first
			// writer with the file handle still open.
			writer?.Dispose();
			writer = null;
			metaWritten = false;

			if (!string.IsNullOrEmpty(path))
			{
				try
				{
					var dir = Path.GetDirectoryName(path);
					if (!string.IsNullOrEmpty(dir))
						Directory.CreateDirectory(dir);

					// AutoFlush off; flushed after every summary record and on quit, so a
					// timeout-kill loses at most the in-flight missiles' tick lines.
					writer = new StreamWriter(path, append: false) { AutoFlush = false };
				}
				catch (Exception e)
				{
					Log.Write("debug", $"[MissileTrace] file disabled — could not open '{path}': {e.Message}");
					writer = null;
				}
			}

			if (!Enabled)
			{
				Enabled = true;
				Game.OnQuit += Flush;
			}

			Log.Write("debug", $"[MissileTrace] active — ticks={TickRecords} file={(writer != null ? path : "(memory only)")}");
		}

		public static MissileTraceRecord Begin(World world, ProjectileArgs args, int armTick, int rangeLimit, int maxSpeed, int closeEnough)
		{
			WriteMeta(world);

			var target = args.GuidedTarget.Actor;
			var toTarget = args.PassiveTarget - args.Source;

			var rec = new MissileTraceRecord
			{
				Id = ++nextId,
				LauncherType = args.SourceActor?.Info.Name ?? "",
				LauncherId = args.SourceActor?.ActorID ?? 0,
				OwnerClientIndex = args.SourceActor?.Owner?.ClientIndex ?? -1,
				Weapon = WeaponName(world, args.Weapon),
				TargetType = target?.Info.Name ?? "",
				TargetId = target?.ActorID ?? 0,
				LaunchPos = args.Source,
				LaunchAltitude = world.Map.DistanceAboveTerrain(args.Source).Length,
				LaunchTargetPos = args.PassiveTarget,
				LaunchRange = toTarget.Length,
				LaunchHorRange = toTarget.HorizontalLength,
				ArmTick = armTick,
				RangeLimit = rangeLimit,
				MaxSpeed = maxSpeed,
				CloseEnough = closeEnough,
			};

			foreach (var wh in args.Weapon.Warheads)
			{
				// Warheads that defer via AddFrameEndTask land outside the synchronous
				// Impact() window, so their damage cannot be attributed to this missile.
				// Flagged rather than silently reported as zero.
				if (wh.Delay > 0)
					rec.DamageUnattributed = true;

				if (wh is Warheads.Warhead w && (rec.AirThreshold < 0 || w.AirThreshold.Length < rec.AirThreshold))
					rec.AirThreshold = w.AirThreshold.Length;
			}

			Live.Add(rec);
			return rec;
		}

		public static void EmitTick(MissileTraceRecord rec)
		{
			if (!TickRecords || writer == null)
				return;

			if (Completed.Count + Live.Count > MaxRecords)
			{
				droppedTickLines++;
				return;
			}

			Sb.Clear();
			Sb.Append($"{{\"ev\":\"t\",\"id\":{rec.Id},\"tk\":{rec.Tick}");
			Pos("p", rec.Pos);
			Pos("tgt", rec.TargetPos);
			Pos("aim", rec.AimPos);
			Str("st", rec.State);
			Num("hf", rec.HFacing);
			Num("vf", rec.VFacing);
			Num("dhf", rec.DesiredHFacing);
			Num("dvf", rec.DesiredVFacing);
			Num("apb", rec.AllowPassBy ? 1 : 0);
			Num("fs", rec.FlyStraight ? 1 : 0);
			Num("lock", rec.LockOn ? 1 : 0);
			Num("tpb", rec.TargetPassedBy ? 1 : 0);
			Num("rtd", rec.RelTarDist);
			Num("rthd", rec.RelTarHorDist);
			Num("mdt", rec.MinDistanceToTargetField);
			Num("spd", rec.Speed);
			Num("lr", rec.LoopRadius);
			Num("dc", rec.DistanceCovered);
			Num("rl", rec.RangeLimit);
			Num("dat", rec.DistanceAboveTerrain);
			Sb.Append('}');
			writer.WriteLine(Sb.ToString());
		}

		// Idempotent: the jammed-APS Explode() at the top of HomingTick does not
		// return, so Missile.Tick can reach a second Explode() in the same tick.
		// The first termination is the true one; the repeat is counted, not merged.
		public static void Finish(MissileTraceRecord rec, int tick, WPos endPos, int endDistanceAboveTerrain, bool armed)
		{
			if (rec.Finished)
				return;

			rec.Finished = true;
			rec.EndTick = tick;
			rec.EndPos = endPos;
			rec.EndDistanceAboveTerrain = endDistanceAboveTerrain;
			rec.EndReason = rec.PendingReason;
			rec.Outcome = armed ? MissileOutcome.Detonated : MissileOutcome.DudPreArm;
			Complete(rec);
		}

		public static void BeginImpact(MissileTraceRecord rec)
		{
			capturing = rec;
			CapturingImpact = true;
		}

		public static void EndImpact()
		{
			capturing = null;
			CapturingImpact = false;
		}

		// Called from Health.InflictDamage while a traced missile's warheads run.
		// `damage` is the post-modifier nominal value, not the HP actually removed
		// (an overkill hit on a nearly-dead actor reports the nominal figure).
		public static void NoteDamage(Actor victim, int damage)
		{
			var rec = capturing;
			if (rec == null || damage <= 0)
				return;

			rec.DamageTotal += damage;
			if (victim.ActorID == rec.TargetId)
				rec.DamageToTarget += damage;

			foreach (var v in rec.Victims)
			{
				if (v.ActorId == victim.ActorID)
				{
					v.Damage += damage;
					return;
				}
			}

			if (rec.Victims.Count < MaxVictimsPerMissile)
				rec.Victims.Add(new MissileVictim { Type = victim.Info.Name, ActorId = victim.ActorID, Damage = damage });
		}

		static void Complete(MissileTraceRecord rec)
		{
			Live.Remove(rec);

			if (Completed.Count < MaxRecords)
				Completed.Add(rec);
			else
				droppedRecords++;

			WriteSummary(rec);
			writer?.Flush();
		}

		static void WriteSummary(MissileTraceRecord rec)
		{
			if (writer == null)
				return;

			Sb.Clear();
			Sb.Append($"{{\"ev\":\"m\",\"id\":{rec.Id}");
			Str("launcher", rec.LauncherType);
			Num("launcher_id", rec.LauncherId);
			Num("owner", rec.OwnerClientIndex);
			Str("weapon", rec.Weapon);
			Str("target", rec.TargetType);
			Num("target_id", rec.TargetId);
			Pos("launch_pos", rec.LaunchPos);
			Num("launch_alt", rec.LaunchAltitude);
			Pos("launch_tgt", rec.LaunchTargetPos);
			Num("launch_range", rec.LaunchRange);
			Num("launch_hor_range", rec.LaunchHorRange);
			Num("homing_tick", rec.HomingTick);
			Num("arm_tick", rec.ArmTick);
			Num("range_limit", rec.RangeLimit);
			Num("max_speed", rec.MaxSpeed);
			Num("close_enough", rec.CloseEnough);
			Num("min_dist", rec.MinDist == int.MaxValue ? -1 : rec.MinDist);
			Num("min_dist_tick", rec.MinDistTick);
			Num("min_aim_dist", rec.MinAimDist == int.MaxValue ? -1 : rec.MinAimDist);
			Num("min_aim_dist_tick", rec.MinAimDistTick);
			Num("flystraight_tick", rec.FlyStraightTick);
			Num("flystraight_hor_dist", rec.FlyStraightHorDist);
			Num("flystraight_min_dist", rec.FlyStraightMinDist);
			Str("flystraight_state", rec.FlyStraightState);
			Num("flystraight_latches", rec.FlyStraightLatches);
			Num("end_tick", rec.EndTick);
			Pos("end_pos", rec.EndPos);
			Num("end_dat", rec.EndDistanceAboveTerrain);
			Str("end_dat_bucket", DatBucket(rec.EndDistanceAboveTerrain, rec.AirThreshold));
			Num("air_threshold", rec.AirThreshold);
			Str("reason", ReasonName(rec.EndReason));
			Str("outcome", OutcomeName(rec.Outcome));
			Num("armed", rec.Outcome == MissileOutcome.Detonated ? 1 : 0);
			Num("explode_calls", rec.ExplodeCalls);
			Num("distance_covered", rec.DistanceCovered);
			Num("damage", rec.DamageTotal);
			Num("damage_to_target", rec.DamageToTarget);
			Num("damage_unattributed", rec.DamageUnattributed ? 1 : 0);

			Sb.Append(",\"victims\":[");
			for (var i = 0; i < rec.Victims.Count; i++)
			{
				if (i > 0)
					Sb.Append(',');

				var v = rec.Victims[i];
				Sb.Append($"{{\"type\":\"{Escape(v.Type)}\",\"aid\":{v.ActorId},\"dmg\":{v.Damage}}}");
			}

			Sb.Append("]}");
			writer.WriteLine(Sb.ToString());
		}

		static void WriteMeta(World world)
		{
			if (metaWritten || writer == null)
				return;

			metaWritten = true;
			writer.WriteLine($"{{\"ev\":\"meta\",\"schema\":{Schema},\"scenario\":\"{Escape(TestMode.Name ?? "")}\""
				+ $",\"seed\":{world.LobbyInfo.GlobalSettings.RandomSeed},\"timestep\":{world.Timestep}"
				+ $",\"ticks\":{(TickRecords ? 1 : 0)}}}");
		}

		// End-of-run: every missile still aloft is a real outcome — it neither
		// detonated nor was removed — so it gets a record rather than vanishing.
		// Built entirely from the held records (no world reads), so it is valid
		// after teardown, exactly like UnitLifecycleLogger's census.
		static void Flush()
		{
			if (flushed)
				return;

			flushed = true;
			Game.OnQuit -= Flush;

			try
			{
				for (var i = Live.Count - 1; i >= 0; i--)
				{
					var rec = Live[i];
					rec.Finished = true;
					rec.EndReason = MissileEndReason.Unterminated;
					rec.Outcome = MissileOutcome.Unterminated;
					rec.EndTick = rec.Tick;
					rec.EndPos = rec.Pos;

					// No Explode() ran, so there is no impact altitude — carry the last
					// sampled one, which is the altitude it was still flying at.
					rec.EndDistanceAboveTerrain = rec.DistanceAboveTerrain;
					Complete(rec);
				}

				if (writer != null)
				{
					writer.WriteLine($"{{\"ev\":\"end\",\"records\":{Completed.Count}"
						+ $",\"dropped_records\":{droppedRecords},\"dropped_tick_lines\":{droppedTickLines}}}");
					writer.Flush();
					writer.Dispose();
					writer = null;
				}
			}
			catch (Exception e)
			{
				Log.Write("debug", $"[MissileTrace] flush failed: {e.Message}");
			}
		}

		// WeaponInfo carries no name, so recover it by reverse lookup against the
		// ruleset once per distinct weapon. Read-only and trace-gated.
		static string WeaponName(World world, WeaponInfo weapon)
		{
			if (weapon == null)
				return "";

			if (WeaponNames.TryGetValue(weapon, out var name))
				return name;

			name = "";
			foreach (var kv in world.Map.Rules.Weapons)
			{
				if (kv.Value == weapon)
				{
					name = kv.Key;
					break;
				}
			}

			WeaponNames[weapon] = name;
			return name;
		}

		public static string ReasonName(MissileEndReason r)
		{
			switch (r)
			{
				case MissileEndReason.Blocked: return "blocked";
				case MissileEndReason.Ground: return "ground";
				case MissileEndReason.CloseEnough: return "close_enough";
				case MissileEndReason.FuelOut: return "fuel_out";
				case MissileEndReason.OffMap: return "off_map";
				case MissileEndReason.TerrainBound: return "terrain_bound";
				case MissileEndReason.Airburst: return "airburst";
				case MissileEndReason.SegmentClosest: return "segment_closest";
				case MissileEndReason.JammedAps: return "jammed_aps";
				case MissileEndReason.Unterminated: return "unterminated";
				default: return "none";
			}
		}

		// Buckets the impact altitude the way the warhead layer reads it.
		// CreateEffectWarhead.cs:156 uses a STRICT `dat > AirThreshold`, so an impact
		// exactly at the threshold is still ground. "air" is the bucket where a
		// weapon with no air-valid effect warhead detonates silently and invisibly.
		public static string DatBucket(int dat, int airThreshold)
		{
			if (dat < 0)
				return "subterrain";

			var threshold = airThreshold >= 0 ? airThreshold : 128;
			return dat > threshold ? "air" : "ground";
		}

		public static string OutcomeName(MissileOutcome o)
		{
			switch (o)
			{
				case MissileOutcome.Detonated: return "detonated";
				case MissileOutcome.DudPreArm: return "dud_prearm";
				default: return "unterminated";
			}
		}

		static void Num(string key, long value) { Sb.Append($",\"{key}\":{value}"); }
		static void Str(string key, string value) { Sb.Append($",\"{key}\":\"{Escape(value)}\""); }
		static void Pos(string key, WPos p) { Sb.Append($",\"{key}\":[{p.X},{p.Y},{p.Z}]"); }

		static string Escape(string s)
		{
			if (string.IsNullOrEmpty(s))
				return "";

			return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");
		}
	}
}
