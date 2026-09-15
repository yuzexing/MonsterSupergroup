"""Summarize recorded facts; never treat an incomplete recording as a passed run."""
import argparse
import csv
import json
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument('directory', type=Path)
args = parser.parse_args()
result = {'directory': str(args.directory.resolve()), 'roles': {}}
births_by_role = {}
for path in sorted(args.directory.rglob('*-audit.jsonl')):
    rows = [json.loads(line) for line in path.read_text(encoding='utf-8-sig').splitlines() if line.strip()]
    current_round = 0
    for row in rows:
        if row['kind'] == 'round': current_round = row['round']
        row.setdefault('round', current_round)
    births = [r for r in rows if r['kind'] == 'birth']
    frames = [r for r in rows if r['kind'] == 'frame']
    hits = [r for r in rows if r['kind'] == 'health' and r['current'] < r['previous']]
    role = path.stem.removesuffix('-audit')
    births_by_role[role] = {r['id']: r for r in births}
    result['roles'][role] = {
        'birthsObserved': len(births), 'birthMismatches': [r for r in births if not r['match']],
        'lastFrame': frames[-1] if frames else None,
        'completed': bool(frames) and frames[-1]['snapshot']['Phase'] == 5,
        'completedRunIds': sorted({r['snapshot']['RunId'] for r in frames if r['snapshot']['Phase'] == 5}),
        'runs': {run: {'lastFrame': [r for r in frames if r['snapshot']['RunId'] == run][-1],
                       'birthsObserved': sum(r['round'] == next(f['round'] for f in frames if f['snapshot']['RunId'] == run) for r in births),
                       'peakAlive': max(r['snapshot']['Alive'] for r in frames if r['snapshot']['RunId'] == run)}
                 for run in sorted({r['snapshot']['RunId'] for r in frames if r['snapshot']['RunId']})},
        'peakAlive': max((r['snapshot']['Alive'] for r in frames), default=0),
        'hits': hits,
        'minimumHitIntervalRealtime': min((b['realtime']-a['realtime'] for a,b in zip(hits,hits[1:])), default=None),
        'movementExtent': {
            axis: max((r.get('position',{}).get(axis,0) for r in frames), default=0)-min((r.get('position',{}).get(axis,0) for r in frames), default=0)
            for axis in ['x','y']}}
traces = []
for path in sorted(args.directory.rglob('spawns-*.csv')):
    with path.open(encoding='utf-8-sig', newline='') as f:
        rows = list(csv.DictReader(f))
    traces.append({'file': str(path), 'events': {kind: sum(r['event']==kind for r in rows) for kind in sorted(set(r['event'] for r in rows))},
                   'spawnResults': {kind: sum(r['event']=='spawn' and r['result']==kind for r in rows) for kind in sorted(set(r['result'] for r in rows if r['event']=='spawn'))}})
result['serverTraces'] = traces
if 'host' in births_by_role and 'client' in births_by_role:
    host, client = births_by_role['host'], births_by_role['client']
    common = host.keys() & client.keys()
    fields = ['source','hp','damage','speed','xp']
    result['replication'] = {'commonEnemyIds': len(common), 'hostOnly': sorted(host.keys()-client.keys()), 'clientOnly': sorted(client.keys()-host.keys()),
        'mismatches': [i for i in common if any(host[i][k] != client[i][k] for k in fields)]}
encoded = json.dumps(result, ensure_ascii=False, indent=2)
(args.directory / 'summary.json').write_text(encoded+'\n', encoding='utf-8')
print(encoded)
