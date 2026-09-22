"""Bounded render observation summary; measurements do not replace visual acceptance."""
import argparse
import collections
import json
import math
from pathlib import Path


def xy(value):
    return value["x"], value["y"]


def analyze(path):
    groups = collections.defaultdict(list)
    for line in path.read_text(encoding="utf-8-sig").splitlines():
        row = json.loads(line)
        if row.get("kind") == "render":
            groups[(row["targetFps"], row["legacy"], row["moving"], row["role"])].append(row)
    result = []
    for (fps, legacy, moving, role), rows in groups.items():
        same_physics = interpolated = moving_frames = 0
        for previous, row in zip(rows, rows[1:]):
            if row["id"] != previous["id"]:
                continue
            if role == "Replica":
                moving_frames += math.dist(xy(row["rendered"]), xy(previous["rendered"])) > 1e-5
                continue  # Snapshot replicas intentionally keep their existing presentation path.
            if math.hypot(*xy(row["velocity"])) < .1:
                continue
            moving_frames += 1
            if math.dist(xy(row["physics"]), xy(previous["physics"])) < 1e-6:
                same_physics += 1
                if math.dist(xy(row["rendered"]), xy(previous["rendered"])) > 1e-5:
                    interpolated += 1
        result.append(dict(fps=fps, legacy=legacy, playerMoving=moving, role=role, frames=len(rows),
                           movingFrames=moving_frames, framesBetweenPhysicsSteps=same_physics,
                           renderMovedBetweenPhysicsSteps=interpolated,
                           interpolationModes=dict(collections.Counter(r["interpolation"] for r in rows)),
                           renderSizes=sorted(set((r["width"], r["height"]) for r in rows)),
                           observedFps=round(len(rows) / max(.001, sum(r["delta"] for r in rows)), 2)))
    return dict(source=str(path), cases=result)


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("paths", nargs="+", type=Path)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps([analyze(p) for p in args.paths], indent=2), encoding="utf-8")
