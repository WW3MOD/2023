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
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using OpenRA.Primitives;

namespace OpenRA.Network
{
	sealed class SyncReport
	{
		// Upstream keeps 7. Raised because 7 net frames is roughly half a second: any stall
		// longer than that between the desync and the dump - an alt-tab, a GC pause, a
		// disk hiccup - rotates the offending frame out of the ring, and the report then reads
		// "Recorded frames do not contain the frame N", which is indistinguishable from the
		// empty-report failure this whole branch exists to remove. Cost is memory only, and
		// only in games with two or more humans (see OrderManager.StartGame).
		const int NumSyncReports = 32;
		static readonly Cache<Type, TypeInfo> TypeInfoCache = new(t => new TypeInfo(t));

		readonly OrderManager orderManager;

		readonly Report[] syncReports = new Report[NumSyncReports];
		int curIndex = 0;

		static (string[] Names, Values Values) DumpSyncTrait(ISync sync)
		{
			var type = sync.GetType();
			TypeInfo typeInfo;
			lock (TypeInfoCache)
				typeInfo = TypeInfoCache[type];
			var values = new Values(typeInfo.Names.Length);
			var index = 0;

			foreach (var func in typeInfo.SerializableCopyOfMemberFunctions)
				values[index++] = func(sync);

			return (typeInfo.Names, values);
		}

		public SyncReport(OrderManager orderManager)
		{
			this.orderManager = orderManager;
			for (var i = 0; i < NumSyncReports; i++)
				syncReports[i] = new Report();
		}

		internal void UpdateSyncReport(IEnumerable<OrderManager.ClientOrder> orders)
		{
			GenerateSyncReport(syncReports[curIndex], orders);
			curIndex = ++curIndex % NumSyncReports;
		}

		void GenerateSyncReport(Report report, IEnumerable<OrderManager.ClientOrder> orders)
		{
			report.Frame = orderManager.NetFrameNumber;
			report.SyncedRandom = orderManager.World.SharedRandom.Last;
			report.TotalCount = orderManager.World.SharedRandom.TotalCount;
			report.Traits.Clear();
			report.Effects.Clear();
			report.Orders.Clear();
			report.Orders.AddRange(orders);

			foreach (var actor in orderManager.World.ActorsHavingTrait<ISync>())
			{
				foreach (var syncHash in actor.SyncHashes)
				{
					var hash = syncHash.Hash();
					if (hash != 0)
					{
						report.Traits.Add(new TraitReport()
						{
							ActorID = actor.ActorID,
							Type = actor.Info.Name,
							Owner = (actor.Owner == null) ? "null" : actor.Owner.PlayerName,
							Trait = syncHash.Trait.GetType().Name,
							Hash = hash,
							NamesValues = DumpSyncTrait(syncHash.Trait)
						});
					}
				}
			}

			foreach (var sync in orderManager.World.SyncedEffects)
			{
				var hash = Sync.Hash(sync);
				if (hash != 0)
				{
					report.Effects.Add(new EffectReport()
					{
						Name = sync.GetType().Name,
						Hash = hash,
						NamesValues = DumpSyncTrait(sync)
					});
				}
			}
		}

		/// <summary>
		/// Writes the desync report and returns its absolute path, or null when no report could be
		/// produced for that frame. Null is the signal the desync dialog needs: pointing a player at a
		/// file whose entire content is "No sync report available!" wastes the one chance to collect
		/// evidence, so the dialog says the report is missing instead of naming a useless file.
		/// </summary>
		internal string DumpSyncReport(int frame)
		{
			var timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHHmmssZ", CultureInfo.InvariantCulture);

			var reportName = $"syncreport-{timestamp}-{orderManager.LocalClient?.Index}.log";

			// PITFALL: Log.AddChannel returns early when the channel already exists
			// (Support/Log.cs:147-148) - it does NOT repoint an existing channel at a new file.
			// A fixed "sync" channel therefore made every desync after the first append to the
			// FIRST game's report, so the filename's timestamp lied and two reports sat
			// interleaved in one file. That is not hypothetical: desyncing twice without
			// restarting the game is the normal way this gets hit. The channel name has to be
			// unique per dump for the file name to mean anything.
			var channel = $"sync-{timestamp}-{frame}";
			return WriteReport(channel, reportName, frame) ? Log.ChannelFilename(channel) : null;
		}

		// DIAGNOSTIC ONLY (Test.ForceSyncReports) — never reached in normal play.
		// A saved-game restore is validated by a single sync-hash comparison, so the dump that
		// OutOfSync produces on the RESTORED side names a frame and a pile of hashes with nothing
		// to compare them against. This writes the RECORDING side's report for the same frames,
		// one file per frame, so the two sides can be diffed and the diverging trait/field named.
		// One file per frame deliberately: the desync path's filename is timestamped only to the
		// second, so a burst of dumps would otherwise collide into one interleaved file.
		internal void DumpDiagnosticReport(int frame, string tag)
		{
			var reportName = $"syncdiag-{tag}-frame{frame:D6}-{orderManager.LocalClient?.Index}.log";
			WriteReport($"syncdiag-{tag}-{frame}", reportName, frame);
		}

