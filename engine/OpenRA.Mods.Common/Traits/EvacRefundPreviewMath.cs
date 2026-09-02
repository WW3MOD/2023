#region Copyright & License Information
/*
 * WW3MOD evacuation refund preview — the arithmetic behind the figure on the Evacuate button (pure math).
 *
 * WHY THIS EXISTS: the evacuation payout is assembled in TWO places at TWO different times, and the split is
 * not where it looks. DeliversCash.GoDonateCash (DeliversCash.cs:96-103) freezes the VALUE terms at ORDER
 * time — base cost (CustomSellValue.Value or Valued.Cost), the missing-ammo deduction, the missing-supply
 * deduction, and the handicap multiplier — and hands the total to RotateToEdge as `fixedRefund`.
 * RotateToEdge.DoSell (RotateToEdge.cs:447-452) then applies the one term read at ARRIVAL: HP/MaxHP.
 *
 * So GetEvacuationRefund is NOT what the player is paid. It carries no health term AT ALL, and a preview
 * built on it alone would overstate the payout for every damaged unit — by 70% on a 30%-HP tank, at the
 * instant of the press, before any drift on the walk home. ScaleByHealth is that missing factor, extracted
 * here so the button and the payout cannot disagree: DoSell calls it too rather than doing it inline.
 *
 * DETERMINISM: pure integer arithmetic, no random draws, no collection iteration. The long promotion and the
 * truncating division are preserved exactly as DoSell had them inline (int * long => long, then floor), so
 * no refund anywhere moves by a single credit.
 */
#endregion

namespace OpenRA.Mods.Common.Traits
{
	public static class EvacRefundPreviewMath
	{
		/// <summary><para>The single term of the evacuation payout that is read on ARRIVAL rather than frozen
		/// at order time: the fraction of health the actor still has.</para>
		///
		/// <para>An actor with no <c>IHealth</c> is scaled by 1/1 by its caller, matching DoSell.</para></summary>
		public static int ScaleByHealth(int refund, long hp, long maxHp)
		{
			return (int)(refund * hp / maxHp);
		}

		/// <summary><para>The Evacuate tooltip's refund line, or null when nothing in the selection can
		/// evacuate.</para>
		///
		/// <para>AGGREGATION: a plain sum over the evacuable subset — evacuation is per-unit and independent,
		/// so the total is what the budget actually gains.</para>
		///
		/// <para>MIXED SELECTION: the count is always stated when it is not a lone unit, and the "of
		/// <paramref name="selected"/>" form appears whenever the selection contains something the order will
		/// not act on. The button enables on ANY evacuable actor in the selection (CommandBarLogic:481) and
		/// the order is broadcast to all of them (PerformKeyboardOrderOnSelection), with only
		/// DeliversCash@Rotation holders resolving it — so a bare total would silently cover fewer units than
		/// the player has highlighted.</para></summary>
		public static string FormatRefundLine(int total, int evacuable, int selected)
		{
			if (evacuable <= 0)
				return null;

			if (evacuable == 1 && selected == 1)
				return $"Refund at current value: ${total}";

			if (evacuable == selected)
				return $"Refund at current value: ${total} ({evacuable} units)";

			return $"Refund at current value: ${total} ({evacuable} of {selected} selected)";
		}
	}
}
