"""Read actual full-flow records, keeping requests, completions and counting scopes separate."""
import argparse
import csv
import json
import re
import runpy
from collections import Counter
from pathlib import Path


def summarize(root):
    common = runpy.run_path(str(Path(__file__).with_name('Audit-LimboLostSoulRuns.py')))
    report = common['summarize'](root)
    read = common['rows']
    known_run_births = {}
    for role in ('host', 'client'):
        if role not in report:
            continue
        folder = root / role
        rows = read(folder / 'full.jsonl')
        rounds = {}
        for row in rows:
            run = row.get('run')
            if not run:
                continue
            r = rounds.setdefault(run, dict(requests=[], completions=[], waits=[], helpers=Counter(), frames=[]))
            data = json.loads(row.get('payload') or '{}')
            if row['kind'] == 'request-observed':
                r['requests'].append(dict(realtime=row['realtime'], snapshot=data))
            elif row['kind'] == 'completed':
                r['completions'].append(dict(realtime=row['realtime'], snapshot=data))
            elif row['kind'] == 'frame':
                r['frames'].append(data)
                if data['snapshot']['Phase'] == 6:
                    wait = dict(reason=data['snapshot']['TransitionWaitReason'], count=data['snapshot']['TransitionWaitCount'],
                                elapsed=data['snapshot']['Elapsed'], selecting=data['selecting'], timeScale=data['timeScale'])
                    if not r['waits'] or wait != r['waits'][-1]:
                        r['waits'].append(wait)
            elif row['kind'].startswith('test-'):
                r['helpers'][row['kind']] += 1
        for run, r in rounds.items():
            frames = r.pop('frames')
            r['firstSnapshot'] = frames[0]['snapshot'] if frames else None
            r['lastSnapshot'] = frames[-1]['snapshot'] if frames else None
            r['helpers'] = dict(r['helpers'])
            if r['requests'] and r['completions']:
                start, end = r['requests'][0], r['completions'][0]
                r['waitWallSeconds'] = end['realtime'] - start['realtime']
                r['waitStageSeconds'] = end['snapshot']['Elapsed'] - start['snapshot']['TransitionRequestedAt']
                r['spawnedWhileWaiting'] = end['snapshot']['TotalSpawned'] - start['snapshot']['TotalSpawned']
        role_report = report[role]
        # Reconnect can publish an empty snapshot while the same round is restoring.
        # Keep the last observed nonempty RunId; retain the original comparison too.
        known_run = 'unassigned'
        observed_births = {}
        carried_ids = []
        empty_snapshot = False
        for entry in read(folder / (role + '-audit.jsonl')):
            if entry['kind'] == 'frame':
                value = entry['snapshot']['RunId']
                empty_snapshot = not value
                if value: known_run = value
            elif entry['kind'] == 'birth':
                key = known_run + '/' + str(entry['id'])
                observed_births[key] = {k: entry[k] for k in ('source', 'hp', 'damage', 'speed', 'xp')}
                if empty_snapshot: carried_ids.append(key)
        known_run_births[role] = observed_births
        role_report['birthsDuringEmptySnapshot'] = carried_ids
        role_report['transitionRounds'] = rounds
        role_report['spawnAttemptsByClip'] = role_report.pop('spawnByClip')
        role_report['spawnAttemptsByIdentity'] = role_report.pop('spawnByIdentity')
        role_report['spawnResultsByRun'] = {}
        for file in sorted(folder.glob('spawns-*.csv')):
            entries = list(csv.DictReader(file.open(encoding='utf-8-sig')))
            attempts = [e for e in entries if e['event'] == 'spawn']
            successful = [e for e in attempts if e['result'] == 'Spawned']
            role_report['spawnResultsByRun'][file.stem] = dict(
                attempts=len(attempts), successful=len(successful), results=dict(Counter(e['result'] for e in attempts)),
                successfulByClip=dict(Counter(e['clip'] for e in successful)),
                sourceCountPeak=max((int(e['countedAlive']) for e in successful), default=0),
                totalAlivePeakAtSpawns=max((int(e['totalAlive']) for e in successful), default=0))
            if 'firstCompletedRound' in role_report and 'firstCompletedSpawnFile' not in role_report and any(e['event'] == 'completed' for e in entries):
                role_report['firstCompletedSpawnFile'] = file.name
                counts = Counter(e['event'] for e in entries)
                end = next(e for e in entries if e['event'] == 'completed')
                kills = role_report['firstCompletedRound']['confirmedKills']
                role_report['firstCompletedRound']['counting'] = dict(
                    successful=len(successful), confirmedKills=kills, selfDestructs=counts['self-destruct'],
                    retired=counts['retired'], alive=int(end['totalAlive']), sourceCount=int(end['countedAlive']),
                    conservationDelta=len(successful)-kills-counts['self-destruct']-counts['retired']-int(end['totalAlive']))
        raw = (folder / 'player.log').read_text(encoding='utf-8-sig', errors='replace')
        role_report['transitionServerLog'] = re.findall(r'^\[LimboTransition\].*$', raw, re.M)
        role_report['runEndLog'] = re.findall(r'^\[RunEnd\].*$', raw, re.M)
        traps = {}
        for e in read(folder / 'spatial-observation.jsonl'):
            if e['kind'] not in ('phase', 'sample'):
                continue
            state = e['state']; key = e['run'] + '/' + str(e['id'])
            t = traps.setdefault(key, dict(barrier=state['Barrier'], phases=[], centers=set(), edgePointCounts=set(),
                                          minRadius=state['Radius'], maxRadius=state['Radius'], collision=False, first=e['elapsed'], last=e['elapsed']))
            if state['Phase'] not in t['phases']: t['phases'].append(state['Phase'])
            t['centers'].add(tuple(state['Center'].values())); t['edgePointCounts'].add(len(e.get('worldEdgePoints') or []))
            t['minRadius'] = min(t['minRadius'], state['Radius']); t['maxRadius'] = max(t['maxRadius'], state['Radius'])
            t['collision'] |= state['Collision']; t['last'] = e['elapsed']
        for t in traps.values():
            t['centers'] = sorted(t['centers']); t['edgePointCounts'] = sorted(t['edgePointCounts'])
        role_report['spatialPaths'] = traps
        role_report['classification'] = dict(
            confirmedKills=sum(e['kind'] == 'enemy-death' for e in read(folder / 'stage2-observation.jsonl')),
            selfDestructs=role_report['spawnEvents'].get('self-destruct', 0),
            expiry=role_report['spawnEvents'].get('retired', 0))
    if 'client' in known_run_births:
        host, client = known_run_births['host'], known_run_births['client']
        report['knownRunBirthComparison'] = dict(
            missingClientBirthIds=sorted(host.keys() - client.keys()),
            extraClientBirthIds=sorted(client.keys() - host.keys()),
            differentBirths=[i for i in host.keys() & client.keys() if host[i] != client[i]],
            unassignedBirthIds=sorted(i for i in host.keys() | client.keys() if i.startswith('unassigned/')))
    return report


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('directory', type=Path)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    args.output.write_text(json.dumps(summarize(args.directory), ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(args.output)
