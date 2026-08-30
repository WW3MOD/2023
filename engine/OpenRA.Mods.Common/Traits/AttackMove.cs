using System.Collections.Generic;
using System.Linq;
using OpenRA.Mods.Common.Activities;
using OpenRA.Mods.Common.Orders;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Provides access to the attack-move command, which will make the actor automatically engage viable targets while moving to the destination.")]
	sealed class AttackMoveInfo : TraitInfo
	{
		[VoiceReference]
		public readonly string Voice = "Action";

		[Desc("Color to use for the target line.")]
		public readonly Color TargetLineColor = Color.OrangeRed;

		[GrantedConditionReference]
		[Desc("The condition to grant to self while an attack-move is active.")]
		public readonly string AttackMoveCondition = null;

#pragma warning disable CS0414
		[GrantedConditionReference]
		[Desc("The condition to grant to self while an assault-move is active (currently disabled).")]
		public readonly string AssaultMoveCondition = null;

		[Desc("Can the actor be ordered to move in to shroud?")]
		public readonly bool MoveIntoShroud = true;

		[CursorReference]
		public readonly string AttackMoveCursor = "attackmove";

		[CursorReference]
		public readonly string AttackMoveBlockedCursor = "attackmove-blocked";

		// UNREACHABLE, deliberately kept. Assault-move is disabled in WW3MOD — both the targeter
		// (ResolveOrder below) and AttackMoveOrderGenerator.OrderInner hardcode the non-assault
		// branch, so neither of these two cursors is ever emitted and the art at
		// mods/ww3mod/cursors.yaml:221-229 is dead.
		// NOT deleted: these are [CursorReference] fields, so CheckCursors lints their DEFAULTS
		// against the mod's cursor set. Removing the art without removing the fields reds the YAML
		// gate; removing the fields is a bigger call than tidying dead art, which is why the
		// scaffolding (including AssaultMoveCondition above) was kept behind a CS0414 pragma in the
		// first place. Leave both halves together or remove both halves together.
		[CursorReference]
		public readonly string AssaultMoveCursor = "assaultmove";

		[CursorReference]
		public readonly string AssaultMoveBlockedCursor = "assaultmove-blocked";
#pragma warning restore CS0414

		public override object Create(ActorInitializer init) { return new AttackMove(init.Self, this); }
	}

	class AttackMove : IResolveOrder, IOrderVoice, IIssueOrder
	{
		public readonly AttackMoveInfo Info;
		readonly IMove move;

		public AttackMove(Actor self, AttackMoveInfo info)
		{
			// Tolerant of missing IMove — defenses and other non-mobile actors that
			// inherit AutoTarget (and thus AttackMove via ^AutoTarget) can carry the
			// trait as a harmless no-op. The targeter and resolver below also guard
			// against move == null.
			move = self.TraitOrDefault<IMove>();
			Info = info;
		}

		string IOrderVoice.VoicePhraseForOrder(Actor self, Order order)
		{
			if (!Info.MoveIntoShroud && order.Target.Type != TargetType.Invalid)
			{
				var cell = self.World.Map.CellContaining(order.Target.CenterPosition);
				if (!self.Owner.MapLayers.IsExplored(cell))
					return null;
			}

			if (order.OrderString == "AttackMove")
				return Info.Voice;

			return null;
		}

		public IEnumerable<IOrderTargeter> Orders
		{
			get
			{
				yield return new AttackMoveTargeter(Info);
			}
		}

		public Order IssueOrder(Actor self, IOrderTargeter order, in Target target, bool queued)
		{
			if (order.OrderID == "AttackMove")
				return new Order(order.OrderID, self, target, queued);

			return null;
		}

		public void ResolveOrder(Actor self, Order order)
		{
			if (order.OrderString == "AttackMove")
			{
				if (move == null)
					return;

				if (!order.Target.IsValidFor(self))
					return;

				// A unit with nothing to fire has no business being sent to look for a fight. Scoped to
				// orders that execute NOW: asking about ammo at issue time only answers "can I do this"
				// for an unqueued order. "Resupply, then attack-move" is the CORRECT play with a dry
				// unit, and refusing the queued half would punish it — so let a queued order through and
				// let AttackMoveActivity's own guard rule on it when it actually comes up (still dry ⇒
				// it ends at once; rearmed ⇒ it runs). A plain Move is untouched either way.
				if (!order.Queued && AmmoPool.CannotFight(self))
					return;

				var cell = self.World.Map.Clamp(self.World.Map.CellContaining(order.Target.CenterPosition));
				if (!Info.MoveIntoShroud && !self.Owner.MapLayers.IsExplored(cell))
					return;

				var targetLocation = move.NearestMoveableCell(cell);
				var assaultMoving = false; // WW3MOD: AssaultMove disabled

				self.QueueActivity(order.Queued, new AttackMoveActivity(self, () => move.MoveTo(targetLocation, 8, targetLineColor: Info.TargetLineColor), assaultMoving));
				self.ShowTargetLines();
			}
		}

		class AttackMoveTargeter : IOrderTargeter
		{
			readonly AttackMoveInfo info;

			public AttackMoveTargeter(AttackMoveInfo info)
			{
				this.info = info;
			}

			public string OrderID => "AttackMove";
			public int OrderPriority => 4;
			public bool TargetOverridesSelection(Actor self, in Target target, List<Actor> othersAtTarget, CPos xy, TargetModifiers modifiers)
			{
				return modifiers.HasModifier(TargetModifiers.AttackMove);
			}

			public bool CanTarget(Actor self, in Target target, List<Actor> othersAtTarget, CPos xy, TargetModifiers modifiers, ref string cursor)
			{
				if (target.Type != TargetType.Terrain || self.TraitOrDefault<IMove>() == null)
					return false;

				if (modifiers.HasModifier(TargetModifiers.AttackMove))
				{
					var cell = self.World.Map.CellContaining(target.CenterPosition);
					var explored = self.Owner.MapLayers.IsExplored(cell);

					// Mirrors ResolveOrder's gate above (including its !order.Queued scoping, which
					// OrderReadinessMath owns) but resolved over the SELECTION, not over self.
					//
					// It has to be the selection even though this targeter is per-unit, because
					// UnitOrderGenerator.CursorForOrders picks ONE cursor for the whole click by
					// MaxByOrDefault on OrderPriority — and every AttackMove result ties at 4, where
					// MaxByOrDefault keeps the FIRST (Exts.cs:276 replaces only on strictly-greater).
					// Answering per-unit would therefore show blocked or not depending on which unit
					// happened to sort first: with one dry tank at the head of a healthy selection,
					// the whole click reads blocked. Giving every subject the same selection-wide
					// answer makes the tie-break irrelevant.
					var subjects = self.World.Selection.Contains(self)
						? self.World.Selection.Actors
						: new[] { self };

					var refused = OrderReadinessMath.ReadsAsBlocked(
						subjects.Where(a => a.Info.HasTraitInfo<AttackMoveInfo>()),
						modifiers.HasModifier(TargetModifiers.ForceQueue),
						AmmoPool.CannotFight);

					cursor = (explored || info.MoveIntoShroud) && !refused
						? info.AttackMoveCursor
						: info.AttackMoveBlockedCursor;
					return true;
				}

				return false;
			}

			public bool IsQueued => false;
		}
	}

	public class AttackMoveOrderGenerator : UnitOrderGenerator
	{
		TraitPair<AttackMove>[] subjects;

		public AttackMoveOrderGenerator(IEnumerable<Actor> subjects)
		{
			this.subjects = subjects.Where(a => !a.IsDead)
				.SelectMany(a => a.TraitsImplementing<AttackMove>()
					.Select(am => new TraitPair<AttackMove>(a, am)))
				.ToArray();
		}

		public override IEnumerable<Order> Order(World world, CPos cell, int2 worldPixel, MouseInput mi)
		{
			var modifiers = mi.Modifiers;
			if (mi.Button != Game.Settings.Game.AttackMoveButton || !modifiers.HasModifier(Game.Settings.Game.AttackMoveModifiers) || modifiers.HasModifier(Game.Settings.Game.ForceMoveModifiers))
				return Enumerable.Empty<Order>();

			return OrderInner(world, cell, mi);
		}

		protected virtual IEnumerable<Order> OrderInner(World world, CPos cell, MouseInput mi)
		{
			var modifiers = mi.Modifiers;
			if (mi.Button == Game.Settings.Game.AttackMoveButton && modifiers.HasModifier(Game.Settings.Game.AttackMoveModifiers) && !modifiers.HasModifier(Game.Settings.Game.ForceMoveModifiers))
			{
				// Keep this generator active while Alt is held so the attack-move cursor
				// stays visible and subsequent clicks (with or without Shift) continue to
				// issue attack-move orders. CommandBarLogic cancels the mode on Alt KeyUp.

				var queued = modifiers.HasModifier(Modifiers.Shift);
				var orderName = "AttackMove"; // WW3MOD: AssaultMove disabled

				cell = world.Map.Clamp(cell);
				yield return new Order(orderName, null, Target.FromCell(world, cell), queued, null, subjects.Select(s => s.Actor).ToArray());
			}
		}

		public override void SelectionChanged(World world, IEnumerable<Actor> selected)
		{
			subjects = selected.Where(s => !s.IsDead).SelectMany(a => a.TraitsImplementing<AttackMove>()
					.Select(am => new TraitPair<AttackMove>(a, am)))
				.ToArray();

			if (!subjects.Any(s => s.Actor.Info.HasTraitInfo<AutoTargetInfo>()))
				world.CancelInputMode();
		}

		public override string GetCursor(World world, CPos cell, int2 worldPixel, MouseInput mi)
		{
			var modifiers = mi.Modifiers;
			if (mi.Button != Game.Settings.Game.AttackMoveButton || !modifiers.HasModifier(Game.Settings.Game.AttackMoveModifiers) || modifiers.HasModifier(Game.Settings.Game.ForceMoveModifiers))
				return null;

			var subject = subjects.FirstOrDefault();
			if (subject.Actor != null)
			{
				var info = subject.Trait.Info;
				if (world.Map.Contains(cell))
				{
					var explored = subject.Actor.Owner.MapLayers.IsExplored(cell);

					// Same rule as AttackMoveTargeter, through the same combinator. OrderInner issues
					// ONE grouped order to every subject and ResolveOrder drops it per-unit, so the
					// click still achieves something while any subject can fight.
					var refused = OrderReadinessMath.ReadsAsBlocked(
						subjects.Select(s => s.Actor),
						modifiers.HasModifier(Modifiers.Shift),
						AmmoPool.CannotFight);

					var blocked = (!explored && !info.MoveIntoShroud) || refused;
					return blocked ? info.AttackMoveBlockedCursor : info.AttackMoveCursor;
				}

				return info.AttackMoveBlockedCursor;
			}

			return null;
		}

		public override bool InputOverridesSelection(World world, int2 xy, MouseInput mi)
		{
			return true;
		}

		public override bool ClearSelectionOnLeftClick => false;
	}
}
