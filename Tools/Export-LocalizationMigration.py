"""One-time extraction before deleting legacy localization fields.

Requires PyYAML. Writes a migration manifest outside Assets; runtime/builds never
read this file or the source project. Run before GameLocalizationAssets.Import.
"""
import argparse
import json
import re
from pathlib import Path
import yaml


def unity_yaml(path):
    text = path.read_text(encoding="utf-8-sig")
    text = re.sub(r"^%.*\n", "", text, flags=re.M)
    text = re.sub(r"^--- !u!.*$", "---", text, flags=re.M)
    return list(yaml.safe_load_all(text))


def apply_reviewed_effects(entries):
    e=entries
    def put(table,key,zh,en,smart=False): e[(table,key)]={'table':table,'key':key,'zh':zh,'en':en,'smart':smart}
    for key,zh,en in [
    ('ui.content.unknown','未知内容 #{0}','Unknown content #{0}'),('ui.content.unavailable','内容尚未就绪','Content not ready'),
    ('ui.card.heading','等级 {0} · {1}','Level {0} · {1}'),('ui.card.choose_weapon','{0} — 选择武器','{0} — Choose a weapon'),
    ('ui.card.add_equipment','添加装备','Add equipment'),('ui.card.upgrade_level','升级至等级 {0}','Upgrade to level {0}'),
    ('ui.card.choose_target_next','下一步选择装备目标','Choose an equipment target next'),
    ('ui.reward.weapon','武器','Weapon'),('ui.reward.equipment','装备','Equipment'),('ui.reward.perk','天赋','Perk'),
    ('ui.rarity.bronze','青铜','Bronze'),('ui.rarity.silver','白银','Silver'),('ui.rarity.gold','黄金','Gold'),('ui.rarity.crystal','水晶','Crystal'),
    ('ui.hud.experience','等级 {0}    经验 {1:0.##} / {2}','Lv. {0}    XP {1:0.##} / {2}'),
    ('ui.wave.syncing','正在同步波次','Synchronizing waves'),('ui.wave.waiting','等待房主开始本局','Waiting for the host to start'),
    ('ui.wave.stopped','本局已结束','Run ended'),('ui.wave.paused','暂停 · 暂无在线角色','Paused · No active players'),('ui.wave.next','下一波','Next wave'),
    ('ui.wave.progress','第 {0} 波  |  {1}：{2:F1} 秒\n存活 {3}/{4}  |  已生成 {5}/{6}  |  跳过 {7}','Wave {0}  |  {1}: {2:F1}s\nAlive {3}/{4}  |  Spawned {5}/{6}  |  Skipped {7}'),
    ('ui.connection.transport_error','连接失败，请检查网络或稍后重试。','Connection failed. Check your connection and try again.')]: put('MonsterMenus',key,zh,en)
    for kind, rows in [('equipment',[(1,'sizePercent','攻击范围','attack size'),(2,'damagePercent','伤害','damage'),(3,'attackSpeedPercent','攻速','attack speed'),(4,'durationPercent','持续时间','duration'),(16,'critChancePercent','暴击率','critical chance'),(17,'critDamagePercent','暴击倍率','critical damage multiplier')]),('perk',[(2,'damagePercent','所有武器的伤害','all weapon damage'),(3,'attackSpeedPercent','所有武器的攻速','all weapon attack speed'),(17,'critDamagePercent','所有武器的暴击倍率','all weapon critical damage multipliers'),(18,'critChancePercent','所有武器的暴击率','all weapon critical chances'),(19,'durationPercent','所有武器的持续时间','all weapon durations'),(22,'sizePercent','所有武器的攻击范围','all weapon attack sizes')])]:
     for ident,arg,zh,en in rows:
      amount='{'+arg+':0.##}'
      if arg=='critChancePercent': zhtext=f'{zh}提高 {amount} 个百分点。'; entext=f'Increases {en} by {amount} percentage points.'
      elif arg=='critDamagePercent': zhtext=f'{zh}增加 {amount} 个百分点。'; entext=f'Adds {amount} percentage points to {en}.'
      else: zhtext=f'{zh}提高 {amount}%。'; entext=f'Increases {en} by {amount}%.'
      put('MonsterContent',f'{kind}.{ident}.description',zhtext,entext,True)
    put('MonsterContent','equipment.304.description','击退效果提高 {knockbackPercent:0.##}%，攻速提高 {attackSpeedPercent:0.##}%。','Increases knockback by {knockbackPercent:0.##}% and attack speed by {attackSpeedPercent:0.##}%.',True)
    put('MonsterContent','equipment.6.description','射弹数量增加 {projectileCount:0}。','Adds {projectileCount:0} projectiles.',True)
    put('MonsterContent','perk.28.description','所有武器的射弹数量增加 {projectileCount:0}。','Adds {projectileCount:0} projectiles to every weapon.',True)
    put('MonsterContent','weapon.1.quote','“这爱如火燃烧，从微光燃成烈焰。”','“This love blazes like fire, from a small to a great flame.”')
    put('MonsterContent','ultimate.0.description','释放烈焰攻击周围敌人。','Unleashes flames against surrounding enemies.')
    put('MonsterContent','character.1.name','但丁','Dante')
    put('MonsterContent','map.1.name','遗迹边境','Ruins Frontier')
    put('MonsterContent','map.1.description','踏入战场，与同伴一起迎战不断涌来的怪物。','Enter the battlefield and face the growing monster hordes with your companions.')

