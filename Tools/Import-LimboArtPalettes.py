"""Accept two matching original-runtime captures and import immutable baked palettes."""
import hashlib
import json
import re
from pathlib import Path

P = Path(__file__).resolve().parents[1]
ROOT = P / 'Assets/_Project/Content/NetworkCombat/Limbo/Art'
plan = json.loads((ROOT / 'ArtSource.json').read_text(encoding='utf-8'))
index = {}
for meta in (P / 'Assets').rglob('*.meta'):
    m = re.search(r'^guid: (\w+)', meta.read_text(encoding='utf-8-sig', errors='ignore'), re.M)
    if m:
        index[m[1]] = Path(str(meta)[:-5])
pairs = []
for entry in plan['bakes']:
    captures = [P / 'Logs/LimboArt' / ('original-palette-' + key) / entry['file'] for key in ['a', 'b']]
    if captures[0].read_bytes() != captures[1].read_bytes():
        raise ValueError('Independent original captures differ: ' + entry['file'])
    source = Path(entry['sourceTexture'])
    guid = re.search(r'^guid: (\w+)', Path(str(source) + '.meta').read_text(encoding='utf-8-sig'), re.M)[1]
    original = index[guid]
    dest = ROOT / 'BakedPalettes' / entry['file']
    dest.parent.mkdir(parents=True, exist_ok=True)
    if not dest.exists():
        dest.write_bytes(captures[0].read_bytes())
        # Preserve source sampling/color settings; keep original sprite frames as
        # their own assets. The baked atlas is never re-sliced by filename.
        meta = Path(str(original) + '.meta').read_text(encoding='utf-8-sig')
        new_guid = hashlib.md5(('limbo-palette-v1:' + entry['file']).encode()).hexdigest()
        Path(str(dest) + '.meta').write_text(meta.replace('guid: ' + guid, 'guid: ' + new_guid), encoding='utf-8')
    pairs.append(dict(original=original.relative_to(P).as_posix(), lut=entry['lut'], baked=dest.relative_to(P).as_posix(),
                      sourceCaptures=[str(p) for p in captures], sha256=hashlib.sha256(captures[0].read_bytes()).hexdigest()))
(ROOT / 'BakedPalettes.json').write_text(json.dumps(dict(pairs=pairs), indent=2), encoding='utf-8')
print('Verified and imported', len(pairs), 'original shader outputs, two identical captures per atlas/LUT pair.')
