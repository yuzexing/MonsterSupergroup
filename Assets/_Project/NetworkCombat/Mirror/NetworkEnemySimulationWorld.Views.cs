using AstralShift.HellMaiden.AI.Enemy;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    public sealed partial class NetworkEnemySimulationWorld
    {
        public bool TryGetPlayerView(uint player, out Bounds view)
        {
            view = default;
            return isServer && TryGetEligiblePlayer(player, out var endpoint) && endpoint.TryGetServerView(out view);
        }

        public bool IsInPlayerView(uint player, NetworkEnemySimulationAgent enemy) =>
            enemy != null && TryGetPlayerView(player, out var view) && PlayerViewReport.Intersects(view, ServerEnemyBodyBounds(enemy));

        public Bounds ServerEnemyBodyBounds(NetworkEnemySimulationAgent enemy)
        {
            Vector3 position = enemy.transform.position;
            var controller = enemy.GetComponent<EnemyController>();
            Bounds bounds = controller != null && controller.spriteRenderer != null
                ? controller.spriteRenderer.bounds : new Bounds(position, Vector3.zero);
            if (!enemy.Authority.RunsNavigation && Registry.TryGetLatestSnapshot(enemy.netId, out var snapshot) &&
                snapshot.AssignmentEpoch == enemy.Assignment.Epoch)
                bounds.center += (Vector3)snapshot.Position - new Vector3(position.x, position.y, 0);
            return bounds;
        }
    }
}
