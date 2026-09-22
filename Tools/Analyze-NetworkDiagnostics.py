"""Summarize v1/v2 Steam diagnostics. Missing metrics stay unknown; correlations are not causes."""
import argparse
import csv
import json
from collections import defaultdict
from pathlib import Path


def read(path):
    header, rows, malformed = {}, [], 0
    with path.open(encoding='utf-8-sig') as stream:
        for line in stream:
            try:
                row = json.loads(line)
            except json.JSONDecodeError:
                malformed += 1
                continue
            if row.get('kind') == 'header':
                header = row
            elif row.get('kind') == 'sample':
                rows.append(row)
    status_path = Path(str(path) + '.status.json')
    try:
        status = json.loads(status_path.read_text(encoding='utf-8-sig')) if status_path.exists() else None
    except json.JSONDecodeError:
        status = {'complete': False, 'failure': 'Malformed status file'}
    return header, rows, malformed, status


def maximum(rows, key):
    values = [r[key] for r in rows if isinstance(r.get(key), (float, int)) and r[key] >= 0]
    return max(values) if values else None


def delta(row, previous, key):
    if previous is None or key not in row or key not in previous:
        return None
    return max(0, row[key] - previous[key])


def point(row, previous=None):
    connections = row.get('connections', [])
    queue = maximum(connections, 'queueMilliseconds')
    warnings = delta(row, previous, 'warnings')
    result = {k: row.get(k) for k in ('utc', 'time', 'networkTime', 'windowSeconds', 'role', 'run', 'round',
        'phase', 'alive', 'playerCount', 'localHealth', 'localAlive', 'focused', 'overlayState', 'rttMs', 'frameMeanMs',
        'frameMaxMs', 'frameP95Ms', 'mainMaxMs', 'gpuMs', 'renderMs', 'mainWorkMs', 'presentWaitMs',
        'allocatedBytes', 'gcMs', 'managedBytes', 'workingSetBytes', 'privateBytes', 'longFrameCount', 'omittedLongFrames')}
    result.update(queueMs=queue, warningsDelta=warnings, deadWarningsDelta=delta(row, previous, 'deadWarnings'),
        pendingReliableBytes=maximum(connections, 'pendingReliableBytes'),
        pendingUnreliableBytes=maximum(connections, 'pendingUnreliableBytes'),
        unacknowledgedBytes=maximum(connections, 'unacknowledgedBytes'),
        areas=row.get('areas'), signals=[])
    elapsed = row['time'] - previous['time'] if previous else 0
    for key in ('sentBytes', 'receivedBytes'):
        result[key+'PerSecond'] = (max(0, sum(row[key])-sum(previous[key])) / elapsed
            if elapsed > 0 and key in row and key in previous else None)
    for key, threshold, label in (('mainMaxMs', 33.33, 'main-thread'), ('gpuMs', 33.33, 'gpu'),
                                  ('gcMs', 5, 'gc'), ('presentWaitMs', 25, 'present-wait')):
        if row.get(key, -1) > threshold:
            result['signals'].append(label)
    if warnings is not None and warnings > 100:
        result['signals'].append('warning-storm')
    if queue is not None and queue > 100:
        result['signals'].append('network-queue')
    return result


