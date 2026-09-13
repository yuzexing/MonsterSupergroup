using System;
using Pathfinding;
using UnityEngine;

namespace MonsterSupergroup.Gameplay.Combat
{
    /// <summary>The authored map shared by simulation owners and the server. Never generates scenery.</summary>
    [DefaultExecutionOrder(-9000)]
    public sealed class GameplayMapContext : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer ground;
        [SerializeField] private AstarPath navigation;
        public const float Skin = .01f;
        private readonly Collider2D[] overlaps = new Collider2D[32];
        public static GameplayMapContext Active { get; private set; }
        public SpriteRenderer Ground => ground;
        public AstarPath Navigation => navigation;
        public Bounds Bounds => ground.bounds;
        public bool IsReady => ground != null && navigation != null && navigation.data.gridGraph != null &&
            navigation.data.gridGraph.CountNodes() > 0;
        public static GameplayMapContext For(GameObject obj) => Active != null && obj.scene == Active.gameObject.scene ? Active : null;

        public void Configure(SpriteRenderer value, AstarPath graph) { ground = value; navigation = graph; }
        private void Awake()
        {
            Active = this;
            if (!GameplayRuntimeEnvironment.IsDedicatedServer) return;
            foreach (var renderer in GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
            foreach (var particle in GetComponentsInChildren<ParticleSystem>(true))
            {
                particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                // Keep the authored hierarchy and all collision; a server needs no particle simulation.
                var particleRenderer = particle.GetComponent<ParticleSystemRenderer>();
                if (particleRenderer != null) Destroy(particleRenderer);
                Destroy(particle);
            }
        }
        private void OnDestroy() { if (Active == this) Active = null; }

        public static float Radius(CircleCollider2D circle) => circle.radius * Mathf.Max(Mathf.Abs(circle.transform.lossyScale.x), Mathf.Abs(circle.transform.lossyScale.y));
        public static Vector2 Offset(CircleCollider2D circle, Transform root) => (Vector2)(circle.transform.TransformPoint(circle.offset) - root.position);

        public Vector2 Clamp(Vector2 root, float radius, Vector2 offset = default)
        {
            Bounds b = Bounds; float margin = radius + Skin;
            Vector2 center = root + offset;
            center.x = Mathf.Clamp(center.x, b.min.x + margin, b.max.x - margin);
            center.y = Mathf.Clamp(center.y, b.min.y + margin, b.max.y - margin);
            return center - offset;
        }

        public bool IsFree(Vector2 root, float radius, Vector2 offset = default, bool players = false, Collider2D ignore = null)
        {
            if ((Clamp(root, radius, offset) - root).sqrMagnitude > .000001f) return false;
            var filter = new ContactFilter2D { useTriggers = false };
            filter.SetLayerMask(LayerMask.GetMask("Obstacles", "Edges") | (players ? LayerMask.GetMask("Player") : 0));
            int count = Physics2D.OverlapCircle(root + offset, radius + Skin * .5f, filter, overlaps);
            for (int i = 0; i < count; i++) if (overlaps[i] != ignore) return false;
            return true;
        }

        public Vector2 FindSpawn(Vector2 requested, float radius, Vector2 offset = default, bool players = false, Collider2D ignore = null)
        {
            Physics2D.SyncTransforms();
            Vector2 origin = Clamp(requested, radius, offset);
            if (IsFree(origin, radius, offset, players, ignore)) return origin;
            // Keep scanning until every remaining ring is farther than the best legal candidate.
            int maximum = Mathf.CeilToInt(Mathf.Max(Bounds.size.x, Bounds.size.y) / .25f);
            float best = float.PositiveInfinity; Vector2 chosen = default;
            void Consider(int x, int y)
            {
                Vector2 candidate = origin + new Vector2(x, y) * .25f;
                float distance = (candidate - origin).sqrMagnitude;
                if (distance < best && IsFree(candidate, radius, offset, players, ignore)) { chosen = candidate; best = distance; }
            }
            for (int ring = 1; ring <= maximum; ring++)
            {
                if (ring * ring * .0625f > best) return chosen;
                for (int x = -ring; x <= ring; x++) { Consider(x, -ring); Consider(x, ring); }
                for (int y = -ring + 1; y < ring; y++) { Consider(-ring, y); Consider(ring, y); }
            }
            if (!float.IsPositiveInfinity(best)) return chosen;
            throw new InvalidOperationException("Nordic Gameplay has no legal spawn for the requested footprint.");
        }

        public bool IsReachable(Vector2 from, Vector2 target)
        {
            if (!IsReady) return false;
            var a = navigation.GetNearest((Vector3)from, NNConstraint.Walkable);
            var b = navigation.GetNearest((Vector3)target, NNConstraint.Walkable);
            return a.node != null && b.node != null && a.node.Walkable && b.node.Walkable &&
                Vector2.Distance(from, (Vector3)a.position) <= .5f && PathUtilities.IsPathPossible(a.node, b.node);
        }
        public Vector2 Place(Rigidbody2D body, CircleCollider2D foot, Vector2 requested, bool avoidPlayers = true)
        {
            Vector2 position = FindSpawn(requested, Radius(foot), Offset(foot, body.transform), avoidPlayers, foot);
            body.linearVelocity = Vector2.zero;
            body.position = position;
            body.transform.position = new Vector3(position.x, position.y, 0);
            Physics2D.SyncTransforms();
            return position;
        }

        public float DashDistance(Vector2 start, Vector2 direction, float distance, CircleCollider2D foot)
        {
            float radius = Radius(foot); Vector2 offset = Offset(foot, foot.attachedRigidbody.transform);
            Bounds b = Bounds; Vector2 center = start + offset; float margin = radius + Skin;
            if (direction.x > 0) distance = Mathf.Min(distance, (b.max.x - margin - center.x) / direction.x);
            if (direction.x < 0) distance = Mathf.Min(distance, (b.min.x + margin - center.x) / direction.x);
            if (direction.y > 0) distance = Mathf.Min(distance, (b.max.y - margin - center.y) / direction.y);
            if (direction.y < 0) distance = Mathf.Min(distance, (b.min.y + margin - center.y) / direction.y);
            distance = Mathf.Max(0, distance);
            if (IsFree(start + direction * distance, radius, offset)) return distance;
            float blocked = distance;
            for (float d = Mathf.Max(0, distance - .025f); ; d = Mathf.Max(0, d - .025f))
            {
                if (IsFree(start + direction * d, radius, offset))
                {
                    float clear = d;
                    for (int i = 0; i < 10; i++)
                    {
                        float mid = (clear + blocked) * .5f;
                        if (IsFree(start + direction * mid, radius, offset)) clear = mid; else blocked = mid;
                    }
                    return clear;
                }
                if (d <= 0) return 0;
                blocked = d;
            }
        }

        public void ConstrainMotion(Rigidbody2D body, CircleCollider2D foot)
        {
            if (body == null || foot == null || body.bodyType != RigidbodyType2D.Dynamic) return;
            float radius = Radius(foot); Vector2 offset = Offset(foot, body.transform);
            Vector2 clamped = Clamp(body.position, radius, offset);
            if ((clamped - body.position).sqrMagnitude > .000001f) body.position = clamped;
            Vector2 next = Clamp(clamped + body.linearVelocity * Time.fixedDeltaTime, radius, offset);
            body.linearVelocity = (next - clamped) / Time.fixedDeltaTime;
        }
    }
}
