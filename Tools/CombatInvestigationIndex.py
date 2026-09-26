"""Derived investigation index. Original evidence databases are always opened read-only.

Only prepare's fresh destination, or a caller-owned fresh analysis database passed
to build_index, is written. No gameplay state is inferred from missing evidence.
"""
import base64
import hashlib
import json
import math
import os
import sqlite3
import uuid
from collections import defaultdict
from contextlib import closing
from pathlib import Path

import CombatEvidence as evidence
from CombatNetworkEvidence import rejected, valid_metric

VERSION = 3
CLOCK = {"basis": "capture-round-first-record", "crossCaptureExact": False,
         "description": "Seconds since each capture's first record in this run/round; not a shared causal clock."}
BUSINESS_CATEGORIES = ("attack", "damage", "pickup", "selection")
BUSINESS_STAGES = ("collector.enqueue", "collector.drain", "network.submit", "gateway.decision",
                   "gateway.canonical_link", "network.canonical", "replica.entity", "entity.canonical_health")
SCHEMA = """
CREATE TABLE investigation_meta(key TEXT PRIMARY KEY,value TEXT NOT NULL);
CREATE TABLE investigation_context(capture TEXT,run TEXT,round INTEGER,body TEXT,
 PRIMARY KEY(capture,run,round));
CREATE TABLE investigation_records(capture TEXT,seq TEXT,run TEXT,round INTEGER,
 elapsed REAL,sort_time REAL,stage TEXT,category TEXT,source TEXT,target TEXT,
 generation TEXT,role TEXT,summary TEXT,PRIMARY KEY(capture,seq));
CREATE INDEX investigation_order ON investigation_records(run,round,sort_time,capture,length(seq),seq);
CREATE INDEX investigation_category ON investigation_records(run,round,category,sort_time,capture,length(seq),seq);
CREATE INDEX investigation_source_order ON investigation_records(capture,run,round,sort_time,length(seq),seq);
CREATE TABLE investigation_entities(capture TEXT,run TEXT,round INTEGER,entity TEXT,generation TEXT,
 name TEXT,kind TEXT,first REAL,last REAL,body TEXT,PRIMARY KEY(capture,run,round,entity,generation));
CREATE TABLE investigation_members(capture TEXT,seq TEXT,entity TEXT,generation TEXT,direction TEXT,
 PRIMARY KEY(capture,seq,entity,generation,direction));
CREATE INDEX investigation_member_lookup ON investigation_members(entity,generation,direction,capture,seq);
CREATE TABLE investigation_states(capture TEXT,seq TEXT,run TEXT,round INTEGER,entity TEXT,generation TEXT,
 domain TEXT,perspective TEXT,elapsed REAL,body TEXT,
 PRIMARY KEY(capture,seq,entity,generation,domain,perspective));
CREATE INDEX investigation_state_time ON investigation_states(capture,run,round,entity,generation,domain,perspective,elapsed);
CREATE TABLE investigation_operations(capture TEXT,seq TEXT,run TEXT,round INTEGER,entity TEXT,generation TEXT,
 perspective TEXT,operation TEXT,elapsed REAL,PRIMARY KEY(capture,seq));
CREATE INDEX investigation_operation_time ON investigation_operations(capture,run,round,entity,generation,elapsed);
CREATE TABLE investigation_links(capture TEXT,seq TEXT,kind TEXT,value TEXT,PRIMARY KEY(capture,seq,kind,value));
CREATE INDEX investigation_link_lookup ON investigation_links(kind,value,capture,seq);
CREATE TABLE investigation_samples(capture TEXT,seq TEXT,run TEXT,round INTEGER,track TEXT,elapsed REAL,
 validity TEXT,body TEXT,PRIMARY KEY(capture,seq,track));
CREATE INDEX investigation_sample_time ON investigation_samples(run,round,track,elapsed,capture);
CREATE TABLE investigation_network(capture TEXT,seq TEXT,instance TEXT,direction TEXT,channel TEXT,result TEXT,attempt TEXT,message_number TEXT,lane TEXT,PRIMARY KEY(capture,seq));
CREATE INDEX investigation_network_filter ON investigation_network(instance,direction,channel,result,capture,seq);
CREATE INDEX investigation_network_attempt ON investigation_network(capture,instance,direction,attempt);
CREATE INDEX investigation_network_message ON investigation_network(message_number,lane,direction,capture,seq);
CREATE TABLE investigation_catalog(capture TEXT,seq TEXT,run TEXT,round INTEGER,body TEXT,
 PRIMARY KEY(capture,seq));
CREATE INDEX investigation_original_sequence ON records(capture,length(seq),seq);
CREATE INDEX investigation_original_event ON records(run,round,event);
CREATE INDEX investigation_original_root ON records(run,round,root);
CREATE INDEX investigation_original_parent ON records(run,round,parent);
CREATE INDEX investigation_original_server ON records(run,round,server);
"""


def _json(value):
    return json.dumps(value, ensure_ascii=False, separators=(",", ":"), allow_nan=False)


def _finite(value):
    return isinstance(value, (int, float)) and not isinstance(value, bool) and math.isfinite(value)


def _id(value):
    if value is None or isinstance(value, bool):
        return None
    text = str(value)
    return text if text and text != "0" else None


def _read_only(path):
    path = Path(path).resolve(strict=True)
    wal = Path(str(path) + "-wal")
    if wal.exists() and wal.stat().st_size:
        raise ValueError("Use a completed, checkpointed import; an active WAL is not an immutable evidence snapshot")
    # Immutable avoids creating/updating WAL or SHM sidecars beside original evidence.
    db = sqlite3.connect(path.as_uri() + "?mode=ro&immutable=1", uri=True)
    db.row_factory = sqlite3.Row
    db.execute("PRAGMA query_only=ON")
    return db


def prepare(source_db, destination_db):
    """Copy a consistent SQLite snapshot to a *new* destination, then derive indexes."""
    source, destination = Path(source_db).resolve(strict=True), Path(destination_db).absolute()
    if source == destination:
        raise ValueError("Source and analysis database must differ")
    # Exclusive creation also rejects existing symlinks and existing evidence DBs.
    descriptor = os.open(destination, os.O_CREAT | os.O_EXCL | os.O_WRONLY, 0o600)
    os.close(descriptor)
    with closing(_read_only(source)) as original, closing(sqlite3.connect(destination)) as copy:
        with copy:
            original.backup(copy)
    return build_index(destination)


def _category(stage):
    if stage.startswith("investigation.pickup.") or stage.startswith("pickup."):
        return "pickup"
    if stage.startswith("investigation.selection."):
        return "selection"
    if stage == "investigation.state" or stage.startswith("entity.") or stage == "observation.snapshot":
        return "state"
    for category, stages in evidence.VIEW_CATEGORIES.items():
        if stage in stages:
            return category
    if stage.startswith(("performance.", "network.metrics")):
        return "performance"
    if stage.startswith(("replay.", "source.", "process.")):
        return "replay"
    if stage.startswith(("network.", "gateway.", "collector.", "authority.")):
        return "sync"
    return "business"


def _record_category(record, data):
    if rejected(record):
        return "network_rejection"
    if record.get("reason") == "PickupReceiptFact" or any(_id(v.get("PickupDropId")) for v in _walk(data)):
        return "pickup"
    return _category(record.get("stage", ""))


def _payload(db, record):
    try:
        value = evidence.payload(db, record)
        return json.loads(value) if isinstance(value, str) and record.get("stage", "").startswith("performance.") else value
    except (ValueError, TypeError):
        return None


def _walk(value):
    if isinstance(value, dict):
        yield value
        for child in value.values():
            yield from _walk(child)
    elif isinstance(value, list):
        for child in value:
            yield from _walk(child)


def _catalog(data, build_id):
    raw = data.get("investigationCatalog")
    if not isinstance(raw, str):
        return {"status": "unknown", "reason": "ContentCatalogNotCaptured", "entries": []}
    try:
        value = json.loads(raw)
        manifest = data.get("buildManifest") or {}
        manifest = json.loads(manifest) if isinstance(manifest, str) else manifest
        expected = manifest.get("investigationCatalog", {}).get("sha256")
        if not build_id or value.get("buildId") != build_id or expected != hashlib.sha256(raw.encode("utf-8")).hexdigest():
            return {"status": "unknown", "reason": "ContentCatalogBuildOrHashMismatch", "entries": []}
        return {"status": "observed", **value}
    except (ValueError, TypeError, AttributeError):
        return {"status": "unknown", "reason": "InvalidContentCatalog", "entries": []}


def _name(catalog, kind, value):
    candidates = [e for e in catalog.get("entries", []) if e.get("kind") == kind and str(e.get("contentId")) == str(value) and e.get("identityValid") is True]
    if len(candidates) != 1:
        return {"status": "unknown", "reason": "AmbiguousContentId" if candidates else "ContentIdNotCatalogued", "id": str(value), "kind": kind}
    entry = candidates[0]; names = entry.get("names") or {}
    title = names.get("zh-CN") or names.get("zh-Hans") or names.get("en") or entry.get("assetName")
    return {"status": "observed", "id": str(value), "kind": kind, "name": title, "names": names, "assetGuid": entry.get("assetGuid")}


def _field_rows(value, pointer, status, catalog, prefix=""):
    result = []
    if isinstance(value, dict):
        for key, member in value.items():
            path = prefix + "." + key if prefix else key
            if isinstance(member, (dict, list)) and member:
                result.extend(_field_rows(member, pointer, status, catalog, path))
            else:
                row = {"name": path, "value": member, "status": status if member is not None else "unknown", "evidence": pointer}
                kind = {"weaponid": "weapon", "equipmentid": "equipment", "perkid": "blessing", "blessingid": "blessing"}.get(key.lower())
                if kind:
                    row["contentName"] = _name(catalog, kind, member)
                result.append(row)
    elif isinstance(value, list):
        for index, member in enumerate(value):
            path = f"{prefix}[{index}]"
            result.extend(_field_rows(member, pointer, status, catalog, path) if isinstance(member, (dict, list)) else
                          [{"name": path, "value": member, "status": status if member is not None else "unknown", "evidence": pointer}])
    return result


def _steam_connection(value):
    normalized = dict(value)
    normalized["raw"] = value
    for flag, fields in (("queueValid", ("queueMilliseconds",)),
                         ("pendingValid", ("pendingReliableBytes", "pendingUnreliableBytes", "unacknowledgedBytes")),
                         ("localQualityValid", ("localQuality",)), ("remoteQualityValid", ("remoteQuality",))):
        for field in fields:
            if field in normalized:
                normalized[field] = valid_metric(value, field) if flag in ("queueValid", "pendingValid") else value[field] if value.get(flag) is True else None
    return normalized


def _measurements(values):
    result, validity = {}, {}
    for key, value in values.items():
        if isinstance(value, (int, float)) and not isinstance(value, bool):
            valid = _finite(value) and value >= 0
            result[key] = value if valid else None
            validity[key] = "observed" if valid else "unknown"
        else:
            result[key] = value
    return {**result, "fieldValidity": validity, "raw": values}


def _summary(record, data):
    stage = record.get("stage", "")
    if rejected(record):
        return "Steam 发送拒绝 · 连接 " + str(data.get("connectionInstance", data.get("connectionId", "未知"))) + " · 频道 " + str(data.get("channel", "未知")) + " · " + str(data.get("result", record.get("reason", "未知")))
    labels = {"owner.attack_started": "发起攻击", "owner.attack_window": "攻击窗口",
              "owner.attack_stats": "攻击属性", "owner.hit": "命中判断", "ledger.apply": "权威结算",
              "replica.entity": "应用权威状态", "entity.spawn": "观察到实体出生",
              "entity.destroy": "观察到实体离开", "investigation.state": "状态观测",
              "death.report": "提交死亡报告", "death.receipt": "收到死亡确认"}
    title = labels.get(stage, stage.replace("investigation.pickup.", "拾取 · ").replace("investigation.selection.", "选卡 · "))
    parts = [title, str(record.get("outcome") or ""), str(record.get("reason") or "")]
    if isinstance(data, dict):
        for key in ("domain", "dropId", "optionId", "weaponId"):
            if data.get(key) is not None:
                parts.append(f"{key}={data[key]}")
    return " · ".join(part for part in parts if part)


