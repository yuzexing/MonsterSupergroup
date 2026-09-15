"""Archive explicitly selected runs without changing the source recordings.

This collects evidence, not acceptance decisions. Failed and partial runs must
remain identified as such in the accompanying human-reviewed report.
"""
import argparse
import hashlib
import json
import zipfile
from pathlib import Path

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('output', type=Path)
parser.add_argument('runs', nargs='+')
parser.add_argument('--build', default='Builds/LimboReference', help='Actual build directory used by these runs')
args = parser.parse_args()
project = Path(__file__).resolve().parents[1]
recordings = project / 'Logs/LimboReference'
folders = []
for name in args.runs:
    folder = (recordings / name).resolve()
    if folder.parent != recordings.resolve() or not folder.is_dir():
        parser.error(f'Expected an existing run directory name: {name}')
    folders.append(folder)
output = args.output.resolve()
if output.exists():
    parser.error('Choose a new archive path; previous evidence is never overwritten.')
output.mkdir(parents=True)

def digest(path):
    sha = hashlib.sha256()
    with path.open('rb') as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b''):
            sha.update(chunk)
    return sha.hexdigest()

manifest = {'meaning': 'Evidence archive only; inclusion does not imply a passed run.',
            'runs': args.runs, 'files': [], 'provenance': []}
with zipfile.ZipFile(output / 'recordings.zip', 'x', zipfile.ZIP_DEFLATED, compresslevel=6) as archive:
    for folder in folders:
        for path in sorted(folder.rglob('*')):
            if 'invalid' in path.name.lower():
                continue  # Occluded screenshots are retained in the run, not offered as graphical proof.
            if not path.is_file() or path.suffix.lower() not in ('.jsonl', '.json', '.csv', '.log', '.png', '.jpg', '.xml'):
                continue
            name = path.relative_to(recordings).as_posix()
            archive.write(path, name)
            manifest['files'].append({'path': name, 'bytes': path.stat().st_size, 'sha256': digest(path)})
for relative in ('docs/evidence/hellmaiden-attacks/runtime-assets.json',
                 args.build + '/MonsterSupergroupLimbo_Data/Managed/MonsterSupergroup.NetworkCombat.dll',
                 args.build + '/MonsterSupergroupLimbo_Data/Managed/MonsterSupergroup.Gameplay.Combat.dll',
                 args.build + '/MonsterSupergroupLimbo_Data/Managed/MonsterSupergroup.Gameplay.Combat.Runtime.dll',
                 'Assets/_Project/Content/NetworkCombat/Limbo/Dash/DashSource.json',
                 'Assets/_Project/Content/NetworkCombat/Limbo/Dash/DashAdapted.json',
                 'Assets/_Project/Content/NetworkCombat/Limbo/Limbo.playable',
                 'Assets/_Project/Content/NetworkCombat/Limbo/Stage2/Stage2Adapted.json'):
    path = project / relative
    if path.is_file():
        manifest['provenance'].append({'path': relative, 'sha256': digest(path)})
manifest['archiveSha256'] = digest(output / 'recordings.zip')
(output / 'manifest.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
print(json.dumps({'output': str(output), 'files': len(manifest['files']),
                  'bytes': (output / 'recordings.zip').stat().st_size}, ensure_ascii=False))
