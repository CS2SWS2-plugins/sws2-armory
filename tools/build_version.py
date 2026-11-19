#!/usr/bin/env python3
"""Calculate versions from stable tags; preview tags never advance stable versions."""
import argparse
import os
import re
import subprocess
from pathlib import Path


def next_version(tags, bump='patch'):
    versions = [tuple(map(int, match.groups())) for tag in tags
                if (match := re.fullmatch(r'v(\d+)\.(\d+)\.(\d+)', tag))]
    if not versions:
        return '1.0.0'
    major, minor, patch = max(versions)
    if bump == 'major':
        return f'{major + 1}.0.0'
    if bump == 'minor':
        return f'{major}.{minor + 1}.0'
    return f'{major}.{minor}.{patch + 1}'


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--channel', choices=['stable', 'preview'], required=True)
    parser.add_argument('--bump', choices=['major', 'minor', 'patch'], default='patch')
    args = parser.parse_args()
    tags = subprocess.check_output(['git', 'tag', '-l'], text=True).splitlines()
    version = next_version(tags, args.bump)
    if args.channel == 'preview':
        run = os.environ.get('GITHUB_RUN_NUMBER', '1')
        pr = os.environ.get('PR_NUMBER', '')
        if not run.isdecimal() or pr and not pr.isdecimal():
            raise ValueError('Run and PR numbers must be numeric')
        version += f'-pr.{pr}.{run}' if pr else f'-dev.{run}'
    path = Path('src/VersionInfo.cs')
    source, count = re.subn(r'public const string Version = "[^"]+";',
                            f'public const string Version = "{version}";', path.read_text())
    if count != 1:
        raise ValueError('Expected exactly one plugin version constant')
    project = Path('sws2-armory.csproj')
    project_source, project_count = re.subn(r'<Version>[^<]+</Version>', f'<Version>{version}</Version>', project.read_text())
    if project_count != 1:
        raise ValueError('Expected exactly one project version property')
    path.write_text(source)
    project.write_text(project_source)
    if output := os.environ.get('GITHUB_OUTPUT'):
        with open(output, 'a') as stream:
            stream.write(f'version={version}\n')
    print(version)


if __name__ == '__main__':
    main()
