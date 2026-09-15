"""Transcribe LostSoul from the immutable runtime graph; preserve adaptation edits."""
import hashlib
import json
import re
from pathlib import Path

root = Path(__file__).resolve().parents[1]
evidence = root / 'docs/evidence/hellmaiden-attacks'
path = evidence / 'runtime-assets.json'
graph = json.loads(path.read_text(encoding='utf-8-sig'))
objects = {o['id']: o for o in graph['objects']}
enemy = next(e for e in json.loads((evidence / 'recovered-enemies.json').read_text(encoding='utf-8-sig'))['enemies'] if e['identity'] == 'LostSoul')
prefab = enemy['prefabs'][0]
def field(data, key):
    return next(v for k, v in data.items() if k.split('::')[-1] == key)
attack = objects[prefab['attackId']]['data']
prefixes = {'body': prefab['prefab'], 'explosion': field(attack, '_attackVFXPrefab')['path']}
geometry = []
for o in graph['objects']:
    kind = o['type'].split('.')[-1]
    if kind not in ('Transform', 'CircleCollider2D', 'BoxCollider2D', 'PolygonCollider2D'):
        continue
    for group, prefix in prefixes.items():
        if o['path'] != prefix and not o['path'].startswith(prefix + '/'):
            continue
        relative = re.sub(r'\[\d+\]', '', o['path'][len(prefix):].lstrip('/'))
        if relative not in ('', 'Collider', 'HurtBox', 'ExplosionPosition'):
            continue
        row = dict(group=group, path=relative, type=kind, objectId=o['id'], **{k: v for k, v in o['data'].items() if k != 'gameObject'})
        # JsonUtility does not support jagged arrays.
        if 'paths' in row:
            row['polygons'] = [dict(points=[dict(x=x, y=y) for x, y in points]) for points in row.pop('paths')]
        geometry.append(row)
bindings = [dict(field=n, sourceClip=b['clip'], length=b['length'], speed=b['speed'], fade=b['fadeDuration'], normalizedStart=str(b['normalizedStartTime']), eventTimes=[str(t) for t in field(b['events']['fields'], '_NormalizedTimes')]) for n,b in prefab['bindings'].items()]
data = dict(schemaVersion=1, sourceSha256=hashlib.sha256(path.read_bytes()).hexdigest(), identity='LostSoul',
    controllerId=prefab['controllerId'], attackId=prefab['attackId'], animatorId=prefab['animatorId'],
    cooldown=prefab['controller']['attackCooldown'], triggerDistance=prefab['controller']['attackDistance'], chaseSpeed=field(attack,'chaseSpeed'),
    extraStageTimes=prefab['extraStageTimes'], bindings=bindings, geometry=geometry, variants=enemy['databaseValues'],
    previewEnd=min(c['start'] for c in graph['clips'] if c['source']=='Ghoul'),
    differences=['Server-authoritative no-reward disposal replaces local Kill(true,false).',
        'Current Stats rebound on every borrow/reset/restore; cancellation cleans resources without entering another attack.',
        'Original visual dependencies with existing AllIn1 shader adaptation; no original audio.'])
out = root/'Assets/_Project/Content/NetworkCombat/Limbo/LostSoul'
out.mkdir(parents=True,exist_ok=True)
serialized=json.dumps(data,ensure_ascii=False,indent=2)+'\n'
(out/'LostSoulSource.json').write_text(serialized,encoding='utf-8')
if not (out/'LostSoulAdapted.json').exists(): (out/'LostSoulAdapted.json').write_text(serialized,encoding='utf-8')
print('LostSoul source extracted; existing adaptation preserved.')
