#!/usr/bin/env python3
"""
Static audit of the WW3MOD rank / veterancy system.

Resolves every actor in mod.yaml's `Rules:` list through a port of the engine's
MiniYaml loader (see miniyaml.py), then reports, per actor that can hold a rank:
its thresholds, the exact bonuses each rank grants, how it can earn XP, and
which of those bonuses are INERT because the actor lacks the trait the bonus
modifies.

Runs nothing. No build, no game launch, no `--check-yaml`. Reads YAML only.

    python tools/rank-audit/rank_audit.py --csv WORKSPACE/audit/rank-system-260919.csv
    python tools/rank-audit/rank_audit.py --groups        # grouped summary to stdout
    python tools/rank-audit/rank_audit.py --unit MEDI.america

Engine constants this tool hard-codes, with the file it read them from. Each is
a value the audit's conclusions depend on, so re-check these first if a
conclusion stops making sense:

  RANK_CONDITION        'rank-veteran'   defaults.yaml:304-308 (all four
                                         thresholds grant the SAME name, so the
                                         token stacks and consumers read == N)
  XP_ACTOR_MODIFIER     10000            GivesExperience.cs:28  (percent, = x100)
  DETECTABLE_DEFAULT    2                Detectable.cs:23       (DetectableInfo.Vision)
  VISION_LAYERS         11               MapLayers.cs:75
  CONCEALMENT_CEILING   VISION_LAYERS-2  Detectable.cs:118-125  (ClampConcealment)
  MAX_PURCHASABLE_RANK  3                RankAccumulation.cs:59
  TIMESTEP_MS           60               mod.yaml:404-406 (GameSpeeds DefaultSpeed
                                         `default`) => 16.67 ticks/second. NOT 25 tps;
                                         see CLAUDE.md on the 1.5x duration error.
  RANK_CURVE            see below        RankAccumulation.cs:281-303, all defaults
                                         because player.yaml:22 declares the trait bare
"""

import argparse
import csv
import math
import os
import sys
from collections import OrderedDict, defaultdict

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import miniyaml  # noqa: E402

RANK_CONDITION = "rank-veteran"
XP_ACTOR_MODIFIER = 10000
DETECTABLE_DEFAULT_VISION = 2
VISION_LAYERS = 11
CONCEALMENT_CEILING = VISION_LAYERS - 2
MAX_PURCHASABLE_RANK = 3
TIMESTEP_MS = 60

# RankAccumulationInfo defaults -- RankAccumulation.cs:281-303. player.yaml:22
# declares `RankAccumulation:` with no fields, so every one of these is in force.
RANK1_BASE_INTERVAL_TICKS = 2400
COST_REFERENCE_BUILD_TICKS = 100
RANK1_INTERVAL_MULTIPLIER = 2700
RANK1_MAX_INTERVAL_TICKS = 9000
HIGHER_TIER_INTERVAL_MULTIPLIER = 300

# BuildableInfo defaults -- Buildable.cs:50, :53.
BUILD_DURATION_DEFAULT = -1
BUILD_DURATION_MODIFIER_DEFAULT = 100

# The cheapest thing in the game worth XP, used to express a kill threshold as a
# KILL COUNT rather than a value. E1 (Conscript) -- infantry.yaml, Valued.Cost.
CHEAPEST_TARGET_COST = 50