def export(root, source, output):
    original = unity_yaml(source / "Assets/Resources/I2Languages.asset")[0]["MonoBehaviour"]["mSource"]
    codes = [item.get("Code") for item in original["mLanguages"]]
    terms = {item["Term"].split("/", 1)[-1]: item for item in original["mTerms"] if item["TermType"] == 0}
    def translations(key, fallback=""):
        values = terms.get(key, {}).get("Languages", [])
        return {code: (values[codes.index(code)] if len(values) > codes.index(code) else None) or fallback or ""
                for code in ("zh-CN", "en")}

    entries = {}
    def add(table, key, values, smart=False):
        entries[(table, key)] = {"table": table, "key": key, "zh": values["zh-CN"], "en": values["en"], "smart": smart}
    # Import only text groups used by retained UI; do not import source assets, dialogs or videos.
    for key, term in terms.items():
        if term["Term"].split("/")[0] in ("UI", "Achievements"):
            values = translations(key)
            if values["zh-CN"] or values["en"]:
                values["zh-CN"] = values["zh-CN"] or values["en"]
                values["en"] = values["en"] or values["zh-CN"]
                add("MonsterMenus", key, values)
    menus = json.loads((root / "Assets/_Project/Localization/MenuStrings.json").read_text(encoding="utf-8-sig"))["entries"]
    for item in menus:
        add("MonsterMenus", item["key"], {"zh-CN": item["zh"], "en": item["en"]})
    menu_map = {item["key"]: {"zh-CN": item["zh"], "en": item["en"]} for item in menus}
    kinds = {"4d49b6f84b3b09feb740257504c5fc1f": "weapon", "d6a1579f831008f222506193163b30cf": "equipment",
             "d86708921b60a16aa63e5895753f56e8": "perk", "c732e61a0dd2970a7803dc3c14cf9415": "ultimate"}
    assets = []
    for path in (root / "Assets").rglob("*.asset"):
        raw = path.read_text(encoding="utf-8-sig", errors="replace")
        match = re.search(r"m_Script: .*guid: (\w+)", raw)
        if not match or match[1] not in kinds:
            continue
        data = unity_yaml(path)[0]["MonoBehaviour"]
        if "localizedTitle" in data:
            raise SystemExit("This asset is already migrated; use Unity tables/CSV to edit translations: " + str(path))
        kind = kinds[match[1]]
        ident = data.get("ID", data.get("id", 0))
        base = f"{kind}.{ident}"
        record = {"path": path.relative_to(root).as_posix(), "kind": kind, "id": ident, "baseKey": base, "overrides": []}
        for field, oldkey, oldvalue in (("name", "TitleKey", "Title"), ("description", "DescriptionKey", "Description"), ("quote", "QuoteKey", "Quote")):
            if kind == "ultimate": oldkey, oldvalue = oldkey[0].lower() + oldkey[1:], oldvalue.lower()
            values = translations(data.get(oldkey, ""), data.get(oldvalue) or "")
            if field == "name" and base in menu_map: values = menu_map[base]
            if field == "quote" and not data.get("HasQuote"): continue
            # Old indexed tokens become named Smart String arguments. Values/units come from typed GAS parameters.
            for code in values:
                values[code] = re.sub(r"\{([^}]+)\}\[(\d+)\](?:\[%\])?", lambda m: "{" + m[1] + "_" + m[2] + ":0.##}", values[code])
                values[code] = re.sub(r'<sprite[^>]*>', '', values[code])
            add("MonsterContent", base + "." + field, values, field == "description" and kind in ("equipment", "perk"))
        for index, level in enumerate(data.get("levelModifiersData") or []):
            if level.get("overrideDescription"):
                key = base + f".description.level.{index + 1}"
                values = translations(level.get("descriptionKey") or "")
                for code in values:
                    values[code] = re.sub(r"\{([^}]+)\}\[(\d+)\](?:\[%\])?", lambda m: "{" + m[1] + "_" + m[2] + ":0.##}", values[code])
                    values[code] = re.sub(r'<sprite[^>]*>', '', values[code])
                add("MonsterContent", key, values, True)
                record["overrides"].append({"index": index, "key": key})
        assets.append(record)
    # Actual native effects replace incomplete or outdated imported descriptions.
    patches = {
        "equipment.304.description": ("击退效果提高 {KnockbackRaise_0:0.##}%。", "Increases knockback by {KnockbackRaise_0:0.##}%."),
        "weapon.402.description": ("召唤伙伴协助攻击敌人。", "Summons a companion to attack enemies."),
    }
    for key, pair in patches.items():
        if ("MonsterContent", key) in entries:
            entries[("MonsterContent", key)].update(zh=pair[0], en=pair[1])
    apply_reviewed_effects(entries)
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps({"entries": list(entries.values()), "assets": assets}, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps({"assets": len(assets), "entries": len(entries), "content": [e for e in entries.values() if e["table"] == "MonsterContent"]}, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--project", type=Path, default=Path.cwd())
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    export(args.project, args.source, args.output)
