#!/usr/bin/env python3
"""Check the installation ZIP, catalog provenance, translations and version rules."""
import hashlib
import json
import re
import sys
import zipfile
from pathlib import Path
from build_version import next_version


def main():
    root = Path(__file__).resolve().parents[1]
    catalog_path = root / 'resources/catalog/items.json'
    entries = json.loads(catalog_path.read_text())
    manifest = json.loads((catalog_path.parent / 'manifest.json').read_text())
    assert hashlib.sha256(catalog_path.read_bytes()).hexdigest() == manifest['catalogSha256']
    for kind, count in manifest['counts'].items():
        assert sum(e['kind'] == kind for e in entries) == count, kind
    assert next_version([]) == '1.0.0'
    assert next_version(['v1.0.9', 'v1.0.10', 'v9.0.0-pr.1.1', 'malformed']) == '1.0.11'
    assert next_version(['v2.3.4'], 'minor') == '2.4.0'
    assert next_version(['v2.3.4'], 'major') == '3.0.0'
    translations = [json.loads(p.read_text()) for p in (root / 'resources/translations').glob('*.jsonc')]
    for translation in translations:
        assert translation.keys() == translations[0].keys()
        for key, value in translation.items():
            assert set(re.findall(r'\{\d+\}', value)) == set(re.findall(r'\{\d+\}', translations[0][key])), key
    used = set()
    for path in (root / 'src').rglob('*.cs'):
        used.update(re.findall(r'"((?:error|usage|menu|agent)\.[a-z_]+)"', path.read_text()))
    for translation in translations:
        assert used <= translation.keys(), used - translation.keys()
    if len(sys.argv) > 1:
        with zipfile.ZipFile(sys.argv[1]) as package:
            names = set(package.namelist())
            required = ['sws2-armory.dll', 'sws2-armory.deps.json',
                        'Sws2Cs2Menus.Contracts.dll', 'resources/exports/Sws2Cs2Menus.Contracts.dll',
                        'resources/catalog/items.json', 'resources/catalog/manifest.json',
                        'resources/templates/config.template.jsonc',
                        'resources/translations/en.jsonc', 'resources/translations/zh-CN.jsonc',
                        'licenses/AstraSkins-MIT.txt', 'THIRD-PARTY-NOTICES.md']
            for name in required:
                assert 'sws2-armory/' + name in names, name
            version = re.search(r'public const string Version = "([^"]+)";', (root / 'src/VersionInfo.cs').read_text()).group(1)
            dependencies = json.loads(package.read('sws2-armory/sws2-armory.deps.json'))
            assert f'sws2-armory/{version}' in dependencies['libraries'], 'Dependency metadata version must match plugin version'
            assert all(n.startswith('sws2-armory/') for n in names)
            assert not any(n.endswith('SwiftlyS2.Shared.dll') or n.endswith('SwiftlyS2.Core.dll') for n in names)
            assert package.testzip() is None
    print(f'Catalog, translations, version rules and package checks passed ({len(entries)} cosmetics).')


if __name__ == '__main__':
    main()
