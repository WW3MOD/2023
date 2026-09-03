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
	[Desc("Shows a diamond whose fill and colour say how visible this unit currently is.",
		"",
		"GRADED, and it did not used to be. This trait shipped as a binary red '!' — drawn while an enemy the",
		"viewing player was aware of could see the unit, and encoding nothing else, on the stated grounds that",
		"'spotted is spotted'. That decision was reversed deliberately: the request was for a readout of HOW",
		"visible a unit is, which a boolean cannot carry. Set Graded: false to restore the old mark exactly —",
		"Text and Color are still the ungraded glyph and colour and are untouched by the graded path.",
		"",
		"WHAT THE GRADE IS MADE OF. Bands 0-3 read the unit's OWN posture (Detectable.CurrentVisibility:",
		"cover, prone, dug-in, firing, moving, rank) and use no information about where enemies are. Only the",
		"top band, Spotted, is enemy-derived, and it is the same predicate the old '!' used.",
		"",
		"Asymmetry rule, unchanged and load-bearing: an enemy that can see us but that we have not spotted",
		"ourselves does NOT light the top band. A badge driven by true visibility alone would be a wallhack —",
		"it would announce 'someone you cannot see is watching you'. Grading on own posture cannot reintroduce",
		"that, because posture is knowledge the unit legitimately has.",
		"",
		"RENDER-ONLY. Evaluated from the render path, reads RenderPlayer, and writes nothing that",
		"simulation can observe, so it cannot desync and it grants no condition. That is deliberate:",
		"driving this from a granted condition is the shape of two shipped desyncs in this repo (see the",
		"PITFALL at Detectable.cs:152) because a condition token is an allocation handle, not gameplay",
		"state. Nothing here is [Sync] and nothing here is read by any unit's decisions. CurrentVisibility is",
		"[Sync]ed simulation state, but it is only READ here, which is what every decoration already does.")]
	public class WithSpottedDecorationInfo : WithTextDecorationInfo
	{
		[Desc("Ticks between recomputations of the SPOTTED test only. The result is cached in between:",
			"ShouldRender runs once per frame but the answer only needs to change at a sim-relevant rate.",
			"The posture bands are NOT cached — they are one field read, and caching them at this interval",
			"would put a visible ~0.4s lag on the one thing the player changes on purpose.")]
		public readonly int RecalculationInterval = 7;

		[Desc("Only enemies within this distance are considered as possible observers. This bounds the",
			"spatial query, so it MUST be at least the largest Vision.Range in the mod — a long-sighted",
			"observer beyond it is silently missed and the unit reads as unspotted.",
			"Default matches ^StandardVision's outermost band (32c0); raise it if a unit is ever given",
			"vision past that.")]
		public readonly WDist MaximumObserverRange = new WDist(32768);

		[Desc("Draw the graded diamond. False restores the original binary mark exactly: Text in Color,",
			"drawn only while spotted. This is the whole-feature off switch.")]
		public readonly bool Graded = true;

		[Desc("Highest exposure level that still reads as Concealed. Exposure is the INVERSE of",
			"Detectable.CurrentVisibility and runs the other way: 1 is as hidden as the clamp allows,",
			"Detectability.MaximumConcealment (9) is nothing hiding the unit at all. Raising a ceiling moves",
			"MORE units into the lower, calmer band.")]
		public readonly int ConcealedExposureCeiling = 3;

		[Desc("Highest exposure level that still reads as Low.")]
		public readonly int LowExposureCeiling = 5;

		[Desc("Highest exposure level that still reads as Moderate. Above this is High.")]
		public readonly int ModerateExposureCeiling = 7;

		[Desc("Grades below this are not drawn at all. Concealed (the default) draws every unit every frame;",
			"Moderate draws only units that are becoming visible; Spotted reproduces the old mark's density",
			"with the new glyph. This is the knob for 'the map is too busy'.")]
		public readonly DetectabilityGrade MinimumDrawnGrade = DetectabilityGrade.Concealed;

		[Desc("Grades at or above this draw the filled diamond; below it, the hollow one. The fill step is the",
			"coarse channel — it survives low zoom and colour blindness, where the colour ramp alone does not.")]
		public readonly DetectabilityGrade SolidFromGrade = DetectabilityGrade.Moderate;

		[Desc("Glyph for the concealed half of the scale. U+25CA LOZENGE.",
			"PITFALL: this must be a glyph the mod's font actually carries, and the obvious diamond is not.",
			"FreeSansBold.ttf ships NO Geometric Shapes block — U+25C6 BLACK DIAMOND and U+25C7 WHITE DIAMOND",
			"are both absent from its cmap and would render as nothing or as a notdef box, silently, exactly",
			"like a sequence naming a file the mod does not ship. U+25CA and U+2666 are the only hollow/solid",
			"diamond pair the shipped font has, and both were verified to carry real glyph outlines.",
			"Written as an escape, not as a literal, so that a re-encoding of this file cannot quietly turn it",
			"into a character the font does not have — the failure it would cause is invisible.")]
		public readonly string HollowText = "\u25CA";

		[Desc("Glyph for the exposed half of the scale. U+2666 BLACK DIAMOND SUIT.",
			"Same font caveat as HollowText.")]
		public readonly string SolidText = "\u2666";

		[Desc("Colour at Concealed.")]
		public readonly Color ConcealedColor = Color.FromArgb(0x6E, 0x9E, 0x76);

		[Desc("Colour at Low.")]
		public readonly Color LowColor = Color.FromArgb(0xEC, 0xC7, 0x3C);

		[Desc("Colour at Moderate.")]
		public readonly Color ModerateColor = Color.FromArgb(0xF0, 0xB2, 0x32);

		[Desc("Colour at High.")]
		public readonly Color HighColor = Color.FromArgb(0xF0, 0x94, 0x25);

		[Desc("Colour at Spotted. Deliberately the same FF4A3C the binary '!' used, so the one state players",
			"already recognise keeps its exact colour across the change.")]
		public readonly Color SpottedColor = Color.FromArgb(0xFF, 0x4A, 0x3C);

		public override object Create(ActorInitializer init) { return new WithSpottedDecoration(init.Self, this); }
	}

	public class WithSpottedDecoration : WithTextDecoration
	{
		readonly WithSpottedDecorationInfo info;
		readonly SpriteFont gradedFont;

		Detectable detectable;
		int cachedTick = int.MinValue;
		bool cachedSpotted;

		public WithSpottedDecoration(Actor self, WithSpottedDecorationInfo info)
			: base(self, info)
		{
			this.info = info;

			// WithTextDecoration keeps its own font handle private and renders Info.Text in a colour fixed at
			// construction. The graded path needs both to vary per frame, so it renders itself and therefore
			// needs its own handle. Same font, resolved the same way — no second font is loaded.
			gradedFont = Game.Renderer.Fonts[info.Font];
		}

		protected override void Created(Actor self)
		{
			base.Created(self);

			detectable = self.TraitOrDefault<Detectable>();
		}

		protected override bool ShouldRender(Actor self)
		{
			// Cheap gates (fog, viewer relationship, blink) before the spatial query.
			if (!base.ShouldRender(self))
				return false;

			if (!info.Graded)
				return IsSpottedCached(self);

			return CurrentGrade(self) >= info.MinimumDrawnGrade;
		}

		protected override IEnumerable<IRenderable> RenderDecoration(Actor self, WorldRenderer wr, int2 screenPos)
		{
			if (!info.Graded)
				return base.RenderDecoration(self, wr, screenPos);

			if (IsTraitDisabled || self.IsDead || !self.IsInWorld || !ShouldRender(self))
				return Enumerable.Empty<IRenderable>();

			var grade = CurrentGrade(self);
			var text = grade >= info.SolidFromGrade ? info.SolidText : info.HollowText;
			var size = gradedFont.Measure(text);

			return new IRenderable[]
			{
				new UITextRenderable(gradedFont, self.CenterPosition, screenPos - size / 2, 0, ColorFor(grade), text)
			};
		}

		public DetectabilityGrade CurrentGrade(Actor self)
		{
			// Absent a Detectable trait there is no posture to read, so the unit sits at the exposed end of
			// the scale rather than claiming a concealment it has not got.
			var concealment = detectable != null ? detectable.CurrentVisibility : Detectability.MinimumConcealment;

			return Detectability.Grade(concealment, IsSpottedCached(self),
				info.ConcealedExposureCeiling, info.LowExposureCeiling, info.ModerateExposureCeiling);
		}

		Color ColorFor(DetectabilityGrade grade)
		{
			switch (grade)
			{
				case DetectabilityGrade.Concealed: return info.ConcealedColor;
				case DetectabilityGrade.Low: return info.LowColor;
				case DetectabilityGrade.Moderate: return info.ModerateColor;
				case DetectabilityGrade.High: return info.HighColor;
				default: return info.SpottedColor;
			}
		}

		bool IsSpottedCached(Actor self)
		{
			var tick = self.World.WorldTick;
			if (cachedTick == int.MinValue || tick - cachedTick >= info.RecalculationInterval || tick < cachedTick)
			{
				cachedTick = tick;
				cachedSpotted = IsSpotted(self);
			}

			return cachedSpotted;
		}

		bool IsSpotted(Actor self)
		{
			// RenderPlayer is null for observers and for replays with no player locked in; falling back to
			// the owner keeps the mark meaningful there rather than blanking it.
			var viewer = self.World.RenderPlayer ?? self.Owner;
			if (viewer == null)
				return false;

			// The strength an observer's vision must still carry at our cell to reveal us — the same number
			// Detectable.IsVisibleInner feeds to MapLayers.IsVisible. Absent a Detectable trait, 1 is the
			// floor AddSource clamps modified strength to.
			var required = detectable != null ? detectable.CurrentVisibility : 1;

			foreach (var observer in self.World.FindActorsInCircle(self.CenterPosition, info.MaximumObserverRange))
			{
				if (observer == self || observer.IsDead || !observer.IsInWorld)
					continue;

				var owner = observer.Owner;
				if (owner.NonCombatant || owner.RelationshipWith(viewer) != PlayerRelationship.Enemy)
					continue;

				// "an enemy that we are aware of" — we must be able to see the observer itself.
				if (!observer.CanBeViewedByPlayer(viewer))
					continue;

				if (!VisionCovers(observer, self, required))
					continue;

				// Truth gate, checked last because it is the most expensive. Without it the mark could claim
				// spotted when the observer's shroud does not actually reach us (terrain shadow, height
				// modifiers), and for a badge the player makes decisions on a false positive is worse than a
				// false negative.
				if (self.CanBeViewedByPlayer(owner))
					return true;
			}

			return false;
		}

		// Would this observer alone reveal the target? Reproduces the shipped rule rather than guessing at a
		// radius: WW3MOD grades vision into concentric Strength bands (^StandardVision runs Strength 10 at
		// 4c0 down to Strength 1 at 32c0), AddSource stamps each covered cell with the band's strength, and
		// Detectable reveals the actor when some stamped strength reaches its CurrentVisibility. So the
		// question is whether a band that both REACHES us and still carries enough strength exists.
		//
		// Using the outermost range instead would be nearly vacuous — every standard unit "sees" 32 cells at
		// Strength 1 — and would make the asymmetry rule almost never bite.
		//
		// Still an approximation, not the shroud: AddSource subtracts a terrain/airborne shadow modifier
		// from strength that is not reproduced here, so this can be optimistic in shadowed terrain. The
		// per-source records that would answer exactly are private with no accessor (VisionSource keeps
		// origin, strength and cells — it drops the owning Actor). The truth gate in IsSpotted is what stops
		// the optimism turning into a false positive.
		static bool VisionCovers(Actor observer, Actor target, int requiredStrength)
		{
			var distanceSquared = (observer.CenterPosition - target.CenterPosition).HorizontalLengthSquared;

			foreach (var vision in observer.TraitsImplementing<Vision>())
			{
				if (vision.Info is not VisionInfo visionInfo || visionInfo.Strength < requiredStrength)
					continue;

				var range = vision.Range;
				if (range == WDist.Zero || distanceSquared > (long)range.Length * range.Length)
					continue;

				var minRange = vision.MinRange;
				if (minRange.Length > 0 && distanceSquared < (long)minRange.Length * minRange.Length)
					continue;

				return true;
			}

			return false;
		}
	}
}
