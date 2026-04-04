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
using OpenRA.Mods.Common.Effects;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Graduated contestation system for Supply Routes.",
		"Tracks enemy vs friendly unit values in range to fill/deplete a control bar.",
		"Production speed scales with bar level below the slowdown threshold.",
		"When control bar is fully depleted, a defeat bar fills. At 100% defeat bar,",
		"the player is defeated (no allies) or becomes passive (has allies).")]
	public class SupplyRouteContestationInfo : TraitInfo
	{
		[Desc("Range to detect enemy and friendly forces.")]
		public readonly WDist Range = WDist.FromCells(10);

		[Desc("Allowed ProximityCaptor types that count as enemy contesters.")]
		public readonly BitSet<CaptureType> CaptorTypes = new BitSet<CaptureType>("Player", "Vehicle", "Tank", "Infantry");

		[Desc("Internal bar resolution (higher = smoother, avoids integer rounding).")]
		public readonly int BarMax = 100000;

		[Desc("Reference net enemy surplus value. This surplus depletes the bar in BaseTicks.")]
		public readonly int ReferenceValue = 2500;

		[Desc("Ticks to deplete bar from full at ReferenceValue net enemy surplus. (60s at 25 tps)")]
		public readonly int BaseTicks = 1500;

		[Desc("Minimum ticks to deplete bar from full, regardless of enemy surplus. (20s at 25 tps)")]
		public readonly int MinTicks = 500;

		[Desc("Ticks to recover bar from zero to full with no friendlies present. (120s at 25 tps)")]
		public readonly int BaseRecoveryTicks = 3000;

		[Desc("Recovery speed multiplier when friendly units are in range.")]
		public readonly int FriendlyRecoveryMultiplier = 3;

		[Desc("Bar percentage (0-100) below which production starts slowing.")]
		public readonly int SlowdownThreshold = 50;

		[Desc("How often to recalculate force values (in ticks). Performance optimization.")]
		public readonly int ScanInterval = 7;

		[NotificationReference("Speech")]
		[Desc("Speech notification when contestation begins.")]
		public readonly string ContestationNotification = "BaseAttack";

		[Desc("Text notification when contestation begins.")]
		public readonly string ContestationTextNotification = "Supply Route contested!";

		[NotificationReference("Speech")]
		[Desc("Speech notification when defeat bar starts filling.")]
		public readonly string DefeatWarningNotification = "BaseAttack";

		[Desc("Text notification when defeat bar starts filling.")]
		public readonly string DefeatWarningTextNotification = "Supply Route lost! Defeat imminent!";

		[Desc("Text notification when player becomes passive.")]
		public readonly string PassiveTextNotification = "Supply Route overrun! Production and income frozen.";

		[Desc("Text notification when player is reinstated from passive.")]
		public readonly string ReinstatedTextNotification = "Supply Route reclaimed! Production resuming.";

		[Desc("Minimum duration (in milliseconds) between notifications.")]
		public readonly int NotifyInterval = 30000;

		[Desc("Ticks between building flashes while contested.")]
		public readonly int FlashInterval = 100;

		[Desc("Minimap ping duration (ticks).")]
		public readonly int MiniMapPingDuration = 250;

		[Desc("Minimap ping color.")]
		public readonly Color MiniMapPingColor = Color.Orange;

		public override object Create(ActorInitializer init) { return new SupplyRouteContestation(init.Self, this); }
	}

	public class SupplyRouteContestation : ITick, ISelectionBar, IAlwaysVisibleBar, IProductionSpeedModifier,
		INotifyAddedToWorld, INotifyRemovedFromWorld
	{
		readonly SupplyRouteContestationInfo info;
		readonly Actor self;
		readonly List<Actor> actorsInRange = new List<Actor>();

		int proximityTrigger;
		[Sync]
		int controlBar;
		[Sync]
		int defeatBar;
		int cachedNetEnemySurplus;
		int cachedNetFriendlySurplus;
		int scanTick;
		long lastNotifyTime;
		long lastDefeatNotifyTime;
		bool wasContested;
		bool wasInDefeatPhase;
		[Sync]
		bool isPassive;
		MiniMapPings radarPings;

		public SupplyRouteContestation(Actor self, SupplyRouteContestationInfo info)
		{
			this.info = info;
			this.self = self;
			controlBar = info.BarMax;
			lastNotifyTime = -info.NotifyInterval;
			lastDefeatNotifyTime = -info.NotifyInterval;
		}

		void INotifyAddedToWorld.AddedToWorld(Actor self)
		{
			radarPings = self.World.WorldActor.TraitOrDefault<MiniMapPings>();
			proximityTrigger = self.World.ActorMap.AddProximityTrigger(
				self.CenterPosition, info.Range, WDist.Zero, ActorEntered, ActorLeft);
		}

		void INotifyRemovedFromWorld.RemovedFromWorld(Actor self)
		{
			self.World.ActorMap.RemoveProximityTrigger(proximityTrigger);
			actorsInRange.Clear();
		}

		bool IsRelevantActor(Actor a)
		{
			if (a == self || a.Disposed || !a.IsInWorld)
				return false;

			var rel = self.Owner.RelationshipWith(a.Owner);

			if (rel == PlayerRelationship.Enemy)
			{
				var pc = a.Info.TraitInfoOrDefault<ProximityCaptorInfo>();
				return pc != null && pc.Types.Overlaps(info.CaptorTypes);
			}

			if (rel == PlayerRelationship.Ally)
			{
				var valued = a.Info.TraitInfoOrDefault<ValuedInfo>();
				return valued != null && valued.Cost > 0;
			}

			return false;
		}

		void ActorEntered(Actor other)
		{
			if (!IsRelevantActor(other))
				return;

			actorsInRange.Add(other);
		}

		void ActorLeft(Actor other)
		{
			actorsInRange.Remove(other);
		}

		void RecalculateForces()
		{
			actorsInRange.RemoveAll(a => a.Disposed || !a.IsInWorld);

			var enemyValue = 0;
			var friendlyValue = 0;

			foreach (var a in actorsInRange)
			{
				var valued = a.Info.TraitInfoOrDefault<ValuedInfo>();
				if (valued == null || valued.Cost <= 0)
					continue;

				var rel = self.Owner.RelationshipWith(a.Owner);
				if (rel == PlayerRelationship.Enemy)
					enemyValue += valued.Cost;
				else if (rel == PlayerRelationship.Ally)
					friendlyValue += valued.Cost;
			}

			cachedNetEnemySurplus = Math.Max(0, enemyValue - friendlyValue);
			cachedNetFriendlySurplus = Math.Max(0, friendlyValue - enemyValue);
		}

		int CalculateTickRate(int valueSurplus)
		{
			var ticksToFull = Math.Max(info.MinTicks,
				(long)info.BaseTicks * info.ReferenceValue / valueSurplus);
			return Math.Max(1, info.BarMax / (int)Math.Max(1, ticksToFull));
		}

		void ITick.Tick(Actor self)
		{
			// Player already defeated — nothing to do
			if (self.Owner.WinState != WinState.Undefined)
				return;

			if (++scanTick >= info.ScanInterval)
			{
				scanTick = 0;
				RecalculateForces();
			}

			if (cachedNetEnemySurplus > 0)
			{
				// Enemy has value surplus — depleting
				var rate = CalculateTickRate(cachedNetEnemySurplus);

				if (controlBar > 0)
				{
					// Phase 1: Deplete control bar (green → yellow → empty)
					controlBar = Math.Max(0, controlBar - rate);

					if (!wasContested)
					{
						wasContested = true;
						OnContestationStarted();
					}
				}
				else
				{
					// Phase 2: Fill defeat bar (red fills up)
					defeatBar = Math.Min(info.BarMax, defeatBar + rate);

					if (!wasInDefeatPhase)
					{
						wasInDefeatPhase = true;
						OnDefeatPhaseStarted();
					}

					// Check for defeat/passive when defeat bar is full
					if (defeatBar >= info.BarMax)
						OnDefeatBarFull();
				}

				// Flash while being contested (any phase)
				if (self.World.WorldTick % info.FlashInterval == 0)
				{
					var flashColor = controlBar > 0 ? Color.Orange : Color.Red;
					self.World.AddFrameEndTask(w =>
						w.Add(new FlashTarget(self, flashColor, 0.5f, 3, 4, 0)));
				}
			}
			else
			{
				// No enemy surplus — recovery phase
				if (defeatBar > 0)
				{
					// Drain defeat bar first
					var recoveryRate = Math.Max(1, info.BarMax / info.BaseRecoveryTicks);
					var friendlyBoost = cachedNetFriendlySurplus > 0
						? info.FriendlyRecoveryMultiplier : 1;
					defeatBar = Math.Max(0, defeatBar - recoveryRate * friendlyBoost);

					if (defeatBar <= 0)
					{
						wasInDefeatPhase = false;

						// Reinstate passive player
						if (isPassive)
						{
							isPassive = false;
							OnReinstated();
						}
					}
				}
				else if (controlBar < info.BarMax)
				{
					// Then recover control bar
					var recoveryRate = Math.Max(1, info.BarMax / info.BaseRecoveryTicks);
					var friendlyBoost = cachedNetFriendlySurplus > 0
						? info.FriendlyRecoveryMultiplier : 1;
					controlBar = Math.Min(info.BarMax, controlBar + recoveryRate * friendlyBoost);

					if (controlBar >= info.BarMax)
						wasContested = false;
				}
			}
		}

		void OnContestationStarted()
		{
			if (Game.RunTime <= lastNotifyTime + info.NotifyInterval)
				return;

			lastNotifyTime = Game.RunTime;

			var localPlayer = self.World.LocalPlayer;
			if (localPlayer == null || localPlayer.Spectating)
				return;

			if (self.Owner == localPlayer || localPlayer.IsAlliedWith(self.Owner))
			{
				Game.Sound.PlayNotification(self.World.Map.Rules, self.Owner, "Speech",
					info.ContestationNotification, self.Owner.Faction.InternalName);
				TextNotificationsManager.AddTransientLine(self.Owner, info.ContestationTextNotification);
			}

			radarPings?.Add(() => self.Owner.IsAlliedWith(self.World.RenderPlayer),
				self.CenterPosition, info.MiniMapPingColor, info.MiniMapPingDuration);

			self.World.AddFrameEndTask(w =>
				w.Add(new FlashTarget(self, Color.Orange, 0.5f, 5, 4, 0)));
		}

		void OnDefeatPhaseStarted()
		{
			if (Game.RunTime <= lastDefeatNotifyTime + info.NotifyInterval)
				return;

			lastDefeatNotifyTime = Game.RunTime;

			var localPlayer = self.World.LocalPlayer;
			if (localPlayer == null || localPlayer.Spectating)
				return;

			if (self.Owner == localPlayer || localPlayer.IsAlliedWith(self.Owner))
			{
				Game.Sound.PlayNotification(self.World.Map.Rules, self.Owner, "Speech",
					info.DefeatWarningNotification, self.Owner.Faction.InternalName);
				TextNotificationsManager.AddTransientLine(self.Owner, info.DefeatWarningTextNotification);
			}

			radarPings?.Add(() => self.Owner.IsAlliedWith(self.World.RenderPlayer),
				self.CenterPosition, Color.Red, info.MiniMapPingDuration);

			self.World.AddFrameEndTask(w =>
				w.Add(new FlashTarget(self, Color.Red, 0.5f, 8, 3, 0)));
		}

		void OnDefeatBarFull()
		{
			// Already handled
			if (isPassive || self.Owner.WinState != WinState.Undefined)
				return;

			// Check if player has living allies
			var hasAllies = self.World.Players.Any(p =>
				p != self.Owner &&
				!p.NonCombatant &&
				p.Playable &&
				p.IsAlliedWith(self.Owner) &&
				p.WinState != WinState.Lost);

			if (hasAllies)
			{
				// Become passive — production stays at 0, allies can reinstate
				isPassive = true;
				TextNotificationsManager.AddSystemLine(self.Owner.PlayerName + " has lost their Supply Route! Production and income frozen.");

				var localPlayer = self.World.LocalPlayer;
				if (localPlayer != null && !localPlayer.Spectating &&
					(self.Owner == localPlayer || localPlayer.IsAlliedWith(self.Owner)))
				{
					TextNotificationsManager.AddTransientLine(self.Owner, info.PassiveTextNotification);
				}
			}
			else
			{
				// No allies — player is defeated
				var mo = self.Owner.PlayerActor.TraitOrDefault<MissionObjectives>();
				if (mo != null)
				{
					// Find the conquest objective and mark it failed
					// This triggers the standard defeat flow (WinState.Lost, notifications, etc.)
					var objectiveId = mo.Add(self.Owner, "Hold the Supply Route", "Primary", inhibitAnnouncement: true);
					mo.MarkFailed(self.Owner, objectiveId);
				}
			}
		}

		void OnReinstated()
		{
			TextNotificationsManager.AddSystemLine(self.Owner.PlayerName + "'s Supply Route has been reclaimed!");

			var localPlayer = self.World.LocalPlayer;
			if (localPlayer != null && !localPlayer.Spectating &&
				(self.Owner == localPlayer || localPlayer.IsAlliedWith(self.Owner)))
			{
				TextNotificationsManager.AddTransientLine(self.Owner, info.ReinstatedTextNotification);
				Game.Sound.PlayNotification(self.World.Map.Rules, self.Owner, "Speech",
					"BuildingCaptured", self.Owner.Faction.InternalName);
			}
		}

		// IProductionSpeedModifier: 100 = normal, 0 = halted
		int IProductionSpeedModifier.GetProductionSpeedModifier()
		{
			// Passive or defeat bar filling = fully halted
			if (isPassive || controlBar <= 0)
				return 0;

			var barPercent = controlBar * 100 / info.BarMax;
			if (barPercent >= info.SlowdownThreshold)
				return 100;

			// Linear scale: at threshold% = 100% speed, at 0% = 0% speed
			return barPercent * 100 / info.SlowdownThreshold;
		}

		// ISelectionBar: visible to all players
		// Shows control bar (green/yellow) or defeat bar (red)
		float ISelectionBar.GetValue()
		{
			if (controlBar > 0)
				return (float)controlBar / info.BarMax;

			// In defeat phase: show defeat bar filling up
			return (float)defeatBar / info.BarMax;
		}

		Color ISelectionBar.GetColor()
		{
			if (controlBar > 0)
			{
				var barPercent = controlBar * 100 / info.BarMax;
				if (barPercent > info.SlowdownThreshold)
					return Color.LimeGreen;

				return Color.Yellow;
			}

			// Defeat phase: red bar
			return Color.Red;
		}

		bool ISelectionBar.DisplayWhenEmpty => true;

		// IAlwaysVisibleBar: show the bar without selection when being contested
		bool IAlwaysVisibleBar.ShowBarWithoutSelection => controlBar < info.BarMax;
	}
}
