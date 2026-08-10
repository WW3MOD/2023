#region Copyright & License Information
/*
 * Copyright 2007-2022 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Modifies the actor's selection priority when the required condition is met.",
		"Useful for deprioritizing evacuating units from box-select.")]
	public class SelectionPriorityModifierInfo : ConditionalTraitInfo
	{
		[Desc("Priority modifier applied when active. Negative values deprioritize.")]
		public readonly int Modifier = -20;

		[Desc("Hold this modifier key to suppress the deprioritization and select the actor alongside",
			"its healthy peers again. Needed because box-select keeps only the single highest priority",
			"GROUP: a deprioritized unit is not merely ranked lower, it is filtered out entirely, and",
			"Selectable.PriorityModifiers alone cannot undo that (its boost is applied before this",
			"modifier, so the two tiers stay apart at int.MaxValue and int.MaxValue - Modifier).",
			"Valid values are None (no override — the shipped default, so existing deprioritizations",
			"keep their current behaviour), Ctrl and Alt. Should normally match the actor's",
			"Selectable.PriorityModifiers so one key does both jobs.")]
		public readonly SelectionPriorityModifiers SuppressedBy = SelectionPriorityModifiers.None;

		public override object Create(ActorInitializer init) { return new SelectionPriorityModifier(this); }
	}

	public class SelectionPriorityModifier : ConditionalTrait<SelectionPriorityModifierInfo>, ISelectionPriorityModifier
	{
		public SelectionPriorityModifier(SelectionPriorityModifierInfo info)
			: base(info) { }

		int ISelectionPriorityModifier.GetSelectionPriorityModifier(Modifiers modifiers)
		{
			if (IsTraitDisabled || SelectionPriorityMath.Suppressed(Info.SuppressedBy, modifiers))
				return 0;

			return Info.Modifier;
		}
	}

	/// <summary>
	/// The suppression rule, kept free of Actor/World so it is unit-testable. Mirrors
	/// <c>SelectableExts.BaseSelectionPriority</c>'s exclusive reading of Ctrl and Alt: holding both at
	/// once is neither, so a two-key press cannot accidentally satisfy an override the player did not ask
	/// for.
	/// </summary>
	public static class SelectionPriorityMath
	{
		public static bool Suppressed(SelectionPriorityModifiers suppressedBy, Modifiers held)
		{
			if (suppressedBy == SelectionPriorityModifiers.None)
				return false;

			if (held.HasModifier(Modifiers.Ctrl) && !held.HasModifier(Modifiers.Alt))
				return suppressedBy.HasFlag(SelectionPriorityModifiers.Ctrl);

			if (held.HasModifier(Modifiers.Alt) && !held.HasModifier(Modifiers.Ctrl))
				return suppressedBy.HasFlag(SelectionPriorityModifiers.Alt);

			return false;
		}
	}
}
