"""Read archived NUnit results; never rewrite the original Phase 5 evidence."""
import argparse
import hashlib
import json
import re
from pathlib import Path
import xml.etree.ElementTree as ET


def result(path):
    root = ET.parse(path).getroot()
    return {
        "path": path.as_posix(),
        "sha256": hashlib.sha256(path.read_bytes()).hexdigest(),
        **{key: root.get(key) for key in ("result", "total", "passed", "failed", "skipped", "duration", "start-time", "end-time")},
        "failures": [{"name": case.get("fullname"), "message": case.findtext("failure/message"),
                      "stack": case.findtext("failure/stack-trace")}
                     for case in root.iter("test-case") if case.get("result") == "Failed"],
    }


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--logs", type=Path, default=Path("Logs/RegressionClosure20260916"))
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--build", type=Path)
    args = parser.parse_args()
    report = {"runs": [result(path) for path in sorted(args.logs.glob("*.xml"))]}
    report["processes"] = [{"path": str(path), "result": path.read_text().strip()}
                           for path in sorted(args.logs.rglob("*-result"))]
    report["renderedRuns"] = []
    for folder in sorted(path for path in args.logs.iterdir() if path.is_dir()):
        if not list(folder.glob("*-result")):
            continue
        logs = []
        for path in sorted(folder.glob("*.log")):
            contents = path.read_text(encoding="utf-8", errors="replace")
            logs.append({"path": path.as_posix(), "sha256": hashlib.sha256(path.read_bytes()).hexdigest(),
                         "observations": re.findall(r"^\[RegressionClosure\].*$", contents, re.MULTILINE),
                         "exceptions": re.findall(r"^[A-Za-z.]*Exception:.*$", contents, re.MULTILINE)})
        report["renderedRuns"].append({"folder": folder.as_posix(), "logs": logs,
            "screenshots": [{"path": path.as_posix(), "sha256": hashlib.sha256(path.read_bytes()).hexdigest()}
                            for path in sorted(folder.glob("*.png"))],
            "buildHashes": json.loads((folder / "build-hashes.json").read_text(encoding="utf-8-sig"))
                           if (folder / "build-hashes.json").exists() else None})
    if args.build:
        data = args.build.with_name(args.build.stem + "_Data")
        binaries = [args.build, data / "Managed/MonsterSupergroup.Gameplay.Tests.PlayMode.dll",
                    data / "Managed/MonsterSupergroup.NetworkCombat.dll"]
        report["build"] = [{"path": str(path), "sha256": hashlib.sha256(path.read_bytes()).hexdigest()}
                           for path in binaries if path.exists()]
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print(args.output)


if __name__ == "__main__":
    main()
