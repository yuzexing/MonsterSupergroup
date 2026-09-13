"""Import only Midgard visual dependencies. Requires PyYAML; no source code/DLL import.

Run before Tools > Nordic > Build Static Sample in Unity. Existing generated assets
may be refreshed, but foreign GUIDs at a destination are rejected.
"""
import argparse
import csv
import hashlib
import json
import re
import uuid
from pathlib import Path
import yaml

PROJECT = Path(__file__).resolve().parents[1]
OUTPUT = 'Assets/_Project/Content/Nordic'
NAMESPACE = uuid.UUID('bd4400a1-1ca2-4d6c-9ebd-3043019f4cf8')
HEADER = '%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n'


def guid(key):
    return uuid.uuid5(NAMESPACE, key).hex


def blocks(path):
    source = path.read_text(encoding='utf-8-sig')
    matches = list(re.finditer(r'^--- !u!(\d+) &(-?\d+)\s*$', source, re.M))
    result = []
    for i, m in enumerate(matches):
        raw = source[m.end():matches[i + 1].start() if i + 1 < len(matches) else len(source)]
        safe = re.sub(r'(?m)^(\s*(?:m_IndexBuffer|_typelessdata): )([0-9a-fA-F]+)\s*$', r'\1"\2"', raw)
        parsed = yaml.load(safe, Loader=yaml.CSafeLoader)
        kind, data = next(iter(parsed.items()))
        result.append((int(m[1]), int(m[2]), kind, data, raw))
    return result


