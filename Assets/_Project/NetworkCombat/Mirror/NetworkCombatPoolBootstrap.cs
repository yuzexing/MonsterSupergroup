using AstralShift.HellMaiden.Combat;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    /// <summary>Initializes the legacy pool service in the standalone network sandbox.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PoolManager))]
    public sealed class NetworkCombatPoolBootstrap : MonoBehaviour
    {
        [SerializeField] private PoolManager poolManager;

        private void Awake()
        {
            if (poolManager == null)
            {
                poolManager = GetComponent<PoolManager>();
            }
            poolManager.Init();
        }
    }
}
