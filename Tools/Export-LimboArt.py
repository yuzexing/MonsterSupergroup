"""Import the approved visual dependency closure, without importing gameplay scripts.

Original assets/evidence are read-only. Existing shared assets are reused by GUID;
materials and visual templates get independent deterministic GUIDs. Re-running
never overwrites an adaptation. ArtSource.json describes source facts only.
"""
import hashlib
import json
import re
from pathlib import Path

PROJECT = Path(__file__).resolve().parents[1]
SOURCE = Path('F:/DecomplieLatest/HellMaiden/ExportedProject/Assets')
ROOT = PROJECT / 'Assets/_Project/Content/NetworkCombat/Limbo/Art'
EVIDENCE = PROJECT / 'docs/evidence/hellmaiden-attacks/runtime-assets.json'
GUID = re.compile(r'guid: ([a-f0-9]{32})')
BODIES = [
    ('E1', 'Enemy_Brotchi', 'Brotchi', 'Stage2/ReferenceBrotchi.prefab', True),
    ('E2', 'Enemy_Brotchi_LVL2', 'Brotchi_Dash', 'Dash/ReferenceBrotchiDash.prefab', False),
    ('E3', 'Enemy_Imp', 'Imp', 'Imp/ReferenceImp.prefab', False),
    ('E4', 'Enemy_Skeleton', 'Skeleton', 'Stage2/ReferenceSkeleton.prefab', False),
    ('E5', 'Elite_Skeleton', 'Elite_Skeleton', 'Stage2/ReferenceElite_Skeleton.prefab', False),
    ('E6', 'Enemy_Slime', 'Slime', 'Stage2/ReferenceSlime.prefab', True),
    ('E7', 'Enemy_Slime Rusher', 'Slime', 'Stage2/ReferenceRusher.prefab', True),
    ('E8', 'Enemy_LostSoul', 'LostSoul', 'LostSoul/ReferenceLostSoul.prefab', False),
    ('E9', 'Enemy_Ghoul', 'Ghoul', 'Ghoul/ReferenceGhoul.prefab', False),
]
EFFECTS = [('V1', 'Arrow'), ('V2', 'Skeleton_Warning'), ('V3', 'Skeleton_Warning_Elite Variant'),
           ('V4', 'EnemyBulletAttackImp'), ('V5', 'soul enemy warning'), ('V5', 'Enemy_Bomb_ExplosionAttack 1'),
           ('V6', 'Ghoul_Warning'), ('V7,V8', 'FireParticles')]


def sha(p):
    return hashlib.sha256(p.read_bytes()).hexdigest()


def field(value, name):
    if value is None:
        return None
    return next((v for k, v in value.get('fields', value).items() if k.endswith('::' + name)), None)


def references(value, kind):
    if isinstance(value, dict):
        if value.get('type') == kind and 'reference' in value:
            yield value
        for v in value.values():
            yield from references(v, kind)
    elif isinstance(value, list):
        for v in value:
            yield from references(v, kind)


