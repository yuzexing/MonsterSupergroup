using UnityEngine;

namespace MonsterSupergroup.Gameplay.Combat
{
    /// <summary>Prevents client services from awakening in the server scene variant.</summary>
    [DefaultExecutionOrder(-32700)]
    public sealed class ClientOnlySceneObjects : MonoBehaviour
    {
        [SerializeField] private GameObject[] clientObjects;

        private void Awake()
        {
            if (!GameplayRuntimeEnvironment.IsDedicatedServer || clientObjects == null) return;
            foreach (GameObject item in clientObjects)
                if (item != null) item.SetActive(false);
        }
    }
}