def write_asset(relative, data, asset_guid, meta=None):
    path = PROJECT / relative
    path.parent.mkdir(parents=True, exist_ok=True)
    existing_meta = Path(str(path) + '.meta')
    if path.exists():
        if not existing_meta.exists() or not re.search(r'^guid: ' + asset_guid + '$', existing_meta.read_text(), re.M):
            raise RuntimeError('Foreign asset at destination: ' + str(path))
    path.write_bytes(data if isinstance(data, bytes) else data.encode('utf-8'))
    if meta is None:
        meta = 'fileFormatVersion: 2\nguid: ' + asset_guid + '\n'
    existing_meta.write_text(meta, encoding='utf-8')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source', default='F:/DecomplieLatest/NordicAshes/ExportedProject')
    parser.add_argument('--stage', choices=['smoke', 'full'], default='full')
    args = parser.parse_args()
    source = Path(args.source)
    target_layers = blocks(PROJECT / 'ProjectSettings/TagManager.asset')[0][3]['layers']
    index = {}
    for p in (source / 'Assets').rglob('*.meta'):
        match = re.search(r'^guid: ([0-9a-f]{32})', p.read_text(encoding='utf-8-sig'), re.M)
        if match:
            index[match[1]] = p.with_suffix('').relative_to(source).as_posix()
    rows = list(csv.DictReader((source / 'Docs/SceneAnalysis/midgard-props.csv').open(encoding='utf-8-sig')))
    if args.stage == 'smoke':
        rows = [r for r in rows if r['propData'] in ['Assets/MonoBehaviour/Tree_1.asset', 'Assets/MonoBehaviour/Composition_Grass_1.asset']]
    records, mapping, sprite_records, prefabs = [], {}, [], []
    character = None

    def copy_visual(old_guid):
        if old_guid in mapping:
            return mapping[old_guid]
        src = index[old_guid]
        if not src.startswith(('Assets/Sprite/', 'Assets/Texture2D/')):
            raise RuntimeError('Unexpected visual dependency: ' + src)
        kind = 'Sprites' if src.startswith('Assets/Sprite/') else 'Textures'
        dst = OUTPUT + '/' + kind + '/' + Path(src).name
        new_guid = guid(src)
        mapping[old_guid] = new_guid
        data = (source / src).read_bytes()
        if kind == 'Sprites':
            item = blocks(source / src)[0][3]
            for dependency in set(re.findall(r'guid: ([0-9a-f]{32})', data.decode('utf-8-sig'))):
                copy_visual(dependency)
            content = data.decode('utf-8-sig')
            for old, new in mapping.items():
                content = content.replace(old, new)
            data = content.encode('utf-8')
            sprite_records.append(dict(path=dst, source=src, ppu=item['m_PixelsToUnits'],
                                       width=item['m_Rect']['width'], height=item['m_Rect']['height'],
                                       vertexCount=item['m_RD']['m_VertexData']['m_VertexCount'],
                                       pivotX=item['m_Pivot']['x'], pivotY=item['m_Pivot']['y']))
        meta = Path(str(source / src) + '.meta').read_text(encoding='utf-8-sig').replace(old_guid, new_guid)
        write_asset(dst, data, new_guid, meta)
        records.append(dict(source=src, target=dst, sourceGuid=old_guid, targetGuid=new_guid,
                            sourceSha256=hashlib.sha256((source / src).read_bytes()).hexdigest()))
        return new_guid

    # These are target pipeline materials, never the exported placeholder shaders.
    package = next((PROJECT / 'Library/PackageCache').glob('com.unity.render-pipelines.universal@*'))
    for label in ['Lit', 'Unlit']:
        template = package / ('Runtime/Materials/Sprite-' + label + '-Default.mat')
        data = template.read_text(encoding='utf-8-sig').replace('m_Name: Sprite-' + label + '-Default', 'm_Name: Nordic ' + label)
        write_asset(OUTPUT + '/Materials/Nordic' + label + '.mat', data, guid('material/' + label))

    floor_paths = ['Assets/Sprite/fondo base_V3.asset', 'Assets/Sprite/terreno dibujado7.asset', 'Assets/Sprite/terreno dibujado_rocas2.asset']
    for path in floor_paths[:1] if args.stage == 'smoke' else floor_paths:
        old = re.search(r'^guid: (\w+)', Path(str(source / path) + '.meta').read_text(), re.M)[1]
        copy_visual(old)

    if args.stage == 'full':
        rows.append(dict(prefab='Assets/GameObject/Viking (Character Variant).prefab', group='10'))
    for row in rows:
        path, group = row['prefab'], int(row['group'])
        docs = blocks(source / path)
        if group == 10:
            # Keep the actual Axeldor combat visual hierarchy, excluding the player,
            # collectors, combat VFX and HUD. Animation bindings are relative to Scaler.
            transforms = {d[1]: d[3] for d in docs if d[2] == 'Transform'}
            go_names = {d[1]: d[3]['m_Name'] for d in docs if d[2] == 'GameObject'}
            scaler = next(tid for tid, t in transforms.items() if go_names[t['m_GameObject']['fileID']] == 'Scaler')
            keep_transforms = {scaler}
            def collect(tid):
                keep_transforms.add(tid)
                for child in transforms[tid]['m_Children']:
                    collect(child['fileID'])
            for child in transforms[scaler]['m_Children']:
                tid = child['fileID']
                if go_names[transforms[tid]['m_GameObject']['fileID']] in ['Shadow', 'PlayerSprite']:
                    collect(tid)
            keep_gos = {transforms[tid]['m_GameObject']['fileID'] for tid in keep_transforms}
            docs = [d for d in docs if (d[2] == 'GameObject' and d[1] in keep_gos) or d[3].get('m_GameObject', {}).get('fileID') in keep_gos]
        names = {i: d['m_Name'] for _, i, k, d, _ in docs if k == 'GameObject'}
        keep = []
        lights = []
        for class_id, item_id, kind, d, raw in docs:
            allowed = kind in ['GameObject', 'Transform', 'SpriteRenderer']
            if kind in ['PolygonCollider2D', 'BoxCollider2D']:
                allowed = group in [1, 2, 4, 6] and d.get('m_IsTrigger', 0) == 0
            if group in [6, 7] and kind in ['ParticleSystem', 'ParticleSystemRenderer']:
                allowed = True
            if group in [6, 7] and kind == 'MonoBehaviour' and 'm_LightType' in d:
                lights.append(dict(node=names[d['m_GameObject']['fileID']], r=d['m_Color']['r'],
                                   g=d['m_Color']['g'], b=d['m_Color']['b'], intensity=d['m_Intensity'],
                                   inner=d['m_PointLightInnerRadius'], outer=d['m_PointLightOuterRadius'],
                                   falloff=d['m_FalloffIntensity']))
            if allowed:
                keep.append((class_id, item_id, kind, d, raw))
        retained = {d[1] for d in keep}
        rendered = []
        for class_id, item_id, kind, d, raw in keep:
            if kind == 'GameObject':
                raw = re.sub(r'(?m)^  - component: \{fileID: (-?\d+)\}\n',
                             lambda m: m[0] if int(m[1]) in retained else '', raw)
                layer = target_layers.index('Player' if group == 10 else 'Obstacles' if group in [1, 2, 4, 6] else 'Background')
                raw = re.sub(r'(?m)^  m_Layer: .*$', '  m_Layer: ' + str(layer), raw)
                raw = re.sub(r'(?m)^  m_TagString: .*$', '  m_TagString: Untagged', raw)
            if kind == 'Transform' and group == 10:
                raw = re.sub(r'(?m)^  - \{fileID: (-?\d+)\}\n', lambda m: m[0] if int(m[1]) in keep_transforms else '', raw)
                if item_id == scaler:
                    raw = re.sub(r'(?m)^  m_Father: .*$', '  m_Father: {fileID: 0}', raw)
            if kind in ['SpriteRenderer', 'ParticleSystemRenderer']:
                node = names[d['m_GameObject']['fileID']]
                material = 'Unlit' if group in [6, 7] and node in ['Flame', 'Inner', 'llama antorcha'] else 'Lit'
                if kind == 'ParticleSystemRenderer':
                    material = 'Unlit'
                raw = re.sub(r'(?m)^  m_Materials:\n(?:  - .*\n)+',
                             '  m_Materials:\n  - {fileID: 2100000, guid: ' + guid('material/' + material) + ', type: 2}\n', raw)
                raw = re.sub(r'(?m)^  m_MaskInteraction: .*$', '  m_MaskInteraction: 0', raw)
                raw = re.sub(r'(?m)^  m_SortingLayerID: .*$', '  m_SortingLayerID: 0', raw)
                raw = re.sub(r'(?m)^  m_SortingLayer: .*$', '  m_SortingLayer: 0', raw)
                if kind == 'SpriteRenderer':
                    raw = re.sub(r'(?m)^  m_SpriteSortPoint: .*$', '  m_SpriteSortPoint: 1', raw)
            # Removed helpers have no retained references. External refs must be visual-only.
            for dep in set(re.findall(r'guid: ([0-9a-f]{32})', raw)):
                if dep in [guid('material/Lit'), guid('material/Unlit')]:
                    continue
                if dep not in index:
                    raise RuntimeError('Unresolved dependency in ' + path + ': ' + dep)
                raw = raw.replace(dep, copy_visual(dep))
            rendered.append('--- !u!' + str(class_id) + ' &' + str(item_id) + raw)
        dst = OUTPUT + '/Prefabs/' + Path(path).name
        meta = 'fileFormatVersion: 2\nguid: ' + guid(path) + '\nPrefabImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n'
        write_asset(dst, HEADER + ''.join(rendered), guid(path), meta)
        entry = dict(path=dst, source=path, group=group, lights=lights,
                     sourceSpriteCount=sum(d[2] == 'SpriteRenderer' for d in keep),
                     transformCount=sum(d[2] == 'Transform' for d in keep))
        if group == 10:
            character = entry
        else:
            prefabs.append(entry)
        records.append(dict(source=path, target=dst, sourceGuid=re.search(r'^guid: (\w+)', Path(str(source / path)+'.meta').read_text(), re.M)[1],
                            targetGuid=guid(path), removedComponents=sorted({d[2] for d in docs if d[1] not in retained})))
    if args.stage == 'full':
        for clip in ['Viking_Idle_Player', 'Viking_Walk_Player']:
            src = 'Assets/AnimationClip/' + clip + '.anim'
            data = (source / src).read_text(encoding='utf-8-sig')
            for old in set(re.findall(r'guid: ([0-9a-f]{32})', data)):
                data = data.replace(old, copy_visual(old))
            # Source audio/footstep callbacks rely on omitted character controllers.
            data = re.sub(r'(?ms)^  m_Events:\n.*?(?=^  \w|\Z)', '  m_Events: []\n', data)
            dst = OUTPUT + '/Animations/' + clip + '.anim'
            old_guid = re.search(r'^guid: (\w+)', Path(str(source / src) + '.meta').read_text(), re.M)[1]
            meta = Path(str(source / src) + '.meta').read_text().replace(old_guid, guid(src))
            write_asset(dst, data, guid(src), meta)
            records.append(dict(source=src, target=dst, sourceGuid=old_guid, targetGuid=guid(src), removedAnimationEvents=True))
    manifest = dict(stage=args.stage, prefabs=prefabs, character=character, sprites=sprite_records, dependencies=records)
    write_asset(OUTPUT + '/ImportManifest.json', json.dumps(manifest, ensure_ascii=False, indent=2), guid('manifest'))
    print(json.dumps(dict(stage=args.stage, prefabs=len(prefabs), sprites=len(sprite_records),
                          visualDependencies=len(mapping), output=OUTPUT), ensure_ascii=False))


if __name__ == '__main__':
    main()
