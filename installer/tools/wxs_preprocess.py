#!/usr/bin/env python3
"""
Minimaler WiX-Präprozessor für den Linux-Build (wixl verarbeitet <?ifndef?> nicht zuverlässig).
Unterstützt: <?define?>, <?ifdef?>, <?ifndef?>, <?if A = "B" ?>, <?else?>, <?endif?>, $(var.X).

Aufruf:  wxs_preprocess.py EINGABE.wxs AUSGABE.wxs NAME=WERT ...
"""
import re
import sys

PI = re.compile(r'<\?(define|ifdef|ifndef|if|else|endif)\b\s*(.*?)\s*\?>')
VAR = re.compile(r'\$\(var\.([A-Za-z0-9_]+)\)')


def substitute(text, variables):
    def repl(m):
        name = m.group(1)
        if name not in variables:
            raise SystemExit('Unbekannte Variable: ' + name)
        return variables[name]
    return VAR.sub(repl, text)


def evaluate(expr, variables):
    m = re.match(r'^(.+?)\s*(=|!=)\s*(.+)$', expr)
    if not m:
        raise SystemExit('Ausdruck nicht unterstützt: ' + expr)
    left = substitute(m.group(1).strip(), variables).strip('"')
    right = substitute(m.group(3).strip(), variables).strip('"')
    return (left == right) if m.group(2) == '=' else (left != right)


def process(src, variables):
    out = []
    stack = []  # (aktiv, Zweig bereits genommen)
    pos = 0
    for m in PI.finditer(src):
        active = all(a for a, _ in stack)
        if active:
            out.append(src[pos:m.start()])
        pos = m.end()
        kind, arg = m.group(1), m.group(2)
        if kind == 'define':
            if active:
                dm = re.match(r'^([A-Za-z0-9_]+)\s*=\s*"(.*)"$', arg)
                variables[dm.group(1)] = substitute(dm.group(2), variables)
        elif kind in ('ifdef', 'ifndef'):
            cond = (arg in variables) == (kind == 'ifdef')
            stack.append((cond, cond))
        elif kind == 'if':
            cond = evaluate(arg, variables) if active else False
            stack.append((cond, cond))
        elif kind == 'else':
            _, taken = stack.pop()
            stack.append((not taken, True))
        elif kind == 'endif':
            stack.pop()
    out.append(src[pos:])
    return substitute(''.join(out), variables)


def main():
    if len(sys.argv) < 3:
        print(__doc__)
        sys.exit(1)
    variables = {}
    for a in sys.argv[3:]:
        k, _, v = a.partition('=')
        variables[k] = v
    with open(sys.argv[1], encoding='utf-8-sig') as f:
        src = f.read()
    result = process(src, variables)
    # Leere Zeilen der entfernten Anweisungen zusammenfassen
    result = re.sub(r'\n[ \t]*\n(?:[ \t]*\n)+', '\n\n', result.replace('\r\n', '\n'))
    with open(sys.argv[2], 'w', encoding='utf-8') as f:
        f.write(result)


if __name__ == '__main__':
    main()
