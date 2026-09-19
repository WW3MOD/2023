#!/usr/bin/env python3
"""Static guard: a world-actor trait must not dereference .WorldActor while it is being created.

WHY THIS EXISTS
---------------
``engine/OpenRA/World.cs:252`` is literally::

    WorldActor = CreateActor("World", new TypeDictionary());

The assignment happens AFTER CreateActor returns, so every trait on the world actor is
constructed -- and has its ``INotifyCreated.Created`` invoked -- while ``World.WorldActor``
is still null. Any access through it in those two places throws a NullReferenceException
during world construction, which means the match never starts at all.

That shipped on 2026-09-10 in ``DefconWall.Created`` and broke every match. Six gates were
green at the time (build, check, NUnit, YAML lint, lua-gate, nav-guard) because none of them
constructs a World. The smoke gate (``make.ps1 smoke``) is the dynamic half of the answer;
this is the static half, and it is the cheap one -- no build, no launch, ~1s.

THE DISCRIMINATOR IS THE ACTOR, NOT THE IDIOM
---------------------------------------------
``self.World.WorldActor`` in ``Created`` is CORRECT on a player trait or a per-actor trait --
``DefconCasualtyObserver`` does exactly this and is fine -- because player and unit actors are
built after the world actor exists. The same line is a crash on a world trait. So this gate
keys on ``[TraitLocation(...)]`` naming World, and flags nothing else.

WHAT IS IN SCOPE, precisely
---------------------------
For every class carrying ``[TraitLocation(...)]`` whose flag list mentions ``World``
(so ``SystemActors.World`` and ``SystemActors.EditorWorld``, alone or combined):

  * that Info class's own ``Create(...)`` body -- it runs inside CreateActor; and
  * the constructor(s) and ``Created(...)`` of every trait class it instantiates
    (resolved from ``new Foo(...)`` inside ``Create``, project-wide, not just same-file).

A ``.WorldActor`` dereference anywhere in those bodies is a finding. ``IWorldLoaded.WorldLoaded``,
``ITick``, and every later-lifecycle member are NOT in scope and are never reported: by the time
they run the field has been assigned, which is why the overwhelming majority of ``w.WorldActor``
in the engine is correct code.

KNOWN LIMITS, so nobody reads a clean run as more than it is
------------------------------------------------------------
* A ``Created`` inherited from a base class in another type is attributed to that base, not to
  the world trait that inherits it.
* A lambda declared in ``Created`` is compiled into its own method; a lambda INVOKED immediately
  would slip through. A lambda merely stored and run later is correct code anyway.
* Reflection and ``ObjectCreator``-built instances are invisible to any source matcher.

Exit 0 clean, 2 on a finding. There is no warning band: the field is null there or it is not.
"""
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
ENGINE = ROOT / "engine"

# Findings that are understood and deliberately kept. Key is "relative/path.cs:Class.Member",
# value is the reason. Empty by design -- a new entry needs a written justification, which is
# the point. A null-conditional (`WorldActor?.Trait...`) does not crash, but in these two
# members it silently resolves to null, so it is NOT an accepted form and belongs here with a
# reason rather than being waved through by the matcher.
ACCEPTED = {}

TRAIT_LOCATION = re.compile(r"\[\s*TraitLocation\s*\(([^)]*)\)\s*\]")
WORLD_FLAG = re.compile(r"\bWorld\b")
CLASS_DECL = re.compile(r"\b(?:class|struct)\s+(\w+)")
# `.WorldActor` but NOT `.WorldActorInfo` -- the latter is Map/ActorInfo metadata, available
# throughout construction and entirely safe. The \b after the name is what separates them.
WORLD_ACTOR = re.compile(r"\.WorldActor\b(?!\s*Info)")


