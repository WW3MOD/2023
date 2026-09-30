"""
Every production-tooltip weapon heading the mod will render, for all buildables.

WHY IT EXISTS: `AmmoPoolInfo.FormatWeaponLabel` turns a ruleset weapon key into the heading a
player reads, so the shipped headings are a property of the KEYS, and no gate in the tree prints
them. This resolves the rules the way the engine does and runs the same algorithm, so the
follow-up naming work can be sized from a list instead of from memory.

THE ORACLE IS VALIDATED, NOT ASSUMED. `--selftest` replays all 17 FormatWeaponLabel expectations
from engine/OpenRA.Test/OpenRA.Mods.Common/AmmoPoolTest.cs through this file's Python port. If
that does not pass, nothing this script prints means anything. Run it first.

THE TRAP THIS SCRIPT ALREADY FELL INTO, ONCE: MiniYaml `Inherits:` MERGES a trait's child nodes
key-by-key; it does not replace the trait. An earlier version replaced, and therefore reported a
confident, plausible, entirely fictional defect on A10.Airstrike — the one actor that overrides a
single field of an inherited AmmoPool. A bespoke YAML reader that gets inheritance wrong
manufactures defects in exactly the actors that are interesting. See WORKSPACE/DISCOVERIES.md
2026-09-21.

    python3 tools/tooltip-census/census.py --selftest
    python3 tools/tooltip-census/census.py
"""
import sys, re, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from miniyaml import parse

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..'))

def rules_files():
    out, inside = [], False
    for line in open(os.path.join(ROOT, 'mods/ww3mod/mod.yaml'), encoding='utf-8'):
        if line.startswith('Rules:'):
            inside = True; continue
        if inside:
            if not line.startswith('\t'): break
            s = line.strip()
            if not s or s.startswith('#'): continue
            out.append(os.path.join(ROOT, 'mods/ww3mod', s.split('|',1)[1]))
    return out

def merge_nodes(base, incoming):
    """MiniYaml merge: same-key nodes merge their children recursively; '-Key' removes."""
    out = dict(base)
    for k, v, ch in incoming:
        if k.startswith('-'):
            out.pop(k[1:], None)
            continue
        if k in out:
            oldv, oldch = out[k]
            out[k] = (v if v else oldv, merge_nodes(oldch, ch))
        else:
            out[k] = (v, merge_nodes({}, ch))
    return out

actors = {}
for f in rules_files():
    if not os.path.exists(f):
        print('MISSING', f); continue
    for k, v, ch in parse(f):
        if k.startswith('-'): continue
        cur = actors.get(k, {'inherits': [], 'nodes': {}})
        for tk, tv, tch in ch:
            if tk.startswith('Inherits'):
                cur['inherits'].append(tv)
        cur['nodes'] = merge_nodes(cur['nodes'], [n for n in ch if not n[0].startswith('Inherits')])
        actors[k] = cur

lower = {}
for k in actors:
    lower.setdefault(k.lower(), k)

def resolve(name, seen=None):
    if seen is None: seen = set()
    key = lower.get(name.lower())
    if key is None or key in seen: return {}
    seen.add(key)
    a = actors[key]
    eff = {}
    for parent in a['inherits']:
        eff = merge_nodes(eff, [(k, v, [(ck, cv, _to_list(cch)) for ck,(cv,cch) in ch.items()])
                                for k,(v,ch) in resolve(parent, set(seen)).items()])
    eff = merge_nodes(eff, [(k, v, _to_list(ch)) for k,(v,ch) in
                            [(k, val) for k, val in a['nodes'].items()]])
    return eff

def _to_list(d):
    return [(k, v, _to_list(ch)) for k, (v, ch) in d.items()]

def fields(children):
    """children here is the merged dict {key: (value, childdict)}"""
    return {k: v for k, (v, _) in children.items()}

def format_weapon_label(raw):
    if not raw: return "Weapon"
    trimmed = raw.lstrip('^').replace('-', ' ').replace('_', ' ')
    sb = []
    for i, c in enumerate(trimmed):
        if c == '.':
            is_dp = (i > 0 and i+1 < len(trimmed) and trimmed[i-1].isdigit() and trimmed[i+1].isdigit())
            sb.append('.' if is_dp else ' '); continue
        if i > 0 and c.isupper():
            prev = trimmed[i-1]
            if prev.islower() or (prev.isupper() and i+1 < len(trimmed) and trimmed[i+1].islower()):
                sb.append(' ')
        sb.append(c)
    return ' '.join(''.join(sb).split())