def _missing_domains(domains):
    present = {value["domain"] for value in domains}
    expected = {"build": ("weapons", "equipment", "blessings"),
                "progression": ("level", "experience", "pendingUpgrades"),
                "selection": ("locked", "options", "pendingEventId"),
                "attackGate": ("attackPermission",), "attackAttributes": ("attackWeapon", "attackStats")}
    missing = [{"domain": domain, "status": "unknown", "reason": "DomainNotCapturedAtOrBeforeRequestedTime",
             "fields": [{"name": name, "value": None, "status": "unknown", "evidence": None} for name in fields]}
            for domain, fields in expected.items() if domain not in present]
    observed = {key.lower() for domain in domains if domain["domain"] == "observation" for key in domain.get("state", {})}
    aliases = {"health": ("health", "localhealth", "canonicalhealth"), "alive": ("alive", "localalive"),
               "invulnerable": ("invulnerable", "absoluteinvulnerable"), "statusEffects": ("statuses", "statuseffects"), "position": ("position",)}
    absent = [name for name, names in aliases.items() if not observed.intersection(names)]
    if absent:
        missing.append({"domain": "observation", "status": "unknown", "reason": "DomainNotCapturedAtOrBeforeRequestedTime",
            "fields": [{"name": name, "value": None, "status": "unknown", "evidence": None} for name in absent]})
    return missing


def _display_summary(record, category=None):
    stage = record.get("stage", "")
    stages = {"owner.attack_started": "发起攻击", "owner.AttackStarted": "攻击开始标记", "owner.attack_stats": "计算攻击属性",
        "owner.attack_gate": "检查攻击许可", "owner.attack_window": "推进攻击窗口", "owner.contact": "检测攻击接触",
        "owner.damage_calculation": "计算请求伤害", "owner.hit": "处理本地命中", "owner.HitResolved": "记录命中结果",
        "owner.DamageResolved": "记录本地伤害结果", "owner.PredictedLethalHit": "预测致命命中",
        "collector.enqueue": "战斗结果进入待发送队列", "collector.drain": "汇集战斗批次", "network.submit": "提交或接收战斗批次",
        "gateway.decision": "服务端作出权威判断", "ledger.apply": "执行权威结算", "gateway.canonical_link": "生成权威状态输出",
        "network.canonical": "传输权威状态", "replica.entity": "本端应用权威状态", "entity.canonical_health": "更新本地权威生命",
        "death.report": "提交死亡报告", "death.receipt": "收到死亡确认", "entity.spawn": "观察实体出生", "entity.destroy": "观察实体离开",
        "observation.snapshot": "采样当前实体状态", "investigation.state": "记录完整领域状态", "investigation.build.begin": "开始构筑变更",
        "replica.attack_window": "观察远端攻击窗口", "replica.contact": "观察远端接触", "performance.snapshot": "采样性能与队列",
        "source.start": "开始本端本轮记录", "process.start": "记录进程与包身份"}
    pickup = {"drop_decision": "判断是否生成掉落", "spawn": "生成拾取物", "request": "请求拾取", "decision": "判断领取资格",
        "reserve": "预约领取", "wait": "等待领取处理", "authorize": "批准领取", "owner_result": "记录领取端结果",
        "receipt_decision": "校验领取收据", "receipt_commit": "提交领取收据", "receipt_reject": "拒绝领取收据",
        "receipt_ack": "确认领取收据", "retry": "再次发送领取请求", "return": "归还领取预约", "xp_grant": "发放经验",
        "collector_wait": "等待收据发送", "request_resolved": "结束领取请求", "request_reject": "拒绝领取请求", "disappearance": "观察拾取物消失"}
    outcomes = {"Accepted": "接受", "Rejected": "拒绝", "Ignored": "忽略", "Deferred": "延后处理", "Applied": "已应用",
        "Applying": "开始应用", "Sent": "已发送", "Received": "已接收", "Produced": "已输出", "Confirmed": "已确认",
        "Enqueued": "已入队", "Drained": "已组成批次", "Resolved": "已计算", "Observed": "已观测", "Completed": "已完成",
        "Failed": "失败", "Started": "开始", "Blocked": "受阻", "Waiting": "等待", "Granted": "已发放"}
    reasons = {"Coalesced": "多个变化合并，不能拆为单击结果", "Direct": "直接输出", "ReliableCommand": "可靠命令传输",
        "DuplicateEvent": "重复事件，不重复结算", "WrongOwner": "模拟权限不符", "WrongEpoch": "权限版本不符",
        "WrongRound": "对局轮次不符", "StaleVersion": "状态版本过旧", "OlderStateVersion": "状态版本过旧", "PickupReceiptFact": "拾取收据事实"}
    title = stages.get(stage, "已记录的业务阶段")
    if stage.startswith("investigation.pickup."):
        title = "拾取：" + pickup.get(stage.rsplit(".", 1)[-1], "记录领取阶段")
    elif stage.startswith("investigation.selection."):
        title = "选卡：" + {"request": "提交选择请求", "decision": "判断选择", "offer": "生成候选", "commit": "确认选择",
                            "reject": "拒绝选择", "result": "记录选择结果"}.get(stage.rsplit(".", 1)[-1], "记录选择阶段")
    elif stage == "replay.external" and record.get("reason") == "PickupReceiptFact":
        title = "校验拾取收据事实"
    elif stage == "replay.input" and category == "pickup":
        title = "处理包含拾取收据的战斗批次"
    parts = [title]
    if record.get("outcome") in outcomes:
        parts.append(outcomes[record["outcome"]])
    if record.get("reason") in reasons:
        parts.append(reasons[record["reason"]])
    for key in ("Health", "health", "localHealth"):
        before, after = record.get("before"), record.get("after")
        if isinstance(before, dict) and isinstance(after, dict) and key in before and key in after:
            parts.append(f"生命 {before[key]} → {after[key]}")
    return " · ".join(parts)


def _explain_record(record, pointer, catalog, aggregate=False):
    """Explain observed stages, preserving per-capture order and aggregate boundaries."""
    data = record.get("input")
    nested = data.get("data", data) if isinstance(data, dict) else {}
    before, after = record.get("before"), record.get("after")
    if isinstance(nested, dict):
        before = before if before is not None else nested.get("before")
        after = after if after is not None else nested.get("after")
        if before is None and "beforeHealth" in nested:
            before = {"health": nested["beforeHealth"]}
        if after is None and "afterHealth" in nested:
            after = {"health": nested["afterHealth"]}
    stage = record.get("stage", "")
    if stage in ("network.transport", "network.receive", "network.batch", "network.application", "network.connection") and isinstance(data, dict):
        label = {"network.transport": "Steam 接受／拒绝发送", "network.receive": "对端取出消息（尚不证明业务应用）",
                 "network.batch": "只读批次解码与真实业务对象", "network.application": "业务应用／拒绝事实", "network.connection": "连接生命周期"}[stage]
        kind = "input" if stage in ("network.batch", "network.connection") else "change" if stage == "network.application" else "decision"
        shown = dict(data)
        if record.get("associationReason") == "ConnectionLifetimeIdentityOnly":
            shown["contextScope"] = record.get("contextScope")
            shown["contextPurpose"] = "仅证明当时的连接身份，不关联其他轮次的业务或后续生命周期"
        if stage == "network.batch" and isinstance(data.get("members"), list):
            shown["members"] = []
            for member in data["members"]:
                member = dict(member)
                if "entity" in member: member["routeEntity"] = member.pop("entity")
                shown["members"].append(member)
        return [dict(capture=record["captureId"], role=record.get("role"), stage=stage, evidence=pointer,
                     outsideSelection=record.get("outsideSelection", False), kind=kind, title=label,
                     fields=dict(shown, outcome=record.get("outcome"), reason=record.get("reason")), status="observed")], []
    if stage == "investigation.state" and isinstance(data, dict):
        after = data.get("state")
    caption = _display_summary(record, _record_category(record, data))
    common = {"capture": record["captureId"], "role": record.get("role"), "stage": stage,
              "evidence": pointer, "outsideSelection": record.get("outsideSelection", False)}
    prefix = f"{record['captureId'][:8]} / {record.get('role', 'Unknown')} · "
    # Full inputs are preserved; display names are additive, never substituted for IDs.
    named = [field for field in _field_rows(data, pointer, "observed", catalog) if field.get("contentName")]
    decision = {"outcome": record.get("outcome"), "reason": record.get("reason")}
    if isinstance(nested, dict):
        decision.update({key: nested[key] for key in ("reason", "actualAwarded", "actualRestored", "benefitConfirmed",
                        "awaitingServerCommit", "accepted", "valid", "eventId", "optionId") if key in nested})
    if aggregate:
        decision["attribution"] = "合并后的权威状态不能拆分为单次命中的独立结果"
    output = [dict(common, kind="input", title=prefix + caption + " · 输入", fields=data,
                   status="observed" if data is not None else "unknown", contentNames=named),
              dict(common, kind="decision", title=prefix + caption + " · 判断", fields=decision,
                   status="observed" if any(v is not None for v in decision.values()) else "unknown"),
              dict(common, kind="change", title=prefix + caption + " · 变化", fields={"before": before, "after": after},
                   status="observed" if before is not None or after is not None else "unknown",
                   attribution="aggregate" if aggregate else "recorded-stage")]
    unknowns = []
    if before is None and after is None:
        output[-1]["reason"] = "NoRecordedBeforeAfterState"
    if isinstance(nested, dict) and nested.get("benefitConfirmed") is False:
        unknowns.append("DisappearanceOrResolutionDoesNotProveBenefit")
    return output, unknowns


def _elapsed(record, context):
    mono = record.get("monotonicTime")
    if _finite(mono) and context["timeOrigin"].get("monotonic") is not None:
        value = mono - context["timeOrigin"]["monotonic"]
        return value if value >= 0 else None
    return None


def _refs(db, capture, seq):
    return [dict(row) for row in db.execute("SELECT path,line FROM copies WHERE capture=? AND seq=? ORDER BY path,line", (capture, str(seq)))]