		bool WriteReport(string channel, string reportName, int frame)
		{
			Log.AddChannel(channel, reportName);

			var recordedFrames = new List<int>();
			var desyncFrameFound = false;
			foreach (var r in syncReports)
			{
				recordedFrames.Add(r.Frame);
				if (r.Frame == frame)
				{
					desyncFrameFound = true;
					var mod = Game.ModData.Manifest.Metadata;
					Log.Write(channel, $"Player: {Game.Settings.Player.Name} ({Platform.CurrentPlatform} {Environment.OSVersion} {Platform.RuntimeVersion})");
					if (Game.IsHost)
						Log.Write(channel, "Player is host.");
					Log.Write(channel, $"Game ID: {orderManager.LobbyInfo.GlobalSettings.GameUid} (Mod: {mod.TitleTranslated} at Version {mod.Version})");

					// The first thing to check when diffing two players' reports is whether they
					// were even running the same thing. mod.Version above cannot answer that -
					// it is a hardcoded literal. These can: the fingerprint covers engine build,
					// mod rules and installed content, and the sequence line catches an
					// incomplete Red Alert install, which no repo-state check can see.
					Log.Write(channel, $"Build: {BuildFingerprint.ForMod(Game.ModData)}");
					Log.Write(channel, Graphics.SequenceIntegrity.Summary());
					Log.Write(channel, $"Sync for net frame {r.Frame} -------------");
					Log.Write(channel, $"SharedRandom: {r.SyncedRandom} (#{r.TotalCount})");
					Log.Write(channel, "Synced Traits:");
					foreach (var a in r.Traits)
					{
						Log.Write(channel, $"\t {a.ActorID} {a.Type} {a.Owner} {a.Trait} ({a.Hash})");

						var nvp = a.NamesValues;
						for (var i = 0; i < nvp.Names.Length; i++)
							if (nvp.Values[i] != null)
								Log.Write(channel, $"\t\t {nvp.Names[i]}: {nvp.Values[i]}");
					}

					Log.Write(channel, "Synced Effects:");
					foreach (var e in r.Effects)
					{
						Log.Write(channel, $"\t {e.Name} ({e.Hash})");

						var nvp = e.NamesValues;
						for (var i = 0; i < nvp.Names.Length; i++)
							if (nvp.Values[i] != null)
								Log.Write(channel, $"\t\t {nvp.Names[i]}: {nvp.Values[i]}");
					}

					Log.Write(channel, "Orders Issued:");
					foreach (var o in r.Orders)
						Log.Write(channel, $"\t {o}");
				}
			}

			Log.Write(channel, "Sync Report System Info:");
			Log.Write(channel, $"Out of sync frame: {frame}");
			Log.Write(channel, "Recorded frames: " + string.Join(",", recordedFrames));

			if (!desyncFrameFound)
				Log.Write(channel, $"Recorded frames do not contain the frame {frame}. No sync report available!");

			return desyncFrameFound;
		}

		sealed class Report
		{
			public int Frame;
			public int SyncedRandom;
			public int TotalCount;
			public readonly List<TraitReport> Traits = new();
			public readonly List<EffectReport> Effects = new();
			public readonly List<OrderManager.ClientOrder> Orders = new();
		}

		struct TraitReport
		{
			public uint ActorID;
			public string Type;
			public string Owner;
			public string Trait;
			public int Hash;
			public (string[] Names, Values Values) NamesValues;
		}

		struct EffectReport
		{
			public string Name;
			public int Hash;
			public (string[] Names, Values Values) NamesValues;
		}

