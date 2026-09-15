"""Derive a reviewable evidence index from the original runtime graph, never production values."""
import argparse, collections, hashlib, json, pathlib, sys, math
ROOT=pathlib.Path(__file__).resolve().parents[2]
WORK=ROOT/'Logs/EnemyRecovery'
OUT=ROOT/'docs/evidence/hellmaiden-attacks'
OUT.mkdir(parents=True,exist_ok=True)
def read(path): return json.loads(path.read_text(encoding='utf-8-sig'))
def write(path,value): path.write_text(json.dumps(value,ensure_ascii=False,indent=2),encoding='utf-8')
def field(obj,name,default=None):
    fields=obj.get('fields',obj.get('data',obj)) if isinstance(obj,dict) else {}
    return next((v for k,v in fields.items() if k.rsplit('::',1)[-1]==name),default)
parser=argparse.ArgumentParser()
parser.add_argument('--capture-a',type=pathlib.Path,default=WORK/'capture-a/assets.json')
parser.add_argument('--capture-b',type=pathlib.Path,default=WORK/'capture-b/assets.json')
args=parser.parse_args()
capture_a=args.capture_a.resolve();capture_b=args.capture_b.resolve()
a=read(capture_a); b=read(capture_b)
objects={n['id']:n for n in a['objects']}
def resolve(ref): return objects.get((ref or {}).get('reference'))
def normalized(data):
    result=json.loads(json.dumps(data))
    for node in result['objects']: node.pop('runtimeId',None)
    return result
def walk(value,path='$'):
    if isinstance(value,dict):
        yield path,value
        for k,v in value.items(): yield from walk(v,path+'/'+k)
    elif isinstance(value,list):
        for i,v in enumerate(value): yield from walk(v,path+'/'+str(i))
issues=[dict(path=p,value=v) for p,v in walk(a) if 'readError' in v or 'unresolved' in v or ('reference' in v and v['reference'] not in objects)]
comparison=dict(identical=normalized(a)==normalized(b),ignoredFields=['objects[].runtimeId'],clips=len(a['clips']),objects=len(objects),issues=issues,
    files={str(p.relative_to(ROOT)):hashlib.sha256(p.read_bytes()).hexdigest() for p in [capture_a,capture_b]})
write(OUT/'capture-comparison.json',comparison)
write(OUT/'runtime-assets.json',normalized(a))
def transition(value):
    if value is None:return None
    clip=resolve(field(value,'_Clip'))
    return dict(clipId=clip['id'] if clip else None,clip=clip['name'] if clip else None,
        length=clip['data'].get('length') if clip else None,frameRate=clip['data'].get('frameRate') if clip else None,
        speed=field(value,'_Speed'),normalizedStartTime=field(value,'_NormalizedStartTime'),fadeDuration=field(value,'_FadeDuration'),
        events=field(value,'_Events'),animationEvents=clip['data'].get('events') if clip else None)
identities=collections.defaultdict(list)
for clip in a['clips']:identities[clip['source']].append(clip)
database={field(entry,'enemyName'):entry for entry in field(resolve(a['enemyDatabase']),'enemies')}
enemies=[]
for identity,clips in identities.items():
    prefabs=[]
    for ref in dict((c['prefab']['reference'],c['prefab']) for c in clips).values():
        controller=resolve(ref); animator=resolve(field(controller,'enemyAnimator')); attack=resolve(field(controller,'attackScript'))
        bindings={}
        for k,v in animator['data'].items():
            name=k.rsplit('::',1)[-1]
            if name.startswith(('attack','recovery')) and isinstance(v,dict) and v.get('managedType')=='Animancer.ClipTransition':bindings[name]=transition(v)
        sets=[]
        for item in field(animator,'attackSets',[]) or []:
            sets.append({k.rsplit('::',1)[-1]:transition(v) for k,v in item.get('fields',{}).items() if isinstance(v,dict) and v.get('managedType')=='Animancer.ClipTransition'})
        extra={n:field(attack,n) for n in ['warningTime','attackTime','recoveryTime']}
        prefabs.append(dict(controllerId=controller['id'],prefab=controller['path'],animatorId=animator['id'],attackId=attack['id'],attackType=attack['type'],
            controller={n:field(controller,n) for n in ['attackCooldown','attackDistance','hasAttackAnimation','attackOnCameraBounds','isElite','usesPathfinding']},
            extraStageTimes=extra,bindings=bindings,attackSets=sets,attackConfiguration=attack['data']))
    variants=sorted(set(c['variant'] for c in clips))
    database_values=[]
    for variant in variants:
        entry=field(database[identity],'enemyData')[variant]
        stats=field(field(entry,'stats'),'baseStats')
        database_values.append(dict(variant=variant,variantName=field(entry,'variantName'),baseValues={k.rsplit('::',1)[-1]:v for k,v in stats['fields'].items()},sourceDatabase=a['enemyDatabase']['reference']))
    enemies.append(dict(identity=identity,variants=variants,databaseValues=database_values,timelineClips=clips,prefabs=prefabs,status='configuration-recovered; behavior integration remains disabled'))
