#region Copyright & License Information
/*
 * WW3MOD: the consumer half of PlayerResources.UpkeepShortfall.
 *
 * WHY DORMANCY AND NOT LAPSE. The obvious answer to "the player cannot pay for a reservation" is to
 * cancel the reservation and refund it. That cannot be implemented honestly here, and the reason is
 * structural rather than aesthetic: PlayerResources.Upkeep is a SINGLE POOLED FLOAT, billed as one
 * number in one line, so no individual upkeep line is ever charged and the engine has no basis on
 * which to pick a victim. Cheapest? Newest? The one the player was saving for? Worse, the refund
 * would push cash back UP, which can make the very bill affordable again -- so the game would have
 * destroyed a purchase the player could have kept, irreversibly, on their behalf, at the worst
 * possible moment.
 *
 * Dormancy needs no choice and is reversible the moment the player is solvent. It also costs no new
 * rendering: SupportPowersWidget already draws its HoldText over any icon whose power is not Active,
 * and Active is `!Disabled && Instances.Any(i => !i.IsTraitPaused)` -- so a PauseOnCondition keyed
 * off this trait puts "ON HOLD" on a strike the player cannot currently afford, using shipped code.
 */
#endregion

using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Grants a condition to this actor while its owner could not pay the last upkeep bill in full.")]
	public class GrantConditionOnUpkeepShortfallInfo : TraitInfo
	{
		[FieldLoader.Require]
		[GrantedConditionReference]
		[Desc("The condition to grant while the owner is in upkeep arrears.")]
		public readonly string Condition = null;

		public override object Create(ActorInitializer init) { return new GrantConditionOnUpkeepShortfall(this); }
	}

	public class GrantConditionOnUpkeepShortfall : INotifyCreated, ITick, INotifyOwnerChanged
	{
		readonly GrantConditionOnUpkeepShortfallInfo info;

		PlayerResources playerResources;
		int conditionToken = Actor.InvalidConditionToken;

		public GrantConditionOnUpkeepShortfall(GrantConditionOnUpkeepShortfallInfo info)
		{
			this.info = info;
		}

		void INotifyCreated.Created(Actor self)
		{
			playerResources = self.Owner.PlayerActor.Trait<PlayerResources>();
		}

		void INotifyOwnerChanged.OnOwnerChanged(Actor self, Player oldOwner, Player newOwner)
		{
			playerResources = newOwner.PlayerActor.Trait<PlayerResources>();
		}

		void ITick.Tick(Actor self)
		{
			// Ticks every frame but only ever SEES a value that changes once per PassiveIncomeInterval,
			// because UpkeepShortfall is latched by the payday that computed it. The token comparison
			// below is what makes the other 49 ticks free.
			var inArrears = playerResources != null && playerResources.UpkeepShortfall > 0;

			if (inArrears && conditionToken == Actor.InvalidConditionToken)
				conditionToken = self.GrantCondition(info.Condition);
			else if (!inArrears && conditionToken != Actor.InvalidConditionToken)
				conditionToken = self.RevokeCondition(conditionToken);
		}
	}
}