def blank_noncode(src):
    """Replace comments and string/char literals with spaces, preserving length and newlines.

    Length preservation is load-bearing: every offset this module reports is turned back into a
    line number against the ORIGINAL text. It also matters for correctness, not just cosmetics --
    DefconWall.cs and NuclearExchange.cs both discuss `self.World.WorldActor` at length in
    comments explaining why they must not use it, and a text matcher that reads comments would
    report the fix as the bug.
    """
    out = list(src)
    i, n = 0, len(src)
    while i < n:
        c = src[i]
        if c == '/' and i + 1 < n and src[i + 1] == '/':
            while i < n and src[i] != '\n':
                out[i] = ' '
                i += 1
        elif c == '/' and i + 1 < n and src[i + 1] == '*':
            out[i] = out[i + 1] = ' '
            i += 2
            while i < n and not (src[i] == '*' and i + 1 < n and src[i + 1] == '/'):
                if src[i] != '\n':
                    out[i] = ' '
                i += 1
            while i < n and i < len(src) and src[i] in '*/':
                out[i] = ' '
                i += 1
        elif c == '@' and i + 1 < n and src[i + 1] == '"':
            out[i] = out[i + 1] = ' '
            i += 2
            while i < n:
                if src[i] == '"':
                    if i + 1 < n and src[i + 1] == '"':
                        out[i] = out[i + 1] = ' '
                        i += 2
                        continue
                    out[i] = ' '
                    i += 1
                    break
                if src[i] != '\n':
                    out[i] = ' '
                i += 1
        elif c == '"' or c == "'":
            quote = c
            out[i] = ' '
            i += 1
            while i < n and src[i] != quote:
                if src[i] == '\\' and i + 1 < n:
                    out[i] = ' '
                    i += 1
                if i < n:
                    if src[i] != '\n':
                        out[i] = ' '
                    i += 1
            if i < n:
                out[i] = ' '
                i += 1
        else:
            i += 1
    return ''.join(out)


def match_brace(text, open_idx):
    """Index just past the `}` closing the `{` at open_idx, or None if unbalanced."""
    depth = 0
    for i in range(open_idx, len(text)):
        if text[i] == '{':
            depth += 1
        elif text[i] == '}':
            depth -= 1
            if depth == 0:
                return i + 1
    return None


def body_after(text, from_idx):
    """(start, end) of the next `{...}` block, stopping at `;`.

    An abstract or interface member has no block, and without the `;` stop it would silently
    borrow the NEXT member's body -- which is how a matcher starts reporting lines that are
    not in the member it names.
    """
    i = from_idx
    n = len(text)
    while i < n:
        if text[i] == ';':
            return None
        if text[i] == '{':
            end = match_brace(text, i)
            return (i, end) if end else None
        if text[i] == '=' and i + 1 < n and text[i + 1] == '>':
            # Expression body: runs to the next `;` at paren/brace depth 0.
            j, depth = i + 2, 0
            while j < n:
                if text[j] in '([{':
                    depth += 1
                elif text[j] in ')]}':
                    depth -= 1
                elif text[j] == ';' and depth == 0:
                    return (i, j)
                j += 1
            return None
        i += 1
    return None


def skip_parens(text, open_idx):
    """Index just past the `)` closing the `(` at open_idx."""
    depth = 0
    for i in range(open_idx, len(text)):
        if text[i] == '(':
            depth += 1
        elif text[i] == ')':
            depth -= 1
            if depth == 0:
                return i + 1
    return None


def classes_in(text):
    """[(name, decl_start, body_start, body_end)] for every class/struct in `text`."""
    out = []
    for m in CLASS_DECL.finditer(text):
        # Skip `class` in a generic constraint (`where T : class`).
        if text[max(0, m.start() - 2):m.start()].strip().endswith(':'):
            continue
        brace = text.find('{', m.end())
        if brace < 0:
            continue
        if ';' in text[m.end():brace]:
            continue
        end = match_brace(text, brace)
        if end is None:
            continue
        out.append((m.group(1), m.start(), brace, end))
    return out


def members_of(text, cls_name, body_start, body_end, want_created, want_create):
    """[(label, body_start, body_end)] for the ctor(s)/Created/Create of one class.

    Only members declared directly in this class body are returned; a nested type's members sit
    at a deeper brace depth and are skipped, which is what keeps a closure class or a nested
    helper from being mistaken for the trait's own constructor.
    """
    found = []
    names = []
    if want_created:
        names.append(('Created', re.compile(r"(?<![\w.])(?:[\w.]+\.)?Created\s*\(")))
        names.append((cls_name + '()', re.compile(r"(?<![\w.])" + re.escape(cls_name) + r"\s*\(")))
    if want_create:
        names.append(('Create', re.compile(r"(?<![\w.])(?:[\w.]+\.)?Create\s*\(")))

    for label, pat in names:
        for m in pat.finditer(text, body_start, body_end):
            # The member must be declared directly in this class body, not in a nested type.
            if text.count('{', body_start, m.start()) - text.count('}', body_start, m.start()) != 1:
                continue
            paren = text.find('(', m.start())
            if paren < 0:
                continue
            after_args = skip_parens(text, paren)
            if after_args is None:
                continue
            # `new Foo(...)` is a call, not a declaration.
            if text[max(body_start, m.start() - 4):m.start()].rstrip().endswith('new'):
                continue
            body = body_after(text, after_args)
            if body:
                found.append((label, body[0], body[1]))
    return found


