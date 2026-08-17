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

using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits.Render
{
	[Desc("Shows a mark while an enemy the viewing player is AWARE OF can see this actor.",
		"Binary: drawn or not drawn. It deliberately encodes no observer count, distance or severity —",
		"spotted is spotted.",
		"",
		"Asymmetry rule: an enemy that can see us but that we have not spotted ourselves does NOT light",
		"the mark. A badge driven by true visibility alone would be a wallhack — it would announce",
		"'someone you cannot see is watching you'.",
		"",
		"RENDER-ONLY. Evaluated from the render path, reads RenderPlayer, and writes nothing that",
		"simulation can observe, so it cannot desync and it grants no condition. That is deliberate:",
		"driving this from a granted condition is the shape of two shipped desyncs in this repo (see the",
		"PITFALL at Detectable.cs:152) because a condition token is an allocation handle, not gameplay",
		"state. Nothing here is [Sync] and nothing here is read by any unit's decisions.")]
	public class WithSpottedDecorationInfo : WithTextDecorationInfo
	{
		[Desc("Ticks between recomputations. The result is cached in between: ShouldRender runs once per",
			"frame but the answer only needs to change at a sim-relevant rate.")]
		public readonly int RecalculationInterval = 7;

		[Desc("Only enemies within this distance are considered as possible observers. This bounds the",
			"spatial query, so it MUST be at least the largest Vision.Range in the mod — a long-sighted",
			"observer beyond it is silently missed and the unit reads as unspotted.",
			"Default matches ^StandardVision's outermost band (32c0); raise it if a unit is ever given",
			"vision past that.")]
		public readonly WDist MaximumObserverRange = new WDist(32768);

		public override object Create(ActorInitializer init) { return new WithSpottedDecoration(init.Self, this); }
	}

	public class WithSpottedDecoration : WithTextDecoration
	{
		readonly WithSpottedDecorationInfo info;

		Detectable detectable;
		int cachedTick = int.MinValue;
		bool cachedSpotted;

		public WithSpottedDecoration(Actor self, WithSpottedDecorationInfo info)
			: base(self, info)
		{
			this.info = info;
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
