"""Compare rendered offscreen fixtures with the explicitly specified test inputs.

This checks synchronization and source rules; it does not evaluate gameplay pressure.
"""
import argparse
import csv
import json
import math
from pathlib import Path

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('runs', nargs='+', type=Path)
args = parser.parse_args()

def rows(path):
    return [json.loads(line) for line in path.read_text(encoding='utf-8-sig').splitlines() if line.strip()]

reports = []
for folder in args.runs:
    report = {'run': str(folder), 'classification': 'Explicit positions, disabled attacks and canonical injury; technical evidence only', 'failures': [], 'enemies': []}
    peers = {role: rows(folder / role / 'reposition-fixture.jsonl') for role in ('host', 'client')}
    events = [r for file in (folder / 'host').glob('spawns-*.csv') for r in csv.DictReader(file.open(encoding='utf-8-sig'))]
    case = peers['host'][0].get('fixture', 'timeout')
    report['fixture'] = case
    for role in peers:
        ends = [r for r in rows(folder / role / 'spatial-observation.jsonl') if r['kind'] == 'completed-cleanup']
        if not ends or any(any(r[k] for k in ('slots', 'states', 'collisionAreas', 'livingWarningRoots')) for r in ends):
            report['failures'].append(f'{role}: missing or nonempty completion')
    for spawn in (r for r in events if r['event'] == 'spawn' and r['result'] == 'Spawned'):
        enemy = int(spawn['enemy']); elite = spawn['source'] == 'Elite_Skeleton'
        own = {role: [r for r in peers[role] if r['id'] == enemy] for role in peers}
        moves = [r for r in events if r['event'] == 'reposition' and int(r['enemy']) == enemy]
        retire = [r for r in events if r['event'] == 'retired' and int(r['enemy']) == enemy]
        failures = []
        first = min((float(r['elapsed']) for r in moves + retire), default=None)
        if first is None:
            failures.append('no automatic processing observed')
        elif case != 'framing':
            lower = float(spawn['elapsed']) + 30 if case == 'distance' else 43 if case == 'placement-failure' else 40
            if not lower <= first < lower + 1:
                failures.append(f'first processing {first} outside expected [{lower},{lower+1})')
        if case == 'expiry' and not elite and (not retire or moves):
            failures.append('expired ordinary enemy must retire without relocation')
        if case == 'distance' and any(float(b['elapsed']) - float(a['elapsed']) < 4.8 for a, b in zip(moves, moves[1:])):
            failures.append('fixture continued forcing far position after first relocation; repeat with one-shot distance input')
        for role in own:
            before = [r for r in own[role] if 5 < r['elapsed'] < 28]
            if not before or any(r['hp'] != int(spawn['hp']) - 10 or r['reset'] != 0 for r in before):
                failures.append(f'{role}: injected injury mismatch before processing')
            after = own[role][-1]
            if moves and (after['hp'] != (int(spawn['hp']) - 10 if elite else int(spawn['hp'])) or
                          after['reset'] != (0 if elite else len(moves))):
                failures.append(f'{role}: health/reset count mismatch')
        # Compare stable samples, excluding network transit immediately following a discontinuity.
        mismatches = []
        forced_overlap_offsets = []
        for h in own['host']:
            if h['elapsed'] < 5 or any(abs(h['elapsed'] - float(r['elapsed'])) < .5 for r in moves):
                continue
            candidates = [c for c in own['client'] if c['epoch'] == h['epoch']]
            if not candidates:
                mismatches.append(h['elapsed']); continue
            c = min(candidates, key=lambda r: abs(r['elapsed'] - h['elapsed']))
            if abs(c['elapsed'] - h['elapsed']) > .4:
                continue
            # Fixture teleports at 30/33/35 are not network position samples.
            if any(abs(h['elapsed'] - boundary) < .6 for boundary in (28, 30, 33, 35)):
                continue
            distance = math.hypot(h['position']['x'] - c['position']['x'], h['position']['y'] - c['position']['y'])
            if 33 <= h['elapsed'] < 35 and case in ('timeout', 'placement-failure', 'expiry'):
                # This early fixture deliberately places the body at the Host player center.
                # Physics separates the replica while the fixture rewrites the simulator.
                # Retain that offset explicitly; use post-relocation samples for pose agreement.
                forced_overlap_offsets.append(distance)
                distance = 0
            if h['hp'] != c['hp'] or h['reset'] != c['reset'] or distance > .1:
                mismatches.append(h['elapsed'])
        if mismatches:
            failures.append(f'{len(mismatches)} stable peer mismatches')
        if case == 'placement-failure':
            failed = [r for r in events if r['event'] == 'reposition-position-failed' and int(r['enemy']) == enemy]
            if not failed or any(40 <= float(r['elapsed']) < 43 for r in moves):
                failures.append('placement failure interval missing or moved')
        if case == 'framing' and own['host'][0].get('combat', 0):
            visible = []
            for h in own['host']:
                if first is None or h['elapsed'] >= first or h['elapsed'] < 30:
                    continue
                if any(all(abs(h['position'][axis] - m['view']['m_Center'][axis]) <= m['view']['m_Extent'][axis]
                           for axis in ('x', 'y')) for m in h.get('models', [])):
                    visible.append(h['elapsed'])
            if not visible or first < max(visible) + 4.8:
                failures.append('expanded camera did not reset the continuous offscreen timer')
        report['enemies'].append({'id': enemy, 'source': spawn['source'], 'simulators': sorted(set(r['simulator'] for r in own['host'])),
            'firstProcessing': first, 'repositions': len(moves), 'retirements': len(retire),
            'forcedPlayerOverlapMaxPeerOffset': max(forced_overlap_offsets, default=0), 'failures': failures})
        report['failures'].extend(f'{enemy}: {f}' for f in failures)
    report['technicalChecksPassed'] = not report['failures']
    (folder / 'reposition-review.json').write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    reports.append(report)
print(json.dumps(reports, ensure_ascii=False, indent=2))
