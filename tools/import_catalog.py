#!/usr/bin/env python3
"""Import an explicitly checked out AstraSkins snapshot; never fetch at runtime."""
import argparse
import hashlib
import json
import pathlib
import subprocess
from collections import Counter

WEAPONS = dict(zip(
    'deagle elite fiveseven glock ak47 aug awp famas g3sg1 galilar m249 m4a1 mac10 p90 mp5sd ump45 xm1014 bizon mag7 negev sawedoff tec9 hkp2000 mp7 mp9 nova p250 scar20 sg556 ssg08 m4a1_silencer usp_silencer cz75a revolver'.split(),
    [1, 2, 3, 4, 7, 8, 9, 10, 11, 13, 14, 16, 17, 19, 23, 24, 25, 26, 27, 28, 29, 30, 32, 33, 34, 35, 36, 38, 39, 40, 60, 61, 63, 64]))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source', type=pathlib.Path, help='AstraSkins checkout directory')
    args = parser.parse_args()
    destination = pathlib.Path(__file__).resolve().parents[1] / 'resources/catalog'
    entries = []
    hashes = {}
    files = [('weapons', 'Weapon'), ('knives', 'Knife'), ('gloves', 'Glove'),
             ('agents', 'Agent'), ('music_kits', 'Music'), ('stickers', 'Sticker'), ('keychains', 'Charm')]
    for filename, kind in files:
        path = args.source / 'data' / (filename + '.json')
        hashes[path.name] = hashlib.sha256(path.read_bytes()).hexdigest()
        for parent in json.loads(path.read_text(encoding='utf-8-sig')):
            if not parent.get('enabled', True):
                continue
            if kind in ('Weapon', 'Knife', 'Glove'):
                target = parent.get('entityName', parent['id'] if 'id' in parent else '') if kind == 'Weapon' else kind.lower()
                definition = WEAPONS[target.removeprefix('weapon_')] if kind == 'Weapon' else parent['itemDefinitionIndex']
                for skin in parent['skins']:
                    if not skin.get('enabled', True):
                        continue
                    entries.append(dict(id=f'{kind.lower()}/{skin["id"]}', kind=kind, target=target,
                                        group=parent.get('displayNameZh', parent['displayName']),
                                        name=skin['displayName'], nameZh=skin.get('displayNameZh'),
                                        definitionIndex=definition, kit=skin['paintKit'], wear=skin['wear'],
                                        seed=skin['seed'], legacyModel=skin.get('legacyModel', False)))
            else:
                entry = dict(id=f'{kind.lower()}/{parent["id"]}', kind=kind,
                             target=parent['team'] if kind == 'Agent' else kind.lower(),
                             group=parent.get('groupZh') or parent.get('group') or kind,
                             name=parent['displayName'], nameZh=parent.get('displayNameZh'))
                if kind == 'Agent':
                    entry.update(model=parent['model'], definitionIndex=parent['itemDefinitionIndex'],
                                 voicePrefix=parent.get('voicePrefix'),
                                 femaleVoice=parent.get('hasFemaleVoice') if parent.get('hasFemaleVoice') is not None else '_fem' in (parent.get('voicePrefix') or ''))
                else:
                    entry['kit'] = parent[{'Music': 'musicKit', 'Sticker': 'stickerId', 'Charm': 'keychainId'}[kind]]
                entries.append(entry)
    if len({e['id'] for e in entries}) != len(entries):
        raise ValueError('Duplicate IDs in source catalog')
    destination.mkdir(parents=True, exist_ok=True)
    catalog = destination / 'items.json'
    catalog.write_text(json.dumps(entries, ensure_ascii=False, indent=2) + '\n')
    commit = subprocess.check_output(['git', '-C', str(args.source), 'rev-parse', 'HEAD'], text=True).strip()
    manifest = dict(source='https://github.com/Ayrton09/AstraSkins', commit=commit,
                    license='MIT', counts=dict(Counter(e['kind'] for e in entries)),
                    inputSha256=hashes, catalogSha256=hashlib.sha256(catalog.read_bytes()).hexdigest())
    (destination / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n')
    print(json.dumps(manifest['counts']))


if __name__ == '__main__':
    main()
