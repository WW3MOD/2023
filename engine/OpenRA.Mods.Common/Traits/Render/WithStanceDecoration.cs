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

using System.Collections.Generic;
using System.Linq;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Graphics;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits.Render
{
	public enum StanceDecorationAxis { Fire, Engagement }

	[Desc("Draws a single-glyph mark for a NON-DEFAULT stance on one axis of AutoTarget.",
		"",
		"Only non-default states draw. A mark on every unit in FireAtWill/Defensive would be a mark on",
		"every unit on the map, which tells the player nothing; drawing only the deliberate states means",
		"the battlefield shows the handful of units you actually set.",
		"",
		"One instance per axis, so a unit that is both HoldFire and Hunt shows both. Text rather than a",
		"chevron because four states have to be told apart at a glance and four similar chevrons cannot",
		"be — a letter can.",
		"",
		"RENDER-ONLY: reads AutoTarget's public stance and writes nothing, so it cannot change what the",
		"unit does. Deliberately keyed off the trait rather than a granted condition — see the PITFALL at",
		"Detectable.cs:152. It also reads the SYNCED stance, not Predicted*, which is UI-local and would",
		"disagree with the sim for a round trip after a stance button is clicked.")]
	public class WithStanceDecorationInfo : WithDecorationBaseInfo
	{
		[Desc("Which stance axis this instance draws: Fire (HoldFire/Ambush) or Engagement (HoldPosition/Hunt).")]
		public readonly StanceDecorationAxis Axis = StanceDecorationAxis.Fire;

		public readonly string Font = "TinyBold";

		[Desc("Glyph drawn for UnitStance.HoldFire. Fire axis only.")]
		public readonly string HoldFireText = "X";

		public readonly Color HoldFireColor = Color.FromArgb(235, 235, 235);

		[Desc("Glyph drawn for UnitStance.Ambush. Fire axis only.")]
		public readonly string AmbushText = "A";

		public readonly Color AmbushColor = Color.FromArgb(255, 210, 70);

		[Desc("Glyph drawn for EngagementStance.HoldPosition. Engagement axis only.")]
		public readonly string HoldPositionText = "H";

		public readonly Color HoldPositionColor = Color.FromArgb(105, 205, 255);

		// '!' is deliberately NOT used here — it is the spotted mark's glyph, and the two would be read as
		// the same signal at 8px.
		[Desc("Glyph drawn for EngagementStance.Hunt. Engagement axis only.")]
		public readonly string HuntText = ">";

		public readonly Color HuntColor = Color.FromArgb(255, 145, 45);

		public override object Create(ActorInitializer init) { return new WithStanceDecoration(init.Self, this); }

		public override void RulesetLoaded(Ruleset rules, ActorInfo ai)
		{
			if (!Game.ModData.Manifest.Get<Fonts>().FontList.ContainsKey(Font))
				throw new YamlException($"Font '{Font}' is not listed in the mod.yaml's Fonts section");

			base.RulesetLoaded(rules, ai);
		}
	}

	public class WithStanceDecoration : WithDecorationBase<WithStanceDecorationInfo>
	{
		readonly SpriteFont font;

		AutoTarget autoTarget;

		public WithStanceDecoration(Actor self, WithStanceDecorationInfo info)
			: base(self, info)
		{
			font = Game.Renderer.Fonts[info.Font];
		}

		protected override void Created(Actor self)
		{
			base.Created(self);

			// Resolved here, not in the constructor: AutoTarget may not exist yet at construction time, and
			// it is absent entirely on unarmed actors this decoration is attached to via a shared template.
			autoTarget = self.TraitOrDefault<AutoTarget>();
		}

		bool TryGetGlyph(out string text, out Color color)
		{
			text = null;
			color = Color.White;

			if (autoTarget == null || autoTarget.IsTraitDisabled)
				return false;

			if (Info.Axis == StanceDecorationAxis.Fire)
			{
				switch (autoTarget.Stance)
				{
					case UnitStance.HoldFire: text = Info.HoldFireText; color = Info.HoldFireColor; return true;
					case UnitStance.Ambush: text = Info.AmbushText; color = Info.AmbushColor; return true;
					default: return false;
				}
			}

			switch (autoTarget.EngagementStanceValue)
			{
				case EngagementStance.HoldPosition: text = Info.HoldPositionText; color = Info.HoldPositionColor; return true;
				case EngagementStance.Hunt: text = Info.HuntText; color = Info.HuntColor; return true;
				default: return false;
			}
		}

		protected override bool ShouldRender(Actor self)
		{
			return TryGetGlyph(out _, out _) && base.ShouldRender(self);
		}

		protected override IEnumerable<IRenderable> RenderDecoration(Actor self, WorldRenderer wr, int2 screenPos)
		{
			if (!TryGetGlyph(out var text, out var color))
				return Enumerable.Empty<IRenderable>();

			var size = font.Measure(text);
			return new IRenderable[]
			{
				new UITextRenderable(font, self.CenterPosition, screenPos - size / 2, 0, color, text)
			};
		}
	}
}
