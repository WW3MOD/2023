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
using System.Linq;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[TraitLocation(SystemActors.Player)]
	[Desc("Attach this to the player actor.")]
	public class DeveloperModeInfo : TraitInfo, ILobbyOptions
	{
		[Desc("Descriptive label for the developer mode checkbox in the lobby.")]
		public readonly string CheckboxLabel = "Debug Menu";

		[Desc("Tooltip description for the developer mode checkbox in the lobby.")]
		public readonly string CheckboxDescription = "Enables cheats and developer commands";

		[Desc("Default value of the developer mode checkbox in the lobby.")]
		public readonly bool CheckboxEnabled = true;

		[Desc("Prevent the developer mode state from being changed in the lobby.")]
		public readonly bool CheckboxLocked = false;

		[Desc("Whether to display the developer mode checkbox in the lobby.")]
		public readonly bool CheckboxVisible = true;

		[Desc("Display order for the developer mode checkbox in the lobby.")]
		public readonly int CheckboxDisplayOrder = 0;

		[Desc("Default cash bonus granted by the give cash cheat.")]
		public readonly int Cash = 20000;

		[Desc("Growth steps triggered by the grow resources button.")]
		public readonly int ResourceGrowth = 100;

		[Desc("Enable the fast build cheat by default.")]
		public readonly bool FastBuild;

		[Desc("Enable the fast support powers cheat by default.")]
		public readonly bool FastCharge;

		[Desc("Enable the disable visibility cheat by default.")]
		public readonly bool DisableFog;

		[Desc("Enable the unlimited power cheat by default.")]
		public readonly bool UnlimitedPower;

		[Desc("Enable the build anywhere cheat by default.")]
		public readonly bool BuildAnywhere;

		[Desc("Enable the cosmetic reveal cheat by default (see all units without affecting gameplay).")]
		public readonly bool CosmeticReveal;

		[Desc("Enable the path debug overlay by default.")]
		public readonly bool PathDebug;

		IEnumerable<LobbyOption> ILobbyOptions.LobbyOptions(MapPreview map)
		{
			yield return new LobbyBooleanOption("cheats", CheckboxLabel, CheckboxDescription, CheckboxVisible, CheckboxDisplayOrder, CheckboxEnabled, CheckboxLocked);
			yield return new LobbyBooleanOption("sync", "Sync", "Sync game code to detect errors with other players. Lowers performance.",
				true, 5, false, false);
		}

		public override object Create(ActorInitializer init) { return new DeveloperMode(this); }
	}

	public class DeveloperMode : IResolveOrder, ISync, INotifyCreated, IUnlocksRenderPlayer
	{
		readonly DeveloperModeInfo info;
		public bool Enabled { get; private set; }

		[Sync]
		bool fastCharge;

		[Sync]
		bool allTech;

		[Sync]
		bool fastBuild;

		[Sync]
		bool disableFog;

		[Sync]
		bool pathDebug;

		[Sync]
		bool unlimitedPower;

		[Sync]
		bool buildAnywhere;

		[Sync]
		bool cosmeticReveal;

		[Sync]
		bool controlAllUnits;

		public bool FastCharge => Enabled && fastCharge;
		public bool AllTech => Enabled && allTech;
		public bool FastBuild => Enabled && fastBuild;
		public bool DisableFog => Enabled && disableFog;
		public bool PathDebug => Enabled && pathDebug;
		public bool UnlimitedPower => Enabled && unlimitedPower;
		public bool BuildAnywhere => Enabled && buildAnywhere;
		public bool CosmeticReveal => Enabled && cosmeticReveal;
		public bool ControlAllUnits => Enabled && controlAllUnits;

		bool enableAll;

		[TranslationReference("cheat", "player", "suffix")]
		static readonly string CheatUsed = "cheat-used";

		public DeveloperMode(DeveloperModeInfo info)
		{
			this.info = info;
			fastBuild = info.FastBuild;
			fastCharge = info.FastCharge;
			disableFog = info.DisableFog;
			pathDebug = info.PathDebug;
			unlimitedPower = info.UnlimitedPower;
			buildAnywhere = info.BuildAnywhere;
			cosmeticReveal = info.CosmeticReveal;
		}

		void INotifyCreated.Created(Actor self)
		{
			Enabled = self.World.LobbyInfo.NonBotPlayers.Count() == 1 || self.World.LobbyInfo.GlobalSettings
				.OptionOrDefault("cheats", info.CheckboxEnabled);
		}

		void SetFieldForAll(Actor self, Action<DeveloperMode> setter)
		{
			foreach (var player in self.World.Players.Where(p => p.Playable))
			{
				var dm = player.PlayerActor.TraitOrDefault<DeveloperMode>();
				if (dm != null)
					setter(dm);
			}
		}

		public void ResolveOrder(Actor self, Order order)
		{
			if (!Enabled)
				return;

			var debugSuffix = "";
			switch (order.OrderString)
			{
				case "DevAll":
				{
					enableAll ^= true;
					allTech = fastCharge = fastBuild = disableFog = unlimitedPower = buildAnywhere = cosmeticReveal = controlAllUnits = enableAll;

					if (enableAll)
					{
						self.Owner.MapLayers.ExploreAll();

						var amount = order.ExtraData != 0 ? (int)order.ExtraData : info.Cash;
						self.Trait<PlayerResources>().ChangeCash(amount);
					}
					else
						self.Owner.MapLayers.ResetExploration();

					self.Owner.MapLayers.FogDisabled = DisableFog;

					break;
				}

				// --- Toggle (Me only) ---
				case "DevEnableTech": allTech ^= true; break;
				case "DevFastCharge": fastCharge ^= true; break;
				case "DevFastBuild": fastBuild ^= true; break;
				case "DevUnlimitedPower": unlimitedPower ^= true; break;
				case "DevBuildAnywhere": buildAnywhere ^= true; break;
				case "DevCosmeticReveal": cosmeticReveal ^= true; break;
				case "DevControlAllUnits": controlAllUnits ^= true; break;
				case "DevPathDebug": pathDebug ^= true; break;

				case "DevVisibility":
				{
					disableFog ^= true;
					self.Owner.MapLayers.FogDisabled = DisableFog;

					break;
				}

				// --- Enable All players ---
				case "DevFastBuildAll": SetFieldForAll(self, dm => dm.fastBuild = true); break;
				case "DevFastChargeAll": SetFieldForAll(self, dm => dm.fastCharge = true); break;
				case "DevEnableTechAll": SetFieldForAll(self, dm => dm.allTech = true); break;
				case "DevBuildAnywhereAll": SetFieldForAll(self, dm => dm.buildAnywhere = true); break;
				case "DevCosmeticRevealAll": SetFieldForAll(self, dm => dm.cosmeticReveal = true); break;
				case "DevControlAllUnitsAll": SetFieldForAll(self, dm => dm.controlAllUnits = true); break;
				case "DevPathDebugAll": SetFieldForAll(self, dm => dm.pathDebug = true); break;

				case "DevVisibilityAll":
				{
					foreach (var player in self.World.Players.Where(p => p.Playable))
					{
						var dm = player.PlayerActor.TraitOrDefault<DeveloperMode>();
						if (dm != null)
						{
							dm.disableFog = true;
							player.MapLayers.FogDisabled = true;
						}
					}

					break;
				}

				// --- Reset All players ---
				case "DevFastBuildReset": SetFieldForAll(self, dm => dm.fastBuild = false); break;
				case "DevFastChargeReset": SetFieldForAll(self, dm => dm.fastCharge = false); break;
				case "DevEnableTechReset": SetFieldForAll(self, dm => dm.allTech = false); break;
				case "DevBuildAnywhereReset": SetFieldForAll(self, dm => dm.buildAnywhere = false); break;
				case "DevCosmeticRevealReset": SetFieldForAll(self, dm => dm.cosmeticReveal = false); break;
				case "DevControlAllUnitsReset": SetFieldForAll(self, dm => dm.controlAllUnits = false); break;
				case "DevPathDebugReset": SetFieldForAll(self, dm => dm.pathDebug = false); break;

				case "DevVisibilityReset":
				{
					foreach (var player in self.World.Players.Where(p => p.Playable))
					{
						var dm = player.PlayerActor.TraitOrDefault<DeveloperMode>();
						if (dm != null)
						{
							dm.disableFog = false;
							player.MapLayers.FogDisabled = false;
						}
					}

					break;
				}

				// --- Actions ---
				case "DevGiveCash":
				{
					var amount = order.ExtraData != 0 ? (int)order.ExtraData : info.Cash;
					self.Trait<PlayerResources>().ChangeCash(amount);

					debugSuffix = $" ({amount} credits)";
					break;
				}

				case "DevGiveCashAll":
				{
					var amount = order.ExtraData != 0 ? (int)order.ExtraData : info.Cash;
					var receivingPlayers = self.World.Players.Where(p => p.Playable);

					foreach (var player in receivingPlayers)
						player.PlayerActor.Trait<PlayerResources>().ChangeCash(amount);

					debugSuffix = $" ({amount} credits)";
					break;
				}

				case "DevGrowResources":
				{
					foreach (var a in self.World.ActorsWithTrait<ISeedableResource>())
						for (var i = 0; i < info.ResourceGrowth; i++)
							a.Trait.Seed(a.Actor);

					break;
				}

				case "DevCinematicView":
				{
					if (self.World.LocalPlayer == self.Owner)
					{
						if (self.World.RenderPlayer == null)
							self.World.RenderPlayer = self.Owner;
						else
							self.World.RenderPlayer = null;
					}

					break;
				}

				case "DevGiveExploration":
				{
					self.Owner.MapLayers.ExploreAll();
					break;
				}

				case "DevResetExploration":
				{
					self.Owner.MapLayers.ResetExploration();
					break;
				}

				case "DevPlayerExperience":
				{
					self.Owner.PlayerActor.TraitOrDefault<PlayerExperience>()?.GiveExperience((int)order.ExtraData);
					break;
				}

				case "DevKill":
				{
					if (order.Target.Type != TargetType.Actor)
						break;

					var actor = order.Target.Actor;
					var args = order.TargetString.Split(' ');
					var damageTypes = BitSet<DamageType>.FromStringsNoAlloc(args);

					actor.Kill(actor, damageTypes);
					break;
				}

				case "DevDispose":
				{
					if (order.Target.Type != TargetType.Actor)
						break;

					order.Target.Actor.Dispose();
					break;
				}

				default:
					return;
			}

			var arguments = Translation.Arguments("cheat", order.OrderString, "player", self.Owner.PlayerName, "suffix", debugSuffix);
			TextNotificationsManager.Debug(Game.ModData.Translation.GetString(CheatUsed, arguments));
		}

		bool IUnlocksRenderPlayer.RenderPlayerUnlocked => Enabled;

		public static bool IsControlAllUnitsActive(World world)
		{
			// Always active on shellmaps (LocalPlayer is null on shellmaps)
			if (world.Type == WorldType.Shellmap)
				return true;

			var localPlayer = world.LocalPlayer;
			if (localPlayer == null)
				return false;

			var devMode = localPlayer.PlayerActor.TraitOrDefault<DeveloperMode>();
			return devMode != null && devMode.ControlAllUnits;
		}
	}
}
