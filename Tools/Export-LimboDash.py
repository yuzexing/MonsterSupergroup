"""Transcribe recovered Dash numbers and bindings; never overwrite evidence or adaptation."""
import hashlib
import json
import re
from pathlib import Path

root = Path(__file__).resolve().parents[1]
evidence = root / 'docs/evidence/hellmaiden-attacks'
graph_path = evidence / 'runtime-assets.json'
graph = json.loads(graph_path.read_text(encoding='utf-8'))
objects = {o['id']: o for o in graph['objects']}
enemy = next(e for e in json.loads((evidence / 'recovered-enemies.json').read_text(encoding='utf-8'))['enemies'] if e['identity'] == 'Brotchi_Dash')
prefab = enemy['prefabs'][0]

def field(data, key):
    return next(v for k, v in data.items() if k.rsplit('::', 1)[-1] == key)

attack = objects[prefab['attackId']]['data']
controller = objects[prefab['controllerId']]['data']
bindings = []
for name, b in prefab['bindings'].items():
    bindings.append(dict(field=name, sourceClip=b['clip'], length=b['length'], speed=b['speed'],
        fade=b['fadeDuration'], normalizedStart=str(b['normalizedStartTime']),
        eventTimes=[str(t) for t in field(b['events']['fields'], '_NormalizedTimes')]))
geometry = []
arrow = field(attack, 'attackPrefab')['path']
for obj in graph['objects']:
    prefix = next((p for p in (prefab['prefab'], arrow) if obj['path'] == p or obj['path'].startswith(p + '/')), None)
    kind = obj['type'].split('.')[-1]
    if prefix is None or kind not in ('Transform', 'CircleCollider2D', 'BoxCollider2D', 'PolygonCollider2D'):
        continue
    path = re.sub(r'\[\d+\]', '', obj['path'][len(prefix):].lstrip('/'))
    if prefix != arrow and path not in ('', 'Collider', 'HurtBox', 'AttackCollider'):
        continue
    geometry.append(dict(objectId=obj['id'], arrow=prefix == arrow, path=path, type=kind,
        **{k: v for k, v in obj['data'].items() if k != 'gameObject'}))
source_prefab = Path('F:/DecomplieLatest/HellMaiden/ExportedProject/Assets/GameObject/Enemy_Brotchi_LVL2.prefab')
mask = int(re.search(r'dashExclusionLayerMask:\s+serializedVersion: 2\s+m_Bits: (\d+)', source_prefab.read_text(encoding='utf-8')).group(1))
native = source_prefab.read_text(encoding='utf-8').split('Rigidbody2D:', 1)[1].split('--- !u!', 1)[0]
physics = {key: float(re.search(r'  m_' + source + r': ([^\n]+)', native).group(1))
           for key, source in [('mass', 'Mass'), ('linearDamping', 'LinearDamping'), ('angularDamping', 'AngularDamping'), ('gravityScale', 'GravityScale')]}
physics['constraints'] = int(re.search(r'  m_Constraints: (\d+)', native).group(1))
data = dict(schemaVersion=1, sourceSha256=hashlib.sha256(graph_path.read_bytes()).hexdigest(),
    identity='Brotchi_Dash', controllerId=prefab['controllerId'], attackId=prefab['attackId'], animatorId=prefab['animatorId'],
    cooldown=prefab['controller']['attackCooldown'], triggerDistance=prefab['controller']['attackDistance'],
    stopForAttack=field(controller, 'stopForAttack'), extraStageTimes=prefab['extraStageTimes'], bindings=bindings,
    distance=field(attack, 'distance'), homing=field(attack, 'homingTarget'), returnToStart=field(attack, 'returnToInitialDashPosition'),
    movementCurve=field(attack, 'movementCurve')['keys'], exclusionMask=mask,
    exclusionMaskSource=dict(path=str(source_prefab), sha256=hashlib.sha256(source_prefab.read_bytes()).hexdigest()),
    variants=enemy['databaseValues'], geometry=geometry, physics=physics,
    warningStartLength=objects['o00771']['data']['length'], warningEndLength=objects['o00770']['data']['length'],
    differences=[
        'Existing Brotchi reference appearance; independent retimed movement clips visualize source attack phases. Original Dash sprites/audio are not migrated.',
        'Directional orange arrow uses current project warning material; source arrow has disabled polygons, never enabled as a damage area.',
        'Current Stats rebound on initialization, reset, attack reuse and lease restoration; source stale Stats reference defect is corrected.',
        'Source arrow fade callbacks are replaced by existing explicit warning lifecycle cleanup; no FMOD events are copied.'])
output = root / 'Assets/_Project/Content/NetworkCombat/Limbo/Dash'
output.mkdir(parents=True, exist_ok=True)
serialized = json.dumps(data, ensure_ascii=False, indent=2) + '\n'
(output / 'DashSource.json').write_text(serialized, encoding='utf-8')
if not (output / 'DashAdapted.json').exists():
    (output / 'DashAdapted.json').write_text(serialized, encoding='utf-8')
print('Dash numeric source and initial adaptation extracted; existing adaptation preserved.')
