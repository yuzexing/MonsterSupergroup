"""Extract scalar data and curves from the live Limbo references; never copy source assets/code."""
import argparse
import hashlib
import json
import re
from pathlib import Path


def scalar(text, key, default=None):
    match = re.search(r"^\s*(?:- )?" + re.escape(key) + r":\s*([^\r\n]+)$", text, re.M)
    if match:
        return match.group(1).strip()
    if default is None:
        raise ValueError(f"Missing {key}")
    return default


def curve(text, name):
    tail = text[text.index(name + ":"):]
    tail = tail[:tail.index("m_PostInfinity:")]
    return [dict(time=float(t), value=float(v), inSlope=float(a), outSlope=float(b))
            for t, v, a, b in re.findall(r"time: ([^\n]+)\s+value: ([^\n]+)\s+inSlope: ([^\n]+)\s+outSlope: ([^\n]+)", tail)]


def extract(root):
    asset = root / "MonoBehaviour/Progression_Limbo.playable"
    text = asset.read_text(encoding="utf-8-sig")
    blocks = {re.search(r"^--- !u!\d+ &(\d+)", b).group(1): b for b in
              re.split(r"(?=^--- !u!)", text, flags=re.M) if b.startswith("---")}
    prefabs = {}
    for meta in (root / "GameObject").glob("*.prefab.meta"):
        guid = scalar(meta.read_text(encoding="utf-8-sig"), "guid")
        prefabs[guid] = meta.with_suffix("")
    clips = []
    for track in blocks.values():
        if "m_Clips:" not in track or scalar(track, "m_Muted", "0") != "0":
            continue
        for m in re.finditer(r"m_Start: ([^\n]+)\s+m_ClipIn: [^\n]+\s+m_Asset: \{fileID: (\d+)\}\s+m_Duration: ([^\n]+)", track):
            start, identifier, duration = float(m[1]), m[2], float(m[3])
            b = blocks[identifier]
            if "enemyPrefab:" not in b:
                continue
            guid = re.search(r"enemyPrefab: \{fileID: \d+, guid: (\w+)", b).group(1)
            prefab = prefabs[guid]
            prefab_text = prefab.read_text(encoding="utf-8-sig")
            enemy = scalar(prefab_text, "selectedName")
            mode = 1 if "spawnCurve:" in b else 3 if "spawnShapeOptions:" in b else 2
            index = int(scalar(b, "variantIndex"))
            missing = "" if enemy in ("Brotchi", "Slime") else "Original attack animation bindings/timings are missing."
            if enemy == "Imp": missing += " Original bullet movement parameters are missing."
            if enemy == "LostSoul": missing += " Original explosion component references/window are missing."
            if enemy == "Ghoul": missing += " Original MultipleAttackAnimator list is missing."
            clips.append(dict(start=start, duration=duration, mode=mode, enemy=enemy, variant=index,
                count=int(scalar(b, "maxEnemies" if mode == 1 else "enemyAmount")),
                cooldown=float(scalar(b, "spawnCoolDown", "0")), keys=curve(b, "spawnCurve") if mode == 1 else [],
                sourcePrefab=prefab.name, sourceFile=asset.relative_to(root).as_posix(), sourceId=identifier,
                sourceLine=text[:text.index(b)].count("\n") + 1, missingEvidence=missing,
                contactRadius=.55 if enemy == "Brotchi" else .72 if enemy == "Slime" else 0,
                expiresOffscreen=scalar(b, "rubberBandKillsOnClipEnd", "1") == "1",
                resetOnReposition=scalar(prefab_text, "rubberbandStatsReset", "1") == "1"))
    clips.sort(key=lambda c: c["start"])
    assert len(clips) == 31, f"Expected 31 active enemy clips, found {len(clips)}"
    db_path = root / "MonoBehaviour/EnemyDB.asset"
    db_text = db_path.read_text(encoding="utf-8-sig")
    wanted = {}
    for c in clips: wanted[c["enemy"]] = max(wanted.get(c["enemy"], 0), c["variant"])
    enemies = []
    for group in re.split(r"(?=^  - enemyName:)", db_text, flags=re.M)[1:]:
        name = scalar(group, "enemyName")
        if name not in wanted: continue
        variants = []
        for i, b in enumerate(re.split(r"(?=^    - variantName:)", group, flags=re.M)[1:]):
            if i > wanted[name]: break
            base = b.split("baseStats:", 1)[1].split("currentStats:", 1)[0]
            variants.append(dict(name=scalar(b, "variantName"), hp=int(scalar(base, "hp")), damage=int(scalar(base, "damage")),
                xp=float(scalar(base, "xp")), speed=float(scalar(base, "speed")), knockback=float(scalar(base, "knockbackMultiplier")),
                stun=float(scalar(base, "stunTime")), wind=float(scalar(base, "windMultiplier")),
                sourceLine=db_text[:db_text.index(b)].count("\n")+1))
        enemies.append(dict(name=name, variants=variants))
    scene_path = root / "Scenes/Game Scenes/Circle 1 - Limbo/Level_Limbo.unity"
    scene = scene_path.read_text(encoding="utf-8-sig")
    return dict(sourceRoot=str(root), sourceDuration=float(scalar(blocks["11400000"], "m_FixedDuration")),
        stageEnd=720.9, maximumAlive=int(scalar(scene, "maxEnemies")), xpAmplitude=float(scalar(scene, "maxXPMultiplier")),
        xpKeys=curve(scene, "xpMultiplierCurve"), clips=clips, enemies=enemies,
        hashes=[dict(path=p.relative_to(root).as_posix(), sha256=hashlib.sha256(p.read_bytes()).hexdigest())
                for p in (asset, db_path, scene_path)])


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", type=Path, default=Path("F:/DecomplieLatest/HellMaiden/ExportedProject/Assets"))
    parser.add_argument("--output", type=Path, default=Path(__file__).resolve().parents[1] / "Assets/_Project/Content/NetworkCombat/Limbo/limbo-source.json")
    args = parser.parse_args()
    data = extract(args.source)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(data, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print(f"Extracted {len(data['clips'])} clips, {len(data['enemies'])} species -> {args.output}")