write(OUT/'recovered-enemies.json',dict(schemaVersion=1,source=str(capture_a.relative_to(ROOT)),corroboratedBy=str(capture_b.relative_to(ROOT)),timeline=a['timeline'],duration=a['duration'],enemies=enemies))
special=[o for o in a['objects'] if o['type'].split('.')[-1] in ['BulletProjectile','EnemyExplosionAttackVFX']]
write(OUT/'projectile-and-explosion.json',special)
# Geometry remains in original hierarchy units, never flattened into a guessed radius.
write(OUT/'geometry.json',[o for o in a['objects'] if o['type'].split('.')[-1] in ['CircleCollider2D','PolygonCollider2D','BoxCollider2D','Transform']])
for enemy in enemies:
    for p in enemy['prefabs']:
        print(enemy['identity'],enemy['variants'],p['attackType'].split('.')[-1],p['extraStageTimes'],{k:v['length'] for k,v in p['bindings'].items() if k.endswith('LeftUp')},'sets',len(p['attackSets']))
print('Graph check:',comparison['identical'],'issues:',len(issues))

def metrics(folder):
    path=folder/'events.jsonl'
    if not path.exists():return None
    rows=[]
    for line in path.read_text(encoding='utf-8-sig').splitlines():
        try: rows.append(json.loads(line))
        except json.JSONDecodeError:continue
    episodes=[];episode=None
    for row in rows:
        d=row['data']
        if row['kind']=='fixture-enemy':
            episode=dict(**d,startTime=row['time'],cycles=[],events=[],healthChanges=[]);episodes.append(episode)
        elif episode and row['kind']=='attack-event' and d['owner']==episode['id']: episode['events'].append(row)
        elif episode and row['kind']=='player-health':episode['healthChanges'].append(row)
        elif episode and row['kind']=='fixture-enemy-end':episode['end']=d
    for episode in episodes:
        current={};strikes=[]
        for row in episode['events']:
            name=row['data']['method']
            index=row['data']['timing'].get('index')
            if name in ('EnemyAttack.AttackWarningEnter','EnemyAttackExplosion.AttackWarningEnter'):
                if index in (None,0):
                    current=dict(warningStart=row['time'],frame=row['frame'],effective=row['data']['timing']);strikes=[]
                strikes.append(dict(index=index,warningStart=row['time'],effective=row['data']['timing']))
            if name=='EnemyAttack.AttackEnter' and current:
                current.setdefault('attackStart',row['time']);strikes[-1]['attackStart']=row['time']
            if name in ('EnemyAttack.AttackExit','EnemyAttackMelee.AttackExit') and strikes:strikes[-1]['attackEnd']=row['time']
            if name=='EnemyAttack.RecoveryEnter' and current:current['recoveryStart']=row['time']
            if name=='EnemyAttack.RecoveryExit' and index in (None,0) and all(k in current for k in ['warningStart','attackStart','recoveryStart']):
                current.update(end=row['time'],warning=current['attackStart']-current['warningStart'],active=current['recoveryStart']-current['attackStart'],recovery=row['time']-current['recoveryStart'])
                for strike in strikes:
                    if 'attackStart' in strike:strike['warning']=strike['attackStart']-strike['warningStart']
                    if 'attackEnd' in strike:strike['active']=strike['attackEnd']-strike['attackStart']
                if len(strikes)>1:
                    # This span contains later warnings as well as attacks; it is not one hit window.
                    current['firstWarning']=current.pop('warning')
                    current['firstAttackToFinalRecovery']=current.pop('active')
                current['strikes']=strikes
                episode['cycles'].append(current);current={}
        episode['eventCounts']=dict(collections.Counter(r['data']['method'] for r in episode['events']))
        episode['damageDeltas']=dict(collections.Counter(str(r['data']['before']-r['data']['after']) for r in episode['healthChanges'] if r['data']['before']>r['data']['after']))
        episode['finalComboRecoveries']=sum(r['data']['method']=='EnemyAttack.RecoveryExit' and r['data']['timing'].get('index')==0 for r in episode['events'])
        episode.pop('events')
    flights=[];flying={}
    for row in rows:
        d=row['data'];kind=row['kind']
        if kind=='attack-event' and d['method']=='BulletProjectile.FireEnter':
            flight=dict(id=d['id'],owner=d['owner'],start=row['time'],points=[]);flights.append(flight);flying[d['id']]=flight
        elif kind=='motion' and d['type']=='BulletProjectile' and d['fired'] and d['id'] in flying:
            flying[d['id']]['points'].append(dict(time=row.get('fixedTime',row['time']),position=d['position']))
        elif kind=='attack-event' and d['id'] in flying and d['method'] in ('BulletProjectile.HitEnter','BulletProjectile.ExpireEnter'):
            flight=flying.pop(d['id']);flight['end']=row['time'];flight['endReason']=d['method'];flight['duration']=row['time']-flight['start']
    for flight in flights:
        points=flight.pop('points')
        if len(points)>1 and points[-1]['time']>points[0]['time']:
            flight['measuredSpeed']=math.dist(points[-1]['position'],points[0]['position'])/(points[-1]['time']-points[0]['time'])
            flight['measuredOverSeconds']=points[-1]['time']-points[0]['time']
    return dict(run=folder.name,completed=(folder/'observe.complete').exists(),episodes=episodes,probeErrors=[r for r in rows if r['kind'] in ('probe-error','failed')],
        projectileFlights=flights,postStartBindings=[r for r in rows if r['kind']=='post-start-bindings'],originalEventsFile=str(path.relative_to(ROOT)),fixtureConditions=[r for r in rows if r['kind'].startswith('fixture-') and r['kind'] not in ('fixture-enemy','fixture-enemy-end','fixture-cycle-complete')])
reports=[metrics(folder) for folder in sorted(WORK.glob('observe-*'))]
write(OUT/'observations.json',[r for r in reports if r])
