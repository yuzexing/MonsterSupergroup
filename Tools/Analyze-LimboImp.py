"""Summarize real rendered run evidence. Does not label a run passed automatically."""
import json, sys, collections, math
from pathlib import Path

root = Path(sys.argv[1])
result = {}
for role in ('host', 'client'):
    folder = root / role
    file = folder / 'imp-observation.jsonl'
    if not file.exists():
        continue
    rows = [json.loads(s) for s in file.read_text(encoding='utf-8-sig').splitlines()]
    for row in rows:
        row['p'] = json.loads(row['payload']) if row['payload'] else {}
    counts = collections.Counter(r['kind'] for r in rows)
    accepted = [r for r in rows if r['kind'] == 'accepted']
    presented = [r for r in rows if r['kind'] == 'presented']
    key = lambda p: (p['Key']['EnemyEntityId'], p['Key']['ActionId'])
    launches = {key(r['p']): r['p'] for r in presented}
    terminates = [r for r in rows if r['kind'] == 'terminated']
    lifespan = [{'key': key(r['p']), 'reason': r['p']['Reason'], 'age': r['combat']-launches[key(r['p'])]['FiredAt']}
                for r in terminates if key(r['p']) in launches]
    instances = collections.Counter(r['detail'] for r in presented)
    fixtures = [{k: r[k] for k in ('kind','detail','elapsed','combat','realtime')} for r in rows if r['kind'].startswith('fixture-')]
    audit = [json.loads(s) for s in (folder / (role+'-audit.jsonl')).read_text(encoding='utf-8-sig').splitlines()]
    births = [r for r in audit if r['kind'] == 'birth']
    health = [r for r in audit if r['kind'] == 'health']
    result[role] = dict(counts=counts, attackConfig=[r['p'] for r in rows if r['kind']=='attack-config'],
        births=births, health=health, uniqueLaunches=len(launches), duplicatePresentations=len(presented)-len(launches),
        bulletInstanceReuse=dict(instances), lifespan=lifespan, fixtures=fixtures,
        lastFrame=next((r for r in reversed(audit) if r['kind']=='frame'), None),
        maxObservedAge=max((r['p']['age'] for r in rows if r['kind']=='flight'), default=0))
    phases = collections.defaultdict(list)
    for r in rows:
        if r['kind']=='phase' and r['detail'].endswith('/simulator'):
            action=r['p']['Movement']['Runtime']['Action']
            phases[(r['p']['Movement']['EnemyEntityId'],action['ActionId'])].append((action['Phase'], r['combat']))
    result[role]['observedPhaseSequences']=[{'enemy':k[0],'action':k[1],'phases':v} for k,v in phases.items()]
if 'host' in result and 'client' in result:
    normalized = lambda values: [{k:v for k,v in row.items() if k != 'role'} for row in values]
    result['both'] = dict(sameBirths=normalized(result['host']['births'])==normalized(result['client']['births']),
                         sameLaunchCount=result['host']['uniqueLaunches']==result['client']['uniqueLaunches'])
(root/'imp-summary.json').write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
for role in ('host','client'):
    if role in result:
        r=result[role]
        print(role,json.dumps({k:r[k] for k in ('counts','attackConfig','uniqueLaunches','duplicatePresentations','maxObservedAge','lastFrame')},ensure_ascii=False))
        print('health losses', [r['previous']-r['current'] for r in r['health'] if r['current'] < r['previous']])
