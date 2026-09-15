"""Archive completed rounds separately from GUI restarts and sampled performance."""
import collections
import csv
import hashlib
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def rows(path):
    if not path.exists():
        return []
    return [json.loads(s) for s in path.read_text(encoding='utf-8-sig').splitlines() if s.endswith('}')]


def dist(values):
    values = sorted(values)
    return dict(min=values[0], p50=values[int((len(values)-1)*.5)],
                p95=values[int((len(values)-1)*.95)], max=values[-1]) if values else None


reports = []
for name in sys.argv[1:]:
    base = ROOT/'Logs/LimboReference'/name
    report = dict(run=name, roles={}, failures=[])
    birth_sets = {}
    for role in ('host', 'client'):
        folder = base/role
        audit = rows(folder/(role+'-audit.jsonl'))
        if not audit:
            continue
        frames = [r for r in audit if r['kind']=='frame']
        run_ids = list(dict.fromkeys(r['snapshot']['RunId'] for r in frames if r['snapshot']['RunId']))
        completed = next((r for r in frames if r['snapshot']['Phase']==5), None)
        if not completed:
            report['failures'].append(role+': no completed round')
            continue
        run_id = completed['snapshot']['RunId']
        start = next(r['realtime'] for r in frames if r['snapshot']['RunId']==run_id)
        next_round = next((r for r in frames if r['realtime']>completed['realtime'] and r['snapshot']['RunId'] and r['snapshot']['RunId']!=run_id), None)
        end = next_round['realtime'] if next_round else float('inf')
        first_births = []
        for r in audit:
            if r is completed:
                break
            if r['kind']=='birth':
                first_births.append(r)
        birth_sets[role] = sorted(({k:v for k,v in r.items() if k!='role'} for r in first_births), key=lambda r:r['id'])
        mismatch = [r for r in first_births if r.get('match') is not True]
        art = [r for r in rows(folder/'art-observation.jsonl') if start<=r['real']<end]
        perf = [r for r in art if r['kind']=='frame' and r['real']<=completed['realtime']]
        death = [r for r in art if r['kind']=='death']
        stage = [r for r in rows(folder/'stage2-observation.jsonl') if r['kind']=='completed-cleanup' and r['detail']==run_id]
        traps = [r for r in rows(folder/'spatial-observation.jsonl') if r['kind']=='completed-cleanup' and r['run']==run_id]
        errors = [s for s in (folder/'player.log').read_text(encoding='utf-8-sig',errors='replace').splitlines() if 'Exception:' in s or 'Assertion failed' in s]
        rr = dict(completed=completed, nextRound=next_round, runIds=run_ids, births=len(first_births),
                  birthMismatches=mismatch, deathSamples=len(death),
                  deathVariants=dict(collections.Counter((r['appearance']+'/'+str(r['variant'])) for r in death)),
                  aliveOrDamagingDeaths=[r for r in death if r['alive'] or r['enabledDamageAreas']],
                  stageCleanup=stage, trapCleanup=traps, errors=errors,
                  sampledFrameMs=dist([r['frameMs'] for r in perf]), allocatedBytes=dist([r['allocated'] for r in perf]),
                  particles=dist([r['particles'] for r in perf]),
                  guiEvidence={f:(folder/f).exists() for f in ('ui-completed.png','ui-restarted.png')})
        if role=='host':
            files=sorted(folder.glob('spawns-*.csv'))
            if files:
                events=list(csv.DictReader(files[0].open(encoding='utf-8-sig',newline='')))
                rr['spawnCsv']=str(files[0])
                rr['eventCounts']=dict(collections.Counter(r['event'] for r in events))
                rr['successfulSpawns']=dict(collections.Counter(r['source']+'/'+r['variant'] for r in events if r['event']=='spawn' and r['result']=='Spawned'))
        if mismatch or rr['aliveOrDamagingDeaths'] or errors:
            report['failures'].append(role+': inspect birth/death/error records')
        if not stage or not traps:
            report['failures'].append(role+': cleanup samples missing')
        for s in stage:
            if any(json.loads(s['payload']).values()):
                report['failures'].append(role+': stage cleanup nonzero')
        for s in traps:
            if any(s[k] for k in ('slots','states','collisionAreas','livingWarningRoots')):
                report['failures'].append(role+': trap cleanup nonzero')
        report['roles'][role]=rr
    if len(birth_sets)==2:
        report['sameFirstRoundBirths']=birth_sets['host']==birth_sets['client']
        if not report['sameFirstRoundBirths']:
            report['failures'].append('Host/Client first round births differ')
    reports.append(report)
out=ROOT/'Logs/LimboArt/preview-review.json'
out.write_text(json.dumps(dict(note='Technical assists; 1 Hz samples include concurrent processes. GUI images require visual review. Not a gameplay pressure or exhaustive frame profile.',runs=reports),ensure_ascii=False,indent=2),encoding='utf-8')
for report in reports:
    print(report['run'], report['failures'], {k:v['births'] for k,v in report['roles'].items()})
