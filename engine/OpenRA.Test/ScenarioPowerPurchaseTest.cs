#region Copyright & License Information
/*
 * WW3MOD autotest-scenario wiring tests — do the scenarios that FIRE a support power still have a
 * way to get one?
 *
 * WHY THIS FIXTURE EXISTS, and it is a failure that already cost a week of frames. On 2026-09-06
 * every support power in the mod gained `RequiresPurchase: True`, which replaces the charge timer
 * with a magazine. SupportPowerInstance's constructor then forces both `TotalTicks` and
 * `remainingSubTicks` to 0 unconditionally (SupportPowerManager.cs:228-229), so the
 *
 *     MissileStrikePower@X:
 *         ChargeInterval: 1
 *         StartFullyCharged: true
 *
 * block that THIRTEEN autotest scenarios used to stage their power with became INERT — read, and
 * discarded. At zero banked charges `Disabled` is true, so `Active` is false, so `Ready` is false,
 * and every one of those scenarios' Test.ActivateSupportPower calls returned 'not-ready:0' for its
 * whole budget. Four demos stopped producing the frames they exist for; three asserting tests could
 * no longer reach the behaviour they assert.
 *
 * NOTHING REPORTED IT. The YAML is valid, so --check-yaml passes. The Lua names only real bindings,
 * so lua-gate passes. The demos have no verdict to fail, and the tests failed on a downstream
 * assertion whose message pointed at a lobby gate that was fine. The only signal was a launch slot
 * spent watching an empty sky.
 *
 * SO THIS READS THE SCENARIOS THE WAY THE ENGINE WILL. For every `Test.ActivateSupportPower(p, "K",
 * ...)` in a scenario's Lua it resolves K back to the mod's power definition and asserts that the
 * scenario can actually load a shot: money, a named proxy, and — for the EVENT tier, which no
 * faction provides — the sandbox lobby option. It also bans the inert pair outright, because that
 * is the shape the whole failure wore.
 *
 * SCOPE, HONESTLY. Static string wiring only, on scenarios whose Lua names a power key as a
 * LITERAL. It does not run a scenario, does not prove a purchase completes (SupportPowerChargeBank
 * and test-power-buy-loop cover that), does not check the buying player owns a producer, and cannot
 * see a key built at runtime from a variable.
 */
