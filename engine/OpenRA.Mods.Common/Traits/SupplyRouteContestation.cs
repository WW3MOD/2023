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

		// Accessors for AttackSupplyRoute / external queries.
		public SupplyRouteContestationInfo Info => info;
		public int ControlBar => controlBar;
		public int ControlBarFraction => info.BarMax > 0 ? controlBar * 100 / info.BarMax : 0;
		public int NetEnemySurplus => cachedNetEnemySurplus;
		public int NetFriendlySurplus => cachedNetFriendlySurplus;
		public bool IsPassive => isPassive;

		void INotifyAddedToWorld.AddedToWorld(Actor self)
		{
			radarPings = self.World.WorldActor.TraitOrDefault<MiniMapPings>();
			proximityTrigger = self.World.ActorMap.AddProximityTrigger(
				self.CenterPosition, info.Range, WDist.Zero, ActorEntered, ActorLeft);

			// Stagger so multiple Supply Routes don't recompute force values on the same tick.
			scanTick = self.World.SharedRandom.Next(0, info.ScanInterval);
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
			// Player already defeated or SR changed to non-playable owner (e.g. Neutral after defeat) — nothing to do
			if (self.Owner.WinState != WinState.Undefined || self.Owner.NonCombatant || !self.Owner.Playable)
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

			// Become passive immediately — production halts and the bar drives notifications.
			isPassive = true;
			TextNotificationsManager.AddSystemLine(self.Owner.PlayerName + " has lost their Supply Route! Production and income frozen.");

			var localPlayer = self.World.LocalPlayer;
			if (localPlayer != null && !localPlayer.Spectating &&
				(self.Owner == localPlayer || localPlayer.IsAlliedWith(self.Owner)))
			{
				TextNotificationsManager.AddTransientLine(self.Owner, info.PassiveTextNotification);
			}

			// If any allied player still has a non-passive Supply Route, the team is still in play —
			// stay passive and wait for either reinstatement or the last team SR to fall.
			// Otherwise (no remaining active team SRs, or no allies at all) the entire team is defeated.
			if (!HasActiveTeamSupplyRoute())
				ResolveTeamElimination();
		}

		bool HasActiveTeamSupplyRoute()
		{
			foreach (var actor in self.World.ActorsHavingTrait<SupplyRouteContestation>())
			{
				if (actor == self || actor.Disposed || !actor.IsInWorld)
					continue;

				var owner = actor.Owner;
				if (owner.NonCombatant || !owner.Playable)
					continue;

				if (!owner.IsAlliedWith(self.Owner))
					continue;

				// An ally that has already won means the whole team has won — the team is not
				// eliminable and this SR's owner must NOT be defeated. Without this, an ally's
				// victory (which skips it below via the Undefined guard) would leave a still-in-play
				// teammate looking "unsupported" and drive ResolveTeamElimination to mark it Lost,
				// producing a winning team with one member wrongly shown as defeated.
				if (owner.WinState == WinState.Won)
					return true;

				if (owner.WinState != WinState.Undefined)
					continue;

				if (!actor.Trait<SupplyRouteContestation>().isPassive)
					return true;
			}

			return false;
		}

		// The team whose last active Supply Route just fell (this SR's owner + its allies) is
		// defeated. The win is then awarded EXPLICITLY rather than left to
		// ConquestVictoryConditions to infer ("all my enemies are Lost") — in a near-simultaneous
		// mutual overrun the losing side is resolved before the inference tick runs, so the
		// survivors would never be credited, previously leaving every player marked Lost and the
		// end screen reading "mission failed".
		//
		// Two phases: first mark the eliminated team Lost, THEN award the win only to survivors
		// whose every hostile is now Lost (ShouldAwardVictory). This mirrors CVC's win test so
		// FFA / 2v2v2 correctly defers the win until one party remains — the naive "everyone not
		// allied to the loser wins" would instantly end a 3-way game after the first elimination.
		// The WinState guards keep a simultaneous second elimination a no-op, so tick order alone
		// picks the winner instead of defeating everyone.
		void ResolveTeamElimination()
		{
			// TestMode owns win/loss verdicts (see ConquestVictoryConditions.Tick / CheckIfGameIsOver);
			// don't emit stray victory lines or sounds during autotests.
			if (TestMode.IsActive)
				return;

			// Phase 1: mark the eliminated team (this SR's owner + its allies) Lost — but never a
			// team that already has a victor. If a teammate has already Won (its enemies were
			// resolved a tick earlier), the team has won; a still-Undefined member must be awarded
			// in Phase 2, not defeated here. Otherwise the winning team ends with one member Lost.
			foreach (var p in self.World.Players)
			{
				if (p.NonCombatant || !p.Playable || p.WinState != WinState.Undefined)
					continue;

				if (p != self.Owner && !p.IsAlliedWith(self.Owner))
					continue;

				if (TeamAlreadyWon(p))
					continue;

				var mo = p.PlayerActor.TraitOrDefault<MissionObjectives>();
				if (mo == null)
					continue;

				var objectiveId = mo.Add(p, "Hold the Supply Route", "Primary", inhibitAnnouncement: true);
				mo.MarkFailed(p, objectiveId);
			}

			// Phase 2: award the win to any surviving combatant whose every hostile is now Lost.
			foreach (var p in self.World.Players)
			{
				if (p.NonCombatant || !p.Playable || p.WinState != WinState.Undefined)
					continue;

				if (!ShouldAwardVictory(OtherCombatants(p)))
					continue;

				var mo = p.PlayerActor.TraitOrDefault<MissionObjectives>();
				if (mo == null)
					continue;

				AwardVictory(p, mo);
			}
		}

		// (allied-to-survivor, win-state) for every other combatant — the input to ShouldAwardVictory.
		IEnumerable<(bool Allied, WinState State)> OtherCombatants(Player survivor)
		{
			return self.World.Players
				.Where(o => o != survivor && !o.NonCombatant && o.Playable)
				.Select(o => (survivor.IsAlliedWith(o), o.WinState));
		}

		// True when a teammate of this player (an ally, not the player itself) has already Won.
		// Such a team has won and none of its members may be eliminated.
		bool TeamAlreadyWon(Player player)
		{
			return TeamHasVictor(self.World.Players
				.Where(o => o != player && !o.NonCombatant && o.Playable)
				.Select(o => (player.IsAlliedWith(o), o.WinState)));
		}

		// Pure decision: has a teammate already won? Any allied combatant marked Won means the team
		// is victorious, so a still-Undefined member must be awarded rather than eliminated. This is
		// the invariant that prevents a winning team from ending with one member Lost when its
		// members' win/elimination events land on different ticks (e.g. an ally clinches the win via
		// ConquestVictoryConditions one tick before this SR's defeat bar fills).
		public static bool TeamHasVictor(IEnumerable<(bool Allied, WinState State)> otherCombatants)
		{
			foreach (var other in otherCombatants)
				if (other.Allied && other.State == WinState.Won)
					return true;

			return false;
		}

		// Pure decision: should this surviving combatant be awarded the win? True only when every
		// hostile (non-allied) combatant is already Lost — mirrors ConquestVictoryConditions so
		// FFA / multi-team games defer the win until one party remains, while 2v2 awards
		// immediately once the enemy team has been marked Lost. Allies (Undefined or Won) never
		// block — they win together.
		public static bool ShouldAwardVictory(IEnumerable<(bool Allied, WinState State)> otherCombatants)
		{
			foreach (var other in otherCombatants)
				if (!other.Allied && other.State != WinState.Lost)
					return false;

			return true;
		}

		// Pure decision: what a team-elimination event implies for a member of the eliminated team.
		// Returns null when the player is already decided (no change) — the invariant that stops a
		// simultaneous second elimination from flipping the winners to Lost.
		public static WinState? ResolveEliminationOutcome(WinState current, bool onEliminatedTeam)
		{
			if (current != WinState.Undefined)
				return null;

			return onEliminatedTeam ? WinState.Lost : WinState.Won;
		}

		// Firing OnPlayerWon requires ALL required objectives to be Completed. We only complete the
		// conquest primary objective that ConquestVictoryConditions creates — NOT the whole list —
		// so an SR contest can never force-complete scripted campaign objectives. Players without a
		// ConquestVictoryConditions trait (e.g. campaign, -ConquestVictoryConditions) are left to
		// their scenario logic entirely.
		static void AwardVictory(Player p, MissionObjectives mo)
		{
			if (p.PlayerActor.TraitOrDefault<ConquestVictoryConditions>() == null)
				return;

			var completedAny = false;
			for (var id = 0; id < mo.Objectives.Count; id++)
			{
				if (mo.Objectives[id].State == ObjectiveState.Incomplete && mo.Objectives[id].Type == "Primary")
				{
					completedAny = true;
					mo.MarkCompleted(p, id);
				}
			}

			// CVC present but its objective not registered yet — add and complete our own.
			if (!completedAny)
			{
				var objectiveId = mo.Add(p, "Hold the Supply Route", "Primary", inhibitAnnouncement: true);
				mo.MarkCompleted(p, objectiveId);
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