def resolve_targets(text, index, info_name, bs, be):
    """[(class name, body_start, body_end, want_created, want_create)] reachable from one Info.

    `index` maps class name -> [(body_start, body_end)] within the SAME text. The project-wide
    version in scan() passes a path alongside; this single-text form is what the selftest uses.
    """
    targets = [(info_name, bs, be, False, True)]
    created = set()
    for _, cbs, cbe in members_of(text, info_name, bs, be, False, True):
        created.update(re.findall(r"\bnew\s+(\w+)\s*[(<]", text[cbs:cbe]))
    # Conventional fallback: FooInfo -> Foo, for an Info whose Create is inherited or written
    # in a shape the `new` scan above does not reach.
    if info_name.endswith("Info"):
        created.add(info_name[:-4])
    for cn in sorted(created):
        for cbs, cbe in index.get(cn, []):
            targets.append((cn, cbs, cbe, True, False))
    return targets


def world_infos_in(text):
    """[(name, body_start, body_end)] for classes carrying a world-located TraitLocation."""
    out = []
    for cn, decl, bs, be in classes_in(text):
        head_start = max(text.rfind('}', 0, decl), text.rfind(';', 0, decl),
                         text.rfind('{', 0, decl)) + 1
        for am in TRAIT_LOCATION.finditer(text[head_start:decl]):
            if WORLD_FLAG.search(am.group(1)):
                out.append((cn, bs, be))
                break
    return out


def scan():
    """(findings, n_infos, in_scope_member_keys, n_files)."""
    cleaned = {}
    class_index = {}      # class name -> [(path, body_start, body_end)]
    infos = []            # (path, name, body_start, body_end)

    for p in sorted(ENGINE.rglob("*.cs")):
        posix = p.as_posix()
        if "/obj/" in posix or "/bin/" in posix:
            continue
        raw = p.read_text(encoding="utf-8", errors="replace")
        if "class" not in raw:
            continue
        text = blank_noncode(raw)
        cleaned[p] = (raw, text)
        for cn, _, bs, be in classes_in(text):
            class_index.setdefault(cn, []).append((p, bs, be))
        for cn, bs, be in world_infos_in(text):
            infos.append((p, cn, bs, be))

    findings = []
    in_scope = set()
    for p, info_name, bs, be in infos:
        text = cleaned[p][1]
        local = {}
        for cn, cbs, cbe in [(a, b, c) for a, b, c in
                             ((n, s, e) for n, (q, s, e) in
                              ((n, t) for n, lst in class_index.items() for t in lst)
                              if q == p)]:
            local.setdefault(cn, []).append((cbs, cbe))

        targets = []
        for tn, tbs, tbe, wc, wcr in resolve_targets(text, local, info_name, bs, be):
            targets.append((p, tn, tbs, tbe, wc, wcr))
        # Cross-file: a trait class declared away from its Info.
        for tn, _, _, wc, wcr in resolve_targets(text, {}, info_name, bs, be)[1:]:
            for cp, cbs, cbe in class_index.get(tn, []):
                if cp != p:
                    targets.append((cp, tn, cbs, cbe, wc, wcr))

        for tp, tn, tbs, tbe, wc, wcr in targets:
            ttext = cleaned[tp][1]
            for label, mbs, mbe in members_of(ttext, tn, tbs, tbe, wc, wcr):
                rel = tp.relative_to(ROOT).as_posix()
                in_scope.add(f"{rel}:{tn}.{label}")
                for hit in WORLD_ACTOR.finditer(ttext, mbs, mbe):
                    line = ttext.count('\n', 0, hit.start()) + 1
                    findings.append((f"{rel}:{tn}.{label}", rel, line, info_name, tn, label,
                                     cleaned[tp][0].splitlines()[line - 1].strip()))

    seen = set()
    unique = []
    for f in sorted(findings, key=lambda x: (x[1], x[2])):
        if (f[1], f[2]) in seen:
            continue
        seen.add((f[1], f[2]))
        unique.append(f)

    return unique, len(infos), in_scope, len(cleaned)


ANY_MEMBER = re.compile(r"(?<![\w.])((?:[\w.]+\.)?[A-Z]\w*)\s*(?:<[^<>()]*>)?\s*\(")


