#region Copyright & License Information
/*
 * WW3MOD developer test harness — Lua scripting bindings.
 * Activated only when TestMode.IsActive (i.e. the game was launched with
 * Test.Mode=true). All methods are no-ops outside test mode so accidental
 * calls from a regular map don't write result files or quit the game.
 */
#endregion

using System.Linq;
using OpenRA.Mods.Common.Traits;
using OpenRA.Scripting;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Scripting.Global
{
	[ScriptGlobal("Test")]
	public class TestGlobal : ScriptGlobal
	{
		public TestGlobal(ScriptContext context)
			: base(context) { }

		[Desc("Mark the current test as passed and exit the game. " +
			"No-op outside test mode.")]
		public void Pass()
		{
			if (!TestMode.IsActive)
				return;

			TestMode.WriteResult("pass", "");
			Game.Exit();
		}

		[Desc("Mark the current test as failed (with a reason) and exit the game. " +
			"No-op outside test mode.")]
		public void Fail(string reason = "")
		{
			if (!TestMode.IsActive)
				return;

			TestMode.WriteResult("fail", reason ?? "");
			Game.Exit();
		}

		[Desc("Mark the current test as skipped (with a reason) and exit the game. " +
			"No-op outside test mode.")]
		public void Skip(string reason = "")
		{
			if (!TestMode.IsActive)
				return;

			TestMode.WriteResult("skip", reason ?? "");
			Game.Exit();
		}

		[Desc("Resolve the right-click OrderID that `unit` would issue when targeting `target`. " +
			"Walks the same IIssueOrder/IOrderTargeter pipeline as the UI cursor resolver, so " +
			"this catches order-priority and CanTargetActor regressions that direct unit.Attack/Move " +
			"calls bypass. Returns the OrderID string of the highest-priority matching targeter, " +
			"or null if nothing matches. Test mode only.")]
		public string GetTargetOrder(Actor unit, Actor target)
		{
			if (!TestMode.IsActive || unit == null || target == null)
				return null;

			var t = Target.FromActor(target);
			var xy = target.Location;
			var actorsAt = unit.World.ActorMap.GetActorsAt(xy).ToList();

			var orders = unit.TraitsImplementing<IIssueOrder>()
				.SelectMany(trait => trait.Orders)
				.OrderByDescending(o => o.OrderPriority);

			foreach (var o in orders)
			{
				string cursor = null;
				if (o.CanTarget(unit, t, actorsAt, xy, TargetModifiers.None, ref cursor))
					return o.OrderID;
			}

			return null;
		}

		ProductionQueue FindQueueForActor(Player player, string actorType)
		{
			if (!player.World.Map.Rules.Actors.TryGetValue(actorType, out var ai))
				return null;

			var bi = ai.TraitInfoOrDefault<BuildableInfo>();
			if (bi == null)
				return null;

			foreach (var q in player.PlayerActor.TraitsImplementing<ProductionQueue>())
				if (q.Enabled && bi.Queue.Contains(q.Info.Type))
					return q;

			return null;
		}

		[Desc("Enqueue `count` of `actorType` on `player`'s production queue. Routes through " +
			"the StartProduction order so it exercises the real queue pipeline. Test mode only.")]
		public void QueueProduction(Player player, string actorType, int count = 1)
		{
			if (!TestMode.IsActive || player == null)
				return;

			var queue = FindQueueForActor(player, actorType);
			if (queue == null)
				return;

			queue.ResolveOrder(player.PlayerActor, Order.StartProduction(player.PlayerActor, actorType, count));
		}

		[Desc("Pause or resume production of `actorType` on `player`'s queue. Routes through the " +
			"PauseProduction order. Test mode only.")]
		public void PauseProduction(Player player, string actorType, bool paused)
		{
			if (!TestMode.IsActive || player == null)
				return;

			var queue = FindQueueForActor(player, actorType);
			if (queue == null)
				return;

			var order = new Order("PauseProduction", player.PlayerActor, false)
			{
				TargetString = actorType,
				ExtraData = paused ? 1u : 0u,
			};
			queue.ResolveOrder(player.PlayerActor, order);
		}

		[Desc("Returns the RemainingTime (in ticks) of the first queued item of `actorType` on " +
			"`player`'s queue, or -1 if no such item is queued. Test mode only.")]
		public int GetQueueRemainingTime(Player player, string actorType)
		{
			if (!TestMode.IsActive || player == null)
				return -1;

			var queue = FindQueueForActor(player, actorType);
			if (queue == null)
				return -1;

			var item = queue.AllQueued().FirstOrDefault(i => i.Item == actorType);
			return item?.RemainingTime ?? -1;
		}
	}
}
