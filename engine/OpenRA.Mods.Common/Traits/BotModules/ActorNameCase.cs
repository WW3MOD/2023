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

using System.Collections.Generic;

namespace OpenRA.Mods.Common.Traits
{
	// Every actor name is lowercased at ruleset load (Ruleset.cs:126 builds each ActorInfo with
	// k.Key.ToLowerInvariant()), so actor.Info.Name is ALWAYS lowercase at runtime. Bot-module
	// actor-name config collections, however, are materialized by FieldLoader with the default
	// ORDINAL (case-sensitive) comparer (FieldLoader.ParseHashSetOrList/ParseDictionary use
	// Activator.CreateInstance(fieldType, capacity), which discards any comparer chosen in the
	// field initializer). A HashSet/Dictionary built that way silently NO-MATCHES the moment a
	// YAML value carries an uppercase letter — no warning, no exception, the unit is just never
	// built / classified / priced.
	//
	// These helpers normalize an actor-name collection's contents to lowercase once, in the
	// Info's RulesetLoaded, so config case can never silently no-op. This is chosen over swapping
	// in StringComparer.OrdinalIgnoreCase because the fields are readonly (FieldLoader's fresh
	// ordinal instance can't be replaced without reflection or dropping readonly) and because it
	// touches only the flagged collections' contents — it cannot regress ordinal semantics for any
	// other string set (production-queue Type sets, target-type BitSets, terrain/cohesion enums).
	//
	// ONLY call these on collections whose values are ACTOR NAMES. Idempotent: lowercasing an
	// already-lowercase value is a no-op, so re-running (or running against all-lowercase config)
	// leaves the collection byte-identical.
	public static class ActorNameCase
	{
		public static void NormalizeInPlace(HashSet<string> actorNames)
		{
			if (actorNames == null || actorNames.Count == 0)
				return;

			var lowered = new List<string>(actorNames.Count);
			foreach (var name in actorNames)
				lowered.Add(name.ToLowerInvariant());

			actorNames.Clear();
			foreach (var name in lowered)
				actorNames.Add(name);
		}

		public static void NormalizeKeysInPlace(Dictionary<string, int> actorNameKeys)
		{
			if (actorNameKeys == null || actorNameKeys.Count == 0)
				return;

			var lowered = new List<KeyValuePair<string, int>>(actorNameKeys.Count);
			foreach (var kv in actorNameKeys)
				lowered.Add(new KeyValuePair<string, int>(kv.Key.ToLowerInvariant(), kv.Value));

			actorNameKeys.Clear();
			foreach (var kv in lowered)
				actorNameKeys[kv.Key] = kv.Value;
		}
	}
}
