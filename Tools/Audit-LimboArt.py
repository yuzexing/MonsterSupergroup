"""Read-only dependency/hash audit; emits evidence outside source assets."""
import collections
import hashlib
import json
import re
from pathlib import Path

project = Path(__file__).resolve().parents[1]
root = project / 'Assets/_Project/Content/NetworkCombat/Limbo/Art'
manifest = json.loads((root / 'ArtSource.json').read_text(encoding='utf-8'))
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
rows = []
for entry in manifest['entries']:
    source, target = Path(entry['source']), project / entry['destination']
    rows.append(dict(**entry, sourceUnchanged=sha(source) == entry['sourceSha256'],
                     targetExists=target.exists(), targetSha256=sha(target) if target.exists() else None))
bindings = []
for name in ['Slime Path Show', 'Slime Path fadeout', 'MeleeWarning_BuildUp', 'MeleeWarning_BuildDown', 'Buildup Lost Soul', 'Fadeout2']:
    entry = next(e for e in rows if Path(e['source']).name == name+'.anim')
    def material_ids(path):
        return sorted(int(x) for x in re.findall(r'attribute: (\d+)\s', path.read_text(encoding='utf-8-sig')) if int(x) in (0x8b19faf2,0x8d9d63da))
    original, adapted = material_ids(Path(entry['source'])), material_ids(project/entry['destination'])
    bindings.append(dict(clip=name, original=original, adapted=adapted, match=original==adapted))
evidence_unchanged = sha(Path(manifest['sourceEvidence'])) == manifest['sourceSha256']
result = dict(schemaVersion=1, dependencyCount=len(rows), treatments=dict(collections.Counter(e['treatment'] for e in rows)),
              extensions=dict(collections.Counter(Path(e['source']).suffix for e in rows)),
              sourceEvidenceUnchanged=evidence_unchanged, allSourceFilesUnchanged=all(e['sourceUnchanged'] for e in rows),
              allTargetsPresent=all(e['targetExists'] for e in rows), materialBindings=bindings,
              note='Static checks only. Resource completeness does not imply rendered or gameplay validation.', entries=rows)
out = project/'Logs/LimboArt/dependency-audit.json'; out.parent.mkdir(parents=True, exist_ok=True)
out.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
print(json.dumps({k:v for k,v in result.items() if k not in ['entries','materialBindings']}, ensure_ascii=False))
if not evidence_unchanged or not result['allSourceFilesUnchanged'] or not result['allTargetsPresent'] or not all(b['match'] for b in bindings):
    raise SystemExit(1)
