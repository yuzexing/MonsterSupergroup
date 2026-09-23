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
              "Tools/CombatEvidence.py", "Tools/test_compare_combat_evidence.py", "Tools/test_combat_evidence.py",
              "Tools/test_combat_evidence_validation.py")


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


def inventory(root, include_tools=False):
    rows = []
    for name in PROJECT_ROOTS:
        for path in ordinary_tree(root / name):
            rows.append({"path": path.relative_to(root).as_posix(), "bytes": path.stat().st_size, "sha256": digest(path)})
    for name in PROJECT_FILES:
        path = root / name
        if path.is_file(): rows.append({"path": name, "bytes": path.stat().st_size, "sha256": digest(path)})
    if include_tools:
        for name in TOOL_FILES:
            path = root / name
            if path.is_file(): rows.append({"path": name, "bytes": path.stat().st_size, "sha256": digest(path)})
    rows.sort(key=lambda row: row["path"])
    return {"sha256": hashlib.sha256(json.dumps(rows, sort_keys=True, separators=(",", ":")).encode()).hexdigest(), "files": rows}


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


def prepare(source, project, output, unity, overlay=None):
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
    for name in TOOL_FILES:
        path = source / name
        if path.is_file():
            saved = output / "tool-sources" / name
            saved.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(path, saved)
    (output / "git-status.txt").write_bytes(git(source, "status", "--porcelain=v1", "--untracked-files=all"))
    (output / "dirty.patch").write_bytes(git(source, "diff", "--binary", "HEAD", "--", *PROJECT_ROOTS, *PROJECT_FILES, *TOOL_FILES))
    save(output / "identity.json", {"source": str(source), "project": str(project), "output": str(output),
         "unity": str(unity), "unitySha256": digest(unity), "projectVersion": (source / "ProjectSettings/ProjectVersion.txt").read_text(),
         "head": git(source, "rev-parse", "HEAD").decode().strip(), "startedUtc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
         "sourceSnapshotSha256": before["sha256"], "snapshotKind": "PhysicalCopiesNoJunctionsOrHardlinks",
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
    # Keep the tested implementation even when the reusable project is synchronized
    # for a later run. This includes untracked C# and the explicit red-test overlay.
    source_extensions = {".cs", ".asmdef", ".asmref", ".json", ".asset", ".unity", ".prefab", ".meta", ".shader", ".compute", ".hlsl", ".cginc", ".uxml", ".uss"}
    archive_path = output / "tested-code-and-config.zip"
    with zipfile.ZipFile(archive_path, "w", zipfile.ZIP_DEFLATED, compresslevel=1) as archive:
        for row in actual["files"]:
            relative = row["path"]
            if not relative.startswith("Assets/") or Path(relative).suffix.lower() in source_extensions:
                archive.write(project / relative, relative)
    save(output / "sync.json", {"copied": copied, "unchanged": unchanged, "archivedStaleFiles": moved,
         "projectSha256": actual["sha256"], "sourceOverrides": overrides, "complete": True,
         "testedSourceArchive": archive_path.name, "testedSourceArchiveSha256": digest(archive_path),
         "archiveScope": "Code, serialized assets, scene/prefab text, metadata, Packages and ProjectSettings; large binary media remain identified by full snapshot manifest"})
    return {"project": str(project), "sourceHash": before["sha256"], "projectHash": actual["sha256"], "copied": copied}


def finish(output):
    output = Path(output).resolve()
    identity = json.loads((output / "identity.json").read_text(encoding="utf-8"))
    source = inventory(Path(identity["source"]), include_tools=True)
    validation = inventory(Path(identity["project"]))
    save(output / "source-after.json", source)
    save(output / "validation-after.json", validation)
    before = json.loads((output / "source-before.json").read_text(encoding="utf-8"))
    frozen = json.loads((output / "validation-before.json").read_text(encoding="utf-8"))
    def changed(first, last):
        a = {row["path"]: row["sha256"] for row in first["files"]}
        b = {row["path"]: row["sha256"] for row in last["files"]}
        return [{"path": path, "before": a.get(path), "after": b.get(path)}
                for path in sorted(a.keys() | b.keys()) if a.get(path) != b.get(path)]
    result = {"sourceUnchanged": source == before, "validationInputsUnchanged": validation == frozen,
              "sourceChanges": changed(before, source), "validationInputChanges": changed(frozen, validation),
              "completedUtc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())}
    save(output / "integrity.json", result)
    files = [{"path": p.relative_to(output).as_posix(), "bytes": p.stat().st_size, "sha256": digest(p)}
             for p in sorted(output.rglob("*")) if p.is_file() and p.name != "artifact-manifest.json"]
    save(output / "artifact-manifest.json", {"identity": identity, "integrity": result, "files": files})
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
    final = subs.add_parser("finish"); final.add_argument("--output", required=True)
    audit = subs.add_parser("audit-fixtures")
    audit.add_argument("--provenance", required=True); audit.add_argument("--output", required=True)
    audit.add_argument("--build-manifest")
    args = parser.parse_args()
    if args.command == "prepare":
        # Validate the unresolved destination so an existing junction is never followed.
        ensure_physical_parents(Path(args.project).absolute())
        result = prepare(args.source, args.project, args.output, args.unity, args.overlay)
    elif args.command == "finish": result = finish(args.output)
    else: result = audit_fixtures(args.provenance, args.output, args.build_manifest)
    print(json.dumps(result, ensure_ascii=False))
    # A frozen independent snapshot remains valid while colleagues edit the workspace.
    # sourceUnchanged explicitly states whether the result still describes that workspace.
    if args.command == "finish" and not result["validationInputsUnchanged"]: return 2
    if args.command == "audit-fixtures" and not result["allProvenanceVerified"]: return 2
    return 0


if __name__ == "__main__": raise SystemExit(main())
