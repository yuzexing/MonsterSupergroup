"""Read numeric trap evidence only. Never modify the source export or an existing adapted file."""
import hashlib
import json
import pathlib
import re

PROJECT = pathlib.Path(__file__).resolve().parents[1]
SOURCE = pathlib.Path('F:/DecomplieLatest/HellMaiden/ExportedProject/Assets')
OUTPUT = PROJECT / 'Assets/_Project/Content/NetworkCombat/Limbo/Spatial'

def scalar(text, field):
    return float(re.search(r'^\s*' + re.escape(field) + r': ([\d.eE+-]+)$', text, re.M)[1])

def curve(text, field):
    match = re.search(r'^    ' + field + r':\n      serializedVersion: \d+\n      minMaxState: (\d+)\n      scalar: ([\d.eE+-]+)\n      minScalar: ([\d.eE+-]+)', text, re.M)
    if not match or int(match[1]) not in (0, 3):
        raise ValueError('Unsupported particle curve: ' + field)
    return {'mode': int(match[1]), 'maximum': float(match[2]), 'minimum': float(match[3])}

def main():
    paths = ['GameObject/BarrierTrap.prefab', 'GameObject/FireParticles.prefab', 'GameObject/Burst Spawner.prefab',
             'Scripts/Assembly-CSharp/AstralShift/HellMaiden/Combat/Spawners/BarrierTrapSpawner.cs',
             'Scripts/Assembly-CSharp/AstralShift/HellMaiden/Combat/Spawners/TrapSpawner.cs']
    evidence = [{'path': str(SOURCE / p), 'sha256': hashlib.sha256((SOURCE / p).read_bytes()).hexdigest()} for p in paths]
    barrier = (SOURCE / paths[0]).read_text(encoding='utf-8-sig')
    particles = (SOURCE / paths[1]).read_text(encoding='utf-8-sig')
    burst = (SOURCE / paths[2]).read_text(encoding='utf-8-sig')
    data = {'evidence': evidence, 'sides': int(scalar(barrier, 'numberOfSides')), 'minimum': scalar(barrier, 'minRadius'),
            'maximum': scalar(barrier, 'maxRadius'), 'shrinkDuration': scalar(barrier, 'shrinkDuration'),
            'entryDuration': scalar(barrier, 'spawnAnimationDuration'), 'particleRadius': scalar(barrier, 'particleSystemRadius'),
            'cameraOffset': scalar(barrier, 'onSpawnCameraTargetOffset'), 'cameraDuration': scalar(barrier, 'onSpawnCameraTargetDuration'),
            'cameraWait': scalar(barrier, 'onSpawnCameraFramingTimeout'), 'edgeRadius': scalar(barrier, 'm_EdgeRadius'),
            'entryScale': .25, 'effectsDelay': scalar(burst, 'waitForEffectsStart'), 'activationDelay': scalar(burst, 'waitForEnemyActivation'),
            'particles': [], 'differences': []}
    for block in re.split(r'^--- ', particles, flags=re.M):
        if not block.startswith('!u!198 '): continue
        data['particles'].append({'sourceId': block.splitlines()[0].split('&')[1],
            'duration': scalar(block, 'lengthInSec'), 'lifetime': curve(block, 'startLifetime'),
            'speed': curve(block, 'startSpeed'), 'size': curve(block, 'startSize'),
            'rate': curve(block, 'rateOverTime'), 'sourceCullingMode': int(scalar(block, 'cullingMode'))})
    OUTPUT.mkdir(parents=True, exist_ok=True)
    original = OUTPUT / 'SpatialSource.json'
    serialized = json.dumps(data, ensure_ascii=False, indent=2) + '\n'
    if original.exists() and original.read_text(encoding='utf-8') != serialized:
        raise ValueError('Existing evidence differs; review before replacing it.')
    if not original.exists(): original.write_text(serialized, encoding='utf-8')
    adapted = OUTPUT / 'SpatialAdapted.json'
    data['differences'] = [
        'Existing product material and simple particle shapes; no source art or audio imported.',
        'AlwaysSimulate particle culling on all peers so authority release cannot depend on a rendering camera.',
        'Nordic perspective camera frames the captured center and source horizontal extents; source camera plugin interpolation is not reproduced.',
        'Network particle depth is zero on the current map; source trapTransform depth is 100.',
        'Only the selected target receives entry protection; all peers follow the authoritative slow-motion combat clock.']
    if not adapted.exists(): adapted.write_text(json.dumps(data, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
    print('Spatial numeric evidence exported; existing adapted values preserved.')

if __name__ == '__main__': main()
