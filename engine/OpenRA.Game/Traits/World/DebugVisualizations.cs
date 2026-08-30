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
		/// WW3MOD developer overlay: floating damage numbers on every hit, plus a highlighted
		/// readout when HitCheck flags a shot that armour turned from lethal to harmless.
		///
		/// ############################################################################
		/// # DEFAULTS TO TRUE ON PURPOSE, AND MUST BE FLIPPED TO FALSE BEFORE RELEASE. #
		/// ############################################################################
		///
		/// User ruling 2026-08-30: developer-facing feedback only, no player-facing combat feedback
		/// of any kind -- "that could be made default on for now, as long as we change it before
		/// release so make notes appropriately". A debug overlay visible in a stranger's first match
		/// is an immediately-visible this-is-unfinished signal, which is the release audit's own
		/// definition of a BLOCKER.
		///
		/// This comment is NOT the countermeasure -- prose has failed twice in this repo already.
		/// The load-bearing guards are the BLOCKER entry in WORKSPACE/PIPELINE.md and
		/// DebugVisualizationDefaultsTest, which asserts this value and fails the build the moment it
		/// changes, so flipping it is a deliberate act with a visible diff.
		///
		/// Previously this readout was bundled into CombatGeometry, so seeing a damage number also
		/// drew hitshape and muzzle wireframes -- which is why it read as missing rather than as a
		/// developer feature.
		/// </summary>
		public bool DamageNumbers = true;

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
