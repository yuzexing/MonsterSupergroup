#!/usr/bin/env python3
"""Freeze and audit an independent Unity validation project; never launch Unity."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import time
import zipfile

PROJECT_ROOTS = ("Assets", "Packages", "ProjectSettings")
PROJECT_FILES = ("docs/editor-tools/catalog.json", "steam_appid.txt")
TOOL_FILES = ("Tools/CombatEvidenceValidation.py", "Tools/Invoke-CombatEvidenceValidation.ps1",
              "Tools/Invoke-CombatEvidenceBenchmark.ps1", "Tools/CompareCombatEvidence.py",
              "Tools/CombatEvidence.py")
TOOL_TEST_PATTERN = "test_*combat_evidence*.py"
TOOL_FIXTURES = "Tools/fixtures/combat-evidence-v2"
GENERATED_PATHS = ("Assets/AddressableAssetsData/link.xml", "Assets/AddressableAssetsData/link.xml.meta")
BUILD_GENERATED_PATHS = GENERATED_PATHS + ("Assets/AddressableAssetsData/Windows/addressables_content_state.bin",)


def acceptance_policy(name="full-v1", mode=None):
    if name not in ("full-v1", "editor-generated-v1", "build-generated-v1"):
        raise ValueError(f"Unknown acceptance input policy: {name}")
    if name == "editor-generated-v1" and mode not in ("Tests", "Replay"):
        raise ValueError("The generated-input exception is restricted to editor Tests/Replay.")
    if name == "build-generated-v1":
        if mode != "Build": raise ValueError("The build-generated input exception requires Build mode.")
        return {"id": name, "excludedPaths": list(BUILD_GENERATED_PATHS)}
    return {"id": name, "excludedPaths": list(GENERATED_PATHS) if name == "editor-generated-v1" else []}


def digest(path):
    value = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""): value.update(block)
    return value.hexdigest()


def save(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2), encoding="utf-8")


def is_link(path):
    # pathlib.is_symlink alone misses Windows junctions on older Python versions.
    return path.is_symlink() or bool(getattr(path.lstat(), "st_file_attributes", 0) & 0x400)


def ordinary_tree(root):
    if not root.is_dir() or is_link(root): raise ValueError(f"Expected physical directory: {root}")
    for directory, folders, files in os.walk(root, followlinks=False):
        for name in folders + files:
            path = Path(directory) / name
            if is_link(path): raise ValueError(f"Reparse points are not independent snapshots: {path}")
        for name in sorted(files): yield Path(directory) / name


def tool_paths(root):
    paths = {root / name for name in TOOL_FILES if (root / name).is_file()}
    paths.update((root / "Tools").glob(TOOL_TEST_PATTERN))
    fixtures = root / TOOL_FIXTURES
    if fixtures.exists(): paths.update(ordinary_tree(fixtures))
    for path in sorted(paths):
        ensure_physical_parents(path)
        if not path.is_file(): raise ValueError(f"Expected tool input file: {path}")
        yield path


def manifest(rows):
    rows = sorted(rows, key=lambda row: row["path"])
    return {"sha256": hashlib.sha256(json.dumps(rows, sort_keys=True, separators=(",", ":")).encode()).hexdigest(), "files": rows}


def stable_inventory(full, policy):
    return manifest(row for row in full["files"] if row["path"] not in policy["excludedPaths"])


def archive_generated(output, phase, roots, paths=GENERATED_PATHS):
    """Keep generated content as evidence even when excluded from the input gate."""
    result = {}
    for label, (root, expected) in roots.items():
        rows = []
        expected_files = {row["path"]: row for row in expected["files"]} if expected is not None else None
        for relative in paths:
            path = root / relative
            ensure_physical_parents(path)
            exists = path.exists()
            row = {"path": relative, "exists": exists, "bytes": None, "sha256": None, "contentPath": None}
            if exists:
                if not path.is_file(): raise ValueError(f"Generated input is not an ordinary file: {path}")
                row.update(bytes=path.stat().st_size, sha256=digest(path))
                content = output / "generated-inputs" / phase / label / relative
                content.parent.mkdir(parents=True, exist_ok=True)
                shutil.copy2(path, content)
                if content.stat().st_size != row["bytes"] or digest(content) != row["sha256"]:
                    raise ValueError(f"Generated input changed while archiving: {path}")
                row["contentPath"] = content.relative_to(output).as_posix()
            if expected_files is not None:
                expected_row = expected_files.get(relative)
                actual = {key: row[key] for key in ("path", "bytes", "sha256")} if exists else None
                if expected_row != actual: raise ValueError(f"Generated input differs from input manifest: {path}")
            rows.append(row)
        result[label] = rows
    save(output / f"generated-inputs-{phase}.json", result)
    return result


def tool_inventory(root):
    return manifest({"path": path.relative_to(root).as_posix(), "bytes": path.stat().st_size, "sha256": digest(path)}
                    for path in tool_paths(root))


def inventory(root, include_tools=False):
    rows = []
    for name in PROJECT_ROOTS:
        for path in ordinary_tree(root / name):
            rows.append({"path": path.relative_to(root).as_posix(), "bytes": path.stat().st_size, "sha256": digest(path)})
    for name in PROJECT_FILES:
        path = root / name
        if path.is_file(): rows.append({"path": name, "bytes": path.stat().st_size, "sha256": digest(path)})
    if include_tools:
        rows.extend(tool_inventory(root)["files"])
    return manifest(rows)


def git(root, *args):
    result = subprocess.run(["git", "-C", str(root), *args], stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    if result.returncode: raise ValueError(result.stderr.decode("utf-8", "replace"))
    return result.stdout


def beneath(path, root):
    try: path.resolve().relative_to(root.resolve())
    except ValueError: raise ValueError(f"Path outside expected validation directory: {path}")


def ensure_physical_parents(path):
    for parent in (path, *path.parents):
        if parent.exists() and is_link(parent): raise ValueError(f"Snapshot path traverses a link: {parent}")


def prepare(source, project, output, unity, overlay=None, input_policy="full-v1", mode=None):
    policy = acceptance_policy(input_policy, mode)
    generated_paths = BUILD_GENERATED_PATHS if input_policy == "build-generated-v1" else GENERATED_PATHS
    ensure_physical_parents(Path(project).absolute())
    source, project, output, unity = (Path(p).resolve() for p in (source, project, output, unity))
    if source == project or source.is_relative_to(project) or any(project.is_relative_to(source / name) for name in PROJECT_ROOTS):
        raise ValueError("The validation project must be a separate directory.")
    if output.is_relative_to(project) or project.is_relative_to(output) or any(output.is_relative_to(source / name) for name in PROJECT_ROOTS):
        raise ValueError("Evidence output cannot be inside frozen project inputs.")
    # Do not resolve a source-pointing junction away before checking its literal path.
    ensure_physical_parents(project)
    for name in PROJECT_ROOTS:
        target = project / name
        if target.exists(): list(ordinary_tree(target))
    output.mkdir(parents=True, exist_ok=True)
    if (output / "source-before.json").exists(): raise ValueError("Use a fresh evidence output directory.")
    before = inventory(source, include_tools=True)
    save(output / "source-before.json", before)
    save(output / "acceptance-policy.json", policy)
    save(output / "source-stable-before.json", stable_inventory(before, policy))
    tools_before = manifest(row for row in before["files"] if row["path"].startswith("Tools/"))
    save(output / "tool-before.json", tools_before)
    for row in tools_before["files"]:
        name = row["path"]
        path = source / name
        if digest(path) != row["sha256"]: raise ValueError(f"Tool input changed before copying: {name}")
        saved = output / "tool-sources" / name
        saved.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(path, saved)
    if tool_inventory(output / "tool-sources") != tools_before:
        raise ValueError("Archived tool inputs differ from the frozen source manifest.")
    (output / "git-status.txt").write_bytes(git(source, "status", "--porcelain=v1", "--untracked-files=all"))
    (output / "dirty.patch").write_bytes(git(source, "diff", "--binary", "HEAD", "--", *PROJECT_ROOTS, *PROJECT_FILES,
                                              *TOOL_FILES, "Tools/" + TOOL_TEST_PATTERN, TOOL_FIXTURES))
    save(output / "identity.json", {"source": str(source), "project": str(project), "output": str(output),
         "unity": str(unity), "unitySha256": digest(unity), "projectVersion": (source / "ProjectSettings/ProjectVersion.txt").read_text(),
         "head": git(source, "rev-parse", "HEAD").decode().strip(), "startedUtc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
         "sourceSnapshotSha256": before["sha256"], "snapshotKind": "PhysicalCopiesNoJunctionsOrHardlinks",
         "acceptancePolicy": policy, "acceptancePolicySha256": digest(output / "acceptance-policy.json"), "mode": mode,
         "dirtyPatchIncludesUntracked": False, "untrackedInputs": "Included in source-before manifest and physical validation snapshot"})
    desired = {row["path"]: row for row in before["files"] if row["path"].split("/", 1)[0] in PROJECT_ROOTS or row["path"] in PROJECT_FILES}
    copied = unchanged = 0
    project.mkdir(parents=True, exist_ok=True)
    for name in PROJECT_ROOTS: (project / name).mkdir(exist_ok=True)
    for relative, row in desired.items():
        target, original = project / relative, source / relative
        ensure_physical_parents(target.parent)
        if target.exists() and target.is_file() and target.stat().st_nlink > 1:
            raise ValueError(f"Hardlinked target would mutate shared input: {target}")
        if target.is_file() and target.stat().st_size == row["bytes"] and digest(target) == row["sha256"]:
            unchanged += 1
            continue
        if digest(original) != row["sha256"]: raise ValueError(f"Source changed while preparing snapshot: {relative}")
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(original, target)
        copied += 1
    moved = []
    for name in PROJECT_FILES:
        path = project / name
        if name not in desired and path.is_file():
            beneath(path, project)
            destination = output / "stale-validation-inputs" / name
            destination.parent.mkdir(parents=True, exist_ok=True)
            shutil.move(str(path), str(destination))
            moved.append(name)
    for name in PROJECT_ROOTS:
        for path in list(ordinary_tree(project / name)):
            relative = path.relative_to(project).as_posix()
            if relative in desired: continue
            destination = output / "stale-validation-inputs" / relative
            beneath(path, project)
            beneath(destination, output)
            destination.parent.mkdir(parents=True, exist_ok=True)
            shutil.move(str(path), str(destination))
            moved.append(relative)
        # Empty stale folders would cause Unity to generate new .meta files.
        # Remove only empty folders proven to be inside this independent snapshot.
        for directory, _, _ in os.walk(project / name, topdown=False):
            path = Path(directory)
            relative = path.relative_to(project)
            if path != project / name and not (source / relative).is_dir() and not any(path.iterdir()):
                beneath(path, project)
                path.rmdir()
    overrides = []
    if overlay:
        for override in ordinary_tree(Path(overlay).resolve()):
            matches = [relative for relative in desired if Path(relative).name == override.name]
            if len(matches) != 1 or override.suffix != ".cs":
                raise ValueError(f"Source override must name exactly one existing C# input: {override}")
            relative = matches[0]
            shutil.copy2(override, project / relative)
            archived = output / "source-overrides" / relative
            archived.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(override, archived)
            overrides.append({"path": relative, "sourceSha256": desired[relative]["sha256"], "overrideSha256": digest(override)})
            desired[relative] = {"path": relative, "bytes": override.stat().st_size, "sha256": digest(override)}
    actual = inventory(project)
    expected_files = sorted(desired.values(), key=lambda row: row["path"])
    if actual["files"] != expected_files: raise ValueError("Validation snapshot differs from source manifest.")
    save(output / "validation-before.json", actual)
    save(output / "validation-stable-before.json", stable_inventory(actual, policy))
    archive_generated(output, "before", {"source": (source, before), "project": (project, actual)}, generated_paths)
    # Keep the tested implementation even when the reusable project is synchronized
    # for a later run. This includes untracked C# and the explicit red-test overlay.
    source_extensions = {".cs", ".asmdef", ".asmref", ".json", ".asset", ".unity", ".prefab", ".meta", ".shader", ".compute", ".hlsl", ".cginc", ".uxml", ".uss"}
    archive_path = output / "tested-code-and-config.zip"
    with zipfile.ZipFile(archive_path, "w", zipfile.ZIP_DEFLATED, compresslevel=1) as archive:
        for row in actual["files"]:
            relative = row["path"]
            if not relative.startswith("Assets/") or Path(relative).suffix.lower() in source_extensions or relative in generated_paths:
                archive.write(project / relative, relative)
    save(output / "sync.json", {"copied": copied, "unchanged": unchanged, "archivedStaleFiles": moved,
         "projectSha256": actual["sha256"], "sourceOverrides": overrides, "complete": True,
         "testedSourceArchive": archive_path.name, "testedSourceArchiveSha256": digest(archive_path),
         "archiveScope": "Code, serialized assets, scene/prefab text, metadata, Packages and ProjectSettings; large binary media remain identified by full snapshot manifest"})
    return {"project": str(project), "sourceHash": before["sha256"], "projectHash": actual["sha256"], "copied": copied,
            "sourceStableHash": stable_inventory(before, policy)["sha256"],
            "projectStableHash": stable_inventory(actual, policy)["sha256"], "acceptancePolicy": policy}


def finish(output, recheck_output=None):
    output = Path(output).resolve()
    identity = json.loads((output / "identity.json").read_text(encoding="utf-8"))
    legacy = "acceptancePolicy" not in identity
    audit_output = output
    if legacy:
        audit_output = Path(recheck_output).resolve() if recheck_output else output.with_name(output.name + f"-recheck-{time.time_ns()}")
        if audit_output == output or audit_output.is_relative_to(output):
            raise ValueError("Historical archive rechecks must use a separate new output directory.")
        for root in (Path(identity["source"]), Path(identity["project"])):
            if any(audit_output.is_relative_to(root / name) for name in PROJECT_ROOTS):
                raise ValueError("Historical rechecks cannot be written into project inputs.")
        audit_output.mkdir(parents=True, exist_ok=False)
    elif recheck_output:
        raise ValueError("An explicit recheck output is only supported for a legacy policyless archive.")
    audit_errors = []
    def audit(name, read):
        try: return read()
        except (OSError, ValueError) as error:
            audit_errors.append({"input": name, "error": f"{type(error).__name__}: {error}"})
            return None
    source = audit("source", lambda: inventory(Path(identity["source"]), include_tools=True))
    validation = audit("project", lambda: inventory(Path(identity["project"])))
    tools_after = audit("tools", lambda: tool_inventory(output / "tool-sources"))
    tool_before_path = output / "tool-before.json"
    tools_before = json.loads(tool_before_path.read_text(encoding="utf-8")) if tool_before_path.is_file() else None
    # A new audit must not rewrite the historical verdict of a legacy archive.
    suffix = "-recheck" if tools_before is None and not legacy else ""
    save(audit_output / f"source-after{suffix}.json", source)
    save(audit_output / f"validation-after{suffix}.json", validation)
    save(audit_output / f"tool-after{suffix}.json", tools_after)
    before = json.loads((output / "source-before.json").read_text(encoding="utf-8"))
    frozen = json.loads((output / "validation-before.json").read_text(encoding="utf-8"))
    policy = None
    source_stable_before = validation_stable_before = source_stable_after = validation_stable_after = None
    if not legacy:
        def read_policy():
            path = output / "acceptance-policy.json"
            saved = json.loads(path.read_text(encoding="utf-8"))
            if saved != identity["acceptancePolicy"] or digest(path) != identity.get("acceptancePolicySha256"):
                raise ValueError("Acceptance input policy changed after preparation.")
            if saved != acceptance_policy(saved.get("id"), identity.get("mode")):
                raise ValueError("Acceptance input policy does not match its versioned path list.")
            for name in ("invocation.json", "execution.json"):
                invocation = output / name
                if invocation.exists():
                    mode = json.loads(invocation.read_text(encoding="utf-8-sig")).get("mode")
                    if mode is not None and mode != identity.get("mode"):
                        raise ValueError(f"{name} mode differs from the prepared acceptance policy.")
            return saved
        policy = audit("acceptancePolicy", read_policy)
        if policy is not None:
            def read_stable(name, full):
                value = json.loads((output / name).read_text(encoding="utf-8"))
                if value != stable_inventory(full, policy): raise ValueError(f"Stable input manifest differs from full snapshot: {name}")
                return value
            source_stable_before = audit("sourceStableBefore", lambda: read_stable("source-stable-before.json", before))
            validation_stable_before = audit("validationStableBefore", lambda: read_stable("validation-stable-before.json", frozen))
            source_stable_after = stable_inventory(source, policy) if source is not None else None
            validation_stable_after = stable_inventory(validation, policy) if validation is not None else None
    save(audit_output / f"source-stable-after{suffix}.json", source_stable_after)
    save(audit_output / f"validation-stable-after{suffix}.json", validation_stable_after)
    generated_paths = BUILD_GENERATED_PATHS if policy and policy["id"] == "build-generated-v1" else GENERATED_PATHS
    if not legacy:
        # Verify that the preparation archive still proves the generated inputs it saw.
        def check_generated_before():
            saved = json.loads((output / "generated-inputs-before.json").read_text(encoding="utf-8"))
            for label, full in (("source", before), ("project", frozen)):
                expected = {r["path"]: r for r in full["files"]}
                if [r["path"] for r in saved[label]] != list(generated_paths):
                    raise ValueError("Generated pre-run manifest has an unexpected path list.")
                for row in saved[label]:
                    present = expected.get(row["path"])
                    if bool(present) != row["exists"]: raise ValueError("Generated pre-run existence changed.")
                    if present:
                        content = output / row["contentPath"]
                        beneath(content, output / "generated-inputs/before" / label)
                        ensure_physical_parents(content)
                        if (row["bytes"], row["sha256"]) != (present["bytes"], present["sha256"]) or content.stat().st_size != row["bytes"] or digest(content) != row["sha256"]:
                            raise ValueError("Archived generated pre-run content changed.")
                    elif any(row[key] is not None for key in ("bytes", "sha256", "contentPath")):
                        raise ValueError("Absent generated input has unexpected content metadata.")
            return saved
        audit("generatedBefore", check_generated_before)
    audit("generatedAfter", lambda: archive_generated(audit_output, "after", {
        "source": (Path(identity["source"]), source), "project": (Path(identity["project"]), validation)}, generated_paths))
    def changed(first, last):
        if first is None or last is None: return []
        a = {row["path"]: row["sha256"] for row in first["files"]}
        b = {row["path"]: row["sha256"] for row in last["files"]}
        return [{"path": path, "before": a.get(path), "after": b.get(path)}
                for path in sorted(a.keys() | b.keys()) if a.get(path) != b.get(path)]
    result = {"sourceUnchanged": source == before if source is not None else None,
              "validationInputsUnchanged": validation == frozen if validation is not None else None,
              "sourceChanges": changed(before, source), "validationInputChanges": changed(frozen, validation),
              "toolInputsUnchanged": tools_before == tools_after if tools_before is not None and tools_after is not None else None,
              "toolInputChanges": changed(tools_before, tools_after) if tools_before is not None else [],
              "toolInputFailure": "Missing pre-run tool manifest; historical tool stability is unknown." if tools_before is None else None,
              "acceptancePolicy": policy,
              "sourceStableInputsUnchanged": source_stable_before == source_stable_after if source_stable_before is not None and source_stable_after is not None else None,
              "validationStableInputsUnchanged": validation_stable_before == validation_stable_after if validation_stable_before is not None and validation_stable_after is not None else None,
              "sourceStableInputChanges": changed(source_stable_before, source_stable_after),
              "validationStableInputChanges": changed(validation_stable_before, validation_stable_after),
              "policyFailure": "Missing prepared acceptance policy; historical stable input identity is unknown." if legacy else None,
              "auditOutput": str(audit_output), "recheckedArchive": str(output) if legacy else None,
              "auditErrors": audit_errors,
              "completedUtc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())}
    result["inputAuditPassed"] = result["validationInputsUnchanged"] is True and result["toolInputsUnchanged"] is True and not audit_errors
    result["acceptanceInputAuditPassed"] = (policy is not None and result["validationStableInputsUnchanged"] is True and
                                            source_stable_before is not None and result["toolInputsUnchanged"] is True and not audit_errors)
    save(audit_output / f"integrity{suffix}.json", result)
    execution_path = output / "execution.json"
    if execution_path.is_file() and tools_before is not None and not legacy:
        execution = json.loads(execution_path.read_text(encoding="utf-8-sig"))
        stable = result["acceptanceInputAuditPassed"]
        execution["inputAuditPassed"] = result["inputAuditPassed"]
        execution["acceptanceInputAuditPassed"] = stable
        execution["acceptancePolicy"] = policy
        execution["success"] = execution.get("success") is True and stable
        if not stable:
            error = "Frozen project/tool inputs or their prepared acceptance policy changed or could not be audited; this run cannot be accepted."
            if error not in (execution.get("error") or ""):
                execution["error"] = " ".join(filter(None, [execution.get("error"), error]))
        save(execution_path, execution)
    files = [{"path": p.relative_to(audit_output).as_posix(), "bytes": p.stat().st_size, "sha256": digest(p)}
             for p in sorted(audit_output.rglob("*")) if p.is_file() and not p.name.startswith("artifact-manifest")]
    save(audit_output / f"artifact-manifest{suffix}.json", {"identity": identity, "integrity": result, "files": files})
    return result


def audit_fixtures(provenance, output, build_manifest=None):
    provenance = Path(provenance).resolve()
    entries = json.loads(provenance.read_text(encoding="utf-8-sig"))
    build = json.loads(Path(build_manifest).read_text(encoding="utf-8-sig")) if build_manifest else None
    rows = []
    for entry in entries:
        path = Path(entry["file"])
        fixture = json.loads(path.read_text(encoding="utf-8-sig"))
        pointers = []
        for reference in entry.get("originalReferences", []):
            raw = Path(reference["path"])
            with raw.open(encoding="utf-8-sig") as stream:
                original = next((line for number, line in enumerate(stream, 1) if number == reference["line"]), None)
            record = json.loads(original) if original is not None else {}
            pointers.append(dict(reference, fileSha256=digest(raw), physicalLineExists=original is not None,
                                 captureMatches=record.get("captureId") == entry.get("capture"),
                                 recordSequence=record.get("recordSequence")))
        actual_hash = digest(path)
        verified = (actual_hash == entry.get("sha256") and len(pointers) >= 2 and
                    all(p["physicalLineExists"] and p["captureMatches"] for p in pointers) and
                    str(pointers[0]["recordSequence"]) == str(entry.get("firstCheckpoint")) and
                    str(pointers[-1]["recordSequence"]) == str(entry.get("lastSequence")) and
                    fixture.get("source") == entry.get("capture") and fixture.get("engine") == entry.get("engine") and
                    len(fixture.get("steps", [])) == entry.get("steps"))
        if build: verified = verified and build.get("buildGuid") == entry.get("sourceBuild")
        rows.append({"file": str(path), "sha256": actual_hash, "expectedSha256": entry.get("sha256"),
                     "provenanceVerified": verified, "capture": entry.get("capture"), "engine": entry.get("engine"),
                     "sourceBuild": entry.get("sourceBuild"), "sourceRun": entry.get("sourceRun"),
                     "sourceLogFormat": entry.get("sourceLogFormat"), "fixtureFormat": fixture.get("version"),
                     "firstCheckpoint": entry.get("firstCheckpoint"), "lastSequence": entry.get("lastSequence"),
                     "firstKnownCaptureGap": entry.get("firstKnownCaptureGap"), "steps": len(fixture.get("steps", [])),
                     "declaredComplete": fixture.get("complete"), "originalReferences": pointers,
                     "originalContext": entry.get("context"),
                     "verdict": "VerifiedInputProvenanceOnly" if verified else "ProvenanceMismatch",
                     "businessFaultReproduced": False, "businessFaultFixed": False,
                     "limitation": "This audit executes no business replay. A complete pre-gap KCP input is not a reproduced Steam incident or business defect."})
    result = {"schemaVersion": 1, "auditedUtc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
              "provenance": str(provenance), "provenanceSha256": digest(provenance),
              "buildManifest": str(Path(build_manifest).resolve()) if build_manifest else None,
              "buildManifestSha256": digest(Path(build_manifest)) if build_manifest else None,
              "fixtures": rows, "allProvenanceVerified": bool(rows) and all(r["provenanceVerified"] for r in rows)}
    save(Path(output), result)
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    subs = parser.add_subparsers(dest="command", required=True)
    prep = subs.add_parser("prepare")
    for name in ("source", "project", "output", "unity"): prep.add_argument("--" + name, required=True)
    prep.add_argument("--overlay", help="Explicit old C# files for red tests; basename must identify one source input")
    prep.add_argument("--input-policy", default="full-v1", choices=("full-v1", "editor-generated-v1", "build-generated-v1"))
    prep.add_argument("--mode", choices=("Tests", "Replay", "Build"))
    final = subs.add_parser("finish"); final.add_argument("--output", required=True)
    final.add_argument("--recheck-output", help="New independent output directory for a legacy policyless archive")
    audit = subs.add_parser("audit-fixtures")
    audit.add_argument("--provenance", required=True); audit.add_argument("--output", required=True)
    audit.add_argument("--build-manifest")
    args = parser.parse_args()
    if args.command == "prepare":
        # Validate the unresolved destination so an existing junction is never followed.
        ensure_physical_parents(Path(args.project).absolute())
        result = prepare(args.source, args.project, args.output, args.unity, args.overlay, args.input_policy, args.mode)
    elif args.command == "finish": result = finish(args.output, args.recheck_output)
    else: result = audit_fixtures(args.provenance, args.output, args.build_manifest)
    print(json.dumps(result, ensure_ascii=False))
    # A frozen independent snapshot remains valid while colleagues edit the workspace.
    # sourceUnchanged explicitly states whether the result still describes that workspace.
    if args.command == "finish" and not result["acceptanceInputAuditPassed"]: return 2
    if args.command == "audit-fixtures" and not result["allProvenanceVerified"]: return 2
    return 0


if __name__ == "__main__": raise SystemExit(main())
