"""Summarize actual Ghoul rendered logs, without granting readiness."""
import argparse
import csv
import json
import re
from collections import Counter,defaultdict
from pathlib import Path
p=argparse.ArgumentParser();p.add_argument('directory',type=Path);p.add_argument('--output',type=Path);args=p.parse_args()
report={}
for role in ('host','client'):
    root=args.directory/role;file=root/'ghoul.jsonl'
    if not file.exists():continue
    rows=[]
    for line in file.read_text(encoding='utf-8-sig').splitlines():
        try:r=json.loads(line);r['payload']=json.loads(r['payload']) if r.get('payload') else {};rows.append(r)
        except json.JSONDecodeError:continue
    cycles=defaultdict(lambda:dict(phases=set(),epochs=set(),first=1e30,simulator=False));instances=defaultdict(set)
    for r in rows:
        d=r['payload'];a=d.get('action',{})
        if r['kind'] in ('phase','active-frame') and a.get('ActionId'):
            c=cycles[(r['run'],d['id'],a['ActionId'])]
            if a['Phase']!=2 or d['hit'] and a['PoseStrikeIndex']==a['StrikeIndex']:c['phases'].add((a['StrikeIndex'],a['Phase']))
            c['epochs'].add(d['epoch']);c['first']=min(c['first'],r['elapsed']);c['simulator']|=d['simulator']
            if d['instance']:instances[d['instance']].add((d['id'],a['ActionId'],a['StrikeIndex']))
    # All three Active phases plus final Recovery, irrespective of assignment epochs.
    complete=[dict(id=k[1],action=k[2],first=v['first'],epochs=sorted(v['epochs']),simulator=v['simulator']) for k,v in cycles.items() if {(0,2),(1,2),(2,2),(2,3)}<=v['phases']]
    report[role]=dict(events=dict(Counter(r['kind'] for r in rows)),completeCombos=complete,
        completedByQuadrant=dict(Counter(str(min(3,int(c['first']/20))) for c in complete if c['first']<80)),
        observedHitAmounts=dict(Counter(str(r['payload']['amount']) for r in rows if r['kind']=='damage-attempt')),
        handoffs=[r for r in rows if r['kind']=='test-handoff'],statsBindingFailures=sum(r['payload'].get('statsBound') is False for r in rows),
        reusedAttackInstances=sum(len(v)>1 for v in instances.values()),
        cleanup=[r for r in rows if r['kind']=='cleanup'])
    raw=(root/'player.log').read_text(encoding='utf-8-sig',errors='replace')
    interrupts=re.findall(r'\[GhoulInterrupt\] enemy=(\d+) combo=(\d+) command=(\d+) hit=(\d+)',raw)
    placements={(r['run'],r['payload']['id'],r['payload']['action']['ActionId'],r['payload']['action']['StrikeIndex']):r['detail'] for r in rows if r['kind']=='boundary-placement'}
    boundary_hits=Counter()
    for r in rows:
        if r['kind']!='damage-attempt':continue
        d=r['payload'];a=d['action'];boundary_hits[placements.get((r['run'],d['id'],a['ActionId'],a['StrikeIndex']),'unclassified')]+=1
    report[role].update(interrupts=[dict(enemy=a,combo=b,command=c,hit=d) for a,b,c,d in interrupts],
        duplicateInterruptBroadcasts=len(interrupts)-len(set(interrupts)),boundaryPlacements=dict(Counter(placements.values())),
        boundaryHitAttempts=dict(boundary_hits))
    audit=root/(role+'-audit.jsonl')
    if audit.exists():
        audit_rows=[json.loads(x) for x in audit.read_text(encoding='utf-8-sig').splitlines() if x.strip()]
        report[role]['actualHealthLosses']=[r for r in audit_rows if r['kind']=='health' and r['current']<r['previous']]
        report[role]['birthMismatches']=[r for r in audit_rows if r['kind']=='birth' and not r['match']]
text=json.dumps(report,ensure_ascii=False,indent=2)
if args.output:args.output.write_text(text,encoding='utf-8')
print(text)
