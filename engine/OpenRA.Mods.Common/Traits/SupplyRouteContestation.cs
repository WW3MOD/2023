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
		"When control bar is fully depleted, a defeat bar fills. At 100% defeat bar, the player",
		"becomes passive if a teammate still holds an active Supply Route (they can be relieved),",
		"and is defeated outright if nobody is left to relieve them.")]
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

		// Durations here are quoted at the mod's real tick rate. mod.yaml:358 selects the `default`
		// game speed, whose Timestep is 60ms (mod.yaml:381) = 16.67 ticks/second, NOT the 25 tps these
		// comments claimed before. Every duration in this file was understated by 1.5x.
		[Desc("Ticks to deplete bar from full at ReferenceValue net enemy surplus. (90s at 16.67 tps)")]
		public readonly int BaseTicks = 1500;

		[Desc("Minimum ticks to deplete bar from full, regardless of enemy surplus. (30s at 16.67 tps)")]
		public readonly int MinTicks = 500;

		[Desc("Team army value at or above which contestation runs at its unmodified rate.",
			"Below this, depletion accelerates toward MaxCollapseSpeedup as the defending team is ground down.",
			"Only mobile actors count — a building cannot move to relieve a besieged Supply Route.")]
		public readonly int CollapseThreshold = 5000;

		[Desc("Maximum depletion speedup, as a percentage, reached when the defending team has nothing",
			"mobile left anywhere on the map. 100 = no speedup.")]
		public readonly int MaxCollapseSpeedup = 800;

		[Desc("Floor on ticks to deplete the bar from full when the defending team is at zero army value.",
			"Replaces MinTicks as the team is ground down. (~5s at 16.67 tps)")]
		public readonly int CollapseMinTicks = 80;

		[Desc("Ticks to fill the defeat bar once production is already hard-halted (control bar empty)",
			"AND the defending team has no mobile units left anywhere. The outcome is settled at that",
			"point, so the loser is not made to watch the bar. (~1s at 16.67 tps)")]
		public readonly int LockoutCollapseTicks = 17;

		[Desc("Ticks to recover bar from zero to full with no friendlies present. (180s at 16.67 tps)")]
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

		// Not "production and income": passivity's only behavioural hook is IProductionSpeedModifier.
		// Nothing in this trait — or anywhere reading IsPassive — touches PlayerResources, so a passive
		// player's CashTrickler and passive income keep accruing while this line claims otherwise.
		[Desc("Text notification when player becomes passive.")]
		public readonly string PassiveTextNotification = "Supply Route overrun! Production frozen.";

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

	// ISync is load-bearing here, not decoration: Actor.cs:206 hashes a trait only when `trait is ISync`,
	// so without it the [Sync] members below were inert and this trait was absent from every sync report.
	// This trait decides production speed and player defeat, so an unhashed divergence here ends the
	// game differently on the two machines with nothing in the report to say so.
	//
	// This trait needed real per-field judgement rather than a blanket annotation, because it also
	// implements ISelectionBar/IAlwaysVisibleBar and carries genuinely client-local state. The
	// exclusions below are as deliberate as the inclusions — see lastNotifyTime in particular.
	public class SupplyRouteContestation : ITick, ISelectionBar, IAlwaysVisibleBar, IProductionSpeedModifier,
		INotifyAddedToWorld, INotifyRemovedFromWorld, ISync
	{
		readonly SupplyRouteContestationInfo info;
		readonly Actor self;

		// Not hashable (the hasher takes only int, bool and 11 built-in types), but its aggregate IS
		// hashed via the two cached surpluses below, which is the coverage that matters.
		readonly List<Actor> actorsInRange = new List<Actor>();

		// An opaque registration handle from ActorMap.AddProximityTrigger, used only to unregister in
		// RemovedFromWorld. Deterministic, but it encodes no decision — deliberately not hashed.
		int proximityTrigger;

		// SIMULATION. Feeds IProductionSpeedModifier (production speed is simulation, not display) and
		// `controlBar > 0` selects the deplete-vs-defeat phase. ISelectionBar reads it too, but the
		// render path only ever READS it — that is what makes hashing it correct rather than dangerous.
		[Sync]
		int controlBar;

		// SIMULATION. `defeatBar >= BarMax` calls OnDefeatBarFull, which makes the player passive and
		// can resolve the whole game's win/loss.
		[Sync]
		int defeatBar;

		// SIMULATION, and the best canaries in this trait — both previously unannotated. The sign of
		// cachedNetEnemySurplus selects the entire Tick branch and scales CalculateTickRate; the
		// friendly surplus multiplies the recovery rate. They are derived from actorsInRange, which is
		// maintained by ActorMap proximity-trigger callbacks — precisely the kind of state that can
		// diverge without any other symptom until the bar timings drift apart.
		[Sync]
		int cachedNetEnemySurplus;

		[Sync]
		int cachedNetFriendlySurplus;

		// SIMULATION. Total Valued.Cost of every mobile actor owned by an undefeated combatant on the
		// defender's team, anywhere on the map — the global counterpart to the two local surpluses
		// above. It scales the depletion rate and, at zero, triggers the lockout collapse, so it feeds
		// the win/loss decision just as directly as they do.
		[Sync]
		int cachedTeamValue;

		// Memoized Valued.Cost per actor type. Pure ruleset data (an ActorInfo's trait list is fixed at
		// load), so this is deterministic and identical on every client; it exists only to keep the
		// map-wide scan off ActorInfo's linear trait lookup. Deliberately not hashed — it decides
		// nothing, and cachedTeamValue already carries every value read out of it.
		readonly Dictionary<ActorInfo, int> costCache = new Dictionary<ActorInfo, int>();

		// SIMULATION, previously unannotated. Seeded from World.SharedRandom in AddedToWorld and then
		// decides WHICH tick RecalculateForces runs on, so a divergence shifts the entire bar timeline.
		// Being RNG-seeded also makes it a direct check that the shared stream was aligned at actor-add.
		[Sync]
		int scanTick;

		// DELIBERATELY NOT [Sync] — hashing these would be actively harmful, for two independent
		// reasons, and this is the exclusion that most needed getting right.
		// 1. They are assigned from Game.RunTime: local wall-clock milliseconds since this process
		//    started. Two clients launch at different moments, so these values legitimately DIFFER on
		//    every machine. Hashing them would manufacture a desync report in every multiplayer game
		//    — turning the sync system into a false-positive generator rather than a detector.
		// 2. They are `long`, which Sync.EmitSyncOpcodes rejects outright: the annotation would compile
		//    and then throw NotImplementedException the first time a Supply Route was hashed.
		// They only gate notification/ping/flash rate limiting. That path is hash-inert: the FlashTarget
		// it queues is a plain IEffect, never enters World.SyncedEffects, and so cannot reach the hash.
		long lastNotifyTime;
		long lastDefeatNotifyTime;

		// Deliberately NOT [Sync]. Notification latches: their only consumers are OnContestationStarted
		// and OnDefeatPhaseStarted, which do speech, radar pings and a screen flash — never a
		// simulation decision. They are written solely from comparisons on controlBar/defeatBar, both of
		// which ARE hashed, so they carry no divergence signal those two do not already carry.
		bool wasContested;
		bool wasInDefeatPhase;

		// SIMULATION. Forces the production modifier to 0 and is the flag HasActiveTeamSupplyRoute reads
		// across every player when deciding team elimination.
		[Sync]
		bool isPassive;

		// Render-only trait reference (radar pings); not hashable and not simulation.
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
		public int TeamValue => cachedTeamValue;
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

			RecalculateTeamValue();
		}

		int CostOf(ActorInfo actorInfo)
		{
			if (costCache.TryGetValue(actorInfo, out var cost))
				return cost;

			var valued = actorInfo.TraitInfoOrDefault<ValuedInfo>();
			return costCache[actorInfo] = valued != null ? Math.Max(0, valued.Cost) : 0;
		}

		// Everything the defending team still has that could move to relieve this Supply Route.
		//
		// Mobility is the whole criterion, and it is what makes "no units left" mean what a player
		// expects. Buildings are excluded deliberately: a turret on the far side of the map can never
		// affect this fight, and static defence that CAN affect it is already priced by
		// RecalculateForces as friendly value inside the contestation circle. Excluding them is also
		// what lets the total reach zero at all — every live player owns at least the Supply Route
		// itself, so a building-inclusive total could never hit the floor the lockout collapse needs.
		//
		// Team-wide, not per-player, because the user asked for "how strong that team is": a 2v2 ally
		// with a live army can still march over, so their SR must not fall at collapse speed. SameTeam
		// rather than IsAlliedWith for the reason documented on SameTeam itself — RelationshipWith
		// reports every Spectating player as an Ally, so a defeated player's leftovers would otherwise
		// prop up the survivors' rate.
		void RecalculateTeamValue()
		{
			var total = 0;
			foreach (var a in self.World.ActorsHavingTrait<IMove>())
			{
				if (a.Disposed || !a.IsInWorld)
					continue;

				var owner = a.Owner;
				if (owner.NonCombatant || !owner.Playable || owner.WinState != WinState.Undefined)
					continue;

				if (!SameTeam(owner, self.Owner))
					continue;

				total += CostOf(a.Info);
			}

			cachedTeamValue = total;
		}

		int CalculateTickRate(int valueSurplus)
		{
			var ticksToFull = ContestTicksToFull(valueSurplus, CollapseWeakness(cachedTeamValue, info.CollapseThreshold),
				info.BaseTicks, info.ReferenceValue, info.MinTicks, info.CollapseMinTicks, info.MaxCollapseSpeedup);
			return BarRate(info.BarMax, ticksToFull);
		}

		// Pure decision: bar units moved per tick to drain BarMax over ticksToFull ticks. Integer
		// division rounds the rate DOWN, so the realised drain is a little slower than the requested
		// ticksToFull — up to one tick per unit of truncation. Exposed so tests can quote realised
		// durations rather than the requested ones.
		public static int BarRate(int barMax, int ticksToFull)
		{
			return Math.Max(1, barMax / Math.Max(1, ticksToFull));
		}

		// Pure decision: how far the defending team has been ground down, 0 (healthy, or better than
		// healthy) to 100 (nothing mobile left anywhere). Linear, so there is no cliff for a player to
		// play around and no single unit whose death flips the rate.
		public static int CollapseWeakness(int teamValue, int collapseThreshold)
		{
			if (collapseThreshold <= 0)
				return 0;

			var clamped = Math.Min(Math.Max(teamValue, 0), collapseThreshold);
			return (collapseThreshold - clamped) * 100 / collapseThreshold;
		}

		// Pure decision: ticks to drain a full bar, given the attacker's local surplus and how beaten
		// the defending team is globally.
		//
		// The weakness term SCALES the existing local result rather than replacing it, and that is the
		// property that keeps this from becoming a new way to win. Replacing it would let one cheap
		// scout end a player who momentarily owns nothing — and players DO momentarily own nothing:
		// StartingUnitsClass defaults to "none" (SpawnStartingUnits.cs:24), so on the default lobby
		// setting every player begins the match at zero army value, and rotating units out to the map
		// edge for a refund returns them there voluntarily. Scaled, that scout still needs minutes; a real finishing force reaches the
		// floor. The attacker must still bring something.
		public static int ContestTicksToFull(int enemySurplus, int weakness, int baseTicks, int referenceValue,
			int minTicks, int collapseMinTicks, int maxCollapseSpeedup)
		{
			var unfloored = (long)baseTicks * referenceValue / Math.Max(1, enemySurplus);

			// A healthy defending team is the entire contested-game population, and it must come out
			// byte-identical to the pre-collapse formula rather than merely close to it. Returning the
			// original expression here says so directly instead of relying on the scaling arithmetic
			// below to be a no-op at weakness 0.
			if (weakness <= 0)
				return (int)Math.Max(minTicks, unfloored);

			var speedup = 100 + ((long)(maxCollapseSpeedup - 100) * weakness / 100);
			var scaled = unfloored * 100 / Math.Max(1, speedup);
			var floor = minTicks - ((long)(minTicks - collapseMinTicks) * weakness / 100);

			return (int)Math.Max(1, Math.Max(floor, scaled));
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
					// Phase 2: Fill defeat bar (red fills up).
					//
					// Once the control bar is empty the owner can no longer call anything in —
					// GetProductionSpeedModifier returns 0 on exactly this condition, which is the hard
					// lockout (SlowdownThreshold only tapers production; it never stops it). If the team
					// also has nothing mobile left, no input to this outcome remains: they cannot buy,
					// and they have nothing to march over. Collapse it instead of making them watch.
					// Never a slowdown — Max, so a faster ordinary rate still wins.
					var defeatRate = cachedTeamValue <= 0
						? Math.Max(rate, Math.Max(1, info.BarMax / Math.Max(1, info.LockoutCollapseTicks)))
						: rate;

					defeatBar = Math.Min(info.BarMax, defeatBar + defeatRate);

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

			// ONE evaluation, TWO consumers — this is the whole point of the local.
			//
			// "Can anybody still relieve this Supply Route?" IS the passive-versus-defeated fork: true
			// means the owner freezes and can be rescued, false means the team is eliminated in this
			// same tick. The freeze notifications must be gated on THIS value and not on a re-derived
			// "is this a team game?", because no such question is answerable here: a lobby team of one,
			// the last survivor of a team, a player whose only ally is itself overrun, and a
			// free-for-all player are all indistinguishable and all unrescuable. Deriving the message
			// separately is what printed "has lost their Supply Route!" one line above "is defeated".
			//
			// Evaluated BEFORE isPassive is set for readability only — HasActiveTeamSupplyRoute skips
			// this actor, so the order does not affect the result.
			var rescuable = HasActiveTeamSupplyRoute();

			// Become passive immediately — production halts and the bar drives notifications.
			isPassive = true;

			if (rescuable)
			{
				TextNotificationsManager.AddSystemLine(self.Owner.PlayerName + " has lost their Supply Route! Production frozen.");

				var localPlayer = self.World.LocalPlayer;
				if (localPlayer != null && !localPlayer.Spectating &&
					(self.Owner == localPlayer || localPlayer.IsAlliedWith(self.Owner)))
				{
					TextNotificationsManager.AddTransientLine(self.Owner, info.PassiveTextNotification);
				}
			}
			else
			{
				// No remaining active team SRs, or no allies at all — the entire team is defeated.
				// Announcing a freeze here would be announcing a state the player never occupies for
				// longer than this tick; the defeat line that follows is the correct and only report.
				ResolveTeamElimination();
			}
		}

		bool HasActiveTeamSupplyRoute()
		{
			return HasRescuer(OtherSupplyRoutes());
		}

		// (same-team, win-state, passive) for every OTHER live Supply Route — the input to HasRescuer.
		// Lazy, so HasRescuer's short-circuit still stops the world scan at the first live teammate.
		IEnumerable<(bool SameTeam, WinState State, bool IsPassive)> OtherSupplyRoutes()
		{
			foreach (var actor in self.World.ActorsHavingTrait<SupplyRouteContestation>())
			{
				if (actor == self || actor.Disposed || !actor.IsInWorld)
					continue;

				var owner = actor.Owner;
				if (owner.NonCombatant || !owner.Playable)
					continue;

				yield return (SameTeam(owner, self.Owner), owner.WinState, actor.Trait<SupplyRouteContestation>().isPassive);
			}
		}

		// Pure decision: can this overrun Supply Route's owner still be relieved by a teammate? The
		// single predicate behind both halves of OnDefeatBarFull's fork — freeze-and-announce when
		// true, eliminate the team when false — so the two can never disagree about whether a rescue
		// is possible.
		public static bool HasRescuer(IEnumerable<(bool SameTeam, WinState State, bool IsPassive)> otherSupplyRoutes)
		{
			foreach (var sr in otherSupplyRoutes)
			{
				if (!sr.SameTeam)
					continue;

				// An ally that has already won means the whole team has won — the team is not
				// eliminable and this SR's owner must NOT be defeated. Without this, an ally's
				// victory (which skips it below via the Undefined guard) would leave a still-in-play
				// teammate looking "unsupported" and drive ResolveTeamElimination to mark it Lost,
				// producing a winning team with one member wrongly shown as defeated.
				if (sr.State == WinState.Won)
					return true;

				if (sr.State != WinState.Undefined)
					continue;

				// A passive teammate is itself waiting to be relieved and cannot relieve anyone.
				if (!sr.IsPassive)
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
			//
			// Membership and win state are SNAPSHOTTED before the first MarkFailed. MarkFailed sets
			// WinState synchronously; a Lost player is immediately Spectating, and RelationshipWith
			// reports every Spectating player as an Ally. Deciding membership live inside the marking
			// loop therefore dragged every player slotted AFTER the eliminated one onto the
			// "eliminated team" and cascaded the defeat across the whole game.
			// MissionObjectives.WorldLoaded caches its ally/enemy lists against the same hazard.
			var candidates = self.World.Players.Where(p => !p.NonCombatant && p.Playable).ToArray();
			var snapshot = candidates
				.Select(p => (OnEliminatedTeam: SameTeam(p, self.Owner) && !TeamAlreadyWon(p), State: p.WinState))
				.ToArray();

			var targets = SelectEliminationTargets(snapshot);

			for (var i = 0; i < candidates.Length; i++)
			{
				if (!targets[i])
					continue;

				var p = candidates[i];
				var mo = p.PlayerActor.TraitOrDefault<MissionObjectives>();
				if (mo == null)
					continue;

				var objectiveId = mo.Add(p, "Hold the Supply Route", "Primary", inhibitAnnouncement: true);
				mo.MarkFailed(p, objectiveId);
			}

			// Phase 2: award the win to any surviving combatant whose every hostile is now Lost.
			AwardDecidedSurvivors(self.World);
		}

		// Award the win to every still-Undefined combatant whose every hostile is now Lost. This is
		// the single, path-independent win-award: safe to call after ANY defeat — contestation
		// elimination (above), loss of all required units (ConquestVictoryConditions), surrender, or a
		// near-simultaneous mutual defeat. It is idempotent (ShouldAwardVictory + MarkCompleted only
		// touch Undefined/Incomplete state), so re-running it never flips a decided player. Firing it
		// synchronously the moment a hostile falls is what prevents the "everyone Lost, no winner"
		// end-screen: the naive alternative — leaving the win to ConquestVictoryConditions.Tick's
		// next-tick inference — no-ops once the survivor is itself marked Lost, so two defeats landing
		// in the same tick would resolve both to Lost before either inference ran.
		public static void AwardDecidedSurvivors(World world)
		{
			foreach (var p in world.Players)
			{
				if (p.NonCombatant || !p.Playable || p.WinState != WinState.Undefined)
					continue;

				if (!ShouldAwardVictory(OtherCombatants(world, p)))
					continue;

				var mo = p.PlayerActor.TraitOrDefault<MissionObjectives>();
				if (mo == null)
					continue;

				AwardVictory(p, mo);
			}
		}

		// (allied-to-survivor, win-state) for every other combatant — the input to ShouldAwardVictory.
		static IEnumerable<(bool Allied, WinState State)> OtherCombatants(World world, Player survivor)
		{
			return world.Players
				.Where(o => o != survivor && !o.NonCombatant && o.Playable)
				.Select(o => (SameTeam(survivor, o), o.WinState));
		}

		// True when a teammate of this player (an ally, not the player itself) has already Won.
		// Such a team has won and none of its members may be eliminated.
		bool TeamAlreadyWon(Player player)
		{
			return TeamHasVictor(self.World.Players
				.Where(o => o != player && !o.NonCombatant && o.Playable)
				.Select(o => (SameTeam(player, o), o.WinState)));
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

		// Win-state-immune "are a and b on the same team?", from a's point of view.
		// Deliberately NOT IsAlliedWith: RelationshipWith reports any Spectating player as an Ally, and
		// a player becomes Spectating the instant it is marked Lost or Won, so IsAlliedWith cannot
		// express "same team" once win/loss is being resolved. The lobby alliance masks are fixed at
		// world creation (CreateMapPlayers.SetupPlayerMasks, which unions a player's own mask for the
		// p == q pair) and are never touched by win state, so they say what these tests actually mean.
		// NOTE: unlike IsAlliedWith, which returns true for a null argument, this throws on a null a or
		// b. No current call site can pass null; keep it that way if you reuse this.
		static bool SameTeam(Player a, Player b)
		{
			return a == b || a.AlliedPlayersMask.Overlaps(b.PlayerMask);
		}

		// Pure decision: which of these candidates a team-elimination event marks Lost, index-parallel
		// to the input. The contract that matters is INDEPENDENCE — each verdict is a function of that
		// candidate's own (membership, win state) pair alone, so applying one verdict can never
		// reclassify another candidate. That is the invariant the caller relies on when it snapshots
		// membership up front, and it is what stops one player's defeat cascading onto the survivors
		// slotted after it.
		public static bool[] SelectEliminationTargets(IReadOnlyList<(bool OnEliminatedTeam, WinState State)> candidates)
		{
			var targets = new bool[candidates.Count];
			for (var i = 0; i < candidates.Count; i++)
				targets[i] = ResolveEliminationOutcome(candidates[i].State, candidates[i].OnEliminatedTeam) == WinState.Lost;

			return targets;
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
