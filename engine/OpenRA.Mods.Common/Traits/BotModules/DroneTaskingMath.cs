#region Copyright & License Information
/*
 * WW3MOD — where a recon drone gets sent, and whether it may be sent at all (pure math).
 *
 * Split out of DroneOperatorBotModule so the selection rules can be tested without standing up a
 * world. Every function here is a pure function of its arguments: no world, no RNG, no tick clock.
 */
#endregion

namespace OpenRA.Mods.Common.Traits
{
	public static class DroneTaskingMath
	{
		/// <summary>Score returned for a candidate that must never be chosen. Below every real score.</summary>
		public const long Ineligible = long.MinValue;

		/// <summary>
		/// The furthest a drone may be asked to hover from the operator, in cells.
		///
		/// TWO SEPARATE LIMITS BIND HERE AND THE SMALLER ONE WINS.
		/// (1) The weapon: DroneTargeter's range is what decides whether the launch order can be
		/// executed from where the operator is standing at all.
		/// (2) The leash: CarrierSlave.MaxDistance (25 cells, aircraft.yaml) checked every
		/// MaxDistanceCheckTicks. Past it the drone is NOT recalled — it is dragged 10% back and
		/// granted lost-connection, which zeroes its vision. A drone sitting past the leash is
		/// therefore worse than useless: it is a blind unit the bot believes is scouting.
		///
		/// CarrierMasterInfo.MaxSlaveDistance is NOT one of these limits. It has no readers
		/// engine-wide (CarrierMaster.cs:38) — the field is inert, and the 20c0 on ^DR is decoration.
		/// Anything built on that number is standoff arithmetic against a constant nothing reads.
		///
		/// The margin exists because the leash check is periodic rather than continuous and the
		/// operator may be nudged after launch; sitting exactly on the boundary invites the
		/// lost-connection grant on the next check.
		/// </summary>
		public static int MaxHoverDistanceCells(int weaponRangeCells, int leashCells, int marginCells)
		{
			var leash = leashCells - marginCells;
			if (leash < 0)
				leash = 0;

			return weaponRangeCells < leash ? weaponRangeCells : leash;
		}

		/// <summary>
		/// Score an observation cell. Higher is better; <see cref="Ineligible"/> means "do not send".
		///
		/// ticksSinceVerified is ControlField.TicksSinceVerified — ticks since we actually OBSERVED
		/// that square, int.MaxValue if never. It is fog-legal, which is the whole reason it is the
		/// input here rather than an exploration age computed by walking world.Actors.
		///
		/// A PURE STALENESS ARGMAX IS A BUG, NOT A POLICY. The stalest square on any map is the one
		/// nothing can reach, so an unbounded argmax parks the drone in a corner forever — the exact
		/// failure ScoutBotModule.cs:213-222 had to patch. Hence two hard bounds (staleness floor,
		/// POI distance ceiling) and a value term, so the drone is spent on ground someone might
		/// actually contest rather than on blank map.
		/// </summary>
		/// <remarks>
		/// The danger term here is AIR danger, not ground: this cell is where the DRONE hovers, and a
		/// quadcopter in contested airspace is a soft, killable asset rather than an invulnerable eye.
		/// Its 1-world-unit hitshape buys it nothing — HitShape.TargetablePositions never reads Radius,
		/// so every weapon aims at its centre, and a single Stinger kills 50 HP anywhere inside a cell.
		/// Losing it is a real outcome to steer around: it costs 25 supply (the ammo pool is decremented
		/// only on slave death, CarrierMaster.cs:233) and leaves the operator holding a respawned drone
		/// it cannot launch, because `loaded` is re-granted while ammo-primary is still 0.
		/// The operator's own launch cell is judged on GROUND danger, separately, by the caller.
		/// </remarks>
		public static long ScoreCandidate(
			int ticksSinceVerified,
			int minStalenessTicks,
			int poiDistanceCells,
			int maxPoiDistanceCells,
			int airDanger,
			int maxAirDanger,
			int contactBonus)
		{
			// Not stale enough to be worth a sortie: we already know what is there.
			if (ticksSinceVerified < minStalenessTicks)
				return Ineligible;

			// Outside the band of ground anyone is contesting. This is the unreachable-corner guard.
			if (poiDistanceCells > maxPoiDistanceCells)
				return Ineligible;

			// The drone is unarmed and dies to one hit of real AA; hovering it over a hot square is
			// donating 25 supply and the next sortie with it.
			if (airDanger > maxAirDanger)
				return Ineligible;

			// Saturate rather than overflow: a never-observed square reports int.MaxValue, and that
			// must not be allowed to wrap when the bonus is added.
			var staleness = (long)ticksSinceVerified;
			if (staleness > int.MaxValue / 2)
				staleness = int.MaxValue / 2;

			// Closer to a POI is better, so distance subtracts. The contact bonus is what expresses
			// "prefer squares next to something we believe is there over blank map".
			return staleness + contactBonus - poiDistanceCells;
		}

