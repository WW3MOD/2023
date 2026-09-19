#region Copyright & License Information
/*
 * WW3MOD world-construction smoke gate (world trait).
 *
 * Writes a PASS verdict and exits once the world has loaded and Test.SmokeTicks sim ticks
 * have run. Inert unless BOTH Test.Mode=true AND Test.SmokeTicks>0, which is the same
 * off-by-default discipline as UnitLifecycleLogger and TestModeSpeedMultiplier.
 *
 * WHY IT EXISTS
 * A shipped map under mods/ww3mod/maps/ carries no Lua, so it can never reach Test.Pass, so
 * before this there was no way to run one to a verdict. That mattered because "does a World
 * still construct?" is a question only a running World can answer: on 2026-09-10 DefconWall
 * threw a NullReferenceException in INotifyCreated.Created and every match failed to start
 * while build, check, NUnit, YAML lint, lua-gate and nav-guard all stayed green. Nothing we
 * ran constructed a World. tools/autotest/run-smoke.sh + `make.ps1 smoke` is the gate; this
 * trait is what lets that gate reach a verdict on a map that has no script of its own.
 *
 * WHAT A PASS FROM HERE DOES AND DOES NOT MEAN
 * It means: every trait on the world actor was constructed, every INotifyCreated.Created ran,
 * IWorldLoaded ran, and the sim ticked N times without throwing. That is exactly the bug class
 * above and no more. It is NOT a statement about gameplay, balance, rendering or AI -- nothing
 * here asserts anything about the match beyond its own continued existence.
 *
 * NOTHING HERE ENTERS A SYNCED PATH. No field is [Sync], no RNG is drawn, and the tick counter
 * is local bookkeeping that no other trait reads, so arming the gate cannot change a replay.
 * The exit is deferred to Game.RunAfterTick rather than taken inside Tick, so the world
 * finishes the tick it is in before the process begins shutting down.
 */
#endregion

using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[TraitLocation(SystemActors.World)]
	[Desc("Writes a PASS verdict and exits once the world has loaded and Test.SmokeTicks ticks have run.",
		"No-op unless Test.Mode=true and Test.SmokeTicks>0. Used by `make.ps1 smoke` to run a",
		"shipped map -- which carries no Lua and so can never reach Test.Pass -- to a verdict.")]
	public class SmokeTestExitInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) { return new SmokeTestExit(); }
	}

	public class SmokeTestExit : IWorldLoaded, ITick
	{
		bool armed;
		bool fired;
		int ticks;

		// Arming happens HERE and not in the constructor or in Created, and that is the whole
		// point of the trait rather than an implementation detail: reaching IWorldLoaded is
		// itself the evidence that construction survived. Arming earlier would mean the gate
		// could not tell "the world was built" from "the world began to be built".
		void IWorldLoaded.WorldLoaded(World w, OpenRA.Graphics.WorldRenderer wr)
		{
			if (!TestMode.IsActive || TestMode.SmokeTicks <= 0)
				return;

			// A shellmap or the map editor is not a match, and passing on one would report a
			// world that was never asked to run. WorldType.Regular is the only one that counts.
			if (w.Type != WorldType.Regular)
			{
				Log.Write("debug", $"[smoke] world type is {w.Type}, not Regular — gate stays disarmed.");
				return;
			}

			armed = true;
			Log.Write("debug", $"[smoke] world loaded: {w.Map.Title} ({w.Map.Uid}); "
				+ $"passing after {TestMode.SmokeTicks} ticks.");
		}

		void ITick.Tick(Actor self)
		{
			if (!armed || fired)
				return;

			if (++ticks < TestMode.SmokeTicks)
				return;

			fired = true;
			var map = self.World.Map;

			// Deferred rather than immediate: Game.Exit only sets the run state, but WriteResult
			// touches the disk, and doing that from inside the tick puts a file write on the
			// simulation's critical path for no reason.
			Game.RunAfterTick(() =>
			{
				TestMode.WriteResult("pass",
					$"world constructed and ticked {ticks} times on {map.Title} ({map.Uid})");
				Game.Exit();
			});
		}
	}
}