def build_index(database_path):
    """Write derived tables only. Caller must supply a newly created analysis DB."""
    db = sqlite3.connect(database_path)
    db.row_factory = sqlite3.Row
    if db.execute("SELECT 1 FROM sqlite_master WHERE name='investigation_meta'").fetchone():
        db.close()
        raise ValueError("Investigation index already exists; prepare a fresh analysis directory")
    try:
        db.executescript(SCHEMA)
        contexts, entities, active, builds, catalogs = {}, {}, {}, {}, {}
        pickup_events = set()
        package_origins = {}
        for package_row in db.execute("SELECT body FROM metadata WHERE kind='investigation-package.json'"):
            for item in json.loads(package_row[0]).get("contexts", []):
                if item.get("timeOrigin"):
                    package_origins[(item["capture"], item["run"], int(item["round"]))] = item["timeOrigin"]
        records_count = 0
        for row in db.execute("SELECT body FROM records ORDER BY capture,length(seq),seq"):
            record = json.loads(row[0])
            capture, seq = record["captureId"], str(record["recordSequence"])
            run, round_id = str(record.get("runId") or ""), int(record.get("round", 0))
            context_key = (capture, run, round_id)
            if context_key not in contexts:
                contexts[context_key] = dict(capture=capture, run=run, round=round_id, roles=[], buildId=None,
                    first=seq, last=seq, recordCount=0, timeOrigin={"sequence": seq, "utc": record.get("utc"),
                    "monotonic": record.get("monotonicTime") if _finite(record.get("monotonicTime")) else None,
                    **CLOCK})
            context = contexts[context_key]
            if context_key in package_origins:
                context["timeOrigin"] = package_origins[context_key]
            context["last"], context["recordCount"] = seq, context["recordCount"] + 1
            role = str(record.get("role") or "Unknown")
            if role not in context["roles"]:
                context["roles"].append(role)
            elapsed = _elapsed(record, context)
            stage = str(record.get("stage") or "")
            # Replay inputs/external facts can contain actual batched business identities.
            # Checkpoints/outputs stay queryable without expanding large engine snapshots here.
            data = _payload(db, record) if not stage.startswith("replay.") or stage in ("replay.input", "replay.external") else None
            if stage in ("process.start", "source.start") and isinstance(data, dict):
                info = data.get("buildInfo") or {}
                try:
                    info = json.loads(info) if isinstance(info, str) else info
                except ValueError:
                    info = {}
                identity_value = {"buildId": info.get("buildId"), "buildGuid": data.get("buildGuid"),
                                  "version": data.get("version"), "protocol": data.get("protocol")}
                if capture in builds and builds[capture] != identity_value:
                    context["identityErrors"] = ["ConflictingCaptureBuildIdentity"]
                else:
                    builds[capture] = identity_value
                catalogs[capture] = _catalog(data, info.get("buildId"))
                db.execute("INSERT INTO investigation_catalog VALUES(?,?,?,?,?)", (capture, seq, run, round_id, _json(catalogs[capture])))
            source, target = _id(record.get("source")), _id(record.get("target"))
            life_key = context_key + (target,)
            explicit_generation = record.get("entityGeneration")
            identity = data.get("identity", {}) if isinstance(data, dict) else {}
            if stage == "network.identity" and target:
                epoch = record.get("connectionEpoch")
                if epoch:
                    active[life_key] = f"avatar:{target}:epoch:{epoch}"
            if explicit_generation:
                active[life_key] = str(explicit_generation)
            if stage in ("investigation.state", "investigation.build.begin") and isinstance(identity, dict) and identity.get("birth"):
                active[context_key + (_id(data.get("entity")),)] = str(identity["birth"])
            generation = str(explicit_generation or active.get(life_key) or "unknown")
            category = _record_category(record, data)
            if stage.startswith("network.") and isinstance(data, dict):
                instance = str(data.get("connectionInstance") or "unknown")
                direction = str(data.get("direction") or "")
                attempt = data.get("attempt") if direction == "Send" else data.get("receiveSequence")
                db.execute("INSERT INTO investigation_network VALUES(?,?,?,?,?,?,?,?,?)", (capture, seq, instance, direction,
                    str(data.get("channel", "")), str(data.get("result", "")), str(attempt) if attempt is not None else None,
                    str(data.get("messageNumber")) if data.get("messageNumber") is not None else None, str(data.get("lane", "0"))))
                if instance != "unknown" and attempt is not None and direction in ("Send", "Receive"):
                    db.execute("INSERT OR IGNORE INTO investigation_links VALUES(?,?,?,?)", (capture, seq, "network-attempt", _json([capture, run, round_id, instance, direction, str(attempt)])))
            db.execute("INSERT INTO investigation_records VALUES(?,?,?,?,?,?,?,?,?,?,?,?,?)",
                (capture, seq, run, round_id, elapsed, elapsed if elapsed is not None else 1e308,
                 stage, category, source, target, generation, role, _summary(record, data)))
            members = set()
            def member(entity, direction, generation_hint=None, name=None, kind=None, details=None):
                entity = _id(entity)
                if not entity:
                    return
                gen = str(generation_hint or active.get(context_key + (entity,)) or "unknown")
                members.add((entity, gen, direction))
                key = context_key + (entity, gen)
                current = entities.setdefault(key, dict(capture=capture, run=run, round=round_id,
                    entity=entity, generation=gen, name=None, kind="unknown", first=elapsed, last=elapsed,
                    firstEvidence={"capture": capture, "sequence": seq}, lastEvidence={"capture": capture, "sequence": seq},
                    identityStatus="unknown" if gen == "unknown" else "observed"))
                if name:
                    current["name"] = name
                if kind:
                    current["kind"] = kind
                current["last"], current["lastEvidence"] = elapsed, {"capture": capture, "sequence": seq}
                if details:
                    current.update(details)
            member(source, "source")
            member(target, "target", generation, data.get("name") if isinstance(data, dict) else None,
                   "enemy" if stage.startswith("entity.") else "player" if stage == "network.identity" else None)
            for value in _walk(data):
                if stage == "network.batch":
                    for entity in value.get("entities") or []:
                        member(entity.get("entityId", entity.get("entity")) if isinstance(entity, dict) else entity, "target")
                        if isinstance(entity, dict): member(entity.get("source"), "source")
                if stage == "network.application":
                    member(value.get("target"), "target")
                    member(value.get("source"), "source")
                for key, candidate in value.items():
                    normalized = key.replace("_", "").lower()
                    if normalized in ("sourceentityid", "sourceplayerid", "playerid", "ownerplayerid"):
                        member(candidate, "source")
                    elif normalized in ("targetentityid", "enemyentityid", "targetid", "entityid"):
                        member(candidate, "target")
                for kind, names in (("drop", ("dropId", "PickupDropId")), ("event", ("eventId", "EventId", "CauseEventId"))):
                    for name in names:
                        if _id(value.get(name)):
                            link_kind, link_value = kind, str(value[name])
                            if kind == "event" and isinstance(data, dict) and (stage.startswith("investigation.selection.") or data.get("domain") == "selection"):
                                link_kind = "selection-event"
                                link_value = str(data.get("avatar") or data.get("entity") or source or target or "unknown") + ":" + link_value
                            db.execute("INSERT OR IGNORE INTO investigation_links VALUES(?,?,?,?)", (capture, seq, link_kind, link_value))
                            if kind == "event" and category == "pickup":
                                pickup_events.add((run, round_id, str(value[name])))
                drop = _id(value.get("PickupDropId")) or _id(value.get("dropId"))
                claim = _id(value.get("PickupClaimVersion")) or _id(value.get("claimVersion"))
                if drop and claim:
                    db.execute("INSERT OR IGNORE INTO investigation_links VALUES(?,?,?,?)", (capture, seq, "drop-claim", _json([drop, claim])))
            if category == "pickup" and _id(record.get("eventId")):
                pickup_events.add((run, round_id, str(record["eventId"])))
            if isinstance(data, dict):
                operation_id = _id(data.get("operationId"))
                if operation_id and stage in ("investigation.build.begin", "investigation.state"):
                    operation_entity = _id(data.get("entity")) or target
                    operation_generation = active.get(context_key + (operation_entity,), "unknown")
                    operation_perspective = str(data.get("perspective") or role)
                    operation_key = _json([capture, operation_generation, operation_perspective, operation_id])
                    db.execute("INSERT OR IGNORE INTO investigation_links VALUES(?,?,?,?)", (capture, seq, "build-operation", operation_key))
                    if stage == "investigation.build.begin":
                        db.execute("INSERT INTO investigation_operations VALUES(?,?,?,?,?,?,?,?,?)", (capture, seq, run, round_id,
                            operation_entity, operation_generation, operation_perspective, operation_key, elapsed))
                for actor in data.get("actors", []) if stage == "observation.snapshot" else []:
                    if not isinstance(actor, dict):
                        continue
                    entity = _id(actor.get("entity"))
                    if entity is None:
                        continue
                    gen = active.get(context_key + (entity,), "unknown")
                    member(entity, "observation", gen, actor.get("name"), actor.get("kind") or "enemy")
                    state = dict(schemaVersion=0, domain="observation", perspective=role, baseline=False,
                                 continuous=False, state=actor, stateRevision=None)
                    db.execute("INSERT OR REPLACE INTO investigation_states VALUES(?,?,?,?,?,?,?,?,?,?)",
                        (capture, seq, run, round_id, entity, gen, "observation", role, elapsed, _json(state)))
                if stage == "investigation.state":
                    entity = _id(data.get("entity")) or target
                    gen = active.get(context_key + (entity,), "unknown")
                    perspective = str(data.get("perspective") or role)
                    domain = str(data.get("domain") or "unknown")
                    member(entity, "target", gen, details={"identity": identity})
                    db.execute("INSERT OR REPLACE INTO investigation_states VALUES(?,?,?,?,?,?,?,?,?,?)",
                        (capture, seq, run, round_id, entity, gen, domain, perspective, elapsed, _json(data)))
            db.executemany("INSERT OR IGNORE INTO investigation_members VALUES(?,?,?,?,?)",
                           ((capture, seq, entity, gen, direction) for entity, gen, direction in members))
            if stage.startswith(("investigation.catalog", "content.catalog")) and isinstance(data, dict):
                db.execute("INSERT INTO investigation_catalog VALUES(?,?,?,?,?)", (capture, seq, run, round_id, _json(data)))
            samples = []
            if stage == "performance.snapshot" and isinstance(data, dict):
                frame_values = {k: v for k, v in data.items() if k.startswith(("frame", "longFrame", "omittedLong", "gc", "gen", "workingSet", "privateBytes", "systemAvailable", "allocated"))}
                writer_values = {k: v for k, v in data.items() if k.startswith(("evidence", "logQueued", "logFailures"))}
                samples.append(("frames", "observed" if frame_values else "unknown", _measurements(frame_values)))
                samples.append(("writer", "observed" if writer_values else "unknown", _measurements(writer_values)))
                waits = {k: data[k] for k in ("pendingDeaths", "oldestPendingDeathSeconds", "lastDeathConfirmationSeconds", "maximumDeathConfirmationSeconds", "deathReports", "deathReceipts", "confirmedKills") if k in data}
                if waits:
                    samples.append(("business", "observed", _measurements(waits)))
                connections = data.get("connections")
                if connections:
                    valid = any(c.get("queueValid") is True or c.get("pendingValid") is True or c.get("available") is True or c.get("valid") is True
                                for c in connections if isinstance(c, dict))
                    invalid = any(c.get("queueValidity") in ("ReadFailed", "Negative", "AboveOneHourUnverified") for c in connections if isinstance(c, dict))
                    samples.append(("steam", "observed" if valid else "invalid" if invalid else "unknown",
                                    {"connections": [_steam_connection(c) for c in connections if isinstance(c, dict)], "transport": data.get("transport"), "sendFailures": data.get("sendFailures"),
                                     "networkInterval": data.get("networkInterval"), "reliableSnapshotPackets": data.get("reliableSnapshotPackets"),
                                     "metricValidityIsPerField": True, "zeroWithoutValidFlagIsNotAnObservation": True}))
            if stage in ("network.transport", "network.connection"):
                samples.append(("steam", "observed", {"stage": stage, "outcome": record.get("outcome"), "reason": record.get("reason"), "input": data}))
            if category not in ("replay", "performance") and any(word in stage for word in ("submit", "report", "decision", "receipt", "apply", "pickup", "selection")):
                samples.append(("business", "observed", {"stage": stage, "outcome": record.get("outcome"), "reason": record.get("reason"),
                    "eventId": str(record.get("eventId") or "0"), "source": source, "target": target,
                    "inputAvailableInRecordDetail": data is not None}))
            db.executemany("INSERT OR REPLACE INTO investigation_samples VALUES(?,?,?,?,?,?,?,?)",
                ((capture, seq, run, round_id, track, elapsed, validity, _json(body)) for track, validity, body in samples))
            if stage == "entity.destroy":
                # Keep the recorded generation on this row; later unobserved births are not guessed.
                active.pop(life_key, None)
            records_count += 1
        # Direct event identity links preserve historical pickup facts through authority
        # output stages even when the later row no longer repeats the pickup report.
        for run, round_id, event in pickup_events:
            for linked in db.execute("SELECT capture,seq,server FROM records WHERE run=? AND round=? AND event=?", (run, round_id, event)):
                db.execute("UPDATE investigation_records SET category='pickup' WHERE capture=? AND seq=?", (linked["capture"], linked["seq"]))
                if linked["server"]:
                    for output in db.execute("SELECT capture,seq FROM records WHERE run=? AND round=? AND server=?", (run, round_id, linked["server"])):
                        db.execute("UPDATE investigation_records SET category='pickup' WHERE capture=? AND seq=?", tuple(output))
        # A failed-send header and its decoded batch share an exact local attempt key.
        # Route objects are excluded; only actual typed business members are inherited.
        db.execute("""INSERT OR IGNORE INTO investigation_members
            SELECT header.capture,header.seq,m.entity,m.generation,m.direction
            FROM investigation_links header JOIN investigation_links batch ON batch.kind=header.kind AND batch.value=header.value
            JOIN investigation_members m ON m.capture=batch.capture AND m.seq=batch.seq
            WHERE header.kind='network-attempt'""")
        for context in contexts.values():
            context.update(builds.get(context["capture"], {}))
            context["contentCatalog"] = catalogs.get(context["capture"], {"status": "unknown", "reason": "ContentCatalogNotCaptured", "entries": []})
            db.execute("INSERT INTO investigation_context VALUES(?,?,?,?)", (context["capture"], context["run"], context["round"], _json(context)))
        for item in entities.values():
            db.execute("INSERT INTO investigation_entities VALUES(?,?,?,?,?,?,?,?,?,?)",
                (item["capture"], item["run"], item["round"], item["entity"], item["generation"], item["name"], item["kind"], item["first"], item["last"], _json(item)))
        # One full assessment at index preparation, never once per browser page.
        assessed = evidence.coverage(db)
        db.executemany("INSERT INTO investigation_meta VALUES(?,?)", [("version", str(VERSION)),
            ("revision", uuid.uuid4().hex), ("coverage", _json(assessed)), ("records", str(records_count))])
        db.commit()
        return {"database": str(Path(database_path).resolve()), "records": records_count, "contexts": len(contexts), "version": VERSION}
    finally:
        db.close()


