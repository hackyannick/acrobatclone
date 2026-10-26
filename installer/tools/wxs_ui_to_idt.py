#!/usr/bin/env python3
"""
Übersetzt den <UI>-Teil von UI.wxs in MSI-Tabellen (IDT-Dateien), damit die Dialoge
auch in ein mit wixl (msitools) gebautes Paket importiert werden können.
wixl selbst unterstützt eigene Dialoge nicht vollständig.

Aufruf:  wxs_ui_to_idt.py UI.wxs InstallUISequence.idt AUSGABEORDNER
  Erzeugt außerdem multiline.sql für Texte mit Zeilenumbrüchen (nicht per IDT importierbar).
  InstallUISequence.idt  vorhandene Sequenz (msiinfo export paket.msi InstallUISequence),
                         die Dialoge werden darin eingereiht.
Unter Windows mit dem WiX Toolset wird dieses Skript nicht benötigt.
"""
import os
import shutil
import sys
import xml.etree.ElementTree as ET

NS = '{http://schemas.microsoft.com/wix/2006/wi}'

# Control-Attribute (msidbControlAttributes*)
VISIBLE, ENABLED, SUNKEN, INDIRECT, INTEGER = 0x1, 0x2, 0x4, 0x8, 0x10
TRANSPARENT, NOPREFIX = 0x10000, 0x20000
PROGRESS_BLOCKS, SORTED = 0x10000, 0x10000
FIXED_VOLUME, REMOTE_VOLUME = 0x20000, 0x40000
ELEVATION_SHIELD = 0x800000
# Dialog-Attribute (msidbDialogAttributes*)
D_VISIBLE, D_MODAL, D_MINIMIZE, D_KEEPMODELESS, D_TRACKDISK, D_ERROR = 0x1, 0x2, 0x4, 0x10, 0x20, 0x10000

TAB_SKIP_DEFAULT = {'Text', 'Line', 'Bitmap', 'Icon', 'GroupBox', 'ProgressBar'}


def yes(el, name):
    return el.get(name, 'no') == 'yes'


def esc(v):
    if v is None:
        return ''
    return str(v).replace('\r\n', '\x11').replace('\n', '\x11').replace('\t', '\x10')


def write_idt(folder, name, columns, types, keys, rows):
    path = os.path.join(folder, name + '.idt')
    with open(path, 'w', encoding='utf-8', newline='') as f:
        f.write('\t'.join(columns) + '\r\n')
        f.write('\t'.join(types) + '\r\n')
        f.write('\t'.join([name] + keys) + '\r\n')
        for r in rows:
            f.write('\t'.join(esc(v) for v in r) + '\r\n')
    return path


