#region Copyright & License Information
/*
 * WW3MOD frontline-influence Phase 6 — ENGINEER ROUTE-OPENING (@experimental AI).
 *
 * The Phase-4 frontline strength profile can now say "the enemy line is thinnest in sector S" and
 * "the avenue that opens into S is a DESTROYED, REPAIRABLE bridge" (CrossingMap crossing Status ==
 * Repairable). Phases 5 spends the force it HAS across the crossings that already exist; this phase
 * MANUFACTURES a new land axis: when the weakest enemy sector's avenue is a repairable crossing, send
 * an engineer (e6, RepairsBridges) to the LegacyBridgeHut/BridgeHut with the RepairBridge order, screen
 * him with a few free-pool combat units while the repair runs, and — on success — release everyone and
 * let the now-INTACT avenue be picked up naturally by the Phase-5 man-the-line / weakest-point machinery
 * (the repaired bridge folds into a single ground component, so CrossingMap flips the avenue to Intact
 * and man-the-line/offense route across it for free). NO separate attack path is built here.
 *
 * COMMITMENT: every unit sent on the mission is staked in the shared PoiGoalGuard ledger — the engineer
 * under "bridge-repair:<hutId>", each screen unit under "bridge-screen:<hutId>" — so the offense free
 * pool / man-the-line can't poach a briefly-idle escort mid-mission. Commit-on-order gate is the same
 * CommitOnOrderMath.ShouldCommit every other executor uses. On release (success / timeout / failure) the
 * claims are dropped and the units re-enter the pool.
 *
 * FAILURE MODES (all bounded, all release the ledger): engineer dies en route ⇒ per-hut cooldown + a
 * bounded retry; hut unreachable / gone ⇒ abandon; bridge already repaired by arrival ⇒ success (someone
 * else opened it); mission timeout ⇒ cooldown + re-attempt later.
 *
 * GATING (byte-identity invariant, influence-stack.md §per-player-opt-in recipe): the module is declared
 * ONLY in the @experimental block (RequiresCondition: enable-ai-experimental) AND its behaviour is behind
 * the default-off RouteOpenEnabled flag (C# false default), so @stable / normal / human never instantiate
 * it and, even if they did, it is inert. It opts THIS player into the ControlField frontline profile only
 * when the flag is on (RequestFrontlineProfile), never a world-level global — the recipe the shared control
 * field now forces since @stable participates. Zero SharedRandom / LocalRandom draws; the scan stagger is a
 * deterministic countdown. Pure decision math lives in RouteOpenMath (NUnit-pinned, no World).
 */
#endregion