		readonly struct TypeInfo
		{
			static readonly ParameterExpression SyncParam = Expression.Parameter(typeof(ISync), "sync");
			static readonly ConstantExpression NullString = Expression.Constant(null, typeof(string));
			static readonly ConstantExpression TrueString = Expression.Constant(bool.TrueString, typeof(string));
			static readonly ConstantExpression FalseString = Expression.Constant(bool.FalseString, typeof(string));

			public readonly Func<ISync, object>[] SerializableCopyOfMemberFunctions;
			public readonly string[] Names;

			public TypeInfo(Type type)
			{
				const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
				var fields = type.GetFields(Flags)
					.Where(fi => !fi.IsLiteral && !fi.IsStatic && fi.HasAttribute<SyncAttribute>())
					.ToList();
				var properties = type.GetProperties(Flags)
					.Where(pi => pi.HasAttribute<SyncAttribute>())
					.ToList();

				foreach (var prop in properties)
					if (!prop.CanRead || prop.GetIndexParameters().Length > 0)
						throw new InvalidOperationException(
							"Properties using the Sync attribute must be readable and must not use index parameters.\n" +
							"Invalid Property: " + prop.DeclaringType.FullName + "." + prop.Name);

				var sync = Expression.Convert(SyncParam, type);
				SerializableCopyOfMemberFunctions = fields
					.Select(fi => SerializableCopyOfMember(Expression.Field(sync, fi), fi.FieldType, fi.Name))
					.Concat(properties.Select(pi => SerializableCopyOfMember(Expression.Property(sync, pi), pi.PropertyType, pi.Name)))
					.ToArray();

				Names = fields.Select(fi => fi.Name).Concat(properties.Select(pi => pi.Name)).ToArray();
			}

			static Func<ISync, object> SerializableCopyOfMember(MemberExpression getMember, Type memberType, string name)
			{
				// We need to serialize a copy of the current value so if the sync report is generated, the values can
				// be dumped as strings.
				if (memberType.IsValueType)
				{
					// PERF: For value types we can avoid the overhead of calling ToString immediately. We can instead
					// just box a copy of the current value into an object. This is faster than calling ToString. We
					// can call ToString later when we generate the report. Most of the time, the sync report is never
					// generated so we successfully avoid the overhead to calling ToString.
					if (memberType == typeof(bool))
					{
						// PERF: If the member is a Boolean, we can also avoid the allocation caused by boxing it.
						// Instead, we can just return the resulting strings directly.
						var getBoolString = Expression.Condition(getMember, TrueString, FalseString);
						return Expression.Lambda<Func<ISync, string>>(getBoolString, name, new[] { SyncParam }).Compile();
					}

					var boxedCopy = Expression.Convert(getMember, typeof(object));
					return Expression.Lambda<Func<ISync, object>>(boxedCopy, name, new[] { SyncParam }).Compile();
				}

				// For reference types, we have to call ToString right away to get a snapshot of the value. We cannot
				// delay, as calling ToString later may produce different results.
				return MemberToString(getMember, memberType, name);
			}

			static Func<ISync, string> MemberToString(MemberExpression getMember, Type memberType, string name)
			{
				// The lambda generated is shown below.
				// TSync is actual type of the ISync object. Foo is a field or property with the Sync attribute applied.
				var toString = memberType.GetMethod(nameof(object.ToString), Type.EmptyTypes);
				Expression getString;
				if (memberType.IsValueType)
				{
					// (ISync sync) => { return ((TSync)sync).Foo.ToString(); }
					getString = Expression.Call(getMember, toString);
				}
				else
				{
					// (ISync sync) => { var foo = ((TSync)sync).Foo; return foo == null ? null : foo.ToString(); }
					var memberVariable = Expression.Variable(memberType, getMember.Member.Name);
					var assignMemberVariable = Expression.Assign(memberVariable, getMember);
					var member = Expression.Block(new[] { memberVariable }, assignMemberVariable);
					getString = Expression.Call(member, toString);
					var nullMember = Expression.Constant(null, memberType);
					getString = Expression.Condition(Expression.Equal(member, nullMember), NullString, getString);
				}

				return Expression.Lambda<Func<ISync, string>>(getString, name, new[] { SyncParam }).Compile();
			}
		}

		/// <summary>
		/// Holds up to 4 objects directly, or else allocates an array to hold the items. This allows us to record
		/// trait values for traits with up to 4 sync members inline without having to allocate extra memory.
		/// </summary>
		struct Values
		{
			static readonly object Sentinel = new();

			object item1OrArray;
			object item2OrSentinel;
			object item3;
			object item4;

			public Values(int size)
			{
				item1OrArray = null;
				item2OrSentinel = null;
				item3 = null;
				item4 = null;
				if (size > 4)
				{
					item1OrArray = new object[size];
					item2OrSentinel = Sentinel;
				}
			}

			public object this[int index]
			{
				readonly get
				{
					if (item2OrSentinel == Sentinel)
						return ((object[])item1OrArray)[index];

					switch (index)
					{
						case 0: return item1OrArray;
						case 1: return item2OrSentinel;
						case 2: return item3;
						case 3: return item4;
						default: throw new ArgumentOutOfRangeException(nameof(index));
					}
				}

				set
				{
					if (item2OrSentinel == Sentinel)
					{
						((object[])item1OrArray)[index] = value;
						return;
					}

					switch (index)
					{
						case 0: item1OrArray = value; break;
						case 1: item2OrSentinel = value; break;
						case 2: item3 = value; break;
						case 3: item4 = value; break;
						default: throw new ArgumentOutOfRangeException(nameof(index));
					}
				}
			}
		}
	}
}
