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
"""

import argparse
import csv
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
            w.writerow([
                u.name, u.display_name, u.faction,
                u.cost if u.cost is not None else "",
                label_of.get(u.name, "?"),
                thresholds_xp, in_own_cost, bonuses,
                u.earns_xp_how(), inert,
                "yes" if u.buildable else "no",
                "yes" if u.disabled else "no",
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
    ap.add_argument("--unit", help="dump one actor's resolved rank facts")
    ap.add_argument("--root", default=repo_root(), help="repository root")
    args = ap.parse_args()

    tree, rule_files, ranking, non_ranking = collect(args.root)

    if args.unit:
        return print_unit(ranking, non_ranking, args.unit)

    if args.csv:
        write_csv(args.csv, ranking)
        print("wrote %s (%d rows)" % (args.csv, len(ranking)))

    if args.groups or not args.csv:
        print("rules files merged: %d" % len(rule_files))
        print("top-level keys resolved: %d   concrete actors: %d"
              % (len(tree), len(ranking) + len(non_ranking)))
        print()
        print_groups(ranking, non_ranking)

    return 0


if __name__ == "__main__":
    sys.exit(main())