def base_build_time_ticks(cost, build_duration, build_duration_modifier):
    """RankAccrual.BaseBuildTimeTicks -- RankAccumulation.cs:69-73."""
    time = cost // 10 if build_duration == -1 else build_duration
    return max(1, time * build_duration_modifier // 100)


def rank1_interval_ticks(build_time_ticks):
    """RankAccrual.Rank1IntervalTicks -- RankAccumulation.cs:90-103."""
    build = max(1, build_time_ticks)
    reference = max(1, COST_REFERENCE_BUILD_TICKS)
    compressed = math.isqrt(build * reference)
    interval = RANK1_BASE_INTERVAL_TICKS + compressed * RANK1_INTERVAL_MULTIPLIER // 100
    if RANK1_MAX_INTERVAL_TICKS > 0:
        interval = min(interval, RANK1_MAX_INTERVAL_TICKS)
    return max(1, interval)


def interval_ticks(build_time_ticks, tier):
    """RankAccrual.IntervalTicks -- RankAccumulation.cs:111-121."""
    interval = rank1_interval_ticks(build_time_ticks)
    for _ in range(1, tier):
        interval = interval * HIGHER_TIER_INTERVAL_MULTIPLIER // 100
    return max(1, interval)


def ticks_to_minutes(ticks):
    return ticks * TIMESTEP_MS / 1000.0 / 60.0

# The five bonus axes, and the trait each one needs the actor to carry in order
# to do anything at all. `None` means the axis has no precondition beyond the
# rank token itself.
AXES = OrderedDict([
    ("FirepowerMultiplier", "armament"),
    ("ReloadDelayMultiplier", "armament"),
    ("SpeedMultiplier", "locomotion"),
    ("DamageMultiplier", "health"),
    ("DetectableAddativeModifier", "detectable"),
])


def repo_root():
    here = os.path.dirname(os.path.abspath(__file__))
    return os.path.abspath(os.path.join(here, "..", ".."))


def load_weapons(root, mod="ww3mod"):
    """Merge mod.yaml's `Weapons:` list. Returns {weapon_name: resolved Node}."""
    mod_yaml = os.path.join(root, "mods", mod, "mod.yaml")
    with open(mod_yaml, encoding="utf-8") as f:
        lines = f.read().split("\n")

    entries = []
    in_weapons = False
    for line in lines:
        if line.startswith("Weapons:"):
            in_weapons = True
            continue
        if in_weapons:
            if line and not line[0].isspace():
                break
            stripped = line.strip()
            if stripped and not stripped.startswith("#"):
                entries.append(stripped)

    sources = []
    for entry in entries:
        rel = entry.split("|", 1)[1] if "|" in entry else entry
        path = os.path.join(root, "mods", mod, rel)
        with open(path, encoding="utf-8") as f:
            sources.append((rel, f.read()))
    return miniyaml.merge_files(sources)


def weapon_is_lethal(weapon, weapons):
    """
    True if this weapon can reduce an enemy's health, i.e. can produce a kill and
    therefore XP. A weapon with only Damage: 0 warheads (targeting dummies,
    jammers) or no damaging warhead at all cannot.
    """
    node = weapons.get(weapon)
    if node is None:
        return False
    for wh in node.children_prefixed("Warhead"):
        dmg = wh.child_value("Damage")
        if dmg is None:
            continue
        try:
            if int(dmg) > 0:
                return True
        except ValueError:
            continue
    return False


def int_or(value, default=None):
    try:
        return int(value)
    except (TypeError, ValueError):
        return default


class UnitFacts:
    def __init__(self, name, actor, weapons):
        self.name = name
        self.actor = actor

        valued = actor.child("Valued")
        self.cost = int_or(valued.child_value("Cost") if valued else None)

        buildable = actor.child("Buildable")
        self.prerequisites = buildable.child_value("Prerequisites", "") if buildable else ""
        self.buildable = buildable is not None
        self.queue = buildable.child_value("Queue") if buildable else None
        self.disabled = "~disabled" in self.prerequisites
        self.build_duration = int_or(
            buildable.child_value("BuildDuration") if buildable else None,
            BUILD_DURATION_DEFAULT)
        self.build_duration_modifier = int_or(
            buildable.child_value("BuildDurationModifier") if buildable else None,
            BUILD_DURATION_MODIFIER_DEFAULT)

        if "~player.america" in self.prerequisites:
            self.faction = "America"
        elif "~player.russia" in self.prerequisites:
            self.faction = "Russia"
        elif self.buildable:
            self.faction = "both/neutral"
        else:
            self.faction = "not buildable"

        tooltip = actor.child("Tooltip")
        self.display_name = (tooltip.child_value("Name") if tooltip else None) or name

        # --- the rank trait itself -------------------------------------------
        ge_nodes = actor.children_prefixed("GainsExperience")
        self.gains_experience = ge_nodes[0] if ge_nodes else None
        self.thresholds = []
        self.experience_modifier = None
        if self.gains_experience is not None:
            conds = self.gains_experience.child("Conditions")
            if conds is not None:
                for c in conds.children:
                    key = int_or(c.key)
                    if key is not None:
                        self.thresholds.append((key, c.value))
                self.thresholds.sort()
            self.experience_modifier = int_or(
                self.gains_experience.child_value("ExperienceModifier"))

        # --- XP rate modifiers ------------------------------------------------
        self.xp_modifiers = []
        for m in actor.children_prefixed("GainsExperienceMultiplier"):
            self.xp_modifiers.append((
                m.key,
                int_or(m.child_value("Modifier")),
                m.child_value("RequiresCondition"),
            ))

        self.credits_rank_on_evacuation = actor.has_trait("CreditsRankOnEvacuation")

        pwl = actor.child("ProducibleWithLevel")
        self.initial_levels = int_or(pwl.child_value("InitialLevels")) if pwl else None

        # --- rank-conditioned traits -----------------------------------------
        # Every trait node whose RequiresCondition mentions the rank token.
        self.rank_traits = defaultdict(dict)  # base trait name -> {rank: value}
        self.rank_extras = []                 # non-multiplier rank consumers
        for c in actor.children:
            rc = c.child("RequiresCondition")
            if rc is None or not rc.value or RANK_CONDITION not in rc.value:
                continue
            base = c.key.split("@", 1)[0]
            rank = self._rank_from_expression(rc.value)
            value = (c.child_value("Modifier")
                     or c.child_value("VisionModifier")
                     or c.child_value("PilotActor")
                     or c.child_value("Sequence"))
            if base in AXES:
                self.rank_traits[base][rank] = int_or(value, value)
            else:
                self.rank_extras.append((c.key, rc.value, value))

        # --- what the actor can actually consume ------------------------------
        self.armaments = [(a.key, a.child_value("Weapon"),
                           a.child_value("TargetRelationships"))
                          for a in actor.children_prefixed("Armament")]
        self.has_armament = len(self.armaments) > 0
        self.has_locomotion = actor.has_trait("Mobile") or actor.has_trait("Aircraft")
        self.has_health = actor.has_trait("Health")
        detectable = actor.child("Detectable")
        self.has_detectable = detectable is not None
        self.detectable_vision = (int_or(detectable.child_value("Vision"),
                                         DETECTABLE_DEFAULT_VISION)
                                  if detectable else None)

        # Can it kill? Only a damaging weapon aimed at something other than an
        # ally can ever produce XP (GivesExperience.cs:57-72 awards to the killer).
        self.lethal_armaments = []
        for key, weapon, relationships in self.armaments:
            if weapon is None:
                continue
            if relationships is not None and "Ally" in relationships \
                    and "Enemy" not in relationships:
                continue
            if weapon_is_lethal(weapon, weapons):
                self.lethal_armaments.append((key, weapon))
        self.can_kill = len(self.lethal_armaments) > 0

    # ---------------------------------------------------------------- accrual

    def accrual_minutes(self):
        """
        Wall-clock minutes between two grants of each purchasable tier for this
        actor type, or None when the type does not accrue at all.

        `Accrues` requires BuildableInfo AND GainsExperienceInfo
        (RankAccumulation.cs:349-357), so a non-buildable actor banks nothing.
        """
        if not self.buildable or self.cost is None:
            return None
        build = base_build_time_ticks(self.cost, self.build_duration,
                                      self.build_duration_modifier)
        return [ticks_to_minutes(interval_ticks(build, tier))
                for tier in range(1, MAX_PURCHASABLE_RANK + 1)]

    def kills_for_rank1(self):
        """
        Rank 1 needs enemy value equal to this unit's own cost. Expressed as a
        COUNT of the cheapest XP-bearing target, which is the number a player
        actually experiences -- the value threshold is cost-normalised, the kill
        count is not.
        """
        if self.cost is None:
            return None
        if self.experience_modifier is not None:
            # ExperienceModifier REPLACES cost-scaling: thresholds are absolute
            # XP, and a kill awards victim_cost * 100.
            needed_xp = self.thresholds[0][0] * self.experience_modifier if self.thresholds else 0
            per_kill_xp = CHEAPEST_TARGET_COST * (XP_ACTOR_MODIFIER // 100)
            return needed_xp / per_kill_xp if per_kill_xp else None
        return self.cost / float(CHEAPEST_TARGET_COST)

    @staticmethod
    def _rank_from_expression(expr):
        """'... rank-veteran == 3' -> 3; '>= 4' -> 4; bare '!rank-veteran' -> 0."""
        for token in ("== 1", "== 2", "== 3", "== 4", ">= 4"):
            if token in expr:
                return int(token[-1])
        if "!" + RANK_CONDITION in expr:
            return 0
        return None

    # ------------------------------------------------------------------ report

    def inert_axes(self):
        out = []
        for axis, requirement in AXES.items():
            if axis not in self.rank_traits:
                continue
            if requirement == "armament" and not self.has_armament:
                out.append((axis, "no Armament on this actor"))
            elif requirement == "locomotion" and not self.has_locomotion:
                out.append((axis, "no Mobile or Aircraft on this actor"))
            elif requirement == "health" and not self.has_health:
                out.append((axis, "no Health on this actor"))
            elif requirement == "detectable" and not self.has_detectable:
                out.append((axis, "no Detectable on this actor"))
        return out

    def concealment_headroom(self):
        """
        Ranks of the concealment axis that survive ClampConcealment from the
        actor's BASE Detectable.Vision alone, before any cover/prone/dug-in term.
        Returns (base, surviving_ranks) or None when the axis cannot apply.
        """
        if not self.has_detectable or "DetectableAddativeModifier" not in self.rank_traits:
            return None
        base = self.detectable_vision
        surviving = 0
        for rank in sorted(self.rank_traits["DetectableAddativeModifier"]):
            step = self.rank_traits["DetectableAddativeModifier"][rank]
            if not isinstance(step, int):
                continue
            if min(base + step, CONCEALMENT_CEILING) > min(base + step - 1, CONCEALMENT_CEILING):
                surviving += 1
        return base, surviving

    def concealment_best_case(self):
        """
        The concealment level this actor reaches with every POSITIVE non-rank
        modifier active and no penalty active -- i.e. stationary, not firing, in
        full cover, prone and dug in, for an actor that has those nodes.

        Returns (base, stacked_before_rank, [level after each rank step], steps_that_land).

        Mutually exclusive alternatives are collapsed by grouping the non-rank
        DetectableAddativeModifier nodes on the condition VARIABLE they read and
        taking the largest positive VisionModifier in each group: `InCover1/2/3`
        all read `object-proximity` and cannot co-apply (infantry.yaml:780-788),
        so they contribute +3 once rather than +6. Negative modifiers (firing,
        moving) are excluded because they are the player's choice to avoid, and
        the cap question is about the best case.
        """
        if not self.has_detectable or "DetectableAddativeModifier" not in self.rank_traits:
            return None

        groups = defaultdict(int)
        for c in self.actor.children_prefixed("DetectableAddativeModifier"):
            rc = c.child("RequiresCondition")
            cond = rc.value if rc is not None and rc.value else ""
            if RANK_CONDITION in cond:
                continue
            step = int_or(c.child_value("VisionModifier"), 0)
            if step <= 0:
                continue
            # Group on the condition's first token, which is the variable name.
            var = cond.split()[0].lstrip("!") if cond else c.key
            groups[var] = max(groups[var], step)

        base = self.detectable_vision
        stacked = base + sum(groups.values())
        levels = []
        landed = 0
        previous = min(stacked, CONCEALMENT_CEILING)
        for rank in sorted(self.rank_traits["DetectableAddativeModifier"]):
            step = self.rank_traits["DetectableAddativeModifier"][rank]
            if not isinstance(step, int):
                continue
            level = min(stacked + step, CONCEALMENT_CEILING)
            levels.append(level)
            if level > previous:
                landed += 1
            previous = level
        return base, stacked, levels, landed

    def earns_xp_how(self):
        """One short phrase for the CSV: how this unit can actually reach a rank."""
        parts = []
        if self.can_kill:
            weapons = ", ".join(w for _, w in self.lethal_armaments)
            parts.append("kills (%s)" % weapons)
        elif self.has_armament:
            weapons = ", ".join(w or "?" for _, w, _ in self.armaments)
            parts.append("no kill path: armament is non-lethal or ally-only (%s)" % weapons)
        else:
            parts.append("no kill path: no Armament")

        if self.buildable and not self.disabled:
            parts.append("purchase stock (accrual)")
        elif self.buildable and self.disabled:
            parts.append("accrues stock but ~disabled, unspendable")
        else:
            parts.append("not buildable: no accrual")

        if self.initial_levels:
            parts.append("ships pre-ranked at level %d" % self.initial_levels)
        if self.credits_rank_on_evacuation:
            parts.append("evacuation credits rank back")
        return "; ".join(parts)

    def bonus_signature(self):
        """Identity used to group units by identical bonus profile."""
        axes = tuple(sorted(
            (axis, tuple(sorted(ranks.items())))
            for axis, ranks in self.rank_traits.items()))
        extras = tuple(sorted(k.split("@", 1)[0] for k, _, _ in self.rank_extras))
        xp = tuple(sorted(self.xp_modifiers))
        return (tuple(self.thresholds), self.experience_modifier, axes, extras, xp,
                self.credits_rank_on_evacuation)


def collect(root):
    tree, rule_files = miniyaml.load_mod_rules(root)
    weapons = load_weapons(root)

    ranking = []
    non_ranking = []
    for name in sorted(tree):
        if name.startswith("^"):
            continue
        actor = tree[name]
        facts = UnitFacts(name, actor, weapons)
        if facts.gains_experience is not None:
            ranking.append(facts)
        else:
            non_ranking.append(facts)
    return tree, rule_files, ranking, non_ranking


def write_csv(path, ranking):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", newline="", encoding="utf-8") as f:
        w = csv.writer(f)
        w.writerow([
            "unit", "display_name", "faction", "cost", "group",
            "thresholds_xp", "thresholds_in_own_cost", "bonuses",
            "earns_xp_how", "inert_bonuses", "buildable", "prerequisites_disabled",
            "kills_for_rank1_vs_50cr_target",
            "accrual_rank1_min", "accrual_rank2_min", "accrual_rank3_min",
            "concealment_base", "concealment_rank_steps_under_cap",
        ])
        groups = group_units(ranking)
        label_of = {}
        for label, members in groups.items():
            for m in members:
                label_of[m.name] = label

        for u in ranking:
            mult = u.experience_modifier if u.experience_modifier is not None else u.cost
            thresholds_xp = " ".join(
                str(k * mult) if mult is not None else "?" for k, _ in u.thresholds)
            in_own_cost = " ".join(
                "%gx" % (k / 100.0) for k, _ in u.thresholds)
            bonuses = " | ".join(
                "%s:%s" % (axis, "/".join(str(u.rank_traits[axis][r])
                                          for r in sorted(u.rank_traits[axis])))
                for axis in AXES if axis in u.rank_traits)
            extras = sorted({k.split("@", 1)[0] for k, _, _ in u.rank_extras})
            if extras:
                bonuses += " | extras:" + ",".join(extras)
            inert = "; ".join("%s (%s)" % (a, why) for a, why in u.inert_axes())
            accrual = u.accrual_minutes() or ["", "", ""]
            kills = u.kills_for_rank1()
            head = u.concealment_headroom()
            w.writerow([
                u.name, u.display_name, u.faction,
                u.cost if u.cost is not None else "",
                label_of.get(u.name, "?"),
                thresholds_xp, in_own_cost, bonuses,
                u.earns_xp_how(), inert,
                "yes" if u.buildable else "no",
                "yes" if u.disabled else "no",
                ("%.1f" % kills) if kills is not None else "",
                ("%.1f" % accrual[0]) if accrual[0] != "" else "",
                ("%.1f" % accrual[1]) if accrual[1] != "" else "",
                ("%.1f" % accrual[2]) if accrual[2] != "" else "",
                head[0] if head else "",
                head[1] if head else "",
            ])


GROUP_NAMES = {}


def group_units(ranking):
    """Group by identical bonus profile, largest group first."""
    buckets = defaultdict(list)
    for u in ranking:
        buckets[u.bonus_signature()].append(u)

    ordered = sorted(buckets.items(), key=lambda kv: (-len(kv[1]), kv[1][0].name))
    out = OrderedDict()
    for i, (sig, members) in enumerate(ordered):
        label = chr(ord("A") + i) if i < 26 else "G%d" % i
        out["Group " + label] = sorted(members, key=lambda m: m.name)
    return out


def print_groups(ranking, non_ranking):
    groups = group_units(ranking)
    print("RANKING ACTORS: %d in %d distinct bonus profiles" % (len(ranking), len(groups)))
    print("NON-RANKING CONCRETE ACTORS: %d" % len(non_ranking))
    print()
    for label, members in groups.items():
        first = members[0]
        print("=" * 78)
        print("%s  (%d actors)" % (label, len(members)))
        print("  thresholds: %s   ExperienceModifier: %s"
              % (", ".join(str(k) for k, _ in first.thresholds),
                 first.experience_modifier if first.experience_modifier is not None
                 else "unset (scales with own Cost)"))
        for axis in AXES:
            if axis in first.rank_traits:
                vals = "/".join(str(first.rank_traits[axis][r])
                                for r in sorted(first.rank_traits[axis]))
                print("  %-28s %s" % (axis, vals))
        extras = sorted({k.split("@", 1)[0] for k, _, _ in first.rank_extras})
        if extras:
            print("  extras: %s" % ", ".join(extras))
        for key, mod, cond in sorted(first.xp_modifiers):
            print("  XP rate: %s = %s%% while `%s`" % (key, mod, cond))
        print("  evacuation credit: %s" % first.credits_rank_on_evacuation)
        print("  members:")
        for m in members:
            inert = m.inert_axes()
            flags = []
            if not m.can_kill:
                flags.append("CANNOT EARN XP BY KILLING")
            if inert:
                flags.append("inert: " + ", ".join(a for a, _ in inert))
            head = m.concealment_headroom()
            if head is not None and head[1] < len(m.rank_traits.get(
                    "DetectableAddativeModifier", {})):
                flags.append("concealment base %d: only %d of %d rank steps fit under the cap of %d"
                             % (head[0], head[1],
                                len(m.rank_traits["DetectableAddativeModifier"]),
                                CONCEALMENT_CEILING))
            print("    %-26s %-9s %6s  %s"
                  % (m.name, m.faction,
                     m.cost if m.cost is not None else "-",
                     "; ".join(flags)))
        print()


def print_accrual(ranking):
    """
    The two earn paths side by side, cheapest unit first. This is the table §4 of
    the audit is built on: the KILL path is cost-normalised in value but not in
    kill count, while the PURCHASE path is compressed by an integer square root.
    """
    rows = [u for u in ranking
            if u.buildable and not u.disabled and u.cost is not None]
    rows.sort(key=lambda u: (u.cost, u.name))
    print("%-22s %-9s %6s  %7s  %7s %7s %7s  %s"
          % ("unit", "faction", "cost", "kills*", "R1 min", "R2 min", "R3 min", "note"))
    print("-" * 104)
    for u in rows:
        acc = u.accrual_minutes()
        kills = u.kills_for_rank1()
        note = "" if u.can_kill else "CANNOT EARN BY KILLING"
        print("%-22s %-9s %6d  %7s  %7.1f %7.1f %7.1f  %s"
              % (u.name, u.faction, u.cost,
                 ("%.0f" % kills) if kills is not None else "-",
                 acc[0], acc[1], acc[2], note))
    print()
    print("* kills = number of %d-credit targets that must die to this unit for RANK 1"
          % CHEAPEST_TARGET_COST)
    print("  (rank 1 = 1x own cost in enemy value; rank 4 = 8x, so multiply by 8)")
    print("  min = wall-clock minutes per free accrued rank of that tier, at Timestep %dms"
          % TIMESTEP_MS)


def print_concealment(ranking):
    """
    How many of the four concealment rank steps actually raise the level, once the
    actor's own non-rank concealment sources are stacked. ClampConcealment's
    ceiling is %d (Detectable.cs:118-125, MapLayers.VisionLayers = %d).
    """ % (CONCEALMENT_CEILING, VISION_LAYERS)
    rows = []
    for u in ranking:
        r = u.concealment_best_case()
        if r is None:
            continue
        base, stacked, levels, landed = r
        rows.append((landed, -stacked, u.name, u.faction, base, stacked, levels, landed))
    rows.sort()
    print("ceiling = %d  (MapLayers.VisionLayers %d - 2, Detectable.cs:118-125)"
          % (CONCEALMENT_CEILING, VISION_LAYERS))
    print("%-24s %-9s %5s %8s  %-18s %s"
          % ("unit", "faction", "base", "stacked", "level after R1..R4", "rank steps that land"))
    print("-" * 96)
    seen = set()
    for _, _, name, faction, base, stacked, levels, landed in rows:
        key = (base, stacked, tuple(levels))
        tag = "" if key not in seen else "  (same profile)"
        seen.add(key)
        print("%-24s %-9s %5d %8d  %-18s %d of %d%s"
              % (name, faction, base, stacked,
                 "/".join(str(x) for x in levels), landed, len(levels), tag))


def print_unit(ranking, non_ranking, name):
    for u in ranking + non_ranking:
        if u.name.lower() == name.lower():
            print("%s (%s)" % (u.name, u.display_name))
            print("  faction: %s   cost: %s   buildable: %s   ~disabled: %s"
                  % (u.faction, u.cost, u.buildable, u.disabled))
            if u.gains_experience is None:
                print("  NO GainsExperience: cannot hold a rank, and accrues no purchase stock")
                return 0
            mult = u.experience_modifier if u.experience_modifier is not None else u.cost
            print("  thresholds: %s  (x%s = %s XP)"
                  % ([k for k, _ in u.thresholds], mult,
                     [k * mult for k, _ in u.thresholds] if mult else "?"))
            for axis in AXES:
                if axis in u.rank_traits:
                    print("  %-28s %s" % (axis, u.rank_traits[axis]))
            for e in u.rank_extras:
                print("  extra: %s" % (e,))
            print("  armaments: %s" % (u.armaments,))
            print("  lethal armaments: %s" % (u.lethal_armaments,))
            print("  earns XP: %s" % u.earns_xp_how())
            print("  inert: %s" % (u.inert_axes(),))
            print("  concealment (base, surviving rank steps): %s" % (u.concealment_headroom(),))
            return 0
    print("no actor named %r" % name, file=sys.stderr)
    return 1


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--csv", help="write the machine-readable companion to this path")
    ap.add_argument("--groups", action="store_true", help="print the grouped summary")
    ap.add_argument("--accrual", action="store_true",
                    help="print the kill-path vs purchase-path table per buildable unit")
    ap.add_argument("--concealment", action="store_true",
                    help="print how many concealment rank steps survive the cap per unit")
    ap.add_argument("--unit", help="dump one actor's resolved rank facts")
    ap.add_argument("--root", default=repo_root(), help="repository root")
    args = ap.parse_args()

    tree, rule_files, ranking, non_ranking = collect(args.root)

    if args.unit:
        return print_unit(ranking, non_ranking, args.unit)

    if args.csv:
        write_csv(args.csv, ranking)
        print("wrote %s (%d rows)" % (args.csv, len(ranking)))

    if args.accrual:
        print_accrual(ranking)
        return 0

    if args.concealment:
        print_concealment(ranking)
        return 0

    if args.groups or not args.csv:
        print("rules files merged: %d" % len(rule_files))
        print("top-level keys resolved: %d   concrete actors: %d"
              % (len(tree), len(ranking) + len(non_ranking)))
        print()
        print_groups(ranking, non_ranking)

    return 0


if __name__ == "__main__":
    sys.exit(main())