def enclosing_members(text, cls_name, body_start, body_end):
    """[(label, s, e)] for EVERY method-like member declared directly in this class body.

    Used only by `report`, to name the member a given line sits in. `check` deliberately does
    not use this: it looks only for the three members that run during construction, and a
    looser member finder there would widen the gate by accident.
    """
    out = []
    for m in ANY_MEMBER.finditer(text, body_start, body_end):
        if text.count('{', body_start, m.start()) - text.count('}', body_start, m.start()) != 1:
            continue
        if text[max(body_start, m.start() - 4):m.start()].rstrip().endswith('new'):
            continue
        after = skip_parens(text, text.find('(', m.start()))
        if after is None:
            continue
        body = body_after(text, after)
        if body:
            out.append((m.group(1), body[0], body[1]))
    return out


def report():
    """Every .WorldActor inside a file that declares a world-located trait, with a verdict.

    This is the sweep, regenerable on demand. `check` answers "is the tree clean"; this
    answers "where does the engine touch WorldActor from a world trait's file, and why is
    each one fine" -- which is the question a reviewer actually has.
    """
    findings, _, _, _ = scan()
    violations = {(f[1], f[2]) for f in findings}

    rows = []
    for p in sorted(ENGINE.rglob("*.cs")):
        posix = p.as_posix()
        if "/obj/" in posix or "/bin/" in posix:
            continue
        raw = p.read_text(encoding="utf-8", errors="replace")
        if "TraitLocation" not in raw or "WorldActor" not in raw:
            continue
        text = blank_noncode(raw)
        if not world_infos_in(text):
            continue
        rel = p.relative_to(ROOT).as_posix()
        members = []
        for cn, _, bs, be in classes_in(text):
            for label, s, e in enclosing_members(text, cn, bs, be):
                members.append((cn, label, s, e))
        for hit in WORLD_ACTOR.finditer(text):
            line = text.count('\n', 0, hit.start()) + 1
            owner = min((m for m in members if m[2] <= hit.start() < m[3]),
                        key=lambda m: m[3] - m[2], default=None)
            where = "{}.{}".format(owner[0], owner[1]) if owner else "(file scope)"
            verdict = "VIOLATION" if (rel, line) in violations else "safe"
            rows.append((rel, line, where, verdict,
                         raw.splitlines()[line - 1].strip()))

    files = sorted({r[0] for r in rows})
    print("worldactor-gate report: {} code-level .WorldActor dereferences in {} files that "
          "declare a world-located trait.".format(len(rows), len(files)))
    print("  (comments and string literals are excluded; .WorldActorInfo is a different "
          "member and is never counted)")
    cur = None
    for rel, line, where, verdict, src in rows:
        if rel != cur:
            cur = rel
            print("\n" + rel)
        print("  {:>5}  {:<9} {:<46} {}".format(line, verdict, where, src[:90]))
    print("\n{} violation(s), {} safe.".format(
        sum(1 for r in rows if r[3] == "VIOLATION"), sum(1 for r in rows if r[3] == "safe")))
    return 2 if any(r[3] == "VIOLATION" for r in rows) else 0


def check():
    findings, n_infos, in_scope, n_files = scan()
    n_members = len(in_scope)
    live = [f for f in findings if f[0] not in ACCEPTED]

    print("worldactor-gate: {} source files, {} world-located TraitLocation attributes, "
          "{} constructor/Created/Create bodies in scope."
          .format(n_files, n_infos, n_members))
    if ACCEPTED:
        print("  {} accepted exception(s) on record.".format(len(ACCEPTED)))

    for _, rel, line, info, cls, label, src in live:
        print("  VIOLATION {}:{}".format(rel, line))
        print("            {}.{} runs during world construction (declared by {}), "
              "and World.WorldActor is null there.".format(cls, label, info))
        print("            {}".format(src))
        print("            Fix: use `self` -- it is the same actor and is always valid. "
              "If the read must wait, move it to IWorldLoaded.WorldLoaded.")

    if live:
        print("worldactor-gate: {} violation(s). See engine/OpenRA/World.cs:252."
              .format(len(live)))
        return 2
    print("worldactor-gate: clean.")
    return 0


# -- Negative fixtures -------------------------------------------------------------------
# A gate nobody has seen fail is not known to work. These are planted violations: the checker
# must fire on each RED case and stay silent on each GREEN one. GREEN is the half that matters
# most here -- the correct idiom outnumbers the broken one by roughly forty to one in this
# engine, so a matcher that cannot tell them apart is not a gate, it is noise.

