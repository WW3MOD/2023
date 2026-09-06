#region Copyright & License Information
/*
 * WW3MOD guard for the Lua presentation bindings (2026-09-06).
 *
 * Camera.Zoom moves the viewport from a map script. The viewport is per-client view state, so that
 * is only safe while two things hold, and neither is visible at the call site:
 *
 *   1. Nothing sync-hashed reads the viewport. That is ViewportIsNotSimulationStateTest's job, and
 *      it covers Viewport.SetZoom automatically because it enumerates Viewport's members by
 *      metadata token rather than by name.
 *   2. The zoom path itself does not reach back into the simulation. That is this file's job, and
 *      it is the same argument ScreenShakerTouchesNothingButTheViewport makes for the shake model.
 *
 * Trigger.OnTick is the opposite case and is filed here for the contrast: it runs INSIDE the
 * simulation, so what needs pinning is not that it avoids the world but that every client walks the
 * callbacks in the same order.
 */
#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Scripting;
using OpenRA.Scripting;

namespace OpenRA.Test
{
	[TestFixture]
	public class LuaPresentationApiTest
	{
		[Test]
		public void CameraGlobalTouchesNothingButTheViewport()
		{
			// A script global is not sync-hashed, so the hazard is not that CameraGlobal itself is
			// hashed — it is that a "camera" binding could quietly do simulation work on the way to
			// moving the view, and then a demo staging a shot would change the match. Checking for
			// the RNG TYPES rather than for World.SharedRandom specifically is both scannable (an IL
			// call scan sees calls, and SharedRandom is a field) and strictly stronger: no generator
			// may be touched at all, whichever instance it came from.
			var forbidden = new[]
			{
				typeof(OpenRA.Support.MersenneTwister),
				typeof(Random),
				typeof(OpenRA.Traits.IIssueOrder),
				typeof(Order)
			};

			var resolvedCalls = 0;
			var offenders = new List<string>();

			foreach (var method in typeof(CameraGlobal)
				.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
					BindingFlags.Static | BindingFlags.DeclaredOnly)
				.OfType<MethodBase>())
			{
				var scan = IlScan.Scan(method);
				resolvedCalls += scan.ResolvedCalls;

				foreach (var callee in scan.Callees)
					if (forbidden.Contains(callee.DeclaringType))
						offenders.Add($"CameraGlobal.{method.Name} -> {callee.DeclaringType.Name}.{callee.Name}");
			}

			Assert.That(resolvedCalls, Is.GreaterThan(5),
				$"IL scan resolved only {resolvedCalls} call targets across CameraGlobal — the scanner " +
				"is broken, or the bindings were moved somewhere this no longer sees.");

			Assert.That(offenders, Is.Empty,
				"A Camera binding reaches into the simulation. Camera.Zoom and Camera.Position are " +
				"client-local presentation: a script staging a screenshot must not be able to change " +
				"what the match does:" + Environment.NewLine + string.Join(Environment.NewLine, offenders));
		}

		[Test]
		public void ZoomIsClampedOnASinglePathThroughTheViewport()
		{
			// Camera.Zoom, Test.SetZoom and the mouse wheel must not each carry their own idea of how
			// far out is far enough — the renderer's maximum viewport size is allocated from
			// EffectiveMinZoom (Viewport.UpdateViewportZooms), so a second, laxer clamp would ask the
			// renderer for a sheet it was never sized for. AdjustZoom routing through SetZoom is what
			// makes "one clamp" true, and it is invisible at every call site.
			var setZoom = typeof(Viewport).GetMethod("SetZoom", BindingFlags.Public | BindingFlags.Instance);
			Assert.That(setZoom, Is.Not.Null, "Viewport.SetZoom is gone — Camera.Zoom has no clamped entry point.");

			var adjustZoom = typeof(Viewport).GetMethod("AdjustZoom", BindingFlags.Public | BindingFlags.Instance,
				null, new[] { typeof(float) }, null);
			Assert.That(adjustZoom, Is.Not.Null, "Viewport.AdjustZoom(float) is gone.");

			Assert.That(IlScan.Scan(adjustZoom).Callees, Has.Some.Matches<MethodBase>(m => m == setZoom),
				"Viewport.AdjustZoom no longer clamps through SetZoom, so the mouse wheel and " +
				"Camera.Zoom can now disagree about the zoom floor.");

			Assert.That(typeof(Viewport).GetProperty("EffectiveMinZoom", BindingFlags.Public | BindingFlags.Instance),
				Is.Not.Null,
				"Viewport.EffectiveMinZoom is gone — Camera.MinZoom would report the locked floor, which " +
				"is four times higher than the one the mouse wheel actually reaches.");
		}

		[Test]
		public void TickCallbacksRunInARegistrationOrderedContainer()
		{
			// Trigger.OnTick callbacks run inside the simulation, so they may legitimately do
			// simulation work — which makes their INVOCATION ORDER load-bearing. Registration order is
			// script-load order and identical on every client; hash order is not. A future refactor to
			// a HashSet or Dictionary here would be invisible in single-player and would desync a
			// multiplayer match only once two callbacks both touched the world.
			var field = typeof(ScriptContext).GetField("tickCallbacks",
				BindingFlags.NonPublic | BindingFlags.Instance);

			Assert.That(field, Is.Not.Null,
				"ScriptContext.tickCallbacks is gone or renamed — Trigger.OnTick's ordering guarantee " +
				"is no longer pinned by anything.");

			Assert.That(field.FieldType.IsGenericType, Is.True, $"tickCallbacks is a {field.FieldType.Name}.");
			Assert.That(field.FieldType.GetGenericTypeDefinition(), Is.EqualTo(typeof(List<>)),
				$"Trigger.OnTick callbacks are stored in a {field.FieldType.Name}. They must be walked in " +
				"registration order on every client, so the container has to be an ordered one.");
		}
	}
}