#endregion

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace OpenRA.Test
{
	[TestFixture]
	public class ScenarioPowerPurchaseTest
	{
		static string FindRepo(params string[] relative)
		{
			var dir = new DirectoryInfo(AppContext.BaseDirectory);
			for (var i = 0; i < 10 && dir != null; i++, dir = dir.Parent)
			{
				var candidate = Path.Combine(new[] { dir.FullName }.Concat(relative).ToArray());
				if (Directory.Exists(candidate) || File.Exists(candidate))
					return candidate;
			}

			throw new FileNotFoundException("could not locate " + string.Join("/", relative));
		}

		static string Field(MiniYamlNode node, string key)
		{
			return node.Value.Nodes.FirstOrDefault(n => n.Key == key)?.Value.Value?.Trim();
		}

		sealed class Power
		{
			public string Order;
			public bool RequiresPurchase;
			public string Tier;
			public string Proxy;
		}

		/// <summary>
		/// Every support power the mod defines, keyed by OrderName: whether it is bought, which tier
		/// prerequisite it carries, and which proxy actor sells it.
		///
		/// BOTH RULES FILES, and the merge is not optional. rules/player.yaml deliberately carries
		/// the TIER for the six powers DEFINED in rules/ingame/nuclear-arsenal.yaml, so a per-file
		/// walk sees six powers with an OrderName and no Prerequisites plus six of the reverse, and
		/// concludes the arsenal is ungated. This reproduces what MiniYaml does at load for the
		/// three fields that matter here.
		/// </summary>
		static Dictionary<string, Power> ModPowers()
		{
			var byOrder = new Dictionary<string, Power>();
			var orderOfTrait = new Dictionary<string, string>();
			var lateTier = new Dictionary<string, string>();

			var rules = FindRepo("mods", "ww3mod", "rules");
			foreach (var file in new[] { Path.Combine(rules, "player.yaml"), Path.Combine(rules, "ingame", "nuclear-arsenal.yaml") })
			{
				var player = MiniYaml.FromFile(file).FirstOrDefault(n => n.Key == "Player");
				if (player == null)
					continue;

				foreach (var trait in player.Value.Nodes)
				{
					if (!trait.Key.Split('@')[0].EndsWith("Power", StringComparison.Ordinal))
						continue;

					var order = Field(trait, "OrderName");
					if (order != null)
					{
						orderOfTrait[trait.Key] = order;
						byOrder[order] = new Power
						{
							Order = order,
							RequiresPurchase = string.Equals(Field(trait, "RequiresPurchase"), "True", StringComparison.OrdinalIgnoreCase),
						};
					}

					var tier = Field(trait, "Prerequisites");
					if (tier == null)
						continue;

					if (orderOfTrait.TryGetValue(trait.Key, out var known))
						byOrder[known].Tier = tier;
					else
						lateTier[trait.Key] = tier;
				}
			}

			// A tier written against a trait whose OrderName lives in the file read LATER. Order
			// independence matters: mod.yaml's file order must not silently turn this fixture green.
			foreach (var kv in orderOfTrait)
				if (byOrder[kv.Value].Tier == null && lateTier.TryGetValue(kv.Key, out var late))
					byOrder[kv.Value].Tier = late;

			foreach (var actor in MiniYaml.FromFile(Path.Combine(rules, "powers.yaml")))
			{
				var charge = actor.Value.Nodes.FirstOrDefault(n => n.Key == "ProvidesSupportPowerCharge");
				var sells = charge == null ? null : Field(charge, "Power");
				if (sells != null && byOrder.TryGetValue(sells, out var power))
					power.Proxy = actor.Key;
			}

			return byOrder;
		}

		/// <summary>
		/// The trait key rules/player.yaml or nuclear-arsenal.yaml uses for a given OrderName. Built
		/// by walking the same two files rather than guessed from the name: `KinzhalStrike` lives on
		/// `MissileStrikePower@Kinzhal`, and the suffix is not derivable from the order string.
		/// </summary>
		static Dictionary<string, string> ModTraitKeys()
		{
			var keys = new Dictionary<string, string>();
			var rules = FindRepo("mods", "ww3mod", "rules");
			foreach (var file in new[] { Path.Combine(rules, "player.yaml"), Path.Combine(rules, "ingame", "nuclear-arsenal.yaml") })
			{
				var player = MiniYaml.FromFile(file).FirstOrDefault(n => n.Key == "Player");
				if (player == null)
					continue;

				foreach (var trait in player.Value.Nodes)
				{
					var order = Field(trait, "OrderName");
					if (order != null)
						keys[order] = trait.Key;
				}
			}

			return keys;
		}

		sealed class Scenario
		{
			public string Name;
			public string RulesText;
			public string LuaText;
			public List<MiniYamlNode> Rules;
		}

		/// <summary>
		/// Every autotest scenario that carries both a rules.yaml and at least one .lua. Scenarios
		/// missing either are skipped rather than failed — that is lua-gate's and --check-yaml's
		/// job, and duplicating it here would report one fault twice under two names.
		/// </summary>
		static IEnumerable<Scenario> Scenarios()
		{
			var root = FindRepo("tools", "autotest", "scenarios");
			foreach (var dir in Directory.EnumerateDirectories(root).OrderBy(d => d, StringComparer.Ordinal))
			{
				var rules = Path.Combine(dir, "rules.yaml");
				if (!File.Exists(rules))
					continue;

				var lua = string.Concat(Directory.EnumerateFiles(dir, "*.lua")
					.OrderBy(f => f, StringComparer.Ordinal).Select(File.ReadAllText));
				if (lua.Length == 0)
					continue;

				yield return new Scenario
				{
					Name = Path.GetFileName(dir),
					RulesText = File.ReadAllText(rules),
					LuaText = lua,
					Rules = MiniYaml.FromFile(rules),
				};
			}
		}

		/// <summary>
		/// Power keys a scenario ORDERS — the second argument of every Test.ActivateSupportPower
		/// call, whether it is spelled as a literal or held in a local.
		///
		/// RESOLVING THE VARIABLE FORM IS NOT OPTIONAL, AND IT HAS TO BE DONE FROM THE CALL SITE
		/// BACKWARDS rather than from the declaration forwards. test-power-aimpoint-center and its
		/// siblings hold the key in `OrderKey` rather than spelling it at the call, so a
		/// literals-only walk misses them entirely. But test-power-buy-loop ALSO declares
		/// `local ControlKey = "KinzhalStrike"` and never fires it — it reads that power's state as
		/// a control and nothing more. The first version of this fixture took every
		/// `local …Key… = "&lt;a real power&gt;"` as evidence of a shot and reported that scenario as
		/// firing a Kinzhal it does not fire. So: collect the identifiers actually PASSED to
		/// ActivateSupportPower, and resolve only those.
		/// </summary>
		static readonly Regex ActivateLiteral =
			new Regex("ActivateSupportPower\\s*\\(\\s*[A-Za-z_][\\w.]*\\s*,\\s*\"([^\"]+)\"");

		static readonly Regex ActivateVariable =
			new Regex("ActivateSupportPower\\s*\\(\\s*[A-Za-z_][\\w.]*\\s*,\\s*([A-Za-z_]\\w*)\\s*,");

		/// <summary>
		/// The power key handed to TestHarness.EnsurePower(player, proxy, powerKey, tick). Counted
		/// alongside the ordered ones because a scenario can legitimately BUY a power it never
		/// fires: test-tacnuke-lobby-gated-off buys a Kinzhal purely as a positive control, and that
		/// purchase needs money and a real proxy exactly as a fired one does.
		/// </summary>
		static readonly Regex EnsureVariable =
			new Regex("EnsurePower\\s*\\(\\s*[A-Za-z_][\\w.]*\\s*,\\s*[A-Za-z_]\\w*\\s*,\\s*([A-Za-z_]\\w*)\\s*,");

		static readonly Regex ProxyLiteral = new Regex("\"(power\\.[a-z0-9]+)\"");

		static string[] FiredPowers(Scenario s, Dictionary<string, Power> mod)
		{
			var keys = new HashSet<string>();
			foreach (Match m in ActivateLiteral.Matches(s.LuaText))
				keys.Add(m.Groups[1].Value);

			foreach (Match m in ActivateVariable.Matches(s.LuaText).Cast<Match>().Concat(EnsureVariable.Matches(s.LuaText).Cast<Match>()))
			{
				// `local <name> = "<power>"`. A key ASSEMBLED AT RUNTIME resolves to nothing and is
				// skipped — test-power-buy-loop matches `BoughtStrike_<ActorID>` out of the bin
				// listing, because AllowMultiple makes the ActorID unknowable in advance. That is a
				// real blind spot and is named in this file's scope note.
				var decl = new Regex("local\\s+" + Regex.Escape(m.Groups[1].Value) + "\\s*=\\s*\"([^\"]+)\"");
				var d = decl.Match(s.LuaText);
				if (d.Success)
					keys.Add(d.Groups[1].Value);
			}

			foreach (var deliberate in DeliberateRefusals(s.Name))
				keys.Remove(deliberate);

			return keys.Where(mod.ContainsKey).ToArray();
		}

		/// <summary>
		/// Powers a scenario fires ON PURPOSE EXPECTING TO BE REFUSED, so the checks below must not
		/// demand that it be able to obtain them.
		///
		/// THIS IS ONE NAME AND IT SHOULD STAY THAT SHORT. It is not an exemption from the rule, it
		/// is the statement that the scenario's SUBJECT IS the refusal: test-tacnuke-lobby-gated-off
		/// issues the nuke order specifically to prove that a power the host did not enable cannot
		/// be fired past the UI either (its check 3), and handing it the money and the sandbox
		/// switch would turn that assertion into a tautology. Its own rules.yaml says the same thing
		/// from the other side, and its Kinzhal CONTROL is still held to every rule here.
		///
		/// Anything added to this list needs the same argument written beside it. "The purchase was
		/// awkward to set up" is not that argument.
		/// </summary>
		static string[] DeliberateRefusals(string scenario)
		{
			if (scenario == "test-tacnuke-lobby-gated-off")
				return new[] { "TacNukeStrike" };

			return Array.Empty<string>();
		}

		/// <summary>The scenario's own override of a mod power trait, if it has one.</summary>
		static MiniYamlNode ScenarioTrait(Scenario s, string traitKey)
		{
			var player = s.Rules.FirstOrDefault(n => n.Key == "Player");
			return player?.Value.Nodes.FirstOrDefault(n => n.Key == traitKey);
		}

		/// <summary>True when the scenario has explicitly opted this power out of the bank.</summary>
		static bool OptedOut(Scenario s, string traitKey)
		{
			var over = traitKey == null ? null : ScenarioTrait(s, traitKey);
			return over != null && string.Equals(Field(over, "RequiresPurchase"), "False", StringComparison.OrdinalIgnoreCase);
		}

		static int DefaultCash(Scenario s)
		{
			var player = s.Rules.FirstOrDefault(n => n.Key == "Player");
			var res = player?.Value.Nodes.FirstOrDefault(n => n.Key.Split('@')[0] == "PlayerResources");
			var cash = res == null ? null : Field(res, "DefaultCash");
			return cash != null && int.TryParse(cash, out var v) ? v : -1;
		}

		[Test]
		public void TheFixtureFindsSomethingToCheck()
		{
			var mod = ModPowers();
			Assert.That(mod.Count, Is.GreaterThanOrEqualTo(10),
				"fewer than ten support powers were parsed out of rules/player.yaml and " +
				"rules/ingame/nuclear-arsenal.yaml, so the walk is broken rather than the mod");

			Assert.That(mod.Values.Count(p => p.RequiresPurchase), Is.GreaterThanOrEqualTo(10),
				"fewer than ten powers were read as RequiresPurchase, so the flag is not being " +
				"parsed and every check below would pass vacuously");

			var firing = Scenarios().Count(s => FiredPowers(s, mod).Length > 0);
			Assert.That(firing, Is.GreaterThanOrEqualTo(10),
				"fewer than ten autotest scenarios were found firing a support power. Either the " +
				"scenarios moved or the ActivateSupportPower regex stopped matching — and a " +
				"fixture that matches nothing passes everything.");
		}

		/// <summary>
		/// THE BAN ON THE INERT PAIR. This is the exact shape the 2026-09-06 breakage wore, in all
		/// thirteen scenarios, and it is worth failing on by itself rather than only through its
		/// consequences: a scenario author who writes it has a wrong model of how the power works,
		/// and the next thing they write from that model will be wrong too.
		/// </summary>
		[Test]
		public void NoScenarioStagesAPurchasedPowerWithAChargeTimer()
		{
			var mod = ModPowers();
			var traitKeys = ModTraitKeys();
			var offenders = new List<string>();

			foreach (var s in Scenarios())
			{
				var player = s.Rules.FirstOrDefault(n => n.Key == "Player");
				if (player == null)
					continue;

				foreach (var trait in player.Value.Nodes)
				{
					if (!trait.Key.Split('@')[0].EndsWith("Power", StringComparison.Ordinal))
						continue;

					if (Field(trait, "ChargeInterval") == null && Field(trait, "StartFullyCharged") == null)
						continue;

					// An explicit opt-out is allowed and is the documented escape hatch: it turns
					// the bank off, at which point the two fields are live again and mean what they
					// say. demo-subtick-smoothing is the one scenario that uses it, because its
					// control arm is a scenario-defined timer power with no proxy to buy.
					if (string.Equals(Field(trait, "RequiresPurchase"), "False", StringComparison.OrdinalIgnoreCase))
						continue;

					// Resolve the scenario's trait key back to the mod's power. A scenario that
					// DEFINES its own power (its own OrderName, not one the mod knows) is genuinely
					// timer-charged, because RequiresPurchase defaults to false.
					var order = traitKeys.FirstOrDefault(kv => kv.Value == trait.Key).Key;
					if (order == null || !mod.TryGetValue(order, out var power) || !power.RequiresPurchase)
						continue;

					offenders.Add(s.Name + ": `" + trait.Key + "` sets ChargeInterval/StartFullyCharged on " +
						order + ", which carries RequiresPurchase: True");
				}
			}

			Assert.That(offenders, Is.Empty,
				"a scenario is staging a PURCHASED support power with a CHARGE TIMER, and both " +
				"fields are inert: SupportPowerInstance's constructor forces TotalTicks and " +
				"remainingSubTicks to 0 whenever RequiresPurchase is set " +
				"(SupportPowerManager.cs:228-229). The power sits at zero charges, which is " +
				"Disabled, which is not Ready, so every Test.ActivateSupportPower call returns " +
				"'not-ready:0' for the whole run — silently, with no lint error and no Lua fault. " +
				"BUY the shot instead (TestHarness.EnsurePower, and see any of the nuke " +
				"scenarios' rules.yaml), or set `RequiresPurchase: False` on the trait if the " +
				"scenario genuinely needs a timer power and can justify it in a comment.\n  " +
				string.Join("\n  ", offenders));
		}

		/// <summary>
		/// A scenario that FIRES a purchased power must be able to BUY one: it must name the power's
		/// proxy actor, and it must have money. Both are silent when missing — a proxy that is never
		/// named simply never enters the queue, and DefaultCash 0 makes the purchase stall forever
		/// with no message at all.
		/// </summary>
		[Test]
		public void EveryScenarioThatFiresABoughtPowerCanBuyOne()
		{
			var mod = ModPowers();
			var traitKeys = ModTraitKeys();
			var offenders = new List<string>();

			foreach (var s in Scenarios())
			{
				foreach (var key in FiredPowers(s, mod))
				{
					if (!mod.TryGetValue(key, out var power) || !power.RequiresPurchase)
						continue;

					if (OptedOut(s, traitKeys.TryGetValue(power.Order, out var tk) ? tk : null))
						continue;

					if (power.Proxy == null)
					{
						offenders.Add(s.Name + ": fires " + key + ", which is purchased and has NO proxy in rules/powers.yaml");
						continue;
					}

					if (!s.RulesText.Contains(power.Proxy, StringComparison.Ordinal)
						&& !s.LuaText.Contains(power.Proxy, StringComparison.Ordinal))
						offenders.Add(s.Name + ": fires " + key + " but never names its proxy `" + power.Proxy + "`");

					if (DefaultCash(s) == 0)
						offenders.Add(s.Name + ": fires " + key + ", which must be bought, with DefaultCash 0");
				}
			}

			Assert.That(offenders, Is.Empty,
				"a scenario fires a support power it has no way to obtain. Every shipped power " +
				"carries RequiresPurchase: True, and GrantCharge has exactly one caller in the " +
				"engine (SupportPowerProductionQueue.BuildUnit), so the ONLY way to load a shot is " +
				"to buy the proxy actor named by ProvidesSupportPowerCharge. The scenario needs " +
				"cash in PlayerResources.DefaultCash, a call to TestHarness.EnsurePower naming the " +
				"proxy, and normally a short BuildDuration override on it.\n  " +
				string.Join("\n  ", offenders));
		}

		/// <summary>
		/// THE TIER GATE, and it is the half that is easiest to forget because it is not in the
		/// scenario's own subject. `powers.event` is provided by NO faction — only by the three
		/// unfiltered ProvidesPrerequisite traits in rules/player.yaml gated on
		/// `!powers-sandbox-disabled`. A scenario firing an event-tier warhead without ticking the
		/// sandbox lobby option gets a power that is not Permitted, which is absent from the buy tab
		/// AND from the support bin, and the purchase is refused with no message.
		/// </summary>
		[Test]
		public void EveryScenarioFiringAnEventTierPowerTicksTheSandboxOption()
		{
			var mod = ModPowers();
			var traitKeys = ModTraitKeys();
			var offenders = new List<string>();

			foreach (var s in Scenarios())
			{
				foreach (var key in FiredPowers(s, mod))
				{
					if (!mod.TryGetValue(key, out var power) || !power.RequiresPurchase)
						continue;

					if (power.Tier == null || !power.Tier.Contains("powers.event", StringComparison.Ordinal))
						continue;

					if (OptedOut(s, traitKeys.TryGetValue(power.Order, out var tk) ? tk : null))
						continue;

					var world = s.Rules.FirstOrDefault(n => n.Key == "World");
					var lobby = world?.Value.Nodes.FirstOrDefault(n => n.Key.Split('@')[0] == "PowersLobbyOptions");
					var sandbox = lobby == null ? null : Field(lobby, "PowersSandboxCheckboxEnabled");

					if (!string.Equals(sandbox, "true", StringComparison.OrdinalIgnoreCase))
						offenders.Add(s.Name + ": fires " + key + " (tier " + power.Tier + ") without PowersSandboxCheckboxEnabled");
				}
			}

			Assert.That(offenders, Is.Empty,
				"a scenario fires an EVENT-TIER support power without the sandbox lobby option. " +
				"`powers.event` is provided by no faction, ever — only by the sandbox switch — so " +
				"the power is not Permitted, is absent from the buy tab, and the purchase is " +
				"refused. Add to the scenario's World block:\n" +
				"    PowersLobbyOptions:\n" +
				"        PowersSandboxCheckboxEnabled: true\n" +
				"        PowersSandboxCheckboxLocked: true\n  " +
				string.Join("\n  ", offenders));
		}

		/// <summary>
		/// A scenario that names a proxy must name one that EXISTS. A typo here is the quietest
		/// fault in the set: ClassicProductionQueueProperties.Build throws a LuaException for an
		/// actor with no BuildableInfo, which aborts the script on load and writes the same
		/// `status: fail` a deliberately-red arm writes.
		/// </summary>
		[Test]
		public void EveryProxyNamedByAScenarioExists()
		{
			var known = new HashSet<string>(ModPowers().Values.Where(p => p.Proxy != null).Select(p => p.Proxy));
			var offenders = new List<string>();

			foreach (var s in Scenarios())
				foreach (Match m in ProxyLiteral.Matches(s.LuaText))
					if (!known.Contains(m.Groups[1].Value))
						offenders.Add(s.Name + ": names proxy `" + m.Groups[1].Value + "`, which rules/powers.yaml does not sell");

			Assert.That(offenders, Is.Empty,
				"a scenario's Lua names a `power.*` proxy actor that rules/powers.yaml does not " +
				"define, or that defines no ProvidesSupportPowerCharge. Player.Build throws a " +
				"LuaException for an actor with no Buildable, which aborts the scenario on load.\n  " +
				string.Join("\n  ", offenders));
		}
	}
}
