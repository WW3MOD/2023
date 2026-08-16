#region Copyright & License Information
/*
 * Copyright 2007-2022 The OpenRA Developers (see AUTHORS)
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
using OpenRA.Graphics;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	public class UnitTypeDefaults
	{
		public UnitStance? FireStance;
		public EngagementStance? Engagement;
		public CohesionMode? Cohesion;
		public ResupplyBehavior? Resupply;
	}

	public class UnitDefaultsManagerInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) { return new UnitDefaultsManager(); }
	}

	// PITFALL: this is a PER-MACHINE preference store, backed by a file under Platform.SupportDir.
	// Nothing in the simulation may read it. It used to be read by AutoTarget.Created, which runs on
	// every client for every actor — so each client applied its own file to everyone's units and the
	// synced stance/cohesion fields diverged with nobody pressing anything. Preferences now reach the
	// world the only way local state legitimately can: the owning client issues an ORDER, which every
	// client resolves identically.
	public class UnitDefaultsManager : IWorldLoaded, IGameOver
	{
		readonly Dictionary<string, UnitTypeDefaults> typeDefaults = new Dictionary<string, UnitTypeDefaults>();
		string filePath;
		World world;

		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{
			filePath = Path.Combine(Platform.SupportDir, "ww3mod", "unit-defaults.yaml");
			Load();

			world = w;
			w.ActorAdded += ApplyDefaultsToLocalActor;
		}

		/// <summary>
		/// Fires on every client, but only the OWNING client passes the LocalPlayer check and issues the
		/// orders — exactly like a player clicking the stance buttons. Every client then resolves the
		/// same replicated orders, so the stance a unit ends up with is identical everywhere even though
		/// the preference behind it is local. Spectators and replay viewers have no LocalPlayer and
		/// issue nothing.
		/// </summary>
		void ApplyDefaultsToLocalActor(Actor a)
		{
			var local = world?.LocalPlayer;
			if (local == null || a.Owner != local || local.IsBot || !local.Playable)
				return;

			var defaults = GetDefaults(a.Info.Name);
			if (defaults == null)
				return;

			// Only actors that actually accept these orders — AutoTarget.ResolveOrder gates every one
			// of them on Info.EnableStances, so issuing to anything else would be pure order spam.
			var autoTarget = a.Info.TraitInfoOrDefault<AutoTargetInfo>();
			if (autoTarget == null || !autoTarget.EnableStances)
				return;

			if (defaults.FireStance.HasValue)
				world.IssueOrder(new Order("SetUnitStance", a, false) { ExtraData = (uint)defaults.FireStance.Value });

			if (defaults.Engagement.HasValue)
				world.IssueOrder(new Order("SetEngagementStance", a, false) { ExtraData = (uint)defaults.Engagement.Value });

			if (defaults.Cohesion.HasValue)
				world.IssueOrder(new Order("SetCohesion", a, false) { ExtraData = (uint)defaults.Cohesion.Value });

			if (defaults.Resupply.HasValue)
				world.IssueOrder(new Order("SetResupplyBehavior", a, false) { ExtraData = (uint)defaults.Resupply.Value });
		}

		void IGameOver.GameOver(World world)
		{
			Save();
		}

		public UnitTypeDefaults GetDefaults(string actorType)
		{
			return typeDefaults.TryGetValue(actorType, out var defaults) ? defaults : null;
		}

		public void SetFireStance(string actorType, UnitStance stance)
		{
			var defaults = GetOrCreate(actorType);
			defaults.FireStance = stance;
		}

		public void SetEngagement(string actorType, EngagementStance stance)
		{
			var defaults = GetOrCreate(actorType);
			defaults.Engagement = stance;
		}

		public void SetCohesion(string actorType, CohesionMode mode)
		{
			var defaults = GetOrCreate(actorType);
			defaults.Cohesion = mode;
		}

		public void SetResupply(string actorType, ResupplyBehavior behavior)
		{
			var defaults = GetOrCreate(actorType);
			defaults.Resupply = behavior;
		}

		UnitTypeDefaults GetOrCreate(string actorType)
		{
			if (!typeDefaults.TryGetValue(actorType, out var defaults))
			{
				defaults = new UnitTypeDefaults();
				typeDefaults[actorType] = defaults;
			}

			return defaults;
		}

		void Load()
		{
			if (!File.Exists(filePath))
				return;

			try
			{
				var yaml = MiniYaml.FromFile(filePath);
				foreach (var node in yaml)
				{
					var actorType = node.Key;
					var defaults = GetOrCreate(actorType);

					foreach (var child in node.Value.Nodes)
					{
						switch (child.Key)
						{
							case "FireStance":
								if (Enum.TryParse<UnitStance>(child.Value.Value, out var fs))
									defaults.FireStance = fs;
								break;
							case "Engagement":
								if (Enum.TryParse<EngagementStance>(child.Value.Value, out var es))
									defaults.Engagement = es;
								break;
							case "Cohesion":
								if (Enum.TryParse<CohesionMode>(child.Value.Value, out var cm))
									defaults.Cohesion = cm;
								break;
							case "Resupply":
								if (Enum.TryParse<ResupplyBehavior>(child.Value.Value, out var rb))
									defaults.Resupply = rb;
								break;
						}
					}
				}
			}
			catch
			{
				// If the file is corrupt, just start fresh
			}
		}

		void Save()
		{
			if (typeDefaults.Count == 0)
				return;

			try
			{
				var dir = Path.GetDirectoryName(filePath);
				if (!Directory.Exists(dir))
					Directory.CreateDirectory(dir);

				var nodes = new List<MiniYamlNode>();
				foreach (var kv in typeDefaults)
				{
					var children = new List<MiniYamlNode>();
					if (kv.Value.FireStance.HasValue)
						children.Add(new MiniYamlNode("FireStance", kv.Value.FireStance.Value.ToString()));
					if (kv.Value.Engagement.HasValue)
						children.Add(new MiniYamlNode("Engagement", kv.Value.Engagement.Value.ToString()));
					if (kv.Value.Cohesion.HasValue)
						children.Add(new MiniYamlNode("Cohesion", kv.Value.Cohesion.Value.ToString()));
					if (kv.Value.Resupply.HasValue)
						children.Add(new MiniYamlNode("Resupply", kv.Value.Resupply.Value.ToString()));

					if (children.Count > 0)
						nodes.Add(new MiniYamlNode(kv.Key, new MiniYaml("", children)));
				}

				nodes.WriteToFile(filePath);
			}
			catch
			{
				// Don't crash if we can't save defaults
			}
		}
	}
}
