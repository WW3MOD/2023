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
 * A THIRD ROUTE EXISTS SINCE 2026-09-13 and both purchase tests now know about it: the DOOMSDAY
 * FINAL EXCHANGE arms every surviving side's game-enders for fifteen seconds, opening all three
 * gates at once (tier, condition, magazine). See ArmedByTheFinalExchange for what that exemption
 * does and does not check.
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
using OpenRA.Mods.Common.Traits;

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

			/// <summary>Stated yield in tons of TNT, 0 for a conventional power. Read by BOTH
			/// exemptions: <see cref="ArmedByTheFinalExchange"/> tests it for the game-ender band,
			/// <see cref="LoadedByTheExchange"/> only for being nuclear at all.</summary>
			public int NuclearYieldTons;
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
							NuclearYieldTons = int.TryParse(Field(trait, "NuclearYieldTons"), out var tons) ? tons : 0,
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
			foreach (var m in ActivateLiteral.Matches(s.LuaText).Cast<Match>())
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
		/// <summary>
		/// <para>THE THIRD ROUTE TO A LOADED GAME-ENDER, added 2026-09-13 with the DOOMSDAY final
		/// exchange. This fixture was written when there were exactly two — tick the sandbox option, or
		/// buy the proxy — and both of the tests below refuse a scenario that does neither. The
		/// exchange is a third: when the window opens, <see cref="DoomsdayStrike"/> grants the
		/// game-ender condition, overrides the event tier and banks a shot on every surviving side
		/// (<see cref="SupportPowerInstance.MakeReady"/>), so a scenario firing a game-ender inside
		/// those fifteen seconds needs no sandbox switch, no proxy and no cash.</para>
		///
		/// <para>IT ASKS THE LADDER, NOT A STRING. The band comes from
		/// <see cref="NuclearReleaseLadder.RungForYield"/> — the same call the runtime's own
		/// IsGameEnder makes — rather than from matching `nuclear-release-gameender` in the YAML, so
		/// this predicate cannot drift away from the one that decides what actually gets armed. The
		/// Tsar Bomba is excluded by the ladder's SandboxOnlyAboveTons for the same reason it is
		/// excluded at runtime: decision 04 keeps it off the shop floor by a route no schedule reaches.</para>
		///
		/// <para>WHAT IT CANNOT SEE, stated rather than hidden: WHEN the scenario fires. A scenario that
		/// configures the ending and then fires its game-ender at tick 30 — long before the window
		/// opens — is exempted here and will still get 'hidden' at runtime. Closing that would mean
		/// statically evaluating Trigger.AfterDelay arithmetic, which is well past what this fixture
		/// claims in its header. The demo's own on-screen status line is the backstop for it.</para>
		/// </summary>
		static bool ArmedByTheFinalExchange(Scenario s, Power power)
		{
			if (!IsGameEnderYield(power))
				return false;

			var world = s.Rules.FirstOrDefault(n => n.Key == "World");
			var doomsday = world?.Value.Nodes.FirstOrDefault(n => n.Key.Split('@')[0] == "DoomsdayStrike");
			if (doomsday == null)
				return false;

			// A demo or test runs under TestMode, where the salvo is OFF unless the scenario opts in.
			// Without this line the ending never triggers, so nothing is ever armed.
			if (!string.Equals(Field(doomsday, "RunInTestMode"), "True", StringComparison.OrdinalIgnoreCase))
				return false;

			// Absent means the shipped 250. Zero is the documented escape hatch that skips the window
			// entirely and fires the salvo on the trigger tick, arming nobody.
			var ticks = Field(doomsday, "FinalExchangeWindowTicks");
			return ticks == null || (int.TryParse(ticks, out var parsed) && parsed > 0);
		}

		/// <summary>
		/// <para>Is this power one of the weapons the two exchange paths hand out? The yield test, held
		/// in one place because BOTH of the exemptions below need it and a second copy would be the
		/// second silent copy of the band table this fixture's header warns about.</para>
		///
		/// <para>It mirrors <see cref="NuclearGameEnders.Is"/> rather than calling it, and that is
		/// deliberate: this fixture reads YAML into its own <c>Power</c> record and never constructs a
		/// TraitInfo, so calling the runtime predicate would mean building one. What matters is that
		/// both ask <see cref="NuclearReleaseLadder"/> — the band table and SandboxOnlyAboveTons — so
		/// neither can drift from the ladder even though they are two expressions of it.</para>
		/// </summary>
		static bool IsGameEnderYield(Power power)
		{
			var tons = power.NuclearYieldTons;
			if (tons <= 0 || tons > NuclearReleaseLadder.SandboxOnlyAboveTons)
				return false;

			return NuclearReleaseLadder.RungForYield(tons) == (int)NuclearRung.GameEnder;
		}

		/// <summary>
		/// <para>THE FOURTH ROUTE TO A LOADED GAME-ENDER, and the one this fixture was actively wrong
		/// about until 2026-09-14. The tier test below asserted, in so many words, that an event-tier
		/// power is unreachable without the sandbox switch — and for the top rung that had been a
		/// statement about a BUG rather than about the design. A retaliation window at
		/// <see cref="NuclearRung.GameEnder"/> is decision 01's only route to a game-ender in a normal
		/// match ("game-enders exist only as a retaliation grant"), and it granted the player nothing:
		/// <see cref="NuclearExchange"/> gated its grant on <see cref="SupportPowerInstance.Permitted"/>,
		/// which ANDs the `powers.event` the shipped enders declare. A user reported the empty column.
		/// The window now overrides that tier exactly as the final exchange does, so an Escalation
		/// scenario firing a game-ender needs no sandbox switch.</para>
		///
		/// <para>NARROWER THAN <see cref="LoadedByTheExchange"/>, WHICH IS THE POINT. That one exempts
		/// ANY band from the PURCHASE requirement, because every band is loaded by a window. This one
		/// exempts only the TOP band from the TIER requirement, because only the top band's powers are
		/// on the event tier at all — and a lower-band event-tier power (`TacNukeStrike`) is
		/// deliberately NOT reachable this way. The two must not be merged.</para>
		///
		/// <para>IT CANNOT SEE THE FACTION, and says so rather than pretending otherwise: whether the
		/// firing player owns the warhead is a runtime question about who holds `player.russia`, and
		/// <see cref="NuclearGameEnders.OwnedByFaction"/> is what answers it. Nor can it see WHEN the
		/// scenario fires — the same limit <see cref="ArmedByTheFinalExchange"/> records.</para>
		/// </summary>
		static bool ArmedByTheEndWindow(Scenario s, Power power)
		{
			return IsGameEnderYield(power) && LoadedByTheExchange(s, power);
		}

		static bool OptedOut(Scenario s, string traitKey)
		{
			var over = traitKey == null ? null : ScenarioTrait(s, traitKey);
			return over != null && string.Equals(Field(over, "RequiresPurchase"), "False", StringComparison.OrdinalIgnoreCase);
		}

		/// <summary>
		/// <para>Is this scenario's shot loaded by the DEFCON Escalation nuclear exchange rather than
		/// bought? If so it needs neither a proxy nor cash, and DefaultCash 0 is LOAD-BEARING rather
		/// than a bug — it is what makes a 'ready' reading mean the exchange loaded the warhead.</para>
		///
		/// <para>ADDED 2026-09-13 WITH A SECOND CALLER OF THE BANK. Until then
		/// SupportPowerProductionQueue.BuildUnit was the only thing in the engine that could load a
		/// purchased power, which is what the sibling assertion below asserts in so many words.
		/// <see cref="NuclearExchange"/> now loads the retaliation window's tier through
		/// SupportPowerInstance.MakeReady, because the ruling requires that tier to be ready the
		/// INSTANT the window opens and a purchase is a shop visit and a bill.</para>
		///
		/// <para>DELIBERATELY NARROW, on both axes. The scenario must run in
		/// <see cref="DefconGameMode.Escalation"/> — the exchange is a strict no-op in Skirmish and
		/// Sandbox, where a scenario firing a bought power really must buy it — AND the power must
		/// carry a yield, because the exchange only ever loads bands and never a conventional
		/// power.</para>
		/// </summary>
		static bool LoadedByTheExchange(Scenario s, Power power)
		{
			if (power.NuclearYieldTons <= 0)
				return false;

			var world = s.Rules.FirstOrDefault(n => n.Key == "World");
			var escalation = world?.Value.Nodes.FirstOrDefault(n => n.Key.Split('@')[0] == "DefconEscalation");
			return escalation != null
				&& string.Equals(Field(escalation, "ModeDefault"), nameof(DefconGameMode.Escalation), StringComparison.OrdinalIgnoreCase);
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

					// TWO EXEMPTIONS, AND THEY ARE NOT THE SAME ONE. Both landed on 2026-09-13 from
					// different branches and both are needed: ArmedByTheFinalExchange covers a
					// GAME-ENDER handed out by DoomsdayStrike's ending, LoadedByTheExchange covers
					// ANY band handed out by a retaliation window in an Escalation match. A scenario
					// can hit either without hitting the other.
					if (ArmedByTheFinalExchange(s, power) || LoadedByTheExchange(s, power))
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
				"carries RequiresPurchase: True, and outside DEFCON Escalation the ONLY way to load " +
				"a shot is to buy the proxy actor named by ProvidesSupportPowerCharge " +
				"(SupportPowerProductionQueue.BuildUnit). The scenario needs cash in " +
				"PlayerResources.DefaultCash, a call to TestHarness.EnsurePower naming the proxy, " +
				"and normally a short BuildDuration override on it.\n  " +
				"TWO EXCEPTIONS, and they are different ones. A GAME-ENDER in a scenario that runs " +
				"DoomsdayStrike's ending is armed by that ending (ArmedByTheFinalExchange). And ANY " +
				"nuclear power in a scenario that sets `DefconEscalation: ModeDefault: Escalation` may " +
				"be loaded by a retaliation window, which calls SupportPowerInstance.MakeReady itself " +
				"(LoadedByTheExchange) -- such a scenario needs neither proxy nor cash.\n  " +
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

					// TWO ROUTES AROUND THE EVENT TIER, and they are different mechanisms with the
					// same licence: DoomsdayStrike's ending arms every surviving side, and a
					// retaliation window at the top rung arms the side that was hit by 100 kt. Both
					// call SupportPowerInstance.MakeReady, which overrides `powers.event` and nothing
					// else -- see NuclearGameEnders.
					if (ArmedByTheFinalExchange(s, power) || ArmedByTheEndWindow(s, power))
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
				"TWO EXCEPTIONS, both for GAME-ENDERS only. A scenario running DoomsdayStrike's " +
				"ending (ArmedByTheFinalExchange), and an Escalation scenario whose retaliation " +
				"window reaches NuclearRung.GameEnder (ArmedByTheEndWindow) -- both override the " +
				"event tier, and neither overrides the faction beside it.\n  " +
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
				foreach (var m in ProxyLiteral.Matches(s.LuaText).Cast<Match>())
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
