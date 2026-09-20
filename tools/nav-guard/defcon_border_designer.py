"""defcon-border-designer -- WORK IN PROGRESS, not wired into any gate.

Authoring aid for the DEFCON 3 (Positioning-phase) border on the shipped maps, i.e. the
REGION form of DefconWall (DefconWallInfo.RegionTerrainTypes / RegionCells). River Zeta is
the one map authored so far (184680d4); this is the tool for the remaining eight.

THE METHOD IT IMPLEMENTS. A border that separates the map in the 8-connected graph the
engine floods (DefconWallRegion.Label) is exactly a 4-CONNECTED chain of blocked cells
running from one Bounds edge to another -- that duality is what makes this a shortest-path
problem rather than a min-cut one. `barrier_path` runs Dijkstra over the Bounds rectangle
on a 4-connected grid with a per-terrain cost (DEFC) that makes cells a player ALREADY
reads as a barrier -- Water, River, Cliffs -- nearly free, and open ground expensive. The
route therefore hugs whatever real feature exists and crosses open ground only where the
map leaves it no choice; `finalize` reports how many cells of the chosen route were
"invented" (crossed open ground) against how many were found.

The chain is then dilated by one cell (`thicken`, r=1) so the band is three cells thick.
That is not decoration: a one-cell band seals an axis-aligned line and nothing else, which
is the leak DefconWallInfo.HalfWidth documents at 131 map/locomotor combinations, and the
same arithmetic applies to a hand-drawn diagonal. Three cells is also exactly the thickness
the shipped HalfWidth of 1024 produces, so an authored band is never thinner than the
derived line it replaces.

`sweep` enumerates candidate edge-to-edge anchor pairs and ranks them on route cost per
cell against the AREA BALANCE of the two halves, rejecting any candidate that fails to put
the two reference spawns in different components. `finalize` then reports, for one chosen
candidate: component sizes, each spawn's Chebyshev distance to the band (the fairness
number that actually matters -- how far each side must advance to reach the border),
capturable structures per side, any capturable the band would swallow, and the full
defcon_wall_audit result including the engine load gate.

Capturables are forbidden to the router by default (`forbid_caps`, margin 2 cells) so a
route cannot be chosen that buries an Oil Derrick inside the band -- the woodland-warfare
failure the derived bisector already produces.

WHERE THE OUTPUT GOES, and this changed: a border is now MAP data, not rule data. All nine
authored borders live in their map's own map.yaml as `Zones: DMZ` (row-ranges by Y), which is
what the editor's Zones tool paints and what DefconWall unions into its region path. Use
`emit_zone` for that block; `emit` still produces the legacy RegionCells form for a border
authored by a mod rule instead of by the map. defcon_wall_audit.py reads BOTH back out of a map
(`--region-from-map`) and is the check to run on anything this tool produces.

STILL NOT DONE: there is no PNG/ASCII render of a finished band beyond `show`.
"""
import sys, json
from pathlib import Path
from collections import deque, Counter
sys.path.insert(0, str(Path(__file__).resolve().parent))
import modload, nav_guard
import defcon_wall_audit as dwa

RULES = modload.load_mod(nav_guard.MOD_DIR)
MAPS = {p.name: p for p in modload.discover_maps(nav_guard.MOD_DIR)}

_LOADED = {}


def load(name):
    if name not in _LOADED:
        mp = modload.load_map(MAPS[name])
        _LOADED[name] = (mp, RULES.tilesets[mp.tileset])
    return _LOADED[name]

def terrain_cells(mp, ts, types):
    t = set(types)
    return {(x, y) for y in range(mp.height) for x in range(mp.width)
            if mp.terrain_type(ts, x, y) in t}

def line_cells(a, b):
    """Every cell the segment a..b passes through (supercover-ish: dense sampling)."""
    (x0, y0), (x1, y1) = a, b
    n = max(abs(x1 - x0), abs(y1 - y0)) * 4 + 1
    out = set()
    for i in range(n + 1):
        t = i / n
        out.add((round(x0 + (x1 - x0) * t), round(y0 + (y1 - y0) * t)))
    return out