def summarize(path):
    header, rows, malformed, status = read(path)
    groups = defaultdict(list)
    for row in rows:
        groups[(row.get('role'), row.get('run'), row.get('round'))].append(row)
    runs = []
    for (role, run, round_id), samples in groups.items():
        samples.sort(key=lambda r: r['time'])
        connections = [c for row in samples for c in row.get('connections', [])]
        elapsed = samples[-1]['time'] - samples[0]['time']
        rates = {}
        for field in ('sentBytes', 'receivedBytes'):
            rates[field + 'PerSecond'] = [max(0, samples[-1][field][i] - samples[0][field][i]) / elapsed
                for i in range(2)] if elapsed > 0 and field in samples[0] and field in samples[-1] else None
        frames = sum(r['frameCount'] for r in samples)
        histogram = [sum(r.get('frameHistogram', [0]*7)[i] for r in samples) for i in range(7)]
        slow, previous = [], None
        for row in samples:
            if row['frameMaxMs'] > 100 or row['frameMeanMs'] > 33.33:
                slow.append(point(row, previous))
            previous = row
        runs.append({
            'role': role, 'run': run, 'round': round_id, 'samples': len(samples),
            'sampleIntervalSeconds': elapsed, 'maxAlive': maximum(samples, 'alive'),
            'frameMeanMs': sum(r['frameMeanMs']*r['frameCount'] for r in samples)/frames if frames else None,
            'worstWindowP95Ms': maximum(samples, 'frameP95Ms'), 'worstWindowP99Ms': maximum(samples, 'frameP99Ms'),
            'maxFrameMs': maximum(samples, 'frameMaxMs'), 'frameOverflow': sum(r.get('frameOverflow', 0) for r in samples),
            'frameHistogram': histogram if any('frameHistogram' in r for r in samples) else None,
            'longFrames': sum(r.get('longFrameCount', 0) for r in samples) if header.get('schemaVersion', 1) >= 2 else None,
            'omittedLongFrames': sum(r.get('omittedLongFrames', 0) for r in samples),
            'maxQueueMs': maximum(connections, 'queueMilliseconds'),
            'maxPendingReliableBytes': maximum(connections, 'pendingReliableBytes'),
            'maxPendingUnreliableBytes': maximum(connections, 'pendingUnreliableBytes'),
            'maxUnacknowledgedBytes': maximum(connections, 'unacknowledgedBytes'),
            'maxPendingDeaths': maximum(samples, 'pendingDeaths'), 'lastPendingDeaths': samples[-1].get('pendingDeaths'),
            'maxDeathWaitSeconds': maximum(samples, 'oldestPendingDeathSeconds'),
            'lastRejections': samples[-1].get('rejections'),
            'sendFailuresDelta': delta(samples[-1], samples[0], 'sendFailures'),
            'reliableSnapshotPacketsDelta': delta(samples[-1], samples[0], 'reliableSnapshotPackets'),
            'slowWindows': slow, **rates})
    return {'file': str(path), 'header': header, 'writerStatus': status, 'malformedLines': malformed,
            'evidenceComplete': bool(status and status.get('complete') and not malformed), 'runs': runs}


def align(paths):
    """Pair same-build/run/round windows by synchronized network time, not unrelated PC uptime."""
    data = [(str(path), *read(path)[:2]) for path in paths]
    matches, unavailable = [], []
    for name, header, rows in data:
        for row in rows:
            if not row.get('run') or (row['frameMaxMs'] <= 100 and row['frameMeanMs'] <= 33.33):
                continue
            guid = header.get('buildGuid')
            if not guid or guid == '0'*32 or 'networkTime' not in row:
                unavailable.append({'file': name, 'time': row['time'], 'reason': 'Missing valid build identity or network time'})
                continue
            peers = []
            for other, peer_header, peer_rows in data:
                if other == name or peer_header.get('buildGuid') != guid:
                    continue
                candidates = [p for p in peer_rows if p.get('run') == row['run'] and p.get('round') == row.get('round') and 'networkTime' in p]
                if not candidates:
                    continue
                nearest = min(candidates, key=lambda p: abs(p['networkTime'] - row['networkTime']))
                distance = abs(nearest['networkTime'] - row['networkTime'])
                if distance <= 1.5:
                    peers.append({'file': other, 'networkTimeDifferenceSeconds': distance,
                                  'rttMs': nearest.get('rttMs'), 'sample': point(nearest)})
            matches.append({'file': name, 'sample': point(row), 'peers': peers})
    return {'method': 'Nearest sample within 1.5 s; same nonzero buildGuid, run, round. Approximate correlation, not causality.',
            'slowWindows': matches, 'unavailable': unavailable}


def write_timeline(paths, destination):
    records = []
    for path in paths:
        header, rows, _, _ = read(path)
        previous = None
        for row in rows:
            record = point(row, previous)
            record.update(file=str(path), buildGuid=header.get('buildGuid'), captureId=header.get('captureId'))
            record['areas'] = json.dumps(record['areas'], separators=(',', ':'))
            record['signals'] = ','.join(record['signals'])
            records.append(record)
            previous = row
    records.sort(key=lambda r: (r.get('utc') or '', r['file'], r['time']))
    if records:
        with destination.open('w', encoding='utf-8-sig', newline='') as stream:
            writer = csv.DictWriter(stream, fieldnames=list(records[0])); writer.writeheader(); writer.writerows(records)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('files', nargs='+', type=Path)
    parser.add_argument('--output', type=Path)
    parser.add_argument('--timeline-csv', type=Path)
    args = parser.parse_args()
    result = {'reports': [summarize(path) for path in args.files], 'alignment': align(args.files)}
    text = json.dumps(result, ensure_ascii=False, indent=2)
    if args.output:
        args.output.write_text(text + '\n', encoding='utf-8')
    if args.timeline_csv:
        write_timeline(args.files, args.timeline_csv)
    print(text)

if __name__ == '__main__':
    main()