RED = {
    "world trait reads WorldActor in Created": """
namespace X {
  [TraitLocation(SystemActors.World)]
  public class BadWallInfo : TraitInfo {
    public override object Create(ActorInitializer init) { return new BadWall(init.Self); }
  }
  public class BadWall : INotifyCreated {
    void INotifyCreated.Created(Actor self) {
      escalation = self.World.WorldActor.TraitOrDefault<DefconEscalation>();
    }
  }
}""",
    "world trait reads WorldActor in its constructor": """
namespace X {
  [TraitLocation(SystemActors.World)]
  public class BadCtorInfo : TraitInfo {
    public override object Create(ActorInitializer init) { return new BadCtor(init.Self); }
  }
  public class BadCtor {
    public BadCtor(Actor self) { foo = self.World.WorldActor.Owner; }
  }
}""",
    "Info.Create itself reads WorldActor": """
namespace X {
  [TraitLocation(SystemActors.World)]
  public class BadCreateInfo : TraitInfo {
    public override object Create(ActorInitializer init) {
      return new BadCreate(init.World.WorldActor.Owner);
    }
  }
  public class BadCreate { public BadCreate(Player p) { } }
}""",
    "combined flags still count as world-located": """
namespace X {
  [TraitLocation(SystemActors.World | SystemActors.EditorWorld)]
  public class BadBothInfo : TraitInfo {
    public override object Create(ActorInitializer init) { return new BadBoth(init.Self); }
  }
  public class BadBoth : INotifyCreated {
    void INotifyCreated.Created(Actor self) { x = self.World.WorldActor.Trait<Foo>(); }
  }
}""",
    "null-conditional is still a finding": """
namespace X {
  [TraitLocation(SystemActors.World)]
  public class BadSoftInfo : TraitInfo {
    public override object Create(ActorInitializer init) { return new BadSoft(init.Self); }
  }
  public class BadSoft : INotifyCreated {
    void INotifyCreated.Created(Actor self) { x = self.World.WorldActor?.Trait<Foo>(); }
  }
}""",
    "trait class declared before its Info still counts": """
namespace X {
  public class BadOrderFirst : INotifyCreated {
    void INotifyCreated.Created(Actor self) { x = self.World.WorldActor.Trait<Foo>(); }
  }
  [TraitLocation(SystemActors.World)]
  public class BadOrderFirstInfo : TraitInfo {
    public override object Create(ActorInitializer init) { return new BadOrderFirst(); }
  }
}""",
}

GREEN = {
    "the shipped fix: `self`, not self.World.WorldActor": """
namespace X {
  [TraitLocation(SystemActors.World)]
  public class GoodWallInfo : TraitInfo {
    public override object Create(ActorInitializer init) { return new GoodWall(init.Self); }
  }
  public class GoodWall : INotifyCreated {
    void INotifyCreated.Created(Actor self) { escalation = self.TraitOrDefault<Foo>(); }
  }
}""",
    "a PLAYER trait may read WorldActor in Created": """
namespace X {
  [TraitLocation(SystemActors.Player)]
  public class ObserverInfo : TraitInfo {
    public override object Create(ActorInitializer init) { return new Observer(init.Self); }
  }
  public class Observer : INotifyCreated {
    void INotifyCreated.Created(Actor self) { x = self.World.WorldActor.Trait<Foo>(); }
  }
}""",
    "an unattributed per-actor trait may read WorldActor in Created": """
namespace X {
  public class UnitThingInfo : TraitInfo {
    public override object Create(ActorInitializer init) { return new UnitThing(init.Self); }
  }
  public class UnitThing : INotifyCreated {
    void INotifyCreated.Created(Actor self) { x = self.World.WorldActor.Trait<Foo>(); }
  }
}""",
    "a world trait reading WorldActor in WorldLoaded is correct": """
namespace X {
  [TraitLocation(SystemActors.World)]
  public class LateInfo : TraitInfo {
    public override object Create(ActorInitializer init) { return new Late(); }
  }
  public class Late : IWorldLoaded {
    void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr) { x = w.WorldActor.Trait<Foo>(); }
  }
}""",
    "a world trait reading WorldActor in ITick is correct": """
namespace X {
  [TraitLocation(SystemActors.World)]
  public class TickerInfo : TraitInfo {
    public override object Create(ActorInitializer init) { return new Ticker(); }
  }
  public class Ticker : ITick {
    void ITick.Tick(Actor self) { x = self.World.WorldActor.Trait<Foo>(); }
  }
}""",
    "WorldActorInfo is map metadata, not the actor": """
namespace X {
  [TraitLocation(SystemActors.World)]
  public class MetaInfo : TraitInfo {
    public override object Create(ActorInitializer init) { return new Meta(init.Self); }
  }
  public class Meta : INotifyCreated {
    void INotifyCreated.Created(Actor self) {
      foreach (var t in self.World.Map.WorldActorInfo.TraitInfos<Foo>()) { }
    }
  }
}""",
    "a comment naming the trap is not the trap": """
namespace X {
  [TraitLocation(SystemActors.World)]
  public class CommentedInfo : TraitInfo {
    public override object Create(ActorInitializer init) { return new Commented(init.Self); }
  }
  public class Commented : INotifyCreated {
    void INotifyCreated.Created(Actor self) {
      // SELF, NOT self.World.WorldActor -- World.cs:252 has not assigned it yet.
      /* nor self.World.WorldActor here */
      var s = "self.World.WorldActor";
      escalation = self.TraitOrDefault<Foo>();
    }
  }
}""",
    "a lambda stored in Created runs later and is correct": """
namespace X {
  [TraitLocation(SystemActors.World)]
  public class DeferredInfo : TraitInfo {
    public override object Create(ActorInitializer init) { return new Deferred(init.Self); }
  }
  public class Deferred : INotifyCreated {
    void INotifyCreated.Created(Actor self) { later = () => self.TraitOrDefault<Foo>(); }
  }
}""",
}


