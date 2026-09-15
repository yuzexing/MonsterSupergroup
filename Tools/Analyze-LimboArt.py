"""Summarize actual rendered art runs. Never awards a visual or gameplay pass."""
import collections
import json
import re
import sys
from pathlib import Path

PROJECT = Path(__file__).resolve().parents[1]
base = PROJECT / 'Logs/LimboReference'
roots = [base / arg for arg in sys.argv[1:]] if len(sys.argv) > 1 else sorted({p for pattern in ('art-final-*','art-retest-*','art-delivery-*','art-preview-*','art-release2-*','art-release3-*','art-release4-*','art-release5-*') for p in base.glob(pattern)})


def read_rows(path):
    if not path.exists():
        return []
    # A concurrently written last line is not evidence until complete.
    return [json.loads(line) for line in path.read_text(encoding='utf-8-sig').splitlines() if line.endswith('}')]


def distribution(values):
    values = sorted(values)
    if not values:
        return None
    return dict(min=values[0], p50=values[int((len(values)-1)*.5)],
                p95=values[int((len(values)-1)*.95)], max=values[-1])


result = {}
for root in roots:
    run = {}
    births = {}
    for role in ('host', 'client'):
        folder = root / role
        rows = read_rows(folder / 'art-observation.jsonl')
        if not rows:
            continue
        bodies = [r for r in rows if r['kind'] == 'body']
        frames = [r for r in rows if r['kind'] == 'frame']
        death_rows = [r for r in rows if r['kind'] == 'death']
        audit = read_rows(folder / (role+'-audit.jsonl'))
        births[role] = [r for r in audit if r['kind'] == 'birth']
        variants = {}
        for key in sorted({(r['identity'], r['variant']) for r in bodies}):
            group = [r for r in bodies if (r['identity'], r['variant']) == key]
            variants[str(key)] = dict(samples=len(group), ids=sorted({r['id'] for r in group}),
                clips=sorted({r['clip'] for r in group}), textures=sorted({r['texture'] for r in group}),
                luts=sorted({r['lut'] for r in group}), shaders=sorted({r['shader'] for r in group}),
                rotation=distribution([min(abs(r['rotation']), abs(360-r['rotation'])) for r in group]),
                alpha=distribution([r['color']['a'] for r in group]))
        text = (folder/'player.log').read_text(encoding='utf-8-sig', errors='replace')
        errors = sorted(set(re.findall(r'^.*(?:Exception:|Assertion failed|Shader is null|MissingReferenceException).*$' ,text,re.M)))
        run[role] = dict(variants=variants, birthCount=len(births[role]),
            deathPresentation=dict(samples=len(death_rows), ids=sorted({r['id'] for r in death_rows}),
                clips=sorted({r['clip'] for r in death_rows}),
                normalized=distribution([r['normalized'] for r in death_rows]),
                shadowAlpha=distribution([r['shadowAlpha'] for r in death_rows]),
                aliveOrDamaging=[r for r in death_rows if r['alive'] or r['enabledDamageAreas'] != 0]),
            birthMismatches=[r for r in births[role] if r.get('match') is not True],
            finalFrame=next((r for r in reversed(audit) if r['kind']=='frame'),None),
            sampledFrameMs=distribution([r['frameMs'] for r in frames]),
            allocatedBytes=distribution([r['allocated'] for r in frames]),
            particles=distribution([r['particles'] for r in frames]),
            particleSystems=distribution([r['particleSystems'] for r in frames]),
            captures=sorted(p.name for p in folder.glob('*.png')), errors=errors,
            cleanup=[r for r in read_rows(folder/'stage2-observation.jsonl')+read_rows(folder/'spatial-observation.jsonl') if r.get('kind')=='completed-cleanup'])
    if len(births)==2:
        normalize = lambda values: sorted([{k:v for k,v in r.items() if k!='role'} for r in values],key=lambda r:r['id'])
        run['sameBirths'] = normalize(births['host']) == normalize(births['client'])
    if run:
        result[root.name]=run
out = PROJECT/'Logs/LimboArt/rendered-summary.json'
out.parent.mkdir(parents=True,exist_ok=True)
out.write_text(json.dumps(dict(note='Technical fixtures. 1 Hz sampled frame duration, not full-frame performance profiling. Parallel processes and audit overhead apply. Screenshots require visual review.',runs=result),ensure_ascii=False,indent=2),encoding='utf-8')
for name, run in result.items():
    print(name, json.dumps({role:{'births':r['birthCount'],'mismatches':len(r['birthMismatches']),'errors':len(r['errors']), 'captures':len(r['captures']), 'end':r['finalFrame']['snapshot'] if r['finalFrame'] else None} for role,r in run.items() if role in ('host','client')},ensure_ascii=False))