class Investigation:
    def __init__(self, database_path):
        self.db = _read_only(database_path)
        try:
            self.meta = dict(self.db.execute("SELECT key,value FROM investigation_meta"))
            if int(self.meta.get("version", 0)) != VERSION:
                raise ValueError("Prepare an investigation index before opening it")
            self._coverage = json.loads(self.meta["coverage"])
            self._totals = {}
            self._performance_cache = {}
            self._writer_snapshot_cache = {}
            self._role_cache = {}
            self._package = None
            self._package_errors = []
            packages = list(self.db.execute("SELECT body FROM metadata WHERE kind='investigation-package.json'"))
            if packages:
                if len(packages) != 1:
                    raise ValueError("Conflicting investigation package manifests")
                self._package = json.loads(packages[0][0])
                self._package_errors.extend(self._package.get("verificationErrors", []))
                present = {(r[0], r[1]) for r in self.db.execute("SELECT capture,seq FROM records")}
                for item in self._package.get("required", []):
                    key = (item.get("capture", item.get("captureId")), str(item.get("sequence", item.get("recordSequence"))))
                    if key not in present:
                        self._package_errors.append("RequiredRecordMissing:" + ":".join(key))
                conflicts = self.db.execute("SELECT count(*) FROM issues WHERE reason='ConflictingCopy'").fetchone()[0]
                if conflicts:
                    self._package_errors.append("ConflictingCopy")
        except BaseException:
            self.close()
            raise

    def close(self):
        self.db.close()

    def __enter__(self):
        return self

    def __exit__(self, *_):
        self.close()

    def _selection(self, selection):
        s = dict(selection)
        if not isinstance(s.get("run"), str) or not s["run"] or s["run"] == "boot":
            raise ValueError("Select one explicit gameplay run")
        if not isinstance(s.get("round"), int) or isinstance(s["round"], bool) or s["round"] < 0:
            raise ValueError("Select an integer round")
        captures = s.get("captures") or []
        if not isinstance(captures, list) or any(not isinstance(c, str) or not c for c in captures):
            raise ValueError("captures must be a list of capture IDs")
        known = {r[0] for r in self.db.execute("SELECT capture FROM investigation_context WHERE run=? AND round=?", (s["run"], s["round"]))}
        if not known or not set(captures) <= known:
            raise ValueError("Unknown run/round/capture selection")
        s["captures"] = sorted(set(captures) or known)
        if s.get("entityCapture") is not None and s["entityCapture"] not in s["captures"]:
            raise ValueError("entityCapture must belong to the selected captures")
        for key in ("after", "before"):
            if s.get(key) is not None and (not _finite(s[key]) or s[key] < 0):
                raise ValueError(f"{key} must be finite nonnegative elapsed seconds")
        if s.get("after") is not None and s.get("before") is not None and s["after"] > s["before"]:
            raise ValueError("after must not exceed before")
        if s.get("direction", "any") not in ("any", "source", "target"):
            raise ValueError("Unknown entity direction")
        if s.get("category") not in (None, "all", "business", "attack", "damage", "movement", "pickup", "selection", "state", "sync", "performance", "replay", "network_rejection"):
            raise ValueError("Unknown investigation category")
        for field in ("connectionInstance", "connectionCapture", "networkDirection", "channel", "returnCode"):
            if s.get(field) is not None:
                s[field] = str(s[field])
                if len(s[field]) > 256: raise ValueError("Network filter too long")
        if s.get("entity") is not None:
            s["entity"] = str(s["entity"])
        return s

    def _where(self, s, alias="r", time=True, filters=True):
        p = alias + "." if alias else ""
        clauses = [p + "run=?", p + "round=?", p + "capture IN (" + ",".join("?" for _ in s["captures"]) + ")"]
        values = [s["run"], s["round"], *s["captures"]]
        if time:
            for bound, op in (("after", ">="), ("before", "<=")):
                if s.get(bound) is not None:
                    clauses.append(p + "elapsed" + op + "?"); values.append(s[bound])
        if filters:
            if s.get("connectionCapture"):
                clauses.append(p + "capture=?"); values.append(s["connectionCapture"])
            for key, column in (("connectionInstance", "instance"), ("networkDirection", "direction"), ("channel", "channel"), ("returnCode", "result")):
                if s.get(key) is not None:
                    clauses.append("EXISTS(SELECT 1 FROM investigation_network n WHERE n.capture=" + p + "capture AND n.seq=" + p + "seq AND n." + column + "=?)")
                    values.append(s[key])
            category = s.get("category")
            if category == "business":
                clauses.append("(" + p + "category IN (" + ",".join("?" for _ in BUSINESS_CATEGORIES) + ") OR " +
                               p + "stage IN (" + ",".join("?" for _ in BUSINESS_STAGES) + "))")
                values.extend(BUSINESS_CATEGORIES + BUSINESS_STAGES)
            elif category and category != "all":
                clauses.append(p + "category=?"); values.append(category)
            if s.get("entity") is not None:
                if s.get("entityCapture") is not None:
                    clauses.append(p + "capture=?"); values.append(s["entityCapture"])
                sub = ["m.capture=" + p + "capture", "m.seq=" + p + "seq", "m.entity=?"]
                values.append(s["entity"])
                if s.get("generation"):
                    sub.append("m.generation=?"); values.append(s["generation"])
                if s.get("direction", "any") != "any":
                    sub.append("m.direction=?"); values.append(s["direction"])
                clauses.append("EXISTS(SELECT 1 FROM investigation_members m WHERE " + " AND ".join(sub) + ")")
        return " AND ".join(clauses), values

    def matches(self):
        grouped = {}
        for row in self.db.execute("SELECT body FROM investigation_context WHERE run<>'boot' ORDER BY run,round,capture"):
            item = json.loads(row[0]); key = (item["run"], item["round"])
            role_key = (item["capture"], *key)
            if role_key not in self._role_cache:
                roles = set()
                for _, data in self._performance_rows({"run": item["run"], "round": item["round"], "captures": [item["capture"]]}):
                    role = str(data.get("role") or "").lower()
                    if role in ("host", "client", "server", "offline"):
                        roles.add(role)
                self._role_cache[role_key] = sorted(roles)
            actual = self._role_cache[role_key]
            active_roles = [r for r in actual if r != "offline"]
            item["actualRoles"] = actual
            item["actualRole"] = active_roles[0] if len(active_roles) == 1 else "offline" if actual == ["offline"] else "unknown"
            item["actualRoleSource"] = "performance.role" if actual else "not-recorded"
            item["lightweightNetwork"] = self.db.execute("SELECT 1 FROM metadata WHERE kind='network-diagnostics-source' AND body LIKE ?", ('%"' + item["capture"] + '"%',)).fetchone() is not None
            grouped.setdefault(key, {"run": key[0], "round": key[1], "captures": []})["captures"].append(item)
        return {"matches": list(grouped.values()), "clock": CLOCK, "revision": self.meta["revision"]}

    def _performance_rows(self, selection):
        where, values = self._where(selection, "s", filters=False)
        for row in self.db.execute("""SELECT s.capture,s.seq,s.elapsed,r.body FROM investigation_samples s
                CROSS JOIN records r WHERE """ + where + " AND s.track='frames' AND r.capture=s.capture AND r.seq=s.seq", values):
            key = (row["capture"], row["seq"])
            if key not in self._performance_cache:
                self._performance_cache[key] = _payload(self.db, json.loads(row["body"]))
            data = self._performance_cache[key]
            if isinstance(data, dict):
                yield row, data

    def _writer_snapshot_rows(self, selection):
        """Point observations already captured by the combat writer, without actor expansion."""
        where, values = self._where(selection, "s", filters=False)
        fields = ("pendingBytes", "peakQueueBytes", "retainedBytes", "peakRetainedBytes", "dropped", "writerFailure", "writerMs",
                  "sinkCalls", "sinkFailures", "sinkMs", "checkpointMs", "evidenceStages")
        for row in self.db.execute("""SELECT s.capture,s.seq,s.elapsed,r.body
                FROM investigation_records s INDEXED BY investigation_category CROSS JOIN records r WHERE """ + where +
                " AND s.category='state' AND s.stage='observation.snapshot' AND r.capture=s.capture AND r.seq=s.seq", values):
            key = (row["capture"], row["seq"])
            if key not in self._writer_snapshot_cache:
                data = _payload(self.db, json.loads(row["body"]))
                self._writer_snapshot_cache[key] = {field: data[field] for field in fields if field in data} if isinstance(data, dict) else {}
            data = self._writer_snapshot_cache[key]
            if data:
                yield row, data

    def entities(self, selection):
        s = self._selection(selection); where, values = self._where(s, "", time=False, filters=False)
        rows = self.db.execute("SELECT body FROM investigation_entities WHERE " + where + " ORDER BY capture,length(entity),entity,generation", values)
        items = [json.loads(r[0]) for r in rows]
        if s.get("entity"):
            items = [i for i in items if i["entity"] == s["entity"]]
        if s.get("entityCapture"):
            items = [i for i in items if i["capture"] == s["entityCapture"]]
        return {"items": items, "clock": CLOCK}

    def _pointer(self, capture, seq):
        return {"capture": capture, "sequence": str(seq), "references": _refs(self.db, capture, seq)}

    def _item(self, row):
        raw = self.db.execute("SELECT body FROM records WHERE capture=? AND seq=?", (row["capture"], row["seq"])).fetchone()
        summary = row["summary"] if row["category"] == "network_rejection" else _display_summary(json.loads(raw[0]), row["category"]) if raw else row["summary"]
        return {"capture": row["capture"], "sequence": row["seq"], "elapsed": row["elapsed"],
                "stage": row["stage"], "category": row["category"], "source": row["source"], "target": row["target"],
                "entityGeneration": row["generation"], "role": row["role"], "summary": summary,
                "evidence": self._pointer(row["capture"], row["seq"])}

    def timeline(self, selection, cursor=None, limit=200):
        s = self._selection(selection)
        if not isinstance(limit, int) or isinstance(limit, bool) or not 1 <= limit <= 1000:
            raise ValueError("Page size must be 1..1000")
        where, values = self._where(s)
        fingerprint = hashlib.sha256(_json(s).encode()).hexdigest()
        if fingerprint not in self._totals:
            self._totals[fingerprint] = self.db.execute("SELECT count(*) FROM investigation_records r WHERE " + where, values).fetchone()[0]
        total = self._totals[fingerprint]
        if cursor:
            try:
                token = json.loads(base64.urlsafe_b64decode(cursor.encode()))
                if token["revision"] != self.meta["revision"] or token["selection"] != fingerprint:
                    raise ValueError("Cursor belongs to a different query or database")
                key = token["key"]
                if len(key) != 4 or not _finite(key[0]) or not isinstance(key[1], str) or not isinstance(key[2], int) or not str(key[3]).isdigit():
                    raise ValueError("Invalid cursor key")
            except (ValueError, KeyError, TypeError, UnicodeError) as error:
                raise ValueError("Invalid or mismatched cursor") from error
            where += " AND (r.sort_time,r.capture,length(r.seq),r.seq)>(?,?,?,?)"
            values += key
        rows = list(self.db.execute("SELECT r.* FROM investigation_records r WHERE " + where +
                    " ORDER BY r.sort_time,r.capture,length(r.seq),r.seq LIMIT ?", values + [limit + 1]))
        next_cursor = None
        if len(rows) > limit:
            r = rows[limit - 1]
            token = {"revision": self.meta["revision"], "selection": fingerprint,
                     "key": [r["sort_time"], r["capture"], len(r["seq"]), r["seq"]]}
            next_cursor = base64.urlsafe_b64encode(_json(token).encode()).decode()
        return {"items": [self._item(row) for row in rows[:limit]], "nextCursor": next_cursor, "total": total,
                "clock": CLOCK, "coverage": self.coverage(s)}

    def coverage(self, selection):
        s = self._selection(selection)
        original = self._coverage
        package_scope = None
        if self._package:
            declared = self._package.get("selection", {})
            captures = declared.get("captures") or s["captures"]
            supported = (s["run"] == declared.get("run") and s["round"] == declared.get("round") and set(s["captures"]) <= set(captures))
            for key in ("entity", "entityCapture", "generation", "direction"):
                if declared.get(key) not in (None, "any") and s.get(key) != declared[key]:
                    supported = False
            declared_category = declared.get("category")
            if declared_category not in (None, "all") and s.get("category") != declared_category:
                if declared_category != "business" or s.get("category") not in BUSINESS_CATEGORIES:
                    supported = False
            for key, compare in (("after", lambda a, b: a >= b), ("before", lambda a, b: a <= b)):
                if declared.get(key) is not None and (s.get(key) is None or not compare(s[key], declared[key])):
                    supported = False
            original = self._package.get("originalCoverage") or {}
            package_scope = {"status": "within-package" if supported else "outside-package", "selection": declared,
                             "omissions": self._package.get("omissions"), "verificationErrors": self._package_errors,
                             "sourceHistoryPreserved": True}
            if not supported:
                return {"complete": False, "status": "unknown", "intervals": [], "scopeGaps": [{"reason": "OutsideInvestigationPackage"}],
                        "packageScope": package_scope, "originalCoverage": original}
        intervals = [i for i in original.get("intervals", []) if i.get("captureId") in s["captures"] and
                     i.get("runId") == s["run"] and str(i.get("round")) == str(s["round"])]
        gaps = [g for g in original.get("scopeGaps", []) if not g.get("runId") or g["runId"] == s["run"]]
        if self._package_errors:
            gaps += [{"reason": "InvestigationPackageVerificationFailed", "details": self._package_errors}]
        return {"complete": bool(intervals) and all(i["complete"] for i in intervals) and not gaps and (self._package is None or self._package.get("complete") is True),
                "intervals": intervals, "scopeGaps": gaps, "scope": "whole-selected-capture-run-round",
                "timeFilterDoesNotEraseHistoricalGaps": True, "packageScope": package_scope}

    def _record(self, capture, seq):
        row = self.db.execute("SELECT body FROM records WHERE capture=? AND seq=?", (capture, str(seq))).fetchone()
        if not row:
            raise ValueError("Unknown record")
        record = json.loads(row[0]); original = json.loads(row[0])
        record["references"] = _refs(self.db, capture, seq)
        for field in ("input", "before", "after"):
            try:
                record[field] = evidence.payload(self.db, record, field)
            except ValueError as error:
                record.setdefault("missingPayloads", []).append({"field": field, "reason": str(error)})
        record["raw"] = original
        return record

    def _selected_record(self, s, capture, seq):
        where, values = self._where(s)
        return self.db.execute("SELECT 1 FROM investigation_records r WHERE " + where + " AND r.capture=? AND r.seq=?", values + [capture, str(seq)]).fetchone() is not None

    def context_records(self, selection, seeds):
        """Full scoped closure, with no report-size cap; returns resolved original records."""
        s = self._selection(selection)
        pending, seen, candidates = [], set(), {}
        context_keys, connection_context_keys, seed_keys = set(), set(), set()
        for seed in seeds:
            capture = seed.get("capture", seed.get("captureId")); seq = seed.get("sequence", seed.get("recordSequence"))
            row = self.db.execute("SELECT run,round FROM records WHERE capture=? AND seq=?", (capture, str(seq))).fetchone()
            if not row or capture not in s["captures"] or row["run"] != s["run"] or row["round"] != s["round"]:
                raise ValueError("Seed lies outside the selected match/captures")
            pending.append((capture, str(seq)))
            seed_keys.add((capture, str(seq)))
        # Keep matched source/build/catalog context, even outside the requested time window.
        for r in self.db.execute("SELECT capture,seq FROM investigation_catalog WHERE run=? AND round=?", (s["run"], s["round"])):
            if r["capture"] in s["captures"]:
                pending.append(tuple(r))
                context_keys.add(tuple(r))
        for row in self.db.execute("SELECT body FROM investigation_context WHERE run=? AND round=?", (s["run"], s["round"])):
            context = json.loads(row[0])
            if context["capture"] in s["captures"]:
                pending.append((context["capture"], context["first"]))
                context_keys.add((context["capture"], context["first"]))
        expanded_events, expanded_links, expanded_servers = set(), set(), set()
        while pending:
            capture, seq = pending.pop()
            if (capture, seq) in seen:
                continue
            seen.add((capture, seq))
            record = self._record(capture, seq)
            if (capture, seq) in context_keys and (capture, seq) not in seed_keys and record.get("stage", "").startswith("network."):
                # A time-origin record or an earlier connection binding is retained
                # for provenance only, not recursively used to expand another session.
                continue
            # A connection binding is context, never a key that recursively joins every
            # packet in a session. Attempts are joined only within this capture/lifetime.
            net = self.db.execute("SELECT instance FROM investigation_network WHERE capture=? AND seq=?", (capture, seq)).fetchone()
            if net and net[0] != "unknown":
                for binding in self.db.execute("""SELECT n.capture,n.seq FROM investigation_network n JOIN records r USING(capture,seq)
                    WHERE n.capture=? AND n.instance=? AND r.stage='network.connection'
                    AND (length(n.seq),n.seq)<=(?,?) ORDER BY length(n.seq) DESC,n.seq DESC LIMIT 1""", (capture, net[0], len(seq), seq)):
                    context_keys.add(tuple(binding)); connection_context_keys.add(tuple(binding)); pending.append(tuple(binding))
            events = {str(record[k]) for k in ("eventId", "rootEventId", "parentEventId") if _id(record.get(k))}
            events.update(r[0] for r in self.db.execute("SELECT value FROM investigation_links WHERE capture=? AND seq=? AND kind='event'", (capture, seq)))
            for event_id in events - expanded_events:
                expanded_events.add(event_id)
                for column in ("event", "root", "parent"):
                    pending.extend((r[0], r[1]) for r in self.db.execute("SELECT capture,seq FROM records WHERE run=? AND round=? AND " + column + "=?",
                        (s["run"], s["round"], event_id)) if r[0] in s["captures"])
                # Imported event_links may include batch-number/payload-hash backfills.
                # Only literal event IDs parsed into investigation_links confirm a link.
                pending.extend((r[0], r[1]) for r in self.db.execute("""SELECT r.capture,r.seq
                    FROM investigation_links e INDEXED BY investigation_link_lookup CROSS JOIN records r
                    WHERE e.kind='event' AND e.value=? AND r.capture=e.capture AND r.seq=e.seq AND r.run=? AND r.round=?""",
                    (event_id, s["run"], s["round"])) if r[0] in s["captures"])
                for weak in self.db.execute("""SELECT r.capture,r.seq FROM event_links e INDEXED BY linked_event CROSS JOIN records r
                    WHERE e.event=? AND r.capture=e.capture AND r.seq=e.seq AND r.run=? AND r.round=?""", (event_id, s["run"], s["round"])):
                    if weak[0] in s["captures"]:
                        candidates[(weak[0], weak[1])] = "ImportedFacetOnlyAssociation"
            for kind, value in self.db.execute("SELECT kind,value FROM investigation_links WHERE capture=? AND seq=?", (capture, seq)):
                if (kind, value) in expanded_links:
                    continue
                expanded_links.add((kind, value))
                pending.extend((r[0], r[1]) for r in self.db.execute("""SELECT l.capture,l.seq
                    FROM investigation_links l INDEXED BY investigation_link_lookup CROSS JOIN records r
                    WHERE r.capture=l.capture AND r.seq=l.seq AND r.run=? AND r.round=? AND l.kind=? AND l.value=?""",
                    (s["run"], s["round"], kind, value)) if r[0] in s["captures"])
            server = record.get("serverSequence")
            if server and str(server) != "0" and str(server) not in expanded_servers:
                expanded_servers.add(str(server))
                for linked in self.db.execute("SELECT capture,seq,stage FROM records WHERE run=? AND round=? AND server=?", (s["run"], s["round"], server)):
                    if linked[0] not in s["captures"]:
                        continue
                    if linked[2] in ("network.message", "network.transport"):
                        candidates[(linked[0], linked[1])] = "UnverifiedTransportSequenceAssociation"
                    else:
                        pending.append((linked[0], linked[1]))
        combat_chain = any(self.db.execute("SELECT category FROM investigation_records WHERE capture=? AND seq=?", key).fetchone()[0]
                           in BUSINESS_CATEGORIES for key in seen)
        # SDK numbers are scoped to a directional connection. Reciprocal Steam IDs
        # narrow peer candidates, but local lifetime serials are not a shared nonce.
        # Never upgrade a reused message number to a confirmed delivery by hash/time.
        headers = {}
        for source in self.db.execute("SELECT body FROM metadata WHERE kind='network-diagnostics-source'"):
            body = json.loads(source[0]); header = body.get("header") or {}
            if header.get("captureId") in s["captures"]: headers[header["captureId"]] = header
        for capture, seq in list(seen):
            net = self.db.execute("SELECT * FROM investigation_network WHERE capture=? AND seq=?", (capture, seq)).fetchone()
            if not net or net["direction"] not in ("Send", "Receive") or not str(net["message_number"] or "").isdigit() or int(net["message_number"]) <= 0:
                continue
            local = headers.get(capture, {}).get("localSteamIdentity")
            if not local: continue
            remote_ids = set()
            for body in self.db.execute("""SELECT r.body FROM investigation_network n JOIN records r USING(capture,seq)
                WHERE n.capture=? AND n.instance=? AND r.stage='network.connection'""", (capture, net["instance"])):
                data = _payload(self.db, json.loads(body[0]))
                if isinstance(data, dict) and data.get("remoteIdentity"): remote_ids.add(str(data["remoteIdentity"]))
            if len(remote_ids) != 1: continue
            remote = next(iter(remote_ids))
            direction = "Receive" if net["direction"] == "Send" else "Send"
            for peer in self.db.execute("""SELECT n.capture,n.seq,n.instance FROM investigation_network n JOIN records r USING(capture,seq)
                WHERE n.message_number=? AND n.lane=? AND n.direction=? AND r.run=? AND r.round=? AND r.stage IN ('network.transport','network.receive')""",
                (net["message_number"], net["lane"], direction, s["run"], s["round"])):
                if peer["capture"] == capture or peer["capture"] not in s["captures"] or str(headers.get(peer["capture"], {}).get("localSteamIdentity")) != remote:
                    continue
                peer_remote = set()
                for body in self.db.execute("""SELECT r.body FROM investigation_network n JOIN records r USING(capture,seq)
                    WHERE n.capture=? AND n.instance=? AND r.stage='network.connection'""", (peer["capture"], peer["instance"])):
                    data = _payload(self.db, json.loads(body[0]))
                    if isinstance(data, dict) and data.get("remoteIdentity"): peer_remote.add(str(data["remoteIdentity"]))
                if peer_remote == {str(local)}:
                    candidates[(peer["capture"], peer["seq"])] = "ReciprocalSteamIdentityAndMessageNumberLifetimeUnverified"
        for capture, seq in sorted(seen | set(candidates), key=lambda k: (k[0], len(k[1]), k[1])):
            record = self._record(capture, seq)
            record["outsideSelection"] = not self._selected_record(s, capture, seq)
            if (capture, seq) in seen:
                contextual = (capture, seq) in context_keys and (capture, seq) not in seed_keys
                record["associationStatus"] = "context" if contextual else "confirmed"
                record["associationReason"] = ("ConnectionLifetimeIdentityOnly" if (capture, seq) in connection_context_keys else "CaptureBuildOrTimeOrigin") if contextual else "RecordedEventOrDomainIdentity"
                if (capture, seq) in connection_context_keys:
                    record["contextScope"] = dict(capture=capture, run=record.get("runId"), round=record.get("round"), identityOnly=True,
                        outsideSelectedMatch=record.get("runId") != s["run"] or record.get("round") != s["round"])
            else:
                data = record.get("input")
                business = str(data.get("business") or "") if isinstance(data, dict) else ""
                conflict = combat_chain and "movement" in business.lower()
                record["associationStatus"] = "not-established" if conflict else "candidate"
                record["associationReason"] = "ConflictingTransportBusinessType" if conflict else candidates[(capture, seq)]
            yield record

    def detail(self, selection, capture, sequence):
        s = self._selection(selection)
        rows = list(self.context_records(s, [{"capture": capture, "sequence": str(sequence)}]))
        selected = next(r for r in rows if r["captureId"] == capture and str(r["recordSequence"]) == str(sequence))
        row = self.db.execute("SELECT * FROM investigation_records WHERE capture=? AND seq=?", (capture, str(sequence))).fetchone()
        pointer = self._pointer(capture, sequence)
        dependencies, unknowns = {}, []
        pending = [value for record in rows for key, value in record.get("raw", record).items() if key.endswith("Ref") and isinstance(value, str)]
        pending += [value["$evidenceRef"] for record in rows for value in _walk(record.get("raw", record)) if isinstance(value.get("$evidenceRef"), str)]
        while pending:
            reference = pending.pop(); digest = Path(reference).name.split(".")[0]
            if digest in dependencies:
                continue
            blob = self.db.execute("SELECT body FROM blobs WHERE hash=?", (digest,)).fetchone()
            dependencies[digest] = {"hash": digest, "reference": reference, "present": blob is not None}
            if blob:
                for value in _walk(json.loads(blob[0])):
                    if isinstance(value.get("$evidenceRef"), str):
                        pending.append(value["$evidenceRef"])
            else:
                unknowns.append("MissingBlob:" + reference)
        for record in rows:
            unknowns.extend(p["reason"] for p in record.get("missingPayloads", []))
            if record.get("stage") == "network.batch" and isinstance(record.get("input"), dict):
                if record["input"].get("complete") is False or any(v.get("parseFailure") for v in _walk(record["input"])):
                    unknowns.append("BatchParseIncompleteObjectsMayBeUnknown")
        steps, canonical_events = [], defaultdict(set)
        for record in rows:
            if record.get("associationStatus") in ("confirmed", "context") and record.get("stage") == "gateway.canonical_link" and _id(record.get("serverSequence")):
                canonical_events[str(record["serverSequence"])].add(str(record.get("eventId")))
        for record in rows:
            if record.get("associationStatus") in ("candidate", "not-established"):
                continue
            stage = record.get("stage", "")
            if stage == "network.connection" and record.get("associationReason") == "CaptureBuildOrTimeOrigin":
                continue
            if record is not selected and (_record_category(record, record.get("input")) in ("replay", "performance") or stage == "observation.snapshot"):
                continue
            aggregate = record.get("reason") == "Coalesced" or (
                stage in ("gateway.canonical_link", "network.canonical", "replica.entity", "entity.canonical_health") and
                len(canonical_events[str(record.get("serverSequence"))]) > 1)
            if aggregate:
                unknowns.append("CanonicalCoalescingPreventsPerHitAttribution")
            explained, missing = _explain_record(record, self._pointer(record["captureId"], record["recordSequence"]),
                                                self._content_catalog(record["captureId"], s), aggregate)
            steps.extend(explained); unknowns.extend(missing)
        if selected.get("before") is None and selected.get("after") is None and not any(
                step["kind"] == "change" and step["status"] == "observed" for step in steps):
            unknowns.append("NoRecordedBeforeAfterState")
        network_facts = None
        if selected.get("stage") == "network.transport":
            net = self.db.execute("SELECT * FROM investigation_network WHERE capture=? AND seq=?", (capture, str(sequence))).fetchone()
            if net and net["instance"] != "unknown":
                later = []
                for other in self.db.execute("""SELECT r.* FROM investigation_records r JOIN investigation_network n USING(capture,seq)
                    WHERE r.capture=? AND n.instance=? AND r.run=? AND r.round=? AND r.elapsed>=?
                    AND r.stage IN ('network.transport','network.receive','network.application')
                    ORDER BY r.sort_time,length(r.seq),r.seq LIMIT 21""", (capture, net["instance"], s["run"], s["round"], row["elapsed"] if row["elapsed"] is not None else 1e308)):
                    if other["seq"] != str(sequence): later.append(self._item(other))
                network_facts = {"connectionInstance": net["instance"], "recovery": "not-inferred",
                    "followingConnectionFacts": later[:20], "followingFactsAreNotSameMessage": True,
                    "followingFactsLimit": 20, "fullFactsRemainQueryable": True,
                    "failureWindows": self.failure_windows(s, [{"capture": capture, "sequence": str(sequence)}])}
                windows = network_facts["failureWindows"]
                if windows:
                    network_facts["load"] = self.network_load(dict(s, captures=[capture], after=windows[0]["after"], before=windows[0]["before"], connectionInstance=net["instance"]))
        return {"record": self._item(row), "records": rows, "steps": steps, "networkFacts": network_facts,
                "candidates": [{"capture": r["captureId"], "sequence": str(r["recordSequence"]), "status": r["associationStatus"],
                    "reason": r["associationReason"], "evidence": self._pointer(r["captureId"], r["recordSequence"])}
                    for r in rows if r.get("associationStatus") in ("candidate", "not-established")],
                "summary": "沿已记录关联展开的各端输入、判断和变化；端间排序不表示精确同时或因果延迟。",
                "unknowns": sorted(set(unknowns)), "dependencies": list(dependencies.values()), "catalog": self._content_catalog(capture, s)}

    def network_connections(self, selection):
        s = self._selection(selection)
        where, values = self._where(s, filters=False, time=False)
        rows = self.db.execute("""SELECT DISTINCT n.capture,n.instance,n.direction,n.channel,n.result
            FROM investigation_records r JOIN investigation_network n USING(capture,seq) WHERE """ + where + " ORDER BY n.capture,n.instance,n.direction,n.channel,n.result", values)
        return {"items": [dict(row) for row in rows]}

    def network_load(self, selection):
        s = self._selection(selection)
        traffic, polls, pointers, alive_samples = {}, {}, [], []
        for row, data in self._performance_rows(s):
            if _finite(data.get("alive")) and data["alive"] >= 0:
                alive_samples.append(dict(capture=row["capture"], elapsed=row["elapsed"], alive=data["alive"], evidence=self._pointer(row["capture"], row["seq"])))
            interval = data.get("networkInterval")
            if not isinstance(interval, dict): continue
            pointers.append(self._pointer(row["capture"], row["seq"]))
            for value in interval.get("traffic") or []:
                if s.get("connectionInstance") and str(value.get("connectionInstance")) != s["connectionInstance"]: continue
                key = (row["capture"], *(str(value.get(k, "unknown")) for k in ("connectionInstance", "direction", "channel", "business", "result", "bytesScope")))
                item = traffic.setdefault(key, dict(zip(("capture", "connectionInstance", "direction", "channel", "business", "result", "bytesScope"), key)))
                for field in ("attempts", "accepted", "rejected", "bytes", "acceptedBytes", "rejectedBytes", "reliableFallbacks", "received", "receivedBytes"):
                    if _finite(value.get(field)) and value[field] >= 0: item[field] = item.get(field, 0) + value[field]
            for value in interval.get("receivePolls") or []:
                if s.get("connectionInstance") and str(value.get("connectionInstance")) != s["connectionInstance"]: continue
                key = (row["capture"], str(value.get("connectionInstance")))
                item = polls.setdefault(key, dict(capture=key[0], connectionInstance=key[1]))
                for field in ("calls", "messages", "limitHits", "failedReads", "totalProcessingSeconds"):
                    if _finite(value.get(field)) and value[field] >= 0: item[field] = item.get(field, 0) + value[field]
                for field in ("maximumGapSeconds", "maximumProcessingSeconds"):
                    if _finite(value.get(field)) and value[field] >= 0: item[field] = max(item.get(field, 0), value[field])
        return dict(traffic=sorted(traffic.values(), key=lambda v: v.get("bytes", 0), reverse=True), receivePolls=list(polls.values()),
                    evidence=pointers, intervalAggregatesNotCumulative=True, transportAndBusinessBytesMustNotBeAdded=True,
                    maxObservedAlive=max((v["alive"] for v in alive_samples), default=None), aliveSamples=alive_samples,
                    status="observed" if pointers else "unknown", recovery="not-inferred")

    def failure_windows(self, selection, seeds=None):
        """Per-capture/per-lifetime windows, not cross-PC clock subtraction or a recovery claim."""
        s = self._selection(dict(selection, category="network_rejection"))
        where, values = self._where(s)
        rows = list(self.db.execute("""SELECT r.capture,r.seq,r.elapsed,n.instance FROM investigation_records r
            JOIN investigation_network n USING(capture,seq) WHERE """ + where + " ORDER BY r.sort_time", values))
        if seeds:
            chosen = {(p.get("capture", p.get("captureId")), str(p.get("sequence", p.get("recordSequence")))) for p in seeds}
            connections = {(r["capture"], r["instance"]) for r in rows if (r["capture"], r["seq"]) in chosen}
            rows = [r for r in rows if (r["capture"], r["instance"]) in connections]
        grouped = {}
        for row in rows:
            if row["elapsed"] is not None:
                grouped.setdefault((row["capture"], row["instance"]), []).append(row)
        return [dict(capture=cap, connectionInstance=instance, after=max(0, min(r["elapsed"] for r in values)-10),
                     before=max(r["elapsed"] for r in values)+20, recovery="not-inferred",
                     reason="FirstObservedRejectionMinus10sToLastObservedRejectionPlus20s") for (cap, instance), values in grouped.items()]

    def _content_catalog(self, capture, s):
        row = self.db.execute("SELECT body FROM investigation_catalog WHERE capture=? AND run=? AND round=? ORDER BY length(seq),seq LIMIT 1", (capture, s["run"], s["round"])).fetchone()
        if row is None:
            row = self.db.execute("SELECT body FROM investigation_catalog WHERE capture=? AND run='boot' ORDER BY length(seq),seq LIMIT 1", (capture,)).fetchone()
        return json.loads(row[0]) if row else {"status": "unknown", "reason": "ContentCatalogNotCaptured", "entries": []}

    def _attack_observations(self, s, capture, entity, at, generation):
        domains = []
        for stage, domain in (("owner.attack_gate", "attackGate"), ("owner.attack_stats", "attackAttributes")):
            row = self.db.execute("""SELECT r.seq,r.elapsed FROM investigation_records r INDEXED BY investigation_category
                WHERE r.run=? AND r.round=? AND r.category='attack' AND r.capture=? AND r.stage=? AND r.sort_time<=?
                AND EXISTS(SELECT 1 FROM investigation_members m WHERE m.capture=r.capture AND m.seq=r.seq
                    AND m.entity=? AND m.generation=? AND m.direction='source')
                ORDER BY r.sort_time DESC,length(r.seq) DESC,r.seq DESC LIMIT 1""",
                (s["run"], s["round"], capture, stage, at, entity, generation or "unknown")).fetchone()
            if not row:
                continue
            record = self._record(capture, row["seq"])
            pointer = self._pointer(capture, row["seq"])
            pointer["outsideSelection"] = not self._selected_record(s, capture, row["seq"])
            observed = {"input": record.get("input"), "outcome": record.get("outcome"), "reason": record.get("reason"),
                        "before": record.get("before"), "after": record.get("after")}
            fields = _field_rows(observed, pointer, "observed", self._content_catalog(capture, s))
            for field in fields:
                field["outsideSelection"] = pointer["outsideSelection"]
            domains.append({"domain": domain, "perspective": record.get("role"), "status": "observed", "state": observed,
                "fields": fields, "evidence": pointer, "baseline": None, "continuous": False,
                "dependencies": [{"capture": capture, "sequence": row["seq"]}], "outsideSelection": pointer["outsideSelection"],
                "contextReason": "EarlierAttackObservation" if pointer["outsideSelection"] else None,
                "observedAt": row["elapsed"], "ageSeconds": at - row["elapsed"],
                "unknowns": ["AttackObservationDoesNotDescribeCurrentLoadout"]})
        return domains

    def state(self, selection, capture, entity, at):
        s = self._selection(selection)
        if capture not in s["captures"] or not _finite(at) or at < 0:
            raise ValueError("State requires a selected capture and finite nonnegative elapsed time")
        entity = str(entity)
        params = [capture, s["run"], s["round"], entity, at]
        sql = "SELECT * FROM investigation_states WHERE capture=? AND run=? AND round=? AND entity=? AND elapsed<=?"
        if s.get("generation"):
            sql += " AND generation=?"; params.append(s["generation"])
        rows = list(self.db.execute(sql + " ORDER BY elapsed DESC,length(seq) DESC,seq DESC", params))
        latest_birth = self.db.execute("""SELECT generation FROM investigation_entities WHERE capture=? AND run=? AND round=?
            AND entity=? AND generation<>'unknown' AND first<=? ORDER BY first DESC LIMIT 1""", (capture, s["run"], s["round"], entity, at)).fetchone()
        if not s.get("generation") and latest_birth:
            rows = [row for row in rows if row["generation"] == latest_birth[0]]
        generation = s.get("generation") or (latest_birth[0] if latest_birth else rows[0]["generation"] if rows else "unknown")
        context_dependencies = []
        for birth, reason in ((generation, "SelectedEntityBirth"),
                              (latest_birth[0] if latest_birth else None, "LatestEntityBirthAtRequestedTime")):
            if birth in (None, "unknown"):
                continue
            identity_row = self.db.execute("""SELECT body FROM investigation_entities
                WHERE capture=? AND run=? AND round=? AND entity=? AND generation=? AND first<=?""",
                (capture, s["run"], s["round"], entity, birth, at)).fetchone()
            if identity_row:
                identity_pointer = json.loads(identity_row[0])["firstEvidence"]
                if not any(p["capture"] == identity_pointer["capture"] and p["sequence"] == identity_pointer["sequence"] for p in context_dependencies):
                    context_dependencies.append(dict(identity_pointer, reason=reason))
        destroyed = self.db.execute("""SELECT i.seq FROM records r INDEXED BY by_entity CROSS JOIN investigation_records i
            WHERE r.entity=? AND r.stage='entity.destroy' AND r.capture=? AND r.run=? AND r.round=?
              AND i.capture=r.capture AND i.seq=r.seq AND i.generation=? AND i.sort_time<=? LIMIT 1""",
            (entity, capture, s["run"], s["round"], generation, at)).fetchone()
        if destroyed:
            context_dependencies.append({"capture": capture, "sequence": destroyed[0], "reason": "EntityLifecycleEnded"})
        if not rows:
            attacks = self._attack_observations(s, capture, entity, at, generation)
            return {"capture": capture, "entity": entity, "at": at, "generation": generation, "status": "unknown", "domains": attacks,
                    "contextDependencies": context_dependencies, "missingDomains": _missing_domains(attacks), "unknowns": ["NoStateAtOrBeforeRequestedTime"]}
        domains, chosen = [], set()
        assessed = self.coverage(s)
        local_intervals = [i for i in assessed.get("intervals", []) if i.get("captureId") == capture]
        source_gaps = assessed.get("scopeGaps", []) + [g for i in local_intervals for g in i.get("gaps", [])]
        local_complete = bool(local_intervals) and all(i.get("complete") for i in local_intervals) and not source_gaps
        if self._package and (self._package_errors or (assessed.get("packageScope") or {}).get("status") != "within-package"):
            local_complete = False
        tail_row = self.db.execute("SELECT elapsed FROM investigation_records WHERE capture=? AND run=? AND round=? AND elapsed IS NOT NULL ORDER BY sort_time DESC LIMIT 1", (capture, s["run"], s["round"])).fetchone()
        tail = tail_row[0] if tail_row else None
        end = self.db.execute("SELECT seq FROM investigation_records WHERE capture=? AND run=? AND round=? AND sort_time<=? ORDER BY sort_time DESC,length(seq) DESC,seq DESC LIMIT 1",
                              (capture, s["run"], s["round"], at)).fetchone()
        end_sequence = int(end[0]) if end else 0
        for row in rows:
            key = (row["domain"], row["perspective"])
            if row["generation"] != generation or key in chosen:
                continue
            chosen.add(key); data = json.loads(row["body"])
            chain = []
            baseline = None
            for previous in rows:
                if previous["generation"] == generation and (previous["domain"], previous["perspective"]) == key:
                    chain.append(previous)
                    if json.loads(previous["body"]).get("baseline") is True:
                        baseline = previous
                        break
            first_sequence = int(baseline["seq"] if baseline else row["seq"])
            def overlaps(gap):
                try:
                    return int(gap.get("first", 0)) <= end_sequence and int(gap.get("last", 2**64 - 1)) >= first_sequence
                except (ValueError, TypeError):
                    return True
            blocking_gaps = [g for g in source_gaps if overlaps(g)]
            revisions = [json.loads(c["body"]).get("stateRevision") for c in reversed(chain)]
            revision_closed = bool(revisions) and all(str(v).isdigit() for v in revisions)
            if revision_closed:
                numbers = list(map(int, revisions))
                revision_closed = all(b == a + 1 for a, b in zip(numbers, numbers[1:]))
            unknowns = []
            operations = list(self.db.execute("""SELECT o.seq FROM investigation_operations o WHERE o.capture=? AND o.run=? AND o.round=?
                AND o.entity=? AND o.generation=? AND o.perspective=? AND o.elapsed<=? AND NOT EXISTS(
                SELECT 1 FROM investigation_links l JOIN investigation_records r USING(capture,seq)
                WHERE l.kind='build-operation' AND l.value=o.operation AND r.stage='investigation.state' AND r.elapsed<=?)""",
                (capture, s["run"], s["round"], entity, generation, row["perspective"], at, at))) if row["domain"] == "build" else []
            if operations:
                unknowns.append("OperationInProgressOrCompletionMissing")
            if blocking_gaps or (not local_complete and not source_gaps):
                unknowns.append("EvidenceGapMayHideLaterMutation")
            if tail is None or at > tail:
                unknowns.append("RequestedTimeBeyondCapturedTail")
            if destroyed:
                unknowns.append("LocalEntityLifecycleEnded")
            if latest_birth and generation != latest_birth[0]:
                unknowns.append("LocalEntityInstanceReused")
            if generation == "unknown":
                unknowns.append("UnknownEntityBirth")
            if data.get("continuous") and (baseline is None or not revision_closed):
                unknowns.append("BaselineOrStateRevisionChainMissing")
            reliable = bool(data.get("continuous")) and baseline is not None and revision_closed and not unknowns
            status = "reliable" if reliable else "unknown" if unknowns else "observed"
            if not data.get("continuous"):
                unknowns.append("LatestObservationOnly")
            pointer = self._pointer(capture, row["seq"])
            pointer["outsideSelection"] = not self._selected_record(s, capture, row["seq"])
            baseline_pointer = self._pointer(capture, baseline["seq"]) if baseline else None
            if baseline_pointer:
                baseline_pointer["outsideSelection"] = not self._selected_record(s, capture, baseline["seq"])
                baseline_pointer["contextReason"] = "EarlierBaselineRequiredForState" if baseline_pointer["outsideSelection"] else None
            states = data.get("state") if isinstance(data.get("state"), dict) else {}
            fields = _field_rows(states, pointer, status, self._content_catalog(capture, s))
            for field in fields:
                field["outsideSelection"] = pointer["outsideSelection"]
            domains.append({"domain": row["domain"], "perspective": row["perspective"], "status": status,
                "state": states, "fields": fields, "outsideSelection": pointer["outsideSelection"],
                "contextReason": "EarlierObservationRequiredForState" if pointer["outsideSelection"] else None,
                "stateRevision": data.get("stateRevision"), "buildRevision": data.get("buildRevision"),
                "baseline": baseline_pointer, "evidence": pointer,
                "dependencies": [{"capture": capture, "sequence": item["seq"]} for item in reversed(chain)] +
                                [{"capture": capture, "sequence": item["seq"]} for item in operations],
                "observedAt": row["elapsed"], "ageSeconds": at - row["elapsed"], "unknowns": unknowns})
        domains.extend(self._attack_observations(s, capture, entity, at, generation))
        return {"capture": capture, "entity": entity, "at": at, "generation": generation,
                "status": "unknown" if any(d["status"] == "unknown" for d in domains) else "reliable" if domains and all(d["status"] == "reliable" for d in domains) else "observed",
                "domains": domains, "contextDependencies": context_dependencies,
                "missingDomains": _missing_domains(domains), "unknowns": ["CrossCaptureRevisionsAreNotComparable"]}

    def _track_coverage(self, selection):
        scope_id = selection.get("packageTrackScope")
        if scope_id is None:
            return self.coverage(selection)
        if not self._package:
            raise ValueError("packageTrackScope requires an imported investigation package")
        scopes = [value for value in self._package.get("trackScopes", []) if value.get("id") == scope_id]
        if len(scopes) != 1:
            raise ValueError("Unknown or ambiguous package track scope")
        declared = self._selection(scopes[0].get("selection", scopes[0]))
        if any(selection.get(key) is not None for key in ("entity", "entityCapture", "generation")) or selection.get("category") not in (None, "all"):
            raise ValueError("Package track scopes do not authorize entity or business-category filters")
        keys = ("run", "round", "captures", "after", "before")
        if any(selection.get(key) != declared.get(key) for key in keys):
            raise ValueError("Track request differs from its declared package scope")
        original = self._package.get("originalCoverage") or {}
        intervals = [i for i in original.get("intervals", []) if i.get("captureId") in selection["captures"] and
                     i.get("runId") == selection["run"] and str(i.get("round")) == str(selection["round"])]
        gaps = [g for g in original.get("scopeGaps", []) if not g.get("runId") or g["runId"] == selection["run"]]
        if self._package_errors:
            gaps += [{"reason": "InvestigationPackageVerificationFailed", "details": self._package_errors}]
        return {"complete": bool(intervals) and all(i.get("complete") for i in intervals) and not gaps,
                "intervals": intervals, "scopeGaps": gaps, "scope": "declared-package-track-scope",
                "packageTrackScope": scope_id, "timeFilterDoesNotEraseHistoricalGaps": True,
                "packageScope": {"status": "within-package-track-scope", "selection": {key: declared.get(key) for key in keys},
                                 "verificationErrors": self._package_errors, "sourceHistoryPreserved": True}}

    def tracks(self, selection):
        s = self._selection(selection); where, values = self._where(s, "", filters=False)
        grouped = defaultdict(list)
        for row in self.db.execute("SELECT * FROM investigation_samples WHERE " + where + " ORDER BY elapsed,capture,length(seq),seq", values):
            grouped[row["track"]].append({"capture": row["capture"], "sequence": row["seq"], "elapsed": row["elapsed"],
                "status": row["validity"], "values": json.loads(row["body"]), "evidence": self._pointer(row["capture"], row["seq"])})
        for row, data in self._writer_snapshot_rows(s):
            grouped["writer"].append({"capture": row["capture"], "sequence": row["seq"], "elapsed": row["elapsed"],
                "sourceStage": "observation.snapshot", "status": "observed", "values": _measurements(data),
                "evidence": self._pointer(row["capture"], row["seq"]),
                "clock": {**CLOCK, "observationKind": "point", "timestampSource": "record.monotonicTime",
                          "zeroOnlyDescribesThisObservation": True, "countersAreCumulativeNotIntervalDeltas": True}})
        grouped["writer"].sort(key=lambda value: (value["elapsed"] if value["elapsed"] is not None else 1e308,
                                                value["capture"], len(value["sequence"]), value["sequence"]))
        assessed = self._track_coverage(s)
        gaps = assessed["scopeGaps"] + [g for i in assessed["intervals"] for g in i.get("gaps", [])]
        performance = {(row["capture"], row["seq"]): data for row, data in self._performance_rows(s)}
        no_remote = bool(performance) and all(str(data.get("role", "")).lower() in ("host", "server", "offline") and
            data.get("connections") == [] and _finite(data.get("playerCount")) and 0 <= data["playerCount"] <= 1
            for data in performance.values())
        # A recorded connection or transport event disproves a blanket no-remote assertion.
        no_remote = no_remote and not any(sample["values"].get("stage") in ("network.connection", "network.transport")
                                          for sample in grouped["steam"])
        tracks = []
        for name in ("business", "steam", "frames", "writer"):
            samples = grouped[name]
            positive, valid, negative, invalid = [], 0, 0, 0
            facts = {}
            for sample in samples:
                data = sample["values"]; measures = []
                sample_invalid = False
                sample_complete = False
                if name == "frames":
                    measures = [v for v in [data.get("frameMaxMs")] if _finite(v) and v >= 0]
                    sample_complete = bool(measures)
                    hit = any(value > 100 for value in measures)
                elif name == "writer":
                    # logQueuedBytes belongs to the separate observation-log writer. Its
                    # empty queue cannot establish that the combat evidence writer is empty.
                    measure_fields = ("evidenceQueuedBytes", "pendingBytes", "logQueuedBytes") if performance.get((sample["capture"], sample["sequence"]), {}).get("lightweightNetwork") else ("evidenceQueuedBytes", "pendingBytes")
                    measures = [data[k] for k in measure_fields
                                if _finite(data.get(k)) and data[k] >= 0]
                    sample_complete = bool(measures)
                    hit = any(value > 0 for value in measures)
                    for key in ("dropped", "peakQueueBytes", "peakRetainedBytes", "sinkFailures", "writerMs"):
                        value = data.get(key)
                        previous = facts.get((sample["capture"], key))
                        if _finite(value) and value >= 0 and (previous is None or value >= previous["value"]):
                            facts[(sample["capture"], key)] = {"capture": sample["capture"], "name": key, "value": value,
                                "status": "observed", "semantics": "CumulativeAtObservationNotSelectedIntervalDelta", "evidence": sample["evidence"]}
                    if data.get("writerFailure"):
                        facts[(sample["capture"], "writerFailure")] = {"capture": sample["capture"], "name": "writerFailure",
                            "value": data["writerFailure"], "status": "observed", "evidence": sample["evidence"]}
                elif name == "business":
                    calibration = performance.get((sample["capture"], sample["sequence"])) or {}
                    age_valid = calibration.get("pendingDeathAgeClock") == "Unity.UnscaledTime" and calibration.get("pendingDeathAgeClockVersion") == 1
                    for age in ("oldestPendingDeathSeconds", "lastDeathConfirmationSeconds", "maximumDeathConfirmationSeconds"):
                        if age in data:
                            data.setdefault("raw", {}).setdefault(age, data[age])
                            if not age_valid:
                                data[age] = None
                                data.setdefault("fieldValidity", {})[age] = "invalid"
                                data.setdefault("invalidReasons", {})[age] = "MixedClockAgeUnavailable"
                    measures = [data[k] for k in ("pendingDeaths", "oldestPendingDeathSeconds")
                                if _finite(data.get(k)) and data[k] >= 0]
                    sample_complete = bool(measures)
                    hit = any(value > 0 for value in measures) or data.get("stage") in (
                        "investigation.pickup.wait", "investigation.pickup.collector_wait")
                else:
                    connections = data.get("connections")
                    complete_connections = []
                    for connection in connections or []:
                        raw = connection.get("raw", connection)
                        queue = raw.get("queueMilliseconds")
                        queue_valid = valid_metric(raw, "queueMilliseconds") is not None
                        pending_fields = ("pendingReliableBytes", "pendingUnreliableBytes", "unacknowledgedBytes")
                        pending_valid = all(valid_metric(raw, k) is not None for k in pending_fields)
                        normalized = _steam_connection(raw)
                        if not queue_valid:
                            normalized["queueMilliseconds"] = None
                        if not pending_valid:
                            for key in pending_fields:
                                normalized[key] = None
                        connection.clear(); connection.update(normalized)
                        if queue_valid:
                            measures.append(queue)
                        if pending_valid:
                            measures.extend(raw[k] for k in pending_fields)
                        complete_connections.append(queue_valid and pending_valid)
                        sample_invalid |= raw.get("readSucceeded") is False or raw.get("queueValidity") in (
                            "ReadFailed", "Negative", "AboveOneHourUnverified") or (_finite(queue) and (queue < 0 or queue > 3600000))
                    sample_complete = bool(complete_connections) and all(complete_connections)
                    hit = any(value > 0 for value in measures)
                    if data.get("stage") == "network.transport" and rejected({"stage": "network.transport", "input": data.get("input"), "outcome": data.get("outcome")}):
                        measures.append(1); hit = True
                        sample["sendRejected"] = True
                        sample["acceptedIsNotDelivered"] = True
                valid += bool(measures)
                invalid += sample_invalid
                negative += sample_complete and not hit
                if hit:
                    positive.append(sample["evidence"])
                sample["observationStatus"] = "observed" if measures else "invalid" if sample_invalid else "unknown"
                sample["issueStatus"] = "observed" if hit else "not-observed" if sample_complete else "invalid" if sample_invalid else "unknown"
                sample["status"] = sample["issueStatus"]
                calibration = performance.get((sample["capture"], sample["sequence"]))
                if calibration:
                    clock_fields = {key: calibration[key] for key in ("clockDomain", "clockReadStartedTicks", "clockReadEndedTicks", "stopwatchFrequency",
                        "clockRealtimeSeconds", "clockUnscaledSeconds", "clockNetworkSeconds", "time", "networkTime", "windowSeconds") if key in calibration}
                    sample["clock"] = {**CLOCK, "sampleClock": clock_fields,
                        "calibrationStatus": "observed" if "clockReadStartedTicks" in clock_fields and "clockReadEndedTicks" in clock_fields else "unknown",
                        "samplingWindowIsNotExactEventTime": True}
            status = "observed" if positive else "not-applicable" if name == "steam" and no_remote and assessed.get("complete") else (
                "not-observed" if valid and valid == negative and not invalid and assessed.get("complete") else "invalid" if invalid else "unknown")
            reasons = {
                "business": {"observed": "已记录待确认业务或领取等待；等待不等同 Steam 发送拥堵。", "not-observed": "完整记录中的有效等待样本未出现待确认积压；采样间事件仍以原始记录为准。"},
                "steam": {"observed": "已记录 Steam 发送拒绝，或有效指标中的待发送字节／发送等待；展开查看具体依据。接受不等于送达，后续成功不代表恢复。无效字段不参与积压判定。",
                          "not-observed": "完整记录中的有效 Steam 指标未显示发送积压。", "not-applicable": "所选样本均无远端连接，且为本地 Host／离线会话；这些样本不适用 Steam 发送积压判断。"},
                "frames": {"observed": "已记录超过 100 毫秒的本地帧；不能仅凭时间重叠归因于网络或写盘。", "not-observed": "完整记录中的有效帧样本未超过 100 毫秒。"},
                "writer": {"observed": "已记录本地日志待处理字节；队列非零本身不能证明持续写入瓶颈。", "not-observed": "完整记录中的有效日志队列样本均为零。"}}
            reason = reasons[name].get(status, "指标读取失败或值未经验证，不能作为有效积压测量。" if status == "invalid" else
                "缺少有效指标或采样覆盖不完整，无法判断该轨道是否发生问题；业务事件或连接事件本身不构成积压测量。")
            tracks.append({"id": name, "status": status, "issueStatus": status,
                "observationStatus": "observed" if valid else "invalid" if invalid else "unknown", "reason": reason,
                "metrics": {"validSamples": valid, "zeroOrBelowThresholdSamples": negative, "invalidSamples": invalid,
                            "positiveSamples": len(positive), "positiveEvidence": positive, "recordedFacts": list(facts.values())},
                "clock": CLOCK, "gapCount": len(gaps), "coverageRef": "#/coverage", "samples": samples})
        return {"clock": CLOCK, "tracks": tracks, "coverage": assessed}
