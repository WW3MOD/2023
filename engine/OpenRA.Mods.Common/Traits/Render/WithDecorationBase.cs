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

using System;
using System.Collections.Generic;
using System.Linq;
using OpenRA.Graphics;
using OpenRA.Support;
using OpenRA.Traits;
using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Traits.Render
{
	public enum BlinkState { Off, On }

	public static class DecorationBlink
	{
		// Render-only blink-phase math. Derives the pattern index from wall-clock milliseconds so the
		// blink cadence is constant regardless of game speed (fast-forward / slow-motion). One pattern
		// step lasts blinkInterval nominal ticks; nominalTickMs anchors that to real time (Ui.Timestep
		// at normal speed) so the on-screen appearance is unchanged at default speed. Pure: reads no
		// sim / [Sync] state and writes nothing — safe to call from the render path only.
		public static int PhaseIndex(long runTimeMs, int blinkInterval, int nominalTickMs, int patternLength)
		{
			var stepMs = Math.Max(1L, (long)blinkInterval * nominalTickMs);
			return (int)(runTimeMs / stepMs % patternLength);
		}

		// Milliseconds per frame at the given health, ramping linearly from baseTickMs at (and above)
		// rampStartHealth down to rampTickMs at zero health, so the blink rate itself reads as how much
		// time the actor has left. Linear in the INTERVAL rather than in the frequency: a frequency ramp
		// spends almost all of its visible change in the last few percent of health, which is the
		// "evaluates to nothing over most of its range" shape this indicator has to avoid.
		// Pure: no sim state, no RNG.
		public static int IntervalForHealth(int baseTickMs, int rampTickMs, int rampStartHealth, int healthPercent)
		{
			if (rampTickMs <= 0 || rampStartHealth <= 0)
				return baseTickMs;

			var h = Math.Clamp(healthPercent, 0, rampStartHealth);
			return rampTickMs + (baseTickMs - rampTickMs) * h / rampStartHealth;
		}
	}

	// Accumulates blink phase so that the interval can CHANGE without the frame jumping.
	//
	// PITFALL: do not compute a variable-rate blink as runTime / interval % length. That re-derives the
	// index from absolute time, so the moment the interval changes the index leaps somewhere unrelated —
	// at runTime 300000ms an interval of 450 gives frame 0 and 440 gives frame 1. With a health-scaled
	// interval that re-rolls on EVERY damage event, so the pip stutters and skips exactly while it is
	// being shot at, which is the one moment it exists to be read. Phase is therefore carried forward
	// across rate changes instead, in thousandths of a frame to keep it integer.
	//
	// This state is intentionally client-divergent — it is seeded from wall-clock, which differs between
	// machines. Nothing synced may ever read it. It feeds one decoration's sprite and nothing else.
	public sealed class BlinkPhase
	{
		long anchorMs;
		long anchorMilliFrames;
		int intervalMs = 1;
		bool started;

		public int Advance(long runTimeMs, int newIntervalMs, int patternLength)
		{
			if (patternLength <= 0)
				return 0;

			newIntervalMs = Math.Max(1, newIntervalMs);

			if (!started)
			{
				anchorMs = runTimeMs;
				intervalMs = newIntervalMs;
				started = true;
			}
			else if (newIntervalMs != intervalMs)
			{
				// Re-anchor at the phase already reached, so the new rate continues from here.
				anchorMilliFrames = MilliFrames(runTimeMs);
				anchorMs = runTimeMs;
				intervalMs = newIntervalMs;
			}

			return (int)(MilliFrames(runTimeMs) / 1000 % patternLength);
		}

		long MilliFrames(long runTimeMs)
		{
			var elapsed = Math.Max(0L, runTimeMs - anchorMs);
			return anchorMilliFrames + elapsed * 1000 / intervalMs;
		}
	}

	public abstract class WithDecorationBaseInfo : ConditionalTraitInfo
	{
		[Desc("Position in the actor's selection box to draw the decoration.")]
		public readonly string Position = "TopLeft";

		[Desc("Player relationships who can view the decoration.")]
		public readonly PlayerRelationship ValidRelationships = PlayerRelationship.Ally;

		[Desc("Should this be visible only when selected?")]
		public readonly bool RequiresSelection = false;

		[Desc("Offset sprite center position from the selection box edge.")]
		public readonly int2 Margin = int2.Zero;

		[Desc("Screen-space offsets to apply when defined conditions are enabled.",
			"A dictionary of [condition string]: [x, y offset].")]
		public readonly Dictionary<BooleanExpression, int2> Offsets = new();

		[Desc("The number of ticks that each step in the blink pattern in active.")]
		public readonly int BlinkInterval = 5;

		[Desc("A pattern of ticks (BlinkInterval long) where the decoration is visible or hidden.")]
		public readonly BlinkState[] BlinkPattern = Array.Empty<BlinkState>();

		[Desc("Override blink conditions to use when defined conditions are enabled.",
			"A dictionary of [condition string]: [pattern].")]
		public readonly Dictionary<BooleanExpression, BlinkState[]> BlinkPatterns = new();

		[ConsumedConditionReference]
		public IEnumerable<string> ConsumedConditions
		{
			get { return Offsets.Keys.Concat(BlinkPatterns.Keys).SelectMany(r => r.Variables).Distinct(); }
		}
	}

	public abstract class WithDecorationBase<InfoType> : ConditionalTrait<InfoType>, IDecoration where InfoType : WithDecorationBaseInfo
	{
		protected readonly Actor Self;
		int2 conditionalOffset;
		BlinkState[] blinkPattern;

		protected WithDecorationBase(Actor self, InfoType info)
			: base(info)
		{
			Self = self;
			blinkPattern = info.BlinkPattern;
		}

		protected virtual bool ShouldRender(Actor self)
		{
			if (self.World.FogObscures(self))
				return false;

			if (blinkPattern != null && blinkPattern.Length > 0)
			{
				// PITFALL: drive the blink from wall-clock (Game.RunTime), NOT self.World.WorldTick — ticks
				// advance at the game-speed logic rate, so a WorldTick-driven blink strobes on fast-forward
				// and crawls on slow-motion. Ui.Timestep anchors the cadence to normal speed. Render-only.
				var i = DecorationBlink.PhaseIndex(Game.RunTime, Info.BlinkInterval, Ui.Timestep, blinkPattern.Length);
				if (blinkPattern[i] != BlinkState.On)
					return false;
			}

			if (self.World.RenderPlayer != null)
			{
				var relationship = self.Owner.RelationshipWith(self.World.RenderPlayer);
				if (!Info.ValidRelationships.HasRelationship(relationship))
					return false;
			}

			return true;
		}

		bool IDecoration.RequiresSelection => Info.RequiresSelection;

		protected abstract IEnumerable<IRenderable> RenderDecoration(Actor self, WorldRenderer wr, int2 pos);

		IEnumerable<IRenderable> IDecoration.RenderDecoration(Actor self, WorldRenderer wr, ISelectionDecorations container)
		{
			if (IsTraitDisabled || self.IsDead || !self.IsInWorld || !ShouldRender(self))
				return Enumerable.Empty<IRenderable>();

			var screenPos = container.GetDecorationOrigin(self, wr, Info.Position, Info.Margin) + conditionalOffset;
			return RenderDecoration(self, wr, screenPos);
		}

		public override IEnumerable<VariableObserver> GetVariableObservers()
		{
			foreach (var observer in base.GetVariableObservers())
				yield return observer;

			foreach (var condition in Info.Offsets.Keys)
				yield return new VariableObserver(OffsetConditionChanged, condition.Variables);

			foreach (var condition in Info.BlinkPatterns.Keys)
				yield return new VariableObserver(BlinkConditionsChanged, condition.Variables);
		}

		void OffsetConditionChanged(Actor self, IReadOnlyDictionary<string, int> conditions)
		{
			conditionalOffset = int2.Zero;
			foreach (var kv in Info.Offsets)
			{
				if (kv.Key.Evaluate(conditions))
				{
					conditionalOffset = kv.Value;
					break;
				}
			}
		}

		void BlinkConditionsChanged(Actor self, IReadOnlyDictionary<string, int> conditions)
		{
			blinkPattern = Info.BlinkPattern;
			foreach (var kv in Info.BlinkPatterns)
			{
				if (kv.Key.Evaluate(conditions))
				{
					blinkPattern = kv.Value;
					return;
				}
			}
		}
	}
}