def scan_source(src):
    """Run the same matcher over one in-memory source. The selftest's single-file form."""
    text = blank_noncode(src)
    index = {}
    for cn, _, bs, be in classes_in(text):
        index.setdefault(cn, []).append((bs, be))

    hits = []
    for info_name, bs, be in world_infos_in(text):
        for tn, tbs, tbe, wc, wcr in resolve_targets(text, index, info_name, bs, be):
            for label, mbs, mbe in members_of(text, tn, tbs, tbe, wc, wcr):
                for hit in WORLD_ACTOR.finditer(text, mbs, mbe):
                    hits.append((tn, label, text.count('\n', 0, hit.start()) + 1))
    return hits


def selftest():
    failures = []
    for name, src in RED.items():
        hits = scan_source(src)
        if not hits:
            failures.append("RED   not caught: " + name)
        else:
            print("  red   ok: {} -> {}.{} line {}".format(name, hits[0][0], hits[0][1], hits[0][2]))
    for name, src in GREEN.items():
        hits = scan_source(src)
        if hits:
            failures.append("GREEN false positive: {} -> {}".format(name, hits))
        else:
            print("  green ok: " + name)

    # Tripwire on the tree itself. The fixtures above prove the matcher can tell red from
    # green on synthetic input; this proves it is still POINTED AT the real thing. A gate
    # whose scope quietly collapses to nothing passes every fixture it has and reports a
    # clean tree forever, which is the failure mode that would have let DefconWall through
    # a second time. DefconWall.Created is the exact member the 2026-09-10 crash lived in,
    # so it is the one member that must never fall out of scope.
    canary = "engine/OpenRA.Mods.Common/Traits/World/DefconWall.cs:DefconWall.Created"
    if (ROOT / "engine" / "OpenRA.Mods.Common" / "Traits" / "World" / "DefconWall.cs").exists():
        in_scope = scan()[2]
        if canary not in in_scope:
            failures.append(
                "TRIPWIRE: {} is not in scope. The gate is no longer looking at the member "
                "the 2026-09-10 crash lived in, so a clean `check` proves nothing."
                .format(canary))
        else:
            print("  tree  ok: {} is in scope ({} members total)".format(canary, len(in_scope)))
    else:
        failures.append("TRIPWIRE: DefconWall.cs not found; cannot confirm the gate has scope.")

    if failures:
        print("selftest: FAILED")
        for f in failures:
            print("  " + f)
        return 1
    print("selftest: ok. {} planted violations caught, {} correct forms left alone, "
          "tree tripwire in scope.".format(len(RED), len(GREEN)))
    return 0


if __name__ == "__main__":
    cmd = sys.argv[1] if len(sys.argv) > 1 else "check"
    if cmd == "selftest":
        sys.exit(selftest())
    if cmd == "report":
        sys.exit(report())
    sys.exit(check())
