import json, math, argparse
from pathlib import Path
from collections import Counter, defaultdict
p=argparse.ArgumentParser();p.add_argument('run');a=p.parse_args();root=Path(a.run)
report={'run':str(root),'kind':'technical evidence; rendered review still required','roles':{}}
for role in ('host','client'):
 folder=root/role
 if not (folder/'dash-observation.jsonl').exists():continue
 rows=[json.loads(s) for s in (folder/'dash-observation.jsonl').read_text(encoding='utf-8-sig').splitlines() if s]
 for r in rows:
  if r.get('payload'):r['p']=json.loads(r['payload'])
  else:r['p']={}
 samples=[r for r in rows if r['kind'] in ('phase','sample')]
 phases=[r for r in rows if r['kind']=='phase']
 stats=Counter(r['p'].get('damage') for r in samples)
 hits=[r for r in rows if r['kind']=='damage-attempt']
 loops=defaultdict(set)
 for r in phases:
  d=r['p'];act=d['action']
  if 2<=r['elapsed']<54 and act['Phase']==3:
   facing=act['Facing'];q=('R' if facing['x']>=0 else 'L')+('U' if facing['y']>=0 else 'D');loops[q].add(act['ActionId'])
 auditfile=folder/(role+'-audit.jsonl');aud=[json.loads(s) for s in auditfile.read_text(encoding='utf-8-sig').splitlines() if s] if auditfile.exists() else []
 damage=Counter(r['previous']-r['current'] for r in aud if r.get('kind')=='health' and r['current']<r['previous'])
 frames=[r for r in aud if r.get('kind')=='frame'];last=frames[-1] if frames else {}
 warnings=Counter(r['p']['warningInstance'] for r in phases if r['p'].get('warningInstance'))
 edges=defaultdict(list)
 for r in phases:
  if r['p'].get('warningInstance'):edges[r['p']['warningInstance']].append((r['p']['id'],r['p']['action']['ActionId']))
 reuse={str(k):len(set(v)) for k,v in edges.items() if len(set(v))>1}
 info={'phase_rows':len(phases),'samples':len(samples),'damage_bindings':dict(stats),'stats_mismatches':sum(not r['p']['statsBound'] for r in samples),
  'completed_recovery_actions_by_direction':{k:len(v) for k,v in loops.items()},'actual_health_losses':dict(damage),
  'attempts':len(hits),'wrong_phase_attempts':sum(r['p']['action']['Phase']!=2 for r in hits),
  'handoffs':[{k:r[k] for k in ('elapsed','detail','p')} for r in rows if r['kind']=='test-handoff'],
  'reused_warning_instance_distinct_actions':reuse,'last_frame':last,
  'assists':dict(Counter(r['kind'] for r in rows if r['kind'].startswith('test-'))),
  'warning_pool_enemy_ids':{str(k):sorted({v[0] for v in values}) for k,values in edges.items()}}
 boundary=defaultdict(lambda:{'minimum_gap':float('inf'),'attempts':0})
 for r in rows:
  if r['kind']=='boundary-gap':
   key=(r['detail'],r['p']['action']);boundary[key]['minimum_gap']=min(boundary[key]['minimum_gap'],r['p']['gap'])
 for r in hits:
  for key,value in boundary.items():
   if key[1]==r['p']['action']['ActionId']:value['attempts']+=1
 info['boundary_actions']=[{'side':key[0],'action':key[1],**value} for key,value in boundary.items()]
 info['boundary_ready']=[r['elapsed'] for r in rows if r['kind']=='boundary-ready']
 info['births']=[r for r in aud if r.get('kind')=='birth']
 report['roles'][role]=info
out=root/'dash-summary.json';out.write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps(report,ensure_ascii=False,indent=2))