		/// <summary>
		/// Whether a launch order may be issued THIS tick.
		///
		/// EVERY TERM IS A SEPARATE FAILURE THAT LOOKS LIKE SUCCESS.
		///
		/// armamentReady: ^DR's primary Armament is PauseOnCondition "!loaded || !ammo-primary", so
		/// an unpaused armament is exactly "a drone is docked AND there is ammo to replace it if it
		/// dies". Reading the armament's own pause state rather than re-deriving those two conditions
		/// is deliberate: it cannot drift out of agreement with the YAML gate. Note the nasty state
		/// this catches — after a drone is killed the quadcopter respawns in ~9s and re-grants
		/// `loaded`, but ammo-primary is 0, so the operator visibly HAS a drone and cannot launch it.
		///
		/// noDroneAirborne: the retarget branch (CarrierMaster.cs:137-140) is unreachable for ^DR, so
		/// a second launch order while one is out cannot redirect anything. The reason is the launch
		/// itself: it revokes `loaded` (:161-162), which pauses the only armament, so Attacking()
		/// cannot be re-entered while a drone is out. A second order would burn the 3s FireDelay and a
		/// 12s BurstWait for nothing.
		///
		/// isStationary: the operator must not be MOVING. CarrierMaster carries PauseOnCondition
		/// "moving", and TraitPaused calls SetConnection(false) AND Recall() (CarrierMaster.cs:318-322)
		/// — so movement throws the sortie away. It also matters at launch: Attacking() early-returns
		/// on IsTraitPaused, and the spawn happens inside the FireDelay callback (Armament.cs:692), so
		/// a still-moving operator fires the weapon, starts the cooldown and spawns NOTHING.
		///
		/// THIS TERM IS "NOT MOVING", NOT "IDLE", AND THE DIFFERENCE IS THE WHOLE BUG.
		/// After its first launch the operator is never idle again: the Attack activity holds
		/// indefinitely because ChooseArmamentsForTarget filters IsTraitDisabled but not
		/// IsTraitPaused, and ^DR does not opt into AbandonWhenArmamentsPaused (Attack.cs:248-256
		/// documents exactly this wedge). An idle gate therefore latches false forever and the module
		/// issues exactly one sortie per operator for the rest of the match. A wedged operator is
		/// standing perfectly still and is a legitimate launch platform — what must be excluded is a
		/// WALKING one, which is what this asks.
		///
		/// inRange: force-firing a cell outside weapon range makes the attack activity WALK there,
		/// which grants `moving` and defeats the point of standing off.
		/// </summary>
		public static bool CanLaunch(
			bool armamentReady,
			bool noDroneAirborne,
			bool isStationary,
			int targetDistanceCells,
			int maxHoverDistanceCells)
		{
			if (!armamentReady || !noDroneAirborne || !isStationary)
				return false;

			return targetDistanceCells >= 0 && targetDistanceCells <= maxHoverDistanceCells;
		}

		/// <summary>
		/// Whether to issue a fresh launch order this cycle.
		///
		/// WHY THIS EXISTS AT ALL. The engine re-fires a held Attack activity by itself every time
		/// `loaded` comes back, so an operator left alone keeps shuttling its drone to the ONE cell it
		/// was first given — a fixed observation post, not the rolling sweep this module is for. The
		/// module only gets a different cell by ordering one, and an unqueued order is what clears the
		/// held activity (Actor.QueueActivity(false, …) calls CancelActivity first). So: re-order to
		/// move the post, stay silent to keep it.
		///
		/// settleTicks guards the FireDelay window: the spawn is a delayed action owned by the Armament,
		/// not by the activity, so re-ordering inside that gap does not cancel the pending launch — it
		/// just aims the operator somewhere else while the drone still departs for the old cell.
		///
		/// BE CLEAR ABOUT WHAT ACTUALLY PROTECTS THAT WINDOW TODAY: it is NOT this parameter. The
		/// caller stamps OrderedTick from the tick captured in its evaluation, and evaluations are
		/// exactly ReevaluateInterval apart, so ticksSinceLaunchOrder is always ReevaluateInterval at
		/// the shipped cadence and this branch is unreachable. The real invariant is
		/// ReevaluateInterval (200) > FireDelay (50), which nothing asserts. settleTicks is insurance
		/// that keeps the rule correct if the cadence is ever lowered, not a live guard — do not read
		/// its presence as evidence the window is defended.
		/// </summary>
		public static bool ShouldRetask(
			bool hasStandingOrder,
			bool sameCellAsOrdered,
			int ticksSinceLaunchOrder,
			int settleTicks)
		{
			if (!hasStandingOrder)
				return true;

			if (ticksSinceLaunchOrder < settleTicks)
				return false;

			// The held activity already re-fires at this cell unprompted; re-ordering it would cancel
			// and rebuild the same activity for nothing.
			return !sameCellAsOrdered;
		}

		/// <summary>
		/// Whether a square we previously sent a drone to has been covered well enough to retire it
		/// from the candidate set. Retiring is what stops the module re-picking one hot square every
		/// cycle while the rest of the map goes stale.
		/// </summary>
		public static bool IsCovered(int ticksSinceVerified, int minStalenessTicks)
		{
			return ticksSinceVerified < minStalenessTicks;
		}
	}
}
