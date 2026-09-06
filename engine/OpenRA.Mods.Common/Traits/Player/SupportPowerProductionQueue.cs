#region Copyright & License Information
/*
 * WW3MOD addition. Not upstream OpenRA.
 */
#endregion

using System.Collections.Generic;
using System.Linq;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Marks a bodiless proxy actor as a PURCHASE ORDER for a support power rather than",
		"something that gets built. Put it on an actor that carries Buildable, Valued, Tooltip and",
		"RenderSprites and nothing else: SupportPowerProductionQueue never calls Production.Produce",
		"for it, so the actor is never instantiated and needs no body, no health and no exit.")]
	public class ProvidesSupportPowerChargeInfo : TraitInfo
	{
		[FieldLoader.Require]
		[Desc("The OrderName of the support power this purchase charges -- the SupportPowerManager",
			"key. NOT the trait name and NOT the @suffix: MissileStrikePower@TacNuke is bought by",
			"naming its `OrderName: TacNukeStrike`. There is no lint for a typo here; a wrong name",
			"produces an entry that takes the money and then refuses to complete (the queue refunds",
			"and drops it), which is loud in play but silent at load.")]
		public readonly string Power = null;

		public override object Create(ActorInitializer init) { return new ProvidesSupportPowerCharge(); }
	}

	public class ProvidesSupportPowerCharge { }

	[TraitLocation(SystemActors.Player)]
	[Desc("A production queue whose items are support powers instead of units. Completing an item",
		"banks a shot on the matching SupportPower (which must carry RequiresPurchase: True) rather",
		"than spawning an actor, so the power's cameo appears in the support bin already loaded.",
		"",
		"Everything else is an ordinary ClassicProductionQueue and is inherited on purpose: the",
		"sidebar tab, the cost drain over the build time, the clock on the cameo, right-click hold,",
		"middle-click cancel with a partial refund, and the SupplyRouteContestation production",
		"slowdown all come for free because this is the same machinery the unit tabs use.",
		"",
		"Like any ClassicProductionQueue it is INERT unless the player owns an actor whose Production",
		"trait lists this queue's Type -- in WW3MOD that is the Supply Route. Losing the beachhead",
		"stops power purchases the same way it stops reinforcements.")]
	public class SupportPowerProductionQueueInfo : ClassicProductionQueueInfo, Requires<SupportPowerManagerInfo>
	{
		public override object Create(ActorInitializer init) { return new SupportPowerProductionQueue(init, this); }
	}

	public class SupportPowerProductionQueue : ClassicProductionQueue
	{
		readonly Actor self;

		Player cachedOwner;
		SupportPowerManager cachedManager;

		public SupportPowerProductionQueue(ActorInitializer init, SupportPowerProductionQueueInfo info)
			: base(init, info)
		{
			self = init.Self;
		}

		// Resolved lazily rather than in Created: ProductionQueue implements INotifyCreated
		// EXPLICITLY, so there is no Created to override, and the manager is not reachable from the
		// constructor anyway. Cached against the owner because BuildableItems runs every frame the
		// sidebar draws, and re-keyed on owner change because ProductionQueue survives one.
		SupportPowerManager Manager
		{
			get
			{
				if (cachedManager == null || cachedOwner != self.Owner)
				{
					cachedOwner = self.Owner;
					cachedManager = self.Owner.PlayerActor.Trait<SupportPowerManager>();
				}

				return cachedManager;
			}
		}

		SupportPowerInstance InstanceFor(ActorInfo unit)
		{
			var charge = unit.TraitInfoOrDefault<ProvidesSupportPowerChargeInfo>();
			if (charge == null)
				return null;

			return Manager.Powers.TryGetValue(charge.Power, out var instance) ? instance : null;
		}

		// THE LOBBY GATE, AND IT IS THE SAME GATE AS THE BIN'S. `Purchasable` is the power's own
		// Permitted flag, which is false whenever the SupportPower trait is condition-disabled
		// (RequiresCondition: !tacnuke-disabled) or its faction prerequisite is unmet. So a power the
		// host switched off is not merely unaffordable, it has no entry in the shop at all -- and a
		// power gated to the other faction never appears either.
		//
		// Filtering AllItems as well as BuildableItems is what makes it ABSENT rather than greyed
		// out: ProductionPaletteWidget draws one cameo per AllItems entry and only dims the ones
		// missing from BuildableItems (ProductionPaletteWidget.cs:707, :780).
		bool Purchasable(ActorInfo unit)
		{
			var instance = InstanceFor(unit);
			return instance != null && instance.Purchasable;
		}

		public override IEnumerable<ActorInfo> AllItems()
		{
			return base.AllItems().Where(Purchasable);
		}

		public override IEnumerable<ActorInfo> BuildableItems()
		{
			return base.BuildableItems().Where(Purchasable);
		}

		// The seam. ProductionQueue calls this from the frame-end task a finished ProductionItem
		// schedules, and treats `true` as "it left the queue". So banking a charge here and ending
		// production is the exact analogue of a unit walking out of an exit -- including the
		// contract that returning false leaves the item finished-but-undelivered, retried next tick.
		protected override bool BuildUnit(ActorInfo unit)
		{
			var item = Queue.FirstOrDefault(i => i.Done && i.Item == unit.Name);
			if (item == null)
				return false;

			var instance = InstanceFor(unit);
			if (instance == null || !instance.Purchasable)
			{
				// The power went away between paying and delivering -- host-disabled mid-match by a
				// rules reload, faction changed, player defeated, or (the one a mod author will
				// actually hit) ProvidesSupportPowerCharge.Power naming a power that does not exist.
				// Refund in full and drop it rather than retrying forever: the retry is silent and
				// the money would be gone.
				playerResources.GiveCash(item.TotalCost - item.RemainingCost);
				EndProduction(item);
				return false;
			}

			instance.GrantCharge();
			EndProduction(item);
			return true;
		}
	}
}
