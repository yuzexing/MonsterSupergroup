"""Extract immutable Ghoul references; leave an existing adaptation untouched."""
import hashlib
import json
import re
from pathlib import Path

root = Path(__file__).resolve().parents[1]
evidence = root/'docs/evidence/hellmaiden-attacks'
path = evidence/'runtime-assets.json'
graph = json.loads(path.read_text(encoding='utf-8-sig'))
objects = {o['id']: o for o in graph['objects']}
enemy = next(e for e in json.loads((evidence/'recovered-enemies.json').read_text(encoding='utf-8-sig'))['enemies'] if e['identity']=='Ghoul')
p = enemy['prefabs'][0]
def field(data, name):
    return next(v for k,v in data.get('fields',data).items() if k.split('::')[-1]==name)
attack = objects[p['attackId']]['data']
animator = objects[p['animatorId']]['data']
geometry=[]
for group, prefix in [('body',p['prefab']),('attack',field(attack,'attackPrefab')['path'])]:
    for o in objects.values():
        kind=o['type'].split('.')[-1]
        if kind not in ('Transform','CircleCollider2D','BoxCollider2D','PolygonCollider2D'): continue
        if o['path']!=prefix and not o['path'].startswith(prefix+'/'): continue
        relative=re.sub(r'\[\d+\]','',o['path'][len(prefix):].lstrip('/'))
        if group=='body' and relative not in ('','Collider','HurtBox'): continue
        row=dict(group=group,path=relative,type=kind,objectId=o['id'],**{k:v for k,v in o['data'].items() if k!='gameObject'})
        if 'paths' in row: row['polygons']=[dict(points=[dict(x=x,y=y) for x,y in polygon]) for polygon in row.pop('paths')]
        geometry.append(row)
data=dict(schemaVersion=1,sourceSha256=hashlib.sha256(path.read_bytes()).hexdigest(),identity='Ghoul',
    controllerId=p['controllerId'],attackId=p['attackId'],animatorId=p['animatorId'],
    cooldown=p['controller']['attackCooldown'],triggerDistance=p['controller']['attackDistance'],
    consecutiveAttacks=field(attack,'consecutiveAttacks'),
    areaSideWarpDistance={axis:field(field(attack,'areaSideWarpDistance'),axis) for axis in ('x','y','z')},
    extraStageTimes=p['extraStageTimes'],bindings=p['bindings'],attackSets=field(animator,'attackSets'),
    geometry=geometry,variants=enemy['databaseValues'],previewEnd=599.9666666666667,
    differences=['Whole combo uses absolute combat time; elapsed strikes are not replayed.',
        'Existing GAS/Mirror validates interrupts and routes them to the current simulator and replicas.',
        'Source third left warning uses right attack 2; logical duration follows source left bindings.',
        'Existing shader adaptation and Stats rebind; no source audio.'])
out=root/'Assets/_Project/Content/NetworkCombat/Limbo/Ghoul';out.mkdir(parents=True,exist_ok=True)
text=json.dumps(data,ensure_ascii=False,indent=2)+'\n'
(out/'GhoulSource.json').write_text(text,encoding='utf-8')
if not (out/'GhoulAdapted.json').exists(): (out/'GhoulAdapted.json').write_text(text,encoding='utf-8')
else:
    adapted_path=out/'GhoulAdapted.json'
    adapted=json.loads(adapted_path.read_text(encoding='utf-8'))
    # Migrate the earlier raw Vector3 representation without changing its adapted values.
    offset=adapted['areaSideWarpDistance']
    if 'fields' in offset:
        adapted['areaSideWarpDistance']={axis:field(offset,axis) for axis in ('x','y','z')}
        adapted_path.write_text(json.dumps(adapted,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print('Ghoul source extracted; adaptation preserved.')