def main():
    source, current = {}, {}
    for root, index in [(SOURCE, source), (PROJECT / 'Assets', current)]:
        for p in root.rglob('*.meta'):
            m = re.search(r'^guid: (\w+)', p.read_text(encoding='utf-8-sig', errors='ignore'), re.M)
            if m:
                index[m[1]] = Path(str(p)[:-5])
    reverse = {p: g for g, p in source.items()}
    shader = PROJECT / 'Assets/Plugins/AllIn1SpriteShader/Shaders/AllIn1SpriteShader.shader'
    shader_guid = GUID.search(Path(str(shader) + '.meta').read_text())[1]
    entries, visited = {}, {}
    previous_path = ROOT / 'ArtSource.json'
    previous = {e['sourceGuid']: e for e in json.loads(previous_path.read_text(encoding='utf-8'))['entries']} if previous_path.exists() else {}

    def guid_for(g):
        p = source[g]
        if p.suffix == '.shader':
            return shader_guid
        if p.suffix in ('.mat', '.prefab') or (p.suffix == '.anim' and p.stem in warning_names):
            return hashlib.md5(('limbo-reference-art-v1:' + g).encode()).hexdigest()
        return g

    def import_asset(p, group, parent=''):
        g = reverse[p]
        if g in entries:
            entries[g]['groups'] = sorted(set(entries[g]['groups'] + [group]))
            if parent and parent not in entries[g]['referencedBy']:
                entries[g]['referencedBy'].append(parent)
        if g in visited:
            return visited[g]
        if p.suffix in ('.cs', '.dll', '.controller') or p.parent.name in ('MonoBehaviour', 'AudioClip'):
            raise ValueError('Unapproved dependency: ' + str(p))
        dest_guid = guid_for(g)
        dest = current.get(dest_guid, ROOT / 'Imported' / p.relative_to(SOURCE))
        if p.suffix == '.shader':
            dest = shader
        visited[g] = dest
        entries[g] = dict(source=str(p), sourceGuid=g, sourceSha256=sha(p), destination=dest.relative_to(PROJECT).as_posix(),
                          destinationGuid=dest_guid, groups=[group], referencedBy=[parent] if parent else [],
                          treatment='shader-adaptation' if p.suffix == '.shader' else 'reuse' if dest_guid == g and dest_guid in current else 'independent-adaptation' if dest_guid != g else 'import',
                          sourceSize=p.stat().st_size)
        if g in previous:
            entries[g]['treatment'] = previous[g]['treatment']
        existed = dest.exists()
        if p.suffix == '.shader':
            return dest
        if p.suffix in ('.prefab', '.anim', '.asset', '.mat'):
            text = p.read_text(encoding='utf-8-sig')
            if p.suffix == '.prefab':
                # Native visual data only: every gameplay/audio/UI behaviour and
                # physics component is excluded from the templates.
                kept, removed = [], []
                allowed = {1, 4, 212, 198, 199, 95, 210, 224}
                for block in re.split(r'(?=^--- !u!)', text, flags=re.M):
                    m = re.match(r'--- !u!(\d+) &(-?\d+)', block)
                    if m and int(m[1]) not in allowed:
                        removed.append(m[2])
                    else:
                        kept.append(block)
                text = ''.join(kept)
                for id_ in removed:
                    text = re.sub(r'^  - component: \{fileID: ' + id_ + r'\}\n', '', text, flags=re.M)
                    text = text.replace('{fileID: ' + id_ + '}', '{fileID: 0}')
                text = re.sub(r'm_Controller: \{[^}]+\}', 'm_Controller: {fileID: 0}', text)
                # Do not follow constraints/audio/UI references from discarded components.
            def remap(m):
                dep = m[1]
                if dep.startswith('0000000000000000'):
                    return m[0]
                if dep not in source:
                    if dep in current:
                        return m[0]
                    raise ValueError('Unresolved GUID ' + dep + ' in ' + str(p))
                import_asset(source[dep], group, p.relative_to(SOURCE).as_posix())
                return 'guid: ' + guid_for(dep)
            text = GUID.sub(remap, text)
            if not existed:
                dest.parent.mkdir(parents=True, exist_ok=True)
                dest.write_text(text, encoding='utf-8')
        else:
            if not existed:
                dest.parent.mkdir(parents=True, exist_ok=True)
                dest.write_bytes(p.read_bytes())
        if not existed:
            meta = Path(str(p) + '.meta').read_text(encoding='utf-8-sig')
            Path(str(dest) + '.meta').write_text(meta.replace('guid: ' + g, 'guid: ' + dest_guid), encoding='utf-8')
        return dest

    data = json.loads(EVIDENCE.read_text(encoding='utf-8-sig'))
    objects = {o['id']: o for o in data['objects']}
    warning_names = {'Slime Path Show', 'Slime Path fadeout', 'MeleeWarning_BuildUp', 'MeleeWarning_BuildDown', 'Buildup Lost Soul', 'Fadeout2'}
    bodies, effects, palettes = [], [], []
    for group, name, identity, target, contact in BODIES:
        animator = next(o for o in objects.values() if o['type'].split('.')[-1] in ('EnemyAnimator', 'MultipleAttackAnimator') and o['path'] == name + '[0]/Sprite[0]')
        bindings = []
        values = list(animator['data'].items())
        for i, attack_set in enumerate(field(animator['data'], 'attackSets') or []):
            values.extend(('attackSets.Array.data['+str(i)+'].'+key.split('::')[-1], value) for key, value in attack_set['fields'].items())
        for key, value in values:
            prop = key.split('::')[-1]
            if contact and prop.startswith(('attack', 'recovery')):
                continue
            if not isinstance(value, dict) or value.get('managedType') != 'Animancer.ClipTransition':
                continue
            clip = field(value, '_Clip')
            if not clip:
                continue
            path = import_asset(SOURCE / 'AnimationClip' / (clip['name'] + '.anim'), group, animator['path'] + '/' + prop)
            events = field(value, '_Events')
            callbacks = []
            for index, callback in enumerate(field(events, '_Callbacks') or []):
                if not callback:
                    continue
                for call in field(field(callback, 'm_PersistentCalls'), 'm_Calls') or []:
                    method = field(call, 'm_MethodName')
                    target_type = field(call, 'm_TargetAssemblyTypeName') or ''
                    if target_type.startswith('FMODUnity.'):
                        continue
                    if method != 'DeathAnimationShadowFade' or not target_type.startswith('AstralShift.HellMaiden.AI.Enemy.EnemyAnimator,'):
                        raise ValueError('Unclassified visual callback: '+str(method)+' / '+target_type)
                    callbacks.append(dict(index=index, method=method))
            bindings.append(dict(field=prop, path=path.relative_to(PROJECT).as_posix(), length=objects[clip['reference']]['data']['length'],
                                 speed=field(value, '_Speed'), fade=field(value, '_FadeDuration'),
                                 normalizedStart=str(field(value, '_NormalizedStartTime')),
                                 eventTimes=[str(x) for x in (field(events, '_NormalizedTimes') or [])], visualCallbacks=callbacks))
        p = import_asset(SOURCE / 'GameObject' / (name + '.prefab'), group)
        bodies.append(dict(group=group, name=name, identity=identity, target='Assets/_Project/Content/NetworkCombat/Limbo/' + target,
                           template=p.relative_to(PROJECT).as_posix(), bindings=bindings, contact=contact))
    for group, name in EFFECTS:
        p = import_asset(SOURCE / 'GameObject' / (name + '.prefab'), group)
        effects.append(dict(group=group, name=name, template=p.relative_to(PROJECT).as_posix()))
    for name in sorted(warning_names):
        import_asset(SOURCE / 'AnimationClip' / (name + '.anim'), 'warnings')
    used = {'Brotchi': [0, 1], 'Brotchi_Dash': [0, 1], 'Imp': [0, 1], 'Skeleton': [0, 2], 'Elite_Skeleton': [0, 1], 'Slime': [0, 1, 2], 'LostSoul': [0, 1], 'Ghoul': [0]}
    for aggregate in field(objects[data['enemyDatabase']['reference']]['data'], 'enemies'):
        name = field(aggregate, 'enemyName')
        for variant in used.get(name, []):
            v = field(aggregate, 'enemyData')[variant]
            lut = field(v, 'colorLUT'); path = ''
            if lut:
                path = import_asset(SOURCE / 'Texture2D' / (lut['name'] + '.png'), 'palettes', name + '/' + str(variant)).relative_to(PROJECT).as_posix()
            hue = [field(field(v, 'hueColor'), channel) for channel in ('r','g','b','a')]
            if any(channel is None for channel in hue):
                raise ValueError('Missing source hue: '+name+'/'+str(variant))
            palettes.append(dict(identity=name, variant=variant, texture=path, hue=hue))
    bakes = {}
    def source_links(p):
        return {source[g] for g in GUID.findall(p.read_text(encoding='utf-8-sig')) if g in source}
    for body in bodies:
        textures = set()
        for binding in body['bindings']:
            if binding['field'].startswith('shadow'):
                continue
            clip = SOURCE / 'AnimationClip' / Path(binding['path']).name
            for sprite in source_links(clip):
                if sprite.parent.name == 'Sprite':
                    textures.update(t for t in source_links(sprite) if t.parent.name == 'Texture2D')
        for palette in palettes:
            if palette['identity'] != body['identity'] or not palette['texture']:
                continue
            for texture in sorted(textures):
                lut = Path(palette['texture'])
                key = hashlib.sha256((texture.name + '|' + lut.name).encode()).hexdigest()[:20]
                bakes[key] = dict(file=key+'.png', textureName=texture.stem, lutName=lut.stem,
                                  sourceTexture=str(texture), lut=palette['texture'])
    ROOT.mkdir(parents=True, exist_ok=True)
    manifest = dict(schemaVersion=1, sourceEvidence=str(EVIDENCE), sourceSha256=sha(EVIDENCE), bodies=bodies, effects=effects,
                    palettes=palettes, bakes=list(bakes.values()), entries=sorted(entries.values(), key=lambda x: x['source']),
                    deferred=[], excluded=['audio', 'UI', 'loot', 'player', 'weapons', 'maps', 'gameplay scripts'])
    (ROOT / 'ArtSource.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps(dict(bodies=len(bodies), effectRoots=len(effects), dependencies=len(entries), sourceSha256=manifest['sourceSha256'])))


if __name__ == '__main__':
    main()
