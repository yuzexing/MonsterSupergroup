"""Fingerprint originals and existing saves; this script only reads those locations."""
import hashlib, json, os, pathlib, sys
ROOT = pathlib.Path(__file__).resolve().parents[2]
BASE = ROOT / 'Logs/EnemyRecovery/input-fingerprints.json'
SOURCES = {
    'original': pathlib.Path(r'F:\BaiduNetdiskDownload\d-地狱公主\Hell Maiden-v0.2.31'),
    'export': pathlib.Path(r'F:\DecomplieLatest\HellMaiden\ExportedProject'),
    'saved-games': pathlib.Path(os.environ['USERPROFILE']) / 'Saved Games/HellMaiden',
    'unity-profile': pathlib.Path(os.environ['USERPROFILE']) / 'AppData/LocalLow/AstralShift/Hell Maiden',
}
def snapshot():
    result = {}
    for kind, folder in SOURCES.items():
        entries = {}
        for p in sorted(folder.rglob('*')):
            if not p.is_file(): continue
            s = p.stat()
            item = dict(size=s.st_size, mtime_ns=s.st_mtime_ns)
            # Entire original and saves are hashed. Export is inventoried in full,
            # with hashes for the serialized assets and code used by this task.
            if kind != 'export' or p.suffix.lower() in ('.cs', '.prefab', '.playable', '.asset', '.anim', '.unity', '.meta'):
                with p.open('rb') as stream:
                    item['sha256'] = hashlib.file_digest(stream, 'sha256').hexdigest()
            entries[str(p.relative_to(folder))] = item
        result[kind] = dict(path=str(folder), exists=folder.exists(), files=entries)
    return result
current = snapshot()
if sys.argv[1] == 'baseline':
    if BASE.exists(): raise SystemExit('Baseline already exists; refusing to overwrite.')
    BASE.write_text(json.dumps(current, indent=2), encoding='utf-8')
    print({k: len(v['files']) for k,v in current.items()})
elif sys.argv[1] == 'verify':
    expected = json.loads(BASE.read_text(encoding='utf-8'))
    differences = [k for k in current if current[k] != expected[k]]
    (BASE.parent / 'input-verification.json').write_text(json.dumps(dict(unchanged=not differences, differences=differences),indent=2))
    print('Input differences:', differences)
    sys.exit(bool(differences))
else: raise SystemExit('Use baseline or verify')
