"""One-shot audit helper: read the shipped MiniYaml rules and report every
AmmoPool's per-round supply cost against its actor's call-in cost.

Not a build artifact. Arithmetic over the shipped pools only -- no simulation.
"""
import os, re, sys, json

ROOT = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(ROOT)
MOD = os.path.join(REPO, "mods", "ww3mod")

RULES = []
with open(os.path.join(MOD, "mod.yaml"), encoding="utf-8") as f:
    inrules = False
    for line in f:
        if re.match(r"^Rules:", line):
            inrules = True
            continue
        if inrules:
            m = re.match(r"^\s+ww3mod\|(\S+)", line)
            if m:
                RULES.append(m.group(1))
            elif line.strip() and not line.startswith((" ", "\t")):
                break


def indent_of(line):
    n = 0
    for c in line:
        if c == "\t":
            n += 1
        elif c == " ":
            n += 1
        else:
            break
    return n


def parse(path):
    """Return {actor: {trait_key: {field: value}}} merging across files."""
    out = {}
    if not os.path.exists(path):
        return out
    with open(path, encoding="utf-8") as f:
        lines = f.readlines()
    actor = None
    trait = None
    for raw in lines:
        line = raw.rstrip("\n")
        if not line.strip() or line.lstrip().startswith("#"):
            continue
        ind = indent_of(line)
        content = line.strip()
        content = re.sub(r"\s+#.*$", "", content)
        if ind == 0:
            actor = content.rstrip(":").strip()
            out.setdefault(actor, {})
            trait = None
        elif ind == 1 and actor is not None:
            trait = content.rstrip(":").strip()
            if ":" in trait:
                k, _, v = trait.partition(":")
                trait = k.strip()
            out[actor].setdefault(trait, {})
        elif ind >= 2 and actor is not None and trait is not None:
            if ":" in content:
                k, _, v = content.partition(":")
                out[actor][trait][k.strip()] = v.strip()
    return out


ALL = {}
for rel in RULES:
    d = parse(os.path.join(MOD, rel))
    for a, traits in d.items():
        tgt = ALL.setdefault(a, {})
        for t, fields in traits.items():
            tgt.setdefault(t, {}).update(fields)
        tgt.setdefault("__files__", {})[rel] = True


def resolve(actor, trait_pred, seen=None):
    """Collect traits matching pred, following Inherits chains."""
    if seen is None:
        seen = set()
    if actor in seen or actor not in ALL:
        return {}
    seen.add(actor)
    res = {}
    node = ALL[actor]
    for key in sorted(node.keys()):
        if key.startswith("Inherits"):
            parent = node[key] if isinstance(node[key], str) else None
    # Inherits stored as trait with no fields; re-read raw
    for t, fields in node.items():
        if t.startswith("Inherits"):
            continue
        if trait_pred(t):
            res[t] = fields
    return res


# Inherits values are lost by the parser above (they're `Inherits: ^Foo` at ind 1).
# Re-scan for them explicitly.
INHERITS = {}
for rel in RULES:
    p = os.path.join(MOD, rel)
    if not os.path.exists(p):
        continue
    actor = None
    for raw in open(p, encoding="utf-8"):
        line = raw.rstrip("\n")
        if not line.strip() or line.lstrip().startswith("#"):
            continue
        ind = indent_of(line)
        c = re.sub(r"\s+#.*$", "", line.strip())
        if ind == 0:
            actor = c.rstrip(":").strip()
        elif ind == 1 and actor and c.startswith("Inherits"):
            _, _, v = c.partition(":")
            if v.strip():
                INHERITS.setdefault(actor, []).append(v.strip())


def chain(actor, seen=None):
    if seen is None:
        seen = []
    if actor in seen:
        return seen
    seen = seen + [actor]
    for p in INHERITS.get(actor, []):
        seen = chain(p, seen)
    return seen


def traits_of(actor, prefix):
    """All traits whose key starts with prefix, resolved through inheritance
    (child overrides parent for same trait key)."""
    result = {}
    for a in reversed(chain(actor)):
        for t, fields in ALL.get(a, {}).items():
            if t.startswith(prefix):
                result.setdefault(t, {}).update(fields)
    return result


def field(actor, trait_prefix, name, default=None):
    ts = traits_of(actor, trait_prefix)
    for t, f in ts.items():
        if name in f:
            return f[name]
    return default


rows = []
for actor in sorted(ALL.keys()):
    if actor.startswith("^") or ".husk" in actor:
        continue
    pools = traits_of(actor, "AmmoPool")
    if not pools:
        continue
    cost = field(actor, "Valued", "Cost")
    try:
        cost = int(cost)
    except (TypeError, ValueError):
        cost = None
    tname = field(actor, "Tooltip", "Name") or ""
    arms = traits_of(actor, "Armament")
    armmap = {}
    for t, f in arms.items():
        nm = f.get("Name", "primary")
        armmap.setdefault(nm, []).append(f.get("Weapon", "?"))
    for pt, pf in sorted(pools.items()):
        try:
            ammo = int(pf.get("Ammo", 1))
            reload_ct = int(pf.get("ReloadCount", 1))
            sv = int(pf.get("SupplyValue", 1))
        except ValueError:
            continue
        pname = pf.get("Name", "primary")
        armnames = [x.strip() for x in pf.get("Armaments", "primary, secondary").split(",")]
        weapons = []
        for an in armnames:
            weapons.extend(armmap.get(an, []))
        batch = max(1, reload_ct)
        per_round = sv / batch
        batches = (ammo + batch - 1) // batch
        full = batches * sv
        rows.append(dict(actor=actor, tooltip=tname, cost=cost, pool=pname,
                         ammo=ammo, reload=batch, sv=sv, per_round=per_round,
                         full=full, weapons=weapons))

rows.sort(key=lambda r: r["per_round"])
print(f"{'actor':<16}{'pool':<18}{'ammo':>5}{'rld':>5}{'SV':>7}{'/rnd':>9}{'full':>7}{'unit$':>7}  weapons")
for r in rows:
    w = "+".join(sorted(set(r["weapons"]))) or "(none)"
    pct = f"{100*r['full']/r['cost']:.1f}%" if r["cost"] else "-"
    print(f"{r['actor']:<16}{r['pool']:<18}{r['ammo']:>5}{r['reload']:>5}{r['sv']:>7}"
          f"{r['per_round']:>9.1f}{r['full']:>7}{str(r['cost']):>7}  {w} [{pct}]")

json.dump(rows, open(os.path.join(ROOT, "ammo_audit.json"), "w"), indent=1)
print(f"\n{len(rows)} pools across {len(set(r['actor'] for r in rows))} actors")