def main(ui_path, sequence_idt, out):
    base = os.path.dirname(os.path.abspath(ui_path))
    os.makedirs(out, exist_ok=True)
    root = ET.parse(ui_path).getroot()
    frag = root.find(NS + 'Fragment')
    ui = frag.find(NS + 'UI')

    # Binärdaten (Bitmaps)
    binary_rows = []
    bin_dir = os.path.join(out, 'Binary')
    os.makedirs(bin_dir, exist_ok=True)
    for b in frag.findall(NS + 'Binary'):
        bid = b.get('Id')
        shutil.copyfile(os.path.join(base, b.get('SourceFile')), os.path.join(bin_dir, bid + '.ibd'))
        binary_rows.append([bid, bid + '.ibd'])

    text_styles, dialogs, controls, events, conditions, mappings, checkboxes = [], [], [], [], [], [], {}
    multiline = []
    for ts in ui.findall(NS + 'TextStyle'):
        color = None
        if ts.get('Red') or ts.get('Green') or ts.get('Blue'):
            color = int(ts.get('Red', 0)) + int(ts.get('Green', 0)) * 256 + int(ts.get('Blue', 0)) * 65536
        bits = (1 if yes(ts, 'Bold') else 0) | (2 if yes(ts, 'Italic') else 0) | (4 if yes(ts, 'Underline') else 0)
        text_styles.append([ts.get('Id'), ts.get('FaceName'), ts.get('Size'), color if color is not None else '', bits or ''])

    for d in ui.findall(NS + 'Dialog'):
        did = d.get('Id')
        attrs = 0 if yes(d, 'Hidden') else D_VISIBLE
        if not yes(d, 'Modeless'):
            attrs |= D_MODAL
        if not yes(d, 'NoMinimize'):
            attrs |= D_MINIMIZE
        if yes(d, 'KeepModeless'):
            attrs |= D_KEEPMODELESS
        if yes(d, 'TrackDiskSpace'):
            attrs |= D_TRACKDISK
        if yes(d, 'ErrorDialog'):
            attrs |= D_ERROR
        ctrl_list = d.findall(NS + 'Control')
        tab = []
        default = cancel = ''
        for c in ctrl_list:
            ctype = c.get('Type')
            skip = c.get('TabSkip')
            is_skip = (skip == 'yes') if skip is not None else (ctype in TAB_SKIP_DEFAULT)
            if not is_skip:
                tab.append(c.get('Id'))
            if yes(c, 'Default'):
                default = c.get('Id')
            if yes(c, 'Cancel'):
                cancel = c.get('Id')
        next_of = {}
        for i, cid in enumerate(tab):
            next_of[cid] = tab[(i + 1) % len(tab)] if len(tab) > 1 else ''
        dialogs.append([did, 50, 50, d.get('Width'), d.get('Height'), attrs, d.get('Title', ''),
                        tab[0] if tab else '', default, cancel])

        for c in ctrl_list:
            cid, ctype = c.get('Id'), c.get('Type')
            a = 0 if yes(c, 'Hidden') else VISIBLE
            if not yes(c, 'Disabled'):
                a |= ENABLED
            if yes(c, 'Sunken'):
                a |= SUNKEN
            if yes(c, 'Indirect'):
                a |= INDIRECT
            if yes(c, 'Integer'):
                a |= INTEGER
            if yes(c, 'Transparent'):
                a |= TRANSPARENT
            if yes(c, 'NoPrefix'):
                a |= NOPREFIX
            if yes(c, 'ProgressBlocks'):
                a |= PROGRESS_BLOCKS
            if yes(c, 'Sorted'):
                a |= SORTED
            if yes(c, 'Fixed'):
                a |= FIXED_VOLUME
            if yes(c, 'Remote'):
                a |= REMOTE_VOLUME
            if yes(c, 'ElevationShield'):
                a |= ELEVATION_SHIELD
            if ctype in ('Line', 'Bitmap', 'ProgressBar'):
                a &= ~ENABLED if ctype == 'Line' else ~0
            text = c.get('Text')
            t_el = c.find(NS + 'Text')
            if t_el is not None:
                if t_el.get('SourceFile'):
                    with open(os.path.join(base, t_el.get('SourceFile')), encoding='cp1252') as fh:
                        text = fh.read()
                else:
                    text = t_el.text or ''
            if text and ctype == 'ScrollableText':
                text = text.replace('\r\n', '').replace('\n', '')
            elif text and '\n' in text:
                if "'" in text:
                    raise SystemExit('Mehrzeiliger Text darf kein Apostroph enthalten: ' + did + '/' + cid)
                multiline.append((did, cid, text.replace('\r\n', '\n').replace('\n', '\r\n')))
                text = text.replace('\r\n', ' ').replace('\n', ' ')
            prop = c.get('Property', '')
            if c.get('CheckBoxValue') and prop:
                checkboxes[prop] = c.get('CheckBoxValue')
            controls.append([did, cid, ctype, c.get('X'), c.get('Y'), c.get('Width'), c.get('Height'), a,
                             prop, text or '', next_of.get(cid, ''), ''])
            order = 0
            for p in c.findall(NS + 'Publish'):
                order += 1
                o = p.get('Order', order)
                cond = (p.text or '1').strip() or '1'
                if p.get('Event'):
                    events.append([did, cid, p.get('Event'), p.get('Value', ''), cond, o])
                else:
                    events.append([did, cid, '[' + p.get('Property') + ']', p.get('Value', '{}'), cond, o])
            for cnd in c.findall(NS + 'Condition'):
                action = cnd.get('Action')
                conditions.append([did, cid, action[0].upper() + action[1:], (cnd.text or '').strip()])
            for s in c.findall(NS + 'Subscribe'):
                mappings.append([did, cid, s.get('Event'), s.get('Attribute')])

    action_text = [[p.get('Action'), (p.text or '').strip(), p.get('Template', '')] for p in ui.findall(NS + 'ProgressText')]
    ui_text = [[t.get('Id'), (t.text or '')] for t in ui.findall(NS + 'UIText')]

    # Dialoge in die Installationssequenz einreihen
    with open(sequence_idt, encoding='utf-8') as fh:
        lines = [l.rstrip('\r\n') for l in fh if l.strip()]
    seq = {}
    seq_cond = {}
    for l in lines[3:]:
        parts = l.split('\t')
        seq[parts[0]] = int(parts[2])
        seq_cond[parts[0]] = parts[1]
    used = set(seq.values())
    pending = ui.find(NS + 'InstallUISequence').findall(NS + 'Show')
    exit_codes = {'success': -1, 'cancel': -2, 'error': -3}
    while pending:
        progressed = False
        for s in list(pending):
            name, cond = s.get('Dialog'), (s.text or '').strip()
            n = None
            if s.get('OnExit'):
                n = exit_codes[s.get('OnExit')]
            elif s.get('Sequence'):
                n = int(s.get('Sequence'))
            elif s.get('Before') in seq:
                n = seq[s.get('Before')] - 1
                while n in used:
                    n -= 1
            elif s.get('After') in seq:
                n = seq[s.get('After')] + 1
                while n in used:
                    n += 1
            if n is None:
                continue
            seq[name] = n
            seq_cond[name] = cond
            used.add(n)
            pending.remove(s)
            progressed = True
        if not progressed:
            raise SystemExit('Sequenz nicht auflösbar: ' + ', '.join(s.get('Dialog') for s in pending))

    write_idt(out, 'InstallUISequence', ['Action', 'Condition', 'Sequence'], ['s72', 'S255', 'I2'], ['Action'],
              sorted(([k, seq_cond.get(k, ''), v] for k, v in seq.items()), key=lambda r: r[2]))
    write_idt(out, 'Binary', ['Name', 'Data'], ['s72', 'v0'], ['Name'], binary_rows)
    write_idt(out, 'TextStyle', ['TextStyle', 'FaceName', 'Size', 'Color', 'StyleBits'],
              ['s72', 's32', 'i2', 'I4', 'I2'], ['TextStyle'], text_styles)
    write_idt(out, 'Dialog', ['Dialog', 'HCentering', 'VCentering', 'Width', 'Height', 'Attributes', 'Title',
                              'Control_First', 'Control_Default', 'Control_Cancel'],
              ['s72', 'i2', 'i2', 'i2', 'i2', 'I4', 'L128', 's50', 'S50', 'S50'], ['Dialog'], dialogs)
    write_idt(out, 'Control', ['Dialog_', 'Control', 'Type', 'X', 'Y', 'Width', 'Height', 'Attributes', 'Property',
                               'Text', 'Control_Next', 'Help'],
              ['s72', 's50', 's20', 'i2', 'i2', 'i2', 'i2', 'I4', 'S72', 'L0', 'S50', 'L50'], ['Dialog_', 'Control'], controls)
    write_idt(out, 'ControlEvent', ['Dialog_', 'Control_', 'Event', 'Argument', 'Condition', 'Ordering'],
              ['s72', 's50', 's50', 's255', 'S255', 'I2'], ['Dialog_', 'Control_', 'Event', 'Argument', 'Condition'], events)
    write_idt(out, 'ControlCondition', ['Dialog_', 'Control_', 'Action', 'Condition'],
              ['s72', 's50', 's50', 's255'], ['Dialog_', 'Control_', 'Action', 'Condition'], conditions)
    write_idt(out, 'EventMapping', ['Dialog_', 'Control_', 'Event', 'Attribute'],
              ['s72', 's50', 's50', 's50'], ['Dialog_', 'Control_', 'Event'], mappings)
    write_idt(out, 'CheckBox', ['Property', 'Value'], ['s72', 'S64'], ['Property'], [[k, v] for k, v in checkboxes.items()])
    write_idt(out, 'ActionText', ['Action', 'Description', 'Template'], ['s72', 'L0', 'L0'], ['Action'], action_text)
    write_idt(out, 'UIText', ['Key', 'Text'], ['s72', 'L255'], ['Key'], ui_text)
    # Mehrzeilige Texte lassen sich nicht per IDT importieren: SQL-Anweisungen, getrennt durch eine Zeile '--'
    with open(os.path.join(out, 'multiline.sql'), 'w', encoding='utf-8', newline='') as f:
        for did, cid, text in multiline:
            f.write("UPDATE `Control` SET `Text` = '%s' WHERE `Dialog_` = '%s' AND `Control` = '%s'\n--\n" % (text, did, cid))
    with open(os.path.join(out, '_ForceCodepage.idt'), 'w', newline='') as f:
        f.write('\r\n\r\n1252\t_ForceCodepage\r\n')
    print('Dialoge: %d, Steuerelemente: %d, Ereignisse: %d' % (len(dialogs), len(controls), len(events)))


if __name__ == '__main__':
    if len(sys.argv) != 4:
        print(__doc__)
        sys.exit(1)
    main(sys.argv[1], sys.argv[2], sys.argv[3])