def is_mode_tag(w): return 0 < len(w) <= 3 and all(c.isupper() for c in w)

def merge_variant_labels(labels):
    if len(labels) <= 1: return labels[0] if labels else "Weapon"
    split=[l.split(' ') for l in labels]; common=0; shortest=min(len(w) for w in split)
    while common < shortest and all(w[common]==split[0][common] for w in split): common+=1
    if common==0: return ' + '.join(labels)
    if not all(all(is_mode_tag(x) for x in w[common:]) for w in split): return ' + '.join(labels)
    return ' '.join(split[0][:common])

def format_pool_label(pool_name):
    label = format_weapon_label(pool_name)
    if label.lower().endswith(' ammo') and len(label) > 5: label = label[:-5]
    return label


CSHARP_EXPECTATIONS = [
    ('TankRound.Abrams', 'Tank Round Abrams'), ('GradRockets', 'Grad Rockets'),
    ('ArtilleryRound.Paladin', 'Artillery Round Paladin'),
    ('Hellfire.Littlebird', 'Hellfire Littlebird'), ('60mm_Mortar', '60mm Mortar'),
    ('Stinger.quad', 'Stinger quad'), ('5.56mm.DMR', '5.56mm DMR'),
    ('12.7mm.Hind.AA', '12.7mm Hind AA'), ('5.56mm.DMR.silencer', '5.56mm DMR silencer'),
    ('HIMARSTargeter', 'HIMARS Targeter'), ('DroneTargeter', 'Drone Targeter'),
    ('9M311', '9M311'), ('MP5', 'MP5'), ('RPG', 'RPG'), ('WGM', 'WGM'),
    (None, 'Weapon'), ('', 'Weapon'),
]

if '--selftest' in sys.argv:
    bad = [(r, format_weapon_label(r), e) for r, e in CSHARP_EXPECTATIONS
           if format_weapon_label(r) != e]
    if bad:
        for r, got, want in bad:
            print(f'MISMATCH {r!r}: got {got!r}, AmmoPoolTest.cs says {want!r}')
        sys.exit(1)
    print(f'selftest OK - {len(CSHARP_EXPECTATIONS)}/{len(CSHARP_EXPECTATIONS)} '
          'FormatWeaponLabel expectations from AmmoPoolTest.cs reproduced')
    sys.exit(0)

rows=[]
for name in sorted(actors):
    if name.startswith('^') or name.startswith('-'): continue
    eff=resolve(name)
    if 'Buildable' not in eff: continue
    arms={}
    for tk,(tv,tch) in eff.items():
        if tk=='Armament' or tk.startswith('Armament@'):
            f=fields(tch); arms[f.get('Name','primary')]=f.get('Weapon')
    for tk,(tv,tch) in eff.items():
        if tk=='AmmoPool' or tk.startswith('AmmoPool@'):
            f=fields(tch); pname=f.get('Name','primary')
            if int(f.get('Ammo',1))<=0 or int(f.get('SupplyValue',1))<=0: continue
            tt=f.get('TooltipName'); ra=f.get('Armaments')
            al=['primary','secondary'] if ra is None else [a.strip() for a in ra.split(',') if a.strip()]
            bound=[arms[a] for a in al if a in arms and arms[a]]
            if tt: lab,src,raw=tt,'TooltipName','-'
            elif not bound: lab,src,raw=format_pool_label(pname),'poolname',pname
            else:
                seen=[]
                for w in bound:
                    l=format_weapon_label(w)
                    if l not in seen: seen.append(l)
                lab,src,raw=merge_variant_labels(seen),'weapon','+'.join(bound)
            rows.append((lab,src,raw,name))
named=sorted({(l,r) for l,s,r,n in rows if s=='TooltipName'})
print(f'=== ALREADY HAND-NAMED via AmmoPoolInfo.TooltipName ({len(named)}) — the lever exists ===')
for l,r in named:
    who=sorted({n for ll,s,rr,n in rows if ll==l})
    print(f'  "{l.upper()}"  <- {", ".join(who)}')
print()
auto=sorted({(l,r) for l,s,r,n in rows if s!='TooltipName'})
print(f'=== AUTO-DERIVED headings ({len(auto)} distinct) — rendered upper-case by the Subhead row ===')
for l,r in auto:
    who=sorted({n for ll,s,rr,n in rows if ll==l and s!='TooltipName'})
    print(f'  {r:36s} -> "{l.upper()}"   ({len(who)} actor(s): {", ".join(who[:3])}{" ..." if len(who)>3 else ""})')
