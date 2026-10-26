#!/usr/bin/env python3
"""
Erstellt das MSI-Paket ohne Windows mit msitools (wixl, msibuild, msiinfo).

Unter Windows wird stattdessen AcroClone.Setup.wixproj mit dem WiX Toolset 3 gebaut.
wixl unterstützt nicht alle WiX-Elemente; dieses Skript gleicht das aus:
  1. Präprozessor-Anweisungen auflösen (wxs_preprocess.py)
  2. Komponentenbedingungen und die "Programm starten"-Aktion vorübergehend entfernen
  3. Paket mit wixl erstellen
  4. Bedingungen und Aktion per SQL nachtragen
  5. Setup-Dialoge aus UI.wxs als Tabellen importieren (wxs_ui_to_idt.py)

Aufruf:  build_msi_linux.py PLATTFORM(x64|x86) AUSGABE.msi
Erwartet den Release-Build von AcroClone unter src/AcroClone/bin/Release.
"""
import os
import re
import subprocess
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
INSTALLER = os.path.dirname(HERE)
REPO = os.path.dirname(INSTALLER)


def run(*args, **kw):
    print('>', ' '.join(args))
    subprocess.check_call(list(args), **kw)


def main(platform, output):
    output = os.path.abspath(output)
    work = tempfile.mkdtemp(prefix='acroclone-msi-')
    pre = os.path.join(work, 'product.wxs')
    run(sys.executable, os.path.join(HERE, 'wxs_preprocess.py'), os.path.join(INSTALLER, 'Product.wxs'), pre,
        'Platform=' + platform, 'NoUI=1',
        'SourceDir=' + os.path.join(REPO, 'src', 'AcroClone', 'bin', 'Release'),
        'LicenseDir=' + os.path.join(REPO, 'lib', 'pdfium', 'licenses'),
        'IconFile=' + os.path.join(REPO, 'src', 'AcroClone', 'app.ico'))

    src = open(pre, encoding='utf-8').read()
    # Pfade mit "\" (Windows) für Linux umschreiben
    src = re.sub(r'(Source(?:File)?=")([^"]+)"', lambda m: m.group(1) + m.group(2).replace('\\', '/') + '"', src)
    # Komponentenbedingungen merken und entfernen
    conditions = []

    def strip_condition(m):
        body = m.group(2)
        c = re.search(r'\s*<Condition>(.*?)</Condition>', body, re.S)
        if c:
            conditions.append((m.group(1), c.group(1).strip()))
            return m.group(0).replace(c.group(0), '')
        return m.group(0)
    src = re.sub(r'<Component Id="([^"]+)"[^>]*>(.*?)</Component>', strip_condition, src, flags=re.S)
    # Benutzerdefinierte Aktionen mit Directory-Quelle merken und entfernen
    actions = []

    def strip_action(m):
        attrs = dict(re.findall(r'(\w+)="([^"]*)"', m.group(0)))
        if 'Directory' not in attrs:
            return m.group(0)
        ret = attrs.get('Return', 'check')
        flags = {'check': 0, 'ignore': 64, 'asyncWait': 128, 'asyncNoWait': 192}[ret]
        target = attrs['ExeCommand'].replace('&quot;', '"')
        actions.append((attrs['Id'], 34 + flags, attrs['Directory'], target))
        return ''
    src = re.sub(r'<CustomAction [^>]*/>', strip_action, src)
    src = src.replace(' CompressionLevel="high"', '')
    wixl_in = os.path.join(work, 'product-wixl.wxs')
    open(wixl_in, 'w', encoding='utf-8').write(src)

    if os.path.exists(output):
        os.remove(output)
    run('wixl', '-a', platform, '-o', output, wixl_in)

    queries = []
    for comp, cond in conditions:
        queries.append("UPDATE `Component` SET `Condition` = '%s' WHERE `Component` = '%s'" % (cond.replace("'", "''"), comp))
    for aid, atype, source, target in actions:
        queries.append("INSERT INTO `CustomAction` (`Action`, `Type`, `Source`, `Target`) VALUES ('%s', %d, '%s', '%s')"
                       % (aid, atype, source, target.replace("'", "''")))
    for q in queries:
        run('msibuild', output, '-q', q)

    # Dialoge importieren
    seq_idt = os.path.join(work, 'InstallUISequence.idt')
    with open(seq_idt, 'w', encoding='utf-8') as f:
        f.write(subprocess.check_output(['msiinfo', 'export', output, 'InstallUISequence']).decode('utf-8'))
    idt_dir = os.path.join(work, 'idt')
    run(sys.executable, os.path.join(HERE, 'wxs_ui_to_idt.py'), os.path.join(INSTALLER, 'UI.wxs'), seq_idt, idt_dir)
    tables = ['_ForceCodepage', 'Binary', 'TextStyle', 'Dialog', 'Control', 'ControlEvent', 'ControlCondition',
              'EventMapping', 'CheckBox', 'ActionText', 'UIText', 'InstallUISequence']
    args = ['msibuild', output]
    for t in tables:
        args += ['-i', t + '.idt']
    run(*args, cwd=idt_dir)
    with open(os.path.join(idt_dir, 'multiline.sql'), encoding='utf-8', newline='') as f:
        for q in f.read().split('\n--\n'):
            if q.strip():
                subprocess.check_call(['msibuild', output, '-q', q])
    # AdminUISequence: Abschlussdialoge
    for name, seq in (('ExitDlg', -1), ('UserExitDlg', -2), ('FatalErrorDlg', -3)):
        run('msibuild', output, '-q',
            "INSERT INTO `AdminUISequence` (`Action`, `Sequence`) VALUES ('%s', %d)" % (name, seq))
    # Zusammenfassung: deutsche Sprache im Paket kennzeichnen
    run('msibuild', output, '-s', 'AcroClone 9 Pro', 'AcroClone', platform.replace('x86', 'Intel') + ';1031')
    print('Fertig:', output)


if __name__ == '__main__':
    if len(sys.argv) != 3 or sys.argv[1] not in ('x64', 'x86'):
        print(__doc__)
        sys.exit(1)
    main(sys.argv[1], sys.argv[2])
