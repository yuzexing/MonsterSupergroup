"""Read existing rendered evidence; never turns an unobserved case into a pass."""
import argparse
import csv
import json
import re
from collections import Counter, defaultdict
from pathlib import Path

parser=argparse.ArgumentParser()
parser.add_argument('directory',type=Path)
args=parser.parse_args()
summary={}
for role in ('host','client'):
    root=args.directory/role
    if not root.exists():continue
    events=[]
    p=root/'lostsoul-observation.jsonl'
    if p.exists():
        for line in p.read_text(encoding='utf-8-sig').splitlines():
            try:events.append(json.loads(line))
            except json.JSONDecodeError:pass
    phases=[];damage=[];pool=defaultdict(set);cycles=defaultdict(dict)
    for event in events:
        payload=json.loads(event['payload']) if event.get('payload') else {}
        if event['kind'] in ('phase','active-frame','sample'):
            phases.append(payload)
            if payload.get('simulator') and payload.get('action',{}).get('ActionId'):
                cycle=cycles[(payload['id'],payload['action']['ActionId'])]
                cycle.setdefault('start',event['elapsed'])
                cycle.setdefault('phases',set()).add(payload['action']['Phase'])
            for field in ('warningInstance','explosionInstance'):
                if payload.get(field):pool[(field,payload[field])].add(payload['id'])
        if event['kind']=='damage-attempt':damage.append(payload)
    raw=(root/'player.log').read_text(encoding='utf-8-sig',errors='replace') if (root/'player.log').exists() else ''
    audit=[]
    if (root/(role+'-audit.jsonl')).exists():
        for line in (root/(role+'-audit.jsonl')).read_text(encoding='utf-8-sig').splitlines():
            try:audit.append(json.loads(line))
            except json.JSONDecodeError:pass
    spawns=[]
    for file in root.glob('spawns-*.csv'):
        spawns.extend(csv.DictReader(file.open(encoding='utf-8-sig')))
    summary[role]=dict(eventCounts=dict(Counter(e['kind'] for e in events)),phaseCounts=dict(Counter(str(x['action']['Phase']) for x in phases)),
        spawnEvents=dict(Counter(x['event'] for x in spawns)),
        selfDestructs=[dict(id=int(m[0]),epoch=int(m[1]),action=m[2],combat=float(m[3]),deadline=float(m[4])) for m in re.findall(r'\[LostSoulDispose\] id=(\d+) epoch=(\d+) action=(\d+) combat=([\d.]+) deadline=([\d.]+)',raw)],
        damageAmounts=dict(Counter(str(x['amount']) for x in damage)),
        statsBindingFailures=sum(x.get('statsBound') is False for x in phases),
        poolInstancesReusedAcrossEnemies=[dict(kind=k[0],instance=k[1],enemies=sorted(v)) for k,v in pool.items() if len(v)>1],
        birthMismatches=[x for x in audit if x.get('kind')=='birth' and not x['match']],
        healthLosses=[x['previous']-x['current'] for x in audit if x.get('kind')=='health' and x['current']<x['previous']],
        maxElapsed=max((x.get('snapshot',{}).get('Elapsed',0) for x in audit),default=0),
        errors=re.findall(r'^.*(?:Exception:|\[LimboAudit\].*mismatch|Error:.*).*$' ,raw,re.M),
        handoffs=[e for e in events if e['kind']=='test-handoff'],
        simulatorCycles=[dict(id=k[0],action=k[1],start=v['start'],phases=sorted(v['phases'])) for k,v in cycles.items()],
        screenshots=len(list(root.glob('*.png'))))
print(json.dumps(summary,ensure_ascii=False,indent=2))
