"""Check recorded spatial fixture states, not art fidelity or gameplay pressure."""
import argparse
import json
from pathlib import Path

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('runs', nargs='+', type=Path)
args = parser.parse_args()
reports = []
for folder in args.runs:
    report = {'run': str(folder), 'roles': {}, 'failures': []}
    for role in ('host', 'client'):
        source = folder / role / 'spatial-observation.jsonl'
        if not source.exists():
            report['failures'].append(f'{role}: no spatial observations')
            continue
        rows = [json.loads(line) for line in source.read_text(encoding='utf-8-sig').splitlines() if line.strip()]
        ends = [row for row in rows if row['kind'] == 'completed-cleanup']
        report['roles'][role] = {'completedRuns': [row['run'] for row in ends],
            'maximumSlots': max((row['slots'] for row in rows), default=0),
            'barrierContactSamples': sum(row.get('localPlayerTouchesBarrier', False) for row in rows)}
        if not ends:
            report['failures'].append(f'{role}: no completed cleanup')
        for row in ends:
            for field in ('slots', 'states', 'collisionAreas', 'livingWarningRoots'):
                if row[field] != 0:
                    report['failures'].append(f'{role}/{row["run"]}: {field}={row[field]}')
        if any(row['slots'] > 1 for row in rows):
            report['failures'].append(f'{role}: simultaneous trap slots')
    actions = folder / 'host' / 'spatial-actions.jsonl'
    if actions.exists():
        rows = [json.loads(line) for line in actions.read_text(encoding='utf-8-sig').splitlines() if line.strip()]
        report['actions'] = [{'kind': r['kind'], 'phase': r['phase'], 'passed': r['passed']} for r in rows]
        report['failures'] += [f'{r["kind"]}/{r["phase"]}: recorded failure' for r in rows if not r['passed']]
    report['technicalStateChecksPassed'] = not report['failures']
    (folder / 'spatial-matrix-review.json').write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    reports.append(report)
print(json.dumps(reports, ensure_ascii=False, indent=2))
