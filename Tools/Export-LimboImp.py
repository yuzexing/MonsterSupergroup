"""Derive Imp authoring data from recovered runtime evidence, never from class defaults."""
import hashlib,json,pathlib,re
ROOT=pathlib.Path(__file__).resolve().parents[1]
EVIDENCE=ROOT/'docs/evidence/hellmaiden-attacks'
OUT=ROOT/'Assets/_Project/Content/NetworkCombat/Limbo/Imp'
def read(p):return json.loads(p.read_text(encoding='utf-8'))
def field(n,key):return next(v for k,v in n.items() if k.rsplit('::',1)[-1]==key)
graph=read(EVIDENCE/'runtime-assets.json');objects=graph['objects']
enemy=next(e for e in read(EVIDENCE/'recovered-enemies.json')['enemies'] if e['identity']=='Imp')
prefab=enemy['prefabs'][0];bullet=next(o for o in objects if o['id']=='o00309')
bindings=[]
for name,b in prefab['bindings'].items():
    events=field(b['events']['fields'],'_NormalizedTimes')
    assert not b['animationEvents'], 'New clip events require explicit adaptation'
    bindings.append(dict(field=name,clip=b['clip'],length=b['length'],speed=b['speed'],fade=b['fadeDuration'],
        normalizedStart=str(b['normalizedStartTime']),eventTimes=[str(v) for v in events]))
geometry=[]
for obj in objects:
    prefix=next((p for p in ['Enemy_Imp[0]','EnemyBulletAttackImp[0]'] if obj['path'].startswith(p)),None)
    if not prefix or obj['type'].split('.')[-1] not in ('CircleCollider2D','BoxCollider2D','PolygonCollider2D','Transform'):continue
    path=re.sub(r'\[\d+\]','',obj['path'][len(prefix):].lstrip('/'))
    if obj['type'].endswith('Transform') and path not in ('','Collider','HurtBox','BulletPositionParent','BulletPositionParent/BulletPosition','BulletVisual','Hitbox'):continue
    geometry.append(dict(objectId=obj['id'],bullet=prefix.startswith('EnemyBullet'),path=path,type=obj['type'].split('.')[-1],**obj['data']))
data=dict(schemaVersion=1,sourceSha256=hashlib.sha256((EVIDENCE/'runtime-assets.json').read_bytes()).hexdigest(),
    animatorId=prefab['animatorId'],projectileId=bullet['id'],bindings=bindings,geometry=geometry,
    warningExtra=prefab['extraStageTimes']['warningTime'],activeExtra=prefab['extraStageTimes']['attackTime'],recoveryExtra=prefab['extraStageTimes']['recoveryTime'],
    cooldown=prefab['controller']['attackCooldown'],distance=prefab['controller']['attackDistance'],
    bulletSpeed=field(bullet['data'],'speed'),bulletDuration=field(bullet['data'],'duration'),bulletTimeout=field(bullet['data'],'bulletHasTimeOut'),bulletPierce=field(bullet['data'],'pierce'),
    differences=['Current appearance retained. Empty-method source audio callbacks are not bound; event times are retained.',
        'Damage binds current birth Stats. Source pooled stale Stats references are not reproduced.',
        'Reference projectile lifetime and single-pierce collision are confirmed by the server.'])
OUT.mkdir(parents=True,exist_ok=True)
(OUT/'ImpSource.json').write_text(json.dumps(data,ensure_ascii=False,indent=2),encoding='utf-8')
if not (OUT/'ImpAdapted.json').exists():
    (OUT/'ImpAdapted.json').write_text(json.dumps(data,ensure_ascii=False,indent=2),encoding='utf-8')
print('Imp source derived; existing adaptation preserved')
