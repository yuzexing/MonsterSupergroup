"""Extract stage-two authoring values without changing recovered evidence."""
import hashlib, json, pathlib, re

ROOT = pathlib.Path(__file__).resolve().parents[1]
EVIDENCE = ROOT / 'docs/evidence/hellmaiden-attacks'
OUT = ROOT / 'Assets/_Project/Content/NetworkCombat/Limbo/Stage2'
graph = json.loads((EVIDENCE / 'runtime-assets.json').read_text(encoding='utf-8'))
enemies = json.loads((EVIDENCE / 'recovered-enemies.json').read_text(encoding='utf-8'))['enemies']

def field(data, key):
    return next(v for k, v in data.items() if k.rsplit('::', 1)[-1] == key)

rows = []
for identity in ('Skeleton', 'Elite_Skeleton', 'Brotchi', 'Slime'):
    enemy = next(e for e in enemies if e['identity'] == identity)
    for prefab in enemy['prefabs']:
        key = 'Rusher' if 'Rusher' in prefab['prefab'] else identity
        attack_root = field(prefab['attackConfiguration'], 'attackPrefab')['path'] if 'Skeleton' in identity else None
        controller = next(o['data'] for o in graph['objects'] if o['id'] == prefab['controllerId'])
        row = dict(key=key, identity=identity, controllerId=prefab['controllerId'], animatorId=prefab['animatorId'],
                   cooldown=prefab['controller']['attackCooldown'], distance=prefab['controller']['attackDistance'],
                   stopForAttack=field(controller,'stopForAttack'), elite=prefab['controller']['isElite'],
                   warningExtra=prefab['extraStageTimes']['warningTime'], activeExtra=prefab['extraStageTimes']['attackTime'],
                   recoveryExtra=prefab['extraStageTimes']['recoveryTime'], bindings=[], geometry=[])
        if attack_root:
            for name,b in prefab['bindings'].items():
                row['bindings'].append(dict(field=name, sourceClip=b['clip'], length=b['length'], speed=b['speed'],
                    fade=b['fadeDuration'], normalizedStart=str(b['normalizedStartTime']),
                    eventTimes=[str(t) for t in field(b['events']['fields'],'_NormalizedTimes')]))
        for obj in graph['objects']:
            prefix = next((p for p in (prefab['prefab'], attack_root) if p and (obj['path']==p or obj['path'].startswith(p+'/'))), None)
            if not prefix: continue
            kind=obj['type'].split('.')[-1]
            if kind not in ('Transform','CircleCollider2D','BoxCollider2D','PolygonCollider2D'):continue
            path=re.sub(r'\[\d+\]','',obj['path'][len(prefix):].lstrip('/'))
            is_attack=prefix==attack_root
            if not is_attack and path not in ('','Collider','HurtBox','Hitbox','AttackCollider'):continue
            data={k:v for k,v in obj['data'].items() if k not in ('gameObject','paths','size')}
            if kind=='PolygonCollider2D':
                assert len(obj['data']['paths'])==1
                data['points']=[n for point in obj['data']['paths'][0] for n in point]
            if kind=='BoxCollider2D': data['size']=obj['data']['size']
            if not is_attack and path=='Hitbox':path='AttackCollider'
            row['geometry'].append(dict(objectId=obj['id'], attack=is_attack, path=path, type=kind, **data))
        rows.append(row)

# Only scalar animation curves are transcribed; no source images/materials or Prefabs are copied.
warning=[]
for name in ('MeleeWarning_BuildUp','MeleeWarning_BuildDown'):
    path=pathlib.Path('F:/DecomplieLatest/HellMaiden/ExportedProject/Assets/AnimationClip')/(name+'.anim')
    raw=path.read_text(encoding='utf-8')
    curves=[]
    blocks=raw.split('  m_FloatCurves:\n',1)[1].split('  m_PPtrCurves:',1)[0].split('  - serializedVersion: 2\n')[1:]
    for block in blocks:
        if '    attribute: m_Color.a\n' not in block:continue
        keys=[]
        for key in block.split('      - serializedVersion: 3\n')[1:]:
            keys.append({k:re.search(r'        '+k+r': ([^\n]+)',key).group(1) for k in ['time','value','inSlope','outSlope']})
        curves.append(dict(path=re.search(r'    path: ([^\n]+)',block).group(1),attribute='m_Color.a',keys=keys))
    warning.append(dict(name=name,length=float(re.search(r'm_StopTime: ([^\n]+)',raw).group(1)),curves=curves,
                        source=str(path),sha256=hashlib.sha256(path.read_bytes()).hexdigest()))
data=dict(schemaVersion=1,sourceSha256=hashlib.sha256((EVIDENCE/'runtime-assets.json').read_bytes()).hexdigest(),
          enemies=rows,warning=warning,differences=[
              'Elite animations retime existing Skeleton sprite curves. Source directional lengths retained; original elite art is not restored.',
              'Contact enemies reuse current Base appearance. Current Stats rebound after each reset.',
              'Warning opacity curves retained; source shader-specific material properties and FMOD audio omitted.'])
OUT.mkdir(parents=True,exist_ok=True)
(OUT/'Stage2Source.json').write_text(json.dumps(data,ensure_ascii=False,indent=2),encoding='utf-8')
if not (OUT/'Stage2Adapted.json').exists():
    (OUT/'Stage2Adapted.json').write_text(json.dumps(data,ensure_ascii=False,indent=2),encoding='utf-8')
print('Extracted five source Prefab configurations; existing adaptation preserved.')
