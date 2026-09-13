using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Random = UnityEngine.Random;

namespace MonsterSupergroup.NordicSample.Editor
{
    // Native rules: WorldManager.CO_UpdateChunk.MoveNext RVA 0x909f60;
    // IsNearProp 0x912c00. Finite, ordered baking replaces coroutine streaming.
    public static class NordicMidgardLayout
    {
        [Serializable] public sealed class Floor
        {
            public float min, max;
            public Color color;
            public string path;
            public int order;
        }
        [Serializable] public sealed class EnvironmentGroup
        {
            public int group;
            public float min, max, distance;
            public bool mirror;
            public string[] paths;
        }
        [Serializable] public sealed class Config
        {
            public string version, source, sourceSha256;
            public int seed, screenColumns, screenRows, referenceWidth, referenceHeight;
            public int chunkSize, chunkBaseSize, bufferChunks, floorSortPoint;
            public float noiseImpact, offset, offsetBase;
            public bool flipBaseX, flipBaseY, flipX, flipY, rotate;
            public Floor[] baseFloors, floors;
            public EnvironmentGroup[] groups;
            public Vector2 MapSize => new Vector2(screenColumns * 20 * Mathf.Tan(40 * Mathf.Deg2Rad) * referenceWidth / referenceHeight,
                screenRows * 20 * Mathf.Tan(40 * Mathf.Deg2Rad));
        }
        [Serializable] public sealed class Placement
        {
            public string id, kind, path;
            public int chunkX, chunkY, group, order;
            public Vector3 position, scale;
            public float rotation;
            public Color color;
        }
        [Serializable] public sealed class Layout
        {
            public string version, sourceSha256;
            public int seed, chunks;
            public Vector2 noiseOffset, mapSize, center;
            public Placement[] placements;
            public string[] absentProps;
        }

        public static float Height(Config c, Vector2 noise, float x, float y)
            => Mathf.Clamp(Mathf.PerlinNoise(x / c.noiseImpact + noise.x, y / c.noiseImpact + noise.y), 0, .999f);

        public static Layout Generate(Config c, Bounds bounds)
        {
            if (c.chunkSize != 10 || c.chunkBaseSize != 2 || c.offset != 1 || c.noiseImpact <= 0 || c.groups.Length != 9)
                throw new InvalidOperationException("Unsupported Midgard configuration; inspect native coordinate rules before extending.");
            Random.State saved = Random.state;
            try
            {
                Random.InitState(c.seed);
                Vector2 noise = new Vector2(Random.Range(-1000f, 1000f), Random.Range(-1000f, 1000f));
                float step = c.chunkSize * c.offset;
                int minX = Mathf.FloorToInt(bounds.min.x / step) - c.bufferChunks;
                int maxX = Mathf.FloorToInt(bounds.max.x / step) + c.bufferChunks;
                int minY = Mathf.FloorToInt(bounds.min.y / step) - c.bufferChunks;
                int maxY = Mathf.FloorToInt(bounds.max.y / step) + c.bufferChunks;
                var result = new List<Placement>();
                var placed = c.groups.ToDictionary(g => g.group, _ => new Dictionary<Vector2Int, List<Vector3>>());
                for (int cx = minX; cx <= maxX; cx++)
                for (int cy = minY; cy <= maxY; cy++)
                {
                    Vector3 origin = new Vector3(cx * step, cy * step, 0);
                    for (int x = (int)origin.x; x < origin.x + c.chunkBaseSize; x++)
                    for (int y = (int)origin.y; y < origin.y + c.chunkBaseSize; y++)
                    {
                        Floor f = c.baseFloors[c.baseFloors.Length > 1 ? Random.Range(0, c.baseFloors.Length) : 0];
                        AddFloor(f, true, x, y);
                    }
                    for (int x = (int)origin.x; x < origin.x + c.chunkSize; x++)
                    for (int y = (int)origin.y; y < origin.y + c.chunkSize; y++)
                    {
                        float h = Height(c, noise, x, y);
                        Floor f = Array.Find(c.floors, f => h >= f.min && h < f.max);
                        if (f != null) AddFloor(f, false, x, y);
                    }
                    for (int x = (int)origin.x; x < origin.x + c.chunkSize; x++)
                    for (int y = (int)origin.y; y < origin.y + c.chunkSize; y++)
                    {
                        float h = Height(c, noise, x, y);
                        foreach (EnvironmentGroup g in c.groups)
                        {
                            if (h < g.min || h >= g.max || g.paths.Length == 0) continue;
                            string path = g.paths[Random.Range(0, g.paths.Length)];
                            Vector3 candidate = new Vector3(x + Random.value * .5f, y + Random.value * .5f, 0);
                            bool near = false;
                            for (int nx = cx - 1; nx <= cx + 1 && !near; nx++)
                            for (int ny = cy - 1; ny <= cy + 1 && !near; ny++)
                                if (placed[g.group].TryGetValue(new Vector2Int(nx, ny), out var neighbors))
                                    near = neighbors.Any(p => (p - candidate).sqrMagnitude <= g.distance * g.distance);
                            if (near) continue;
                            // Original objects remain under origin-aligned pool parents.
                            Vector3 position = candidate * c.offset;
                            Vector3 scale = Vector3.one * Random.Range(1f, 1.25f);
                            if (g.mirror) scale.x *= Random.value > .5f ? 1 : -1;
                            var key = new Vector2Int(cx, cy);
                            if (!placed[g.group].TryGetValue(key, out var bucket)) placed[g.group][key] = bucket = new List<Vector3>();
                            bucket.Add(position);
                            result.Add(new Placement { id = "prop-" + result.Count, kind = "prop", path = path,
                                group = g.group, chunkX = cx, chunkY = cy, position = position, scale = scale, color = Color.white });
                        }
                    }

                    void AddFloor(Floor f, bool baseFloor, int x, int y)
                    {
                        Vector3 p = new Vector3(x, y + (x + origin.x) / 10000f, 0);
                        p = origin + (p - origin) * (baseFloor ? c.offsetBase : c.offset);
                        // This final write replaces the native earlier worldData.scale write.
                        Vector3 scale = new Vector3((baseFloor ? c.flipBaseX : c.flipX) && Random.value > .5f ? -1 : 1,
                            (baseFloor ? c.flipBaseY : c.flipY) && Random.value > .5f ? -1 : 1, 1);
                        result.Add(new Placement { id = (baseFloor ? "base-" : "floor-") + result.Count,
                            kind = baseFloor ? "base" : "floor", path = f.path, chunkX = cx, chunkY = cy,
                            position = p, scale = scale, color = f.color, order = f.order,
                            rotation = !baseFloor && c.rotate ? Random.Range(0, 360) : 0 });
                    }
                }
                var used = new HashSet<string>(result.Where(p => p.kind == "prop").Select(p => p.path));
                return new Layout { version = c.version, sourceSha256 = c.sourceSha256, seed = c.seed,
                    noiseOffset = noise, mapSize = c.MapSize, center = bounds.center,
                    chunks = (maxX - minX + 1) * (maxY - minY + 1), placements = result.ToArray(),
                    absentProps = c.groups.SelectMany(g => g.paths).Where(p => !used.Contains(p)).ToArray() };
            }
            finally { Random.state = saved; }
        }
    }
}
