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
using System.Linq;
using OpenRA.Mods.Common.Activities;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[TraitLocation(SystemActors.World)]
	[Desc("DOOMSDAY / \"Dead Hand\": the replacement for a plain time-limited game. When the clock reaches",
		"zero the map is annihilated by a staggered ICBM salvo instead of the match simply stopping.",
		"",
		"Named for the Soviet Perimeter system, which is what it is: an automatic retaliatory launch that",
		"nobody on the map ordered and nobody can stop.",
		"",
		"THE SEQUENCE, in order, and each stage is separately tunable:",
		"  1. The clock hits zero. Statistics freeze on that exact tick, before anything is launched.",
		"  2. A tight wave of SMALL warheads on the outliers: derricks, isolated structures, Supply Routes.",
		"  3. A deliberate pause, long enough to read as 'it is over'.",
		"  4. A wave of LARGE warheads on the population centres, two per city.",
		"  5. Fill warheads, only where the targeted salvo left the coverage guarantee open.",
		"  6. Everything still alive is destroyed, and the winner is resolved from the FROZEN score.",
		"",
		"Attach to the World actor. Requires " + nameof(TimeLimitManager) + ", which supplies the trigger.")]
	public class DoomsdayStrikeInfo : TraitInfo, ILobbyOptions, Requires<TimeLimitManagerInfo>
	{
		[Desc("Label for the lobby checkbox.")]
		public readonly string DoomsdayLabel = "Doomsday";

		[Desc("Tooltip for the lobby checkbox.")]
		public readonly string DoomsdayDescription = "When the Doomsday Clock expires, the map is destroyed by a nuclear salvo. The highest score at that moment wins.";

		[Desc("Default state of the lobby checkbox.")]
		public readonly bool DoomsdayEnabled = true;

		[Desc("Prevent the checkbox from being changed in the lobby.")]
		public readonly bool DoomsdayLocked = false;

		[Desc("Whether to show the checkbox in the lobby.")]
		public readonly bool DoomsdayCheckboxVisible = true;

		[Desc("Display order for the lobby checkbox.")]
		public readonly int DoomsdayCheckboxDisplayOrder = 62;

		[Desc("Run the salvo in TestMode sessions too. Defaults to false, which is what keeps the",
			"existing timed tournament and autotest configurations behaving exactly as they did:",
			"they set a time limit and expect the score comparison, not an apocalypse. The demo",
			"scenario sets this true.")]
		public readonly bool RunInTestMode = false;

		[ActorReference(typeof(BallisticMissileInfo))]
		[FieldLoader.Require]
		[Desc("Missile actor for the opening wave on outlier targets. Must carry " + nameof(BallisticMissile) + ".")]
		public readonly string OutlierMissile = null;

		[ActorReference(typeof(BallisticMissileInfo))]
		[FieldLoader.Require]
		[Desc("Missile actor for the city wave, and for the fill warheads.")]
		public readonly string CityMissile = null;

		[Desc("CONSERVATIVE lethal radius of the SMALLEST warhead in the salvo. The entire coverage",
			"guarantee is stated against this one number, so it must be at or below the radius within",
			"which the outlier warhead reliably kills — not its maximum effect radius, and not the",
			"city warhead's, which is larger and therefore covered by being over-provisioned.",
			"",
			"Raising this thins the salvo and is the field to check first if something survives.")]
		public readonly WDist LethalRadius = new(20 * 1024);

		[Desc("Maximum distance an aim point is displaced by the synced RNG. STRICTLY SMALLER than",
			nameof(LethalRadius) + ": placement is done against (LethalRadius - JitterRadius), so no draw",
			"can move an impact far enough to uncover something the layout had covered.")]
		public readonly WDist JitterRadius = new(4 * 1024);

		[Desc("Minimum distance between two impacts. 'Never detonating too many too close' — this is",
			"what thins a dense line of derricks down to a spread-out salvo.")]
		public readonly WDist MinSeparation = new(12 * 1024);

		[Desc("Two buildings within this distance of each other belong to the same city. Applied",
			"transitively, so a ribbon development links into one city rather than several.")]
		public readonly WDist CityLinkDistance = new(8 * 1024);

		[Desc("A cluster with at least this many buildings is a CITY and gets " + nameof(DoomsdayStrikeInfo.WarheadsPerCity),
			"large warheads. Anything smaller is an OUTLIER and gets one small warhead in the opening wave.")]
		public readonly int CityMinBuildings = 4;

		[Desc("Large warheads aimed at each city, spread about its centre along its long axis.")]
		public readonly int WarheadsPerCity = 2;

		[Desc("Actor types that are always outlier targets in their own right, regardless of what they",
			"cluster with — the high-value point targets. Oil derricks are the case the design names.")]
		public readonly HashSet<string> HighValueTypes = new() { "oilb", "supplyroute" };

		[Desc("Actor types excluded from target enumeration even though they are buildings. Walls and",
			"tank traps are structures to the engine and scenery to a targeteer.")]
		public readonly HashSet<string> ExcludeTypes = new() { "barb", "sbag", "fenc", "brik", "cycl", "tanktrap", "tanktrap2" };

		[Desc("Height above the aim point at which a warhead enters the map.")]
		public readonly WDist SpawnAltitude = new(38 * 1024);

		[Desc("Horizontal distance from the aim point at which a warhead enters the map. Together with",
			nameof(SpawnAltitude) + " this sets the TERMINAL ANGLE, which is the whole point: 38c0 over",
			"5c0 is a slope of 7.6, an 82.5-degree descent, against the ~10 degrees the shipped strike",
			"missiles fly. Re-entry, not an artillery arc.",
			"",
			"MUST BE NON-ZERO. BallisticMissileFly divides by the horizontal distance and completes",
			"immediately at zero (BallisticMissileFly.cs:281-283), so a purely vertical drop would",
			"teleport onto the target and detonate on its first tick.")]
		public readonly WDist ApproachDistance = new(5 * 1024);

		[Desc("Ticks between consecutive impacts inside one wave.")]
		public readonly int WithinWaveTicks = 2;

		[Desc("THE PAUSE: ticks between the last outlier impact and the first city impact. The single",
			"most important timing value in the sequence — long enough that the viewer has concluded it",
			"is over, short enough that the whole salvo stays inside a few seconds. 40 ticks is 2.4s at",
			"the mod's 60ms timestep.")]
		public readonly int OutlierToCityPauseTicks = 40;

		[Desc("Ticks between the last city impact and the first fill impact.")]
		public readonly int CityToFillPauseTicks = 25;

		[Desc("Ticks after the LAST impact before anything still alive is destroyed outright. This is",
			"the backstop that makes 'nothing survives' unconditional rather than contingent on warhead",
			"tuning — see the class remarks.")]
		public readonly int AnnihilationDelayTicks = 90;

		[Desc("Ticks after the annihilation before the win/loss verdict is applied from the frozen score.")]
		public readonly int ResolutionDelayTicks = 30;

		[Desc("Ticks of lead-in between the clock expiring and the first impact.")]
		public readonly int LeadInTicks = 30;

		[Desc("Damage type applied by the final annihilation sweep.")]
		public readonly BitSet<DamageType> AnnihilationDamageTypes = default;

		IEnumerable<LobbyOption> ILobbyOptions.LobbyOptions(MapPreview map)
		{
			yield return new LobbyBooleanOption("doomsday", DoomsdayLabel, DoomsdayDescription,
				DoomsdayCheckboxVisible, DoomsdayCheckboxDisplayOrder, DoomsdayEnabled, DoomsdayLocked);
		}

		public override object Create(ActorInitializer init) { return new DoomsdayStrike(init.Self, this); }
	}

	/// <summary>
	/// THE MODE. See <see cref="DoomsdayStrikeInfo"/> for the sequence; the interesting parts of the
	/// implementation are the three guarantees it has to keep.
	///
	/// COVERAGE is a construction, not a sample. The aim points come from real assets, and then
	/// <see cref="DoomsdayMath.UncoveredCells"/> is run over the whole playable rectangle and every cell
	/// it reports gets a fill warhead. The layout is built against (LethalRadius - JitterRadius) and the
	/// jitter is drawn inside a disc of exactly JitterRadius, so no draw can uncover a cell the layout
	/// had covered. DoomsdayCoverageTest asserts this over every shipped map size.
	///
	/// ...AND THERE IS STILL A BACKSTOP. The cover above is a statement about GEOMETRY — it proves every
	/// cell is inside some warhead's stated lethal radius. It is not a statement about DAMAGE, because
	/// the warheads' yields live in weapons-superweapons.yaml and are tuned independently of this trait.
	/// A unit that survives inside the radius (extreme armour, a garrison, a bugged Versus row) would
	/// break the user's absolute requirement, so <see cref="Annihilate"/> destroys whatever is left after
	/// the last impact. Both mechanisms are deliberate: the salvo is what the requirement means, the
	/// sweep is what makes it true.
	///
	/// DETERMINISM. Every random draw goes through World.SharedRandom. Nothing in the pipeline iterates
	/// a Dictionary or a HashSet — the asset list is sorted by ActorID before it is used for anything,
	/// and the two Info HashSets are only ever membership-TESTED, never enumerated. This is simulation
	/// state and it must be byte-identical on every client; note this is the exact opposite of the rule
	/// that governs render-only effects like the screen shake, which must avoid SharedRandom.
	/// </summary>
	public class DoomsdayStrike : ITick, INotifyTimeLimit
	{
		readonly DoomsdayStrikeInfo info;
		readonly World world;
		readonly bool enabled;

		// Cell-space radii, converted once at construction. The math layer is entirely in cells.
		readonly int lethalCells;
		readonly int jitterCells;
		readonly int effectiveCells;
		readonly int minSeparationCells;
		readonly int cityLinkCells;

		// The scheduled salvo, in ascending spawn tick. Consumed from the front by Tick.
		readonly List<(int SpawnTick, WPos Target, string Actor)> pending = new();
		int nextPending;

		bool triggered;
		int lastImpactTick;
		int annihilationTick;
		int resolutionTick;
		bool annihilated;
		bool resolved;

		/// <summary>
		/// True from the trigger until the verdict is applied. While set, the ordinary victory checks
		/// stand down — see <see cref="VictoryChecksSuspended"/>.
		/// </summary>
		public bool SalvoInProgress { get; private set; }

		public DoomsdayStrike(Actor self, DoomsdayStrikeInfo info)
		{
			this.info = info;
			world = self.World;

			var option = world.LobbyInfo.GlobalSettings.OptionOrDefault("doomsday", info.DoomsdayEnabled.ToString());
			enabled = bool.TryParse(option, out var parsed) ? parsed : info.DoomsdayEnabled;

			lethalCells = info.LethalRadius.Length / 1024;
			jitterCells = info.JitterRadius.Length / 1024;
			effectiveCells = DoomsdayMath.EffectiveRadius(lethalCells, jitterCells);
			minSeparationCells = info.MinSeparation.Length / 1024;
			cityLinkCells = info.CityLinkDistance.Length / 1024;
		}

		/// <summary>
		/// Whether the ordinary win/loss machinery should stand down for this world.
		///
		/// It has to, and the reason is not cosmetic. Once the warheads start landing, players lose their
		/// last units in whatever order the geometry happens to produce, and
		/// <see cref="ConquestVictoryConditions"/> would award the match to whoever survived a few ticks
		/// longer. The user's requirement is that the winner comes from the SCORE as it stood before the
		/// first warhead — so the checks are suspended for the duration and the verdict is applied at the
		/// end from a score that has been frozen the whole time.
		/// </summary>
		public static bool VictoryChecksSuspended(World world)
		{
			var dd = world.WorldActor.TraitOrDefault<DoomsdayStrike>();
			return dd != null && dd.SalvoInProgress;
		}

		void INotifyTimeLimit.NotifyTimerExpired(Actor self)
		{
			if (triggered || !enabled)
				return;

			if (TestMode.IsActive && !info.RunInTestMode)
				return;

			triggered = true;

			// ORDER IS LOAD-BEARING, and this is the requirement most easily done superficially.
			// TimeLimitManager notifies WORLD traits before PLAYER traits (TimeLimitManager.cs:150-157),
			// so this runs before ConquestVictoryConditions sees the same event. Freezing here means the
			// score every later reader sees — including the verdict at the end of the salvo — is the score
			// as it stood on the expiry tick, with nothing the annihilation does able to move it.
			FreezeStatistics();
			SalvoInProgress = true;

			BuildSalvo();

			TextNotificationsManager.AddSystemLine("DEAD HAND ACTIVATED. Incoming.");
		}

		/// <summary>
		/// Stop every per-player accumulator, on the tick the clock expired.
		///
		/// This is a FREEZE OF THE ACCUMULATION, not a snapshot of the display. Two flags do it, and they
		/// were chosen because they are choke points rather than because they are convenient:
		///   * PlayerExperience.Frozen — GiveExperience is the single entry point through which every
		///     score-affecting event in the engine passes (kills via GivesExperience, captures, donations,
		///     infiltration, repairs, Lua). Guarding it there covers all of them at once, which is why the
		///     kills the nukes themselves cause credit nobody.
		///   * PlayerStatistics.Frozen — stops the income/army sampling in its Tick and every
		///     UpdatesPlayerStatistics lifecycle callback, so unit counts, asset values, kill/death tallies
		///     and the composition telemetry all stop where they were.
		/// </summary>
		void FreezeStatistics()
		{
			foreach (var p in world.Players)
			{
				p.PlayerActor.TraitOrDefault<PlayerStatistics>()?.Freeze();
				p.PlayerActor.TraitOrDefault<PlayerExperience>()?.Freeze();
			}
		}

		/// <summary>
		/// Enumerate strategic assets, cluster them, assign warheads, enforce separation, jitter, then
		/// top up for total coverage — and turn the result into spawn ticks.
		/// </summary>
		void BuildSalvo()
		{
			var bounds = world.Map.Bounds;

			// ---- 1. Enumerate. Sorted by ActorID, which is assigned in world-creation order and is
			// therefore identical on every client. Everything downstream inherits this ordering.
			// Buildings, not scenery. The distinction is a TRAIT test rather than a name list, and it
			// matters more than it looks: river-zeta carries 1713 `v17` and 864 `v16` actors, which are
			// crop tiles inheriting ^CivField and have no Building trait at all, against a few dozen real
			// structures on ^CivBuilding which do. Clustering on names would have targeted the fields.
			//
			// Info.Name is lowercased defensively before every comparison, for the same reason
			// UpdatesPlayerStatistics does it (PlayerStatistics.cs): the Rules.Actors dictionary is
			// case-sensitive with lowercased keys, and a yaml-supplied type list that happens to be
			// capitalised would otherwise silently match nothing.
			var buildings = world.Actors
				.Where(a => a.IsInWorld && !a.Disposed && a.Info.HasTraitInfo<BuildingInfo>()
					&& !info.ExcludeTypes.Contains(a.Info.Name.ToLowerInvariant()))
				.OrderBy(a => a.ActorID)
				.ToList();

			var highValue = new List<CPos>();
			var cityCandidates = new List<CPos>();
			foreach (var a in buildings)
			{
				if (info.HighValueTypes.Contains(a.Info.Name.ToLowerInvariant()))
					highValue.Add(a.Location);
				else
					cityCandidates.Add(a.Location);
			}

			// ---- 2. Cluster the rest into cities. Small clusters are outliers, not cities.
			var clusters = DoomsdayMath.ClusterAssets(cityCandidates, cityLinkCells);

			// Candidate aim points, in PRIORITY order — the min-separation filter walks this order and
			// keeps the earlier entry when two collide, so cities outrank derricks outrank stragglers.
			var candidates = new List<CPos>();
			var candidateTiers = new List<DoomsdayTier>();

			foreach (var cluster in clusters)
			{
				if (cluster.Count < info.CityMinBuildings)
					continue;

				foreach (var p in DoomsdayMath.CityAimPoints(cityCandidates, cluster, info.WarheadsPerCity, minSeparationCells, effectiveCells))
				{
					candidates.Add(p);
					candidateTiers.Add(DoomsdayTier.City);
				}
			}

			foreach (var p in highValue)
			{
				candidates.Add(p);
				candidateTiers.Add(DoomsdayTier.Outlier);
			}

			foreach (var cluster in clusters)
			{
				if (cluster.Count >= info.CityMinBuildings)
					continue;

				candidates.Add(DoomsdayMath.Centroid(cityCandidates, cluster));
				candidateTiers.Add(DoomsdayTier.Outlier);
			}

			// ---- 3. Jitter, inside a disc strictly smaller than the coverage margin.
			//
			// JITTER RUNS BEFORE THE SEPARATION FILTER, and the order is the whole point. Filtering first
			// would enforce the minimum separation on the IDEAL layout and then let the RNG walk two kept
			// impacts back toward each other — up to 2*JitterRadius, which at the shipped 12-cell
			// separation and 4-cell jitter is a worst case of 4 cells apart. The user's constraint is
			// about where the warheads actually land, so it is enforced on where they actually land.
			//
			// Nothing is lost by doing it this way: the jitter is bounded by the coverage margin, so a
			// jittered point still covers everything its unjittered self did, and the fill pass below runs
			// on the final positions either way.
			for (var i = 0; i < candidates.Count; i++)
			{
				var j = DoomsdayMath.DiscJitter(world.SharedRandom.Next(), world.SharedRandom.Next(0, jitterCells + 1), jitterCells);
				var c = new CPos(candidates[i].X + j.X, candidates[i].Y + j.Y);
				candidates[i] = new CPos(
					Math.Clamp(c.X, bounds.Left, bounds.Right - 1),
					Math.Clamp(c.Y, bounds.Top, bounds.Bottom - 1));
			}

			// ---- 4. Separation, on the jittered positions. The candidate order is the priority order, so
			// a city aim point always survives a collision with a derrick rather than the other way round.
			var aimCells = new List<CPos>();
			var aimTiers = new List<DoomsdayTier>();
			foreach (var i in DoomsdayMath.MinSeparationFilter(candidates, minSeparationCells))
			{
				aimCells.Add(candidates[i]);
				aimTiers.Add(candidateTiers[i]);
			}

			// ---- 5. Top up for total coverage. Whatever the targeted salvo left open gets a fill
			// warhead, and those arrive last so the targeted strikes are what the viewer reads.
			var spacing = DoomsdayMath.GridSpacing(lethalCells, jitterCells);
			var uncovered = DoomsdayMath.UncoveredCells(bounds, aimCells, effectiveCells);
			foreach (var p in DoomsdayMath.FillPoints(bounds, uncovered, spacing))
			{
				// Fill points are NOT jittered. They are the coverage mechanism and they are already at
				// worst-case distance from the corner of their own strip; spending the margin twice is
				// exactly the mistake the effective-radius split exists to prevent.
				aimCells.Add(p);
				aimTiers.Add(DoomsdayTier.Fill);
			}

			// ---- 6. Schedule.
			var timings = new DoomsdayMath.ScheduleTimings(info.WithinWaveTicks, info.OutlierToCityPauseTicks, info.CityToFillPauseTicks);
			var impacts = DoomsdayMath.BuildSchedule(aimCells, aimTiers, timings);

			var outlierFlight = FlightTicks(info.OutlierMissile);
			var cityFlight = FlightTicks(info.CityMissile);
			var maxFlight = Math.Max(outlierFlight, cityFlight);
			var firstImpactTick = world.WorldTick + info.LeadInTicks + maxFlight;

			// Seeded rather than left at zero so that a degenerate map with nothing to aim at — no
			// buildings AND a bounds so small the fill pass emits nothing — still runs the lead-in and
			// the delays instead of annihilating on the trigger tick, which is what a zero would mean
			// once it was compared against an already-larger WorldTick.
			lastImpactTick = firstImpactTick;

			foreach (var impact in impacts)
			{
				var actor = impact.Tier == DoomsdayTier.Outlier ? info.OutlierMissile : info.CityMissile;
				var flight = impact.Tier == DoomsdayTier.Outlier ? outlierFlight : cityFlight;
				var arrival = firstImpactTick + impact.ArrivalOffset;

				pending.Add((arrival - flight, world.Map.CenterOfCell(impact.Cell), actor));

				if (arrival > lastImpactTick)
					lastImpactTick = arrival;
			}

			// Spawn order, not arrival order: a slower missile aimed at a later arrival can still need to
			// launch before a faster one aimed at an earlier arrival. Sorted by spawn tick so Tick can
			// consume from the front. OrderBy is a stable sort, so equal spawn ticks keep schedule order.
			pending.Sort((a, b) => a.SpawnTick.CompareTo(b.SpawnTick));

			annihilationTick = lastImpactTick + info.AnnihilationDelayTicks;
			resolutionTick = annihilationTick + info.ResolutionDelayTicks;
		}

		/// <summary>
		/// Flight time of one warhead, read from the activity's own arithmetic rather than kept in step
		/// with it by hand. Constant across the salvo because every warhead flies the same horizontal
		/// distance — <see cref="DoomsdayStrikeInfo.ApproachDistance"/> — regardless of where it is aimed.
		/// Terrain height under the aim point does not enter into it: hDist is a HORIZONTAL length.
		/// </summary>
		int FlightTicks(string actorType)
		{
			var missileInfo = world.Map.Rules.Actors[actorType].TraitInfo<BallisticMissileInfo>();
			return BallisticMissileFly.EstimateArcTicks(missileInfo, info.ApproachDistance.Length);
		}

		void ITick.Tick(Actor self)
		{
			if (!triggered)
				return;

			while (nextPending < pending.Count && pending[nextPending].SpawnTick <= world.WorldTick)
			{
				Launch(pending[nextPending].Target, pending[nextPending].Actor);
				nextPending++;
			}

			if (!annihilated && world.WorldTick >= annihilationTick)
			{
				annihilated = true;
				Annihilate();
			}

			if (!resolved && world.WorldTick >= resolutionTick)
			{
				resolved = true;
				Resolve();
			}
		}

		/// <summary>
		/// Put one warhead in the air on a re-entry trajectory.
		///
		/// STEEPNESS COMES FROM THE GEOMETRY, NOT FROM LaunchAngle, and that distinction is the whole
		/// design. Raising LaunchAngle on a BallisticMissile scales the arc apex with shot length
		/// (BallisticMissileFly.cs:62-63), so the same setting produces a different trajectory on a big
		/// map than on a small one — which is exactly why the shipped strike missiles sit at a deliberately
		/// low 30 raw units. Here the missile is spawned high and CLOSE, so the descent is steep by
		/// construction and identical on every map: SpawnAltitude over ApproachDistance, 38c0 over 5c0,
		/// is a constant slope of 7.6 whatever the map is. The missile actors set LaunchAngle 0, which
		/// makes the arc term vanish entirely and leaves a dead-straight 82.5-degree descent.
		/// </summary>
		void Launch(WPos target, string actorType)
		{
			// Bearing is drawn from SharedRandom so the salvo does not arrive in parade formation, all on
			// the same heading. It affects only the direction the missile comes IN from; the aim point and
			// therefore the coverage argument are already fixed.
			var bearing = new WAngle(world.SharedRandom.Next(0, 1024));
			var offset = new WVec(0, -info.ApproachDistance.Length, 0).Rotate(WRot.FromYaw(bearing));
			var spawnPos = target + offset + new WVec(0, 0, info.SpawnAltitude.Length);

			var missile = world.CreateActor(false, actorType, new TypeDictionary
			{
				new CenterPositionInit(spawnPos),
				// The world-owning player, NOT a lookup for a player literally named "Neutral": a map is
				// free to call its OwnsWorld player anything, and First() on a missing name throws.
				new OwnerInit(world.WorldActor.Owner),
				new FacingInit((target - spawnPos).Yaw),
			});

			// ORDERING IS LOAD-BEARING, and it is the same handshake MissileStrikePower performs
			// (MissileStrikePower.cs:118-141): BallisticMissile.AddedToWorld queues BallisticMissileFly,
			// whose constructor reads Target.CenterPosition unconditionally, so the Target must be set
			// between building the actor and adding it to the world.
			var bm = missile.Trait<BallisticMissile>();
			bm.Target = Target.FromPos(target);
			world.AddFrameEndTask(w => w.Add(missile));
		}

		/// <summary>
		/// The backstop. Destroy everything still standing, so "nothing survives" is a property of the
		/// mode rather than a property of this week's warhead tuning. See the class remarks for why this
		/// exists alongside a proven geometric cover rather than instead of one.
		///
		/// Statistics are already frozen, so none of these deaths reach anybody's score.
		/// </summary>
		void Annihilate()
		{
			// Snapshot first: Kill mutates the actor list through husks and death effects, and iterating
			// world.Actors lazily while that happens is how this would throw.
			var doomed = world.Actors
				.Where(a => a.IsInWorld && !a.Disposed && a.Info.HasTraitInfo<HealthInfo>())
				.OrderBy(a => a.ActorID)
				.ToList();

			foreach (var a in doomed)
				if (a.IsInWorld && !a.Disposed)
					a.Kill(a, info.AnnihilationDamageTypes);

			TextNotificationsManager.AddSystemLine("Total strategic annihilation.");
		}

		/// <summary>
		/// Apply the verdict from the frozen score.
		///
		/// This deliberately REUSES the shipped time-limit resolution rather than reimplementing it: the
		/// suspension is lifted and <see cref="INotifyTimeLimit.NotifyTimerExpired"/> is re-raised on the
		/// player actors, which runs ConquestVictoryConditions' existing highest-Experience-wins
		/// comparison. Because PlayerExperience has been frozen since the expiry tick, that comparison
		/// reads exactly the numbers it would have read then — which is what makes the freeze
		/// load-bearing rather than decorative, and is the property DoomsdayStatsFreezeTest pins.
		///
		/// SIMULTANEOUS ELIMINATION IS NOT A CASE HERE. Every player was destroyed on the same tick by
		/// the sweep above, but no player has a WinState yet, because the victory checks were suspended
		/// for the whole salvo. So the tie-break never runs on "who lost their last unit last" — there is
		/// exactly one ordering decision, taken here, over frozen numbers. A genuine score TIE resolves
		/// through the existing OrderByDescending, which is a stable sort over world.Players in its fixed
		/// creation order: the earliest-seated tied player wins, identically on every client.
		/// </summary>
		void Resolve()
		{
			SalvoInProgress = false;

			foreach (var p in world.Players)
				foreach (var ntl in p.PlayerActor.TraitsImplementing<INotifyTimeLimit>())
					ntl.NotifyTimerExpired(p.PlayerActor);
		}
	}
}
