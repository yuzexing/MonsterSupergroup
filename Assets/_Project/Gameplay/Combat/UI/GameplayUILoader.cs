using Cysharp.Threading.Tasks;
using MonsterSupergroup.Gameplay.Combat;
using UnityEngine;
using UnityEngine.Serialization;

namespace MonsterSupergroup.Gameplay.UI
{
    [DisallowMultipleComponent]
    public sealed class GameplayUILoader : MonoBehaviour
    {
        [FormerlySerializedAs("UIPrefab")]
        [SerializeField] private GameplayUIRoot uiPrefab;

        public GameplayUIRoot Instance { get; private set; }

        private void Start()
        {
            if (GameplayRuntimeEnvironment.IsDedicatedServer || Instance != null) return;
            if (uiPrefab == null)
            {
                Debug.LogError("GameplayUILoader requires a GameplayUIRoot prefab.", this);
                return;
            }

            // Parenting gives the instance this loader's scene even when Mirror
            // has not yet made the newly loaded additive scene active.
            Instance = Instantiate(uiPrefab, transform, false);
            Instance.name = "GameplayUIRoot";
            Instance.Initialize().Forget();
        }
    }
}
