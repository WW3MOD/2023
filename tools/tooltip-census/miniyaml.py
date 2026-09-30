import os, re, sys

def parse(path):
    """Parse a MiniYaml file into a list of (key, value, children) top-level nodes."""
    lines = open(path, encoding='utf-8').read().split('\n')
    root = []
    stack = [(-1, root)]
    for raw in lines:
        if not raw.strip() or raw.lstrip().startswith('#'):
            continue
        # tabs only in this codebase
        indent = len(raw) - len(raw.lstrip('\t'))
        text = raw.strip()
        if '#' in text:
            # strip trailing comment only if preceded by whitespace
            m = re.search(r'\s+#', text)
            if m:
                text = text[:m.start()].strip()
        if not text:
            continue
        if ':' in text:
            k, _, v = text.partition(':')
            k = k.strip(); v = v.strip()
        else:
            k = text; v = ''
        node = (k, v, [])
        while stack and stack[-1][0] >= indent:
            stack.pop()
        stack[-1][1].append(node)
        stack.append((indent, node[2]))
    return root

def load_rules(files):
    """Merge top-level actor definitions across files. Later files merge into earlier."""
    actors = {}
    for f in files:
        for k, v, ch in parse(f):
            if k.startswith('-'):
                continue
            actors.setdefault(k, {'name': k, 'value': v, 'traits': []})
            actors[k]['traits'].extend(ch)
    return actors
