using System.Collections.Generic;
using System.Linq;
using OpenRA.Mods.Common.Activities;
using OpenRA.Mods.Common.Orders;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Provides access to the attack-move command, which will make the actor automatically engage viable targets while moving to the destination.")]
	sealed class AttackMoveInfo : TraitInfo, Requires<IMoveInfo>
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
			move = self.Trait<IMove>();
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
				if (!order.Target.IsValidFor(self))
					return;

				var cell = self.World.Map.Clamp(self.World.Map.CellContaining(order.Target.CenterPosition));
				if (!Info.MoveIntoShroud && !self.Owner.MapLayers.IsExplored(cell))
					return;

				// Apply cohesion offset so grouped units spread to different targets
				cell = CohesionMoveModifier.ApplyCohesionOffset(self, cell);

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
					cursor = explored || info.MoveIntoShroud ? info.AttackMoveCursor : info.AttackMoveBlockedCursor;
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
				world.CancelInputMode();

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
					var blocked = !explored && !info.MoveIntoShroud;
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
