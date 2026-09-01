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

namespace OpenRA.Traits
{
	[TraitLocation(SystemActors.World | SystemActors.EditorWorld)]
	[Desc("Enables visualization commands. Attach this to the world actor.")]
	public class DebugVisualizationsInfo : TraitInfo<DebugVisualizations> { }

	public class DebugVisualizations
	{
		public bool CombatGeometry;
		public bool RenderGeometry;
		public bool ScreenMap;
		public bool ActorTags;

		/// <summary>
		/// <para>WW3MOD developer overlay: floating damage numbers on every hit, plus a highlighted
		/// readout when HitCheck flags a shot that armour turned from lethal to harmless. Toggled by
		/// the "Damage Numbers" checkbox in the debug panel.</para>
		///
		/// <para>DEFAULTS OFF, AND TURNING IT BACK ON IS A RELEASE BLOCKER. If you set this to true you
		/// must file a BLOCKER entry in WORKSPACE/PIPELINE.md carrying the marker
		/// HITCHECK-OVERLAY-DEFAULT-ON in the same commit -- DebugVisualizationDefaultsTest asserts
		/// the entry exists if and only if this is true, and will fail the build until you do.
		/// That is not bureaucracy: a debug overlay visible in a stranger's first match is an
		/// immediately-visible this-is-unfinished signal, which is the release audit's own
		/// definition of a BLOCKER.</para>
		///
		/// <para>It shipped default-ON briefly (user ruling 2026-08-30, "that could be made default on for
		/// now, as long as we change it before release"), tracked as R17. The deferral ran out of
		/// road the same day: main is pushed as work lands and the user play-tests from another
		/// machine, so default-on would have put damage numbers over every unit on their next pull --
		/// and directly under a planned full play-through whose whole purpose is filing polish items,
		/// which would then have been filed against a debug build. Flipped, R17 discharged and
		/// deleted, and the lock left in place pointing the other way.</para>
		///
		/// <para>THE DETECTOR IS NOT AFFECTED BY THIS FLAG. HitCheck writes hitcheck.log unconditionally;
		/// this gates only the on-screen half. Turning the overlay off costs visibility, never
		/// detection.</para>
		///
		/// <para>Kept separate from CombatGeometry deliberately: this readout used to be bundled into it,
		/// so asking for a damage number also drew hitshape and muzzle wireframes, which is why it
		/// read as a missing feature rather than a developer one.</para>
		/// </summary>
		public bool DamageNumbers = false;

		// The depth buffer may have been left enabled by the previous world
		// Initializing this as dirty forces us to reset the default rendering before the first render
		bool depthBufferDirty = true;
		bool depthBuffer;
		public bool DepthBuffer
		{
			get => depthBuffer;
			set
			{
				depthBuffer = value;
				depthBufferDirty = true;
			}
		}

		float depthBufferContrast = 1f;
		public float DepthBufferContrast
		{
			get => depthBufferContrast;
			set
			{
				depthBufferContrast = value;
				depthBufferDirty = true;
			}
		}

		float depthBufferOffset;
		public float DepthBufferOffset
		{
			get => depthBufferOffset;
			set
			{
				depthBufferOffset = value;
				depthBufferDirty = true;
			}
		}

		public void UpdateDepthBuffer()
		{
			if (depthBufferDirty)
			{
				Game.Renderer.WorldSpriteRenderer.SetDepthPreview(DepthBuffer, DepthBufferContrast, DepthBufferOffset);
				depthBufferDirty = false;
			}
		}
	}
}
