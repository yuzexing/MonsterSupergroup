using System.Collections.Generic;
using AstralShift.HellMaiden.AI;
using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.Player.Attacks;
using Mirror;
using UnityEngine;
namespace MonsterSupergroup.NetworkCombat
{
    public static class GluttonyGeometry
    {
        public static bool Eligible(EnemyController e, NetworkEnemySimulationAgent a) =>
            e != null && a != null && a.ProductEnemyInitialized &&
            a.SimulationMode == EnemySimulationMode.NormalClient && !e.isElite &&
            !e.attackOnDeath && !(e.attackScript is EnemyAttackExplosion) && !e.IsImmune && e.hurtBox != null;
        public static void Candidates(List<Collider2D> hits, List<uint> result, Vector2 origin)
        {
            result.Clear();
            foreach (var hit in hits)
            {
                if (hit == null || !hit.TryGetComponent<EnemyHurtbox>(out var box)) continue;
                var e = box.GetComponentInParent<EnemyController>();
                var a = e != null ? e.GetComponent<NetworkEnemySimulationAgent>() : null;
                if (Eligible(e,a) && e.IsAlive && box.IsActive() && a.netId != 0 && !result.Contains(a.netId))
                    result.Add(a.netId);
            }
            result.Sort((a,b) => { int c = Distance(a,origin).CompareTo(Distance(b,origin)); return c != 0 ? c : a.CompareTo(b); });
        }
        private static float Distance(uint id, Vector2 origin) => NetworkClient.spawned.TryGetValue(id, out var obj) && obj != null
            ? ((Vector2)obj.transform.position - origin).sqrMagnitude : float.MaxValue;
        internal static bool ServerTarget(uint id, out Vector2 center, out Vector2 extents)
        {
            center = extents = default;
            if (!NetworkServer.spawned.TryGetValue(id, out var obj) || obj == null) return false;
            var e = obj.GetComponent<EnemyController>();
            var a = obj.GetComponent<NetworkEnemySimulationAgent>();
            if (!Eligible(e,a) || NetworkCombatWorld.Instance == null ||
                !NetworkCombatWorld.Instance.Gateway.Ledger.TryGetState(id, out var hp) || !hp.Alive || hp.AbsoluteInvulnerable) return false;
            var bounds = e.hurtBox.GetBounds(); center = bounds.center; extents = bounds.extents;
            var world = NetworkEnemySimulationWorld.Instance;
            if (a.Assignment.Host != EnemySimulationHost.ServerAuthoritative && a.Assignment.Host != EnemySimulationHost.ServerFallback &&
                world != null && world.Registry.TryGetLatestSnapshot(id, out var snapshot) && snapshot.AssignmentEpoch == a.Assignment.Epoch)
                center += snapshot.Position - (Vector2)a.transform.position;
            return Finite(center) && Finite(extents);
        }
        public static bool InRectangle(Vector2 center, Vector2 extents, Vector2 origin, Vector2 dir, float length, float width, float tolerance)
        {
            Vector2 offset = center-origin, side = new Vector2(-dir.y,dir.x);
            float along = Vector2.Dot(offset,dir), across = Mathf.Abs(Vector2.Dot(offset,side));
            float ex = Mathf.Abs(dir.x)*extents.x + Mathf.Abs(dir.y)*extents.y;
            float ey = Mathf.Abs(side.x)*extents.x + Mathf.Abs(side.y)*extents.y;
            return along+ex >= -tolerance && along-ex <= length+tolerance && across <= width*.5f+ey+tolerance;
        }
        public static bool InCircle(Vector2 center, Vector2 extents, Vector2 origin, float radius)
        {
            Vector2 d = center-origin;
            d = new Vector2(Mathf.Max(0,Mathf.Abs(d.x)-extents.x), Mathf.Max(0,Mathf.Abs(d.y)-extents.y));
            return d.sqrMagnitude <= radius*radius;
        }
        public static bool Finite(Vector2 v) => float.IsFinite(v.x) && float.IsFinite(v.y);
    }
}