def thicken(cells, r):
    out = set()
    for (x, y) in cells:
        for dy in range(-r, r + 1):
            for dx in range(-r, r + 1):
                out.add((x + dx, y + dy))
    return out

def polyline(points, r):
    core = set()
    for a, b in zip(points, points[1:]):
        core |= line_cells(a, b)
    return thicken(core, r)

def clip(cells, mp):
    l, t, w, h = mp.bounds
    return {c for c in cells if l <= c[0] < l + w and t <= c[1] < t + h}

CAPNAMES = ('oilb','logisticscenter','fcom','miss','hosp','bio','gun','mslo','afld','hpad','agun','sam','hsam','cram','ftur','barl','brl3','ammobox1','ammobox2','ammobox3','ctflag')

def audit(name, types, blocked, verbose=True, quiet_loco=True):
    mp, ts = load(name)
    spawns = sorted(a.location for a in mp.actors if a.name == 'mpspawn')
    srs = [(x - 1, y - 1) for x, y in spawns]
    in_b, dropped, comps = dwa.engine_load_gate(mp, blocked)
    print(f"{name}: border={len(blocked)} inBounds={in_b} dropped={dropped} openComponents={comps}")
    # label open graph
    l, t, w, h = mp.bounds
    inside = {c for c in blocked if l <= c[0] < l+w and t <= c[1] < t+h}
    lab = {}; cid = 0; sizes = []
    for y in range(t, t+h):
        for x in range(l, l+w):
            if (x,y) in inside or (x,y) in lab: continue
            q = deque([(x,y)]); lab[(x,y)] = cid; n = 0
            while q:
                cx, cy = q.popleft(); n += 1
                for dy in (-1,0,1):
                    for dx in (-1,0,1):
                        if dx==0 and dy==0: continue
                        nn = (cx+dx, cy+dy)
                        if not (l <= nn[0] < l+w and t <= nn[1] < t+h): continue
                        if nn in inside or nn in lab: continue
                        lab[nn] = cid; q.append(nn)
            sizes.append(n); cid += 1
    order = sorted(range(cid), key=lambda i: -sizes[i])
    print("   open comps sizes:", [sizes[i] for i in order[:6]])
    print("   spawn comps:", [(s, lab.get(s)) for s in spawns])
    print("   SR    comps:", [(s, lab.get(s)) for s in srs])
    caps = [(a.name, a.location) for a in mp.actors if a.name in CAPNAMES]
    inband = [c for c in caps if c[1] in inside]
    print(f"   capturables in band: {inband}")
    bycomp = {}
    for nm, loc in caps:
        bycomp.setdefault(lab.get(loc), []).append((nm, loc))
    for k in sorted(bycomp, key=lambda z: (z is None, z)):
        print(f"     comp {k}: {len(bycomp[k])} -> {Counter(n for n,_ in bycomp[k]).most_common()}")
    # per-locomotor
    locos = modload.world_locomotors(RULES, mp.rule_overrides)
    occ, _ = nav_guard.cell_occupancy(RULES, mp, "live")
    bad = 0
    for loco in locos:
        model = nav_guard.build_cell_model(RULES, mp, ts, loco, occ)
        if sum(model.passable) == 0: continue
        r = dwa.audit_region(model, blocked, nav_guard.DEFAULT_SQUEEZE, spawns)
        if not [c for c in r["spawn_components"] if c is not None]: continue
        ok = r["separates"]
        if not ok: bad += 1
        if (not ok) or r["newly_isolated"] or not quiet_loco:
            xs = ys = ''
            if r["sealed_cells"]:
                X = [model.left + (c % model.width) for c in r["sealed_cells"]]
                Y = [model.top + (c // model.width) for c in r["sealed_cells"]]
                xs = f" sealedbbox x{min(X)}..{max(X)} y{min(Y)}..{max(Y)}"
            print(f"     {'ok ' if ok else 'LEAK'} {loco.name:<24} comps={r['distinct']} spawns={r['spawn_components']} sealed={r['newly_isolated']}{xs}")
    print(f"   -> {'SEPARATES' if comps>=2 and bad==0 else 'FAIL'} (engine gate {comps} comps, {bad} leaking locomotors)")
    return comps, bad

import heapq
COST = {'Cliffs':2,'Water':2,'River':2,'RiverShallow':4,'Bridge':6,'Rock':5,
        'Rough':8,'Debris':8,'Beach':8,'Road':10,'Clear':10,'Tree':6}

def barrier_path(name, anchors, cost=None, forbid=(), bias=None):
    """Min-cost 4-connected chain through the Bounds, visiting `anchors` in order.

    `bias` is an optional (axis, centre, weight) triple -- axis 'x' or 'y' -- adding
    `weight * distance-from-centre` to every cell. It is what keeps a border on a map with
    no continuous feature from wandering: without it the router will happily detour thirty
    cells to pick up one more cliff, and the result reads as a scribble rather than a line.
    """
    mp, ts = load(name)
    c = dict(COST); c.update(cost or {})
    l,t,w,h = mp.bounds
    forbid = set(forbid)
    def W(x,y):
        # A forbidden cell is priced out rather than removed. Deleting cells can disconnect
        # the grid outright -- a capturable sitting on the map edge walls its own anchor in --
        # and a router that cannot reach its destination gives no border at all, which is
        # strictly worse than a border that grazes a derrick and gets moved by hand.
        base = 1000 if (x,y) in forbid else c.get(mp.terrain_type(ts,x,y), 10)
        if bias:
            axis, centre, weight = bias
            base += weight * abs((x if axis == 'x' else y) - centre)
        return base
    def dij(src, dst):
        dist = {src: W(*src)}; prev = {}
        pq = [(dist[src], src)]
        while pq:
            d, u = heapq.heappop(pq)
            if u == dst: break
            if d > dist.get(u, 1<<60): continue
            ux, uy = u
            for dx,dy in ((1,0),(-1,0),(0,1),(0,-1)):
                v = (ux+dx, uy+dy)
                if not (l <= v[0] < l+w and t <= v[1] < t+h): continue
                nd = d + W(*v)
                if nd < dist.get(v, 1<<60):
                    dist[v] = nd; prev[v] = u; heapq.heappush(pq,(nd,v))
        if dst not in dist: raise RuntimeError(f"no path {src}->{dst}")
        path=[dst]
        while path[-1] != src: path.append(prev[path[-1]])
        return list(reversed(path)), dist[dst]
    forbid -= {tuple(a) for a in anchors}

    chain=[]; total=0
    for a,b in zip(anchors, anchors[1:]):
        p, d = dij(tuple(a), tuple(b)); total += d
        chain += p if not chain else p[1:]
    return mp, ts, chain, total

def build(name, anchors, types=(), r=1, cost=None, forbid=(), verbose=True, bias=None):
    mp, ts, chain, total = barrier_path(name, anchors, cost, forbid, bias)
    band = clip(thicken(set(chain), r), mp)
    tcells = terrain_cells(mp, ts, types) if types else set()
    blocked = band | tcells
    hand = sorted(clip(band - tcells, mp), key=lambda c:(c[1],c[0]))
    if verbose:
        print(f"  path len={len(chain)} cost={total} band={len(band)} terrain={len(tcells)} hand={len(hand)}")
    return mp, ts, chain, band, tcells, blocked, hand

def emit(hand):
    """The LEGACY form: DefconWallInfo.RegionCells, a flat X,Y list for a map's rules.yaml.

    Still read by the engine and still correct for a border authored by a mod rule rather than by
    the map. For a border that belongs to the MAP, prefer emit_zone -- the editor can paint that
    one back, and this one it cannot.
    """
    return ", ".join(f"{x},{y}" for x,y in hand)

def emit_zone(hand, zone="DMZ"):
    """The CURRENT form: a `Zones:` block for the map's own map.yaml, row-ranges by Y.

    Paste the whole thing into map.yaml between the `Actors:` and `Rules:` blocks -- that is where
    Map.YamlFields orders it, so putting it there means the first editor save is not a huge diff.
    Byte-identical to what MapZones.EncodeRows writes, which is what the editor will write over it
    the first time anyone adjusts the band with the Zones tool.
    """
    by_row = {}
    for x, y in hand:
        by_row.setdefault(y, set()).add(x)

    out = ["Zones:", "	" + zone + ":"]
    for y in sorted(by_row):
        xs = sorted(by_row[y]); runs = []; lo = hi = xs[0]
        for x in xs[1:]:
            if x == hi + 1:
                hi = x; continue
            runs.append((lo, hi)); lo = hi = x
        runs.append((lo, hi))
        out.append("		" + str(y) + ": " + ", ".join(str(a) if a == b else f"{a}-{b}" for a, b in runs))

    return chr(10).join(out)

def gap_path(mp, blocked, a, b):
    """Shortest 8-connected route through the OPEN graph from a to b, avoiding `blocked`."""
    l,t,w,h = mp.bounds
    inside = {c for c in blocked if l<=c[0]<l+w and t<=c[1]<t+h}
    def ok(c): return l<=c[0]<l+w and t<=c[1]<t+h and c not in inside
    if not ok(a) or not ok(b): return None
    prev={a:None}; q=deque([a])
    while q:
        u=q.popleft()
        if u==b: break
        for dy in(-1,0,1):
            for dx in(-1,0,1):
                if dx==0 and dy==0: continue
                v=(u[0]+dx,u[1]+dy)
                if ok(v) and v not in prev:
                    prev[v]=u; q.append(v)
    if b not in prev: return None
    p=[b]
    while prev[p[-1]] is not None: p.append(prev[p[-1]])
    return list(reversed(p))

def show(mp, ts, blocked, extra=(), x0=None,x1=None,y0=None,y1=None):
    GL = {'Clear':'.','Road':'-','Rough':',','Debris':'"','Water':'#','River':'~',
          'RiverShallow':'=','Beach':':','Bridge':'B','Cliffs':'^','Rock':'o'}
    spawns={a.location for a in mp.actors if a.name=='mpspawn'}
    ex=set(extra)
    x0=0 if x0 is None else x0; x1=mp.width-1 if x1 is None else x1
    y0=0 if y0 is None else y0; y1=mp.height-1 if y1 is None else y1
    print("     "+"".join(str(x//10%10) if x%10==0 else (str(x%10) if x%5==0 else ' ') for x in range(x0,x1+1)))
    for y in range(y0,y1+1):
        row=[]
        for x in range(x0,x1+1):
            c=GL.get(mp.terrain_type(ts,x,y),'?')
            if (x,y) in blocked: c='X'
            if (x,y) in ex: c='*'
            if (x,y) in spawns: c='S'
            row.append(c)
        print(f"{y:4d} "+"".join(row))

def capturable_footprints(mp, margin=2):
    """Every cell a neutral capturable structure occupies, dilated by `margin`."""
    out = {}
    for a in mp.actors:
        if a.name not in CAPNAMES: continue
        node = RULES.actor(a.name)
        shape = modload.actor_shape(a.name, node) if node else None
        occ = set()
        if shape:
            for dx,dy in list(shape.blocking) + list(getattr(shape,'footprint',()) or ()):
                occ.add((a.location[0]+dx, a.location[1]+dy))
        occ.add(a.location)
        out[(a.name, a.location)] = occ
    return out

def forbid_caps(mp, margin=2):
    f=set()
    for occ in capturable_footprints(mp).values():
        for (x,y) in occ:
            for dy in range(-margin,margin+1):
                for dx in range(-margin,margin+1):
                    f.add((x+dx,y+dy))
    return f

DEFC = {'Water':1,'River':1,'RiverShallow':2,'Bridge':4,'Cliffs':1,'Rock':5,
        'Rough':8,'Beach':7,'Road':10,'Clear':10,'Debris':8,'Tree':6}

def comps_of(mp, band):
    l,t,w,h=mp.bounds
    lab={}; sizes=[]
    for y in range(t,t+h):
        for x in range(l,l+w):
            if (x,y) in band or (x,y) in lab: continue
            cid=len(sizes); q=deque([(x,y)]); lab[(x,y)]=cid; n=0
            while q:
                cx,cy=q.popleft(); n+=1
                for dy in(-1,0,1):
                    for dx in(-1,0,1):
                        if dx==0 and dy==0: continue
                        nn=(cx+dx,cy+dy)
                        if not (l<=nn[0]<l+w and t<=nn[1]<t+h): continue
                        if nn in band or nn in lab: continue
                        lab[nn]=cid; q.append(nn)
            sizes.append(n)
    return lab, sizes

def edge_points(mp, step=4):
    l,t,w,h=mp.bounds
    N=[(x,t) for x in range(l,l+w,step)]
    S=[(x,t+h-1) for x in range(l,l+w,step)]
    W=[(l,y) for y in range(t,t+h,step)]
    E=[(l+w-1,y) for y in range(t,t+h,step)]
    return dict(N=N,S=S,W=W,E=E)

def sweep(name, refA, refB, pairs=(('N','S'),('W','E'),('N','E'),('N','W'),('S','E'),('S','W')),
          step=6, r=1, cost=None, forbid_caps_margin=2, top=10):
    mp, ts = load(name)
    c = dict(DEFC); c.update(cost or {})
    fb = forbid_caps(mp, forbid_caps_margin)
    ep = edge_points(mp, step)
    out=[]
    for ka,kb in pairs:
        for a in ep[ka]:
            for b in ep[kb]:
                if a==b: continue
                try:
                    _,_,chain,total = barrier_path(name,[a,b],cost=c,forbid=fb)
                except Exception: continue
                band = clip(thicken(set(chain),r), mp)
                lab,sizes = comps_of(mp, band)
                ia, ib = lab.get(refA), lab.get(refB)
                if ia is None or ib is None or ia==ib: continue
                bal = min(sizes[ia],sizes[ib])/max(sizes[ia],sizes[ib])
                stray = sum(sizes)-sizes[ia]-sizes[ib]
                out.append(dict(a=a,b=b,pair=(ka,kb),cost=total,n=len(chain),
                                avg=total/len(chain),bal=bal,A=sizes[ia],B=sizes[ib],
                                stray=stray,band=len(band)))
    out.sort(key=lambda r_: r_['avg']/max(r_['bal'],0.05) + r_['stray']*0.02)
    for r_ in out[:top]:
        print(f"  {r_['pair']} {r_['a']}->{r_['b']} avg={r_['avg']:.2f} cost={r_['cost']} "
              f"bal={r_['bal']:.2f} A={r_['A']} B={r_['B']} stray={r_['stray']} band={r_['band']}")
    return out

def isthmus(name, x0, x1, y0, y1, wet=('Water', 'River')):
    """Every DRY cell in a rectangle -- the land bridge through a lake, bank to bank.

    River Zeta's ruling, applied to a lake instead of a ford: cover the crossing bank to
    bank rather than plug it in the middle. A band that stops one cell short of the far
    shore leaves a sliver of beach walled in by its own border and the water, which reads
    as a hole in the band and measures as a sealed pocket.
    """
    mp, ts = load(name)
    return {(x, y) for y in range(y0, y1 + 1) for x in range(x0, x1 + 1)
            if mp.terrain_type(ts, x, y) not in wet}


def finalize(name, anchors, types=(), r=1, cost=None, forbid_extra=(), cap_margin=2,
             quiet=True, bias=None, extra=()):
    mp, ts = load(name)
    c = dict(DEFC); c.update(cost or {})
    fb = forbid_caps(mp, cap_margin) | set(forbid_extra)
    mp,ts,chain,band,tc,blocked,hand = build(name, anchors, types=types, r=r, cost=c,
                                             forbid=fb, bias=bias)
    if extra:
        band = band | clip(set(extra), mp)
        blocked = band | tc
        hand = sorted(clip(band - tc, mp), key=lambda cc: (cc[1], cc[0]))
    lab, sizes = comps_of(mp, blocked)
    spawns = sorted(a.location for a in mp.actors if a.name=='mpspawn')
    srs = [(x-1,y-1) for x,y in spawns]
    # invented cells: path cells on non-barrier terrain
    inv = [p for p in chain if mp.terrain_type(ts,*p) not in ('Water','River','Cliffs','RiverShallow')]
    print(f"  invented (non water/cliff) path cells: {len(inv)}/{len(chain)}")
    for s in spawns:
        d = min(max(abs(s[0]-b[0]),abs(s[1]-b[1])) for b in blocked)
        print(f"    spawn {s} comp={lab.get(s)} chebyshev-to-band={d}")
    caps = capturable_footprints(mp)
    side = {}
    for (nm,loc),occ in caps.items():
        cs = {lab.get(o) for o in occ if o in lab}
        side[(nm,loc)] = (cs, any(o in blocked for o in occ))
    inband = [k for k,v in side.items() if v[1]]
    print(f"    capturables in band: {inband}")
    from collections import Counter
    cnt = Counter()
    for k,v in side.items():
        if v[1]: continue
        for s_ in v[0]: cnt[s_]+=1
    print(f"    capturables per component: {dict(cnt)}   comp sizes={ {i:sizes[i] for i in sorted(set(lab.values()))} }")
    audit(name, types, blocked, quiet_loco=quiet)
    return mp,ts,chain,band,tc,blocked,hand,lab,sizes


def scan(name, groupA, groupB, cands, r=1, cost=None, cap_margin=2, top=12):
    """Rank candidate (anchors, bias) pairs on FAIRNESS first, feature-following second.

    `spread` is the difference between the farthest and nearest spawn's Chebyshev distance
    to the band, and it is the number that decides a candidate. The derived bisector is
    equidistant from both sides by construction, so an authored border that hands one side a
    twenty-cell head start is a worse border however handsome the river it follows.
    `inv` counts route cells on terrain that is NOT already a barrier -- the border the map
    did not provide and this tool invented.
    """
    mp, ts = load(name)
    c = dict(DEFC); c.update(cost or {})
    fb = forbid_caps(mp, cap_margin)
    caps = capturable_footprints(mp)
    spawns = sorted(a.location for a in mp.actors if a.name == 'mpspawn')
    out = []
    for anchors, bias in cands:
        try:
            _, _, chain, total = barrier_path(name, anchors, cost=c, forbid=fb, bias=bias)
        except Exception:
            continue
        band = clip(thicken(set(chain), r), mp)
        lab, sizes = comps_of(mp, band)
        ga = {lab.get(s) for s in groupA} - {None}
        gb = {lab.get(s) for s in groupB} - {None}
        if len(ga) != 1 or len(gb) != 1 or ga == gb:
            continue
        d = [min(max(abs(s[0]-b[0]), abs(s[1]-b[1])) for b in band) for s in spawns]
        inv = sum(1 for p in chain
                  if mp.terrain_type(ts, *p) not in ('Water', 'River', 'Cliffs', 'Rock'))
        hit = [k for k, occ in caps.items() if occ & band]
        ia, ib = ga.pop(), gb.pop()
        out.append(dict(anchors=anchors, bias=bias, n=len(chain), inv=inv, band=len(band),
                        spread=max(d)-min(d), dist=d, A=sizes[ia], B=sizes[ib],
                        stray=sum(sizes)-sizes[ia]-sizes[ib], caphit=hit,
                        bal=min(sizes[ia], sizes[ib])/max(sizes[ia], sizes[ib])))
    out.sort(key=lambda z: (z['spread'], z['inv']))
    for z in out[:top]:
        print(f"  {z['anchors']} bias={z['bias']} spread={z['spread']:2d} dist={z['dist']} "
              f"inv={z['inv']:3d}/{z['n']:3d} bal={z['bal']:.2f} stray={z['stray']} "
              f"cap={len(z['caphit'])}")
    return out
