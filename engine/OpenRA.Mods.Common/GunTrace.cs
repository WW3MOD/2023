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

namespace OpenRA.Mods.Common
{
	/// <summary>
	/// Env-gated trace of the gun→bullet→warhead→health chain, for diagnosing
	/// "it fires and nothing dies". Enable with WW3_GUNTRACE=1; off by default,
	/// so this costs one static bool read per call site in a normal game.
	/// </summary>
	public static class GunTrace
	{
		public static readonly bool Enabled =
			Environment.GetEnvironmentVariable("WW3_GUNTRACE") == "1";

		public static void Write(string line)
		{
			if (Enabled)
				Log.Write("debug", "[GUNTRACE] " + line);
		}
	}
}
