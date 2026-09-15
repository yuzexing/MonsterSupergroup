"""Read-only analysis of recorded continuous previews, split by actual run id."""
import collections
import csv
import json
import statistics
import sys
from pathlib import Path

root = Path(sys.argv[1])
result = {}
for role in ('host', 'client'):
    folder = root / role
    if not (folder / 'stage2-observation.jsonl').exists():
        continue
    observations = [json.loads(x) for x in (folder / 'stage2-observation.jsonl').read_text(encoding='utf-8-sig').splitlines()]
    runs = collections.defaultdict(list)
    current = ''
    for row in observations:
        if row['kind'] == 'round':
            current = row['detail']
        if current:
            row['p'] = json.loads(row.get('payload') or '{}')
            runs[current].append(row)
    summaries = {}
    audit = [json.loads(x) for x in (folder / (role+'-audit.jsonl')).read_text(encoding='utf-8-sig').splitlines()]
    round_number = 0
    for row in audit:
        if row['kind']=='round': round_number=row['round']
        row.setdefault('round',round_number)
    for run, rows in runs.items():
        frames = [r for r in audit if r['kind']=='frame' and r['snapshot']['RunId']==run]
        round_number = frames[0]['round'] if frames else -1
        selections = []
        started = None
        for row in audit:
            if row['round']!=round_number or row['kind']!='selection': continue
            if row['selecting'] and started is None: started=row
            elif not row['selecting'] and started is not None:
                selections.append(dict(level=started['level'],startElapsed=started['elapsed'],endElapsed=row['elapsed'],
                                       realtimeSeconds=row['realtime']-started['realtime']))
                started=None
        configs = {r['detail']: r['p']['birth'] for r in rows if r['kind'] == 'configuration'}
        deaths = [r for r in rows if r['kind'] == 'enemy-death']
        confirmed = {r['detail'] for r in deaths}
        timings = collections.defaultdict(list)
        for row in deaths:
            if row['p']['hasFirstHit']:
                timings[configs[row['detail']]['SourceEnemy']].append(row['p']['seconds'])
        pauses = []
        for a, b in zip(rows, rows[1:]):
            frozen = b['realtime'] - a['realtime'] - (b['combat'] - a['combat'])
            if frozen > .5:
                pauses.append(dict(before=a['elapsed'], after=b['elapsed'], seconds=frozen))
        summaries[run] = dict(
            sourceCounts=dict(collections.Counter((c['SourceEnemy'] + '/v' + str(c['Variant'])) for c in configs.values())),
            confirmedKills=len(deaths),
            ttk={k:dict(count=len(v), minimum=min(v), median=statistics.median(v), maximum=max(v)) for k,v in timings.items()},
            activeDeaths=[dict(enemy=r['detail'], source=configs[r['detail']]['SourceEnemy'], elapsed=r['elapsed'], action=r['p']['action'])
                          for r in rows if r['kind']=='enemy-health' and r['p']['current']==0 and r['p']['action']['Phase']==2 and r['detail'] in confirmed],
            cleanup=[dict(kind=r['kind'], elapsed=r['elapsed'], counts=r['p']) for r in rows if r['kind'] in ('cleanup','completed-cleanup')],
            clockSuspensions=pauses,
            fixtureEvents=sum(r['kind'].startswith('fixture-') for r in rows),
            bindsIncorrect=sum(not r['p'].get('currentStatsBound',True) for r in rows if r['kind']=='geometry'),
            lastElapsed=rows[-1]['elapsed'])
        summaries[run]['selectionWindows']=selections
        summaries[run]['lastFrame']=frames[-1] if frames else None
        summaries[run]['healthLosses']=[r for r in audit if r['round']==round_number and r['kind']=='health' and r['current']<r['previous']]
    result[role] = summaries
traces = []
for path in sorted(root.rglob('spawns-*.csv')):
    with path.open(encoding='utf-8-sig', newline='') as handle:
        rows = list(csv.DictReader(handle))
    births = [r for r in rows if r['event']=='spawn' and r['result']=='Spawned']
    traces.append(dict(file=path.name, events=dict(collections.Counter(r['event'] for r in rows)),
        successfulByClip=dict(collections.Counter(r['clip'] for r in births)),
        lastBirthByClip={k:max(float(r['elapsed']) for r in births if r['clip']==k) for k in {r['clip'] for r in births}},
        rusherAfterClipEnd=sum(r['clip']=='4' and float(r['elapsed'])>200.083334 for r in births),
        bins10s=[dict(start=t, births=sum(t<=float(r['elapsed'])<t+10 for r in births),
                     deaths=sum(r['event']=='death' and t<=float(r['elapsed'])<t+10 for r in rows)) for t in range(0,210,10)]))
result['traces']=traces
output=root/'continuous-summary.json'
output.write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print(output)
print(json.dumps(result,ensure_ascii=False,indent=2))
