#region Copyright & License Information
/*
 * WW3MOD sub-tick render clock (2026-09-06).
 *
 * The simulation runs at Timestep 60, i.e. 16.67 ticks per second (mods/ww3mod/mod.yaml). Rendering
 * is already decoupled and uncapped by default (Settings.cs, CapFramerate = false /
 * CapFramerateToGameFps = false), so the screen updates far more often than the world does — a fast
 * projectile does not look like 16 fps because the screen is slow, it looks like 16 fps because its
 * position only changes 16.67 times a second while everything around it is being redrawn.
 *
 * This is the missing quantity: how far through the CURRENT logic tick the frame being drawn is.
 * With it, a view-only consumer can draw a mover at its last simulated position plus one tick of its
 * last velocity scaled by this fraction, and the sprite glides between ticks instead of stepping.
 *
 * IT IS DERIVED FROM WALL CLOCK AND IS THEREFORE NOT SIMULATION STATE. Game.RunTime is a stopwatch;
 * two clients running the same match will read different fractions on the same tick. Anything
 * sync-hashed that reads this will desync, and it will not show up in single-player. That claim is
 * pinned by SubTickClockIsNotSimulationStateTest, which IL-scans every ISync implementor for a call
 * to any member of this type and currently allows NONE of them — not even from a render method.
 * A type is on the correct side of that line when it consumes the fraction and returns renderables,
 * the way SubTickMotionSmoothing does, without ever storing it.
 */
#endregion

namespace OpenRA.Graphics
{
	/// <summary>
	/// How far through the current logic tick the frame being drawn is. View state only — see the
	/// file header before reading this from anywhere that can reach the simulation.
	/// </summary>
	public static class SubTickClock
	{
		/// <summary>A whole tick, as <see cref="Fraction"/> counts it.</summary>
		public const int One = 1024;

		/// <summary>
		/// Elapsed portion of the current logic tick in 1024ths: 0 on the frame drawn immediately
		/// after a tick, <see cref="One"/> on the frame drawn immediately before the next one.
		/// Zero whenever the loop has no usable interval to measure against.
		/// </summary>
		public static int Fraction { get; private set; }

		/// <summary>
		/// The fraction implied by a Game.Loop iteration. Pure, so it can be exercised without a
		/// running loop — see SubTickClockTest.
		/// </summary>
		/// <param name="now">Current wall-clock reading, in the same units as nextLogic.</param>
		/// <param name="nextLogic">Timestamp the next logic tick is due at.</param>
		/// <param name="logicInterval">Milliseconds between logic ticks.</param>
		public static int Measure(long now, long nextLogic, int logicInterval)
		{
			// A non-positive interval is the game save load path (logicInterval is forced to 1) going
			// wrong, or a caller that has nothing to measure. Predicting from a garbage denominator
			// would throw or fling sprites across the map; standing still is the safe answer.
			if (logicInterval <= 0)
				return 0;

			// nextLogic is bumped by logicInterval the instant a tick runs, so the remaining time is
			// nextLogic - now and the elapsed portion is the interval minus that. Both ends need
			// clamping: the loop deliberately lets nextLogic fall BEHIND now when logic is running
			// late (Game.Loop, MaxLogicTicksBehind), which would otherwise read as a fraction above
			// one and predict further than a tick of travel.
			var elapsed = logicInterval - (nextLogic - now);
			if (elapsed <= 0)
				return 0;

			if (elapsed >= logicInterval)
				return One;

			return (int)(elapsed * One / logicInterval);
		}

		/// <summary>Called once per rendered frame from Game.Loop, immediately before RenderTick.</summary>
		public static void Update(long now, long nextLogic, int logicInterval)
		{
			Fraction = Measure(now, nextLogic, logicInterval);
		}

		/// <summary>Drop back to "no prediction" — used when no world is being simulated.</summary>
		public static void Reset()
		{
			Fraction = 0;
		}
	}
}
