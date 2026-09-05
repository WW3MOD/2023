#region Copyright & License Information
/*
 * WW3MOD powers economy — the claims that are cheap to check statically and expensive to discover
 * in a game.
 *
 * The three missile strikes stopped being free abilities on the Player actor and became purchases:
 * one bodiless proxy each, bought from a new `Powers` production queue at the Supply Route, costing
 * upkeep for as long as they are held and refundable in full until they are fired. Most of that is
 * only observable in a running game and is covered by three autotest scenarios. What is pinned here
 * is the part that is arithmetic or YAML — and, in one case, the part whose failure mode is a slow
 * invisible drain rather than anything a run would report.
 *
 * THE BOT TEST IS THE ONE THAT MATTERS MOST. Nothing in the game would say "the bot bought a strike
 * it will never fire and has been paying for it since minute four". It would show up as the bot
 * being poor, which reads as a balance problem, in a mod whose bots are being tuned continuously.
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
	public class PowersEconomyTest
	{
		const string QueueType = "Powers";

		static readonly string[] Proxies = { "powerproxy.kinzhal", "powerproxy.gbu57", "powerproxy.tacnuke" };

		static string FindRepoRoot()
		{
			var dir = new DirectoryInfo(AppContext.BaseDirectory);
			while (dir != null)
			{
				if (Directory.Exists(Path.Combine(dir.FullName, "mods", "ww3mod", "rules"))
					&& Directory.Exists(Path.Combine(dir.FullName, "tools", "autotest", "scenarios")))
					return dir.FullName;

				dir = dir.Parent;
			}

			throw new DirectoryNotFoundException("could not locate the repository root");
		}

		static string RulesPath(params string[] parts)
		{
			return Path.Combine(new[] { FindRepoRoot(), "mods", "ww3mod", "rules" }.Concat(parts).ToArray());
		}

		/// <summary>
		/// Flat read of one trait block's child keys inside one top-level actor block. Deliberately not
		/// a MiniYaml parser, for the same reason MissileStrikeArrivalTest and SupportPowerAimPointTest
		/// take the same shortcut: pulling the real ruleset in would drag the whole mod load into a
		/// unit test. Asserts the top-level exists, so a renamed actor fails loudly rather than
		/// returning an empty dictionary that makes every ContainsKey assertion below pass vacuously.
		/// </summary>
		static Dictionary<string, string> ReadTrait(string path, string topLevel, string trait)
		{
			var fields = new Dictionary<string, string>();
			var inTop = false;
			var inTrait = false;
			var seenTop = false;

			foreach (var raw in File.ReadLines(path))
			{
				var line = raw.Split('#')[0].TrimEnd();
				if (line.Trim().Length == 0)
					continue;

				var indent = line.TakeWhile(c => c == '\t').Count();
				var body = line.Trim();

				if (indent == 0)
				{
					if (inTop)
						break;

					inTop = body == topLevel + ":";
					seenTop |= inTop;
					inTrait = false;
				}
				else if (indent == 1 && inTop)
					inTrait = body == trait + ":";
				else if (indent >= 2 && inTrait && body.Contains(':'))
				{
					var parts = body.Split(new[] { ':' }, 2);
					fields[parts[0].Trim()] = parts[1].Trim();
				}
			}

			Assert.That(seenTop, Is.True, $"`{topLevel}` not found in {Path.GetFileName(path)}");
			return fields;
		}

		// --- (1) the bots ---------------------------------------------------------------------

		[Test]
		public void PowersQueueIsInvisibleToEveryBotModule()
		{
			// THE FAILURE THIS PREVENTS IS SILENT AND SLOW. A bot that could queue a power would buy
			// one, never fire it (no SupportPowerBotModule is configured in this mod, so bots do not
			// use support powers at all), and pay its upkeep for the rest of the match. There is no
			// log line for that and no scenario that would catch it; it looks like the bot being bad
			// at economy.
			//
			// Exclusion is STRUCTURAL rather than declared: bot modules opt IN to queue types by name,
			// and `Powers` is in none of the defaults and none of ai.yaml's explicit lists. This test
			// is what turns "nobody added it" into "adding it is a red test".
			var baseBuilder = new BaseBuilderBotModuleInfo();
			var unitBuilder = new UnitBuilderBotModuleInfo();

			Assert.That(baseBuilder.BuildingQueues, Does.Not.Contain(QueueType));
			Assert.That(baseBuilder.DefenseQueues, Does.Not.Contain(QueueType));
			Assert.That(unitBuilder.UnitQueues, Does.Not.Contain(QueueType),
				"UnitBuilderBotModule's default queue set is where a new queue type would most " +
				"plausibly be added by accident");

			// And the explicit lists, which override those defaults wherever ai.yaml sets one.
			var ai = File.ReadAllLines(RulesPath("ai", "ai.yaml"))
				.Select(l => l.Split('#')[0].Trim())
				.Where(l => Regex.IsMatch(l, @"^(Building|Defense|Unit)Queues:"))
				.ToArray();

			Assert.That(ai, Is.Not.Empty,
				"found no *Queues: lines in ai.yaml at all -- the scan is broken, not the config, " +
				"and this test would otherwise pass while checking nothing");

			foreach (var line in ai)
			{
				var named = line.Split(new[] { ':' }, 2)[1].Split(',').Select(s => s.Trim());
				Assert.That(named, Does.Not.Contain(QueueType),
					$"ai.yaml line `{line}` hands the Powers queue to a bot. A bot that buys a " +
					"strike it will never fire pays upkeep on it forever, and the symptom is a poor " +
					"bot rather than anything that names this cause. If bots are ever meant to use " +
					"powers they need a SupportPowerBotModule FIRST, so that what they buy gets used.");
			}
		}

		[Test]
		public void NoBotFiresSupportPowersToday()
		{
			// The premise the test above rests on, stated separately so that adding a
			// SupportPowerBotModule fails here rather than quietly invalidating the argument.
			var ai = File.ReadAllText(RulesPath("ai", "ai.yaml"));
			Assert.That(ai, Does.Not.Contain("SupportPowerBotModule"),
				"bots gained the ability to FIRE support powers. That is not a problem in itself, " +
				"but it removes the reason powers were kept off the bot queues -- revisit " +
				"PowersQueueIsInvisibleToEveryBotModule rather than deleting it.");
		}

		// --- (2) the upkeep arithmetic --------------------------------------------------------

		[TestCase(4000, 20)]
		[TestCase(15000, 75)]
		public void UpkeepIsHalfAPercentOfThePricePerInterval(int price, int expected)
		{
			// PermilleCost 5 is the same rate every infantryman and vehicle in the mod already pays,
			// which is the whole argument for it: a reserved strike costs what a parked tank costs.
			Assert.That(InfersUpkeepInfo.UpkeepPerInterval(price, 0, 5), Is.EqualTo(expected));
		}

		[Test]
		public void EveryPowerUpkeepClearsTheDisplayFloor()
		{
			// IngameCashCounterLogic sums each group and skips it when the total casts to zero, which
			// is why a 50-credit rifleman at 0.25/interval never appears in the breakdown. A power
			// billing invisibly would be strictly worse than one billing visibly: the player would
			// watch their income fall with no line item to blame.
			foreach (var proxy in Proxies)
			{
				var cost = int.Parse(ReadTrait(RulesPath("player.yaml"), proxy, "Valued")["Cost"]);
				var permille = int.Parse(ReadTrait(RulesPath("player.yaml"), proxy, "InfersUpkeep")["PermilleCost"]);
				var upkeep = InfersUpkeepInfo.UpkeepPerInterval(cost, 0, permille);

				Assert.That(upkeep, Is.GreaterThanOrEqualTo(1f),
					$"{proxy} bills {upkeep}/interval, which casts to {(int)upkeep} in the cash " +
					"tooltip's breakdown and is therefore invisible to the player paying it");
			}
		}

		// --- (3) the proxy shape --------------------------------------------------------------

		[Test]
		public void EveryProxyIsBoughtBankedAndReclaimable()
		{
			// The five fields that make a purchase a RESERVATION rather than an unlock. Each is
			// separately load-bearing and each defaults to the wrong thing for this use, because each
			// default is the right thing for a power on the Player actor.
			foreach (var proxy in Proxies)
			{
				var trait = ReadTrait(RulesPath("player.yaml"), proxy, "MissileStrikePower@"
					+ (proxy.EndsWith("kinzhal", StringComparison.Ordinal) ? "Kinzhal"
						: proxy.EndsWith("gbu57", StringComparison.Ordinal) ? "GBU57" : "TacNuke"));

				Assert.That(trait.ContainsKey("ChargeInterval"), Is.False,
					$"{proxy} still carries a ChargeInterval. Paying for a power and THEN waiting " +
					"for it to charge is the double gate this whole feature exists to remove.");
				Assert.That(trait.GetValueOrDefault("StartFullyCharged"), Is.EqualTo("True"),
					$"{proxy} must arrive ready; the purchase is the wait");
				Assert.That(trait.GetValueOrDefault("OneShot"), Is.EqualTo("True"),
					$"{proxy} must be consumed by firing, or one purchase is an unlimited ability");
				Assert.That(trait.GetValueOrDefault("AllowMultiple"), Is.EqualTo("True"),
					$"{proxy} must key per ActorID, or a second purchase silently merges into the first");
				Assert.That(trait.GetValueOrDefault("DisposeSelfOnActivate"), Is.EqualTo("True"),
					$"{proxy} must remove itself when fired. InfersUpkeep unregisters only on " +
					"INotifyRemovedFromWorld, so without this a spent strike bills the player for " +
					"the rest of the match -- invisibly, and with no event to trace it to.");
				Assert.That(trait.GetValueOrDefault("Releasable"), Is.EqualTo("True"),
					$"{proxy} must be reclaimable, or a mis-click costs its whole price");
				Assert.That(trait.GetValueOrDefault("PauseOnCondition"), Is.EqualTo("upkeep-unpaid"),
					$"{proxy} must go dormant rather than being destroyed when its upkeep goes unpaid");
			}
		}

		[Test]
		public void TheDefaultsForTheNewFieldsAreTheOldBehaviour()
		{
			// The rule for a behavioural field on a shared trait: default to baseline, so nothing that
			// does not opt in changes. SupportPowerInfo is on every power in the mod, including mslo's
			// NukePower and anything a map adds.
			var info = new MissileStrikePowerInfo();

			Assert.That(info.DisposeSelfOnActivate, Is.False,
				"a shipped power that did not ask to be disposed must not start disposing its own " +
				"actor -- on a power carried by a BUILDING that would delete the building");
			Assert.That(info.Releasable, Is.False,
				"releasing refunds Valued.Cost and disposes the carrier. Defaulting this true would " +
				"make every power in the mod right-click-refundable, including ones carried by the " +
				"Player actor itself.");
		}

		[Test]
		public void TheProxiesAreBoughtFromTheSupplyRoute()
		{
			var production = ReadTrait(RulesPath("ingame", "structures.yaml"), "SUPPLYROUTE", "Production@Local");
			var produces = production["Produces"].Split(',').Select(s => s.Trim()).ToArray();

			Assert.That(produces, Does.Contain(QueueType),
				"the Supply Route is the only production actor a player has, so a Powers queue it " +
				"does not produce is a queue that never ticks: ClassicProductionQueue.Tick sets " +
				"Enabled false when no owned Production trait lists the type, and Enabled false " +
				"CLEARS THE QUEUE (refunding as it goes). The tab would grey out and purchases " +
				"would silently vanish rather than erroring.");

			foreach (var proxy in Proxies)
			{
				var buildable = ReadTrait(RulesPath("player.yaml"), proxy, "Buildable");
				Assert.That(buildable.GetValueOrDefault("Queue"), Is.EqualTo(QueueType), proxy);
				Assert.That(buildable.ContainsKey("BuildDuration"), Is.True,
					$"{proxy} needs an explicit BuildDuration: it is the ChargeInterval the power " +
					"gave up, and leaving it cost-derived would silently retune the scarcity of all " +
					"three strikes at once");
			}
		}

		[Test]
		public void ThePurchaseWaitIsTheChargeItReplaced()
		{
			// The mapping that makes this a change of CURRENCY rather than of pacing: each proxy's
			// build time is the exact ChargeInterval its power used to carry, so the relative scarcity
			// approved for the three strikes survives the move. 3000 / 3000 / 11250 encode the
			// 4000 / 4000 / 15000 price ratio.
			var expected = new Dictionary<string, int>
			{
				{ "powerproxy.kinzhal", 3000 },
				{ "powerproxy.gbu57", 3000 },
				{ "powerproxy.tacnuke", 11250 },
			};

			foreach (var kv in expected)
			{
				var buildable = ReadTrait(RulesPath("player.yaml"), kv.Key, "Buildable");
				Assert.That(int.Parse(buildable["BuildDuration"]), Is.EqualTo(kv.Value),
					$"{kv.Key}'s purchase wait moved away from the charge it replaced. That is a " +
					"balance change and may well be the right one -- but it is not a free one, and " +
					"this test is where it gets noticed.");
			}

			var nukeCost = int.Parse(ReadTrait(RulesPath("player.yaml"), "powerproxy.tacnuke", "Valued")["Cost"]);
			var kinzhalCost = int.Parse(ReadTrait(RulesPath("player.yaml"), "powerproxy.kinzhal", "Valued")["Cost"]);
			Assert.That(expected["powerproxy.tacnuke"] * kinzhalCost,
				Is.EqualTo(expected["powerproxy.kinzhal"] * nukeCost),
				"the nuke's wait and its price must stay in the same ratio to the Kinzhal's " +
				"(11250/3000 == 15000/4000 == 3.75); if one moves without the other, one of the two " +
				"axes silently stops being the scarcity knob it was tuned as");
		}

		// --- (4) the scenario budgets ---------------------------------------------------------

		static int ReadLuaConstant(string scenario, string name)
		{
			var path = Path.Combine(FindRepoRoot(), "tools", "autotest", "scenarios", scenario, scenario + ".lua");
			var match = Regex.Match(File.ReadAllText(path), @"^local\s+" + name + @"\s*=\s*(\d+)", RegexOptions.Multiline);

			Assert.That(match.Success, Is.True, $"{scenario}.lua has no `local {name} = <number>`");
			return int.Parse(match.Groups[1].Value);
		}

		[Test]
		public void ScenarioArrivalBudgetCoversTheShippedMissileDelay()
		{
			// The same gate SupportPowerAimPointTest applies to the aim-point scenarios, pointed at
			// the one powers scenario that actually fires. MissileStrikePower holds the missile OUT OF
			// THE WORLD for MissileDelay ticks and Player.GetActorsByType filters on IsInWorld, so a
			// budget written against a smaller delay does not fail slowly -- it reports a delivery
			// failure that has nothing to do with delivery, and costs a launch slot to diagnose.
			var delay = int.Parse(ReadTrait(RulesPath("player.yaml"), "powerproxy.kinzhal",
				"MissileStrikePower@Kinzhal")["MissileDelay"]);
			var budget = ReadLuaConstant("test-powers-buy-upkeep", "ArrivalBudget");

			Assert.That(budget, Is.GreaterThanOrEqualTo(delay + 60),
				$"test-powers-buy-upkeep waits {budget} ticks for a missile the shipped power holds " +
				$"out of the world for {delay}. Raise ArrivalBudget in the scenario.");
		}

		[Test]
		public void ScenarioWholeRunBudgetsCoverTheirOwnPhases()
		{
			// Each scenario's whole-run budget has to exceed the sum of the phase budgets it can
			// legitimately spend, or the run closes mid-phase and reports a timeout instead of
			// whichever phase actually stalled -- the least useful failure a launch slot can buy.
			var buy = ReadLuaConstant("test-powers-buy-upkeep", "BuyDeadline")
				+ ReadLuaConstant("test-powers-buy-upkeep", "ArrivalBudget")
				+ ReadLuaConstant("test-powers-buy-upkeep", "FlightBudget")
				+ (3 * ReadLuaConstant("test-powers-buy-upkeep", "PaydayWindow"));
			Assert.That(ReadLuaConstant("test-powers-buy-upkeep", "ObserveTicks"), Is.GreaterThan(buy));

			var release = ReadLuaConstant("test-powers-release-refund", "BuyDeadline")
				+ ReadLuaConstant("test-powers-release-refund", "ReleaseDeadline")
				+ (2 * ReadLuaConstant("test-powers-release-refund", "PaydayWindow"));
			Assert.That(ReadLuaConstant("test-powers-release-refund", "ObserveTicks"), Is.GreaterThan(release));

			var dormant = ReadLuaConstant("test-powers-dormant", "BuyDeadline")
				+ ReadLuaConstant("test-powers-dormant", "DormantDeadline")
				+ ReadLuaConstant("test-powers-dormant", "RecoverDeadline");
			Assert.That(ReadLuaConstant("test-powers-dormant", "ObserveTicks"), Is.GreaterThan(dormant));
		}

		[Test]
		public void TheDormancyScenarioRunsOnAZeroIncomeEconomy()
		{
			// Without this the scenario cannot fail: the shipped 100 per interval comfortably covers a
			// 20-per-interval reservation, no shortfall is ever recorded, and the run reports "never
			// went dormant" for a reason that has nothing to do with dormancy.
			var path = Path.Combine(FindRepoRoot(), "tools", "autotest", "scenarios",
				"test-powers-dormant", "rules.yaml");
			var resources = ReadTrait(path, "Player", "PlayerResources");

			Assert.That(resources.GetValueOrDefault("PassiveIncome"), Is.EqualTo("0"),
				"test-powers-dormant needs an income smaller than the upkeep it is starving, or the " +
				"bill is simply paid every interval and the scenario is vacuous");
		}
	}
}
