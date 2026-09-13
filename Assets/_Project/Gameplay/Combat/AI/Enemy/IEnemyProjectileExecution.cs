using UnityEngine;

namespace AstralShift.HellMaiden.AI.Enemy
{
    // Gameplay does not depend on Mirror. A network adapter replaces only spawning.
    public interface IEnemyProjectileExecution
    {
        void ShowCharge();
        void Launch(Vector2 direction);
        void CancelCharge();
    }
}