using System.Collections.Generic;
using System.Linq;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	// ============================================================
	// Pure ROUTE-OPEN decision math — engine-free, NUnit-pinned (RouteOpenMathTest). Ports verbatim into
	// the future v3 brain (only the profile/ledger plumbing is engine-specific). Integer/boolean-only,
	// deterministic, ZERO RNG (mirrors FrontlineAllocationMath / MissionCommitmentMath).
	// ============================================================
	public static class RouteOpenMath
	{
		/// <summary>The route-open TRIGGER: dispatch an engineer only when the module is enabled, the frontline
		/// profile is built, there IS a believed-weakest enemy frontier sector (<paramref name="weakestSector"/>
		/// != <see cref="FrontlineProfileMath.NoSector"/>), and that weakest sector's avenue is a REPAIRABLE
		/// destroyed crossing (<paramref name="repairableAvenueInWeakest"/>). Any of the four false ⇒ no-op — this
		/// pins the flag-off, no-profile, no-front, and "repairable crossing but NOT in the weakest sector" cases.</summary>
		public static bool ShouldDispatch(bool enabled, bool hasProfile, int weakestSector, bool repairableAvenueInWeakest)
			=> enabled && hasProfile && weakestSector != FrontlineProfileMath.NoSector && repairableAvenueInWeakest;

		/// <summary>Has the per-hut retry cooldown elapsed? A hut with no prior failure
		/// (<paramref name="hasPriorFailure"/> false) is always eligible; otherwise the cooldown must have fully
		/// elapsed since the last failure (<c>current − lastFail ≥ cooldown</c>, boundary inclusive).</summary>
		public static bool CooldownElapsed(bool hasPriorFailure, int lastFailTick, int currentTick, int cooldownTicks)
			=> !hasPriorFailure || currentTick - lastFailTick >= cooldownTicks;

		/// <summary>Is this hut still within its bounded per-hut retry budget? <paramref name="maxAttempts"/> ≤ 0
		/// means "unbounded" (never blocks); otherwise the number of prior attempts must be strictly below it.</summary>
		public static bool CanAttempt(int attempts, int maxAttempts)
			=> maxAttempts <= 0 || attempts < maxAttempts;

		/// <summary>Has the in-flight mission run past its timeout? True when <c>current − start ≥ timeout</c>
		/// (boundary inclusive). <paramref name="timeoutTicks"/> ≤ 0 disables the valve (never times out).</summary>
		public static bool MissionTimedOut(int startTick, int currentTick, int timeoutTicks)
			=> timeoutTicks > 0 && currentTick - startTick >= timeoutTicks;

		/// <summary>The hut's per-hut attempt count after a mission resolves: a SUCCESS resets it to 0 (a repaired
		/// bridge that is later re-destroyed must be a fresh target, not one that inherited a stale failure count and
		/// could exhaust its retry budget prematurely); a FAILURE increments it (bounded by
		/// <see cref="CanAttempt"/>). Pure so the reset-on-success semantics is pinned without a World.</summary>
		public static int NextAttemptCount(int current, bool success)
			=> success ? 0 : current + 1;

		/// <summary>How many combat units to actually pull for the screen: the desired size clamped to what is
		/// available, floored at zero. The mission proceeds on the engineer alone if the pool is empty (a best-
		/// effort screen — repairing the route is the objective), so this never blocks the dispatch.</summary>
		public static int ClampScreenSize(int desired, int available)
		{
			var n = desired < available ? desired : available;
			return n < 0 ? 0 : n;
		}
	}

	[TraitLocation(SystemActors.Player)]
	[Desc("WW3MOD frontline-influence Phase 6 (@experimental): when the frontline strength profile says the",
		"enemy line is thinnest in a sector whose avenue is a REPAIRABLE destroyed bridge, dispatch an engineer",
		"(RepairsBridges) to the hut with a small combat screen to open a NEW land axis. On repair, releases its",
		"commitments and lets the now-intact avenue be picked up by the Phase-5 man-the-line / weakest-point",
		"machinery. Default OFF (RouteOpenEnabled) + declared only in the @experimental block ⇒ byte-identical.")]
	public class EngineerRouteOpenBotModuleInfo : ConditionalTraitInfo
	{
		[Desc("MASTER FLAG. All Phase-6 behaviour is behind this default-off switch; only the @experimental",
			"block turns it on. Off ⇒ the module never touches the profile / ledger / orders (byte-identical).")]
		public readonly bool RouteOpenEnabled = false;

		[Desc("Ticks between route-open evaluation passes.")]
		public readonly int ScanInterval = 100;

		[Desc("Ticks an in-flight repair mission may run before it is abandoned (release commitments, cool down,",
			"re-attempt later). 0 disables the timeout valve.")]
		public readonly int MissionTimeoutTicks = 1500;

		[Desc("Ticks a hut is on cooldown after a FAILED attempt (engineer died en route / mission timed out),",
			"before this module will try it again.")]
		public readonly int RetryCooldownTicks = 900;

		[Desc("Bounded retries per hut: after this many failed attempts the module stops re-trying that hut for",
			"the match. 0 = unbounded.")]
		public readonly int MaxAttemptsPerHut = 3;

		[Desc("Desired combat-screen size (units) escorting/holding near the crossing during the repair. Clamped",
			"to what the free pool can spare; the mission still proceeds on the engineer alone if none are free.")]
		public readonly int ScreenSize = 3;

		[Desc("How far (map cells) the screen holds BEHIND the crossing, toward our own Supply Route — a standoff",
			"picket on the friendly-near-bank side so AutoTarget engages anyone approaching the repair.")]
		public readonly int ScreenStandoffCells = 3;

		[Desc("Commitment lifetime (ticks) staked in the shared PoiGoalGuard ledger for the engineer + each screen",
			"unit, refreshed every scan while the mission runs so the offense pool never poaches them mid-mission.")]
		public readonly int CommitTtlTicks = 400;

		[Desc("Actor types of the bot's home Supply Route — used to compute the 'behind the crossing' direction.")]
		public readonly HashSet<string> SupplyRouteTypes = new() { "supplyroute" };

		[Desc("Actor types NEVER recruited into the combat screen — owned by other modules (capturer, the engineer",
			"itself, supply trucks, scouts, transport carriers). Aircraft are handled by their own squad module.")]
		public readonly HashSet<string> ExcludedActorTypes = new()
		{
			"tecn", "tecn.america", "tecn.russia",
			"e6", "e6.america", "e6.russia",
			"truk",
			"humvee", "btr",
			"bradley", "bmp2", "m113"
		};

		public override void RulesetLoaded(Ruleset rules, ActorInfo ai)
		{
			base.RulesetLoaded(rules, ai);

			// Case-harden actor-name config (see ActorNameCase). SupplyRouteTypes is a hardcoded lowercase
			// default, so only the user-facing name sets need normalising.
			ActorNameCase.NormalizeInPlace(ExcludedActorTypes);
		}

		public override object Create(ActorInitializer init) { return new EngineerRouteOpenBotModule(init.Self, this); }
	}

	public class EngineerRouteOpenBotModule : ConditionalTrait<EngineerRouteOpenBotModuleInfo>, IBotTick
	{
		readonly World world;
		readonly Player player;

		int scanCountdown;

		ControlField controlField;
		PoiGoalGuard goalGuard;

		// Per-hut failure memory (bounded retry + cooldown). Keyed on the hut's stable ActorID.
		readonly Dictionary<uint, int> attemptsByHut = new();
		readonly Dictionary<uint, int> lastFailTickByHut = new();

		// One active mission at a time — a single new land axis. Null hut ⇒ idle.
		Actor missionHut;
		Actor missionEngineer;
		readonly List<Actor> missionScreen = new();
		int missionStartTick;

		static string RepairObjectiveKey(uint hutId) => "bridge-repair:" + hutId;
		static string ScreenObjectiveKey(uint hutId) => "bridge-screen:" + hutId;

		public EngineerRouteOpenBotModule(Actor self, EngineerRouteOpenBotModuleInfo info)
			: base(info)
		{
			world = self.World;
			player = self.Owner;
		}

		protected override void TraitEnabled(Actor self)
		{
			if (!Info.RouteOpenEnabled)
				return;

			// Deterministic scan stagger (NOT a random draw — influence-stack invariant: no LocalRandom in new
			// paths). A fixed offset is enough to keep this module's pass off the other modules' ticks.
			scanCountdown = Info.ScanInterval;

			controlField = world.WorldActor.TraitOrDefault<ControlField>();

			// Opt THIS player into the frontline strength profile (per-player recipe — never a world global).
			// @stable / normal / human never reach here (the module is not on their actor + the flag is off) ⇒
			// the profile arrays are never built for them ⇒ byte-identical.
			controlField?.RequestFrontlineProfile(player);

			goalGuard = player.PlayerActor.TraitOrDefault<PoiGoalGuard>();

			Log.Write("debug",
				$"[exp-route-open] TraitEnabled — player={player.PlayerName} screenSize={Info.ScreenSize} " +
				$"timeout={Info.MissionTimeoutTicks} cooldown={Info.RetryCooldownTicks} maxAttempts={Info.MaxAttemptsPerHut}");
		}

		protected override void TraitDisabled(Actor self)
		{
			// If the enabling condition toggles off mid-mission, release the in-flight commitments now instead of
			// leaking them until TTL self-expiry, and reset mission state. NOT a failure — no cooldown / attempt bump
			// (the disable is external, not the hut's fault). Mirrors the CompleteMission release path (reviewer NIT-3).
			if (missionHut == null)
				return;

			ReleaseCommitment(missionEngineer);
			foreach (var s in missionScreen)
				ReleaseCommitment(s);

			missionHut = null;
			missionEngineer = null;
			missionScreen.Clear();
		}

		void IBotTick.BotTick(IBot bot)
		{
			if (!Info.RouteOpenEnabled || player.WinState != WinState.Undefined || controlField == null)
				return;

			if (--scanCountdown > 0)
				return;
			scanCountdown = Info.ScanInterval;

			if (missionHut != null)
				TickActiveMission(bot);
			else
				TryStartMission(bot);
		}

		// --- Active mission ----------------------------------------------------------

		void TickActiveMission(IBot bot)
		{
			// Objective resolved (hut gone) or the bridge is no longer dead ⇒ someone (us, on success, or another
			// party) opened the crossing. Either way the axis is open: release and let Phase-5 pick it up.
			if (missionHut.IsDead || !missionHut.IsInWorld || !IsBridgeDead(missionHut))
			{
				CompleteMission(success: true);
				return;
			}

			var screenKey = ScreenObjectiveKey(missionHut.ActorID);

			// Repair IN PROGRESS: the engineer has entered the hut and triggered the repair (and, with
			// EnterBehaviour.Dispose, is already consumed — so it reads as "gone"). The bridge stays Dead until the
			// animation completes, so we must NOT treat the missing engineer as a failure here — hold the screen and
			// wait for the bridge to flip (success, above). This check precedes both the engineer-gone and timeout
			// gates for exactly that reason: a mission that is succeeding must never be recorded as failed.
			if (IsBridgeRepairing(missionHut))
			{
				foreach (var s in missionScreen)
					if (s != null && !s.IsDead && s.IsInWorld && s.Owner == player)
						RefreshCommitment(s, screenKey);
				return;
			}

			var engineerAlive = missionEngineer != null && !missionEngineer.IsDead && missionEngineer.IsInWorld
				&& missionEngineer.Owner == player;

			// Engineer died / lost en route with the bridge still dead and NOT yet under repair ⇒ failure: cool the
			// hut down + bounded retry.
			if (!engineerAlive)
			{
				CompleteMission(success: false);
				return;
			}

			// Mission ran too long (and the repair never started) ⇒ abandon (release, cooldown, re-attempt later).
			if (RouteOpenMath.MissionTimedOut(missionStartTick, world.WorldTick, Info.MissionTimeoutTicks))
			{
				CompleteMission(success: false);
				return;
			}

			// Still en route: refresh commitments so the offense pool never poaches the engineer or the screen, and
			// re-issue the repair order if the engineer somehow went idle (activity cancelled) with the bridge dead.
			RefreshCommitment(missionEngineer, RepairObjectiveKey(missionHut.ActorID));
			if (missionEngineer.IsIdle)
				bot.QueueOrder(new Order("RepairBridge", missionEngineer, Target.FromActor(missionHut), false));

			foreach (var s in missionScreen)
				if (s != null && !s.IsDead && s.IsInWorld && s.Owner == player)
					RefreshCommitment(s, screenKey);
		}

		void CompleteMission(bool success)
		{
			var hutId = missionHut.ActorID;

			// Release every commitment we still hold (dead actors release harmlessly).
			ReleaseCommitment(missionEngineer);
			foreach (var s in missionScreen)
				ReleaseCommitment(s);

			attemptsByHut.TryGetValue(hutId, out var n);
			var next = RouteOpenMath.NextAttemptCount(n, success);
			if (next == 0)
			{
				// SUCCESS (or a reset): clear the hut's failure memory so a later re-destroyed bridge is a fresh
				// target and doesn't inherit a stale attempt count / cooldown (reviewer NIT-2).
				attemptsByHut.Remove(hutId);
				lastFailTickByHut.Remove(hutId);
			}
			else
			{
				attemptsByHut[hutId] = next;
				lastFailTickByHut[hutId] = world.WorldTick;
			}

			Log.Write("debug",
				$"[exp-route-open] mission {(success ? "COMPLETE" : "FAILED")} player={player.PlayerName} " +
				$"hut={hutId} attempts={(attemptsByHut.TryGetValue(hutId, out var a) ? a : 0)} tick={world.WorldTick}");

			missionHut = null;
			missionEngineer = null;
			missionScreen.Clear();
		}

		// --- Mission start -----------------------------------------------------------

		void TryStartMission(IBot bot)
		{
			if (!controlField.HasFrontlineProfile(player))
				return;

			var weakestSector = controlField.WeakestEnemySector(player);

			// Find a repairable avenue in the weakest sector whose hut is a live, dead-bridge, off-cooldown,
			// under-retry-budget target. The pure trigger keys on whether such an avenue exists.
			var hut = FindRepairTargetInWeakestSector(weakestSector, out var hutCell);
			var repairableAvenueInWeakest = hut != null;

			if (!RouteOpenMath.ShouldDispatch(Info.RouteOpenEnabled, controlField.HasFrontlineProfile(player),
				weakestSector, repairableAvenueInWeakest))
				return;

			// Need an available engineer (RepairsBridges) we own, not already committed elsewhere. Nearest to the hut.
			var engineer = FindAvailableEngineer(hutCell);
			if (engineer == null)
				return;

			// Own SR — the "behind the crossing" reference for the screen standoff.
			var ownSR = world.Actors.FirstOrDefault(a =>
				a.Owner == player && !a.IsDead && a.IsInWorld && Info.SupplyRouteTypes.Contains(a.Info.Name));
			if (ownSR == null)
				return;

			// Assemble the screen from the ledger-checked free pool (best effort up to ScreenSize).
			var screen = RecruitScreen(hutCell, engineer);

			// Dispatch: engineer → RepairBridge on the hut; commit under bridge-repair:<hutId>.
			bot.QueueOrder(new Order("RepairBridge", engineer, Target.FromActor(hut), false));
			CommitUnit(engineer, RepairObjectiveKey(hut.ActorID));

			// Screen holds a standoff behind the crossing (toward our SR); commit under bridge-screen:<hutId>.
			var holdCell = ShiftToward(hutCell, ownSR.Location, Info.ScreenStandoffCells);
			if (!world.Map.Contains(holdCell))
				holdCell = hutCell;

			missionScreen.Clear();
			var screenKey = ScreenObjectiveKey(hut.ActorID);
			foreach (var s in screen)
			{
				// No acceptance, no claim and no roster entry — a screen unit listed but never ordered would
				// sit at the SR while the mission believes the bridge is screened.
				if (!bot.QueueOrder(new Order("AttackMove", s, Target.FromCell(world, holdCell), false)))
					continue;

				CommitUnit(s, screenKey);
				missionScreen.Add(s);
			}

			missionHut = hut;
			missionEngineer = engineer;
			missionStartTick = world.WorldTick;

			AIUtils.BotDebug("AI ({0}): route-open — engineer {1} → repair hut {2} (weakest sector {3}), screen {4}",
				player.ClientIndex, engineer.Info.Name, hutCell, weakestSector, screen.Count);
			Log.Write("debug",
				$"[exp-route-open] DISPATCH player={player.PlayerName} hut={hut.ActorID}@{hutCell} " +
				$"weakestSector={weakestSector} engineer={engineer.ActorID} screen={screen.Count} tick={world.WorldTick}");
		}

		// The live bridge hut for a repairable avenue in the weakest sector that is (a) present, (b) still dead,
		// (c) off cooldown, (d) under its retry budget. Deterministic (avenues are in a fixed mapping order; the
		// hut lookup is by exact cell). Returns null when no such target exists (⇒ trigger is false).
		Actor FindRepairTargetInWeakestSector(int weakestSector, out CPos hutCell)
		{
			hutCell = default;
			if (weakestSector == FrontlineProfileMath.NoSector)
				return null;

			foreach (var avenue in controlField.AvenuesForSector(weakestSector))
			{
				if (avenue.Status != CrossingStatus.Repairable)
					continue;

				var hut = FindBridgeHutAt(avenue.Cell);
				if (hut == null || !IsBridgeDead(hut))
					continue;

				var hutId = hut.ActorID;
				var hasPriorFailure = lastFailTickByHut.TryGetValue(hutId, out var lastFail);
				attemptsByHut.TryGetValue(hutId, out var attempts);

				if (!RouteOpenMath.CanAttempt(attempts, Info.MaxAttemptsPerHut))
					continue;
				if (!RouteOpenMath.CooldownElapsed(hasPriorFailure, lastFail, world.WorldTick, Info.RetryCooldownTicks))
					continue;

				hutCell = avenue.Cell;
				return hut;
			}

			return null;
		}

		Actor FindBridgeHutAt(CPos cell)
			=> world.Actors.FirstOrDefault(a =>
				!a.IsDead && a.IsInWorld
				// world.Actors includes non-positional actors (Player, World) whose OccupiesSpace is null —
				// Actor.Location would NRE on them, so filter by trait (and null-guard) BEFORE touching Location.
				&& (a.Info.HasTraitInfo<LegacyBridgeHutInfo>() || a.Info.HasTraitInfo<BridgeHutInfo>())
				&& a.OccupiesSpace != null && a.Location == cell);

		// Nearest own engineer (RepairsBridges) that is alive, positionable, and NOT already committed to another
		// task. Deterministic (nearest by squared distance, tie-break lowest ActorID).
		Actor FindAvailableEngineer(CPos hutCell)
		{
			Actor best = null;
			var bestDist = long.MaxValue;
			foreach (var actor in world.Actors)
			{
				if (actor.Owner != player || actor.IsDead || !actor.IsInWorld)
					continue;
				if (!actor.Info.HasTraitInfo<RepairsBridgesInfo>())
					continue;
				if (actor.OccupiesSpace == null)
					continue;
				if (goalGuard != null && goalGuard.Ledger.IsCommitted(actor, world.WorldTick))
					continue;

				var dx = actor.Location.X - hutCell.X;
				var dy = actor.Location.Y - hutCell.Y;
				var d = (long)dx * dx + (long)dy * dy;
				if (d < bestDist || (d == bestDist && (best == null || actor.ActorID < best.ActorID)))
				{
					bestDist = d;
					best = actor;
				}
			}

			return best;
		}

		// Recruit up to ScreenSize combat units from the ledger-checked free pool, nearest the crossing first.
		// Eligible = own, armed (AttackBase/AutoTarget), mobile, idle, not excluded, not the engineer, not
		// committed. Deterministic (sort by distance then ActorID).
		List<Actor> RecruitScreen(CPos hutCell, Actor engineer)
		{
			var candidates = new List<Actor>();
			foreach (var actor in world.Actors)
			{
				if (actor == engineer || actor.Owner != player || actor.IsDead || !actor.IsInWorld || !actor.IsIdle)
					continue;
				if (Info.ExcludedActorTypes.Contains(actor.Info.Name.ToLowerInvariant()))
					continue;
				if (!actor.Info.HasTraitInfo<AttackBaseInfo>() && !actor.Info.HasTraitInfo<AutoTargetInfo>())
					continue;
				if (!actor.Info.HasTraitInfo<IPositionableInfo>())
					continue;
				if (goalGuard != null && goalGuard.Ledger.IsCommitted(actor, world.WorldTick))
					continue;

				candidates.Add(actor);
			}

			candidates.Sort((a, b) =>
			{
				var da = SqDist(a.Location, hutCell);
				var db = SqDist(b.Location, hutCell);
				if (da != db)
					return da.CompareTo(db);
				return a.ActorID.CompareTo(b.ActorID);
			});

			var take = RouteOpenMath.ClampScreenSize(Info.ScreenSize, candidates.Count);
			return candidates.GetRange(0, take);
		}

		// --- Ledger helpers ----------------------------------------------------------

		void CommitUnit(Actor unit, string objective)
		{
			if (CommitOnOrderMath.ShouldCommit(Info.RouteOpenEnabled, goalGuard != null && !goalGuard.IsTraitDisabled))
				goalGuard.Ledger.Commit(unit, objective, world.WorldTick, Info.CommitTtlTicks);
		}

		void RefreshCommitment(Actor unit, string objective) => CommitUnit(unit, objective);

		void ReleaseCommitment(Actor unit)
		{
			if (unit != null && goalGuard != null && !goalGuard.IsTraitDisabled)
				goalGuard.Ledger.Release(unit);
		}

		// --- Misc helpers ------------------------------------------------------------

		static bool IsBridgeDead(Actor hut)
		{
			var legacy = hut.TraitOrDefault<LegacyBridgeHut>();
			if (legacy != null)
				return legacy.BridgeDamageState == DamageState.Dead;

			var modern = hut.TraitOrDefault<BridgeHut>();
			if (modern != null)
				return modern.BridgeDamageState == DamageState.Dead;

			return false;
		}

		// The hut's repair is under way (engineer entered, animation running). While true the BridgeDamageState is
		// still Dead but the mission is succeeding — see TickActiveMission for why this must gate before failure.
		static bool IsBridgeRepairing(Actor hut)
		{
			var legacy = hut.TraitOrDefault<LegacyBridgeHut>();
			if (legacy != null)
				return legacy.Repairing;

			var modern = hut.TraitOrDefault<BridgeHut>();
			if (modern != null)
				return modern.Repairing;

			return false;
		}

		static long SqDist(CPos a, CPos b)
		{
			var dx = a.X - b.X;
			var dy = a.Y - b.Y;
			return (long)dx * dx + (long)dy * dy;
		}

		// Shift `from` toward `toward` by `cells` map cells. Coincident points return `from` unchanged.
		static CPos ShiftToward(CPos from, CPos toward, int cells)
		{
			var dx = toward.X - from.X;
			var dy = toward.Y - from.Y;
			var len = System.Math.Sqrt(dx * dx + dy * dy);
			if (len < 1)
				return from;

			var sx = (int)System.Math.Round(dx / len * cells);
			var sy = (int)System.Math.Round(dy / len * cells);
			return new CPos(from.X + sx, from.Y + sy);
		}
	}
}
