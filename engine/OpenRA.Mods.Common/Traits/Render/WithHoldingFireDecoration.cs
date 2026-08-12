#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

namespace OpenRA.Mods.Common.Traits.Render
{
	[Desc("Shows a decoration while this actor's target scan is deliberately declining an enemy it",
		"could otherwise shoot — anti-overkill or break-off. Without it a unit standing next to a live",
		"enemy and not firing is indistinguishable from a bug.",
		"Render-only: reads AutoTarget and writes nothing, so it cannot change what the unit does.")]
	public class WithHoldingFireDecorationInfo : WithDecorationInfo
	{
		[Desc("Keep showing the marker for this many ticks after the last declining scan.",
			"Scans run every MinimumScanTimeInterval..MaximumScanTimeInterval ticks, so this must span",
			"more than one scan or the marker strobes between them.")]
		public readonly int LingerTicks = 15;

		public override object Create(ActorInitializer init) { return new WithHoldingFireDecoration(init.Self, this); }
	}

	public class WithHoldingFireDecoration : WithDecoration
	{
		readonly WithHoldingFireDecorationInfo info;
		AutoTarget autoTarget;

		public WithHoldingFireDecoration(Actor self, WithHoldingFireDecorationInfo info)
			: base(self, info)
		{
			this.info = info;
		}

		protected override void Created(Actor self)
		{
			base.Created(self);

			// Resolved here rather than in the constructor: AutoTarget may not exist yet at
			// construction time, and it is optional on a decorated actor.
			autoTarget = self.TraitOrDefault<AutoTarget>();
		}

		protected override bool ShouldRender(Actor self)
		{
			if (autoTarget == null)
				return false;

			var last = autoTarget.LastHeldFireTick;
			if (last < 0 || self.World.WorldTick - last > info.LingerTicks)
				return false;

			return base.ShouldRender(self);
		}
	}
}
