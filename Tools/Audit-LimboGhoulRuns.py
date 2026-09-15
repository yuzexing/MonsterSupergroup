"""Read recorded Ghoul runs; never grant readiness or synthesize gameplay evidence."""
import argparse
import json
import re
import runpy
from collections import Counter, defaultdict
from pathlib import Path

parser=argparse.ArgumentParser()
parser.add_argument('directory',type=Path)
parser.add_argument('--output',type=Path,required=True)
args=parser.parse_args()
common=runpy.run_path(str(Path(__file__).with_name('Audit-LimboLostSoulRuns.py')))
report=common['summarize'](args.directory)
for role in ('host','client'):
    folder=args.directory/role
    if role not in report:continue
    rows=common['rows'](folder/'ghoul.jsonl')
    audit=common['rows'](folder/(role+'-audit.jsonl'))
    completed=next((r for r in audit if r['kind']=='frame' and r['snapshot']['Phase']==5),None)
    report[role]['firstCompletionSnapshot']=completed['snapshot'] if completed else None
    # A later Stopped snapshot belongs to UI restart teardown, not a regression of completion.
    report[role]['wavePhaseTransitions']=[]
    previous=None
    for r in audit:
        if r['kind']!='frame':continue
        state=r['snapshot'];key=(state['RunId'],state['Phase'])
        if key!=previous:report[role]['wavePhaseTransitions'].append(dict(run=key[0],phase=key[1],elapsed=state['Elapsed'],realtime=r['realtime']))
        previous=key
    for row in rows:row['data']=json.loads(row.get('payload') or '{}')
    phases=[r for r in rows if r['kind'] in ('phase','active-frame')]
    combos=defaultdict(lambda:dict(starts=set(),phases=set(),epochs=set()))
    for r in phases:
        d=r['data'];a=d['action']
        if not a['ActionId']:continue
        c=combos[(r['run'],d['id'],a['ActionId'])]
        c['starts'].add(a['ComboStartedAt']);c['epochs'].add(d['epoch'])
        if a['Phase']!=2 or d['hit'] and a['PoseStrikeIndex']==a['StrikeIndex']:
            c['phases'].add((a['StrikeIndex'],a['Phase']))
    hits=[r for r in rows if r['kind']=='damage-attempt']
    raw=(folder/'player.log').read_text(encoding='utf-8-sig',errors='replace')
    interrupts=re.findall(r'\[GhoulInterrupt\] enemy=(\d+) combo=(\d+) command=(\d+) hit=(\d+)',raw)
    report[role]['ghoul']={
        'events':dict(Counter(r['kind'] for r in rows)),
        'completeCombos':sum({(0,2),(1,2),(2,2),(2,3)}<=c['phases'] for c in combos.values()),
        'comboStartChanged':[str(k) for k,c in combos.items() if len(c['starts'])>1],
        'combosAcrossEpochs':sum(len(c['epochs'])>1 for c in combos.values()),
        'statsBindingFailures':sum(r['data'].get('statsBound') is False for r in phases),
        'activeColliderOutsideAppliedActive':[dict(elapsed=r['elapsed'],id=r['data']['id'],action=r['data']['action']) for r in phases
            if 'scheduledPhase' in r['data'] and r['data']['hit'] and r['data']['action']['Phase']!=2],
        'hitAmounts':dict(Counter(str(r['data']['amount']) for r in hits)),
        'settlementsAfterDeadline':sum(r['combat']>r['data']['action']['ActiveUntil'] for r in hits),
        'maximumSettlementDelay':max([0]+[r['combat']-r['data']['action']['ActiveUntil'] for r in hits]),
        'interruptCount':len(interrupts),
        'duplicateComboInterrupts':len(interrupts)-len({(a,b) for a,b,c,d in interrupts}),
        'interruptSources':dict(Counter(str(int(d)>>48) for a,b,c,d in interrupts)),
        'assistance':dict(Counter(r['kind'] for r in rows if r['kind'].startswith('test-'))),
    }
args.output.write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print(args.output)
